using System;
using Server;

namespace Server.Mobiles
{
    // Fires every 10 seconds to drive background skill gain for active bots.
    public class PlayerBotSkillTimer : Timer
    {
        private PlayerBot m_Bot;

        public PlayerBotSkillTimer( PlayerBot bot )
            : base( TimeSpan.FromSeconds( 10.0 ), TimeSpan.FromSeconds( 10.0 ) )
        {
            m_Bot    = bot;
            Priority = TimerPriority.FiveSeconds;
        }

        protected override void OnTick()
        {
            if ( m_Bot == null || m_Bot.Deleted )
            {
                Stop();
                return;
            }

            BotActivity act = m_Bot.CurrentActivity;

            // Only gain skills when doing something active
            if ( act == BotActivity.Combat   ||
                 act == BotActivity.Crafting ||
                 act == BotActivity.Hunting  )
            {
                m_Bot.TickSkillGain( act );
            }
        }
    }
}
