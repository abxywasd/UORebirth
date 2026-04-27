using System;
using System.Collections.Generic;
using Server;
using Server.Targeting;

namespace Server.Mobiles
{
    // Coordinates broadcast commands issued via "all <cmd>" speech so that exactly
    // one targeting cursor is opened on the master (not one per bot in range).
    public static class PlayerBotAllCommandHandler
    {
        // Masters that currently have an open "all attack" cursor.
        private static readonly Dictionary<Mobile, bool> s_PendingAttackCursor = new Dictionary<Mobile, bool>();

        // Deduplication guard: OnSpeech fires once per bot in range, so without this,
        // each broadcast method would be called N times and produce N*N ack lines.
        // TryClaimBroadcast returns true only for the first call per master per speech-event
        // batch; the guard is cleared by a zero-delay timer after the synchronous dispatch ends.
        private static readonly Dictionary<Mobile, bool> s_BroadcastGuard = new Dictionary<Mobile, bool>();

        private static bool TryClaimBroadcast( Mobile master )
        {
            if ( s_BroadcastGuard.ContainsKey( master ) )
                return false;

            s_BroadcastGuard[master] = true;
            Timer.DelayCall( TimeSpan.Zero, delegate { s_BroadcastGuard.Remove( master ); } );
            return true;
        }

        // Called by PlayerBot.OnSpeech on "all kill" / "all attack" keywords.
        // Returns true when this bot should suppress the BaseAI handler.
        public static bool TryBeginAllAttack( Mobile master )
        {
            if ( s_PendingAttackCursor.ContainsKey( master ) )
                return true; // cursor already open — suppress this bot's duplicate

            if ( !TryClaimBroadcast( master ) )
                return true; // another bot already handled this speech event

            s_PendingAttackCursor[master] = true;
            master.Target = new PlayerBotAllAttackTarget( master );
            master.SendMessage( "Select a target for all your bots." );
            return true;
        }

        // Called by the targeting cursor when a target is chosen.
        public static void BroadcastAttackOrder( Mobile master, Mobile target )
        {
            s_PendingAttackCursor.Remove( master );
            // No TryClaimBroadcast here — this is triggered by cursor resolution, not speech.
            int delayMs = 0;
            foreach ( PlayerBot bot in GetControlledBots( master ) )
            {
                bot.ControlTarget = target;
                bot.ControlOrder  = OrderType.Attack;
                SayAckDelayed( bot, CommandAckType.Attack, delayMs );
                delayMs += Utility.Random( 80, 300 );
            }
        }

        // Called when the cursor is cancelled or expires.
        public static void ClearPendingCursor( Mobile master )
        {
            s_PendingAttackCursor.Remove( master );
        }

        // Broadcasts a status report — each bot says HP% and current activity overhead.
        public static void BroadcastStatusReport( Mobile master )
        {
            if ( !TryClaimBroadcast( master ) ) return;
            foreach ( PlayerBot bot in GetControlledBots( master ) )
            {
                string msg = String.Format(
                    "[{0}% HP] [{1}]",
                    (int)( (double)bot.Hits / bot.HitsMax * 100 ),
                    bot.ActivityState.Current
                );
                bot.Say( msg );
            }
        }

        // Broadcasts a non-targeted order to all controlled bots.
        public static void BroadcastOrder( Mobile master, OrderType order, Mobile orderTarget )
        {
            if ( !TryClaimBroadcast( master ) ) return;
            CommandAckType ack;
            switch ( order )
            {
                case OrderType.Come:   ack = CommandAckType.Come;   break;
                case OrderType.Follow: ack = CommandAckType.Follow; break;
                case OrderType.Guard:  ack = CommandAckType.Guard;  break;
                case OrderType.Stay:   ack = CommandAckType.Stay;   break;
                default:               ack = CommandAckType.Stop;   break; // Stop and anything else
            }

            int delayMs = 0;
            foreach ( PlayerBot bot in GetControlledBots( master ) )
            {
                bot.ControlTarget = orderTarget;
                bot.ControlOrder  = order;
                SayAckDelayed( bot, ack, delayMs );
                delayMs += Utility.Random( 80, 300 );
            }
        }

        // Steps every controlled bot one tile away from the master, clearing the path.
        public static void BroadcastMove( Mobile master )
        {
            if ( !TryClaimBroadcast( master ) ) return;
            int delayMs = 0;
            foreach ( PlayerBot bot in GetControlledBots( master ) )
            {
                PlayerBotAI ai = bot.AIObject as PlayerBotAI;
                if ( ai == null ) continue;
                ai.MoveAwayFromMaster();
                SayAckDelayed( bot, CommandAckType.Move, delayMs );
                delayMs += Utility.Random( 80, 300 );
            }
        }

        // Sets ForceMasterHeal on every mage bot so they cast Heal on master this tick.
        public static void BroadcastHealMaster( Mobile master )
        {
            if ( !TryClaimBroadcast( master ) ) return;
            int delayMs = 0;
            foreach ( PlayerBot bot in GetControlledBots( master ) )
            {
                if ( !bot.UsesMagic ) continue;
                bot.ForceMasterHeal = true;
                SayAckDelayed( bot, CommandAckType.Heal, delayMs );
                delayMs += Utility.Random( 80, 300 );
            }
        }

        // Speaks immediately (delayMs == 0) or via a one-shot Timer so bots stagger
        // their acknowledgment lines rather than all speaking in the same tick.
        private static void SayAckDelayed( PlayerBot bot, CommandAckType ack, int delayMs )
        {
            if ( delayMs == 0 )
            {
                PlayerBotSpeaker.SayCommandAck( bot, ack );
                return;
            }

            PlayerBot  b = bot;
            CommandAckType a = ack;
            Timer.DelayCall( TimeSpan.FromMilliseconds( delayMs ), delegate
            {
                if ( !b.Deleted && b.Alive )
                    PlayerBotSpeaker.SayCommandAck( b, a );
            } );
        }

        private static List<PlayerBot> GetControlledBots( Mobile master )
        {
            var bots = new List<PlayerBot>();
            foreach ( Mobile m in World.Mobiles.Values )
            {
                PlayerBot pb = m as PlayerBot;
                if ( pb != null && !pb.Deleted && pb.Alive && pb.ControlMaster == master )
                    bots.Add( pb );
            }
            return bots;
        }
    }

    // Targeting cursor that broadcasts the attack order to all controlled bots on resolution.
    public class PlayerBotAllAttackTarget : Target
    {
        private readonly Mobile m_Master;

        public PlayerBotAllAttackTarget( Mobile master )
            : base( -1, false, TargetFlags.Harmful )
        {
            m_Master = master;
        }

        protected override void OnTarget( Mobile from, object targeted )
        {
            Mobile target = targeted as Mobile;
            if ( target == null || !target.Alive || target.Deleted )
            {
                PlayerBotAllCommandHandler.ClearPendingCursor( m_Master );
                from.SendMessage( "That is not a valid target." );
                return;
            }

            PlayerBotAllCommandHandler.BroadcastAttackOrder( m_Master, target );
        }

        protected override void OnTargetCancel( Mobile from, TargetCancelType cancelType )
        {
            PlayerBotAllCommandHandler.ClearPendingCursor( m_Master );
        }
    }
}
