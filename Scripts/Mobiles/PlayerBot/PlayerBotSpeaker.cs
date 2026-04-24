using System;
using Server;

namespace Server.Mobiles
{
    public static class PlayerBotSpeaker
    {
        private static readonly string[] s_PKLines = new string[]
        {
            "I smell blood.",
            "Stay out of my way.",
            "Red names make red stains.",
            "Tonight we hunt.",
            "None shall pass.",
            "Your head will make a fine trophy.",
            "I've killed better men than you.",
        };

        private static readonly string[] s_CrafterLines = new string[]
        {
            "These ingots won't smelt themselves.",
            "A good craftsman never blames his tools.",
            "Looking for quality work?",
            "The forge calls.",
            "Fine blade, if I do say so.",
            "*pounds the anvil rhythmically*",
        };

        private static readonly string[] s_AdventurerLines = new string[]
        {
            "Good hunting today.",
            "Seen any orcs nearby?",
            "The roads are dangerous these days.",
            "On my way to the dungeon.",
            "Careful of the liches down below.",
            "I could use a few more coins.",
            "Adventure awaits!",
        };

        private static readonly string[] s_CombatLines = new string[]
        {
            "You'll regret this!",
            "For glory!",
            "En garde!",
            "Die!",
            "Take that!",
            "Is that all you have?",
        };

        private static readonly string[] s_TownLines = new string[]
        {
            "Fine day, isn't it?",
            "Britain is always busy.",
            "I need to restock before heading out.",
            "Have you heard any news?",
            "The bank is just around the corner.",
        };

        private static readonly string[] s_FleeLines = new string[]
        {
            "I'll be back!",
            "Retreat!",
            "This isn't over!",
            "*runs*",
        };

        public static void SayContextual( PlayerBot bot )
        {
            string[] pool;

            switch ( bot.CurrentActivity )
            {
                case BotActivity.Combat:
                    pool = s_CombatLines;
                    break;
                case BotActivity.Crafting:
                    pool = s_CrafterLines;
                    break;
                case BotActivity.TownVisit:
                    pool = s_TownLines;
                    break;
                case BotActivity.Fleeing:
                    pool = s_FleeLines;
                    break;
                default:
                    switch ( bot.PlayerBotProfile )
                    {
                        case PlayerBotPersona.PlayerBotProfile.PlayerKiller:
                            pool = s_PKLines;
                            break;
                        case PlayerBotPersona.PlayerBotProfile.Crafter:
                            pool = s_CrafterLines;
                            break;
                        default:
                            pool = s_AdventurerLines;
                            break;
                    }
                    break;
            }

            if ( pool.Length > 0 )
                bot.Say( pool[Utility.Random( pool.Length )] );
        }

        public static void ReactToPlayer( PlayerBot bot, Mobile player, string speech )
        {
            string lower = speech.ToLower();

            if ( lower.IndexOf( "hello" ) >= 0 || lower.IndexOf( "hail" ) >= 0 || lower.IndexOf( "hi " ) >= 0 )
            {
                bot.Say( "Greetings, " + player.Name + "." );
                return;
            }

            if ( lower.IndexOf( "where" ) >= 0 )
            {
                bot.Say( "I roam where I will." );
                return;
            }

            if ( lower.IndexOf( "help" ) >= 0 )
            {
                bot.Say( "I cannot stop now." );
                return;
            }
        }
    }
}
