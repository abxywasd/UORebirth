using System;
using System.Collections.Generic;
using Server;
using Server.Gumps;
using Server.Items;
using Server.Network;
using Server.Targeting;

namespace Server.Mobiles
{
    // ────────────────────────────────────────────────────────────────────────────
    // Shared helpers
    // ────────────────────────────────────────────────────────────────────────────
    internal static class PlayerBotOwnerGumpHelper
    {
        public static string GetItemName( Item item )
        {
            if ( item.Name != null && item.Name.Length > 0 )
                return item.Name;

            int id = item.ItemID & TileData.MaxItemValue;
            if ( id < TileData.ItemTable.Length )
            {
                string tileName = TileData.ItemTable[id].Name;
                if ( tileName != null && tileName.Length > 0 )
                    return tileName;
            }

            return item.GetType().Name;
        }

        public static List<PlayerBot> GetControlledBots( Mobile from )
        {
            var result = new List<PlayerBot>();
            foreach ( Mobile m in World.Mobiles.Values )
            {
                PlayerBot bot = m as PlayerBot;
                if ( bot != null && !bot.Deleted && bot.Alive && bot.ControlMaster == from )
                    result.Add( bot );
            }
            return result;
        }

        // Renders a filled/empty bar using two tile IDs.
        public static void AddBar( Gump g, int x, int y, int current, int max, int barWidth )
        {
            int filled = ( max > 0 ) ? ( current * barWidth / max ) : 0;
            filled = Math.Max( 0, Math.Min( barWidth, filled ) );

            if ( filled > 0 )
                g.AddImageTiled( x, y, filled, 10, 9755 );
            if ( filled < barWidth )
                g.AddImageTiled( x + filled, y, barWidth - filled, 10, 9756 );
        }

        // Validates that the action should proceed and sends appropriate messages.
        // Returns false if the calling handler should abort.
        public static bool ValidateBot( Mobile from, PlayerBot bot )
        {
            if ( from == null || from.Deleted || !from.Alive )
                return false;

            if ( bot == null || bot.Deleted )
            {
                from.SendMessage( "Your bot no longer exists." );
                return false;
            }

            if ( !bot.Alive )
            {
                from.SendMessage( "Your bot is dead." );
                return false;
            }

            if ( bot.ControlMaster != from )
            {
                from.SendMessage( "You no longer control that bot." );
                return false;
            }

            return true;
        }
    }

    // ============================================================
    // PlayerBotListGump — entry point; shows all controlled bots
    // ============================================================
    public class PlayerBotListGump : Gump
    {
        private Mobile m_From;
        private int    m_Page;

        private const int BotsPerPage = 10;
        private const int RowHeight   = 45;

        public PlayerBotListGump( Mobile from, int page ) : base( 50, 50 )
        {
            m_From = from;
            m_Page = page;

            List<PlayerBot> bots = PlayerBotOwnerGumpHelper.GetControlledBots( from );

            int pageCount = Math.Max( 1, ( bots.Count + BotsPerPage - 1 ) / BotsPerPage );
            if ( m_Page >= pageCount ) m_Page = pageCount - 1;
            if ( m_Page < 0 )         m_Page = 0;

            int startIdx  = m_Page * BotsPerPage;
            int endIdx    = Math.Min( startIdx + BotsPerPage, bots.Count );
            int rowCount  = ( bots.Count == 0 ) ? 1 : ( endIdx - startIdx );

            int gumpHeight = 65 + rowCount * RowHeight + 50;

            AddPage( 0 );
            AddBackground( 0, 0, 400, gumpHeight, 9200 );

            // ── Header ──────────────────────────────────────────────────────────
            AddLabel( 15, 12, 0x384, "Your Bots" );
            if ( bots.Count == 0 )
                AddLabel( 15, 32, 0, "No bots under your command." );
            else
                AddLabel( 15, 32, 0, String.Format( "{0} bot{1} controlled",
                    bots.Count, bots.Count == 1 ? "" : "s" ) );

            AddImageTiled( 15, 54, 370, 2, 9304 );

            // ── Bot rows ─────────────────────────────────────────────────────────
            if ( bots.Count == 0 )
            {
                AddLabel( 15, 62, 0x26, "You have no bots under your command." );
            }
            else
            {
                int y = 62;
                for ( int i = startIdx; i < endIdx; i++ )
                {
                    PlayerBot bot = bots[i];
                    int rowIdx = i - startIdx;

                    // [Manage] button
                    AddButton( 15, y + 4, 4005, 4007, 100 + rowIdx, GumpButtonType.Reply, 0 );

                    // Name (green) + profile/experience
                    AddLabel( 55, y, 0x40, bot.Name );
                    AddLabel( 190, y, 0, bot.PlayerBotProfile.ToString() + " / " + bot.PlayerBotExperience.ToString() );

                    // HP bar
                    AddLabel( 55, y + 18, 0, "HP:" );
                    PlayerBotOwnerGumpHelper.AddBar( this, 80, y + 21, bot.Hits, bot.HitsMax, 80 );
                    AddLabel( 170, y + 18, 0, bot.ActivityState.Current.ToString() );

                    y += RowHeight;
                    if ( i < endIdx - 1 )
                        AddImageTiled( 15, y - 3, 370, 2, 9304 );
                }
            }

            // ── Footer ────────────────────────────────────────────────────────────
            int footerY = gumpHeight - 46;
            AddImageTiled( 15, footerY - 5, 370, 2, 9304 );

            if ( bots.Count > 0 )
            {
                AddButton( 15, footerY, 4017, 4019, 1, GumpButtonType.Reply, 0 );
                AddLabel(  55, footerY, 0x26, "Release All" );
            }

            if ( pageCount > 1 )
            {
                if ( m_Page > 0 )
                {
                    AddButton( 200, footerY, 4014, 4016, 2, GumpButtonType.Reply, 0 );
                    AddLabel(  235, footerY, 0, "Prev" );
                }
                AddLabel( 265, footerY, 0, String.Format( "Pg {0}/{1}", m_Page + 1, pageCount ) );
                if ( m_Page < pageCount - 1 )
                {
                    AddButton( 315, footerY, 4005, 4007, 3, GumpButtonType.Reply, 0 );
                    AddLabel(  350, footerY, 0, "Next" );
                }
            }
        }

        public override void OnResponse( NetState sender, RelayInfo info )
        {
            Mobile from = m_From;
            if ( from == null || from.Deleted || !from.Alive )
                return;

            if ( info.ButtonID == 0 ) return;

            List<PlayerBot> bots = PlayerBotOwnerGumpHelper.GetControlledBots( from );

            switch ( info.ButtonID )
            {
                case 1: // Release All
                {
                    int count = bots.Count;
                    foreach ( PlayerBot bot in bots )
                    {
                        bot.SetControlMaster( null );
                        bot.ActivityState.SetActivity( BotActivity.Wandering );
                    }
                    from.SendMessage( "Released {0} bot{1}.", count, count == 1 ? "" : "s" );
                    return; // all released — no reason to reopen
                }

                case 2: // Prev page
                    from.SendGump( new PlayerBotListGump( from, m_Page - 1 ) );
                    return;

                case 3: // Next page
                    from.SendGump( new PlayerBotListGump( from, m_Page + 1 ) );
                    return;

                default:
                    if ( info.ButtonID >= 100 )
                    {
                        int idx = m_Page * BotsPerPage + ( info.ButtonID - 100 );
                        if ( idx >= 0 && idx < bots.Count )
                        {
                            PlayerBot bot = bots[idx];
                            if ( bot == null || bot.Deleted )
                            {
                                from.SendMessage( "That bot no longer exists." );
                                from.SendGump( new PlayerBotListGump( from, m_Page ) );
                            }
                            else
                            {
                                from.SendGump( new PlayerBotManageGump( from, bot ) );
                            }
                        }
                    }
                    break;
            }
        }
    }

    // ============================================================
    // PlayerBotManageGump — per-bot hub with nav buttons
    // ============================================================
    public class PlayerBotManageGump : Gump
    {
        private Mobile    m_From;
        private PlayerBot m_Bot;

        public PlayerBotManageGump( Mobile from, PlayerBot bot ) : base( 50, 50 )
        {
            m_From = from;
            m_Bot  = bot;

            AddPage( 0 );
            AddBackground( 0, 0, 420, 230, 9200 );

            // ── Header ──────────────────────────────────────────────────────────
            AddLabel( 15, 12, 0x384, "Managing: " + bot.Name );
            AddLabel( 15, 32, 0, bot.PlayerBotProfile.ToString()
                + " - " + bot.PlayerBotExperience.ToString() );
            AddImageTiled( 15, 52, 390, 2, 9304 );

            // ── HP bar ───────────────────────────────────────────────────────────
            AddLabel( 15, 62, 0, "HP:" );
            PlayerBotOwnerGumpHelper.AddBar( this, 42, 65, bot.Hits, bot.HitsMax, 90 );
            AddLabel( 140, 62, 0x40, String.Format( "{0}/{1}", bot.Hits, bot.HitsMax ) );

            // ── Mana bar ─────────────────────────────────────────────────────────
            AddLabel( 220, 62, 0, "Mana:" );
            PlayerBotOwnerGumpHelper.AddBar( this, 258, 65, bot.Mana, bot.ManaMax, 90 );
            AddLabel( 356, 62, 0x40, String.Format( "{0}/{1}", bot.Mana, bot.ManaMax ) );

            // ── Stam bar ─────────────────────────────────────────────────────────
            AddLabel( 15, 82, 0, "Stam:" );
            PlayerBotOwnerGumpHelper.AddBar( this, 48, 85, bot.Stam, bot.StamMax, 90 );
            AddLabel( 148, 82, 0x40, String.Format( "{0}/{1}", bot.Stam, bot.StamMax ) );

            AddImageTiled( 15, 105, 390, 2, 9304 );

            // ── Navigation buttons ────────────────────────────────────────────────
            AddButton( 15,  115, 4005, 4007, 1, GumpButtonType.Reply, 0 );
            AddLabel(  50,  115, 0, "Stats" );

            AddButton( 110, 115, 4005, 4007, 2, GumpButtonType.Reply, 0 );
            AddLabel(  145, 115, 0, "Skills" );

            AddButton( 210, 115, 4005, 4007, 3, GumpButtonType.Reply, 0 );
            AddLabel(  245, 115, 0, "Inventory" );

            AddButton( 315, 115, 4005, 4007, 4, GumpButtonType.Reply, 0 );
            AddLabel(  350, 115, 0, "Equip" );

            AddImageTiled( 15, 145, 390, 2, 9304 );

            // ── Bot List / Release ────────────────────────────────────────────────
            AddButton( 15,  155, 4005, 4007, 5, GumpButtonType.Reply, 0 );
            AddLabel(  50,  155, 0, "< Bot List" );

            AddButton( 295, 155, 4017, 4019, 6, GumpButtonType.Reply, 0 );
            AddLabel(  335, 155, 0x26, "Release Bot" );

            AddImageTiled( 15, 183, 390, 2, 9304 );
        }

        public override void OnResponse( NetState sender, RelayInfo info )
        {
            Mobile from = m_From;

            if ( info.ButtonID == 0 ) return;

            if ( !PlayerBotOwnerGumpHelper.ValidateBot( from, m_Bot ) )
                return;

            switch ( info.ButtonID )
            {
                case 1: // Stats
                    from.SendGump( new PlayerBotStatsGump( from, m_Bot ) );
                    break;

                case 2: // Skills
                    from.SendGump( new PlayerBotSkillsGump( from, m_Bot ) );
                    break;

                case 3: // Inventory (proximity-gated)
                    if ( !from.InRange( m_Bot, 3 ) )
                        from.SendMessage( "You must be closer to your bot to access their belongings." );
                    else
                        from.SendGump( new PlayerBotInventoryGump( from, m_Bot, 0 ) );
                    break;

                case 4: // Equipment (proximity-gated)
                    if ( !from.InRange( m_Bot, 3 ) )
                        from.SendMessage( "You must be closer to your bot to access their belongings." );
                    else
                        from.SendGump( new PlayerBotEquipGump( from, m_Bot ) );
                    break;

                case 5: // Back to bot list
                    from.SendGump( new PlayerBotListGump( from, 0 ) );
                    break;

                case 6: // Release bot — confirmation dialog
                    from.SendGump( new WarningGump(
                        1049583,   // "Are you sure?"
                        0xFFFFFF,
                        String.Format( "Release {0}? They will return to wandering the world.", m_Bot.Name ),
                        0xFFFFFF,
                        320, 200,
                        new WarningGumpCallback( OnReleaseConfirm ),
                        m_Bot ) );
                    break;
            }
        }

        private void OnReleaseConfirm( Mobile from, bool okay, object state )
        {
            if ( !okay )
            {
                from.SendGump( new PlayerBotManageGump( from, m_Bot ) );
                return;
            }

            PlayerBot bot = state as PlayerBot;
            if ( !PlayerBotOwnerGumpHelper.ValidateBot( from, bot ) )
                return;

            bot.SetControlMaster( null );
            bot.ActivityState.SetActivity( BotActivity.Wandering );
            from.SendMessage( "{0} has been released.", bot.Name );
            from.SendGump( new PlayerBotListGump( from, 0 ) );
        }
    }

    // ============================================================
    // PlayerBotStatsGump — read-only persona and combat stats
    // ============================================================
    public class PlayerBotStatsGump : Gump
    {
        private Mobile    m_From;
        private PlayerBot m_Bot;

        public PlayerBotStatsGump( Mobile from, PlayerBot bot ) : base( 50, 50 )
        {
            m_From = from;
            m_Bot  = bot;

            AddPage( 0 );
            AddBackground( 0, 0, 420, 340, 9200 );

            AddLabel( 15, 12, 0x384, "Stats - " + bot.Name );
            AddImageTiled( 15, 32, 390, 2, 9304 );

            int y = 42;

            // ── Persona ──────────────────────────────────────────────────────────
            AddLabel( 15,  y, 0, "Profile:" );
            AddLabel( 140, y, 0x40, bot.PlayerBotProfile.ToString() );
            y += 18;

            AddLabel( 15,  y, 0, "Experience:" );
            AddLabel( 140, y, 0x40, bot.PlayerBotExperience.ToString() );
            y += 18;

            string combatStyle;
            if ( bot.UsesMagic && !bot.PrefersMelee )
                combatStyle = "Mage";
            else if ( bot.UsesMagic && bot.PrefersMelee )
                combatStyle = "Melee / Mage Hybrid";
            else
                combatStyle = "Melee (" + bot.PreferedCombatSkill.ToString() + ")";

            AddLabel( 15,  y, 0, "Combat:" );
            AddLabel( 140, y, 0x40, combatStyle );
            y += 18;

            AddLabel( 15,  y, 0, "Uses Magic:" );
            AddLabel( 140, y, bot.UsesMagic ? 0x40 : 0x26, bot.UsesMagic ? "Yes" : "No" );
            y += 18;

            AddImageTiled( 15, y + 4, 390, 2, 9304 );
            y += 14;

            // ── Attributes ───────────────────────────────────────────────────────
            AddLabel( 15,  y, 0, "Strength:" );
            AddLabel( 140, y, 0x40, bot.Str.ToString() );

            AddLabel( 215, y, 0, "Dexterity:" );
            AddLabel( 315, y, 0x40, bot.Dex.ToString() );
            y += 18;

            AddLabel( 15,  y, 0, "Intelligence:" );
            AddLabel( 140, y, 0x40, bot.Int.ToString() );
            y += 18;

            AddLabel( 15,  y, 0, "Hits:" );
            AddLabel( 140, y, 0x40, String.Format( "{0} / {1}", bot.Hits, bot.HitsMax ) );
            y += 18;

            AddLabel( 15,  y, 0, "Mana:" );
            AddLabel( 140, y, 0x40, String.Format( "{0} / {1}", bot.Mana, bot.ManaMax ) );
            y += 18;

            AddLabel( 15,  y, 0, "Stamina:" );
            AddLabel( 140, y, 0x40, String.Format( "{0} / {1}", bot.Stam, bot.StamMax ) );
            y += 18;

            AddImageTiled( 15, y + 4, 390, 2, 9304 );
            y += 14;

            // ── Activity ─────────────────────────────────────────────────────────
            TimeSpan elapsed = bot.ActivityState.TimeInCurrentActivity;
            string elapsedStr = String.Format( "{0}m {1}s",
                (int)elapsed.TotalMinutes, elapsed.Seconds );

            AddLabel( 15,  y, 0, "Activity:" );
            AddLabel( 140, y, 0x40, bot.ActivityState.Current.ToString()
                + "  (" + elapsedStr + ")" );
            y += 18;

            string groupStr = ( bot.Group != null )
                ? String.Format( "Yes (size {0})", bot.Group.Members.Count )
                : "None";
            AddLabel( 15,  y, 0, "Group:" );
            AddLabel( 140, y, 0x40, groupStr );
            y += 18;

            AddImageTiled( 15, y + 4, 390, 2, 9304 );
            y += 14;

            // ── Back button ──────────────────────────────────────────────────────
            AddButton( 15, y, 4005, 4007, 1, GumpButtonType.Reply, 0 );
            AddLabel(  50, y, 0, "Back" );
        }

        public override void OnResponse( NetState sender, RelayInfo info )
        {
            Mobile from = m_From;
            if ( info.ButtonID == 0 ) return;

            if ( !PlayerBotOwnerGumpHelper.ValidateBot( from, m_Bot ) )
                return;

            from.SendGump( new PlayerBotManageGump( from, m_Bot ) );
        }
    }

    // ============================================================
    // PlayerBotSkillsGump — read-only skill list
    // ============================================================
    public class PlayerBotSkillsGump : Gump
    {
        private Mobile    m_From;
        private PlayerBot m_Bot;

        public PlayerBotSkillsGump( Mobile from, PlayerBot bot ) : base( 50, 50 )
        {
            m_From = from;
            m_Bot  = bot;

            // Build sorted skill list (non-zero only)
            var skills = new List<Skill>();
            for ( int i = 0; i < bot.Skills.Length; i++ )
            {
                Skill sk = bot.Skills[i];
                if ( sk.Base > 0.0 )
                    skills.Add( sk );
            }
            skills.Sort( ( a, b ) => b.Base.CompareTo( a.Base ) );

            int rowCount   = skills.Count;
            int gumpHeight = 80 + Math.Max( 1, rowCount ) * 18 + 50;

            AddPage( 0 );
            AddBackground( 0, 0, 420, gumpHeight, 9200 );

            AddLabel( 15, 12, 0x384, "Skills - " + bot.Name );
            AddImageTiled( 15, 32, 390, 2, 9304 );

            // Column headers
            AddLabel( 15,  42, 0, "Skill" );
            AddLabel( 280, 42, 0, "Value" );
            AddImageTiled( 15, 58, 390, 2, 9304 );

            int y = 65;
            if ( rowCount == 0 )
            {
                AddLabel( 15, y, 0x26, "This bot has no trained skills." );
                y += 18;
            }
            else
            {
                foreach ( Skill sk in skills )
                {
                    string skillName = SkillInfo.Table[(int)sk.SkillID].Name;
                    AddLabel( 15,  y, 0, skillName );
                    AddLabel( 280, y, 0x40, sk.Base.ToString( "F1" ) );
                    y += 18;
                }
            }

            AddImageTiled( 15, y + 4, 390, 2, 9304 );
            y += 14;

            // Total
            AddLabel( 15,  y, 0, "Total:" );
            AddLabel( 280, y, 0x40, String.Format( "{0:F1} / 700.0", bot.Skills.Total ) );
            y += 22;

            // Back
            AddButton( 15, y, 4005, 4007, 1, GumpButtonType.Reply, 0 );
            AddLabel(  50, y, 0, "Back" );
        }

        public override void OnResponse( NetState sender, RelayInfo info )
        {
            Mobile from = m_From;
            if ( info.ButtonID == 0 ) return;

            if ( !PlayerBotOwnerGumpHelper.ValidateBot( from, m_Bot ) )
                return;

            from.SendGump( new PlayerBotManageGump( from, m_Bot ) );
        }
    }

    // ============================================================
    // PlayerBotInventoryGump — backpack contents + item transfer
    // ============================================================
    public class PlayerBotInventoryGump : Gump
    {
        private Mobile    m_From;
        private PlayerBot m_Bot;
        private int       m_Page;

        private const int ItemsPerPage = 12;
        private const int RowHeight    = 18;

        public PlayerBotInventoryGump( Mobile from, PlayerBot bot, int page ) : base( 50, 50 )
        {
            m_From = from;
            m_Bot  = bot;
            m_Page = page;

            // Snapshot backpack contents
            var items = new List<Item>();
            if ( bot.Backpack != null )
            {
                foreach ( Item item in bot.Backpack.Items )
                    items.Add( item );
            }

            int pageCount = Math.Max( 1, ( items.Count + ItemsPerPage - 1 ) / ItemsPerPage );
            if ( m_Page >= pageCount ) m_Page = pageCount - 1;
            if ( m_Page < 0 )         m_Page = 0;

            int startIdx = m_Page * ItemsPerPage;
            int endIdx   = Math.Min( startIdx + ItemsPerPage, items.Count );
            int rowCount = endIdx - startIdx;

            int gumpHeight = 90 + Math.Max( 1, rowCount ) * RowHeight + 90;

            AddPage( 0 );
            AddBackground( 0, 0, 460, gumpHeight, 9200 );

            // ── Header ──────────────────────────────────────────────────────────
            AddLabel( 15, 12, 0x384, "Inventory - " + bot.Name );
            if ( pageCount > 1 )
                AddLabel( 310, 12, 0, String.Format( "Page {0}/{1}", m_Page + 1, pageCount ) );
            AddImageTiled( 15, 32, 430, 2, 9304 );

            // Column headers
            AddLabel( 15,  42, 0, "Item" );
            AddLabel( 310, 42, 0, "Qty" );
            AddLabel( 380, 42, 0, "Action" );
            AddImageTiled( 15, 58, 430, 2, 9304 );

            // ── Rows ─────────────────────────────────────────────────────────────
            int y = 65;
            if ( items.Count == 0 )
            {
                AddLabel( 15, y, 0x26, "Bot's backpack is empty." );
                y += RowHeight;
            }
            else
            {
                for ( int i = startIdx; i < endIdx; i++ )
                {
                    Item item   = items[i];
                    int  rowIdx = i - startIdx;

                    string name = PlayerBotOwnerGumpHelper.GetItemName( item );
                    if ( name.Length > 25 ) name = name.Substring( 0, 25 );

                    AddLabel( 15,  y, 0, name );
                    AddLabel( 310, y, 0x40, item.Amount.ToString() );

                    // [Take] button
                    AddButton( 380, y, 4005, 4007, 100 + rowIdx, GumpButtonType.Reply, 0 );
                    AddLabel(  415, y, 0, "Take" );

                    y += RowHeight;
                }
            }

            // ── Footer ────────────────────────────────────────────────────────────
            int footerY = gumpHeight - 86;
            AddImageTiled( 15, footerY, 430, 2, 9304 );
            footerY += 8;

            // Action buttons
            AddButton( 15,  footerY, 4005, 4007, 1, GumpButtonType.Reply, 0 );
            AddLabel(  50,  footerY, 0x40, "Take All Gold" );

            AddButton( 220, footerY, 4005, 4007, 2, GumpButtonType.Reply, 0 );
            AddLabel(  255, footerY, 0, "Give Item to Bot" );

            footerY += 26;

            // Pagination
            if ( pageCount > 1 )
            {
                if ( m_Page > 0 )
                {
                    AddButton( 15, footerY, 4014, 4016, 3, GumpButtonType.Reply, 0 );
                    AddLabel(  50, footerY, 0, "Prev" );
                }
                AddLabel( 175, footerY, 0, String.Format( "Page {0}/{1}", m_Page + 1, pageCount ) );
                if ( m_Page < pageCount - 1 )
                {
                    AddButton( 310, footerY, 4005, 4007, 4, GumpButtonType.Reply, 0 );
                    AddLabel(  345, footerY, 0, "Next" );
                }
                footerY += 26;
            }

            // Back
            AddButton( 15, footerY, 4005, 4007, 5, GumpButtonType.Reply, 0 );
            AddLabel(  50, footerY, 0, "Back" );
        }

        public override void OnResponse( NetState sender, RelayInfo info )
        {
            Mobile from = m_From;

            if ( info.ButtonID == 0 ) return;

            if ( !PlayerBotOwnerGumpHelper.ValidateBot( from, m_Bot ) )
                return;

            // Proximity check for all actions
            if ( !from.InRange( m_Bot, 3 ) )
            {
                from.SendMessage( "You are too far away." );
                from.SendGump( new PlayerBotInventoryGump( from, m_Bot, m_Page ) );
                return;
            }

            switch ( info.ButtonID )
            {
                case 1: // Take All Gold
                    TakeAllGold( from );
                    break;

                case 2: // Give Item to Bot
                    from.Target = new PlayerBotGiveTarget( from, m_Bot, m_Page );
                    from.SendMessage( "Target an item in your backpack to give to " + m_Bot.Name + "." );
                    return; // don't reopen yet; target handler will

                case 3: // Prev page
                    from.SendGump( new PlayerBotInventoryGump( from, m_Bot, m_Page - 1 ) );
                    return;

                case 4: // Next page
                    from.SendGump( new PlayerBotInventoryGump( from, m_Bot, m_Page + 1 ) );
                    return;

                case 5: // Back
                    from.SendGump( new PlayerBotManageGump( from, m_Bot ) );
                    return;

                default:
                    if ( info.ButtonID >= 100 )
                        TakeItem( from, info.ButtonID - 100 );
                    break;
            }

            from.SendGump( new PlayerBotInventoryGump( from, m_Bot, m_Page ) );
        }

        private void TakeItem( Mobile from, int rowIdx )
        {
            if ( m_Bot.Backpack == null ) return;

            var items = new List<Item>( m_Bot.Backpack.Items );
            int idx   = m_Page * ItemsPerPage + rowIdx;

            if ( idx < 0 || idx >= items.Count ) return;

            Item item = items[idx];
            if ( item == null || item.Deleted ) return;
            if ( item.Parent != m_Bot.Backpack ) return;

            if ( !from.PlaceInBackpack( item ) )
                from.SendMessage( "Your pack is full." );
        }

        private void TakeAllGold( Mobile from )
        {
            if ( m_Bot.Backpack == null ) return;

            var goldItems = new List<Item>();
            foreach ( Item item in m_Bot.Backpack.Items )
            {
                if ( item is Gold )
                    goldItems.Add( item );
            }

            if ( goldItems.Count == 0 )
            {
                from.SendMessage( m_Bot.Name + " has no gold." );
                return;
            }

            int total = 0;
            foreach ( Item g in goldItems )
                total += g.Amount;

            foreach ( Item g in goldItems )
                g.Delete();

            from.AddToBackpack( new Gold( total ) );
            from.SendMessage( "You take {0} gold from {1}.", total, m_Bot.Name );
        }
    }

    // ============================================================
    // PlayerBotEquipGump — equipment slots + equip/unequip
    // ============================================================
    public class PlayerBotEquipGump : Gump
    {
        private Mobile    m_From;
        private PlayerBot m_Bot;

        // Ordered visible equipment slots: (Layer, DisplayName)
        private static readonly KeyValuePair<Layer, string>[] s_Slots = new KeyValuePair<Layer, string>[]
        {
            new KeyValuePair<Layer, string>( Layer.Helm,        "Head" ),
            new KeyValuePair<Layer, string>( Layer.InnerTorso,  "Chest" ),
            new KeyValuePair<Layer, string>( Layer.Arms,        "Arms" ),
            new KeyValuePair<Layer, string>( Layer.Gloves,      "Gloves" ),
            new KeyValuePair<Layer, string>( Layer.Neck,        "Gorget / Neck" ),
            new KeyValuePair<Layer, string>( Layer.InnerLegs,   "Inner Legs" ),
            new KeyValuePair<Layer, string>( Layer.Pants,       "Pants" ),
            new KeyValuePair<Layer, string>( Layer.OuterLegs,   "Outer Legs" ),
            new KeyValuePair<Layer, string>( Layer.OuterTorso,  "Outer Torso" ),
            new KeyValuePair<Layer, string>( Layer.MiddleTorso, "Middle Torso" ),
            new KeyValuePair<Layer, string>( Layer.OneHanded,   "Right Hand" ),
            new KeyValuePair<Layer, string>( Layer.TwoHanded,   "Two-Handed" ),
            new KeyValuePair<Layer, string>( Layer.Cloak,       "Cloak" ),
            new KeyValuePair<Layer, string>( Layer.Shirt,       "Shirt" ),
            new KeyValuePair<Layer, string>( Layer.Shoes,       "Shoes" ),
            new KeyValuePair<Layer, string>( Layer.Waist,       "Waist" ),
            new KeyValuePair<Layer, string>( Layer.Ring,        "Ring" ),
            new KeyValuePair<Layer, string>( Layer.Bracelet,    "Bracelet" ),
            new KeyValuePair<Layer, string>( Layer.Earrings,    "Earrings" ),
        };

        public PlayerBotEquipGump( Mobile from, PlayerBot bot ) : base( 50, 50 )
        {
            m_From = from;
            m_Bot  = bot;

            int rowCount   = s_Slots.Length;
            int gumpHeight = 90 + rowCount * 18 + 60;

            AddPage( 0 );
            AddBackground( 0, 0, 460, gumpHeight, 9200 );

            // ── Header ──────────────────────────────────────────────────────────
            AddLabel( 15, 12, 0x384, "Equipment - " + bot.Name );
            AddImageTiled( 15, 32, 430, 2, 9304 );

            AddLabel( 15,  42, 0, "Slot" );
            AddLabel( 160, 42, 0, "Item" );
            AddLabel( 390, 42, 0, "Act." );
            AddImageTiled( 15, 58, 430, 2, 9304 );

            // ── Equipment rows ────────────────────────────────────────────────────
            int y = 65;
            for ( int i = 0; i < s_Slots.Length; i++ )
            {
                Layer  layer = s_Slots[i].Key;
                string label = s_Slots[i].Value;
                Item   item  = bot.FindItemOnLayer( layer );

                AddLabel( 15, y, 0, label );

                if ( item != null && !item.Deleted )
                {
                    string name = PlayerBotOwnerGumpHelper.GetItemName( item );
                    if ( name.Length > 24 ) name = name.Substring( 0, 24 );
                    AddLabel( 160, y, 0x40, name );

                    // [Take] button
                    AddButton( 390, y, 4005, 4007, 100 + i, GumpButtonType.Reply, 0 );
                    AddLabel(  425, y, 0, "Take" );
                }
                else
                {
                    AddLabel( 160, y, 0x26, "(empty)" );
                }

                y += 18;
            }

            // ── Footer ────────────────────────────────────────────────────────────
            int footerY = gumpHeight - 52;
            AddImageTiled( 15, footerY, 430, 2, 9304 );
            footerY += 8;

            AddButton( 15,  footerY, 4005, 4007, 1, GumpButtonType.Reply, 0 );
            AddLabel(  50,  footerY, 0, "Equip Item" );
            AddLabel(  150, footerY, 0, "(target from your pack)" );

            footerY += 26;

            AddButton( 15, footerY, 4005, 4007, 2, GumpButtonType.Reply, 0 );
            AddLabel(  50, footerY, 0, "Back" );
        }

        public override void OnResponse( NetState sender, RelayInfo info )
        {
            Mobile from = m_From;

            if ( info.ButtonID == 0 ) return;

            if ( !PlayerBotOwnerGumpHelper.ValidateBot( from, m_Bot ) )
                return;

            // Proximity check
            if ( !from.InRange( m_Bot, 3 ) )
            {
                from.SendMessage( "You are too far away." );
                from.SendGump( new PlayerBotEquipGump( from, m_Bot ) );
                return;
            }

            switch ( info.ButtonID )
            {
                case 1: // Equip Item — open target
                    from.Target = new PlayerBotEquipTarget( from, m_Bot );
                    from.SendMessage( "Target an item from your backpack to equip on " + m_Bot.Name + "." );
                    return; // don't reopen yet

                case 2: // Back
                    from.SendGump( new PlayerBotManageGump( from, m_Bot ) );
                    return;

                default:
                    if ( info.ButtonID >= 100 )
                    {
                        int slotIdx = info.ButtonID - 100;
                        if ( slotIdx >= 0 && slotIdx < s_Slots.Length )
                            TakeEquippedItem( from, s_Slots[slotIdx].Key );
                    }
                    break;
            }

            from.SendGump( new PlayerBotEquipGump( from, m_Bot ) );
        }

        private void TakeEquippedItem( Mobile from, Layer layer )
        {
            Item item = m_Bot.FindItemOnLayer( layer );
            if ( item == null || item.Deleted ) return;

            m_Bot.RemoveItem( item );

            if ( !from.PlaceInBackpack( item ) )
            {
                // Pack full — put it back on the bot
                m_Bot.EquipItem( item );
                from.SendMessage( "Your pack is full." );
            }
        }
    }

    // ============================================================
    // PlayerBotGiveTarget — lets the player give an item to a bot
    // ============================================================
    public class PlayerBotGiveTarget : Target
    {
        private Mobile    m_From;
        private PlayerBot m_Bot;
        private int       m_Page;

        public PlayerBotGiveTarget( Mobile from, PlayerBot bot, int page )
            : base( 2, false, TargetFlags.None )
        {
            m_From = from;
            m_Bot  = bot;
            m_Page = page;
        }

        protected override void OnTarget( Mobile from, object targeted )
        {
            Item item = targeted as Item;

            if ( item == null || item.Deleted )
            {
                from.SendMessage( "That is not a valid item." );
                from.SendGump( new PlayerBotInventoryGump( from, m_Bot, m_Page ) );
                return;
            }

            if ( !PlayerBotOwnerGumpHelper.ValidateBot( from, m_Bot ) )
                return;

            if ( !from.InRange( m_Bot, 3 ) )
            {
                from.SendMessage( "You are too far from your bot." );
                from.SendGump( new PlayerBotInventoryGump( from, m_Bot, m_Page ) );
                return;
            }

            // Item must be in player's top-level backpack
            if ( from.Backpack == null || item.Parent != from.Backpack )
            {
                from.SendMessage( "That item must be in your backpack." );
                from.SendGump( new PlayerBotInventoryGump( from, m_Bot, m_Page ) );
                return;
            }

            if ( m_Bot.Backpack == null )
            {
                from.SendMessage( m_Bot.Name + " has no backpack." );
                from.SendGump( new PlayerBotInventoryGump( from, m_Bot, m_Page ) );
                return;
            }

            // Check bot backpack capacity (standard RunUO max 125)
            if ( m_Bot.Backpack.Items.Count >= 125 )
            {
                from.SendMessage( m_Bot.Name + "'s pack is full." );
                from.SendGump( new PlayerBotInventoryGump( from, m_Bot, m_Page ) );
                return;
            }

            from.Backpack.RemoveItem( item );
            m_Bot.Backpack.DropItem( item );

            string name = PlayerBotOwnerGumpHelper.GetItemName( item );
            from.SendMessage( "You give {0} to {1}.", name, m_Bot.Name );
            from.SendGump( new PlayerBotInventoryGump( from, m_Bot, m_Page ) );
        }

        protected override void OnTargetCancel( Mobile from, TargetCancelType cancelType )
        {
            from.SendGump( new PlayerBotInventoryGump( from, m_Bot, m_Page ) );
        }
    }

    // ============================================================
    // PlayerBotEquipTarget — lets the player equip an item on a bot
    // ============================================================
    public class PlayerBotEquipTarget : Target
    {
        private Mobile    m_From;
        private PlayerBot m_Bot;

        public PlayerBotEquipTarget( Mobile from, PlayerBot bot )
            : base( 2, false, TargetFlags.None )
        {
            m_From = from;
            m_Bot  = bot;
        }

        protected override void OnTarget( Mobile from, object targeted )
        {
            Item item = targeted as Item;

            if ( item == null || item.Deleted )
            {
                from.SendMessage( "That is not a valid item." );
                from.SendGump( new PlayerBotEquipGump( from, m_Bot ) );
                return;
            }

            if ( !PlayerBotOwnerGumpHelper.ValidateBot( from, m_Bot ) )
                return;

            if ( !from.InRange( m_Bot, 3 ) )
            {
                from.SendMessage( "You are too far from your bot." );
                from.SendGump( new PlayerBotEquipGump( from, m_Bot ) );
                return;
            }

            // Item must be in player's top-level backpack
            if ( from.Backpack == null || item.Parent != from.Backpack )
            {
                from.SendMessage( "That item must be in your backpack." );
                from.SendGump( new PlayerBotEquipGump( from, m_Bot ) );
                return;
            }

            // Must be a wearable item
            if ( !( item is BaseArmor ) && !( item is BaseWeapon ) && !( item is BaseClothing ) )
            {
                from.SendMessage( "That cannot be equipped." );
                from.SendGump( new PlayerBotEquipGump( from, m_Bot ) );
                return;
            }

            Layer itemLayer = item.Layer;

            // Clear occupied slot(s)
            ClearSlot( m_Bot, itemLayer );

            // For 2H weapons, also clear OneHanded; for OneHanded items, also clear TwoHanded
            if ( itemLayer == Layer.TwoHanded )
                ClearSlot( m_Bot, Layer.OneHanded );
            else if ( itemLayer == Layer.OneHanded )
                ClearSlot( m_Bot, Layer.TwoHanded );

            // Move item from player's pack to bot
            from.Backpack.RemoveItem( item );

            if ( !m_Bot.EquipItem( item ) )
            {
                // Equip failed (e.g. non-equippable layer) — return to player
                from.AddToBackpack( item );
                from.SendMessage( "That could not be equipped." );
                from.SendGump( new PlayerBotEquipGump( from, m_Bot ) );
                return;
            }

            string name = PlayerBotOwnerGumpHelper.GetItemName( item );
            from.SendMessage( "You equip {0} on {1}.", name, m_Bot.Name );
            from.SendGump( new PlayerBotEquipGump( from, m_Bot ) );
        }

        protected override void OnTargetCancel( Mobile from, TargetCancelType cancelType )
        {
            from.SendGump( new PlayerBotEquipGump( from, m_Bot ) );
        }

        private static void ClearSlot( PlayerBot bot, Layer layer )
        {
            Item existing = bot.FindItemOnLayer( layer );
            if ( existing != null && !existing.Deleted )
            {
                bot.RemoveItem( existing );
                if ( bot.Backpack != null )
                    bot.Backpack.DropItem( existing );
            }
        }
    }
}
