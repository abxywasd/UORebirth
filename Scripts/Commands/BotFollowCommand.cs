using System;
using System.Collections.Generic;
using Server;
using Server.Commands;
using Server.Mobiles;

namespace Server.Scripts.Commands
{
    public static class BotFollowCommand
    {
        private static readonly Dictionary<Mobile, BotFollowSession> s_ActiveFollows
            = new Dictionary<Mobile, BotFollowSession>();

        public static void Initialize()
        {
            CommandSystem.Register( "botfollow",  AccessLevel.GameMaster, new CommandEventHandler( BotFollow_OnCommand ) );
            CommandSystem.Register( "botinfo",    AccessLevel.GameMaster, new CommandEventHandler( BotInfo_OnCommand ) );
            CommandSystem.Register( "botpause",   AccessLevel.GameMaster, new CommandEventHandler( BotPause_OnCommand ) );
            CommandSystem.Register( "botresume",  AccessLevel.GameMaster, new CommandEventHandler( BotResume_OnCommand ) );
        }

        // ── [botfollow ─────────────────────────────────────────────────────────────
        [Usage( "botfollow <botName>" )]
        [Description( "Toggle: shadow a PlayerBot as it navigates. Second call stops the follow." )]
        private static void BotFollow_OnCommand( CommandEventArgs e )
        {
            Mobile from = e.Mobile;

            // No-arg: stop current follow
            if ( e.Length < 1 )
            {
                if ( s_ActiveFollows.ContainsKey( from ) )
                    DetachFollow( from, "Follow stopped." );
                else
                    from.SendMessage( "Usage: [botfollow <botName>" );
                return;
            }

            string name = e.GetString( 0 );

            // Already following someone: toggle off if same bot, swap if different
            if ( s_ActiveFollows.ContainsKey( from ) )
            {
                BotFollowSession existing = s_ActiveFollows[from];
                if ( existing.Target != null &&
                     string.Equals( existing.Target.Name, name, StringComparison.OrdinalIgnoreCase ) )
                {
                    DetachFollow( from, "Follow stopped." );
                    return;
                }
                DetachFollow( from, null ); // stop previous silently before starting new
            }

            PlayerBot bot = FindBot( name );
            if ( bot == null )
            {
                from.SendMessage( 0x22, "No PlayerBot named '{0}' found.", name );
                return;
            }

            var session = new BotFollowSession
            {
                Target          = bot,
                LastPosition    = bot.Location,
                LastMoveTime    = DateTime.Now,
                StatusTickCount = 0,
                StuckWarned     = false
            };

            session.FollowTimer = new InternalTimer( from, session );
            session.FollowTimer.Start();
            s_ActiveFollows[from] = session;

            if ( !from.Hidden )
                from.Hidden = true;

            from.SendMessage( 0x55, "Now following {0}. Type [botfollow again to stop.", bot.Name );
        }

        // ── [botinfo ───────────────────────────────────────────────────────────────
        [Usage( "botinfo <botName>" )]
        [Description( "Prints a full state snapshot for a named PlayerBot." )]
        private static void BotInfo_OnCommand( CommandEventArgs e )
        {
            if ( e.Length < 1 )
            {
                e.Mobile.SendMessage( "Usage: [botinfo <botName>" );
                return;
            }

            Mobile    from = e.Mobile;
            PlayerBot bot  = FindBot( e.GetString( 0 ) );

            if ( bot == null )
            {
                from.SendMessage( 0x22, "No PlayerBot named '{0}' found.", e.GetString( 0 ) );
                return;
            }

            ActivityState state = bot.ActivityState;

            from.SendMessage( 0x55, "--- [{0}] ---", bot.Name );
            from.SendMessage( 0x55, "  Location : ({0},{1},{2}) {3}", bot.X, bot.Y, bot.Z, bot.Map );
            from.SendMessage( 0x55, "  Activity : {0}", state.Current );
            from.SendMessage( 0x55, "  Destination : {0}",
                state.FinalDestination != null ? state.FinalDestination.Name : "none" );

            if ( state.WaypointHops != null && state.WaypointHops.Count > 0 )
            {
                BotWaypoint[] hops     = state.WaypointHops.ToArray();
                var           hopNames = new string[hops.Length];
                for ( int i = 0; i < hops.Length; i++ )
                    hopNames[i] = hops[i].Name;

                from.SendMessage( 0x55, "  Hop queue ({0}) : {1}", hops.Length,
                    string.Join( " -> ", hopNames ) );

                BotWaypoint next = hops[0];
                double dx   = next.Location.X - bot.X;
                double dy   = next.Location.Y - bot.Y;
                double dist = Math.Sqrt( dx * dx + dy * dy );
                from.SendMessage( 0x55, "  Next hop : {0} at ({1},{2}) — {3:F0} tiles",
                    next.Name, next.Location.X, next.Location.Y, dist );
            }
            else
            {
                from.SendMessage( 0x55, "  Hop queue : none (direct travel or idle)" );
            }

            from.SendMessage( 0x55, "  Persona : {0} ({1})", bot.PlayerBotProfile, bot.PlayerBotExperience );
            from.SendMessage( 0x55, "  Target  : {0}", bot.Combatant != null ? bot.Combatant.Name : "none" );
            from.SendMessage( 0x55, "  Paused  : {0}", bot.IsPaused );
        }

        // ── [botpause ──────────────────────────────────────────────────────────────
        [Usage( "botpause <botName>" )]
        [Description( "Freezes a PlayerBot in place by suspending its AI tick." )]
        private static void BotPause_OnCommand( CommandEventArgs e )
        {
            if ( e.Length < 1 )
            {
                e.Mobile.SendMessage( "Usage: [botpause <botName>" );
                return;
            }

            Mobile    from = e.Mobile;
            string    name = e.GetString( 0 );
            PlayerBot bot  = FindBot( name );

            if ( bot == null )
            {
                from.SendMessage( 0x22, "No PlayerBot named '{0}' found.", name );
                return;
            }

            if ( bot.IsPaused )
            {
                from.SendMessage( 0x55, "{0} is already paused.", bot.Name );
                return;
            }

            bot.IsPaused = true;
            from.SendMessage( 0x55, "{0} paused. Use [botresume {0} to continue.", bot.Name );
        }

        // ── [botresume ─────────────────────────────────────────────────────────────
        [Usage( "botresume <botName>" )]
        [Description( "Resumes a paused PlayerBot." )]
        private static void BotResume_OnCommand( CommandEventArgs e )
        {
            if ( e.Length < 1 )
            {
                e.Mobile.SendMessage( "Usage: [botresume <botName>" );
                return;
            }

            Mobile    from = e.Mobile;
            string    name = e.GetString( 0 );
            PlayerBot bot  = FindBot( name );

            if ( bot == null )
            {
                from.SendMessage( 0x22, "No PlayerBot named '{0}' found.", name );
                return;
            }

            if ( !bot.IsPaused )
            {
                from.SendMessage( 0x55, "{0} is not paused.", bot.Name );
                return;
            }

            bot.IsPaused = false;
            from.SendMessage( 0x55, "{0} resumed.", bot.Name );
        }

        // ── Helpers ────────────────────────────────────────────────────────────────
        private static PlayerBot FindBot( string name )
        {
            foreach ( Mobile m in World.Mobiles.Values )
            {
                PlayerBot pb = m as PlayerBot;
                if ( pb != null && !pb.Deleted &&
                     string.Equals( pb.Name, name, StringComparison.OrdinalIgnoreCase ) )
                    return pb;
            }
            return null;
        }

        private static void DetachFollow( Mobile gm, string message )
        {
            BotFollowSession session;
            if ( !s_ActiveFollows.TryGetValue( gm, out session ) )
                return;

            session.FollowTimer.Stop();
            s_ActiveFollows.Remove( gm );

            if ( message != null )
                gm.SendMessage( 0x55, message );
        }

        // ── Session ────────────────────────────────────────────────────────────────
        private class BotFollowSession
        {
            public PlayerBot     Target;
            public InternalTimer FollowTimer;
            public Point3D       LastPosition;
            public DateTime      LastMoveTime;
            public int           StatusTickCount;
            public bool          StuckWarned;
        }

        // ── 500ms follow timer ─────────────────────────────────────────────────────
        private class InternalTimer : Timer
        {
            private readonly Mobile           m_GM;
            private readonly BotFollowSession m_Session;

            public InternalTimer( Mobile gm, BotFollowSession session )
                : base( TimeSpan.FromMilliseconds( 500 ), TimeSpan.FromMilliseconds( 500 ) )
            {
                Priority  = TimerPriority.TwoFiftyMS;
                m_GM      = gm;
                m_Session = session;
            }

            protected override void OnTick()
            {
                PlayerBot bot = m_Session.Target;

                // GM disconnected
                if ( m_GM.Deleted || m_GM.NetState == null )
                {
                    s_ActiveFollows.Remove( m_GM );
                    Stop();
                    return;
                }

                // Bot gone
                if ( bot == null || bot.Deleted )
                {
                    m_GM.SendMessage( 0x22, "Bot was deleted. Follow ended." );
                    DetachFollow( m_GM, null );
                    return;
                }

                // Bot died
                if ( !bot.Alive )
                {
                    m_GM.SendMessage( 0x22, "[{0}] died. Follow ended.", bot.Name );
                    DetachFollow( m_GM, null );
                    return;
                }

                // Teleport GM 3 tiles behind the bot; fall back to bot's location if the
                // offset tile is impassable (inside a wall, mountain, etc.)
                Point3D followPos = GetFollowPosition( bot );
                if ( !bot.Map.CanFit( followPos.X, followPos.Y, followPos.Z, 16, false, false, false ) )
                    followPos = bot.Location;
                m_GM.MoveToWorld( followPos, bot.Map );

                ActivityState state = bot.ActivityState;

                // Stuck detection — only meaningful during active travel
                if ( state.Current == BotActivity.Traveling )
                {
                    if ( bot.Location != m_Session.LastPosition )
                    {
                        m_Session.LastPosition = bot.Location;
                        m_Session.LastMoveTime = DateTime.Now;
                        m_Session.StuckWarned  = false;
                    }
                    else
                    {
                        double stuckSecs = ( DateTime.Now - m_Session.LastMoveTime ).TotalSeconds;

                        if ( stuckSecs >= 30.0 )
                        {
                            m_GM.SendMessage( 0x22,
                                "[{0}] stuck >30s — follow ended. Consider adding a node here.",
                                bot.Name );
                            DetachFollow( m_GM, null );
                            return;
                        }

                        if ( stuckSecs >= 8.0 && !m_Session.StuckWarned )
                        {
                            m_Session.StuckWarned = true;
                            string nextHop = state.WaypointHops != null && state.WaypointHops.Count > 0
                                ? state.WaypointHops.Peek().Name
                                : ( state.FinalDestination != null ? state.FinalDestination.Name : "?" );
                            m_GM.SendMessage( 0x22,
                                "[{0}] STUCK — no movement for {1:F0}s | trying to reach: {2}",
                                bot.Name, stuckSecs, nextHop );
                        }
                    }
                }
                else
                {
                    // Not traveling — reset stuck tracking so it starts fresh if travel resumes
                    m_Session.LastPosition = bot.Location;
                    m_Session.LastMoveTime = DateTime.Now;
                    m_Session.StuckWarned  = false;
                }

                // Status message every 4 ticks (2 seconds)
                m_Session.StatusTickCount++;
                if ( m_Session.StatusTickCount >= 4 )
                {
                    m_Session.StatusTickCount = 0;
                    SendStatusMessage( m_GM, bot, state );
                }
            }

            private static Point3D GetFollowPosition( PlayerBot bot )
            {
                int dx = 0, dy = 0;
                switch ( bot.Direction & Direction.Mask )
                {
                    case Direction.North: dy =  3;           break; // facing N → offset S
                    case Direction.Right: dx = -2; dy =  2;  break; // NE → SW
                    case Direction.East:  dx = -3;           break; // E  → W
                    case Direction.Down:  dx = -2; dy = -2;  break; // SE → NW
                    case Direction.South: dy = -3;           break; // S  → N
                    case Direction.Left:  dx =  2; dy = -2;  break; // SW → NE
                    case Direction.West:  dx =  3;           break; // W  → E
                    case Direction.Up:    dx =  2; dy =  2;  break; // NW → SE
                }
                return new Point3D( bot.X + dx, bot.Y + dy, bot.Z );
            }

            private static void SendStatusMessage( Mobile gm, PlayerBot bot, ActivityState state )
            {
                string msg;

                switch ( state.Current )
                {
                    case BotActivity.Traveling:
                    {
                        string dest = state.FinalDestination != null
                            ? state.FinalDestination.Name : "?";

                        if ( state.WaypointHops != null && state.WaypointHops.Count > 0 )
                        {
                            BotWaypoint next = state.WaypointHops.Peek();
                            double dx   = next.Location.X - bot.X;
                            double dy   = next.Location.Y - bot.Y;
                            double dist = Math.Sqrt( dx * dx + dy * dy );
                            msg = string.Format( "[{0}] Travel -> {1} | Next: {2} | {3:F0} tiles",
                                bot.Name, dest, next.Name, dist );
                        }
                        else
                        {
                            msg = string.Format( "[{0}] Travel -> {1} | direct",
                                bot.Name, dest );
                        }
                        break;
                    }
                    case BotActivity.Combat:
                    {
                        string target = bot.Combatant != null ? bot.Combatant.Name : "none";
                        msg = string.Format( "[{0}] Combat — target: {1}", bot.Name, target );
                        break;
                    }
                    case BotActivity.Hunting:
                        msg = string.Format( "[{0}] Hunting", bot.Name );
                        break;
                    case BotActivity.Crafting:
                        msg = string.Format( "[{0}] Crafting", bot.Name );
                        break;
                    default:
                    {
                        string dest = state.FinalDestination != null
                            ? state.FinalDestination.Name : "none";
                        msg = string.Format( "[{0}] {1} | dest: {2}",
                            bot.Name, state.Current, dest );
                        break;
                    }
                }

                gm.SendMessage( 0x55, msg );
            }
        }
    }
}
