using System;
using System.Collections.Generic;
using System.Text;

namespace Server.Mobiles
{
    public class PlayerBotPersona
    {
        public PlayerBotProfile    Profile    { get; set; }
        public PlayerBotExperience Experience { get; set; }
        public CombatStyle         Combat     { get; set; }

        public enum PlayerBotProfile
        {
            PlayerKiller = 0,
            Crafter      = 1,
            Adventurer   = 2
        }

        public enum PlayerBotExperience
        {
            Newbie      = 0,
            Average     = 1,
            Proficient  = 2,
            Grandmaster = 3
        }

        public enum CombatStyle
        {
            Melee         = 0,
            Archery       = 1,
            Magic         = 2,
            MeleeAndMagic = 3
        }
    }
}
