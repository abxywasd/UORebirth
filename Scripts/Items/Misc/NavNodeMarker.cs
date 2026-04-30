using System;
using Server;
using Server.Network;

namespace Server.Items
{
    public class NavNodeMarker : Item
    {
        private string m_NodeName;

        public string NodeName { get { return m_NodeName; } }

        public NavNodeMarker( string nodeName ) : base( 0xF22 )
        {
            m_NodeName = nodeName;
            Name       = "Nav: " + nodeName;
            Hue        = 0x21;
            Movable    = false;
            Timer.DelayCall( TimeSpan.FromSeconds( 60.0 ), Delete );
        }

        public NavNodeMarker( Serial serial ) : base( serial )
        {
        }

        public override void OnSingleClick( Mobile from )
        {
            from.SendMessage( 0x21, "Nav node: " + m_NodeName );
        }

        public override void Serialize( GenericWriter writer )
        {
            base.Serialize( writer );
            writer.Write( (int)0 );
        }

        public override void Deserialize( GenericReader reader )
        {
            base.Deserialize( reader );
            reader.ReadInt();
            Timer.DelayCall( TimeSpan.Zero, Delete );
        }
    }
}
