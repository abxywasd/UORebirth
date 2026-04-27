using System;
using System.Collections.Generic;
using Server;
using Server.Items;
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
        private DateTime m_NextTargetScanTime;
        private int      m_CombatTick;

        // Attack order stickiness: suppress "assist master" override for this many seconds
        // after the master manually orders the bot to attack a specific target.
        private DateTime m_AttackOrderIssuedTime;
        private const double AttackOrderGracePeriod = 8.0;

        // AI timer interval for running activities — matches player-like run cadence
        private const double BotRunSpeed = 0.15;

        // Navigation stuck detection
        private Point3D  m_LastTravelPos;
        private int      m_StuckTicks;

        // Observation check cache (avoid per-tick sector queries)
        private bool     m_LastObservedState;
        private DateTime m_NextObservationCheck;

        // Rate-limit the ally-heal sector scan to once per second
        private DateTime m_NextAllyHealScanTime;

        // Weapon swap state: items unequipped before casting, restored after
        private List<Item> m_StashedEquipment;
        private Mobile     m_HealTarget;

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

            // Restore stashed weapons whenever not actively casting or awaiting target
            if ( bot.UsesMagic )
                MaybeRestoreWeapons();

            // Handle pending spell target first (MageAI pattern)
            Target targ = m_Mobile.Target;
            if ( targ != null )
            {
                ProcessSpellTarget( targ, bot );
                return true;
            }

            // Controlled bots: handle attack orders and master-assist ourselves
            if ( m_Mobile.Controled && m_Mobile.ControlMaster != null )
                return DoControlledThink( bot );

            // Uncontrolled: self-defense before healing so aggressors set Combatant
            // and the melee engine starts swinging even while we cast a heal
            if ( CheckSelfDefense( bot ) )
                return true;

            // Uncontrolled mage bots self-heal between fights
            if ( !m_Mobile.Controled && CheckSelfHeal( bot ) )
                return true;

            // Engage NPCs attacking a player/group-member so Agressor-mode NPCs
            // can retaliate against this bot and spread aggro off the real player.
            if ( !m_Mobile.Controled && CheckDefendNearby( bot ) )
                return true;

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

        // ── Obey override: run our custom logic every controlled AI tick ──────────
        // The RunUO timer calls Obey() (not Think()) when Controled==true.
        // Without this override, Think()/DoControlledThink() only runs when
        // DoOrderAttack fires — so the bot wanders to its spawn point and
        // goes out of EndPickTarget range (14 tiles) before the player can
        // issue a kill order.
        public override bool Obey()
        {
            if ( m_Mobile.Deleted )
                return false;

            PlayerBot bot = m_Mobile as PlayerBot;
            if ( bot == null )
                return base.Obey();

            if ( bot.UsesMagic )
                MaybeRestoreWeapons();

            Target targ = m_Mobile.Target;
            if ( targ != null )
            {
                ProcessSpellTarget( targ, bot );
                return true;
            }

            return DoControlledThink( bot );
        }

        // ── Controlled-bot dispatch ────────────────────────────────────────────
        private bool DoControlledThink( PlayerBot bot )
        {
            Mobile master = m_Mobile.ControlMaster;
            if ( master == null || master.Deleted )
                return false;

            EnsureRunSpeed();

            switch ( m_Mobile.ControlOrder )
            {
                case OrderType.Attack:
                {
                    Mobile ct = m_Mobile.ControlTarget;
                    if ( ct != null && !ct.Deleted && ct.Alive && ct.Map == m_Mobile.Map )
                    {
                        m_Mobile.Combatant      = ct;
                        m_Mobile.Warmode        = true;
                        m_AttackOrderIssuedTime = DateTime.Now;
                        bot.ActivityState.SetActivity( BotActivity.Combat );
                        return DoActivityCombat( bot );
                    }
                    // ct != null but dead/gone — clear the stale order
                    // ct == null means the targeting cursor hasn't been resolved yet; keep order pending
                    if ( ct != null )
                    {
                        m_Mobile.ControlTarget = null;
                        m_Mobile.ControlOrder  = OrderType.None;
                    }
                    break;
                }

                case OrderType.Stop:
                case OrderType.Stay:
                    m_Mobile.Combatant = null;
                    m_Mobile.Warmode   = false;
                    bot.ActivityState.SetActivity( BotActivity.Wandering );
                    return true;

                case OrderType.Come:
                    m_Mobile.Combatant = null;
                    m_Mobile.Warmode   = false;
                    bot.ActivityState.SetActivity( BotActivity.Wandering );
                    if ( !m_Mobile.InRange( master, 2 ) )
                        FollowRunning( master, 2 );
                    return true;

                case OrderType.Follow:
                {
                    Mobile followTarget = m_Mobile.ControlTarget ?? master;
                    m_Mobile.Combatant  = null;
                    m_Mobile.Warmode    = false;
                    bot.ActivityState.SetActivity( BotActivity.Wandering );
                    if ( !m_Mobile.InRange( followTarget, 2 ) )
                        FollowRunning( followTarget, 2 );
                    return true;
                }

                case OrderType.Guard:
                    // Guard = follow master + react to his aggressors; handled by the assist block below.
                    break;
            }

            // Assist master when they are in combat.
            // Suppressed for AttackOrderGracePeriod seconds after a manual attack order
            // so the player's explicit targeting intent is not overridden on the very next tick.
            bool recentManualOrder = (DateTime.Now - m_AttackOrderIssuedTime).TotalSeconds < AttackOrderGracePeriod;

            if ( !recentManualOrder
                 && master.Alive && master.Combatant != null
                 && !master.Combatant.Deleted && master.Combatant.Alive )
            {
                m_Mobile.Combatant = master.Combatant;
                m_Mobile.Warmode   = true;
                bot.ActivityState.SetActivity( BotActivity.Combat );
                return DoActivityCombat( bot );
            }

            // Defend ourselves if attacked
            if ( CheckSelfDefense( bot ) )
                return true;

            // Default: follow master; heal self/master/allies proactively
            m_Mobile.Warmode = false;
            bot.ActivityState.SetActivity( BotActivity.Wandering );
            if ( master.Alive && !m_Mobile.InRange( master, 3 ) )
                FollowRunning( master, 2 );
            if ( CheckSelfHeal( bot ) )
                return true;
            if ( CheckMasterHeal( bot ) )
                return true;
            if ( CheckAllyHeal( bot ) )
                return true;
            return true;
        }

        // Sets CurrentSpeed to BotRunSpeed; only fires OnCurrentSpeedChanged when transitioning from a slower speed.
        private void EnsureRunSpeed()
        {
            if ( m_Mobile.CurrentSpeed > BotRunSpeed )
                m_Mobile.CurrentSpeed = BotRunSpeed;
        }

        // Steps toward target with Direction.Running always set; falls back to pathfinding if blocked.
        private void FollowRunning( Mobile target, int range )
        {
            if ( m_Mobile.InRange( target, range ) )
            {
                m_Path = null;
                return;
            }

            Direction d = (m_Mobile.GetDirectionTo( target ) & Direction.Mask) | Direction.Running;
            if ( DoMove( d ) )
            {
                m_Path = null;
                return;
            }

            // Blocked — use PathFollower with run=true so each step also carries Running
            if ( m_Path == null || m_Path.Goal != (object)target )
            {
                m_Path = new PathFollower( m_Mobile, target );
                m_Path.Mover = new MoveMethod( DoMoveImpl );
            }

            if ( m_Path.Follow( true, range ) )
                m_Path = null;
        }

        // ── Activity: Wander ──────────────────────────────────────────────────
        private bool DoActivityWander( PlayerBot bot )
        {
            bot.ActivityState.ActivityTimer++;

            // PKs patrol at run speed — they look for prey, not a stroll
            if ( bot.PlayerBotProfile == PlayerBotPersona.PlayerBotProfile.PlayerKiller )
                EnsureRunSpeed();

            // Periodically decide on a new activity
            if ( DateTime.Now >= m_NextActivityChange )
            {
                bot.ChooseNextActivity();
                m_NextActivityChange = DateTime.Now + TimeSpan.FromSeconds( 30.0 + Utility.Random( 60 ) );
            }

            // PKs proactively scan for any valid target (rate-limited to ~2s)
            if ( bot.PlayerBotProfile == PlayerBotPersona.PlayerBotProfile.PlayerKiller )
            {
                if ( DateTime.Now >= m_NextTargetScanTime )
                {
                    m_NextTargetScanTime = DateTime.Now + TimeSpan.FromSeconds( 2.0 );
                    Mobile target = ScanForTarget( bot, m_Mobile.RangePerception );
                    if ( target != null )
                    {
                        m_Mobile.Combatant = target;
                        m_Mobile.FocusMob  = target;
                        bot.ActivityState.SetActivity( BotActivity.Combat );
                        m_Mobile.Warmode = true;
                        return true;
                    }
                }
            }
            else if ( AquireFocusMob( m_Mobile.RangePerception, m_Mobile.FightMode, false, false, true ) )
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

            EnsureRunSpeed();

            // Interrupt travel: PKs scan for any target; others react to aggressors only
            if ( bot.PlayerBotProfile == PlayerBotPersona.PlayerBotProfile.PlayerKiller
                 && DateTime.Now >= m_NextTargetScanTime )
            {
                m_NextTargetScanTime = DateTime.Now + TimeSpan.FromSeconds( 2.0 );
                Mobile target = ScanForTarget( bot, m_Mobile.RangePerception );
                if ( target != null )
                {
                    m_Mobile.Combatant = target;
                    m_Mobile.FocusMob  = target;
                    bot.ActivityState.SetActivity( BotActivity.Combat );
                    m_Mobile.Warmode = true;
                    return true;
                }
            }
            else if ( AquireFocusMob( m_Mobile.RangePerception, FightMode.Agressor, false, false, true ) )
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
            EnsureRunSpeed();

            if ( m_Mobile.Combatant != null && !m_Mobile.Combatant.Deleted && m_Mobile.Combatant.Alive )
            {
                bot.ActivityState.SetActivity( BotActivity.Combat );
                return DoActivityCombat( bot );
            }

            // Direct scan bypasses AquireFocusMob's faction-check limitation
            Mobile target = ScanForTarget( bot, m_Mobile.RangePerception );
            if ( target != null )
            {
                m_Mobile.Combatant = target;
                m_Mobile.FocusMob  = target;
                bot.ActivityState.SetActivity( BotActivity.Combat );
                m_Mobile.Warmode = true;
                return true;
            }

            Action = ActionType.Wander;
            WalkRandomInHome( 2, 5, 15 );
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
                Mobile next = ScanForTarget( bot, m_Mobile.RangePerception );
                if ( next != null )
                {
                    m_Mobile.Combatant = next;
                    m_Mobile.FocusMob  = null;
                    return true;
                }

                m_Mobile.Warmode = false;
                Action = ActionType.Wander;
                if ( bot.PlayerBotProfile == PlayerBotPersona.PlayerBotProfile.PlayerKiller )
                    bot.ChooseNextActivity();
                else
                    bot.ActivityState.SetActivity( BotActivity.Hunting );
                return true;
            }

            EnsureRunSpeed();

            // Notify group of shared target
            if ( bot.Group != null && bot.Group.Leader == bot )
                bot.Group.SharedTarget = c;

            // Self-heal before anything else
            if ( CheckSelfHeal( bot ) )
                return true;

            // Heal/cure master if controlled and they need it
            if ( CheckMasterHeal( bot ) )
                return true;

            // Heal/cure allied bots hired by the same master
            if ( CheckAllyHeal( bot ) )
                return true;

            // Offensive spells (mage path)
            if ( bot.UsesMagic && TryCastOffensiveSpell( bot, c ) )
                return true;

            // Melee approach
            if ( !m_Mobile.InRange( c, m_Mobile.RangeFight ) )
            {
                FollowRunning( c, m_Mobile.RangeFight );
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

            EnsureRunSpeed();
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

            EnsureRunSpeed();

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

            StashWeaponsForCasting();
            if ( spell.Cast() )
            {
                TimeSpan delay = spell.GetCastDelay() + TimeSpan.FromSeconds( Utility.Random( 3 ) );
                m_NextCastTime = DateTime.Now + delay;
                return true;
            }

            RestoreStashedWeapons();
            return false;
        }

        // ── Magic: Self-heal ──────────────────────────────────────────────────
        private bool CheckSelfHeal( PlayerBot bot )
        {
            if ( !bot.UsesMagic ) return false;
            if ( m_Mobile.Spell != null && m_Mobile.Spell.IsCasting ) return false;
            if ( DateTime.Now < m_NextCastTime ) return false;
            if ( !bot.Poisoned && bot.Hits >= bot.HitsMax - 15 ) return false;
            if ( !PlayerBotCombatHelper.HasHealSpellReady( bot ) ) return false;

            StashWeaponsForCasting();
            bool result = PlayerBotCombatHelper.TryCastHeal( bot, ref m_NextCastTime );
            if ( !result )
                RestoreStashedWeapons();
            return result;
        }

        // ── Magic: Heal/cure master ────────────────────────────────────────────
        private bool CheckMasterHeal( PlayerBot bot )
        {
            if ( !bot.UsesMagic ) return false;
            if ( m_Mobile.Spell != null && m_Mobile.Spell.IsCasting ) return false;
            if ( DateTime.Now < m_NextCastTime ) return false;

            Mobile master = m_Mobile.ControlMaster;
            if ( master == null || master.Deleted || !master.Alive ) return false;
            if ( !m_Mobile.InRange( master, 12 ) ) return false;

            bool forced = bot.ForceMasterHeal;
            bot.ForceMasterHeal = false;

            if ( !forced && !master.Poisoned && master.Hits >= master.HitsMax - 10 ) return false;
            if ( !PlayerBotCombatHelper.HasHealSpellReadyFor( bot, master ) ) return false;

            StashWeaponsForCasting();
            bool result = PlayerBotCombatHelper.TryCastHealTarget( bot, master, ref m_NextCastTime );
            if ( result )
                m_HealTarget = master;
            else
                RestoreStashedWeapons();
            return result;
        }

        // ── Magic: Heal/cure allied bots hired by the same master ────────────
        private bool CheckAllyHeal( PlayerBot bot )
        {
            if ( !bot.UsesMagic ) return false;
            if ( m_Mobile.Spell != null && m_Mobile.Spell.IsCasting ) return false;
            if ( DateTime.Now < m_NextCastTime ) return false;
            if ( DateTime.Now < m_NextAllyHealScanTime ) return false;

            Mobile master = m_Mobile.ControlMaster;
            if ( master == null || master.Deleted ) return false;

            Map map = m_Mobile.Map;
            if ( map == null || map == Map.Internal ) return false;

            m_NextAllyHealScanTime = DateTime.Now + TimeSpan.FromSeconds( 1.0 );

            Mobile bestTarget = null;
            int worstDeficit  = 0;

            IPooledEnumerable eable = map.GetMobilesInRange( m_Mobile.Location, 12 );
            foreach ( Mobile m in eable )
            {
                if ( m == m_Mobile || m.Deleted || !m.Alive ) continue;
                if ( m == master ) continue;

                BaseCreature bc = m as BaseCreature;
                if ( bc == null || !bc.Controled || bc.ControlMaster != master ) continue;

                if ( m.Poisoned )
                {
                    bestTarget = m;
                    break;
                }

                int deficit = m.HitsMax - m.Hits;
                if ( deficit > worstDeficit && deficit > 10 )
                {
                    worstDeficit = deficit;
                    bestTarget   = m;
                }
            }
            eable.Free();

            if ( bestTarget == null ) return false;
            if ( !PlayerBotCombatHelper.HasHealSpellReadyFor( bot, bestTarget ) ) return false;

            StashWeaponsForCasting();
            bool result = PlayerBotCombatHelper.TryCastHealTarget( bot, bestTarget, ref m_NextCastTime );
            if ( result )
                m_HealTarget = bestTarget;
            else
                RestoreStashedWeapons();
            return result;
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
                Mobile healTarget = m_HealTarget ?? m_Mobile;
                m_HealTarget = null;
                targ.Invoke( m_Mobile, healTarget );
            }
            else
            {
                targ.Cancel( m_Mobile, TargetCancelType.Canceled );
            }
        }

        // ── Self-defense: react to aggressors regardless of current activity ─────
        private bool CheckSelfDefense( PlayerBot bot )
        {
            if ( bot.CurrentActivity == BotActivity.Combat || bot.CurrentActivity == BotActivity.Fleeing )
                return false;

            List<AggressorInfo> list = m_Mobile.Aggressors;
            for ( int i = 0; i < list.Count; i++ )
            {
                Mobile aggr = ((AggressorInfo)list[i]).Attacker;
                if ( aggr != null && !aggr.Deleted && aggr.Alive
                     && aggr.Map == m_Mobile.Map
                     && m_Mobile.InRange( aggr.Location, m_Mobile.RangePerception )
                     && bot.ShouldAttack( aggr ) )
                {
                    m_Mobile.Combatant = aggr;
                    m_Mobile.FocusMob  = aggr;
                    bot.ActivityState.SetActivity( BotActivity.Combat );
                    m_Mobile.Warmode = true;
                    return true;
                }
            }
            return false;
        }

        // ── Defend nearby: engage NPCs fighting the player/group so Agressor-mode ──
        // ── NPCs can subsequently retaliate against this bot. ─────────────────────
        private bool CheckDefendNearby( PlayerBot bot )
        {
            if ( m_Mobile.Combatant != null )
                return false;

            IPooledEnumerable eable = m_Mobile.Map.GetMobilesInRange( m_Mobile.Location, m_Mobile.RangePerception );
            Mobile bestTarget = null;

            foreach ( Mobile m in eable )
            {
                if ( m == m_Mobile || m.Deleted || !m.Alive )
                    continue;

                BaseCreature bc = m as BaseCreature;
                if ( bc == null || bc is PlayerBot )
                    continue;

                Mobile combatant = bc.Combatant;
                if ( combatant == null )
                    continue;

                bool fightingPlayer = combatant is PlayerMobile;
                bool fightingGroupMember = combatant is PlayerBot &&
                                           ((PlayerBot)combatant).Group != null &&
                                           ((PlayerBot)combatant).Group == bot.Group;

                if ( !fightingPlayer && !fightingGroupMember )
                    continue;

                if ( !m_Mobile.CanBeHarmful( bc, false ) )
                    continue;

                if ( bot.ShouldAttack( bc ) )
                {
                    bestTarget = bc;
                    break;
                }
            }

            eable.Free();

            if ( bestTarget == null )
                return false;

            m_Mobile.Combatant = bestTarget;
            m_Mobile.FocusMob  = bestTarget;
            bot.ActivityState.SetActivity( BotActivity.Combat );
            m_Mobile.Warmode = true;
            return true;
        }

        // ── Direct target scan (bypasses AquireFocusMob faction-check) ──────────
        private Mobile ScanForTarget( PlayerBot bot, int range )
        {
            Map map = bot.Map;
            if ( map == null || map == Map.Internal )
                return null;

            Mobile best     = null;
            double bestDist = double.MaxValue;

            IPooledEnumerable eable = map.GetMobilesInRange( bot.Location, range );
            foreach ( Mobile m in eable )
            {
                if ( !m.Alive || m.Deleted || m.IsDeadBondedPet )
                    continue;
                if ( !bot.CanSee( m ) || !bot.InLOS( m ) )
                    continue;
                if ( !bot.ShouldAttack( m ) )
                    continue;

                double dist = bot.GetDistanceToSqrt( m );
                if ( dist < bestDist )
                {
                    bestDist = dist;
                    best     = m;
                }
            }
            eable.Free();

            return best;
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

        // ── Weapon swap helpers ────────────────────────────────────────────────

        // Unequip weapons and shields to backpack before a cast attempt.
        // Spellbooks are left in place — they allow casting while equipped.
        private void StashWeaponsForCasting()
        {
            Container pack = m_Mobile.Backpack;
            if ( pack == null ) return;

            if ( m_StashedEquipment == null )
                m_StashedEquipment = new List<Item>();

            Item oneHanded = m_Mobile.FindItemOnLayer( Layer.OneHanded );
            Item twoHanded = m_Mobile.FindItemOnLayer( Layer.TwoHanded );

            if ( oneHanded != null && !(oneHanded is Spellbook) && !m_StashedEquipment.Contains( oneHanded ) )
            {
                m_StashedEquipment.Add( oneHanded );
                pack.DropItem( oneHanded );
            }
            if ( twoHanded != null && !(twoHanded is Spellbook) && !m_StashedEquipment.Contains( twoHanded ) )
            {
                m_StashedEquipment.Add( twoHanded );
                pack.DropItem( twoHanded );
            }
        }

        // Re-equip anything that was stashed.
        private void RestoreStashedWeapons()
        {
            if ( m_StashedEquipment == null || m_StashedEquipment.Count == 0 )
                return;

            foreach ( Item item in m_StashedEquipment )
            {
                if ( item != null && !item.Deleted )
                    m_Mobile.EquipItem( item );
            }
            m_StashedEquipment.Clear();
        }

        // Called at top of Think()/Obey() — restores weapons once casting and
        // any pending target cursor are both fully resolved.
        private void MaybeRestoreWeapons()
        {
            if ( m_StashedEquipment == null || m_StashedEquipment.Count == 0 )
                return;

            bool active = (m_Mobile.Spell != null && m_Mobile.Spell.IsCasting)
                          || m_Mobile.Target != null;
            if ( !active )
                RestoreStashedWeapons();
        }

        // ── "all move" helper: step away from master ──────────────────────────
        public void MoveAwayFromMaster()
        {
            Mobile master = m_Mobile.ControlMaster;
            if ( master == null || master.Deleted ) return;

            if ( m_Mobile.Combatant != null )
            {
                m_Mobile.PublicOverheadMessage( MessageType.Regular, 0x3B2, 501482 ); // I am too busy fighting...
                return;
            }

            // Opposite direction of master; try adjacent if blocked
            Direction away = (Direction)( ( (int)( m_Mobile.GetDirectionTo( master ) & Direction.Mask ) + 4 ) & 0x7 );
            if ( !DoMove( away | Direction.Running ) )
            {
                Direction alt = (Direction)( ( (int)away + 1 ) & 0x7 );
                DoMove( alt | Direction.Running );
            }
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
