using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using Server;
using Server.Commands;
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
        }

        // ── [navbuild ─────────────────────────────────────────────────────────────
        [Usage( "navbuild <addnode|insert|connect|removeedge|edges|remove|show|rebuild|export|goto|move> [args]" )]
        [Description( "In-game nav graph authoring tool." )]
        private static void NavBuild_OnCommand( CommandEventArgs e )
        {
            if ( e.Length < 1 )
            {
                e.Mobile.SendMessage( "Usage: [navbuild <addnode|insert|connect|removeedge|edges|remove|show|rebuild|export|goto|move>" );
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
                default:
                    e.Mobile.SendMessage( "Unknown subcommand '{0}'. Valid: addnode insert connect removeedge edges remove show rebuild export goto move", sub );
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

            from.SendMessage( 0x55, "Node '{0}' ({1},{2}) — {3} edge(s):",
                name, wp.Location.X, wp.Location.Y, neighbors.Count );

            foreach ( string neighbor in neighbors )
            {
                BotWaypoint nb = PlayerBotNavigator.GetLandmark( neighbor );
                if ( nb == null ) { from.SendMessage( 0x55, "  -> {0} [MISSING]", neighbor ); continue; }
                double dx   = wp.Location.X - nb.Location.X;
                double dy   = wp.Location.Y - nb.Location.Y;
                double dist = Math.Sqrt( dx*dx + dy*dy );
                string warn = dist > 300 ? " ← WARN" : "";
                from.SendMessage( 0x55, "  <-> {0} ({1:F0} tiles){2}", neighbor, dist, warn );
            }
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
