using System;
using System.Collections.Generic;
using Server;
using Server.Gumps;
using Server.Network;

namespace Server.Mobiles
{
    public class PlayerBotDirectorGump : Gump
    {
        private Mobile            m_From;
        private PlayerBotDirector m_Director;

        // Navigation state
        private int m_Tab;
        private int m_BotPage;
        private int m_BotFilter;   // 0=All 1=PK 2=Crafter 3=Adv 4=Encounter
        private int m_BotSort;     // 0=Name 1=Persona 2=Activity 3=HP
        private int m_SpawnCount;
        private int m_SpawnPersona; // -1=Random 0=PK 1=Crafter 2=Adv
        private int m_SpawnXP;      // -1=Random 0-3=specific
        private int m_PoiPage;
        private int m_GroupPage;

        // Layout constants
        private const int W = 600;
        private const int H = 500;
        private const int ContentY = 58;

        // Hues
        private const int HueTeal   = 0x384;
        private const int HueGreen  = 0x40;
        private const int HueRed    = 0x26;
        private const int HueYellow = 0x35;
        private const int HueGray   = 0x480;
        private const int HueWhite  = 0;

        public PlayerBotDirectorGump( Mobile from, PlayerBotDirector dir,
            int tab = 0, int botPage = 0, int botFilter = 0, int botSort = 0,
            int spawnCount = 1, int spawnPersona = -1, int spawnXP = -1,
            int poiPage = 0, int groupPage = 0 )
            : base( 50, 50 )
        {
            m_From       = from;
            m_Director   = dir;
            m_Tab        = tab;
            m_BotPage    = botPage;
            m_BotFilter  = botFilter;
            m_BotSort    = botSort;
            m_SpawnCount = Math.Max( 1, Math.Min( 50, spawnCount ) );
            m_SpawnPersona = spawnPersona;
            m_SpawnXP    = spawnXP;
            m_PoiPage    = poiPage;
            m_GroupPage  = groupPage;

            AddPage( 0 );
            AddBackground( 0, 0, W, H, 9200 );
            AddLabel( 15, 10, HueTeal, "PlayerBot Director" );

            DrawTabBar();
            AddImageTiled( 15, 50, W - 30, 2, 9304 );

            switch ( m_Tab )
            {
                case 0: DrawDashboard(); break;
                case 1: DrawBotList();   break;
                case 2: DrawSpawn();     break;
                case 3: DrawPOIs();      break;
                case 4: DrawGroups();    break;
                case 5: DrawSettings();  break;
            }
        }

        // ── Tab bar ───────────────────────────────────────────────────────────────
        private void DrawTabBar()
        {
            string[] names = { "Dashboard", "Bot List", "Spawn", "POIs", "Groups", "Settings" };
            int[] xpos = { 10, 100, 193, 280, 363, 448 };
            for ( int i = 0; i < names.Length; i++ )
            {
                int hue = (m_Tab == i) ? HueGreen : HueGray;
                AddButton( xpos[i], 30, 4005, 4007, i + 1, GumpButtonType.Reply, 0 );
                AddLabel( xpos[i] + 35, 32, hue, names[i] );
            }
        }

        // ── Tab 0: Dashboard ─────────────────────────────────────────────────────
        private void DrawDashboard()
        {
            int y = ContentY + 5;

            // Enabled row
            bool en = m_Director.Enabled;
            AddLabel( 15, y, HueWhite, "Enabled:" );
            AddLabel( 130, y, en ? HueGreen : HueRed, en ? "YES" : "NO" );
            AddButton( 210, y - 2, 4005, 4007, 100, GumpButtonType.Reply, 0 );
            AddLabel( 245, y, HueWhite, "Toggle" );
            y += 25;

            // Target count row
            int target = m_Director.TargetBotCount;
            AddLabel( 15, y, HueWhite, "Target count:" );
            AddLabel( 130, y, HueGreen, target.ToString() );
            AddButton( 175, y - 2, 4014, 4016, 101, GumpButtonType.Reply, 0 ); // -10
            AddButton( 200, y - 2, 4014, 4016, 102, GumpButtonType.Reply, 0 ); // -1
            AddButton( 225, y - 2, 4005, 4007, 103, GumpButtonType.Reply, 0 ); // +1
            AddButton( 250, y - 2, 4005, 4007, 104, GumpButtonType.Reply, 0 ); // +10
            AddLabel( 280, y, HueGray, "-10 -1 +1 +10" );
            y += 25;

            // Live counts
            List<PlayerBot> live       = m_Director.GetLiveBots();
            List<PlayerBot> encounter  = m_Director.GetEncounterBots();
            List<PlayerBot> controlled = m_Director.GetControlledBots();
            int regular = live.Count - encounter.Count;

            AddLabel( 15, y, HueWhite, "Regular:" );
            AddLabel( 90, y, HueGreen, regular.ToString() );
            AddLabel( 130, y, HueWhite, "Encounter:" );
            AddLabel( 215, y, HueYellow, encounter.Count.ToString() );
            AddLabel( 255, y, HueWhite, "Controlled:" );
            AddLabel( 340, y, HueGreen, controlled.Count.ToString() );
            y += 5;

            AddImageTiled( 15, y + 18, W - 30, 1, 9304 );
            y += 28;

            // Personas
            AddLabel( 15, y, HueTeal, "Personas" );
            y += 18;
            int pks      = m_Director.GetBotsByPersona( PlayerBotPersona.PlayerBotProfile.PlayerKiller ).Count;
            int crafters = m_Director.GetBotsByPersona( PlayerBotPersona.PlayerBotProfile.Crafter ).Count;
            int advs     = m_Director.GetBotsByPersona( PlayerBotPersona.PlayerBotProfile.Adventurer ).Count;
            AddLabel( 15,  y, HueWhite, "PlayerKillers:" ); AddLabel( 120, y, HueRed,   pks.ToString() );
            AddLabel( 150, y, HueWhite, "Crafters:" );      AddLabel( 225, y, HueGreen, crafters.ToString() );
            AddLabel( 255, y, HueWhite, "Adventurers:" );   AddLabel( 345, y, HueGreen, advs.ToString() );
            y += 5;

            AddImageTiled( 15, y + 18, W - 30, 1, 9304 );
            y += 28;

            // Activities
            AddLabel( 15, y, HueTeal, "Activities" );
            y += 18;
            int wander  = m_Director.GetBotsByActivity( BotActivity.Wandering ).Count;
            int travel  = m_Director.GetBotsByActivity( BotActivity.Traveling ).Count;
            int combat  = m_Director.GetBotsByActivity( BotActivity.Combat ).Count;
            int craft   = m_Director.GetBotsByActivity( BotActivity.Crafting ).Count;
            int other   = live.Count - wander - travel - combat - craft;
            int groups  = m_Director.GetActiveGroups().Count;

            AddLabel( 15,  y, HueWhite, "Wander:" );  AddLabel( 75,  y, HueGreen, wander.ToString() );
            AddLabel( 110, y, HueWhite, "Travel:" );   AddLabel( 168, y, HueGreen, travel.ToString() );
            AddLabel( 205, y, HueWhite, "Combat:" );   AddLabel( 263, y, HueYellow, combat.ToString() );
            AddLabel( 300, y, HueWhite, "Craft:" );    AddLabel( 348, y, HueGreen, craft.ToString() );
            AddLabel( 385, y, HueWhite, "Other:" );    AddLabel( 433, y, HueGray, other.ToString() );
            y += 20;
            AddLabel( 15, y, HueWhite, "Active groups:" ); AddLabel( 120, y, HueGreen, groups.ToString() );
            y += 5;

            AddImageTiled( 15, y + 18, W - 30, 1, 9304 );
            y += 28;

            // Quick actions
            AddLabel( 15, y, HueTeal, "Quick Actions" );
            y += 20;

            AddButton( 15, y - 2, 4005, 4007, 105, GumpButtonType.Reply, 0 );
            AddLabel( 50, y, HueWhite, "Spawn 1 Here" );
            AddButton( 165, y - 2, 4005, 4007, 106, GumpButtonType.Reply, 0 );
            AddLabel( 200, y, HueWhite, "Hire 10 Nearby" );
            AddButton( 325, y - 2, 4005, 4007, 107, GumpButtonType.Reply, 0 );
            AddLabel( 360, y, HueWhite, "Dismiss Mine" );
            y += 30;

            AddButton( 15, y - 2, 4014, 4016, 108, GumpButtonType.Reply, 0 );
            AddLabel( 50, y, HueYellow, "Del Encounters" );
            AddButton( 175, y - 2, 4014, 4016, 109, GumpButtonType.Reply, 0 );
            AddLabel( 210, y, HueYellow, "Del Regular" );
            AddButton( 330, y - 2, 4017, 4019, 110, GumpButtonType.Reply, 0 );
            AddLabel( 365, y, HueRed, "DELETE ALL" );
        }

        // ── Tab 1: Bot List ───────────────────────────────────────────────────────
        private void DrawBotList()
        {
            int y = ContentY + 5;

            // Filter row
            AddLabel( 15, y, HueWhite, "Filter:" );
            string[] filterLabels = { "All", "PKs", "Crafters", "Adventurers", "Encounter" };
            int[] filterX = { 65, 110, 160, 230, 325 };
            for ( int i = 0; i < filterLabels.Length; i++ )
            {
                int hue = (m_BotFilter == i) ? HueGreen : HueWhite;
                AddButton( filterX[i] - 3, y - 2, 4005, 4007, 260 + i, GumpButtonType.Reply, 0 );
                AddLabel( filterX[i] + 32, y, hue, filterLabels[i] );
            }
            y += 22;

            // Sort row
            AddLabel( 15, y, HueWhite, "Sort:" );
            string[] sortLabels = { "Name", "Persona", "Activity", "HP" };
            int[] sortX = { 65, 120, 195, 275 };
            for ( int i = 0; i < sortLabels.Length; i++ )
            {
                int hue = (m_BotSort == i) ? HueGreen : HueWhite;
                AddButton( sortX[i] - 3, y - 2, 4005, 4007, 270 + i, GumpButtonType.Reply, 0 );
                AddLabel( sortX[i] + 32, y, hue, sortLabels[i] );
            }
            y += 22;

            AddImageTiled( 15, y, W - 30, 1, 9304 );
            y += 5;

            // Column headers
            AddLabel( 15,  y, HueTeal, "Name" );
            AddLabel( 135, y, HueTeal, "Persona" );
            AddLabel( 195, y, HueTeal, "XP" );
            AddLabel( 240, y, HueTeal, "Activity" );
            AddLabel( 320, y, HueTeal, "HP%" );
            AddLabel( 360, y, HueTeal, "Coords" );
            AddLabel( 455, y, HueTeal, "Actions" );
            y += 18;
            AddImageTiled( 15, y, W - 30, 1, 9304 );
            y += 5;

            List<PlayerBot> filtered = GetFilteredBots();
            int pageCount = Math.Max( 1, (filtered.Count + 9) / 10 );
            m_BotPage = Math.Max( 0, Math.Min( m_BotPage, pageCount - 1 ) );

            int startIdx = m_BotPage * 10;
            int endIdx   = Math.Min( startIdx + 10, filtered.Count );

            for ( int i = startIdx; i < endIdx; i++ )
            {
                int slot = i - startIdx;
                PlayerBot bot = filtered[i];
                int rowY = y + slot * 22;

                // Name (truncate)
                string name = bot.Name ?? "???";
                if ( name.Length > 14 ) name = name.Substring( 0, 14 );
                AddLabel( 15, rowY, HueWhite, name );

                // Persona
                int personaHue = bot.PlayerBotProfile == PlayerBotPersona.PlayerBotProfile.PlayerKiller ? HueRed : HueGreen;
                AddLabel( 135, rowY, personaHue, PersonaAbbrev( bot.PlayerBotProfile ) );

                // XP
                AddLabel( 190, rowY, HueGray, XPAbbrev( bot.PlayerBotExperience ) );

                // Activity
                AddLabel( 235, rowY, HueWhite, ActivityAbbrev( bot.ActivityState.Current ) );

                // HP%
                int hpPct = bot.HitsMax > 0 ? (bot.Hits * 100 / bot.HitsMax) : 0;
                int hpHue = hpPct > 50 ? HueGreen : (hpPct > 25 ? HueYellow : HueRed);
                AddLabel( 315, rowY, hpHue, hpPct + "%" );

                // Coords
                AddLabel( 355, rowY, HueGray, bot.X + "," + bot.Y );

                // Action buttons — all inline on one row
                AddButton( 440, rowY, 4005, 4007, 200 + slot, GumpButtonType.Reply, 0 );
                AddLabel(  458, rowY, HueWhite, "Go" );

                AddButton( 478, rowY, 4014, 4016, 210 + slot, GumpButtonType.Reply, 0 );
                AddLabel(  496, rowY, HueRed, "Kill" );

                AddButton( 520, rowY, 4005, 4007, 220 + slot, GumpButtonType.Reply, 0 );
                AddLabel(  538, rowY, HueGray, "Pr" );

                if ( !bot.Controled )
                {
                    AddButton( 558, rowY, 4005, 4007, 230 + slot, GumpButtonType.Reply, 0 );
                    AddLabel(  576, rowY, HueGreen, "Hi" );
                }
                else
                {
                    AddButton( 558, rowY, 4014, 4016, 230 + slot, GumpButtonType.Reply, 0 );
                    AddLabel(  576, rowY, HueYellow, "Rl" );
                }
            }

            if ( filtered.Count == 0 )
                AddLabel( 15, y, HueGray, "(no bots match filter)" );

            // Pagination bar
            AddButton( 15, H - 45, 4014, 4016, 250, GumpButtonType.Reply, 0 );
            AddLabel(  50, H - 43, HueWhite, "Prev" );
            AddLabel( 160, H - 43, HueGray, "Page " + (m_BotPage + 1) + " of " + pageCount + "  (" + filtered.Count + " bots)" );
            AddButton( 340, H - 45, 4005, 4007, 251, GumpButtonType.Reply, 0 );
            AddLabel(  375, H - 43, HueWhite, "Next" );
        }

        // ── Tab 2: Spawn ──────────────────────────────────────────────────────────
        private void DrawSpawn()
        {
            int y = ContentY + 5;

            // Spawn count
            AddLabel( 15, y, HueWhite, "Spawn count:" );
            AddLabel( 130, y, HueGreen, m_SpawnCount.ToString() );
            AddButton( 175, y - 2, 4014, 4016, 300, GumpButtonType.Reply, 0 ); // -10
            AddButton( 200, y - 2, 4014, 4016, 301, GumpButtonType.Reply, 0 ); // -1
            AddButton( 225, y - 2, 4005, 4007, 302, GumpButtonType.Reply, 0 ); // +1
            AddButton( 250, y - 2, 4005, 4007, 303, GumpButtonType.Reply, 0 ); // +10
            AddLabel( 280, y, HueGray, "-10 -1 +1 +10" );
            y += 30;

            // Persona selector
            AddLabel( 15, y, HueWhite, "Persona:" );
            string[] personaOpts = { "Random", "PlayerKiller", "Crafter", "Adventurer" };
            int[] personaVals    = {  -1,       0,              1,         2 };
            int px = 100;
            for ( int i = 0; i < personaOpts.Length; i++ )
            {
                int hue = (m_SpawnPersona == personaVals[i]) ? HueGreen : HueWhite;
                AddButton( px, y - 2, 4005, 4007, 310 + i, GumpButtonType.Reply, 0 );
                AddLabel( px + 35, y, hue, personaOpts[i] );
                px += personaOpts[i].Length * 8 + 45;
            }
            y += 30;

            // XP selector (fixed positions to ensure Grandmaster fits)
            AddLabel( 15, y, HueWhite, "XP Level:" );
            string[] xpOpts  = { "Random", "Newbie", "Average", "Proficient", "GM" };
            int[]    xpVals  = {  -1,       0,        1,         2,            3 };
            int[]    xpXPos  = {  100,      190,      270,       355,          455 };
            for ( int i = 0; i < xpOpts.Length; i++ )
            {
                int hue = (m_SpawnXP == xpVals[i]) ? HueGreen : HueWhite;
                AddButton( xpXPos[i], y - 2, 4005, 4007, 315 + i, GumpButtonType.Reply, 0 );
                AddLabel( xpXPos[i] + 35, y, hue, xpOpts[i] );
            }
            y += 35;

            AddImageTiled( 15, y, W - 30, 1, 9304 );
            y += 10;

            // Spawn actions
            AddButton( 15, y - 2, 4005, 4007, 320, GumpButtonType.Reply, 0 );
            AddLabel(  50, y, HueGreen, "Spawn " + m_SpawnCount + " at My Location" );
            y += 28;

            AddButton( 15, y - 2, 4005, 4007, 321, GumpButtonType.Reply, 0 );
            AddLabel(  50, y, HueGreen, "Spawn " + m_SpawnCount + " at Random POI" );
            y += 35;

            AddImageTiled( 15, y, W - 30, 1, 9304 );
            y += 10;

            AddLabel( 15, y, HueTeal, "Bulk Removal" );
            y += 22;

            AddButton( 15, y - 2, 4014, 4016, 322, GumpButtonType.Reply, 0 );
            AddLabel(  50, y, HueYellow, "Clear Encounter Bots" );
            AddButton( 230, y - 2, 4014, 4016, 323, GumpButtonType.Reply, 0 );
            AddLabel(  265, y, HueYellow, "Clear Regular Bots" );
            y += 30;

            AddButton( 15, y - 2, 4017, 4019, 324, GumpButtonType.Reply, 0 );
            AddLabel(  50, y, HueRed, "DELETE ALL BOTS  (irreversible)" );
        }

        // ── Tab 3: POIs ───────────────────────────────────────────────────────────
        private void DrawPOIs()
        {
            int y = ContentY + 5;
            List<BotPOI> all = PlayerBotPOI.All;
            int totalPOIs = all.Count;

            // Total capacity summary
            int totalCurr = 0, totalCap = 0;
            foreach ( BotPOI poi in all )
            {
                totalCurr += PlayerBotPOI.CountBotsNear( poi );
                totalCap  += poi.MaxBots;
            }
            AddLabel( 15, y, HueWhite, "Total POI bots:" );
            AddLabel( 130, y, HueGreen, totalCurr + " / " + totalCap );
            AddLabel( 220, y, HueGray, "(" + totalPOIs + " POIs)" );
            y += 20;

            AddImageTiled( 15, y, W - 30, 1, 9304 );
            y += 5;

            // Headers
            AddLabel( 15,  y, HueTeal, "Name" );
            AddLabel( 165, y, HueTeal, "Type" );
            AddLabel( 235, y, HueTeal, "Bots" );
            AddLabel( 275, y, HueTeal, "Map" );
            AddLabel( 315, y, HueTeal, "Coords" );
            AddLabel( 405, y, HueTeal, "Actions" );
            y += 16;
            AddImageTiled( 15, y, W - 30, 1, 9304 );
            y += 4;

            int perPage = 15;
            int pageCount = Math.Max( 1, (totalPOIs + perPage - 1) / perPage );
            m_PoiPage = Math.Max( 0, Math.Min( m_PoiPage, pageCount - 1 ) );
            int startIdx = m_PoiPage * perPage;
            int endIdx   = Math.Min( startIdx + perPage, totalPOIs );

            for ( int i = startIdx; i < endIdx; i++ )
            {
                int slot = i - startIdx;
                BotPOI poi = all[i];
                int rowY = y + slot * 22;
                int curr = PlayerBotPOI.CountBotsNear( poi );

                string name = poi.Name;
                if ( name.Length > 18 ) name = name.Substring( 0, 18 );
                AddLabel( 15,  rowY, HueWhite, name );
                AddLabel( 165, rowY, HueGray, POITypeAbbrev( poi.Type ) );

                int bHue = curr >= poi.MaxBots ? HueRed : (curr > 0 ? HueYellow : HueGray);
                AddLabel( 235, rowY, bHue, curr + "/" + poi.MaxBots );
                AddLabel( 275, rowY, HueGray, MapAbbrev( poi.Map ) );
                AddLabel( 315, rowY, HueGray, poi.Location.X + "," + poi.Location.Y );

                AddButton( 405, rowY, 4005, 4007, 400 + slot, GumpButtonType.Reply, 0 );
                AddLabel(  440, rowY, HueWhite, "Go" );
                AddButton( 455, rowY, 4005, 4007, 415 + slot, GumpButtonType.Reply, 0 );
                AddLabel(  490, rowY, HueGreen, "Spn" );
                AddButton( 513, rowY, 4014, 4016, 430 + slot, GumpButtonType.Reply, 0 );
                AddLabel(  548, rowY, HueRed, "Clr" );
            }

            // Pagination
            AddButton( 15, H - 45, 4014, 4016, 450, GumpButtonType.Reply, 0 );
            AddLabel(  50, H - 43, HueWhite, "Prev" );
            AddLabel( 160, H - 43, HueGray, "Page " + (m_PoiPage + 1) + " of " + pageCount );
            AddButton( 290, H - 45, 4005, 4007, 451, GumpButtonType.Reply, 0 );
            AddLabel(  325, H - 43, HueWhite, "Next" );
        }

        // ── Tab 4: Groups ─────────────────────────────────────────────────────────
        private void DrawGroups()
        {
            int y = ContentY + 5;

            List<PlayerBotGroup> groups = m_Director.GetActiveGroups();
            AddLabel( 15, y, HueWhite, "Active groups:" );
            AddLabel( 130, y, HueGreen, groups.Count.ToString() );
            y += 20;

            AddImageTiled( 15, y, W - 30, 1, 9304 );
            y += 5;

            // Headers
            AddLabel( 15,  y, HueTeal, "Leader" );
            AddLabel( 160, y, HueTeal, "Members" );
            AddLabel( 240, y, HueTeal, "Activity" );
            AddLabel( 405, y, HueTeal, "Actions" );
            y += 16;
            AddImageTiled( 15, y, W - 30, 1, 9304 );
            y += 4;

            int perPage = 10;
            int pageCount = Math.Max( 1, (groups.Count + perPage - 1) / perPage );
            m_GroupPage = Math.Max( 0, Math.Min( m_GroupPage, pageCount - 1 ) );
            int startIdx = m_GroupPage * perPage;
            int endIdx   = Math.Min( startIdx + perPage, groups.Count );

            for ( int i = startIdx; i < endIdx; i++ )
            {
                int slot = i - startIdx;
                PlayerBotGroup grp = groups[i];
                int rowY = y + slot * 27;

                PlayerBot leader = grp.Leader;
                if ( leader == null || leader.Deleted )
                {
                    AddLabel( 15, rowY, HueGray, "(disbanded)" );
                    continue;
                }

                string lname = leader.Name ?? "???";
                if ( lname.Length > 16 ) lname = lname.Substring( 0, 16 );
                AddLabel( 15,  rowY, HueWhite, lname );
                AddLabel( 160, rowY, HueGreen, grp.Members.Count.ToString() );
                AddLabel( 240, rowY, HueGray, ActivityAbbrev( leader.ActivityState.Current ) );

                AddButton( 405, rowY, 4005, 4007, 500 + slot, GumpButtonType.Reply, 0 );
                AddLabel(  440, rowY, HueWhite, "Goto" );
                AddButton( 470, rowY, 4014, 4016, 510 + slot, GumpButtonType.Reply, 0 );
                AddLabel(  505, rowY, HueRed, "Disband" );
            }

            if ( groups.Count == 0 )
                AddLabel( 15, y, HueGray, "(no active groups)" );

            // Form group action
            AddImageTiled( 15, H - 75, W - 30, 1, 9304 );
            AddButton( 15, H - 60, 4005, 4007, 550, GumpButtonType.Reply, 0 );
            AddLabel(  50, H - 58, HueGreen, "Form Group Near Me (range 15)" );

            // Pagination
            AddButton( 15, H - 40, 4014, 4016, 551, GumpButtonType.Reply, 0 );
            AddLabel(  50, H - 38, HueWhite, "Prev" );
            AddLabel( 160, H - 38, HueGray, "Page " + (m_GroupPage + 1) + " of " + pageCount );
            AddButton( 290, H - 40, 4005, 4007, 552, GumpButtonType.Reply, 0 );
            AddLabel(  325, H - 38, HueWhite, "Next" );
        }

        // ── Tab 5: Settings ───────────────────────────────────────────────────────
        private void DrawSettings()
        {
            int y = ContentY + 5;
            AddLabel( 15, y, HueTeal, "Tunable Settings" );
            y += 25;

            DrawSettingRow( ref y, "POI bot despawn timeout (min):",        m_Director.PoiTimeoutMinutes,      601, 621 );
            DrawSettingRow( ref y, "Encounter bot despawn timeout (min):",  m_Director.EncounterTimeoutMinutes, 602, 622 );
            DrawSettingRow( ref y, "Encounter chance per tick (%):",        m_Director.EncounterChancePct,     603, 623 );
            DrawSettingRow( ref y, "Encounter tick interval (sec):",        m_Director.EncounterTickSeconds,   604, 624 );
            DrawSettingRow( ref y, "POI tick interval (min):",              m_Director.PoiTickMinutes,         605, 625 );
            DrawSettingRow( ref y, "Director tick interval (min):",         m_Director.DirectorTickMinutes,    606, 626 );
            DrawSettingRow( ref y, "Max bots per burst tick:",              m_Director.MaxBurstPerTick,        607, 627 );
            DrawSettingRow( ref y, "Observation radius (tiles):",           m_Director.ObservationRadius,      608, 628 );

            y += 10;
            AddImageTiled( 15, y, W - 30, 1, 9304 );
            y += 12;

            AddButton( 15, y - 2, 4017, 4019, 650, GumpButtonType.Reply, 0 );
            AddLabel(  50, y, HueYellow, "Reset All to Defaults" );
        }

        private void DrawSettingRow( ref int y, string label, int value, int decBtn, int incBtn )
        {
            AddLabel( 15, y, HueWhite, label );
            AddLabel( 350, y, HueGreen, value.ToString() );
            AddButton( 390, y - 2, 4014, 4016, decBtn, GumpButtonType.Reply, 0 );
            AddLabel(  425, y, HueGray, "-" );
            AddButton( 440, y - 2, 4005, 4007, incBtn, GumpButtonType.Reply, 0 );
            AddLabel(  475, y, HueGray, "+" );
            y += 28;
        }

        // ── Helpers ────────────────────────────────────────────────────────────────
        private List<PlayerBot> GetFilteredBots()
        {
            List<PlayerBot> all = m_Director.GetLiveBots();
            List<PlayerBot> filtered = new List<PlayerBot>();
            foreach ( PlayerBot bot in all )
            {
                switch ( m_BotFilter )
                {
                    case 1: if ( bot.PlayerBotProfile == PlayerBotPersona.PlayerBotProfile.PlayerKiller ) filtered.Add( bot ); break;
                    case 2: if ( bot.PlayerBotProfile == PlayerBotPersona.PlayerBotProfile.Crafter )      filtered.Add( bot ); break;
                    case 3: if ( bot.PlayerBotProfile == PlayerBotPersona.PlayerBotProfile.Adventurer )   filtered.Add( bot ); break;
                    case 4: if ( bot.IsEncounterBot )                                                      filtered.Add( bot ); break;
                    default: filtered.Add( bot ); break;
                }
            }
            filtered.Sort( (a, b) => {
                switch ( m_BotSort )
                {
                    case 1: return a.PlayerBotProfile.CompareTo( b.PlayerBotProfile );
                    case 2: return a.ActivityState.Current.CompareTo( b.ActivityState.Current );
                    case 3: return a.Hits.CompareTo( b.Hits );
                    default: return string.Compare( a.Name, b.Name, StringComparison.Ordinal );
                }
            } );
            return filtered;
        }

        private static string PersonaAbbrev( PlayerBotPersona.PlayerBotProfile p )
        {
            switch ( p )
            {
                case PlayerBotPersona.PlayerBotProfile.PlayerKiller: return "PK";
                case PlayerBotPersona.PlayerBotProfile.Crafter:      return "Craft";
                default:                                              return "Adv";
            }
        }

        private static string XPAbbrev( PlayerBotPersona.PlayerBotExperience xp )
        {
            switch ( xp )
            {
                case PlayerBotPersona.PlayerBotExperience.Newbie:      return "New";
                case PlayerBotPersona.PlayerBotExperience.Average:     return "Avg";
                case PlayerBotPersona.PlayerBotExperience.Proficient:  return "Prof";
                default:                                               return "GM";
            }
        }

        private static string ActivityAbbrev( BotActivity a )
        {
            switch ( a )
            {
                case BotActivity.Wandering:  return "Wander";
                case BotActivity.Traveling:  return "Travel";
                case BotActivity.Combat:     return "Combat";
                case BotActivity.Crafting:   return "Craft";
                case BotActivity.Fleeing:    return "Flee";
                case BotActivity.Hunting:    return "Hunt";
                case BotActivity.TownVisit:  return "Town";
                case BotActivity.Grouped:    return "Group";
                case BotActivity.Recruited:  return "Hired";
                default:                     return "Idle";
            }
        }

        private static string POITypeAbbrev( BotPOIType t )
        {
            switch ( t )
            {
                case BotPOIType.Town:            return "Town";
                case BotPOIType.DungeonEntrance: return "Dungeon";
                default:                          return "Road";
            }
        }

        private static string MapAbbrev( Map map )
        {
            if ( map == Map.Felucca )  return "Fel";
            if ( map == Map.Trammel )  return "Tram";
            if ( map == Map.Ilshenar ) return "Ilsh";
            return "???";
        }

        private void Reopen()
        {
            m_From.SendGump( new PlayerBotDirectorGump(
                m_From, m_Director,
                m_Tab, m_BotPage, m_BotFilter, m_BotSort,
                m_SpawnCount, m_SpawnPersona, m_SpawnXP,
                m_PoiPage, m_GroupPage ) );
        }

        // ── OnResponse ────────────────────────────────────────────────────────────
        public override void OnResponse( NetState sender, RelayInfo info )
        {
            if ( m_Director == null || m_Director.Deleted )
                return;

            int btn = info.ButtonID;

            if ( btn == 0 ) return; // right-click close

            // Tab navigation (1-6)
            if ( btn >= 1 && btn <= 6 )
            {
                m_Tab = btn - 1;
                m_BotPage = 0;
                Reopen();
                return;
            }

            // ── Dashboard actions (100-119) ──────────────────────────────────────
            if ( btn >= 100 && btn <= 119 )
            {
                switch ( btn )
                {
                    case 100: // Toggle enabled
                        m_Director.Enabled = !m_Director.Enabled;
                        break;

                    case 101: m_Director.TargetBotCount -= 10; break;
                    case 102: m_Director.TargetBotCount -= 1;  break;
                    case 103: m_Director.TargetBotCount += 1;  break;
                    case 104: m_Director.TargetBotCount += 10; break;

                    case 105: // Spawn 1 here
                        m_Director.SpawnOneBot( m_From.Location );
                        m_From.SendMessage( "Spawned one bot at your location." );
                        break;

                    case 106: // Hire 10 nearby
                    {
                        int hired = 0;
                        IPooledEnumerable eable = m_From.Map.GetMobilesInRange( m_From.Location, 20 );
                        foreach ( Mobile m in eable )
                        {
                            if ( hired >= 10 ) break;
                            PlayerBot bot = m as PlayerBot;
                            if ( bot == null || bot.Controled ) continue;
                            if ( bot.AddHire( m_From ) ) hired++;
                        }
                        eable.Free();
                        m_From.SendMessage( "Hired {0} bots.", hired );
                        break;
                    }

                    case 107: // Dismiss mine
                    {
                        int dismissed = 0;
                        List<PlayerBot> list = new List<PlayerBot>();
                        foreach ( Mobile m in m_From.GetMobilesInRange( 30 ) )
                        {
                            PlayerBot bot = m as PlayerBot;
                            if ( bot != null && bot.Controled && bot.ControlMaster == m_From )
                                list.Add( bot );
                        }
                        foreach ( PlayerBot bot in list )
                        {
                            bot.SetControlMaster( null );
                            bot.ActivityState.SetActivity( BotActivity.Wandering );
                            dismissed++;
                        }
                        m_From.SendMessage( "Dismissed {0} bots.", dismissed );
                        break;
                    }

                    case 108: // Delete encounters
                    {
                        int n = m_Director.DeleteEncounterBots();
                        m_From.SendMessage( "Deleted {0} encounter bots.", n );
                        break;
                    }

                    case 109: // Delete regular bots
                    {
                        int n = m_Director.DeleteRegularBots();
                        m_From.SendMessage( "Deleted {0} regular bots.", n );
                        break;
                    }

                    case 110: // Delete ALL
                    {
                        List<PlayerBot> all = m_Director.GetLiveBots();
                        int n = all.Count;
                        foreach ( PlayerBot b in all ) b.Delete();
                        m_From.SendMessage( "Deleted {0} bots.", n );
                        break;
                    }
                }
                Reopen();
                return;
            }

            // ── Bot list actions (200-279) ───────────────────────────────────────
            if ( btn >= 200 && btn <= 279 )
            {
                List<PlayerBot> filtered = GetFilteredBots();
                int pageCount = Math.Max( 1, (filtered.Count + 9) / 10 );
                m_BotPage = Math.Max( 0, Math.Min( m_BotPage, pageCount - 1 ) );

                if ( btn >= 200 && btn <= 249 ) // Per-row actions
                {
                    int slot = (btn % 10);
                    int range = (btn / 10) * 10; // 200, 210, 220, 230
                    int idx = m_BotPage * 10 + slot;
                    if ( idx < filtered.Count )
                    {
                        PlayerBot bot = filtered[idx];
                        switch ( range )
                        {
                            case 200: // Goto
                                if ( bot.Map != null && bot.Map != Map.Internal )
                                    m_From.MoveToWorld( bot.Location, bot.Map );
                                break;

                            case 210: // Kill
                                bot.Delete();
                                if ( m_BotPage > 0 && m_BotPage * 10 >= filtered.Count - 1 )
                                    m_BotPage--;
                                break;

                            case 220: // Props
                                m_From.SendGump( new PropertiesGump( m_From, bot ) );
                                break;

                            case 230: // Hire or Release
                                if ( !bot.Controled )
                                    bot.AddHire( m_From );
                                else
                                {
                                    bot.SetControlMaster( null );
                                    bot.ActivityState.SetActivity( BotActivity.Wandering );
                                }
                                break;
                        }
                    }
                }
                else if ( btn == 250 ) { m_BotPage = Math.Max( 0, m_BotPage - 1 ); }
                else if ( btn == 251 ) { m_BotPage = Math.Min( pageCount - 1, m_BotPage + 1 ); }
                else if ( btn >= 260 && btn <= 264 ) { m_BotFilter = btn - 260; m_BotPage = 0; }
                else if ( btn >= 270 && btn <= 273 ) { m_BotSort = btn - 270; }

                Reopen();
                return;
            }

            // ── Spawn tab actions (300-329) ──────────────────────────────────────
            if ( btn >= 300 && btn <= 329 )
            {
                switch ( btn )
                {
                    case 300: m_SpawnCount = Math.Max( 1, m_SpawnCount - 10 ); break;
                    case 301: m_SpawnCount = Math.Max( 1, m_SpawnCount - 1 );  break;
                    case 302: m_SpawnCount = Math.Min( 50, m_SpawnCount + 1 ); break;
                    case 303: m_SpawnCount = Math.Min( 50, m_SpawnCount + 10 ); break;

                    // Persona selector: 310=Random, 311=PK, 312=Crafter, 313=Adv
                    case 310: m_SpawnPersona = -1; break;
                    case 311: m_SpawnPersona = 0;  break;
                    case 312: m_SpawnPersona = 1;  break;
                    case 313: m_SpawnPersona = 2;  break;

                    // XP selector: 315=Random, 316=Newbie, 317=Average, 318=Proficient, 319=GM
                    case 315: m_SpawnXP = -1; break;
                    case 316: m_SpawnXP = 0;  break;
                    case 317: m_SpawnXP = 1;  break;
                    case 318: m_SpawnXP = 2;  break;
                    case 319: m_SpawnXP = 3;  break;

                    case 320: // Spawn at my location
                    {
                        for ( int i = 0; i < m_SpawnCount; i++ )
                        {
                            PlayerBotPersona.PlayerBotProfile p = m_SpawnPersona >= 0
                                ? (PlayerBotPersona.PlayerBotProfile)m_SpawnPersona
                                : (PlayerBotPersona.PlayerBotProfile)Utility.Random( 3 );
                            PlayerBotPersona.PlayerBotExperience x = m_SpawnXP >= 0
                                ? (PlayerBotPersona.PlayerBotExperience)m_SpawnXP
                                : (PlayerBotPersona.PlayerBotExperience)Utility.Random( 4 );
                            m_Director.SpawnOneBot( m_From.Location, p, x );
                        }
                        m_From.SendMessage( "Spawned {0} bot{1} at your location.", m_SpawnCount, m_SpawnCount == 1 ? "" : "s" );
                        break;
                    }

                    case 321: // Spawn at random POI
                    {
                        List<BotPOI> pois = PlayerBotPOI.GetUnderpopulated();
                        if ( pois.Count == 0 ) pois = PlayerBotPOI.All;
                        for ( int i = 0; i < m_SpawnCount; i++ )
                        {
                            BotPOI poi = pois[Utility.Random( pois.Count )];
                            Point3D loc = PlayerBotPOI.RandomSpawnPoint( poi );
                            PlayerBotPersona.PlayerBotProfile p = m_SpawnPersona >= 0
                                ? (PlayerBotPersona.PlayerBotProfile)m_SpawnPersona
                                : (PlayerBotPersona.PlayerBotProfile)Utility.Random( 3 );
                            PlayerBotPersona.PlayerBotExperience x = m_SpawnXP >= 0
                                ? (PlayerBotPersona.PlayerBotExperience)m_SpawnXP
                                : (PlayerBotPersona.PlayerBotExperience)Utility.Random( 4 );
                            m_Director.SpawnOneBot( loc, p, x );
                        }
                        m_From.SendMessage( "Spawned {0} bot{1} at POI locations.", m_SpawnCount, m_SpawnCount == 1 ? "" : "s" );
                        break;
                    }

                    case 322:
                    {
                        int n = m_Director.DeleteEncounterBots();
                        m_From.SendMessage( "Cleared {0} encounter bots.", n );
                        break;
                    }

                    case 323:
                    {
                        int n = m_Director.DeleteRegularBots();
                        m_From.SendMessage( "Cleared {0} regular bots.", n );
                        break;
                    }

                    case 324:
                    {
                        List<PlayerBot> all = m_Director.GetLiveBots();
                        int n = all.Count;
                        foreach ( PlayerBot b in all ) b.Delete();
                        m_From.SendMessage( "Deleted {0} bots.", n );
                        break;
                    }
                }
                Reopen();
                return;
            }

            // ── POI tab actions (400-459) ────────────────────────────────────────
            if ( btn >= 400 && btn <= 459 )
            {
                List<BotPOI> all = PlayerBotPOI.All;
                int perPage = 15;
                int pageCount = Math.Max( 1, (all.Count + perPage - 1) / perPage );
                m_PoiPage = Math.Max( 0, Math.Min( m_PoiPage, pageCount - 1 ) );

                if ( btn >= 400 && btn <= 414 ) // Goto POI
                {
                    int idx = m_PoiPage * perPage + (btn - 400);
                    if ( idx < all.Count )
                    {
                        BotPOI poi = all[idx];
                        m_From.MoveToWorld( poi.Location, poi.Map );
                    }
                }
                else if ( btn >= 415 && btn <= 429 ) // Force spawn at POI
                {
                    int idx = m_PoiPage * perPage + (btn - 415);
                    if ( idx < all.Count )
                    {
                        BotPOI poi = all[idx];
                        Point3D loc = PlayerBotPOI.RandomSpawnPoint( poi );
                        m_Director.SpawnOneBot( loc );
                        m_From.SendMessage( "Spawned one bot at {0}.", poi.Name );
                    }
                }
                else if ( btn >= 430 && btn <= 444 ) // Clear bots near POI
                {
                    int idx = m_PoiPage * perPage + (btn - 430);
                    if ( idx < all.Count )
                    {
                        BotPOI poi = all[idx];
                        int cleared = 0;
                        List<PlayerBot> toDelete = new List<PlayerBot>();
                        foreach ( PlayerBot bot in m_Director.GetLiveBots() )
                        {
                            if ( bot.Map != poi.Map ) continue;
                            int dx = bot.X - poi.Location.X;
                            int dy = bot.Y - poi.Location.Y;
                            if ( dx * dx + dy * dy <= poi.SpawnRadius * poi.SpawnRadius )
                                toDelete.Add( bot );
                        }
                        foreach ( PlayerBot bot in toDelete ) { bot.Delete(); cleared++; }
                        m_From.SendMessage( "Cleared {0} bots near {1}.", cleared, poi.Name );
                    }
                }
                else if ( btn == 450 ) { m_PoiPage = Math.Max( 0, m_PoiPage - 1 ); }
                else if ( btn == 451 ) { m_PoiPage = Math.Min( pageCount - 1, m_PoiPage + 1 ); }

                Reopen();
                return;
            }

            // ── Groups tab actions (500-552) ─────────────────────────────────────
            if ( btn >= 500 && btn <= 552 )
            {
                List<PlayerBotGroup> groups = m_Director.GetActiveGroups();
                int perPage = 10;
                int pageCount = Math.Max( 1, (groups.Count + perPage - 1) / perPage );
                m_GroupPage = Math.Max( 0, Math.Min( m_GroupPage, pageCount - 1 ) );

                if ( btn >= 500 && btn <= 509 ) // Goto leader
                {
                    int idx = m_GroupPage * perPage + (btn - 500);
                    if ( idx < groups.Count )
                    {
                        PlayerBot leader = groups[idx].Leader;
                        if ( leader != null && !leader.Deleted && leader.Map != null )
                            m_From.MoveToWorld( leader.Location, leader.Map );
                    }
                }
                else if ( btn >= 510 && btn <= 519 ) // Disband
                {
                    int idx = m_GroupPage * perPage + (btn - 510);
                    if ( idx < groups.Count )
                        groups[idx].Disband();
                }
                else if ( btn == 550 ) // Form group near me
                {
                    List<PlayerBot> nearby = new List<PlayerBot>();
                    IPooledEnumerable eable = m_From.Map.GetMobilesInRange( m_From.Location, 15 );
                    foreach ( Mobile m in eable )
                    {
                        PlayerBot bot = m as PlayerBot;
                        if ( bot != null && bot.Group == null && !bot.Controled )
                        { nearby.Add( bot ); break; }
                    }
                    eable.Free();
                    if ( nearby.Count > 0 )
                    {
                        PlayerBotGroup.TryForm( nearby[0], 15 );
                        m_From.SendMessage( "Formed a group around {0}.", nearby[0].Name );
                    }
                    else
                    {
                        m_From.SendMessage( "No ungrouped bots within 15 tiles." );
                    }
                }
                else if ( btn == 551 ) { m_GroupPage = Math.Max( 0, m_GroupPage - 1 ); }
                else if ( btn == 552 ) { m_GroupPage = Math.Min( pageCount - 1, m_GroupPage + 1 ); }

                Reopen();
                return;
            }

            // ── Settings tab actions (601-650) ───────────────────────────────────
            if ( btn >= 601 && btn <= 650 )
            {
                switch ( btn )
                {
                    case 601: m_Director.PoiTimeoutMinutes--;       break;
                    case 621: m_Director.PoiTimeoutMinutes++;       break;
                    case 602: m_Director.EncounterTimeoutMinutes--; break;
                    case 622: m_Director.EncounterTimeoutMinutes++; break;
                    case 603: m_Director.EncounterChancePct -= 5;   break;
                    case 623: m_Director.EncounterChancePct += 5;   break;
                    case 604: m_Director.EncounterTickSeconds -= 5;  break;
                    case 624: m_Director.EncounterTickSeconds += 5;  break;
                    case 605: m_Director.PoiTickMinutes--;           break;
                    case 625: m_Director.PoiTickMinutes++;           break;
                    case 606: m_Director.DirectorTickMinutes--;      break;
                    case 626: m_Director.DirectorTickMinutes++;      break;
                    case 607: m_Director.MaxBurstPerTick--;          break;
                    case 627: m_Director.MaxBurstPerTick++;          break;
                    case 608: m_Director.ObservationRadius -= 2;     break;
                    case 628: m_Director.ObservationRadius += 2;     break;
                    case 650: m_Director.ResetSettingsToDefaults();  break;
                }
                Reopen();
                return;
            }
        }
    }
}
