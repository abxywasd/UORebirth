using System;
using System.Collections.Generic;
using Server;
using Server.Items;
using Server.Misc;

namespace Server.Mobiles
{
    // Simulates crafting without going through the gump-driven CraftSystem UI.
    // Called each AI tick while the bot's activity is Crafting.
    public static class PlayerBotCrafter
    {
        private static readonly TimeSpan CraftDelay = TimeSpan.FromSeconds( 5.0 );

        public static bool DoCraftTick( PlayerBot bot )
        {
            if ( DateTime.Now < bot.NextCraftTime )
                return true; // still waiting on craft delay, stand still

            Container pack = bot.Backpack;
            if ( pack == null )
            {
                bot.ActivityState.SetActivity( BotActivity.TownVisit );
                return true;
            }

            // Check for materials
            IronIngot ingot = pack.FindItemByType( typeof(IronIngot) ) as IronIngot;
            if ( ingot == null || ingot.Amount < 10 )
            {
                // Out of materials — travel to the nearest crafter town to "resupply"
                BotWaypoint wp = PlayerBotNavigator.PickDestination( PlayerBotPersona.PlayerBotProfile.Crafter );
                if ( wp != null )
                {
                    bot.SetAfterTravelActivity( BotActivity.TownVisit );
                    List<BotWaypoint> route = PlayerBotNavigator.ComputeRoute( bot.Location, bot.Map, wp );
                    if ( route != null && route.Count > 0 )
                        bot.ActivityState.SetTravelRoute( wp, route );
                    else
                        bot.ActivityState.SetTravelDirect( wp );
                }
                else
                {
                    bot.ActivityState.SetActivity( BotActivity.Wandering );
                }
                return true;
            }

            // Attempt craft
            double skill  = bot.Skills[SkillName.Blacksmith].Value;
            double chance = skill / 50.0;
            if ( chance > 0.95 ) chance = 0.95;
            if ( chance < 0.05 ) chance = 0.05;

            ingot.Amount -= 10;
            if ( ingot.Amount <= 0 )
                ingot.Delete();

            if ( Utility.RandomDouble() < chance )
            {
                Item made = PickCraftOutput( bot );
                if ( !bot.AddToBackpack( made ) )
                    made.Delete();
                else
                    bot.Say( "*pounds the anvil*" );
            }

            // Skill gain regardless of success (mirrors real crafting)
            SkillCheck.Gain( bot, bot.Skills[SkillName.Blacksmith] );

            bot.NextCraftTime = DateTime.Now + CraftDelay;
            return true;
        }

        private static Item PickCraftOutput( PlayerBot bot )
        {
            double skill = bot.Skills[SkillName.Blacksmith].Value;

            // Produce better items as skill grows
            if ( skill >= 90.0 )
            {
                switch ( Utility.Random( 3 ) )
                {
                    case 0: return new Halberd();
                    case 1: return new PlateChest();
                    default: return new Longsword();
                }
            }
            else if ( skill >= 60.0 )
            {
                switch ( Utility.Random( 3 ) )
                {
                    case 0: return new Broadsword();
                    case 1: return new ChainChest();
                    default: return new WarAxe();
                }
            }
            else
            {
                switch ( Utility.Random( 3 ) )
                {
                    case 0: return new Dagger();
                    case 1: return new Mace();
                    default: return new RingmailChest();
                }
            }
        }
    }
}
