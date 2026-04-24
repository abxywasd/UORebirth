using System;
using System.Collections.Generic;
using Server;
using Server.Network;
using Server.Spells;
using Server.Targeting;

namespace Server.Mobiles
{
    public class PlayerBotAI : BaseAI
    {
        private DateTime m_NextCastTime;
        private DateTime m_NextSpeechTime;
        private DateTime m_NextActivityChange;
        private int      m_CombatTick;

        // Navigation stuck detection
        private Point3D  m_LastTravelPos;
        private int      m_StuckTicks;

        // Observation check cache (avoid per-tick sector queries)
        private bool     m_LastObservedState;
        private DateTime m_NextObservationCheck;

        public PlayerBotAI( BaseCreature m ) : base( m )
        {
        }

        // ── Main dispatch ──────────────────────────────────────────────────────
        public override bool Think()
        {
            if ( m_Mobile.Deleted )
                return false;

            PlayerBot bot = m_Mobile as PlayerBot;
            if ( bot == null )
                return base.Think();

            // Handle pending spell target first (MageAI pattern)
            Target targ = m_Mobile.Target;
            if ( targ != null )
            {
                ProcessSpellTarget( targ, bot );
                return true;
            }

            // Recruited bots obey their control master via the standard Obey() path
            if ( m_Mobile.Controled && m_Mobile.ControlMaster != null )
                return base.Think();

            switch ( bot.CurrentActivity )
            {
                case BotActivity.Traveling:  return DoActivityTravel( bot );
                case BotActivity.Hunting:    return DoActivityHunt( bot );
                case BotActivity.Combat:     return DoActivityCombat( bot );
                case BotActivity.Fleeing:    return DoActivityFlee( bot );
                case BotActivity.Crafting:   return DoActivityCraft( bot );
                case BotActivity.TownVisit:  return DoActivityTownVisit( bot );
                case BotActivity.Grouped:    return DoActivityGrouped( bot );
                default:                     return DoActivityWander( bot );
            }
        }

        // ── Activity: Wander ──────────────────────────────────────────────────
        private bool DoActivityWander( PlayerBot bot )
        {
            bot.ActivityState.ActivityTimer++;

            // Periodically decide on a new activity
            if ( DateTime.Now >= m_NextActivityChange )
            {
                bot.ChooseNextActivity();
                m_NextActivityChange = DateTime.Now + TimeSpan.FromSeconds( 30.0 + Utility.Random( 60 ) );
            }

            // Scan for enemies
            if ( AquireFocusMob( m_Mobile.RangePerception, m_Mobile.FightMode, false, false, true ) )
            {
                if ( bot.ShouldAttack( m_Mobile.FocusMob ) )
                {
                    m_Mobile.Combatant = m_Mobile.FocusMob;
                    bot.ActivityState.SetActivity( BotActivity.Combat );
                    m_Mobile.Warmode = true;
                    return true;
                }
            }

            // Follow group leader if assigned
            if ( bot.Group != null && bot.Group.Leader != bot
                 && bot.Group.Leader != null && !bot.Group.Leader.Deleted )
            {
                bot.ActivityState.SetActivity( BotActivity.Grouped );
                return true;
            }

            base.DoActionWander();
            MaybeSpeak( bot );
            return true;
        }

        // ── Activity: Travel ──────────────────────────────────────────────────
        private bool DoActivityTravel( PlayerBot bot )
        {
            Point3D dest  = bot.ActivityState.TravelDestination;
            Map     dMap  = bot.ActivityState.TravelMap;

            if ( dMap == null || dMap == Map.Internal )
            {
                bot.ActivityState.SetActivity( BotActivity.Wandering );
                return true;
            }

            // Interrupt travel if attacked
            if ( AquireFocusMob( m_Mobile.RangePerception, FightMode.Agressor, false, false, true ) )
            {
                if ( bot.ShouldAttack( m_Mobile.FocusMob ) )
                {
                    m_Mobile.Combatant = m_Mobile.FocusMob;
                    bot.ActivityState.SetActivity( BotActivity.Combat );
                    m_Mobile.Warmode = true;
                    return true;
                }
            }

            // Instant travel when no player is watching — eliminates water/obstacle pathfinding failures
            if ( !AnyPlayersInRange( 18 ) )
            {
                int z = dMap.GetAverageZ( dest.X, dest.Y );
                m_Mobile.MoveToWorld( new Point3D( dest.X, dest.Y, z ), dMap );
                m_StuckTicks = 0;
                m_Path = null;
                bot.OnArrived();
                return true;
            }

            // Stuck detection: if position hasn't changed in 10 ticks, force repath;
            // if still stuck after 40 ticks total, give up and wander.
            if ( m_Mobile.Location == m_LastTravelPos )
            {
                m_StuckTicks++;

                if ( m_StuckTicks >= 40 )
                {
                    // Completely lost — abandon travel
                    m_StuckTicks = 0;
                    m_Path = null;
                    bot.ActivityState.SetActivity( BotActivity.Wandering );
                    return true;
                }

                if ( m_StuckTicks % 10 == 0 )
                {
                    // Force repath and nudge in a random walkable direction
                    m_Path = null;
                    Direction nudge = (Direction)Utility.Random( 8 );
                    DoMove( nudge | Direction.Running );
                }
            }
            else
            {
                m_StuckTicks = 0;
            }

            m_LastTravelPos = m_Mobile.Location;

            bool arrived = PlayerBotNavigator.Advance( this, bot, dest, dMap );
            if ( arrived )
            {
                m_StuckTicks = 0;
                bot.OnArrived();
            }

            return true;
        }

        // ── Activity: Hunt ────────────────────────────────────────────────────
        private bool DoActivityHunt( PlayerBot bot )
        {
            if ( m_Mobile.Combatant != null && !m_Mobile.Combatant.Deleted && m_Mobile.Combatant.Alive )
            {
                bot.ActivityState.SetActivity( BotActivity.Combat );
                return DoActivityCombat( bot );
            }

            if ( AquireFocusMob( m_Mobile.RangePerception, FightMode.Closest, false, false, true ) )
            {
                if ( bot.ShouldAttack( m_Mobile.FocusMob ) )
                {
                    m_Mobile.Combatant = m_Mobile.FocusMob;
                    bot.ActivityState.SetActivity( BotActivity.Combat );
                    m_Mobile.Warmode = true;
                    return true;
                }
            }

            WalkRandomInHome( 1, 2, 2 );
            MaybeSpeak( bot );

            // Give up hunting after a while with no targets
            bot.ActivityState.ActivityTimer++;
            if ( bot.ActivityState.ActivityTimer > 120 )
                bot.ActivityState.SetActivity( BotActivity.Wandering );

            return true;
        }

        // ── Activity: Combat ──────────────────────────────────────────────────
        private bool DoActivityCombat( PlayerBot bot )
        {
            Mobile c = m_Mobile.Combatant;
            m_Mobile.Warmode = true;

            if ( c == null || c.Deleted || !c.Alive || c.Map != m_Mobile.Map )
            {
                // Try to find a new target before giving up
                if ( AquireFocusMob( m_Mobile.RangePerception, m_Mobile.FightMode, false, false, true ) )
                {
                    if ( bot.ShouldAttack( m_Mobile.FocusMob ) )
                    {
                        m_Mobile.Combatant = m_Mobile.FocusMob;
                        m_Mobile.FocusMob  = null;
                        return true;
                    }
                }

                bot.ActivityState.SetActivity( BotActivity.Hunting );
                m_Mobile.Warmode = false;
                return true;
            }

            // Notify group of shared target
            if ( bot.Group != null && bot.Group.Leader == bot )
                bot.Group.SharedTarget = c;

            // Self-heal before anything else
            if ( CheckSelfHeal( bot ) )
                return true;

            // Offensive spells (mage path)
            if ( bot.UsesMagic && TryCastOffensiveSpell( bot, c ) )
                return true;

            // Melee approach
            if ( !m_Mobile.InRange( c, m_Mobile.RangeFight ) )
            {
                MoveTo( c, true, m_Mobile.RangeFight );
            }
            else if ( Utility.RandomDouble() <= 0.25 )
            {
                m_Mobile.Direction = m_Mobile.GetDirectionTo( c );
            }

            // Flee check
            if ( bot.ShouldFlee( c ) )
            {
                m_Mobile.FocusMob = c;
                bot.ActivityState.SetActivity( BotActivity.Fleeing );
                Action = ActionType.Flee;
                return true;
            }

            // Trigger skill gain periodically
            m_CombatTick++;
            if ( m_CombatTick % 4 == 0 )
                bot.TryGainCombatSkills();

            return true;
        }

        // ── Activity: Flee ────────────────────────────────────────────────────
        private bool DoActivityFlee( PlayerBot bot )
        {
            if ( m_Mobile.Hits > m_Mobile.HitsMax * 2 / 3 )
            {
                bot.ActivityState.SetActivity( BotActivity.Hunting );
                Action = ActionType.Wander;
                m_Mobile.Warmode = false;
                return true;
            }

            m_Mobile.FocusMob = m_Mobile.Combatant;
            base.DoActionFlee();
            MaybeSpeak( bot );
            return true;
        }

        // ── Activity: Craft ───────────────────────────────────────────────────
        private bool DoActivityCraft( PlayerBot bot )
        {
            // Interrupt if attacked
            if ( AquireFocusMob( m_Mobile.RangePerception, FightMode.Agressor, false, false, true ) )
            {
                bot.ActivityState.SetActivity( BotActivity.Combat );
                m_Mobile.Combatant = m_Mobile.FocusMob;
                m_Mobile.Warmode = true;
                return true;
            }

            return PlayerBotCrafter.DoCraftTick( bot );
        }

        // ── Activity: Town Visit ──────────────────────────────────────────────
        private bool DoActivityTownVisit( PlayerBot bot )
        {
            bot.ActivityState.ActivityTimer++;

            WalkRandomInHome( 2, 2, 1 );
            MaybeSpeak( bot );
            MaybeSocialize( bot );

            if ( bot.ActivityState.ActivityTimer > 120 + Utility.Random( 120 ) )
                bot.ChooseNextActivity();

            return true;
        }

        // ── Activity: Grouped ─────────────────────────────────────────────────
        private bool DoActivityGrouped( PlayerBot bot )
        {
            PlayerBotGroup grp = bot.Group;

            if ( grp == null || grp.Leader == null || grp.Leader.Deleted )
            {
                bot.ActivityState.SetActivity( BotActivity.Wandering );
                return true;
            }

            Mobile leader = grp.Leader;

            // Follow leader
            WalkMobileRange( leader, 1, true, 1, 3 );

            // Attack shared target if one is set
            Mobile sharedTarget = grp.SharedTarget;
            if ( sharedTarget != null && !sharedTarget.Deleted && sharedTarget.Alive )
            {
                m_Mobile.Combatant = sharedTarget;
                bot.ActivityState.SetActivity( BotActivity.Combat );
                m_Mobile.Warmode = true;
            }

            return true;
        }

        // ── Magic: Offensive casting ──────────────────────────────────────────
        private bool TryCastOffensiveSpell( PlayerBot bot, Mobile target )
        {
            if ( m_Mobile.Spell != null && m_Mobile.Spell.IsCasting )
                return true; // already casting — don't move yet

            if ( DateTime.Now < m_NextCastTime )
                return false;

            if ( !m_Mobile.InRange( target, 12 ) || !m_Mobile.InLOS( target ) )
                return false;

            if ( m_Mobile.Mana < 4 )
                return false;

            // Must have a prior combat exchange before spells land (notoriety guard)
            if ( !m_Mobile.CanBeHarmful( target, false ) )
                return false;

            Spell spell = PlayerBotCombatHelper.ChooseOffensiveSpell( bot, target );
            if ( spell == null )
                return false;

            if ( spell.Cast() )
            {
                TimeSpan delay = spell.GetCastDelay() + TimeSpan.FromSeconds( Utility.Random( 3 ) );
                m_NextCastTime = DateTime.Now + delay;
                return true;
            }

            return false;
        }

        // ── Magic: Self-heal ──────────────────────────────────────────────────
        private bool CheckSelfHeal( PlayerBot bot )
        {
            if ( !bot.UsesMagic ) return false;
            if ( m_Mobile.Spell != null && m_Mobile.Spell.IsCasting ) return false;
            if ( DateTime.Now < m_NextCastTime ) return false;

            return PlayerBotCombatHelper.TryCastHeal( bot, ref m_NextCastTime );
        }

        // ── Spell target resolution (MageAI pattern) ──────────────────────────
        private void ProcessSpellTarget( Target targ, PlayerBot bot )
        {
            Mobile toTarget = m_Mobile.Combatant;

            if ( (targ.Flags & TargetFlags.Harmful) != 0 )
            {
                if ( toTarget == null )
                {
                    targ.Cancel( m_Mobile, TargetCancelType.Canceled );
                    return;
                }

                if ( (targ.Range == -1 || m_Mobile.InRange( toTarget, targ.Range ))
                     && m_Mobile.CanSee( toTarget ) && m_Mobile.InLOS( toTarget ) )
                {
                    targ.Invoke( m_Mobile, toTarget );
                }
                else
                {
                    targ.Cancel( m_Mobile, TargetCancelType.Canceled );
                }
            }
            else if ( (targ.Flags & TargetFlags.Beneficial) != 0 )
            {
                targ.Invoke( m_Mobile, m_Mobile );
            }
            else
            {
                targ.Cancel( m_Mobile, TargetCancelType.Canceled );
            }
        }

        // ── Speech helper ──────────────────────────────────────────────────────
        private void MaybeSpeak( PlayerBot bot )
        {
            if ( DateTime.Now < m_NextSpeechTime ) return;
            if ( Utility.Random( 25 ) != 0 ) return;

            m_NextSpeechTime = DateTime.Now + TimeSpan.FromSeconds( 30.0 + Utility.Random( 90 ) );
            PlayerBotSpeaker.SayContextual( bot );
        }

        // ── Social conversation helper ─────────────────────────────────────────
        private void MaybeSocialize( PlayerBot bot )
        {
            if ( bot.InConversation ) return;
            if ( Utility.Random( 150 ) != 0 ) return; // ~1% chance per tick
            PlayerBotConversation.TryStartConversation( bot );
        }

        // ── Observation check (cached 5s to limit sector queries) ─────────────
        private bool AnyPlayersInRange( int range )
        {
            if ( DateTime.Now < m_NextObservationCheck )
                return m_LastObservedState;

            m_NextObservationCheck = DateTime.Now + TimeSpan.FromSeconds( 5.0 );
            m_LastObservedState    = false;

            if ( m_Mobile.Map == null || m_Mobile.Map == Map.Internal )
                return false;

            IPooledEnumerable eable = m_Mobile.Map.GetClientsInRange( m_Mobile.Location, range );
            foreach ( NetState ns in eable )
            {
                if ( ns.Mobile != null && !ns.Mobile.Deleted && ns.Mobile.Alive )
                {
                    m_LastObservedState = true;
                    break;
                }
            }
            eable.Free();

            return m_LastObservedState;
        }

        // ── Navigation helper (used by PlayerBotNavigator.Advance) ────────────
        public bool MoveToPoint( Point3D dest, bool run, int range )
        {
            if ( m_Mobile.InRange( dest, range ) )
                return true;

            // Reuse PathFollower already managed in BaseAI if goal unchanged
            if ( m_Path != null && m_Path.Goal is Point3D && (Point3D)m_Path.Goal == dest )
            {
                if ( m_Path.Follow( run, range ) )
                {
                    m_Path = null;
                    return true;
                }
                return false;
            }

            // Create new PathFollower targeting the destination point
            m_Path = new PathFollower( m_Mobile, dest );
            m_Path.Mover = new MoveMethod( DoMoveImpl );

            if ( m_Path.Follow( run, range ) )
            {
                m_Path = null;
                return true;
            }

            return false;
        }

    }
}
