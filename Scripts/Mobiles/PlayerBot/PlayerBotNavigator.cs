using System;
using System.Collections.Generic;
using Server;

namespace Server.Mobiles
{
    [Flags]
    public enum WaypointTag
    {
        None       = 0,
        Town       = 1 << 0,
        Dungeon    = 1 << 1,
        Shrine     = 1 << 2,
        Notable    = 1 << 3,
        Mining     = 1 << 4,
        Cemetery   = 1 << 5,
        Wilderness = 1 << 6,
        LostLands  = 1 << 7,
        PKHub      = 1 << 8,
    }

    public class BotWaypoint
    {
        public Point3D    Location;
        public Map        Map;
        public string     Name;
        public WaypointTag Tags;
    }

    public static class PlayerBotNavigator
    {
        private static readonly Dictionary<string, BotWaypoint> s_Landmarks
            = new Dictionary<string, BotWaypoint>( StringComparer.OrdinalIgnoreCase );

        public static void Initialize()
        {
            // Towns
            Add( "Britain",          new Point3D( 1475, 1645, 20 ), Map.Felucca, WaypointTag.Town );
            Add( "BuccaneersDen",    new Point3D( 2720, 2110,  0 ), Map.Felucca, WaypointTag.Town | WaypointTag.PKHub );
            Add( "Cove",             new Point3D( 2263, 1237,  0 ), Map.Felucca, WaypointTag.Town );
            Add( "Jhelom",           new Point3D( 1388, 3762,  0 ), Map.Felucca, WaypointTag.Town );
            Add( "Magincia",         new Point3D( 3714, 2235, 20 ), Map.Felucca, WaypointTag.Town );
            Add( "Minoc",            new Point3D( 2475,  417, 15 ), Map.Felucca, WaypointTag.Town );
            Add( "Moonglow",         new Point3D( 4442, 1122,  5 ), Map.Felucca, WaypointTag.Town );
            Add( "NujelM",           new Point3D( 3636, 1198,  0 ), Map.Felucca, WaypointTag.Town );
            Add( "Ocllo",            new Point3D( 3650, 2516,  0 ), Map.Felucca, WaypointTag.Town );
            Add( "Delucia",          new Point3D( 5228, 3978, 37 ), Map.Felucca, WaypointTag.Town | WaypointTag.LostLands );
            Add( "Papua",            new Point3D( 5730, 3208, -4 ), Map.Felucca, WaypointTag.Town | WaypointTag.LostLands );
            Add( "SerpentsHold",     new Point3D( 3025, 3498, 10 ), Map.Felucca, WaypointTag.Town );
            Add( "SkaraBrae",        new Point3D(  576, 2200,  0 ), Map.Felucca, WaypointTag.Town );
            Add( "Trinsic",          new Point3D( 1927, 2779,  0 ), Map.Felucca, WaypointTag.Town );
            Add( "Vesper",           new Point3D( 2882,  788,  0 ), Map.Felucca, WaypointTag.Town );
            Add( "Wind",             new Point3D( 5252,  104, 15 ), Map.Felucca, WaypointTag.Town | WaypointTag.LostLands );
            Add( "Yew",              new Point3D(  535,  992,  0 ), Map.Felucca, WaypointTag.Town );

            // Dungeon entrances (overworld surface)
            Add( "Covetous",         new Point3D( 2499,  919,  0 ), Map.Felucca, WaypointTag.Dungeon );
            Add( "Deceit",           new Point3D( 4111,  432,  5 ), Map.Felucca, WaypointTag.Dungeon | WaypointTag.PKHub );
            Add( "Despise",          new Point3D( 1298, 1080,  0 ), Map.Felucca, WaypointTag.Dungeon | WaypointTag.PKHub );
            Add( "Destard",          new Point3D( 1176, 2637,  0 ), Map.Felucca, WaypointTag.Dungeon );
            Add( "Hythloth",         new Point3D( 4722, 3814,  0 ), Map.Felucca, WaypointTag.Dungeon );
            Add( "Shame",            new Point3D(  514, 1561,  0 ), Map.Felucca, WaypointTag.Dungeon );
            Add( "Wrong",            new Point3D( 2043,  238, 10 ), Map.Felucca, WaypointTag.Dungeon | WaypointTag.PKHub );
            Add( "OrcCave",          new Point3D( 1019, 1431,  0 ), Map.Felucca, WaypointTag.Dungeon | WaypointTag.Wilderness );
            Add( "FireBrit",         new Point3D( 2923, 3407,  8 ), Map.Felucca, WaypointTag.Dungeon );
            Add( "IceBrit",          new Point3D( 1999,   81,  4 ), Map.Felucca, WaypointTag.Dungeon );
            Add( "TerathanKeep",     new Point3D( 5451, 3143,-60 ), Map.Felucca, WaypointTag.Dungeon | WaypointTag.LostLands );

            // Virtue shrines
            Add( "ShrineChaos",        new Point3D( 1458,  844,  0 ), Map.Felucca, WaypointTag.Shrine );
            Add( "ShrineCompassion",   new Point3D( 1858,  874, -1 ), Map.Felucca, WaypointTag.Shrine );
            Add( "ShrineHonesty",      new Point3D( 4217,  564, 36 ), Map.Felucca, WaypointTag.Shrine );
            Add( "ShrineHonor",        new Point3D( 1730, 3528,  3 ), Map.Felucca, WaypointTag.Shrine );
            Add( "ShrineHumility",     new Point3D( 4276, 3699,  0 ), Map.Felucca, WaypointTag.Shrine );
            Add( "ShrineJustice",      new Point3D( 1301,  639, 16 ), Map.Felucca, WaypointTag.Shrine );
            Add( "ShrineSacrifice",    new Point3D( 3355,  299,  9 ), Map.Felucca, WaypointTag.Shrine );
            Add( "ShrineSpirituality", new Point3D( 1595, 2490,  5 ), Map.Felucca, WaypointTag.Shrine );
            Add( "ShrineValor",        new Point3D( 2496, 3933,  0 ), Map.Felucca, WaypointTag.Shrine );

            // Notable overworld landmarks
            Add( "EmpathAbbey",      new Point3D(  635,  860,  0 ), Map.Felucca, WaypointTag.Notable );
            Add( "BlackthornCastle", new Point3D( 1523, 1456, 15 ), Map.Felucca, WaypointTag.Notable );
            Add( "BritishCastle",    new Point3D( 1401, 1625, 28 ), Map.Felucca, WaypointTag.Notable );
            Add( "Lycaeum",          new Point3D( 4312, 1000,  0 ), Map.Felucca, WaypointTag.Notable );
            Add( "SerpentPillarN",   new Point3D( 2986, 2887,  0 ), Map.Felucca, WaypointTag.Notable );
            Add( "BrigandCamp",      new Point3D(  885, 1682,  0 ), Map.Felucca, WaypointTag.Notable | WaypointTag.Wilderness | WaypointTag.PKHub );
            Add( "YewFortDamned",    new Point3D(  972,  768,  0 ), Map.Felucca, WaypointTag.Notable | WaypointTag.Wilderness | WaypointTag.PKHub );
            Add( "OrcFortYew",       new Point3D(  633, 1499,  0 ), Map.Felucca, WaypointTag.Notable | WaypointTag.Wilderness );
            Add( "OrcFortCove",      new Point3D( 2171, 1372,  0 ), Map.Felucca, WaypointTag.Notable | WaypointTag.Wilderness );

            // Mining / crafting areas
            Add( "MinocMiningCamp",  new Point3D( 2583,  528, 15 ), Map.Felucca, WaypointTag.Mining | WaypointTag.Town );
            Add( "MinocNorth",       new Point3D( 2475,  417, 15 ), Map.Felucca, WaypointTag.Mining | WaypointTag.Town );
            Add( "MinocGypsyCamp",   new Point3D( 2540,  651,  0 ), Map.Felucca, WaypointTag.Mining | WaypointTag.Town );
            Add( "EastMines",        new Point3D( 2587,  492,  0 ), Map.Felucca, WaypointTag.Mining );
            Add( "BritSmithGuild",   new Point3D( 1348, 1778,  0 ), Map.Felucca, WaypointTag.Mining );

            // Cemeteries
            Add( "BritainCemetery",  new Point3D( 1384, 1497, 10 ), Map.Felucca, WaypointTag.Cemetery );
            Add( "VesperCemetery",   new Point3D( 2786,  867,  0 ), Map.Felucca, WaypointTag.Cemetery );
            Add( "YewCemetery",      new Point3D(  724, 1138,  0 ), Map.Felucca, WaypointTag.Cemetery );
            Add( "JhelomCemetery",   new Point3D( 1296, 3719,  0 ), Map.Felucca, WaypointTag.Cemetery );
            Add( "MoonglewCemetery", new Point3D( 4546, 1338,  8 ), Map.Felucca, WaypointTag.Cemetery );
        }

        private static void Add( string name, Point3D loc, Map map )
        {
            Add( name, loc, map, WaypointTag.None );
        }

        private static void Add( string name, Point3D loc, Map map, WaypointTag tags )
        {
            if ( !s_Landmarks.ContainsKey( name ) )
                s_Landmarks[name] = new BotWaypoint { Location = loc, Map = map, Name = name, Tags = tags };
        }

        public static BotWaypoint GetLandmark( string name )
        {
            BotWaypoint wp;
            s_Landmarks.TryGetValue( name, out wp );
            return wp;
        }

        public static BotWaypoint PickByTag( WaypointTag mask )
        {
            var matches = new List<BotWaypoint>();
            foreach ( var wp in s_Landmarks.Values )
                if ( (wp.Tags & mask) != WaypointTag.None )
                    matches.Add( wp );
            return matches.Count > 0 ? matches[Utility.Random( matches.Count )] : null;
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
        // Lost Lands waypoints are gated by a per-profile probability roll;
        // up to 10 re-picks are attempted before accepting the result.
        public static BotWaypoint PickDestination( PlayerBotPersona.PlayerBotProfile profile )
        {
            BotWaypoint result;
            int attempts = 0;

            do
            {
                result = PickDestinationInternal( profile );
                attempts++;
            }
            while ( result != null
                    && (result.Tags & WaypointTag.LostLands) != WaypointTag.None
                    && !RollLostLands( profile )
                    && attempts < 10 );

            return result;
        }

        private static bool RollLostLands( PlayerBotPersona.PlayerBotProfile profile )
        {
            switch ( profile )
            {
                case PlayerBotPersona.PlayerBotProfile.Adventurer:   return Utility.Random( 5 )  == 0; // 20%
                case PlayerBotPersona.PlayerBotProfile.PlayerKiller: return Utility.Random( 10 ) == 0; // 10%
                default: return false; // Crafter never
            }
        }

        private static BotWaypoint PickDestinationInternal( PlayerBotPersona.PlayerBotProfile profile )
        {
            string key;

            switch ( profile )
            {
                case PlayerBotPersona.PlayerBotProfile.PlayerKiller:
                {
                    // 70% primary hunting/PK grounds, 30% social hub
                    string[] primary = { "Deceit", "Despise", "Wrong", "Covetous", "Shame", "Destard",
                                         "OrcFortYew", "OrcFortCove", "BrigandCamp", "YewFortDamned",
                                         "OrcCave", "FireBrit", "IceBrit" };
                    string[] social  = { "BuccaneersDen" };
                    key = Utility.Random( 10 ) < 7
                        ? primary[Utility.Random( primary.Length )]
                        : social [Utility.Random( social .Length )];
                    break;
                }

                case PlayerBotPersona.PlayerBotProfile.Crafter:
                {
                    // 60% workshop/supply runs, 40% town browsing
                    string[] workshop = { "MinocMiningCamp", "MinocNorth", "MinocGypsyCamp", "EastMines",
                                          "BritSmithGuild", "Britain", "Minoc", "Vesper", "Yew", "Trinsic" };
                    string[] browse   = { "Britain", "Vesper", "Magincia", "Moonglow", "SkaraBrae",
                                          "Trinsic", "Cove", "NujelM" };
                    key = Utility.Random( 10 ) < 6
                        ? workshop[Utility.Random( workshop.Length )]
                        : browse  [Utility.Random( browse  .Length )];
                    break;
                }

                default: // Adventurer
                {
                    // 40% dungeon, 35% overland, 15% shrine pilgrimage, 10% town
                    string[] dungeon  = { "Deceit", "Despise", "Destard", "Covetous", "Shame", "Wrong",
                                          "Hythloth", "OrcCave", "FireBrit", "IceBrit", "TerathanKeep" };
                    string[] overland = { "EmpathAbbey", "BlackthornCastle", "BrigandCamp", "SerpentsHold",
                                          "Lycaeum", "OrcFortYew", "OrcFortCove", "SerpentPillarN", "YewFortDamned" };
                    string[] shrine   = { "ShrineCompassion", "ShrineHonesty", "ShrineJustice", "ShrineSacrifice",
                                          "ShrineSpirituality", "ShrineValor", "ShrineHumility", "ShrineHonor", "ShrineChaos" };
                    string[] town     = { "Britain", "Moonglow", "Vesper", "Jhelom", "SerpentsHold", "SkaraBrae" };
                    int roll = Utility.Random( 20 );
                    if      ( roll <  8 ) key = dungeon [Utility.Random( dungeon .Length )]; // 40%
                    else if ( roll < 15 ) key = overland[Utility.Random( overland.Length )]; // 35%
                    else if ( roll < 18 ) key = shrine  [Utility.Random( shrine  .Length )]; // 15%
                    else                  key = town    [Utility.Random( town    .Length )]; // 10%
                    break;
                }
            }

            return GetLandmark( key );
        }
    }
}
