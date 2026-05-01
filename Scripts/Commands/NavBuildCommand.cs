using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using Server;
using Server.Commands;
using Server.Gumps;
using Server.Items;
using Server.Mobiles;
using Server.Targeting;

namespace Server.Scripts.Commands
{
    public static class NavBuildCommand
    {
        private static string NavGraphPath
        {
            get { return Path.Combine( Core.BaseDirectory, "Data", "NavGraph.xml" ); }
        }

        public static void Initialize()
        {
            CommandSystem.Register( "navbuild", AccessLevel.GameMaster, new CommandEventHandler( NavBuild_OnCommand ) );
            CommandSystem.Register( "navtest",  AccessLevel.GameMaster, new CommandEventHandler( NavTest_OnCommand ) );
            CommandSystem.Register( "navshow",  AccessLevel.GameMaster, new CommandEventHandler( NavShow_OnCommand ) );
            CommandSystem.Register( "navbot",   AccessLevel.GameMaster, new CommandEventHandler( NavBot_OnCommand ) );
            CommandSystem.Register( "navdrop",   AccessLevel.GameMaster, new CommandEventHandler( NavDrop_OnCommand ) );
            CommandSystem.Register( "navnearest", AccessLevel.GameMaster, new CommandEventHandler( NavNearest_OnCommand ) );
        }

        // ── [navbuild ─────────────────────────────────────────────────────────────
        [Usage( "navbuild <addnode|insert|connect|removeedge|edges|remove|show|rebuild|export|goto|move> [args]" )]
        [Description( "In-game nav graph authoring tool." )]
        private static void NavBuild_OnCommand( CommandEventArgs e )
        {
            if ( e.Length < 1 )
            {
                e.Mobile.SendMessage( "Usage: [navbuild <addnode|insert|connect|removeedge|edges|remove|show|rebuild|export|goto|move|trail>" );
                return;
            }

            string sub = e.GetString( 0 ).ToLower();

            switch ( sub )
            {
                case "addnode":    DoAddNode( e );    break;
                case "insert":     DoInsert( e );     break;
                case "connect":    DoConnect( e );    break;
                case "removeedge": DoRemoveEdge( e ); break;
                case "edges":      DoEdges( e );      break;
                case "remove":     DoRemove( e );     break;
                case "show":       DoShow( e );       break;
                case "rebuild":    DoRebuild( e );    break;
                case "export":     DoExport( e );     break;
                case "goto":       DoGoto( e );       break;
                case "move":       DoMove( e );       break;
                case "trail":      DoTrail( e );      break;
                case "nearest":    DoNearest( e );    break;
                case "isolated":   DoIsolated( e );   break;
                default:
                    e.Mobile.SendMessage( "Unknown subcommand '{0}'. Valid: addnode insert connect removeedge edges remove show rebuild export goto move trail nearest isolated", sub );
                    break;
            }
        }

        // ── addnode ───────────────────────────────────────────────────────────────
        private static void DoAddNode( CommandEventArgs e )
        {
            if ( e.Length < 2 )
            {
                e.Mobile.SendMessage( "Usage: [navbuild addnode <name> [routing]" );
                return;
            }

            string name    = e.GetString( 1 );
            bool routing   = e.Length >= 3 && e.GetString( 2 ).ToLower() == "routing";
            Mobile from    = e.Mobile;
            Point3D pos    = from.Location;
            Map map        = from.Map;
            string mapStr  = (map == Map.Trammel) ? "Trammel" : "Felucca";

            // Reject if name already exists
            if ( PlayerBotNavigator.GetLandmark( name ) != null )
            {
                from.SendMessage( 0x22, "A node named '{0}' already exists.", name );
                return;
            }

            // Write to XML
            XmlDocument doc = LoadOrCreateXml();
            XmlElement nodesEl = (XmlElement)doc.SelectSingleNode( "NavGraph/Nodes" );
            if ( nodesEl == null )
            {
                from.SendMessage( 0x22, "NavGraph.xml is malformed — missing <Nodes> element." );
                return;
            }

            XmlElement el = doc.CreateElement( "Node" );
            el.SetAttribute( "name", name );
            el.SetAttribute( "x",    pos.X.ToString() );
            el.SetAttribute( "y",    pos.Y.ToString() );
            el.SetAttribute( "z",    pos.Z.ToString() );
            el.SetAttribute( "map",  mapStr );
            if ( routing )
                el.SetAttribute( "routing", "true" );
            nodesEl.AppendChild( el );
            SaveXml( doc );

            // Rebuild in-memory graph
            PlayerBotNavigator.BuildGraph();

            // Spawn visual marker
            NavNodeMarker marker = new NavNodeMarker( name );
            marker.MoveToWorld( pos, map );

            int total = PlayerBotNavigator.Landmarks.Count;
            from.SendMessage( 0x55,
                "Added {0}node \"{1}\" at ({2},{3},{4}). Graph rebuilt ({5} nodes).",
                routing ? "routing " : "", name, pos.X, pos.Y, pos.Z, total );
        }

        // ── insert ── add intermediate node, remove old direct edge, wire both ends
        private static void DoInsert( CommandEventArgs e )
        {
            if ( e.Length < 4 )
            {
                e.Mobile.SendMessage( "Usage: [navbuild insert <newName> <nodeA> <nodeB>" );
                e.Mobile.SendMessage( "  Plants a routing node at your feet between nodeA and nodeB." );
                e.Mobile.SendMessage( "  Removes the direct edge A<->B and adds A<->new and new<->B." );
                return;
            }

            string newName = e.GetString( 1 );
            string nameA   = e.GetString( 2 );
            string nameB   = e.GetString( 3 );
            Mobile from    = e.Mobile;

            if ( PlayerBotNavigator.GetLandmark( newName ) != null )
            {
                from.SendMessage( 0x22, "A node named '{0}' already exists. Use [navbuild move {0} to reposition it.", newName );
                return;
            }

            BotWaypoint wpA = PlayerBotNavigator.GetLandmark( nameA );
            BotWaypoint wpB = PlayerBotNavigator.GetLandmark( nameB );
            if ( wpA == null ) { from.SendMessage( 0x22, "Node '{0}' not found.", nameA ); return; }
            if ( wpB == null ) { from.SendMessage( 0x22, "Node '{0}' not found.", nameB ); return; }

            Point3D pos   = from.Location;
            Map     map   = from.Map;
            string mapStr = (map == Map.Trammel) ? "Trammel" : "Felucca";

            XmlDocument doc = LoadOrCreateXml();

            // Add the new node element
            XmlElement nodesEl = (XmlElement)doc.SelectSingleNode( "NavGraph/Nodes" );
            if ( nodesEl == null ) { from.SendMessage( 0x22, "NavGraph.xml is malformed — missing <Nodes>." ); return; }

            XmlElement nodeEl = doc.CreateElement( "Node" );
            nodeEl.SetAttribute( "name",    newName );
            nodeEl.SetAttribute( "x",       pos.X.ToString() );
            nodeEl.SetAttribute( "y",       pos.Y.ToString() );
            nodeEl.SetAttribute( "z",       pos.Z.ToString() );
            nodeEl.SetAttribute( "map",     mapStr );
            nodeEl.SetAttribute( "routing", "true" );
            nodesEl.AppendChild( nodeEl );

            // Remove the direct A<->B edge if it exists
            XmlElement edgesEl = (XmlElement)doc.SelectSingleNode( "NavGraph/Edges" );
            if ( edgesEl != null )
            {
                XmlNodeList toRemove = doc.SelectNodes( string.Format(
                    "NavGraph/Edges/Edge[(@a='{0}' and @b='{1}') or (@a='{1}' and @b='{0}')]",
                    nameA, nameB ) );
                bool removedDirect = toRemove != null && toRemove.Count > 0;
                if ( toRemove != null )
                    foreach ( XmlNode edge in toRemove )
                        edge.ParentNode.RemoveChild( edge );

                // Add new edges: A<->new and new<->B
                XmlElement edgeAN = doc.CreateElement( "Edge" );
                edgeAN.SetAttribute( "a", nameA ); edgeAN.SetAttribute( "b", newName );
                edgesEl.AppendChild( edgeAN );

                XmlElement edgeNB = doc.CreateElement( "Edge" );
                edgeNB.SetAttribute( "a", newName ); edgeNB.SetAttribute( "b", nameB );
                edgesEl.AppendChild( edgeNB );

                SaveXml( doc );
                PlayerBotNavigator.BuildGraph();

                NavNodeMarker marker = new NavNodeMarker( newName );
                marker.MoveToWorld( pos, map );

                double dAN = Math.Sqrt( Math.Pow( wpA.Location.X - pos.X, 2 ) + Math.Pow( wpA.Location.Y - pos.Y, 2 ) );
                double dNB = Math.Sqrt( Math.Pow( pos.X - wpB.Location.X, 2 ) + Math.Pow( pos.Y - wpB.Location.Y, 2 ) );
                string warnA = dAN > 300 ? " [WARN: >300 tiles]" : "";
                string warnB = dNB > 300 ? " [WARN: >300 tiles]" : "";

                from.SendMessage( 0x55, "Inserted routing node \"{0}\" at ({1},{2},{3}).", newName, pos.X, pos.Y, pos.Z );
                if ( removedDirect )
                    from.SendMessage( 0x55, "  Removed direct edge {0}<->{1}.", nameA, nameB );
                else
                    from.SendMessage( 0x55, "  No direct edge {0}<->{1} existed (nothing removed).", nameA, nameB );
                from.SendMessage( 0x55, "  {0} <-> {1}: {2:F0} tiles{3}", nameA, newName, dAN, warnA );
                from.SendMessage( 0x55, "  {0} <-> {1}: {2:F0} tiles{3}", newName, nameB, dNB, warnB );
            }
        }

        // ── removeedge ────────────────────────────────────────────────────────────
        private static void DoRemoveEdge( CommandEventArgs e )
        {
            if ( e.Length < 3 )
            {
                e.Mobile.SendMessage( "Usage: [navbuild removeedge <nameA> <nameB>" );
                return;
            }

            string nameA = e.GetString( 1 );
            string nameB = e.GetString( 2 );
            Mobile from  = e.Mobile;

            XmlDocument doc = LoadOrCreateXml();
            XmlNodeList toRemove = doc.SelectNodes( string.Format(
                "NavGraph/Edges/Edge[(@a='{0}' and @b='{1}') or (@a='{1}' and @b='{0}')]",
                nameA, nameB ) );

            if ( toRemove == null || toRemove.Count == 0 )
            {
                from.SendMessage( 0x22, "No edge found between '{0}' and '{1}'.", nameA, nameB );
                return;
            }

            foreach ( XmlNode edge in toRemove )
                edge.ParentNode.RemoveChild( edge );

            SaveXml( doc );
            PlayerBotNavigator.BuildGraph();

            from.SendMessage( 0x55, "Removed edge {0} <-> {1}. Graph rebuilt.", nameA, nameB );
        }

        // ── edges ── list all connections for a node ──────────────────────────────
        private static void DoEdges( CommandEventArgs e )
        {
            if ( e.Length < 2 )
            {
                e.Mobile.SendMessage( "Usage: [navbuild edges <name>" );
                return;
            }

            string name = e.GetString( 1 );
            Mobile from = e.Mobile;

            BotWaypoint wp = PlayerBotNavigator.GetLandmark( name );
            if ( wp == null )
            {
                from.SendMessage( 0x22, "Node '{0}' not found.", name );
                return;
            }

            List<string> neighbors;
            if ( !PlayerBotNavigator.Edges.TryGetValue( name, out neighbors ) || neighbors.Count == 0 )
            {
                from.SendMessage( 0x55, "Node '{0}' has no edges — it is isolated.", name );
                return;
            }

            var rows = new List<NavNodeListGump.NavRow>();
            foreach ( string neighbor in neighbors )
            {
                BotWaypoint nb = PlayerBotNavigator.GetLandmark( neighbor );
                if ( nb == null ) continue;

                double dx   = wp.Location.X - nb.Location.X;
                double dy   = wp.Location.Y - nb.Location.Y;
                double dist = Math.Sqrt( dx*dx + dy*dy );
                string note = dist > 300
                    ? string.Format( "{0:F0}t WARN", dist )
                    : string.Format( "{0:F0} tiles", dist );

                rows.Add( new NavNodeListGump.NavRow
                {
                    Name     = neighbor,
                    Note     = note,
                    Location = nb.Location,
                    Map      = nb.Map
                } );
            }

            string title = string.Format( "{0} — {1} edge(s)", name, rows.Count );
            from.SendGump( new NavNodeListGump( title, rows ) );
        }

        // ── connect ───────────────────────────────────────────────────────────────
        private static void DoConnect( CommandEventArgs e )
        {
            if ( e.Length == 3 )
            {
                // Name mode
                ConnectByName( e.Mobile, e.GetString( 1 ), e.GetString( 2 ) );
            }
            else if ( e.Length == 1 )
            {
                // Targeting mode — pick first node
                e.Mobile.SendMessage( "Click the first NavNodeMarker." );
                e.Mobile.Target = new NavConnectFirstTarget();
            }
            else
            {
                e.Mobile.SendMessage( "Usage: [navbuild connect [nameA nameB]" );
            }
        }

        public static void ConnectByName( Mobile from, string nameA, string nameB )
        {
            BotWaypoint wpA = PlayerBotNavigator.GetLandmark( nameA );
            BotWaypoint wpB = PlayerBotNavigator.GetLandmark( nameB );

            if ( wpA == null ) { from.SendMessage( 0x22, "Node '{0}' not found.", nameA ); return; }
            if ( wpB == null ) { from.SendMessage( 0x22, "Node '{0}' not found.", nameB ); return; }

            // Check if edge already exists
            List<string> existingEdges;
            if ( PlayerBotNavigator.Edges.TryGetValue( nameA, out existingEdges )
                 && existingEdges.Contains( nameB ) )
            {
                from.SendMessage( 0x22, "Edge '{0}' ↔ '{1}' already exists.", nameA, nameB );
                return;
            }

            double dist = Math.Sqrt(
                Math.Pow( wpA.Location.X - wpB.Location.X, 2 ) +
                Math.Pow( wpA.Location.Y - wpB.Location.Y, 2 ) );

            // Write edge to XML
            XmlDocument doc = LoadOrCreateXml();
            XmlElement edgesEl = (XmlElement)doc.SelectSingleNode( "NavGraph/Edges" );
            if ( edgesEl == null )
            {
                from.SendMessage( 0x22, "NavGraph.xml is malformed — missing <Edges> element." );
                return;
            }

            XmlElement el = doc.CreateElement( "Edge" );
            el.SetAttribute( "a", nameA );
            el.SetAttribute( "b", nameB );
            edgesEl.AppendChild( el );
            SaveXml( doc );

            PlayerBotNavigator.BuildGraph();

            string warn = dist > 300.0 ? " [WARN: > 300 tiles — consider splitting]" : "";
            from.SendMessage( 0x55,
                "Connected \"{0}\" ↔ \"{1}\" ({2:F0} tiles). Graph rebuilt.{3}",
                nameA, nameB, dist, warn );
        }

        // ── remove ────────────────────────────────────────────────────────────────
        private static void DoRemove( CommandEventArgs e )
        {
            if ( e.Length < 2 )
            {
                e.Mobile.SendMessage( "Usage: [navbuild remove <name>" );
                return;
            }

            string name = e.GetString( 1 );
            Mobile from = e.Mobile;

            if ( PlayerBotNavigator.GetLandmark( name ) == null )
            {
                from.SendMessage( 0x22, "Node '{0}' not found.", name );
                return;
            }

            XmlDocument doc = LoadOrCreateXml();

            // Remove node element
            XmlNode nodeEl = doc.SelectSingleNode( string.Format( "NavGraph/Nodes/Node[@name='{0}']", name ) );
            if ( nodeEl != null )
                nodeEl.ParentNode.RemoveChild( nodeEl );

            // Remove all edges referencing this node
            XmlNodeList edges = doc.SelectNodes( string.Format(
                "NavGraph/Edges/Edge[@a='{0}' or @b='{0}']", name ) );
            if ( edges != null )
                foreach ( XmlNode edge in edges )
                    edge.ParentNode.RemoveChild( edge );

            SaveXml( doc );
            PlayerBotNavigator.BuildGraph();

            from.SendMessage( 0x55, "Removed node \"{0}\" and its edges. Graph rebuilt.", name );
        }

        // ── show ──────────────────────────────────────────────────────────────────
        private static void DoShow( CommandEventArgs e )
        {
            Mobile from = e.Mobile;
            int range   = e.Length >= 2 ? e.GetInt32( 1 ) : 600;
            int spawned = 0;

            foreach ( BotWaypoint wp in PlayerBotNavigator.Landmarks.Values )
            {
                if ( wp.Map != from.Map ) continue;
                double dx = wp.Location.X - from.X;
                double dy = wp.Location.Y - from.Y;
                if ( dx*dx + dy*dy > (double)(range * range) ) continue;

                NavNodeMarker marker = new NavNodeMarker( wp.Name );
                marker.MoveToWorld( wp.Location, wp.Map );
                spawned++;
            }

            from.SendMessage( 0x55, "Spawned {0} node markers (range {1}). They auto-delete in 60s.", spawned, range );
        }

        // ── rebuild ───────────────────────────────────────────────────────────────
        private static void DoRebuild( CommandEventArgs e )
        {
            PlayerBotNavigator.BuildGraph();
            e.Mobile.SendMessage( 0x55, "NavGraph rebuilt. {0} nodes, {1} edge entries.",
                PlayerBotNavigator.Landmarks.Count,
                PlayerBotNavigator.Edges.Count );
        }

        // ── export ────────────────────────────────────────────────────────────────
        private static void DoExport( CommandEventArgs e )
        {
            Mobile from = e.Mobile;
            var sb = new StringBuilder();

            sb.AppendLine( "// ── Nodes ────────────────" );
            foreach ( BotWaypoint wp in PlayerBotNavigator.Landmarks.Values )
            {
                if ( wp.RoutingOnly )
                    sb.AppendFormat( "// ROUTING: Add(\"{0}\", new Point3D({1},{2},{3}), Map.Felucca);\n",
                        wp.Name, wp.Location.X, wp.Location.Y, wp.Location.Z );
            }

            sb.AppendLine( "// ── Edges ────────────────" );
            var seen = new List<string>();
            foreach ( var kvp in PlayerBotNavigator.Edges )
            {
                foreach ( string neighbor in kvp.Value )
                {
                    string key = kvp.Key.CompareTo( neighbor ) < 0
                        ? kvp.Key + ":" + neighbor
                        : neighbor + ":" + kvp.Key;
                    if ( !seen.Contains( key ) )
                    {
                        seen.Add( key );
                        sb.AppendFormat( "// Connect(\"{0}\", \"{1}\");\n", kvp.Key, neighbor );
                    }
                }
            }

            // Write to a file in the Data folder for convenience
            string exportPath = Path.Combine( Core.BaseDirectory, "Data", "NavGraph_export.txt" );
            File.WriteAllText( exportPath, sb.ToString() );

            from.SendMessage( 0x55, "Exported {0} nodes and edges to Data/NavGraph_export.txt",
                PlayerBotNavigator.Landmarks.Count );
        }

        // ── [navtest ──────────────────────────────────────────────────────────────
        [Usage( "navtest <from> <to>" )]
        [Description( "Computes and displays the bot route between two named nodes." )]
        private static void NavTest_OnCommand( CommandEventArgs e )
        {
            if ( e.Length < 2 )
            {
                e.Mobile.SendMessage( "Usage: [navtest <fromNode> <toNode>" );
                return;
            }

            string fromName = e.GetString( 0 );
            string toName   = e.GetString( 1 );
            Mobile from     = e.Mobile;

            BotWaypoint destWp = PlayerBotNavigator.GetLandmark( toName );
            if ( destWp == null )
            {
                from.SendMessage( 0x22, "Destination node '{0}' not found.", toName );
                return;
            }

            BotWaypoint startWp = PlayerBotNavigator.GetLandmark( fromName );
            if ( startWp == null )
            {
                from.SendMessage( 0x22, "Start node '{0}' not found.", fromName );
                return;
            }

            List<BotWaypoint> route = PlayerBotNavigator.ComputeRoute( startWp.Location, startWp.Map, destWp );

            if ( route == null || route.Count == 0 )
            {
                from.SendMessage( 0x22, "No graph route found from '{0}' to '{1}' (disconnected or missing edges).", fromName, toName );
                return;
            }

            double totalDist = 0;
            from.SendMessage( 0x55, "Route ({0} hops, {1} → {2}):", route.Count, fromName, toName );

            BotWaypoint prev = startWp;
            for ( int i = 0; i < route.Count; i++ )
            {
                BotWaypoint hop = route[i];
                double dx   = hop.Location.X - prev.Location.X;
                double dy   = hop.Location.Y - prev.Location.Y;
                double dist = Math.Sqrt( dx*dx + dy*dy );
                totalDist  += dist;
                string warn = dist > 300 ? "  ← WARN: > 300 tiles" : "";
                from.SendMessage( 0x55, "  {0} → {1} ({2:F0} tiles){3}", prev.Name, hop.Name, dist, warn );
                prev = hop;
            }

            from.SendMessage( 0x55, "Total path distance: ~{0:F0} tiles.", totalDist );
        }

        // ── [navshow ─────────────────────────────────────────────────────────────
        [Usage( "navshow [range]" )]
        [Description( "Alias for [navbuild show — spawns NavNodeMarkers in range." )]
        private static void NavShow_OnCommand( CommandEventArgs e )
        {
            int range = e.Length >= 1 ? e.GetInt32( 0 ) : 600;
            Mobile from = e.Mobile;
            int spawned = 0;

            foreach ( BotWaypoint wp in PlayerBotNavigator.Landmarks.Values )
            {
                if ( wp.Map != from.Map ) continue;
                double dx = wp.Location.X - from.X;
                double dy = wp.Location.Y - from.Y;
                if ( dx*dx + dy*dy > (double)(range * range) ) continue;

                NavNodeMarker marker = new NavNodeMarker( wp.Name );
                marker.MoveToWorld( wp.Location, wp.Map );
                spawned++;
            }

            from.SendMessage( 0x55, "Spawned {0} node markers (range {1}). Auto-delete in 60s.", spawned, range );
        }

        // ── [navbot ───────────────────────────────────────────────────────────────
        [Usage( "navbot <botName>" )]
        [Description( "Reports the current hop queue and stuck-tick count for a named PlayerBot." )]
        private static void NavBot_OnCommand( CommandEventArgs e )
        {
            if ( e.Length < 1 )
            {
                e.Mobile.SendMessage( "Usage: [navbot <botName>" );
                return;
            }

            string name = e.GetString( 0 );
            Mobile from = e.Mobile;

            PlayerBot found = null;
            foreach ( Mobile m in World.Mobiles.Values )
            {
                PlayerBot pb = m as PlayerBot;
                if ( pb != null && !pb.Deleted &&
                     string.Equals( pb.Name, name, StringComparison.OrdinalIgnoreCase ) )
                {
                    found = pb;
                    break;
                }
            }

            if ( found == null )
            {
                from.SendMessage( 0x22, "No PlayerBot named '{0}' found.", name );
                return;
            }

            ActivityState state = found.ActivityState;
            from.SendMessage( 0x55, "Bot '{0}': Activity={1}, Dest=({2},{3},{4})",
                found.Name, state.Current,
                state.TravelDestination.X, state.TravelDestination.Y, state.TravelDestination.Z );

            if ( state.WaypointHops != null && state.WaypointHops.Count > 0 )
            {
                from.SendMessage( 0x55, "  Hop queue ({0} remaining):", state.WaypointHops.Count );
                int idx = 0;
                foreach ( BotWaypoint hop in state.WaypointHops )
                {
                    from.SendMessage( 0x55, "    [{0}] {1} ({2},{3},{4})",
                        idx++, hop.Name, hop.Location.X, hop.Location.Y, hop.Location.Z );
                    if ( idx >= 10 ) { from.SendMessage( 0x55, "    ... (truncated)" ); break; }
                }
            }
            else
            {
                from.SendMessage( 0x55, "  No hop queue (direct travel or idle)." );
            }

            if ( state.FinalDestination != null )
                from.SendMessage( 0x55, "  Final destination: {0}", state.FinalDestination.Name );
        }

        // ── goto ──────────────────────────────────────────────────────────────────
        private static void DoGoto( CommandEventArgs e )
        {
            if ( e.Length < 2 )
            {
                e.Mobile.SendMessage( "Usage: [navbuild goto <name>" );
                return;
            }

            string name = e.GetString( 1 );
            BotWaypoint wp = PlayerBotNavigator.GetLandmark( name );
            if ( wp == null )
            {
                e.Mobile.SendMessage( 0x22, "Node '{0}' not found.", name );
                return;
            }

            e.Mobile.MoveToWorld( wp.Location, wp.Map );
            e.Mobile.SendMessage( 0x55, "Teleported to node \"{0}\" ({1},{2},{3}). Use [navbuild move {0} to relocate it here.",
                name, wp.Location.X, wp.Location.Y, wp.Location.Z );
        }

        // ── move ── relocate an existing node to current position ─────────────────
        private static void DoMove( CommandEventArgs e )
        {
            if ( e.Length < 2 )
            {
                e.Mobile.SendMessage( "Usage: [navbuild move <name>  — relocates node to your current position" );
                return;
            }

            string name = e.GetString( 1 );
            Mobile from = e.Mobile;
            BotWaypoint wp = PlayerBotNavigator.GetLandmark( name );
            if ( wp == null )
            {
                from.SendMessage( 0x22, "Node '{0}' not found.", name );
                return;
            }

            if ( !wp.FromXml )
            {
                from.SendMessage( 0x22, "Node '{0}' is hardcoded, not from NavGraph.xml. Remove it and re-add instead.", name );
                return;
            }

            Point3D oldPos = wp.Location;
            Point3D newPos = from.Location;
            string mapStr  = (from.Map == Map.Trammel) ? "Trammel" : "Felucca";

            // Update the XML node's coordinates
            XmlDocument doc = LoadOrCreateXml();
            XmlElement el = (XmlElement)doc.SelectSingleNode(
                string.Format( "NavGraph/Nodes/Node[@name='{0}']", name ) );

            if ( el == null )
            {
                from.SendMessage( 0x22, "Node '{0}' not found in NavGraph.xml.", name );
                return;
            }

            el.SetAttribute( "x",   newPos.X.ToString() );
            el.SetAttribute( "y",   newPos.Y.ToString() );
            el.SetAttribute( "z",   newPos.Z.ToString() );
            el.SetAttribute( "map", mapStr );
            SaveXml( doc );

            PlayerBotNavigator.BuildGraph();

            NavNodeMarker marker = new NavNodeMarker( name );
            marker.MoveToWorld( newPos, from.Map );

            from.SendMessage( 0x55, "Moved node \"{0}\" from ({1},{2},{3}) to ({4},{5},{6}). Graph rebuilt.",
                name, oldPos.X, oldPos.Y, oldPos.Z, newPos.X, newPos.Y, newPos.Z );
        }

        // ── nearest ── closest N nodes by distance ────────────────────────────────
        private static void DoNearest( CommandEventArgs e )
        {
            int count = e.Length >= 2 ? e.GetInt32( 1 ) : 10;
            if ( count < 1 ) count = 1;
            if ( count > 50 ) count = 50;
            PrintNearest( e.Mobile, count );
        }

        [Usage( "navnearest [count]" )]
        [Description( "Lists the closest nav nodes to your position, sorted by distance." )]
        private static void NavNearest_OnCommand( CommandEventArgs e )
        {
            int count = e.Length >= 1 ? e.GetInt32( 0 ) : 10;
            if ( count < 1 ) count = 1;
            if ( count > 50 ) count = 50;
            PrintNearest( e.Mobile, count );
        }

        private static void PrintNearest( Mobile from, int count )
        {
            var candidates = new List<KeyValuePair<double, BotWaypoint>>();

            foreach ( BotWaypoint wp in PlayerBotNavigator.Landmarks.Values )
            {
                if ( wp.Map != from.Map ) continue;
                double dx   = wp.Location.X - from.X;
                double dy   = wp.Location.Y - from.Y;
                double dist = Math.Sqrt( dx * dx + dy * dy );
                candidates.Add( new KeyValuePair<double, BotWaypoint>( dist, wp ) );
            }

            candidates.Sort( ( a, b ) => a.Key.CompareTo( b.Key ) );

            int shown = Math.Min( count, candidates.Count );

            if ( shown == 0 )
            {
                from.SendMessage( 0x55, "No nodes on this map." );
                return;
            }

            var rows = new List<NavNodeListGump.NavRow>();
            for ( int i = 0; i < shown; i++ )
            {
                double      dist = candidates[i].Key;
                BotWaypoint wp   = candidates[i].Value;

                List<string> neighbors;
                PlayerBotNavigator.Edges.TryGetValue( wp.Name, out neighbors );
                int edgeCount = neighbors != null ? neighbors.Count : 0;

                string edgeInfo = edgeCount == 0 ? "isolated" : string.Format( "{0}e", edgeCount );
                string routing  = wp.RoutingOnly ? " [rt]" : "";
                string note     = string.Format( "{0:F0}t {1}{2}", dist, edgeInfo, routing );

                rows.Add( new NavNodeListGump.NavRow
                {
                    Name     = wp.Name,
                    Note     = note,
                    Location = wp.Location,
                    Map      = wp.Map
                } );
            }

            string title = string.Format( "Nearest {0} nodes to ({1},{2})", shown, from.X, from.Y );
            from.SendGump( new NavNodeListGump( title, rows ) );
        }

        // ── isolated ── hardcoded landmarks with no edges ─────────────────────────
        private static void DoIsolated( CommandEventArgs e )
        {
            Mobile from    = e.Mobile;
            bool   showAll = e.Length >= 2 && e.GetString( 1 ).ToLower() == "all";

            var isolated = new List<BotWaypoint>();

            foreach ( BotWaypoint wp in PlayerBotNavigator.Landmarks.Values )
            {
                if ( !showAll && wp.FromXml ) continue;

                List<string> neighbors;
                if ( !PlayerBotNavigator.Edges.TryGetValue( wp.Name, out neighbors ) || neighbors.Count == 0 )
                    isolated.Add( wp );
            }

            isolated.Sort( ( a, b ) =>
            {
                int r = a.RoutingOnly.CompareTo( b.RoutingOnly );
                if ( r != 0 ) return r;
                r = a.Tags.CompareTo( b.Tags );
                if ( r != 0 ) return r;
                return string.Compare( a.Name, b.Name, StringComparison.OrdinalIgnoreCase );
            } );

            string scope = showAll ? "nodes" : "hardcoded landmarks";
            if ( isolated.Count == 0 )
            {
                from.SendMessage( 0x55, "All {0} are connected.", scope );
                return;
            }

            var rows = new List<NavNodeListGump.NavRow>();
            foreach ( BotWaypoint wp in isolated )
            {
                string note = wp.RoutingOnly
                    ? "routing orphan"
                    : (wp.Tags == WaypointTag.None ? "Untagged" : wp.Tags.ToString());

                rows.Add( new NavNodeListGump.NavRow
                {
                    Name     = wp.Name,
                    Note     = note,
                    Location = wp.Location,
                    Map      = wp.Map
                } );
            }

            string title = string.Format( "Isolated {0} — {1} node(s)", scope, isolated.Count );
            from.SendGump( new NavNodeListGump( title, rows ) );
        }

        // ── Trail mode ────────────────────────────────────────────────────────────
        private static TrailSession s_ActiveTrail = null;

        private class TrailSession
        {
            public string       Prefix;
            public int          Counter;
            public double       MinDist;       // minimum tiles between drops (default 30, lower for cities)
            public string       AnchorNode;    // optional node trail was anchored from
            public string       LastNodeName;  // last connected node (anchor or dropped)
            public List<string> DroppedNodes;  // names added this session (for cancel rollback)
        }

        private static void DoTrail( CommandEventArgs e )
        {
            if ( e.Length < 2 )
            {
                e.Mobile.SendMessage( "Usage: [navbuild trail <start|drop|end|cancel|status> [args]" );
                return;
            }

            string sub = e.GetString( 1 ).ToLower();
            switch ( sub )
            {
                case "start":  DoTrailStart( e );        break;
                case "drop":   DoTrailDrop( e.Mobile );  break;
                case "end":    DoTrailEnd( e );           break;
                case "cancel": DoTrailCancel( e.Mobile ); break;
                case "status": DoTrailStatus( e.Mobile ); break;
                default:
                    e.Mobile.SendMessage( "Unknown trail subcommand. Valid: start drop end cancel status" );
                    break;
            }
        }

        private static void DoTrailStart( CommandEventArgs e )
        {
            Mobile from = e.Mobile;

            if ( s_ActiveTrail != null )
            {
                from.SendMessage( 0x22,
                    "A trail is already active (prefix: {0}, {1} drop(s)). Use [navbuild trail cancel first.",
                    s_ActiveTrail.Prefix, s_ActiveTrail.DroppedNodes.Count );
                DoTrailStatus( from );
                return;
            }

            if ( e.Length < 3 )
            {
                from.SendMessage( "Usage: [navbuild trail start <prefix> [fromNode]" );
                return;
            }

            string prefix   = e.GetString( 2 );
            string fromNode = null;
            double minDist  = 30.0;

            // Parse remaining optional args: node name and/or min spacing distance.
            // A purely numeric arg is treated as minDist; anything else is treated as fromNode.
            for ( int i = 3; i < e.Length; i++ )
            {
                string arg = e.GetString( i );
                double parsed;
                if ( double.TryParse( arg, out parsed ) )
                    minDist = Math.Max( 1.0, parsed );
                else
                    fromNode = arg;
            }

            if ( fromNode != null && PlayerBotNavigator.GetLandmark( fromNode ) == null )
            {
                from.SendMessage( 0x22, "Anchor node '{0}' not found.", fromNode );
                return;
            }

            s_ActiveTrail = new TrailSession
            {
                Prefix       = prefix,
                Counter      = 1,
                MinDist      = minDist,
                AnchorNode   = fromNode,
                LastNodeName = fromNode,
                DroppedNodes = new List<string>()
            };

            string modeDesc = minDist < 15.0 ? " [DENSE mode]" : "";
            if ( fromNode != null )
            {
                BotWaypoint anchor = PlayerBotNavigator.GetLandmark( fromNode );
                from.SendMessage( 0x55,
                    "Trail started{0}. Prefix: {1}. Min spacing: {2:F0} tiles. Anchored from: {3} ({4},{5}).",
                    modeDesc, prefix, minDist, fromNode, anchor.Location.X, anchor.Location.Y );
            }
            else
            {
                from.SendMessage( 0x55,
                    "Trail started{0}. Prefix: {1}. Min spacing: {2:F0} tiles. No anchor — first drop will be standalone.",
                    modeDesc, prefix, minDist );
            }
        }

        private static void DoTrailDrop( Mobile from )
        {
            if ( s_ActiveTrail == null )
            {
                from.SendMessage( 0x22, "No active trail. Use [navbuild trail start <prefix> first." );
                return;
            }

            Point3D pos    = from.Location;
            Map     map    = from.Map;
            string  mapStr = (map == Map.Trammel) ? "Trammel" : "Felucca";

            // Distance check from last node
            double      lastDist = -1;
            BotWaypoint lastWp   = s_ActiveTrail.LastNodeName != null
                ? PlayerBotNavigator.GetLandmark( s_ActiveTrail.LastNodeName )
                : null;

            if ( lastWp != null )
            {
                double dx = pos.X - lastWp.Location.X;
                double dy = pos.Y - lastWp.Location.Y;
                lastDist  = Math.Sqrt( dx * dx + dy * dy );

                if ( lastDist < s_ActiveTrail.MinDist )
                {
                    from.SendMessage( 0x22,
                        "Too close to {0} ({1:F0} tiles, min {2:F0}) — move further before dropping.",
                        s_ActiveTrail.LastNodeName, lastDist, s_ActiveTrail.MinDist );
                    return;
                }
            }

            // Auto-name: advance counter until we find an unused slot
            string nodeName;
            int    counter = s_ActiveTrail.Counter;
            do
            {
                nodeName = string.Format( "{0}_{1:D2}", s_ActiveTrail.Prefix, counter++ );
            }
            while ( PlayerBotNavigator.GetLandmark( nodeName ) != null );
            s_ActiveTrail.Counter = counter;

            // Write node to XML
            XmlDocument doc     = LoadOrCreateXml();
            XmlElement  nodesEl = (XmlElement)doc.SelectSingleNode( "NavGraph/Nodes" );
            if ( nodesEl == null ) { from.SendMessage( 0x22, "NavGraph.xml malformed." ); return; }

            XmlElement nodeEl = doc.CreateElement( "Node" );
            nodeEl.SetAttribute( "name",    nodeName );
            nodeEl.SetAttribute( "x",       pos.X.ToString() );
            nodeEl.SetAttribute( "y",       pos.Y.ToString() );
            nodeEl.SetAttribute( "z",       pos.Z.ToString() );
            nodeEl.SetAttribute( "map",     mapStr );
            nodeEl.SetAttribute( "routing", "true" );
            nodesEl.AppendChild( nodeEl );

            // Wire edge from previous node
            string prevNodeName = s_ActiveTrail.LastNodeName;
            if ( prevNodeName != null )
            {
                XmlElement edgesEl = (XmlElement)doc.SelectSingleNode( "NavGraph/Edges" );
                if ( edgesEl != null )
                {
                    XmlElement edgeEl = doc.CreateElement( "Edge" );
                    edgeEl.SetAttribute( "a", prevNodeName );
                    edgeEl.SetAttribute( "b", nodeName );
                    edgesEl.AppendChild( edgeEl );
                }
            }

            SaveXml( doc );
            PlayerBotNavigator.BuildGraph();

            s_ActiveTrail.DroppedNodes.Add( nodeName );
            s_ActiveTrail.LastNodeName = nodeName;

            NavNodeMarker marker = new NavNodeMarker( nodeName );
            marker.MoveToWorld( pos, map );

            if ( prevNodeName != null && lastDist >= 0 )
            {
                string warn = lastDist > 300 ? " [WARN: large gap]" : "";
                from.SendMessage( 0x55,
                    "Dropped {0} at ({1},{2},{3}). {4} <-> {5}: {6:F0} tiles{7}",
                    nodeName, pos.X, pos.Y, pos.Z, prevNodeName, nodeName, lastDist, warn );
            }
            else
            {
                from.SendMessage( 0x55,
                    "Dropped {0} at ({1},{2},{3}). (standalone — no previous node)",
                    nodeName, pos.X, pos.Y, pos.Z );
            }
        }

        private static void DoTrailEnd( CommandEventArgs e )
        {
            Mobile from = e.Mobile;

            if ( s_ActiveTrail == null )
            {
                from.SendMessage( 0x22, "No active trail to end." );
                return;
            }

            if ( s_ActiveTrail.DroppedNodes.Count == 0 )
            {
                from.SendMessage( 0x22,
                    "No nodes dropped this session. Use [navbuild trail cancel to discard." );
                return;
            }

            string toNode = e.Length >= 3 ? e.GetString( 2 ) : null;

            if ( toNode != null )
            {
                BotWaypoint toWp = PlayerBotNavigator.GetLandmark( toNode );
                if ( toWp == null )
                {
                    from.SendMessage( 0x22, "End anchor '{0}' not found.", toNode );
                    return;
                }

                XmlDocument doc     = LoadOrCreateXml();
                XmlElement  edgesEl = (XmlElement)doc.SelectSingleNode( "NavGraph/Edges" );
                if ( edgesEl != null )
                {
                    XmlElement edgeEl = doc.CreateElement( "Edge" );
                    edgeEl.SetAttribute( "a", s_ActiveTrail.LastNodeName );
                    edgeEl.SetAttribute( "b", toNode );
                    edgesEl.AppendChild( edgeEl );
                    SaveXml( doc );
                }

                PlayerBotNavigator.BuildGraph();

                double      dist   = 0;
                BotWaypoint lastWp = PlayerBotNavigator.GetLandmark( s_ActiveTrail.LastNodeName );
                if ( lastWp != null )
                {
                    double dx = toWp.Location.X - lastWp.Location.X;
                    double dy = toWp.Location.Y - lastWp.Location.Y;
                    dist      = Math.Sqrt( dx * dx + dy * dy );
                }

                from.SendMessage( 0x55,
                    "Connected: {0} <-> {1} ({2:F0} tiles). Trail saved. {3} node(s) dropped.",
                    s_ActiveTrail.LastNodeName, toNode, dist, s_ActiveTrail.DroppedNodes.Count );
            }
            else
            {
                from.SendMessage( 0x55,
                    "Trail saved. {0} node(s) dropped.", s_ActiveTrail.DroppedNodes.Count );
            }

            var parts = new List<string>();
            if ( s_ActiveTrail.AnchorNode != null ) parts.Add( s_ActiveTrail.AnchorNode );
            parts.AddRange( s_ActiveTrail.DroppedNodes );
            if ( toNode != null ) parts.Add( toNode );
            from.SendMessage( 0x55, "Route: {0}", string.Join( " -> ", parts.ToArray() ) );

            s_ActiveTrail = null;
        }

        private static void DoTrailCancel( Mobile from )
        {
            if ( s_ActiveTrail == null )
            {
                from.SendMessage( 0x22, "No active trail to cancel." );
                return;
            }

            int removed = 0;
            if ( s_ActiveTrail.DroppedNodes.Count > 0 )
            {
                XmlDocument doc = LoadOrCreateXml();

                foreach ( string name in s_ActiveTrail.DroppedNodes )
                {
                    XmlNode nodeEl = doc.SelectSingleNode(
                        string.Format( "NavGraph/Nodes/Node[@name='{0}']", name ) );
                    if ( nodeEl != null ) { nodeEl.ParentNode.RemoveChild( nodeEl ); removed++; }

                    XmlNodeList edges = doc.SelectNodes(
                        string.Format( "NavGraph/Edges/Edge[@a='{0}' or @b='{0}']", name ) );
                    if ( edges != null )
                        foreach ( XmlNode edge in edges )
                            edge.ParentNode.RemoveChild( edge );
                }

                SaveXml( doc );
                PlayerBotNavigator.BuildGraph();
            }

            s_ActiveTrail = null;
            from.SendMessage( 0x55, "Trail cancelled. Removed {0} node(s) from XML. Graph rebuilt.", removed );
        }

        private static void DoTrailStatus( Mobile from )
        {
            if ( s_ActiveTrail == null )
            {
                from.SendMessage( "No active trail." );
                return;
            }

            from.SendMessage( 0x55,
                "Trail active — Prefix: {0}, Drops: {1}, Last node: {2}, Min spacing: {3:F0} tiles",
                s_ActiveTrail.Prefix,
                s_ActiveTrail.DroppedNodes.Count,
                s_ActiveTrail.LastNodeName ?? "(none)",
                s_ActiveTrail.MinDist );

            if ( s_ActiveTrail.LastNodeName != null )
            {
                BotWaypoint lastWp = PlayerBotNavigator.GetLandmark( s_ActiveTrail.LastNodeName );
                if ( lastWp != null )
                {
                    double dx   = from.X - lastWp.Location.X;
                    double dy   = from.Y - lastWp.Location.Y;
                    double dist = Math.Sqrt( dx * dx + dy * dy );
                    from.SendMessage( 0x55,
                        "  Last node: ({0},{1},{2}) — {3:F0} tiles from you",
                        lastWp.Location.X, lastWp.Location.Y, lastWp.Location.Z, dist );
                }
            }
        }

        // ── [navdrop ──────────────────────────────────────────────────────────────
        [Usage( "navdrop" )]
        [Description( "Spam command: drops a routing node at your feet during an active trail." )]
        private static void NavDrop_OnCommand( CommandEventArgs e )
        {
            DoTrailDrop( e.Mobile );
        }

        // ── Targeting: connect by clicking NavNodeMarkers ─────────────────────────
        private class NavConnectFirstTarget : Target
        {
            public NavConnectFirstTarget() : base( -1, false, TargetFlags.None )
            {
            }

            protected override void OnTarget( Mobile from, object targeted )
            {
                NavNodeMarker marker = targeted as NavNodeMarker;
                if ( marker == null )
                {
                    from.SendMessage( 0x22, "Target a NavNodeMarker (the red gems spawned by [navbuild show)." );
                    return;
                }

                from.SendMessage( "First node: '{0}'. Now target the second node.", marker.NodeName );
                from.Target = new NavConnectSecondTarget( marker.NodeName );
            }

            protected override void OnTargetCancel( Mobile from, TargetCancelType cancelType )
            {
                from.SendMessage( "Connect cancelled." );
            }
        }

        private class NavConnectSecondTarget : Target
        {
            private readonly string m_FirstNode;

            public NavConnectSecondTarget( string firstNode ) : base( -1, false, TargetFlags.None )
            {
                m_FirstNode = firstNode;
            }

            protected override void OnTarget( Mobile from, object targeted )
            {
                NavNodeMarker marker = targeted as NavNodeMarker;
                if ( marker == null )
                {
                    from.SendMessage( 0x22, "Target a NavNodeMarker." );
                    return;
                }

                if ( string.Equals( m_FirstNode, marker.NodeName, StringComparison.OrdinalIgnoreCase ) )
                {
                    from.SendMessage( 0x22, "Cannot connect a node to itself." );
                    return;
                }

                ConnectByName( from, m_FirstNode, marker.NodeName );
            }

            protected override void OnTargetCancel( Mobile from, TargetCancelType cancelType )
            {
                from.SendMessage( "Connect cancelled." );
            }
        }

        // ── XML helpers ───────────────────────────────────────────────────────────
        private static XmlDocument LoadOrCreateXml()
        {
            var doc = new XmlDocument();
            string path = NavGraphPath;

            if ( File.Exists( path ) )
            {
                doc.Load( path );
            }
            else
            {
                doc.LoadXml( "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<NavGraph>\n  <Nodes/>\n  <Edges/>\n</NavGraph>" );
            }

            return doc;
        }

        private static void SaveXml( XmlDocument doc )
        {
            var settings = new XmlWriterSettings
            {
                Indent      = true,
                IndentChars = "  ",
                Encoding    = System.Text.Encoding.UTF8
            };

            using ( XmlWriter writer = XmlWriter.Create( NavGraphPath, settings ) )
                doc.Save( writer );
        }
    }
}
