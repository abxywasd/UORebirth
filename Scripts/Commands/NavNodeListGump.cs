using System;
using System.Collections.Generic;
using Server;
using Server.Gumps;
using Server.Mobiles;
using Server.Network;

namespace Server.Scripts.Commands
{
    // Generic paginated gump for displaying nav graph node lists.
    // Used by [navbuild isolated, [navbuild nearest, [navbuild edges.
    public class NavNodeListGump : Gump
    {
        public struct NavRow
        {
            public string  Name;
            public string  Note;      // tag, distance, etc.
            public Point3D Location;
            public Map     Map;
        }

        private const int RowsPerPage = 15;
        private const int RowH        = 22;
        private const int WinW        = 600;
        private const int WinH        = 440;
        private const int HeaderY     = 32;
        private const int FirstRowY   = 52;
        private const int NavY        = 405;

        // Column X positions
        private const int ColGotoBtn  = 5;
        private const int ColName     = 35;
        private const int ColNote     = 245;
        private const int ColLoc      = 355;
        private const int ColMap      = 490;

        private readonly List<NavRow> m_Rows;
        private readonly string       m_Title;

        public NavNodeListGump( string title, List<NavRow> rows )
            : base( 30, 30 )
        {
            m_Title = title;
            m_Rows  = rows;
            Build();
        }

        private void Build()
        {
            int pageCount = Math.Max( 1, (m_Rows.Count + RowsPerPage - 1) / RowsPerPage );

            // Page 0 — always-visible chrome
            AddPage( 0 );
            AddBackground( 0, 0, WinW, WinH, 2600 );

            AddLabel( 10, 8, 0x55, m_Title );
            // Close button
            AddButton( WinW - 26, 8, 4017, 4019, 0, GumpButtonType.Reply, 0 );

            // Column headers
            AddLabel( ColName,    HeaderY, 0x34, "Name" );
            AddLabel( ColNote,    HeaderY, 0x34, "Type / Note" );
            AddLabel( ColLoc,     HeaderY, 0x34, "Location" );
            AddLabel( ColMap,     HeaderY, 0x34, "Map" );

            for ( int page = 1; page <= pageCount; page++ )
            {
                AddPage( page );

                int start = (page - 1) * RowsPerPage;
                int end   = Math.Min( start + RowsPerPage, m_Rows.Count );

                for ( int i = start; i < end; i++ )
                {
                    int    rowY = FirstRowY + (i - start) * RowH;
                    NavRow row  = m_Rows[i];
                    int    hue  = (i % 2 == 0) ? 0x480 : 0x34;

                    // Goto button
                    AddButton( ColGotoBtn, rowY, 4005, 4007, 1000 + i, GumpButtonType.Reply, 0 );

                    // Name (truncate at 24 chars)
                    string nameText = row.Name.Length > 24 ? row.Name.Substring( 0, 22 ) + ".." : row.Name;
                    AddLabel( ColName, rowY, hue, nameText );

                    // Note (truncate at 14 chars)
                    string noteText = row.Note != null
                        ? (row.Note.Length > 14 ? row.Note.Substring( 0, 12 ) + ".." : row.Note)
                        : "";
                    AddLabel( ColNote, rowY, hue, noteText );

                    // Location
                    AddLabel( ColLoc, rowY, hue,
                        string.Format( "({0},{1},{2})", row.Location.X, row.Location.Y, row.Location.Z ) );

                    // Map
                    string mapName = row.Map != null ? row.Map.Name : "?";
                    AddLabel( ColMap, rowY, hue, mapName );
                }

                // Page navigation
                if ( page > 1 )
                    AddButton( 10, NavY, 4014, 4016, 0, GumpButtonType.Page, page - 1 );

                AddLabel( WinW / 2 - 50, NavY + 2, 0x34,
                    string.Format( "Page {0}/{1}  ({2} total)", page, pageCount, m_Rows.Count ) );

                if ( page < pageCount )
                    AddButton( WinW - 26, NavY, 4005, 4007, 0, GumpButtonType.Page, page + 1 );
            }
        }

        public override void OnResponse( NetState sender, RelayInfo info )
        {
            Mobile from = sender.Mobile;
            if ( from == null ) return;

            int bid = info.ButtonID;
            if ( bid == 0 ) return; // close

            if ( bid >= 1000 )
            {
                int idx = bid - 1000;
                if ( idx >= 0 && idx < m_Rows.Count )
                {
                    NavRow row = m_Rows[idx];
                    from.MoveToWorld( row.Location, row.Map );
                    from.SendMessage( 0x55, "Teleported to {0} ({1},{2},{3}).",
                        row.Name, row.Location.X, row.Location.Y, row.Location.Z );
                    // Reopen so the user can keep navigating the list
                    from.SendGump( new NavNodeListGump( m_Title, m_Rows ) );
                }
            }
        }
    }
}
