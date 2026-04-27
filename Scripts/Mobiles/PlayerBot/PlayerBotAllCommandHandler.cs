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

        // Called by PlayerBot.OnSpeech on "all kill" / "all attack" keywords.
        // Returns true when this bot should suppress the BaseAI handler.
        public static bool TryBeginAllAttack( Mobile master )
        {
            if ( s_PendingAttackCursor.ContainsKey( master ) )
                return true; // cursor already open — suppress this bot's duplicate

            s_PendingAttackCursor[master] = true;
            master.Target = new PlayerBotAllAttackTarget( master );
            master.SendMessage( "Select a target for all your bots." );
            return true;
        }

        // Called by the targeting cursor when a target is chosen.
        public static void BroadcastAttackOrder( Mobile master, Mobile target )
        {
            s_PendingAttackCursor.Remove( master );
            foreach ( PlayerBot bot in GetControlledBots( master ) )
            {
                bot.ControlTarget = target;
                bot.ControlOrder  = OrderType.Attack;
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
            foreach ( PlayerBot bot in GetControlledBots( master ) )
            {
                bot.ControlTarget = orderTarget;
                bot.ControlOrder  = order;
            }
        }

        // Steps every controlled bot one tile away from the master, clearing the path.
        public static void BroadcastMove( Mobile master )
        {
            foreach ( PlayerBot bot in GetControlledBots( master ) )
            {
                PlayerBotAI ai = bot.AIObject as PlayerBotAI;
                if ( ai != null )
                    ai.MoveAwayFromMaster();
            }
        }

        // Sets ForceMasterHeal on every mage bot so they cast Heal on master this tick.
        public static void BroadcastHealMaster( Mobile master )
        {
            foreach ( PlayerBot bot in GetControlledBots( master ) )
            {
                if ( bot.UsesMagic )
                    bot.ForceMasterHeal = true;
            }
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
