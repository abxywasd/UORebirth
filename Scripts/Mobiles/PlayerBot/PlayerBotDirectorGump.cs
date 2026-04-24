using System;
using Server;
using Server.Gumps;
using Server.Network;

namespace Server.Mobiles
{
    public class PlayerBotDirectorGump : Gump
    {
        private Mobile            m_From;
        private PlayerBotDirector m_Director;

        public PlayerBotDirectorGump( Mobile from, PlayerBotDirector dir )
            : base( 50, 50 )
        {
            m_From     = from;
            m_Director = dir;

            AddPage( 0 );

            // Background
            AddBackground( 0, 0, 420, 320, 9200 );
            AddLabel( 15, 12, 0x384, "PlayerBot Director" );
            AddLabel( 15, 32, 0,     "Runtime population controller" );

            // Separator line (visual)
            AddImageTiled( 15, 55, 390, 2, 9304 );

            // Enabled toggle
            AddLabel( 15,  70, 0, "Director enabled:" );
            AddLabel( 160, 70, dir.Enabled ? 0x40 : 0x26, dir.Enabled ? "YES" : "NO" );
            AddButton( 260, 68, 4005, 4007, 1, GumpButtonType.Reply, 0 );
            AddLabel( 295, 70, 0, "Toggle" );

            // Target count
            AddLabel( 15,  100, 0, "Target bot count:" );
            AddLabel( 160, 100, 0x40, dir.TargetBotCount.ToString() );
            AddButton( 260, 98,  4014, 4016, 2, GumpButtonType.Reply, 0 );  // -10
            AddButton( 290, 98,  4005, 4007, 3, GumpButtonType.Reply, 0 );  // +10
            AddLabel( 320, 100, 0, "-10 / +10" );

            // Live count (read-only display)
            AddLabel( 15,  130, 0, "Live bots now:" );
            AddLabel( 160, 130, 0x40, dir.GetLiveBots().Count.ToString() );

            // Separator
            AddImageTiled( 15, 155, 390, 2, 9304 );

            // Spawn at GM location
            AddButton( 15, 165, 4005, 4007, 4, GumpButtonType.Reply, 0 );
            AddLabel(  55, 165, 0, "Spawn one bot at your location" );

            // Hire nearby (default 10)
            AddButton( 15, 195, 4005, 4007, 5, GumpButtonType.Reply, 0 );
            AddLabel(  55, 195, 0, "Hire 10 nearest uncontrolled bots" );

            // Dismiss controlled
            AddButton( 15, 225, 4005, 4007, 6, GumpButtonType.Reply, 0 );
            AddLabel(  55, 225, 0, "Dismiss all bots you control" );

            // Separator
            AddImageTiled( 15, 255, 390, 2, 9304 );

            // Delete all bots (destructive — red label)
            AddButton( 15, 265, 4017, 4019, 7, GumpButtonType.Reply, 0 );
            AddLabel(  55, 265, 0x26, "Delete ALL live bots (irreversible)" );
        }

        public override void OnResponse( NetState sender, RelayInfo info )
        {
            if ( m_Director == null || m_Director.Deleted )
                return;

            switch ( info.ButtonID )
            {
                case 1: // Toggle enabled
                    m_Director.Enabled = !m_Director.Enabled;
                    break;

                case 2: // -10 target count
                    m_Director.TargetBotCount -= 10;
                    break;

                case 3: // +10 target count
                    m_Director.TargetBotCount += 10;
                    break;

                case 4: // Spawn at GM location
                    m_Director.SpawnOneBot( m_From.Location );
                    m_From.SendMessage( "Spawned one bot at your location." );
                    break;

                case 5: // Hire nearby bots
                {
                    int hired = 0;
                    IPooledEnumerable eable = m_From.Map.GetMobilesInRange( m_From.Location, 20 );
                    foreach ( Mobile m in eable )
                    {
                        if ( hired >= 10 ) break;
                        PlayerBot bot = m as PlayerBot;
                        if ( bot == null || bot.Controled ) continue;
                        if ( bot.AddHire( m_From ) )
                            hired++;
                    }
                    eable.Free();
                    m_From.SendMessage( "Hired {0} bots.", hired );
                    break;
                }

                case 6: // Dismiss controlled bots
                {
                    int dismissed = 0;
                    System.Collections.Generic.List<PlayerBot> list = new System.Collections.Generic.List<PlayerBot>();
                    foreach ( Mobile m in m_From.GetMobilesInRange( 30 ) )
                    {
                        PlayerBot bot = m as PlayerBot;
                        if ( bot != null && bot.Controled && bot.ControlMaster == m_From )
                            list.Add( bot );
                    }
                    foreach ( PlayerBot bot in list )
                    {
                        bot.SetControlMaster( null );
                        bot.ActivityState.SetActivity( BotActivity.Wandering );
                        dismissed++;
                    }
                    m_From.SendMessage( "Dismissed {0} bots.", dismissed );
                    break;
                }

                case 7: // Delete all
                {
                    System.Collections.Generic.List<PlayerBot> all = m_Director.GetLiveBots();
                    foreach ( PlayerBot b in all )
                        b.Delete();
                    m_From.SendMessage( "Deleted {0} bots.", all.Count );
                    break;
                }
            }

            // ButtonID 0 = right-click close; don't re-open in that case
            if ( info.ButtonID != 0 )
                m_From.SendGump( new PlayerBotDirectorGump( m_From, m_Director ) );
        }
    }
}
