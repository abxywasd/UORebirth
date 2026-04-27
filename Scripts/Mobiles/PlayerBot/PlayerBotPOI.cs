using System;
using System.Collections.Generic;
using Server;

namespace Server.Mobiles
{
    public enum BotPOIType
    {
        Town,
        DungeonEntrance,
        Crossroads
    }

    public class BotPOI
    {
        public string     Name;
        public Point3D    Location;
        public Map        Map;
        public BotPOIType Type;
        public int        MaxBots;
        public int        SpawnRadius;

        public BotPOI( string name, Point3D loc, Map map, BotPOIType type, int maxBots, int radius )
        {
            Name        = name;
            Location    = loc;
            Map         = map;
            Type        = type;
            MaxBots     = maxBots;
            SpawnRadius = radius;
        }
    }

    public static class PlayerBotPOI
    {
        public static readonly List<BotPOI> All = new List<BotPOI>();

        static PlayerBotPOI()
        {
            // Towns — coordinates match PlayerBotNavigator landmarks
            Add( "Britain",            new Point3D( 1495, 1629, 10 ), Map.Felucca, BotPOIType.Town,           6, 30 );
            Add( "Trinsic",            new Point3D( 1907, 2760,  0 ), Map.Felucca, BotPOIType.Town,           4, 25 );
            Add( "Vesper",             new Point3D( 2970,  688,  0 ), Map.Felucca, BotPOIType.Town,           3, 20 );
            Add( "Minoc",              new Point3D( 2505,  544,  0 ), Map.Felucca, BotPOIType.Town,           3, 20 );
            Add( "Yew",                new Point3D(  546,  992,  0 ), Map.Felucca, BotPOIType.Town,           3, 20 );
            Add( "Cove",               new Point3D( 2259, 1206,  0 ), Map.Felucca, BotPOIType.Town,           2, 15 );
            Add( "Skara Brae",         new Point3D(  614, 2210,  0 ), Map.Felucca, BotPOIType.Town,           3, 20 );
            Add( "Moonglow",           new Point3D( 4442, 1172,  0 ), Map.Felucca, BotPOIType.Town,           3, 20 );
            Add( "Magincia",           new Point3D( 3714, 2220, 20 ), Map.Felucca, BotPOIType.Town,           2, 20 );
            Add( "Ocllo",              new Point3D( 3650, 2519,  0 ), Map.Felucca, BotPOIType.Town,           2, 15 );
            Add( "Jhelom",             new Point3D( 1388, 3762,  0 ), Map.Felucca, BotPOIType.Town,           2, 15 );
            Add( "Nujel'm",            new Point3D( 3636, 1198,  0 ), Map.Felucca, BotPOIType.Town,           2, 15 );

            // Dungeon entrances
            Add( "Covetous entrance",  new Point3D( 2499,  918,  0 ), Map.Felucca, BotPOIType.DungeonEntrance, 2, 15 );
            Add( "Deceit entrance",    new Point3D( 1380, 1014,  0 ), Map.Felucca, BotPOIType.DungeonEntrance, 2, 15 );
            Add( "Despise entrance",   new Point3D( 1298, 1080,  0 ), Map.Felucca, BotPOIType.DungeonEntrance, 2, 15 );
            Add( "Destard entrance",   new Point3D( 1176, 2640,  0 ), Map.Felucca, BotPOIType.DungeonEntrance, 2, 15 );
            Add( "Hythloth entrance",  new Point3D( 1473, 3502,  0 ), Map.Felucca, BotPOIType.DungeonEntrance, 2, 15 );
            Add( "Shame entrance",     new Point3D(  512, 1559,  0 ), Map.Felucca, BotPOIType.DungeonEntrance, 2, 15 );
            Add( "Wrong entrance",     new Point3D( 2043,  236, 13 ), Map.Felucca, BotPOIType.DungeonEntrance, 2, 15 );
            Add( "Orc Fort",           new Point3D( 2429, 1380,  0 ), Map.Felucca, BotPOIType.Crossroads,     2, 15 );

            // Road waypoints
            Add( "South Britain road", new Point3D( 1660, 2100,  0 ), Map.Felucca, BotPOIType.Crossroads,     2, 20 );
            Add( "East Britain road",  new Point3D( 2100, 1300,  0 ), Map.Felucca, BotPOIType.Crossroads,     2, 20 );
            Add( "West crossroads",    new Point3D(  900, 1700,  0 ), Map.Felucca, BotPOIType.Crossroads,     2, 20 );
        }

        private static void Add( string name, Point3D loc, Map map, BotPOIType type, int maxBots, int radius )
        {
            All.Add( new BotPOI( name, loc, map, type, maxBots, radius ) );
        }

        // Returns POIs that have fewer live bots than their MaxBots target.
        public static List<BotPOI> GetUnderpopulated()
        {
            var result = new List<BotPOI>();
            foreach ( BotPOI poi in All )
            {
                if ( CountBotsNear( poi ) < poi.MaxBots )
                    result.Add( poi );
            }
            return result;
        }

        public static int CountBotsNear( BotPOI poi )
        {
            int count = 0;
            IPooledEnumerable eable = poi.Map.GetMobilesInRange( poi.Location, poi.SpawnRadius );
            foreach ( Mobile m in eable )
            {
                if ( m is PlayerBot && !m.Deleted && !((PlayerBot)m).Controled )
                    count++;
            }
            eable.Free();
            return count;
        }

        public static Point3D? RandomSpawnPoint( BotPOI poi )
        {
            for ( int tries = 0; tries < 15; tries++ )
            {
                int x = poi.Location.X + Utility.RandomMinMax( -poi.SpawnRadius, poi.SpawnRadius );
                int y = poi.Location.Y + Utility.RandomMinMax( -poi.SpawnRadius, poi.SpawnRadius );
                int z = poi.Map.GetAverageZ( x, y );
                Point3D p = new Point3D( x, y, z );
                if ( poi.Map.CanSpawnMobile( p ) )
                    return p;
            }

            // Last resort: try the POI center itself
            int fz = poi.Map.GetAverageZ( poi.Location.X, poi.Location.Y );
            Point3D center = new Point3D( poi.Location.X, poi.Location.Y, fz );
            return poi.Map.CanSpawnMobile( center ) ? center : (Point3D?)null;
        }
    }
}
