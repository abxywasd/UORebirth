using System;
using System.Collections.Generic;
using Server;

namespace Server.Mobiles
{
    // Runtime-only (not serialized). Groups form and dissolve dynamically.
    public class PlayerBotGroup
    {
        private PlayerBot       m_Leader;
        private List<PlayerBot> m_Members;
        private Mobile          m_SharedTarget;

        public PlayerBot        Leader       { get { return m_Leader; } }
        public List<PlayerBot>  Members      { get { return m_Members; } }
        public Mobile SharedTarget
        {
            get { return m_SharedTarget; }
            set { m_SharedTarget = value; }
        }

        public PlayerBotGroup( PlayerBot leader )
        {
            m_Leader  = leader;
            m_Members = new List<PlayerBot>();
            m_Members.Add( leader );
            leader.Group = this;
        }

        public bool AddMember( PlayerBot bot )
        {
            if ( m_Members.Count >= 6 ) return false;
            if ( m_Members.Contains( bot ) ) return false;

            if ( bot.Group != null && bot.Group != this )
                bot.Group.RemoveMember( bot );

            m_Members.Add( bot );
            bot.Group = this;
            return true;
        }

        public void RemoveMember( PlayerBot bot )
        {
            m_Members.Remove( bot );
            bot.Group = null;

            if ( bot == m_Leader && m_Members.Count > 0 )
                m_Leader = m_Members[0];

            if ( m_Members.Count == 0 )
                Disband();
        }

        public void Disband()
        {
            foreach ( PlayerBot b in new List<PlayerBot>( m_Members ) )
                b.Group = null;
            m_Members.Clear();
        }

        // Scan nearby uncontrolled bots and form a group around initiator.
        public static PlayerBotGroup TryForm( PlayerBot initiator, int range )
        {
            if ( initiator.Group != null ) return initiator.Group;
            if ( initiator.Controled ) return null;

            PlayerBotGroup grp = new PlayerBotGroup( initiator );
            Map map = initiator.Map;
            if ( map == null || map == Map.Internal ) return grp;

            IPooledEnumerable eable = map.GetMobilesInRange( initiator.Location, range );
            foreach ( Mobile m in eable )
            {
                if ( grp.Members.Count >= 6 ) break;
                if ( m == initiator ) continue;

                PlayerBot bot = m as PlayerBot;
                if ( bot == null ) continue;
                if ( bot.Group != null ) continue;
                if ( bot.Controled ) continue;

                grp.AddMember( bot );
            }
            eable.Free();

            return grp;
        }
    }
}
