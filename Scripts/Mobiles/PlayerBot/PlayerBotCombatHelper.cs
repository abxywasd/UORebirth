using System;
using System.Collections.Generic;
using Server;
using Server.Items;
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

        // Spell IDs (from Initializer.cs) for every spell used by bots.
        private const int ID_Heal         = 3;
        private const int ID_MagicArrow   = 4;
        private const int ID_Weaken       = 7;
        private const int ID_Cure         = 10;
        private const int ID_Harm         = 11;
        private const int ID_Fireball     = 17;
        private const int ID_Poison       = 19;
        private const int ID_GreaterHeal  = 28;
        private const int ID_Lightning    = 29;
        private const int ID_MindBlast    = 36;
        private const int ID_EnergyBolt   = 41;
        private const int ID_Explosion    = 42;
        private const int ID_FlameStrike  = 50;

        // Choose an offensive spell scaled to the bot's Magery skill.
        // Only returns a spell the bot has in its spellbook AND has reagents for.
        // Returns null if no valid spell is available right now.
        public static Spell ChooseOffensiveSpell( PlayerBot bot, Mobile target )
        {
            int mana     = bot.Mana;
            double mag   = bot.Skills[SkillName.Magery].Value;
            Container pack = bot.Backpack;

            if ( mana < 4 || pack == null ) return null;

            int maxCircle = (int)(mag / 87.5 * 8.0);
            if ( maxCircle > 8 ) maxCircle = 8;
            if ( maxCircle < 1 ) maxCircle = 1;

            // Walk down until we find a circle we can afford
            while ( maxCircle > 1 && s_CircleMana[maxCircle] > mana / 2 )
                maxCircle--;

            // Build list of spells available right now (spellbook + reagents + mana)
            var candidates = new List<Spell>( 4 );

            if ( maxCircle >= 1 )
            {
                TryAdd( bot, pack, candidates, ID_MagicArrow, new MagicArrowSpell( bot, null ) );
                TryAdd( bot, pack, candidates, ID_Weaken,     new WeakenSpell( bot, null ) );
            }
            if ( maxCircle >= 2 )
                TryAdd( bot, pack, candidates, ID_Harm,       new HarmSpell( bot, null ) );
            if ( maxCircle >= 3 )
            {
                TryAdd( bot, pack, candidates, ID_Fireball,   new FireballSpell( bot, null ) );
                TryAdd( bot, pack, candidates, ID_Poison,     new PoisonSpell( bot, null ) );
            }
            if ( maxCircle >= 4 )
                TryAdd( bot, pack, candidates, ID_Lightning,  new LightningSpell( bot, null ) );
            if ( maxCircle >= 5 )
                TryAdd( bot, pack, candidates, ID_MindBlast,  new MindBlastSpell( bot, null ) );
            if ( maxCircle >= 6 )
            {
                TryAdd( bot, pack, candidates, ID_EnergyBolt, new EnergyBoltSpell( bot, null ) );
                TryAdd( bot, pack, candidates, ID_Explosion,  new ExplosionSpell( bot, null ) );
            }
            if ( maxCircle >= 7 )
                TryAdd( bot, pack, candidates, ID_FlameStrike, new FlameStrikeSpell( bot, null ) );

            if ( candidates.Count == 0 )
                return null;

            return candidates[Utility.Random( candidates.Count )];
        }

        // Adds the spell to candidates only if the bot has it in a spellbook
        // and has all required reagents in their backpack.
        private static void TryAdd( PlayerBot bot, Container pack, List<Spell> candidates, int spellID, Spell spell )
        {
            Spellbook book = Spellbook.Find( bot, spellID );
            if ( book == null || !book.HasSpell( spellID ) )
                return;

            if ( HasReagents( pack, spell.Reagents ) )
                candidates.Add( spell );
        }

        // Returns true if the container holds at least one of each required reagent type.
        private static bool HasReagents( Container pack, Type[] reagents )
        {
            if ( reagents == null || reagents.Length == 0 )
                return true;

            foreach ( Type t in reagents )
            {
                if ( pack.GetAmount( t ) < 1 )
                    return false;
            }

            return true;
        }

        // Returns true if the bot can cast at least one heal/cure spell for itself right now
        // (spellbook present, reagents in pack, enough mana, condition met).
        // Use this to avoid stashing weapons when no cast would succeed.
        public static bool HasHealSpellReady( PlayerBot bot )
        {
            if ( bot.Skills[SkillName.Magery].Value < 10.0 ) return false;
            Container pack = bot.Backpack;
            if ( pack == null ) return false;

            if ( bot.Poisoned && bot.Mana >= s_CircleMana[2] )
                if ( HasSpellAndReagents( bot, ID_Cure, new CureSpell( bot, null ), pack ) ) return true;
            if ( bot.Hits < bot.HitsMax - 50 && bot.Mana >= s_CircleMana[4] )
                if ( HasSpellAndReagents( bot, ID_GreaterHeal, new GreaterHealSpell( bot, null ), pack ) ) return true;
            if ( bot.Hits < bot.HitsMax - 15 && bot.Mana >= s_CircleMana[1] )
                if ( HasSpellAndReagents( bot, ID_Heal, new HealSpell( bot, null ), pack ) ) return true;
            return false;
        }

        // Same check but for an external target (master or ally bot).
        public static bool HasHealSpellReadyFor( PlayerBot bot, Mobile target )
        {
            if ( bot.Skills[SkillName.Magery].Value < 10.0 ) return false;
            Container pack = bot.Backpack;
            if ( pack == null ) return false;

            if ( target.Poisoned && bot.Mana >= s_CircleMana[2] )
                if ( HasSpellAndReagents( bot, ID_Cure, new CureSpell( bot, null ), pack ) ) return true;
            if ( target.Hits < target.HitsMax - 30 && bot.Mana >= s_CircleMana[4] )
                if ( HasSpellAndReagents( bot, ID_GreaterHeal, new GreaterHealSpell( bot, null ), pack ) ) return true;
            if ( target.Hits < target.HitsMax - 10 && bot.Mana >= s_CircleMana[1] )
                if ( HasSpellAndReagents( bot, ID_Heal, new HealSpell( bot, null ), pack ) ) return true;
            return false;
        }

        // Attempt self-cure or self-heal.  Sets nextCastTime on success.
        // Returns true if a heal spell was initiated.
        public static bool TryCastHeal( PlayerBot bot, ref DateTime nextCastTime )
        {
            if ( bot.Skills[SkillName.Magery].Value < 10.0 )
                return false;

            Container pack = bot.Backpack;
            if ( pack == null )
                return false;

            // Cure poison — top priority
            if ( bot.Poisoned && bot.Mana >= s_CircleMana[2] )
            {
                Spell cure = new CureSpell( bot, null );
                if ( HasSpellAndReagents( bot, ID_Cure, cure, pack ) && cure.Cast() )
                {
                    nextCastTime = DateTime.Now + cure.GetCastDelay()
                                 + TimeSpan.FromSeconds( 0.25 );
                    return true;
                }
            }

            // Greater Heal
            if ( bot.Hits < bot.HitsMax - 50 && bot.Mana >= s_CircleMana[4] )
            {
                Spell gh = new GreaterHealSpell( bot, null );
                if ( HasSpellAndReagents( bot, ID_GreaterHeal, gh, pack ) && gh.Cast() )
                {
                    nextCastTime = DateTime.Now + gh.GetCastDelay()
                                 + TimeSpan.FromSeconds( 0.25 );
                    return true;
                }
            }

            // Lesser Heal
            if ( bot.Hits < bot.HitsMax - 15 && bot.Mana >= s_CircleMana[1] )
            {
                Spell lh = new HealSpell( bot, null );
                if ( HasSpellAndReagents( bot, ID_Heal, lh, pack ) && lh.Cast() )
                {
                    nextCastTime = DateTime.Now + lh.GetCastDelay()
                                 + TimeSpan.FromSeconds( 0.25 );
                    return true;
                }
            }

            return false;
        }

        // Checks both spellbook presence and reagent availability in one call.
        private static bool HasSpellAndReagents( PlayerBot bot, int spellID, Spell spell, Container pack )
        {
            Spellbook book = Spellbook.Find( bot, spellID );
            if ( book == null || !book.HasSpell( spellID ) )
                return false;

            return HasReagents( pack, spell.Reagents );
        }

        // Attempt to heal or cure a specific target (e.g. the player master).
        // Sets nextCastTime on success; returns true if a spell was initiated.
        public static bool TryCastHealTarget( PlayerBot bot, Mobile target, ref DateTime nextCastTime )
        {
            if ( bot.Skills[SkillName.Magery].Value < 10.0 )
                return false;

            Container pack = bot.Backpack;
            if ( pack == null )
                return false;

            // Cure poison — top priority
            if ( target.Poisoned && bot.Mana >= s_CircleMana[2] )
            {
                Spell cure = new CureSpell( bot, null );
                if ( HasSpellAndReagents( bot, ID_Cure, cure, pack ) && cure.Cast() )
                {
                    nextCastTime = DateTime.Now + cure.GetCastDelay()
                                 + TimeSpan.FromSeconds( 0.25 );
                    return true;
                }
            }

            // Greater Heal
            if ( target.Hits < target.HitsMax - 30 && bot.Mana >= s_CircleMana[4] )
            {
                Spell gh = new GreaterHealSpell( bot, null );
                if ( HasSpellAndReagents( bot, ID_GreaterHeal, gh, pack ) && gh.Cast() )
                {
                    nextCastTime = DateTime.Now + gh.GetCastDelay()
                                 + TimeSpan.FromSeconds( 0.25 );
                    return true;
                }
            }

            // Lesser Heal
            if ( target.Hits < target.HitsMax - 10 && bot.Mana >= s_CircleMana[1] )
            {
                Spell lh = new HealSpell( bot, null );
                if ( HasSpellAndReagents( bot, ID_Heal, lh, pack ) && lh.Cast() )
                {
                    nextCastTime = DateTime.Now + lh.GetCastDelay()
                                 + TimeSpan.FromSeconds( 0.25 );
                    return true;
                }
            }

            return false;
        }
    }
}
