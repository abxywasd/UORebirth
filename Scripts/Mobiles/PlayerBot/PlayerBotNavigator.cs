using System;
using System.Collections.Generic;
using Server;

namespace Server.Mobiles
{
    public class BotWaypoint
    {
        public Point3D Location;
        public Map Map;
        public string Name;
    }

    public static class PlayerBotNavigator
    {
        private static readonly Dictionary<string, BotWaypoint> s_Landmarks
            = new Dictionary<string, BotWaypoint>( StringComparer.OrdinalIgnoreCase );

        public static void Initialize()
        {
            // Towns (Felucca) — coordinates from Data/Regions.xml <go> entries
            Add( "Britain",    new Point3D( 1495, 1629, 10 ), Map.Felucca );
            Add( "Cove",       new Point3D( 2259, 1206,  0 ), Map.Felucca );
            Add( "Minoc",      new Point3D( 2505,  544,  0 ), Map.Felucca );
            Add( "Trinsic",    new Point3D( 1907, 2760,  0 ), Map.Felucca );
            Add( "Vesper",     new Point3D( 2970,  688,  0 ), Map.Felucca );
            Add( "Yew",        new Point3D(  546,  992,  0 ), Map.Felucca );
            Add( "SkaraBrae",  new Point3D(  614, 2210,  0 ), Map.Felucca );
            Add( "Moonglow",   new Point3D( 4442, 1172,  0 ), Map.Felucca );
            Add( "Magincia",   new Point3D( 3714, 2220, 20 ), Map.Felucca );
            Add( "Ocllo",      new Point3D( 3650, 2519,  0 ), Map.Felucca );
            Add( "Jhelom",     new Point3D( 1388, 3762,  0 ), Map.Felucca );
            Add( "NujelM",     new Point3D( 3636, 1198,  0 ), Map.Felucca );

            // Dungeon entrances (overworld surface)
            Add( "Covetous",   new Point3D( 2499,  918,  0 ), Map.Felucca );
            Add( "Deceit",     new Point3D( 1380, 1014,  0 ), Map.Felucca );
            Add( "Despise",    new Point3D( 1298, 1080,  0 ), Map.Felucca );
            Add( "Destard",    new Point3D( 1176, 2640,  0 ), Map.Felucca );
            Add( "Hythloth",   new Point3D( 1473, 3502,  0 ), Map.Felucca );
            Add( "Shame",      new Point3D(  512, 1559,  0 ), Map.Felucca );
            Add( "Wrong",      new Point3D( 2043,  236, 13 ), Map.Felucca );
            Add( "OrcFort",    new Point3D( 2429, 1380,  0 ), Map.Felucca );
        }

        private static void Add( string name, Point3D loc, Map map )
        {
            if ( !s_Landmarks.ContainsKey( name ) )
                s_Landmarks[name] = new BotWaypoint { Location = loc, Map = map, Name = name };
        }

        public static BotWaypoint GetLandmark( string name )
        {
            BotWaypoint wp;
            s_Landmarks.TryGetValue( name, out wp );
            return wp;
        }

        // Called every AI tick while activity == Traveling.
        // Returns true when the bot has arrived within 5 tiles of dest.
        public static bool Advance( PlayerBotAI ai, PlayerBot bot, Point3D dest, Map destMap )
        {
            if ( bot.Map != destMap )
                return false;

            if ( bot.GetDistanceToSqrt( dest ) <= 5.0 )
                return true;

            return ai.MoveToPoint( dest, true, 5 );
        }

        // Pick a travel destination appropriate for the bot's profile.
        public static BotWaypoint PickDestination( PlayerBotPersona.PlayerBotProfile profile )
        {
            string[] pool;

            switch ( profile )
            {
                case PlayerBotPersona.PlayerBotProfile.PlayerKiller:
                    pool = new string[] { "Deceit", "Despise", "Covetous", "Wrong", "Britain", "OrcFort" };
                    break;
                case PlayerBotPersona.PlayerBotProfile.Crafter:
                    pool = new string[] { "Britain", "Minoc", "Vesper", "Cove", "Trinsic", "Yew" };
                    break;
                default: // Adventurer
                    pool = new string[] { "Deceit", "Destard", "Shame", "Hythloth", "Britain", "Moonglow", "Despise" };
                    break;
            }

            return GetLandmark( pool[Utility.Random( pool.Length )] );
        }
    }
}
