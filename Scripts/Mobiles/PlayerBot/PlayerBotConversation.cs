using System;
using Server;

namespace Server.Mobiles
{
    internal class ConversationTopic
    {
        public readonly string[] Lines;
        public ConversationTopic( params string[] lines ) { Lines = lines; }
    }

    public static class PlayerBotConversation
    {
        private static readonly ConversationTopic[] s_Topics = new ConversationTopic[]
        {
            new ConversationTopic(
                "I was thinking about heading to Covetous later.",
                "Covetous? I heard the upper levels are crawling with liches.",
                "Aye, but the loot is worth it if you have a decent mage.",
                "Maybe. Last time I barely made it out with my life."
            ),
            new ConversationTopic(
                "Have you been to Deceit recently?",
                "Not since last week. Why, is something happening there?",
                "Someone said there is a red camping the entrance every evening.",
                "Typical. Always a murderer lurking when you least expect it."
            ),
            new ConversationTopic(
                "I am almost out of black pearls.",
                "Try the mage shop near the Britain bank, they usually have some.",
                "They were sold out yesterday. Demand must be high.",
                "Reagent prices keep going up. Someone has been buying them all out."
            ),
            new ConversationTopic(
                "Just got back from Destard.",
                "How did it go? Dragons are no joke.",
                "Lost half my reagents but came back with some nice dragon scales.",
                "Nice. I might need those for my armoursmith."
            ),
            new ConversationTopic(
                "Anyone else noticed more reds on the roads lately?",
                "Yeah, I got jumped near the Trinsic crossroads yesterday.",
                "I try to stick to the guarded areas when I can.",
                "Smart. Britain bank is the safest spot but it gets crowded."
            ),
            new ConversationTopic(
                "I am working on getting my smithing up to grandmaster.",
                "How close are you? That last stretch is brutal.",
                "About 80 right now. The skill gains really slow down.",
                "Hang in there. I hit grandmaster last month and it felt great."
            ),
            new ConversationTopic(
                "Have you tried the dungeon Shame?",
                "Once. The earth elementals hit really hard.",
                "Good for mining iron ore though, if you bring protection.",
                "True. I never thought of it that way."
            ),
            new ConversationTopic(
                "Moonglow is a nice town if you need a break from fighting.",
                "I like it. The mage guild there has good spell components.",
                "And it is quiet. Not as busy as Britain.",
                "Exactly. Sometimes you just want to bank in peace."
            ),
            new ConversationTopic(
                "I need mandrake root and I am completely out.",
                "There is an alchemist in Minoc who keeps a good stock.",
                "That is quite a trek from here.",
                "Worth it if you are planning any serious dungeon runs."
            ),
            new ConversationTopic(
                "Heard about the guild war near Wrong?",
                "Yeah, they have been fighting there for days.",
                "I stay out of guild politics myself.",
                "Wise. There is enough danger without picking sides."
            ),
        };

        // Attempt to start a conversation between initiator and a nearby idle town bot.
        // Returns true if a conversation was started.
        public static bool TryStartConversation( PlayerBot initiator )
        {
            if ( initiator.InConversation || initiator.Deleted )
                return false;

            PlayerBot partner = null;
            IPooledEnumerable eable = initiator.Map.GetMobilesInRange( initiator.Location, 6 );
            foreach ( Mobile m in eable )
            {
                PlayerBot other = m as PlayerBot;
                if ( other == null || other == initiator || other.Deleted || other.Controled || other.InConversation )
                    continue;
                if ( other.CurrentActivity != BotActivity.TownVisit && other.CurrentActivity != BotActivity.Wandering )
                    continue;
                partner = other;
                break;
            }
            eable.Free();

            if ( partner == null )
                return false;

            ConversationTopic topic = s_Topics[Utility.Random( s_Topics.Length )];
            initiator.InConversation = true;
            partner.InConversation   = true;
            ScheduleLine( initiator, partner, topic, 0 );
            return true;
        }

        private static void ScheduleLine( PlayerBot speaker, PlayerBot listener, ConversationTopic topic, int index )
        {
            double delay = 3.0 + Utility.Random( 3 );
            Timer.DelayCall( TimeSpan.FromSeconds( delay ), delegate()
            {
                if ( speaker.Deleted || listener.Deleted )
                {
                    if ( !speaker.Deleted )  speaker.InConversation  = false;
                    if ( !listener.Deleted ) listener.InConversation = false;
                    return;
                }

                speaker.Say( topic.Lines[index] );

                if ( index + 1 < topic.Lines.Length )
                    ScheduleLine( listener, speaker, topic, index + 1 );
                else
                {
                    speaker.InConversation  = false;
                    listener.InConversation = false;
                }
            } );
        }
    }
}
