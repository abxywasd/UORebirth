using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
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
        public bool       RoutingOnly; // graph hop only, never a bot destination
        public bool       FromXml;     // loaded from NavGraph.xml (cleared on rebuild)
    }
    
    //Controls where bots choose to go and how they route there.
    //Used by the bot AI (PickDestination) to select a travel target, and by A* (ComputeRoute) to find a path to it.
    //WaypointTag — fine-grained, combinable flags: Town | PKHub, Mining | Town, Dungeon | LostLands | PKHub, etc. Used to match bot persona to appropriate destinations.
    //RoutingOnly flag — some nodes are pure infrastructure (never a destination).
    //Every node is a precise coordinate, not a zone.
    //~80+ entries: full world coverage — towns, dungeons, shrines, moongates, bridges, crossroads, cemeteries, mining camps, wilderness landmarks, T2A entrances.
    //Extended at runtime by XML routing nodes from NavGraph.xml.
    public static class PlayerBotNavigator
    {
        private static readonly Dictionary<string, BotWaypoint> s_Landmarks
            = new Dictionary<string, BotWaypoint>( StringComparer.OrdinalIgnoreCase );

        private static readonly Dictionary<string, List<string>> s_Edges
            = new Dictionary<string, List<string>>( StringComparer.OrdinalIgnoreCase );

        // Names of nodes that came from NavGraph.xml (cleared each BuildGraph call)
        private static readonly List<string> s_XmlNodes
            = new List<string>();

        // Public read-only views for NavBuildCommand
        public static Dictionary<string, BotWaypoint> Landmarks { get { return s_Landmarks; } }
        public static Dictionary<string, List<string>> Edges     { get { return s_Edges; } }

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
            Add( "SerpentsHold",     new Point3D( 3023, 3413, 15 ), Map.Felucca, WaypointTag.Town );
            Add( "SkaraBrae",        new Point3D(  576, 2200,  0 ), Map.Felucca, WaypointTag.Town );
            Add( "Trinsic",          new Point3D( 1927, 2779,  0 ), Map.Felucca, WaypointTag.Town );
            Add( "TrinsicSouth",     new Point3D( 2002, 2929,  0 ), Map.Felucca, WaypointTag.Town );
            Add( "VesperWest",       new Point3D( 2761,  972,  0 ), Map.Felucca, WaypointTag.Town );
            Add( "VesperNorth",      new Point3D( 2907,  606,  0 ), Map.Felucca, WaypointTag.Town );
            Add( "VesperDocks",      new Point3D( 3042,  828,  -3), Map.Felucca, WaypointTag.Town );
            Add( "Wind",             new Point3D( 5251,  134, 20 ), Map.Felucca, WaypointTag.Town | WaypointTag.LostLands );
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
            Add( "ShrineSpirituality", new Point3D( 1602, 2490,  5 ), Map.Felucca, WaypointTag.Shrine );
            Add( "ShrineValor",        new Point3D( 2496, 3933,  0 ), Map.Felucca, WaypointTag.Shrine );

            // Notable overworld landmarks
            Add( "EmpathAbbey",      new Point3D(  635,  860,  0 ), Map.Felucca, WaypointTag.Notable );
            Add( "BlackthornCastle", new Point3D( 1523, 1456, 15 ), Map.Felucca, WaypointTag.Notable );
            Add( "BritishCastle",    new Point3D( 1401, 1625, 28 ), Map.Felucca, WaypointTag.Notable );
            Add( "Lycaeum",          new Point3D( 4312, 1004,  0 ), Map.Felucca, WaypointTag.Notable );
            // This shid is right in the middle of the sea
            //Add( "SerpentPillarN",   new Point3D( 2986, 2887,  0 ), Map.Felucca, WaypointTag.Notable );
            //
            Add( "BrigandCamp",      new Point3D(  885, 1682,  0 ), Map.Felucca, WaypointTag.Notable | WaypointTag.Wilderness | WaypointTag.PKHub );
            Add( "YewFortDamned",    new Point3D(  972,  768,  0 ), Map.Felucca, WaypointTag.Notable | WaypointTag.Wilderness | WaypointTag.PKHub );
            Add( "OrcFortYew",       new Point3D(  633, 1499,  0 ), Map.Felucca, WaypointTag.Notable | WaypointTag.Wilderness );
            Add( "OrcFortCove",      new Point3D( 2171, 1372,  0 ), Map.Felucca, WaypointTag.Notable | WaypointTag.Wilderness );
            Add( "SwampRuinsWest",   new Point3D( 1824, 2414,  0 ), Map.Felucca, WaypointTag.Notable | WaypointTag.Wilderness |
            WaypointTag.PKHub );

            // Mining / crafting areas
            Add( "MinocMiningCamp",  new Point3D( 2583,  528, 15 ), Map.Felucca, WaypointTag.Mining | WaypointTag.Town );
            Add( "MinocNorth",       new Point3D( 2475,  417, 15 ), Map.Felucca, WaypointTag.Mining | WaypointTag.Town );
            Add( "MinocGypsyCamp",   new Point3D( 2540,  651,  0 ), Map.Felucca, WaypointTag.Mining | WaypointTag.Town );
            Add( "EastMines",        new Point3D( 2572,  480,  0 ), Map.Felucca, WaypointTag.Mining );
            Add( "BritSmithGuild",   new Point3D( 1350, 1778,  15 ), Map.Felucca, WaypointTag.Mining );

            // Cemeteries
            Add( "BritainCemetery",  new Point3D( 1384, 1497, 10 ), Map.Felucca, WaypointTag.Cemetery );
            Add( "VesperCemetery",   new Point3D( 2786,  867,  0 ), Map.Felucca, WaypointTag.Cemetery );
            Add( "YewCemetery",      new Point3D(  724, 1138,  0 ), Map.Felucca, WaypointTag.Cemetery );
            Add( "JhelomCemetery",   new Point3D( 1296, 3719,  0 ), Map.Felucca, WaypointTag.Cemetery );
            Add( "MoonglewCemetery", new Point3D( 4546, 1338,  8 ), Map.Felucca, WaypointTag.Cemetery );

            // Moongates
            Add( "MoongateBrit",      new Point3D( 1336, 1997,  5 ), Map.Felucca, WaypointTag.Notable | WaypointTag.Wilderness );
            Add( "MoongateJhelom",    new Point3D( 1499, 3771,  5 ), Map.Felucca, WaypointTag.Notable | WaypointTag.Wilderness );
            Add( "MoongateMagincia",  new Point3D( 3563, 2139, 34 ), Map.Felucca, WaypointTag.Notable | WaypointTag.Wilderness );
            Add( "MoongateMinoc",     new Point3D( 2701,  692,  5 ), Map.Felucca, WaypointTag.Notable | WaypointTag.Wilderness );
            Add( "MoongateMoonglow",  new Point3D( 4467, 1283,  5 ), Map.Felucca, WaypointTag.Notable | WaypointTag.Wilderness );
            Add( "MoongateSkara",     new Point3D(  643, 2067,  5 ), Map.Felucca, WaypointTag.Notable | WaypointTag.Wilderness );
            Add( "MoongateTrinsic",   new Point3D( 1828, 2948, -20 ), Map.Felucca, WaypointTag.Notable | WaypointTag.Wilderness );
            Add( "MoongateVesper",    new Point3D( 2701,  692,  5 ), Map.Felucca, WaypointTag.Notable | WaypointTag.Wilderness );
            Add( "MoongateYew",       new Point3D(  771,  752,  5 ), Map.Felucca, WaypointTag.Notable | WaypointTag.Wilderness );

            // Strategic Bridges & Crossroads
            Add( "MinocVesperBridge", new Point3D( 2822,  702,  0 ), Map.Felucca, WaypointTag.Notable | WaypointTag.Mining );
            Add( "BritTrinsicXroad",  new Point3D( 1388, 1892,  0 ), Map.Felucca, WaypointTag.Notable | WaypointTag.Wilderness );
            Add( "YewPawPath",        new Point3D(  997, 1117,  0 ), Map.Felucca, WaypointTag.Notable | WaypointTag.Wilderness );
            Add( "GreatNorthernRoad", new Point3D( 1529, 873,  0 ), Map.Felucca, WaypointTag.Notable | WaypointTag.Wilderness );
            Add( "MtKendallPass",     new Point3D( 2440, 1006,  0 ), Map.Felucca, WaypointTag.Notable | WaypointTag.Wilderness );

            // T2A Overworld Entrances
            Add( "MarblePassage",     new Point3D( 1957, 2072,  0 ), Map.Felucca, WaypointTag.Notable | WaypointTag.LostLands );
            Add( "DeluciaPassage",    new Point3D( 1629, 3321,  0 ), Map.Felucca, WaypointTag.Notable | WaypointTag.LostLands );
            // Todo: investigate Snake Pass
            Add( "SnakePass",         new Point3D( 1700, 1440,  0 ), Map.Felucca, WaypointTag.Notable | WaypointTag.LostLands );
            Add( "FireIslandEntrance",new Point3D( 2923, 3406,  8 ), Map.Felucca, WaypointTag.Notable | WaypointTag.LostLands );
            //Add( "SerpentPillarS",    new Point3D( 1475, 2987,  0 ), Map.Felucca, WaypointTag.Notable | WaypointTag.LostLands );


            // Wilderness Landmarks
            Add( "HedgeMaze",         new Point3D( 1108, 2300,  0 ), Map.Felucca, WaypointTag.Notable | WaypointTag.Wilderness );
            Add( "IversVictory",      new Point3D( 3720, 2072,  5 ), Map.Felucca, WaypointTag.Notable | WaypointTag.Wilderness );
            // Todo: investigate where is the waterfall
            //Add( "GreatWaterfall",    new Point3D(  178,  828,  0 ), Map.Felucca, WaypointTag.Notable | WaypointTag.Wilderness );
            Add( "DesertCompassion",  new Point3D( 1857, 873,  -1 ), Map.Felucca, WaypointTag.Wilderness | WaypointTag.PKHub );
            // Todo: investigage
            //Add( "StoneCircleYew",    new Point3D(  640,  710,  0 ), Map.Felucca, WaypointTag.Notable | WaypointTag.Wilderness );
            // Todo: investigate
            //Add( "ValorsEnd",         new Point3D( 2390, 3175,  0 ), Map.Felucca, WaypointTag.Notable | WaypointTag.Wilderness );
            Add( "RuinedManor",        new Point3D( 854, 1544, 0), Map.Felucca, WaypointTag.Wilderness | WaypointTag.PKHub);


            // Ruins & Minor Camps
            Add( "Occllo",            new Point3D( 3640, 2528,  0 ), Map.Felucca, WaypointTag.Town );
            // Todo: investigate
            //Add( "GazerIsland",       new Point3D( 4200, 3600,  0 ), Map.Felucca, WaypointTag.Wilderness | WaypointTag.PKHub );
            Add( "HiddenValley",      new Point3D( 1675, 2950,  0 ), Map.Felucca, WaypointTag.Notable | WaypointTag.Wilderness );
            // Todo: investigate
            //Add( "SavageCampT2A",     new Point3D( 5240, 3350,  0 ), Map.Felucca, WaypointTag.Wilderness | WaypointTag.LostLands );

            // Woodcutting & Specialized Mining
            Add( "YewLumberRegion",   new Point3D(  600, 1000,  0 ), Map.Felucca, WaypointTag.Mining );
            Add( "BigMountainMine",   new Point3D( 2429, 177,  0 ), Map.Felucca, WaypointTag.Mining );
            // THANKS CHATGPT FOR THIS
            // No mines in Vesper wtf
            //Add( "VesperMine",        new Point3D( 2900,  900,  0 ), Map.Felucca, WaypointTag.Mining );
            // No mines in Occllo wtf
            //Add( "OclloMines",        new Point3D( 3660, 2600,  0 ), Map.Felucca, WaypointTag.Mining | WaypointTag.Town );

            // More Cemeteries
            // No graveyard in Trinsic wtf
            //Add( "TrinsicCemetery",   new Point3D( 1823, 2883,  0 ), Map.Felucca, WaypointTag.Cemetery );
            // No graveyard in Nujel'm wtf
            //Add( "NujelmCemetery",    new Point3D( 3557, 1262,  0 ), Map.Felucca, WaypointTag.Cemetery );

            // Todo: New Implemented POIs to wire into the graph
            Add( "VesperRuin",       new Point3D( 2582, 1120,  0 ), Map.Felucca, WaypointTag.Wilderness | WaypointTag.PKHub );
            Add( "CoveCemetery",     new Point3D( 2444, 1120,  8 ), Map.Felucca, WaypointTag.Cemetery | WaypointTag.PKHub );
            Add( "CompassionBridge", new Point3D( 1866, 749,   0 ), Map.Felucca, WaypointTag.Wilderness | WaypointTag.PKHub );
            //
            BuildGraph();
        }

        // ── Graph building ────────────────────────────────────────────────────────

        public static void BuildGraph()
        {
            // Remove nodes that were loaded from XML in a previous call
            foreach ( string name in s_XmlNodes )
                s_Landmarks.Remove( name );
            s_XmlNodes.Clear();
            s_Edges.Clear();

            string path = Path.Combine( Core.BaseDirectory, "Data", "NavGraph.xml" );
            if ( !File.Exists( path ) )
            {
                Console.WriteLine( "NavGraph: Data/NavGraph.xml not found — graph has no edges." );
                return;
            }

            try
            {
                var doc = new XmlDocument();
                doc.Load( path );

                // Load routing nodes from XML (skip any that conflict with hardcoded names)
                XmlNodeList nodes = doc.SelectNodes( "NavGraph/Nodes/Node" );
                if ( nodes != null )
                {
                    foreach ( XmlElement el in nodes )
                    {
                        string name = el.GetAttribute( "name" );
                        if ( string.IsNullOrEmpty( name ) ) continue;
                        if ( s_Landmarks.ContainsKey( name ) ) continue; // hardcoded wins

                        int x = int.Parse( el.GetAttribute( "x" ) );
                        int y = int.Parse( el.GetAttribute( "y" ) );
                        int z = int.Parse( el.GetAttribute( "z" ) );

                        string mapStr = el.GetAttribute( "map" );
                        Map map = string.Equals( mapStr, "Trammel", StringComparison.OrdinalIgnoreCase )
                            ? Map.Trammel : Map.Felucca;

                        string tagStr   = el.GetAttribute( "tags" );
                        WaypointTag tags = WaypointTag.None;
                        if ( !string.IsNullOrEmpty( tagStr ) )
                            tags = ParseTags( tagStr );

                        bool routing = string.Equals( el.GetAttribute( "routing" ), "true",
                            StringComparison.OrdinalIgnoreCase );

                        s_Landmarks[name] = new BotWaypoint
                        {
                            Location    = new Point3D( x, y, z ),
                            Map         = map,
                            Name        = name,
                            Tags        = tags,
                            RoutingOnly = routing,
                            FromXml     = true
                        };
                        s_XmlNodes.Add( name );
                    }
                }

                // Load edges (bidirectional)
                XmlNodeList edges = doc.SelectNodes( "NavGraph/Edges/Edge" );
                if ( edges != null )
                {
                    foreach ( XmlElement el in edges )
                    {
                        string a = el.GetAttribute( "a" );
                        string b = el.GetAttribute( "b" );
                        if ( string.IsNullOrEmpty( a ) || string.IsNullOrEmpty( b ) ) continue;
                        AddEdge( a, b );
                    }
                }

                Console.WriteLine( "NavGraph: Loaded {0} nodes ({1} routing), {2} edge entries from NavGraph.xml",
                    s_Landmarks.Count, s_XmlNodes.Count, s_Edges.Count );
            }
            catch ( Exception ex )
            {
                Console.WriteLine( "NavGraph: Failed to load NavGraph.xml: {0}", ex.Message );
            }
        }

        private static void AddEdge( string a, string b )
        {
            List<string> listA;
            if ( !s_Edges.TryGetValue( a, out listA ) )
                s_Edges[a] = listA = new List<string>();
            if ( !listA.Contains( b ) )
                listA.Add( b );

            List<string> listB;
            if ( !s_Edges.TryGetValue( b, out listB ) )
                s_Edges[b] = listB = new List<string>();
            if ( !listB.Contains( a ) )
                listB.Add( a );
        }

        private static WaypointTag ParseTags( string tagStr )
        {
            WaypointTag result = WaypointTag.None;
            foreach ( string part in tagStr.Split( ',' ) )
            {
                try
                {
                    WaypointTag t = (WaypointTag)Enum.Parse( typeof( WaypointTag ), part.Trim(), true );
                    result |= t;
                }
                catch { }
            }
            return result;
        }

        // ── A* Route computation ──────────────────────────────────────────────────

        // Returns ordered hop list ending at destination.
        // Returns null if no graph path exists — caller uses SetTravelDirect().
        public static List<BotWaypoint> ComputeRoute( Point3D start, Map map, BotWaypoint destination )
        {
            if ( destination == null ) return null;

            BotWaypoint startNode = NearestNode( start, map, 600.0 * 600.0 );
            if ( startNode == null ) return null;

            // Already at or adjacent to the destination node
            if ( string.Equals( startNode.Name, destination.Name, StringComparison.OrdinalIgnoreCase ) )
                return new List<BotWaypoint> { destination };

            var open     = new List<string> { startNode.Name };
            var cameFrom = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
            var gScore   = new Dictionary<string, double>( StringComparer.OrdinalIgnoreCase ) { { startNode.Name, 0.0 } };
            var fScore   = new Dictionary<string, double>( StringComparer.OrdinalIgnoreCase )
                { { startNode.Name, Heuristic( startNode, destination ) } };

            while ( open.Count > 0 )
            {
                string current = LowestF( open, fScore );

                if ( string.Equals( current, destination.Name, StringComparison.OrdinalIgnoreCase ) )
                    return ReconstructPath( cameFrom, current );

                open.Remove( current );

                List<string> neighbors;
                if ( !s_Edges.TryGetValue( current, out neighbors ) )
                    continue;

                BotWaypoint curWp = GetLandmark( current );
                if ( curWp == null ) continue;

                foreach ( string neighborName in neighbors )
                {
                    BotWaypoint nb = GetLandmark( neighborName );
                    if ( nb == null ) continue;
                    if ( nb.Map != map ) continue;

                    double tentG = gScore[current] + Heuristic( curWp, nb );

                    double existingG;
                    if ( !gScore.TryGetValue( neighborName, out existingG ) || tentG < existingG )
                    {
                        cameFrom[neighborName] = current;
                        gScore[neighborName]   = tentG;
                        fScore[neighborName]   = tentG + Heuristic( nb, destination );
                        if ( !open.Contains( neighborName ) )
                            open.Add( neighborName );
                    }
                }
            }

            return null; // no path — caller falls back to direct travel
        }

        private static BotWaypoint NearestNode( Point3D pos, Map map, double maxDistSq )
        {
            BotWaypoint best    = null;
            double      bestDsq = maxDistSq;

            foreach ( BotWaypoint wp in s_Landmarks.Values )
            {
                if ( wp.Map != map ) continue;
                double dx  = wp.Location.X - pos.X;
                double dy  = wp.Location.Y - pos.Y;
                double dsq = dx*dx + dy*dy;
                if ( dsq < bestDsq ) { bestDsq = dsq; best = wp; }
            }

            return best;
        }

        private static double Heuristic( BotWaypoint a, BotWaypoint b )
        {
            double dx = a.Location.X - b.Location.X;
            double dy = a.Location.Y - b.Location.Y;
            return Math.Sqrt( dx*dx + dy*dy );
        }

        private static string LowestF( List<string> open, Dictionary<string, double> fScore )
        {
            string best  = open[0];
            double bestF = fScore.ContainsKey( best ) ? fScore[best] : double.MaxValue;
            for ( int i = 1; i < open.Count; i++ )
            {
                string n = open[i];
                double f = fScore.ContainsKey( n ) ? fScore[n] : double.MaxValue;
                if ( f < bestF ) { bestF = f; best = n; }
            }
            return best;
        }

        private static List<BotWaypoint> ReconstructPath( Dictionary<string, string> cameFrom, string current )
        {
            var names = new List<string> { current };
            while ( cameFrom.ContainsKey( current ) )
            {
                current = cameFrom[current];
                names.Insert( 0, current );
            }

            // Drop index 0 (startNode — bot is already there), convert rest to BotWaypoint
            var result = new List<BotWaypoint>();
            for ( int i = 1; i < names.Count; i++ )
            {
                BotWaypoint wp = GetLandmark( names[i] );
                if ( wp != null ) result.Add( wp );
            }
            return result;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

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
            foreach ( BotWaypoint wp in s_Landmarks.Values )
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
                    string[] dungeon  = { "Deceit", "Despise", "Destard", "Covetous", "Shame", "Wrong",
                                          "Hythloth", "OrcCave", "FireBrit", "IceBrit", "TerathanKeep" };
                    string[] overland = { "EmpathAbbey", "BlackthornCastle", "BrigandCamp", "SerpentsHold",
                                          "Lycaeum", "OrcFortYew", "OrcFortCove", "SerpentPillarN", "YewFortDamned" };
                    string[] shrine   = { "ShrineCompassion", "ShrineHonesty", "ShrineJustice", "ShrineSacrifice",
                                          "ShrineSpirituality", "ShrineValor", "ShrineHumility", "ShrineHonor", "ShrineChaos" };
                    string[] town     = { "Britain", "Moonglow", "Vesper", "Jhelom", "SerpentsHold", "SkaraBrae" };
                    int roll = Utility.Random( 20 );
                    if      ( roll <  8 ) key = dungeon [Utility.Random( dungeon .Length )];
                    else if ( roll < 15 ) key = overland[Utility.Random( overland.Length )];
                    else if ( roll < 18 ) key = shrine  [Utility.Random( shrine  .Length )];
                    else                  key = town    [Utility.Random( town    .Length )];
                    break;
                }
            }

            BotWaypoint wp = GetLandmark( key );

            // Never return routing-only nodes as destinations
            if ( wp != null && wp.RoutingOnly )
                return null;

            return wp;
        }
    }
}
