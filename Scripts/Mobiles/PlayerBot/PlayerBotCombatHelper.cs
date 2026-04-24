using System;
using Server;
using Server.Spells;
using Server.Spells.First;
using Server.Spells.Second;
using Server.Spells.Third;
using Server.Spells.Fourth;
using Server.Spells.Fifth;
using Server.Spells.Sixth;
using Server.Spells.Seventh;

namespace Server.Mobiles
{
    public static class PlayerBotCombatHelper
    {
        // Mana cost floors per circle (used to avoid picking spells we can't afford)
        private static readonly int[] s_CircleMana = new int[]
        {
            0,   // index 0 unused
            4,   // circle 1
            6,   // circle 2
            9,   // circle 3
            11,  // circle 4
            14,  // circle 5
            20,  // circle 6
            40,  // circle 7
            50   // circle 8
        };

        // Choose an offensive spell scaled to the bot's Magery skill.
        // Returns null if no spell can be cast right now.
        public static Spell ChooseOffensiveSpell( PlayerBot bot, Mobile target )
        {
            int mana   = bot.Mana;
            double mag = bot.Skills[SkillName.Magery].Value;

            if ( mana < 4 ) return null;

            // Max circle the bot can cast based on Magery (mirrors MageAI)
            int maxCircle = (int)(mag / 87.5 * 8.0);
            if ( maxCircle > 8 ) maxCircle = 8;
            if ( maxCircle < 1 ) maxCircle = 1;

            // Walk down until we find a circle we can afford
            while ( maxCircle > 1 && s_CircleMana[maxCircle] > mana / 2 )
                maxCircle--;

            switch ( Utility.Random( maxCircle ) + 1 )
            {
                case 1:
                    switch ( Utility.Random( 2 ) )
                    {
                        case 0:  return new MagicArrowSpell( bot, null );
                        default: return new WeakenSpell( bot, null );
                    }

                case 2:
                    return new HarmSpell( bot, null );

                case 3:
                    return Utility.RandomBool()
                        ? (Spell)new FireballSpell( bot, null )
                        : (Spell)new PoisonSpell( bot, null );

                case 4:
                    return new LightningSpell( bot, null );

                case 5:
                    return new MindBlastSpell( bot, null );

                case 6:
                    return Utility.RandomBool()
                        ? (Spell)new EnergyBoltSpell( bot, null )
                        : (Spell)new ExplosionSpell( bot, null );

                case 7:
                case 8:
                    return new FlameStrikeSpell( bot, null );

                default:
                    return new MagicArrowSpell( bot, null );
            }
        }

        // Attempt self-cure or self-heal.  Sets nextCastTime on success.
        // Returns true if a heal spell was initiated.
        public static bool TryCastHeal( PlayerBot bot, ref DateTime nextCastTime )
        {
            if ( bot.Skills[SkillName.Magery].Value < 10.0 )
                return false;

            // Cure poison — top priority
            if ( bot.Poisoned && bot.Mana >= 6 )
            {
                Spell cure = new CureSpell( bot, null );
                if ( cure.Cast() )
                {
                    nextCastTime = DateTime.Now + cure.GetCastDelay()
                                 + TimeSpan.FromSeconds( 1.0 );
                    return true;
                }
            }

            // Greater Heal
            if ( bot.Hits < bot.HitsMax - 50 && bot.Mana >= 11 )
            {
                Spell gh = new GreaterHealSpell( bot, null );
                if ( gh.Cast() )
                {
                    nextCastTime = DateTime.Now + gh.GetCastDelay()
                                 + TimeSpan.FromSeconds( 1.5 );
                    return true;
                }
            }

            // Lesser Heal
            if ( bot.Hits < bot.HitsMax - 15 && bot.Mana >= 4 )
            {
                Spell lh = new HealSpell( bot, null );
                if ( lh.Cast() )
                {
                    nextCastTime = DateTime.Now + lh.GetCastDelay()
                                 + TimeSpan.FromSeconds( 1.0 );
                    return true;
                }
            }

            return false;
        }
    }
}
