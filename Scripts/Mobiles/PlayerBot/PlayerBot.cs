using Server.Items;
using Server.Network;
using Server.Spells;
using Server.Misc;
using System;
using System.Collections.Generic;
using System.Text;

namespace Server.Mobiles
{
    public class PlayerBot : BaseCreature
    {
        static Random m_Rnd = new Random();

        // ── Player-typical dye palette ─────────────────────────────────────────
        private static readonly int[] PlayerHues = new int[]
        {
            0x21, 0x26, 0x2B, 0x2F,       // reds
            0x05, 0x0B, 0x10, 0x15,       // blues
            0x40, 0x44, 0x48, 0x52,       // greens
            0x72, 0x76, 0x7A,             // purples
            0x35, 0x38, 0x8A, 0x8C,       // yellows/oranges
            0x96, 0x97, 0x99,             // browns/tans
            0x01, 0x66,                   // blacks/dark
            0x03F5, 0x047E                // whites/light
        };

        private static int RandomPlayerHue()
        {
            return PlayerHues[Utility.Random( PlayerHues.Length )];
        }

        private enum ArmorMat { Naked, Leather, Studded, Ringmail, Chain, Bone, Plate }

        // [matIdx, slotIdx]: matIdx = (int)ArmorMat - 1 (Leather=0 … Plate=5)
        // slotIdx: Chest=0, Legs=1, Arms=2, Gloves=3, Gorget=4, Helm=5
        private static readonly int[,] SlotChances = new int[,]
        {
            {  80,  70,  60,  50,  40,  35 },  // Leather
            {  80,  70,  60,  50,  40,  35 },  // Studded
            {  80,  70,  60,  50,  40,   0 },  // Ringmail (no helm slot)
            {  85,  75,  65,  55,   0,  45 },  // Chain (no gorget slot)
            {  85,  75,  65,  55,   0,  45 },  // Bone (no gorget slot)
            {  90,  80,  70,  60,  55,  40 },  // Plate
        };

        // ── Persona ────────────────────────────────────────────────────────────
        private PlayerBotPersona m_Persona;
        private bool             m_IsPlayerKiller;
        private bool             m_PrefersMelee;
        private SkillName        m_PreferedCombatSkill;

        // ── Activity (version 1 persistent fields) ─────────────────────────────
        private BotActivity m_SavedActivity       = BotActivity.Wandering;
        private Point3D     m_SavedTravelDest;
        private int         m_TravelMapIndex;       // 0=Felucca, 1=Trammel
        private bool        m_UsesMagic;
        private BotActivity m_AfterTravelActivity  = BotActivity.Wandering;

        // ── Version 2 persistent fields ────────────────────────────────────────
        private DateTime m_LastObserved;
        private bool     m_IsEncounterBot;

        // ── Transient runtime state ────────────────────────────────────────────
        private ActivityState       m_ActivityState;
        private PlayerBotGroup      m_Group;
        private PlayerBotSkillTimer m_SkillTimer;
        private Timer               m_GhostSpeechTimer;
        private DeathShroud         m_GhostRobe;

        [NonSerialized]
        public DateTime NextCraftTime;

        [NonSerialized]
        public bool InConversation;

        [NonSerialized]
        public bool ForceMasterHeal;

        [NonSerialized]
        public bool IsPaused;

        // ── Properties ─────────────────────────────────────────────────────────
        [CommandProperty(AccessLevel.GameMaster)]
        public PlayerBotPersona.PlayerBotProfile PlayerBotProfile
        {
            get { return m_Persona.Profile; }
            set { m_Persona.Profile = value; }
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public PlayerBotPersona.PlayerBotExperience PlayerBotExperience
        {
            get { return m_Persona.Experience; }
            set { m_Persona.Experience = value; }
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public bool UsesMagic
        {
            get { return m_UsesMagic; }
            set { m_UsesMagic = value; }
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public BotActivity CurrentActivity
        {
            get { return ActivityState.Current; }
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public bool PrefersMelee
        {
            get { return m_PrefersMelee; }
            set { m_PrefersMelee = value; }
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public SkillName PreferedCombatSkill
        {
            get { return m_PreferedCombatSkill; }
            set { m_PreferedCombatSkill = value; }
        }

        public override bool AlwaysMurderer { get { return m_IsPlayerKiller; } }

        public ActivityState ActivityState
        {
            get
            {
                if ( m_ActivityState == null )
                    m_ActivityState = new ActivityState();
                return m_ActivityState;
            }
        }

        public PlayerBotGroup Group
        {
            get { return m_Group; }
            set { m_Group = value; }
        }

        public DateTime LastObserved
        {
            get { return m_LastObserved; }
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public bool IsEncounterBot
        {
            get { return m_IsEncounterBot; }
            set { m_IsEncounterBot = value; }
        }

        public void MarkObserved()
        {
            m_LastObserved = DateTime.Now;
        }

        public static string[] m_GuildTypes = new string[] { "", " (Chaos)", " (Order)" };

        // ── Constructors ────────────────────────────────────────────────────────
        public PlayerBot( AIType AI ) : base( AI, FightMode.Agressor, 10, 1, 0.15, 0.4 )
        {
            InitPersona( null, null );
            InitBody();
            InitStats();
            InitSkills();
            InitOutfit();
            if ( m_UsesMagic )
                InitReagents();
            InitBackpack();
            StartSkillTimer();
            m_LastObserved = DateTime.Now;
        }

        [Constructable]
        public PlayerBot() : this( AIType.AI_PlayerBot )
        {
        }

        public PlayerBot( PlayerBotPersona.PlayerBotProfile profile, PlayerBotPersona.PlayerBotExperience xp )
            : base( AIType.AI_PlayerBot, FightMode.Agressor, 10, 1, 0.15, 0.4 )
        {
            InitPersona( profile, xp );
            InitBody();
            InitStats();
            InitSkills();
            InitOutfit();
            if ( m_UsesMagic )
                InitReagents();
            InitBackpack();
            StartSkillTimer();
            m_LastObserved = DateTime.Now;
        }

        public PlayerBot( Serial serial ) : base( serial )
        {
        }

        // Teleport all PlayerBots controlled by master to the given location.
        // Collected into a list first to avoid mutating World.Mobiles during iteration.
        public static void TeleportPlayerBots( Mobile master, Point3D loc, Map map )
        {
            var bots = new System.Collections.Generic.List<PlayerBot>();
            foreach ( Mobile m in World.Mobiles.Values )
            {
                PlayerBot pb = m as PlayerBot;
                if ( pb != null && !pb.Deleted && pb.Controled && pb.ControlMaster == master )
                    bots.Add( pb );
            }
            foreach ( PlayerBot pb in bots )
                pb.MoveToWorld( loc, map );
        }

        // ── Alive override (Mobile.Alive is always true for non-players; ghost bots must appear dead) ──
        public override bool Alive { get { return !IsDeadPet && base.Alive; } }

        public override bool CanPaperdollBeOpenedBy( Mobile from )
        {
            if ( IsDeadPet ) return false;
            return base.CanPaperdollBeOpenedBy( from );
        }

        // ── Serialization ───────────────────────────────────────────────────────
        public override void Serialize( GenericWriter writer )
        {
            base.Serialize( writer );
            writer.Write( (int)2 ); // version

            // version 0 fields
            writer.Write( (int)m_Persona.Experience );
            writer.Write( (int)m_Persona.Profile );
            writer.Write( (bool)m_IsPlayerKiller );
            writer.Write( (bool)m_PrefersMelee );
            writer.Write( (int)m_PreferedCombatSkill );

            // version 1 fields
            writer.Write( (bool)m_UsesMagic );
            writer.Write( (int)(ActivityState != null ? ActivityState.Current : BotActivity.Wandering) );
            writer.Write( (Point3D)(ActivityState != null ? ActivityState.TravelDestination : Point3D.Zero) );
            writer.Write( (int)m_TravelMapIndex );
            writer.Write( (int)m_AfterTravelActivity );

            // version 2 fields
            writer.Write( (DateTime)m_LastObserved );
            writer.Write( (bool)m_IsEncounterBot );
        }

        public override void Deserialize( GenericReader reader )
        {
            base.Deserialize( reader );
            int version = reader.ReadInt();

            m_Persona       = new PlayerBotPersona();
            m_ActivityState = new ActivityState();

            m_Persona.Experience   = (PlayerBotPersona.PlayerBotExperience)reader.ReadInt();
            m_Persona.Profile      = (PlayerBotPersona.PlayerBotProfile)reader.ReadInt();
            m_IsPlayerKiller       = reader.ReadBool();
            m_PrefersMelee         = reader.ReadBool();
            m_PreferedCombatSkill  = (SkillName)reader.ReadInt();

            if ( version >= 1 )
            {
                m_UsesMagic           = reader.ReadBool();
                m_SavedActivity       = (BotActivity)reader.ReadInt();
                m_SavedTravelDest     = reader.ReadPoint3D();
                m_TravelMapIndex      = reader.ReadInt();
                m_AfterTravelActivity = (BotActivity)reader.ReadInt();

                m_ActivityState.Current             = m_SavedActivity;
                m_ActivityState.TravelDestination   = m_SavedTravelDest;
                m_ActivityState.TravelMap           = m_TravelMapIndex == 0 ? Map.Felucca : Map.Trammel;
            }

            if ( version >= 2 )
            {
                m_LastObserved   = reader.ReadDateTime();
                m_IsEncounterBot = reader.ReadBool();
            }
            else
            {
                m_LastObserved = DateTime.Now; // default to now so we don't immediately despawn old saves
            }

            StartSkillTimer();
            if ( IsDeadPet )
                StartGhostSpeechTimer();
            Timer.DelayCall( TimeSpan.FromSeconds( 2.0 ), new TimerCallback( ReEquipWeaponIfNeeded ) );
        }

        private void ReEquipWeaponIfNeeded()
        {
            if ( Deleted || Map == Map.Internal ) return;
            if ( FindItemOnLayer( Layer.OneHanded ) != null || FindItemOnLayer( Layer.TwoHanded ) != null ) return;
            if ( Backpack == null ) return;

            for ( int j = 0; j < Backpack.Items.Count; j++ )
            {
                Item item = Backpack.Items[j];
                if ( item is BaseWeapon )
                {
                    EquipItem( item );
                    break;
                }
            }
        }

        // ── Init helpers ────────────────────────────────────────────────────────
        private void InitPersona( PlayerBotPersona.PlayerBotProfile? profileOverride, PlayerBotPersona.PlayerBotExperience? xpOverride )
        {
            m_Persona = new PlayerBotPersona();

            if ( profileOverride.HasValue )
            {
                m_Persona.Profile = profileOverride.Value;
            }
            else
            {
                // 1/6 PK, 2/6 Crafter, 3/6 Adventurer — reds are rare and fearsome
                int profileRoll = Utility.Random( 6 );
                if      ( profileRoll == 0 )      m_Persona.Profile = PlayerBotPersona.PlayerBotProfile.PlayerKiller;
                else if ( profileRoll <= 2 )      m_Persona.Profile = PlayerBotPersona.PlayerBotProfile.Crafter;
                else                              m_Persona.Profile = PlayerBotPersona.PlayerBotProfile.Adventurer;
            }

            if ( xpOverride.HasValue )
            {
                m_Persona.Experience = xpOverride.Value;
            }
            else
            {
                switch ( Utility.Random( 4 ) )
                {
                    case 0: m_Persona.Experience = PlayerBotPersona.PlayerBotExperience.Newbie;      break;
                    case 1: m_Persona.Experience = PlayerBotPersona.PlayerBotExperience.Average;     break;
                    case 2: m_Persona.Experience = PlayerBotPersona.PlayerBotExperience.Proficient;  break;
                    case 3: m_Persona.Experience = PlayerBotPersona.PlayerBotExperience.Grandmaster; break;
                }
            }

            m_IsPlayerKiller = (m_Persona.Profile == PlayerBotPersona.PlayerBotProfile.PlayerKiller);

            // 40% chance to use magic; mages and adventurers more likely
            switch ( m_Persona.Profile )
            {
                case PlayerBotPersona.PlayerBotProfile.PlayerKiller:
                    m_UsesMagic = (Utility.Random(3) == 0); // 33%
                    break;
                case PlayerBotPersona.PlayerBotProfile.Adventurer:
                    m_UsesMagic = Utility.RandomBool();     // 50%
                    break;
                default:
                    m_UsesMagic = (Utility.Random(4) == 0); // 25%
                    break;
            }

            switch ( m_Persona.Profile )
            {
                case PlayerBotPersona.PlayerBotProfile.PlayerKiller:
                    Karma = Utility.RandomMinMax( -1, -127 );
                    break;
                default:
                    Karma = Utility.RandomMinMax( 0, 127 );
                break;
            }
        }

        public virtual void InitBody()
        {
            Hue = Utility.RandomSkinHue();
            SpeechHue = Utility.RandomDyedHue();
            if ( Body == 0 && (Name == null || Name.Length <= 0) )
            {
                if ( Female = Utility.RandomBool() )
                {
                    Body = 401;
                    Name = NameList.RandomName( "female" );
                }
                else
                {
                    Body = 400;
                    Name = NameList.RandomName( "male" );
                }
            }
        }

        private void InitStats()
        {
            switch ( m_Persona.Experience )
            {
                case PlayerBotPersona.PlayerBotExperience.Newbie:
                    SetStr( 30, 35 ); SetDex( 30, 35 ); SetInt( 30, 35 );
                    break;
                case PlayerBotPersona.PlayerBotExperience.Average:
                    SetStr( 45, 65 ); SetDex( 45, 65 ); SetInt( 45, 65 );
                    break;
                case PlayerBotPersona.PlayerBotExperience.Proficient:
                    SetStr( 70, 85 ); SetDex( 70, 85 ); SetInt( 70, 85 );
                    break;
                case PlayerBotPersona.PlayerBotExperience.Grandmaster:
                    SetStr( 95, 100 ); SetDex( 95, 100 ); SetInt( 95, 100 );
                    break;
            }

        }

        private void InitSkills()
        {
            m_PrefersMelee = !m_UsesMagic || Utility.RandomBool();

            SkillName combatSkill;
            if ( m_UsesMagic && !m_PrefersMelee )
            {
                combatSkill = SkillName.Magery;
            }
            else
            {
                int pick = Utility.Random( 4 );
                if      ( pick == 0 ) combatSkill = SkillName.Swords;
                else if ( pick == 1 ) combatSkill = SkillName.Macing;
                else if ( pick == 2 ) combatSkill = SkillName.Fencing;
                else                  combatSkill = SkillName.Wrestling;
            }

            PreferedCombatSkill = combatSkill;

            double lo, hi;
            switch ( m_Persona.Experience )
            {
                case PlayerBotPersona.PlayerBotExperience.Newbie:
                    lo = 15.0; hi = 35.5; break;
                case PlayerBotPersona.PlayerBotExperience.Average:
                    lo = 45.0; hi = 55.5; break;
                case PlayerBotPersona.PlayerBotExperience.Proficient:
                    lo = 65.5; hi = 85.0; break;
                default: // Grandmaster
                    lo = 95.0; hi = 100.0; break;
            }

            SetSkill( SkillName.Tactics,    lo, hi );
            SetSkill( SkillName.MagicResist, lo, hi );
            SetSkill( SkillName.Parry,       lo, hi );

            if ( combatSkill != SkillName.Magery )
                SetSkill( combatSkill, lo, hi );

            // Crafter gets a craft skill
            if ( m_Persona.Profile == PlayerBotPersona.PlayerBotProfile.Crafter )
            {
                SetSkill( SkillName.Blacksmith, lo, hi );
                SetSkill( SkillName.Mining,     lo, hi );
            }

            if ( m_UsesMagic )
                InitMagicSkills( lo, hi );
        }

        private void InitMagicSkills( double lo, double hi )
        {
            SetSkill( SkillName.Magery,    lo, hi );
            SetSkill( SkillName.EvalInt,   lo * 0.9, hi );
            SetSkill( SkillName.Meditation, lo * 0.8, hi );
        }

        public virtual void InitOutfit()
        {
            InitHair();
            InitClothing();
            InitArmor();
            InitFashionLayer();
            InitWeapon();
        }

        private void InitClothing()
        {
            // Base layer with vivid player hues
            if ( Female )
            {
                switch ( Utility.Random( 2 ) )
                {
                    case 0:  AddItem( new Kilt( RandomPlayerHue() ) ); break;
                    default: AddItem( new PlainDress( RandomPlayerHue() ) ); break;
                }
            }
            else
            {
                // 30% short pants, 70% long pants
                if ( Utility.Random( 10 ) < 3 )
                    AddItem( new ShortPants( RandomPlayerHue() ) );
                else
                    AddItem( new LongPants( RandomPlayerHue() ) );
            }

            AddItem( new Shirt( RandomPlayerHue() ) );

            // Footwear variety: boots, shoes, or thigh boots
            switch ( Utility.Random( 3 ) )
            {
                case 0:  AddItem( new Boots()); break;
                case 1:  AddItem( new Shoes()); break;
                default: AddItem( new ThighBoots()); break;
            }
        }

        // ── Armor system ────────────────────────────────────────────────────────

        private void InitArmor()
        {
            // Crafters wear no armor
            if ( m_Persona.Profile == PlayerBotPersona.PlayerBotProfile.Crafter )
                return;

            ArmorMat mat = RollArmorMaterial();
            if ( mat == ArmorMat.Naked ) return;

            bool fullSet = Utility.RandomBool();
            // Cross-tier mixing: for full sets above leather tier, 15% per slot drops one tier
            bool doMix = fullSet && mat > ArmorMat.Studded;

            AddArmorChest( mat, fullSet, doMix );
            AddArmorLegs(  mat, fullSet, doMix );
            AddArmorArms(  mat, fullSet, doMix );
            AddArmorGloves( mat, fullSet, doMix );
            AddArmorGorget( mat, fullSet, doMix );
            AddArmorHelm(  mat, fullSet, doMix );
        }

        private ArmorMat RollArmorMaterial()
        {
            int r = Utility.Random( 100 );
            switch ( m_Persona.Experience )
            {
                case PlayerBotPersona.PlayerBotExperience.Newbie:
                    // 50% Naked, 40% Leather, 10% Chain/Ring
                    if ( r < 50 ) return ArmorMat.Naked;
                    if ( r < 90 ) return RollLeatherSub();
                    return RollChainRingSub();

                case PlayerBotPersona.PlayerBotExperience.Average:
                    // 10% Naked, 50% Leather, 30% Chain/Ring, 10% Plate
                    if ( r < 10 ) return ArmorMat.Naked;
                    if ( r < 60 ) return RollLeatherSub();
                    if ( r < 90 ) return RollChainRingSub();
                    return ArmorMat.Plate;

                case PlayerBotPersona.PlayerBotExperience.Proficient:
                    // 30% Leather, 40% Chain/Ring, 30% Plate
                    if ( r < 30 ) return RollLeatherSub();
                    if ( r < 70 ) return RollChainRingSub();
                    return ArmorMat.Plate;

                default: // Grandmaster
                    // 15% Leather, 30% Chain/Ring, 55% Plate
                    if ( r < 15 ) return RollLeatherSub();
                    if ( r < 45 ) return RollChainRingSub();
                    return ArmorMat.Plate;
            }
        }

        private ArmorMat RollLeatherSub()
        {
            return Utility.RandomBool() ? ArmorMat.Leather : ArmorMat.Studded;
        }

        private ArmorMat RollChainRingSub()
        {
            switch ( Utility.Random( 3 ) )
            {
                case 0:  return ArmorMat.Chain;
                case 1:  return ArmorMat.Ringmail;
                default: return ArmorMat.Bone;
            }
        }

        private ArmorMat TierDown( ArmorMat mat )
        {
            switch ( mat )
            {
                case ArmorMat.Plate:    return RollChainRingSub();
                case ArmorMat.Chain:
                case ArmorMat.Ringmail:
                case ArmorMat.Bone:     return RollLeatherSub();
                default:                return mat;
            }
        }

        private ArmorMat GetSlotMat( ArmorMat mat, bool doMix )
        {
            return ( doMix && Utility.Random( 100 ) < 15 ) ? TierDown( mat ) : mat;
        }

        private bool ShouldAddSlot( ArmorMat mat, int slot, bool fullSet )
        {
            int matIdx = (int)mat - 1; // Leather=0 … Plate=5
            int chance = SlotChances[matIdx, slot];
            if ( chance == 0 ) return false;
            return fullSet || Utility.Random( 100 ) < chance;
        }

        private void AddArmorChest( ArmorMat mat, bool fullSet, bool doMix )
        {
            if ( !ShouldAddSlot( mat, 0, fullSet ) ) return;
            switch ( GetSlotMat( mat, doMix ) )
            {
                case ArmorMat.Leather:  AddItem( new LeatherChest() );  break;
                case ArmorMat.Studded:  AddItem( new StuddedChest() );  break;
                case ArmorMat.Ringmail: AddItem( new RingmailChest() ); break;
                case ArmorMat.Chain:    AddItem( new ChainChest() );    break;
                case ArmorMat.Bone:     AddItem( new BoneChest() );     break;
                case ArmorMat.Plate:    AddItem( new PlateChest() );    break;
            }
        }

        private void AddArmorLegs( ArmorMat mat, bool fullSet, bool doMix )
        {
            if ( !ShouldAddSlot( mat, 1, fullSet ) ) return;
            switch ( GetSlotMat( mat, doMix ) )
            {
                case ArmorMat.Leather:  AddItem( new LeatherLegs() );  break;
                case ArmorMat.Studded:  AddItem( new StuddedLegs() );  break;
                case ArmorMat.Ringmail: AddItem( new RingmailLegs() ); break;
                case ArmorMat.Chain:    AddItem( new ChainLegs() );    break;
                case ArmorMat.Bone:     AddItem( new BoneLegs() );     break;
                case ArmorMat.Plate:    AddItem( new PlateLegs() );    break;
            }
        }

        private void AddArmorArms( ArmorMat mat, bool fullSet, bool doMix )
        {
            if ( !ShouldAddSlot( mat, 2, fullSet ) ) return;
            switch ( GetSlotMat( mat, doMix ) )
            {
                case ArmorMat.Leather:  AddItem( new LeatherArms() );  break;
                case ArmorMat.Studded:  AddItem( new StuddedArms() );  break;
                case ArmorMat.Ringmail:
                case ArmorMat.Chain:    AddItem( new RingmailArms() ); break; // no ChainArms exists
                case ArmorMat.Bone:     AddItem( new BoneArms() );     break;
                case ArmorMat.Plate:    AddItem( new PlateArms() );    break;
            }
        }

        private void AddArmorGloves( ArmorMat mat, bool fullSet, bool doMix )
        {
            if ( !ShouldAddSlot( mat, 3, fullSet ) ) return;
            switch ( GetSlotMat( mat, doMix ) )
            {
                case ArmorMat.Leather:  AddItem( new LeatherGloves() );  break;
                case ArmorMat.Studded:  AddItem( new StuddedGloves() );  break;
                case ArmorMat.Ringmail:
                case ArmorMat.Chain:    AddItem( new RingmailGloves() ); break; // no ChainGloves exists
                case ArmorMat.Bone:     AddItem( new BoneGloves() );     break;
                case ArmorMat.Plate:    AddItem( new PlateGloves() );    break;
            }
        }

        private void AddArmorGorget( ArmorMat mat, bool fullSet, bool doMix )
        {
            if ( !ShouldAddSlot( mat, 4, fullSet ) ) return;
            ArmorMat m = GetSlotMat( mat, doMix );
            // Chain/Bone have no matching gorget; fall back to leather sub if mixing put us there
            if ( m == ArmorMat.Chain || m == ArmorMat.Bone )
                m = RollLeatherSub();
            switch ( m )
            {
                case ArmorMat.Leather:  AddItem( new LeatherGorget() ); break;
                case ArmorMat.Studded:  AddItem( new StuddedGorget() ); break;
                case ArmorMat.Ringmail: // no ring gorget; use a mixed piece
                    switch ( Utility.Random( 3 ) )
                    {
                        case 0:  AddItem( new PlateGorget() );   break;
                        case 1:  AddItem( new LeatherGorget() ); break;
                        default: AddItem( new StuddedGorget() ); break;
                    }
                    break;
                case ArmorMat.Plate:    AddItem( new PlateGorget() );   break;
            }
        }

        private void AddArmorHelm( ArmorMat mat, bool fullSet, bool doMix )
        {
            if ( !ShouldAddSlot( mat, 5, fullSet ) ) return;
            ArmorMat m = GetSlotMat( mat, doMix );
            // Ringmail has 0% helm; if mixing drops us here substitute Chain
            if ( m == ArmorMat.Ringmail ) m = ArmorMat.Chain;
            switch ( m )
            {
                case ArmorMat.Leather:
                case ArmorMat.Studded:
                    switch ( Utility.Random( 6 ) )
                    {
                        case 0:  AddItem( new LeatherCap() ); break;
                        case 1:  AddItem( new ChainCoif() );  break;
                        case 2:  AddItem( new PlateHelm() );  break;
                        case 3:  AddItem( new CloseHelm() );  break;
                        case 4:  AddItem( new NorseHelm() );  break;
                        default: AddItem( new OrcHelm() );    break;
                    }
                    break;
                case ArmorMat.Chain:
                case ArmorMat.Bone:
                    switch ( Utility.Random( 5 ) )
                    {
                        case 0:  AddItem( new ChainCoif() ); break;
                        case 1:  AddItem( new PlateHelm() ); break;
                        case 2:  AddItem( new CloseHelm() ); break;
                        case 3:  AddItem( new NorseHelm() ); break;
                        default: AddItem( new OrcHelm() );   break;
                    }
                    break;
                case ArmorMat.Plate:
                    switch ( Utility.Random( 5 ) )
                    {
                        case 0:  AddItem( new PlateHelm() ); break;
                        case 1:  AddItem( new CloseHelm() ); break;
                        case 2:  AddItem( new ChainCoif() ); break;
                        case 3:  AddItem( new NorseHelm() ); break;
                        default: AddItem( new OrcHelm() );   break;
                    }
                    break;
            }
        }

        // ── Fashion layer ────────────────────────────────────────────────────────
        // Dyed overgarments on top of armor — layered using distinct equipment slots.

        private void InitFashionLayer()
        {
            // Robe: 25%, Layer.OuterTorso
            if ( Utility.Random( 100 ) < 25 && FindItemOnLayer( Layer.OuterTorso ) == null )
                AddItem( new Robe( RandomPlayerHue() ) );

            // Cloak: 35%, Layer.Cloak
            if ( Utility.Random( 100 ) < 35 && FindItemOnLayer( Layer.Cloak ) == null )
                AddItem( new Cloak( RandomPlayerHue() ) );

            // BodySash: 30%, Layer.MiddleTorso
            if ( Utility.Random( 100 ) < 30 && FindItemOnLayer( Layer.MiddleTorso ) == null )
                AddItem( new BodySash( RandomPlayerHue() ) );

            // Kilt over pants for males: 20%, Layer.OuterLegs (different from pants Layer.Pants)
            if ( !Female && Utility.Random( 100 ) < 20 && FindItemOnLayer( Layer.OuterLegs ) == null )
                AddItem( new Kilt( RandomPlayerHue() ) );

            // Skirt for females: 15%, Layer.OuterLegs
            if ( Female && Utility.Random( 100 ) < 15 && FindItemOnLayer( Layer.OuterLegs ) == null )
                AddItem( new Skirt( RandomPlayerHue() ) );

            // HalfApron: 20%, Layer.Waist
            if ( Utility.Random( 100 ) < 20 && FindItemOnLayer( Layer.Waist ) == null )
                AddItem( new HalfApron( RandomPlayerHue() ) );

            // FullApron: 10%, Layer.MiddleTorso (skipped if BodySash already there)
            if ( Utility.Random( 100 ) < 10 && FindItemOnLayer( Layer.MiddleTorso ) == null )
                AddItem( new FullApron( RandomPlayerHue() ) );
        }

        // ── Weapon init ─────────────────────────────────────────────────────────

        private void InitWeapon()
        {
            if ( m_UsesMagic && !m_PrefersMelee )
            {
                // Pure mage: spellbook + 40% chance to carry a visible sidearm
                var book = new Spellbook();
                book.Content = GetSpellbookContent();
                PackItem( book );
                if ( Utility.Random( 10 ) < 4 )
                {
                    if ( Utility.RandomBool() )
                        AddItem( new Dagger() );
                    else
                        AddItem( new Kryss() );
                }
                return;
            }

            if ( m_UsesMagic )
            {
                // Hybrid fighter/mage: carry a spellbook alongside their weapon
                var book = new Spellbook();
                book.Content = GetSpellbookContent();
                PackItem( book );
            }

            if ( m_PrefersMelee )
            {
                if ( PreferedCombatSkill != SkillName.Wrestling )
                {
                    AddItem( GenerateWeapon() );
                    TryAddShield();
                }
            }
            else
            {
                AddItem( new Bow() );
                PackItem( new Arrow( Utility.Random( 50, 100 ) ) );
                PackItem( new Dagger() );
            }
        }

        // Content bitmask: each circle has 8 spells (0-7, 8-15, ...).
        // Fill through the bot's castable circle, always including Heal and Cure.
        private ulong GetSpellbookContent()
        {
            double mag  = Skills[SkillName.Magery].Value;
            int maxCircle = (int)(mag / 87.5 * 8.0);
            if ( maxCircle > 8 ) maxCircle = 8;
            if ( maxCircle < 1 ) maxCircle = 1;

            ulong content = maxCircle >= 8 ? ulong.MaxValue : (1ul << (maxCircle * 8)) - 1;

            // Always include Heal (3) and Cure (10) even for low-circle bots
            content |= (1ul << 3);
            content |= (1ul << 10);

            return content;
        }

        private void InitReagents()
        {
            int qty;
            switch ( m_Persona.Experience )
            {
                case PlayerBotPersona.PlayerBotExperience.Newbie:       qty = 10; break;
                case PlayerBotPersona.PlayerBotExperience.Average:      qty = 25; break;
                case PlayerBotPersona.PlayerBotExperience.Proficient:   qty = 50; break;
                default: /* Grandmaster */                               qty = 80; break;
            }

            PackItem( new BlackPearl( qty ) );
            PackItem( new Bloodmoss( qty ) );
            PackItem( new Garlic( qty ) );
            PackItem( new Ginseng( qty ) );
            PackItem( new MandrakeRoot( qty ) );
            PackItem( new Nightshade( qty ) );
            PackItem( new SpidersSilk( qty ) );
            PackItem( new SulfurousAsh( qty ) );
        }

        // ── Backpack loot ────────────────────────────────────────────────────────

        private void InitBackpack()
        {
            // Gold scaled to experience
            int gold;
            switch ( m_Persona.Experience )
            {
                case PlayerBotPersona.PlayerBotExperience.Newbie:       gold = Utility.Random( 5,   56   ); break;
                case PlayerBotPersona.PlayerBotExperience.Average:      gold = Utility.Random( 50,  201  ); break;
                case PlayerBotPersona.PlayerBotExperience.Proficient:   gold = Utility.Random( 150, 451  ); break;
                default: /* Grandmaster */                               gold = Utility.Random( 300, 1201 ); break;
            }
            PackItem( new Gold( gold ) );

            // Bandages (non-crafters always carry some)
            if ( m_Persona.Profile != PlayerBotPersona.PlayerBotProfile.Crafter )
            {
                int bandages;
                switch ( m_Persona.Experience )
                {
                    case PlayerBotPersona.PlayerBotExperience.Newbie:       bandages = Utility.Random( 5,  16  ); break;
                    case PlayerBotPersona.PlayerBotExperience.Average:      bandages = Utility.Random( 15, 36  ); break;
                    case PlayerBotPersona.PlayerBotExperience.Proficient:   bandages = Utility.Random( 30, 51  ); break;
                    default: /* Grandmaster */                               bandages = Utility.Random( 50, 101 ); break;
                }
                PackItem( new Bandage( bandages ) );
            }

            // Food: 2-5 random items
            int foodCount = 2 + Utility.Random( 4 );
            for ( int i = 0; i < foodCount; i++ )
                PackItem( RandomFoodItem() );

            // Profile-specific loot
            if ( m_Persona.Profile == PlayerBotPersona.PlayerBotProfile.PlayerKiller ||
                 m_Persona.Profile == PlayerBotPersona.PlayerBotProfile.Adventurer )
            {
                // Hunting spoils: each category ~40% chance
                if ( Utility.Random( 10 ) < 4 ) PackItem( new Leather( Utility.Random( 5,  26 ) ) );
                if ( Utility.Random( 10 ) < 4 ) PackItem( new IronIngot( Utility.Random( 2, 14 ) ) );
                if ( Utility.Random( 10 ) < 4 ) PackItem( RandomGem( Utility.Random( 1,  3  ) ) );
                if ( Utility.Random( 10 ) < 4 ) PackItem( new Bone( Utility.Random( 1,   5  ) ) );
                // Extra arrows if carrying a bow (base allotment already in InitWeapon)
                if ( FindItemOnLayer( Layer.TwoHanded ) is Bow )
                    PackItem( new Arrow( Utility.Random( 10, 31 ) ) );
            }
            else if ( m_Persona.Profile == PlayerBotPersona.PlayerBotProfile.Crafter )
            {
                PackItem( new IronIngot( Utility.Random( 10, 41 ) ) );
                PackItem( new Board( Utility.Random( 5, 21 ) ) );
                if ( Utility.RandomBool() )
                {
                    switch ( Utility.Random( 4 ) )
                    {
                        case 0:  PackItem( new Tongs() );       break;
                        case 1:  PackItem( new SmithHammer() ); break;
                        case 2:  PackItem( new Saw() );         break;
                        default: PackItem( new Scissors() );    break;
                    }
                }
            }

            // Non-mages sometimes carry a few reagents
            if ( !m_UsesMagic && Utility.Random( 100 ) < 20 )
            {
                int stacks = 1 + Utility.Random( 3 );
                for ( int i = 0; i < stacks; i++ )
                {
                    int qty = Utility.Random( 5, 11 );
                    switch ( Utility.Random( 8 ) )
                    {
                        case 0:  PackItem( new BlackPearl( qty ) );    break;
                        case 1:  PackItem( new Bloodmoss( qty ) );     break;
                        case 2:  PackItem( new Garlic( qty ) );        break;
                        case 3:  PackItem( new Ginseng( qty ) );       break;
                        case 4:  PackItem( new MandrakeRoot( qty ) );  break;
                        case 5:  PackItem( new Nightshade( qty ) );    break;
                        case 6:  PackItem( new SpidersSilk( qty ) );   break;
                        default: PackItem( new SulfurousAsh( qty ) );  break;
                    }
                }
            }

            // Misc
            if ( Utility.Random( 100 ) < 30 )
            {
                int torchCount = 1 + Utility.Random( 3 );
                for ( int i = 0; i < torchCount; i++ )
                    PackItem( new Torch() );
            }
            if ( Utility.Random( 100 ) < 15 )
                PackItem( new Candle() );
        }

        private static Item RandomFoodItem()
        {
            switch ( Utility.Random( 4 ) )
            {
                case 0:  return new BreadLoaf();
                case 1:  return new CheeseWheel();
                case 2:  return new Grapes();
                default: return new Watermelon();
            }
        }

        private static Item RandomGem( int qty )
        {
            switch ( Utility.Random( 7 ) )
            {
                case 0:  return new Citrine( qty );
                case 1:  return new Tourmaline( qty );
                case 2:  return new Amethyst( qty );
                case 3:  return new Sapphire( qty );
                case 4:  return new Ruby( qty );
                case 5:  return new Emerald( qty );
                default: return new Diamond( qty );
            }
        }

        private void InitHair()
        {
            var hairHue = Utility.RandomHairHue();
            Utility.AssignRandomHair( this, hairHue );

            if ( !Female && Utility.RandomBool() )
                AddRandomFacialHair( hairHue );
        }

        private BaseWeapon GenerateWeapon()
        {
            var pool = new List<BaseWeapon>();

            if ( PreferedCombatSkill == SkillName.Swords )
            {
                if ( Str >= 25 ) { pool.Add( new Broadsword() ); pool.Add( new Cutlass() ); pool.Add( new Katana() ); pool.Add( new Scimitar() ); }
                if ( Str >= 35 ) { pool.Add( new Longsword() ); pool.Add( new TwoHandedAxe() ); }
                if ( Str >= 40 ) { pool.Add( new VikingSword() ); pool.Add( new Bardiche() ); pool.Add( new ExecutionersAxe() ); }
                if ( Str >= 45 ) pool.Add( new Halberd() );
            }
            else if ( PreferedCombatSkill == SkillName.Macing )
            {
                if ( Str >= 10 ) pool.Add( new Club() );
                if ( Str >= 15 ) pool.Add( new QuarterStaff() );
                if ( Str >= 20 ) { pool.Add( new Mace() ); pool.Add( new Maul() ); }
                if ( Str >= 30 ) pool.Add( new WarMace() );
                if ( Str >= 35 ) { pool.Add( new HammerPick() ); pool.Add( new WarAxe() ); }
                if ( Str >= 40 ) { pool.Add( new Scepter() ); pool.Add( new WarHammer() ); }
            }
            else if ( PreferedCombatSkill == SkillName.Fencing )
            {
                if ( Str >= 10 ) { pool.Add( new Pitchfork() ); pool.Add( new Kryss() ); }
                if ( Str >= 15 ) pool.Add( new ShortSpear() );
                if ( Str >= 30 ) pool.Add( new Spear() );
                if ( Str >= 35 ) pool.Add( new WarFork() );
                if ( Str >= 50 ) pool.Add( new Pike() );
            }

            if ( pool.Count == 0 )
                return new Dagger();

            return pool[m_Rnd.Next( pool.Count )];
        }

        private void TryAddShield()
        {
            // Only equip a shield when carrying a 1H weapon
            if ( FindItemOnLayer( Layer.OneHanded ) == null )
                return;

            // Higher chance if the bot has trained Parrying
            bool hasParry = Skills[SkillName.Parry].Base >= 30.0;
            if ( Utility.Random( 100 ) >= ( hasParry ? 75 : 40 ) )
                return;

            AddItem( GenerateShield() );
        }

        private static BaseShield GenerateShield()
        {
            switch ( Utility.Random( 7 ) )
            {
                case 0:  return new Buckler();
                case 1:  return new BronzeShield();
                case 2:  return new MetalShield();
                case 3:  return new HeaterShield();
                case 4:  return new WoodenShield();
                case 5:  return new WoodenKiteShield();
                default: return new MetalKiteShield();
            }
        }

        private void StartSkillTimer()
        {
            if ( m_SkillTimer != null )
                m_SkillTimer.Stop();

            m_SkillTimer = new PlayerBotSkillTimer( this );
            m_SkillTimer.Start();
        }

        // ── Activity logic ──────────────────────────────────────────────────────
        public void ChooseNextActivity()
        {
            if ( Hits < HitsMax / 2 )
            {
                ActivityState.SetActivity( BotActivity.Fleeing );
                return;
            }

            int roll = Utility.Random( 10 );

            switch ( m_Persona.Profile )
            {
                case PlayerBotPersona.PlayerBotProfile.Crafter:
                    if ( roll < 3 )      TravelToRandom( BotActivity.Crafting );
                    else if ( roll < 5 ) TravelToRandom( BotActivity.TownVisit );
                    else                 ActivityState.SetActivity( BotActivity.Wandering );
                    break;

                case PlayerBotPersona.PlayerBotProfile.PlayerKiller:
                    if ( roll < 4 )      TravelToRandom( BotActivity.Hunting );
                    else if ( roll < 6 ) ActivityState.SetActivity( BotActivity.Hunting );
                    else if ( roll < 8 ) TravelToRandom( BotActivity.TownVisit );
                    else                 ActivityState.SetActivity( BotActivity.Wandering );
                    break;

                default: // Adventurer
                    if ( roll < 4 )      TravelToRandom( BotActivity.Hunting );
                    else if ( roll < 6 ) ActivityState.SetActivity( BotActivity.Hunting );
                    else if ( roll < 8 ) TravelToRandom( BotActivity.TownVisit );
                    else                 ActivityState.SetActivity( BotActivity.Wandering );
                    break;
            }
        }

        private void TravelToRandom( BotActivity afterArrival )
        {
            BotWaypoint dest = PlayerBotNavigator.PickDestination( m_Persona.Profile );
            if ( dest == null )
            {
                ActivityState.SetActivity( BotActivity.Wandering );
                return;
            }

            // Don't travel to where we already are
            if ( GetDistanceToSqrt( dest.Location ) <= 10 )
            {
                ActivityState.SetActivity( afterArrival );
                return;
            }

            m_AfterTravelActivity = afterArrival;
            m_TravelMapIndex      = (dest.Map == Map.Felucca) ? 0 : 1;

            List<BotWaypoint> route = PlayerBotNavigator.ComputeRoute( Location, Map, dest );
            if ( route != null && route.Count > 0 )
                ActivityState.SetTravelRoute( dest, route );
            else
                ActivityState.SetTravelDirect( dest );
        }

        public void SetAfterTravelActivity( BotActivity act )
        {
            m_AfterTravelActivity = act;
        }

        // Called by PlayerBotAI when TravelDestination is reached.
        public void OnArrived()
        {
            Home      = Location;
            RangeHome = 20;
            ActivityState.SetActivity( m_AfterTravelActivity );
            m_AfterTravelActivity = BotActivity.Wandering;
        }

        public bool ShouldAttack( Mobile target )
        {
            if ( target == null || target.Deleted || !target.Alive )
                return false;

            // Never attack own group members
            if ( m_Group != null && target is PlayerBot
                 && m_Group.Members.Contains( (PlayerBot)target ) )
                return false;

            // PKs attack everything — except other PK bots (they're fellow reds)
            // Targeting another PK causes the bot to freeze in combat pose because
            // CanBeHarmful blocks swings between two AlwaysMurderer mobiles
            if ( m_Persona.Profile == PlayerBotPersona.PlayerBotProfile.PlayerKiller )
            {
                if ( target is PlayerBot && ((PlayerBot)target).m_IsPlayerKiller )
                    return false;
                return true;
            }

            // Don't fight players unless they attacked first (handled by FightMode.Agressor)
            if ( target is PlayerMobile )
                return false;

            // Non-PK bots attack hostile PlayerBots but leave neutral ones alone
            if ( target is PlayerBot )
            {
                PlayerBot other = (PlayerBot)target;
                return other.m_IsPlayerKiller;
            }

            // Any non-PlayerBot target that's actively in combat is a threat worth engaging
            BaseCreature bc = target as BaseCreature;
            if ( bc != null && bc.Combatant != null )
                return true;

            // Crafters don't hunt; Adventurers do
            return m_Persona.Profile != PlayerBotPersona.PlayerBotProfile.Crafter;
        }

        public bool ShouldFlee( Mobile attacker )
        {
            if ( Hits > HitsMax / 2 ) return false;

            int diff = attacker.Hits - Hits;
            return Utility.Random( 100 ) < (15 + diff);
        }

        // ── Skill gain ──────────────────────────────────────────────────────────
        // Called by PlayerBotAI every 4th combat tick.
        public void TryGainCombatSkills()
        {
            CheckSkill( m_PreferedCombatSkill, 0.0, 100.0 );
            CheckSkill( SkillName.Tactics,     0.0, 100.0 );
            CheckSkill( SkillName.MagicResist, 0.0, 100.0 );

            if ( m_UsesMagic )
                CheckSkill( SkillName.Meditation, 0.0, 100.0 );
        }

        // Called by PlayerBotSkillTimer every 10 seconds.
        public void TickSkillGain( BotActivity context )
        {
            if ( context == BotActivity.Crafting )
            {
                SkillCheck.Gain( this, Skills[SkillName.Blacksmith] );
                if ( Utility.Random( 3 ) == 0 )
                    SkillCheck.Gain( this, Skills[SkillName.Mining] );
            }
            else if ( context == BotActivity.Combat || context == BotActivity.Hunting )
            {
                if ( Utility.Random( 3 ) == 0 )
                    SkillCheck.Gain( this, Skills[SkillName.Parry] );

                if ( m_UsesMagic )
                {
                    if ( Utility.Random( 4 ) == 0 )
                        SkillCheck.Gain( this, Skills[SkillName.Magery] );
                    if ( Utility.Random( 4 ) == 0 )
                        SkillCheck.Gain( this, Skills[SkillName.EvalInt] );
                }

                if ( Utility.Random( 5 ) == 0 )
                    SkillCheck.Gain( this, Skills[m_PreferedCombatSkill] );
            }
        }

        // ── Overrides ───────────────────────────────────────────────────────────
        public override void OnSingleClick( Mobile from )
        {
            if ( Deleted || (AccessLevel == AccessLevel.Player && DisableHiddenSelfClick && Hidden && from == this) )
                return;

            if ( Mobile.GuildClickMessage )
            {
                Server.Guilds.Guild guild = this.Guild as Server.Guilds.Guild;

                if ( guild != null && (this.DisplayGuildTitle || guild.Type != Server.Guilds.GuildType.Regular) )
                {
                    string title = GuildTitle;
                    string type;

                    if ( title == null )
                        title = "";
                    else
                        title = title.Trim();

                    if ( guild.Type >= 0 && (int)guild.Type < m_GuildTypes.Length )
                        type = m_GuildTypes[(int)guild.Type];
                    else
                        type = "";

                    string text = String.Format( title.Length <= 0 ? "[{1}]{2}" : "[{0}, {1}]{2}", title, guild.Abbreviation, type );
                    PrivateOverheadMessage( MessageType.Regular, SpeechHue, true, text, from.NetState );
                }
            }

            int hue;

            if ( NameHue != -1 )
                hue = NameHue;
            else if ( AccessLevel > AccessLevel.Player )
                hue = 11;
            else
                hue = Notoriety.GetHue( Notoriety.Compute( from, this ) );

            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            if ( Karma >= (int)Noto.LordLady || Karma <= (int)Noto.Dark )
                sb.Append( Female ? "Lady " : "Lord " );

            sb.Append( Name );

            if ( ClickTitle && Title != null && Title.Length > 0 )
            {
                sb.Append( ' ' );
                sb.Append( Title );
            }

            if ( Frozen || Paralyzed || (this.Spell != null && this.Spell is Spell && this.Spell.IsCasting && ((Spell)this.Spell).BlocksMovement) )
                sb.Append( " (frozen)" );

            if ( Blessed )
                sb.Append( " (invulnerable)" );

            PrivateOverheadMessage( MessageType.Label, hue, Mobile.AsciiClickMessage, sb.ToString(), from.NetState );
        }

        public override bool HandlesOnSpeech( Mobile from )
        {
            return true;
        }

        public override bool CanBeRenamedBy( Mobile from )
        {
            return false;
        }

        public override void OnSpeech( SpeechEventArgs e )
        {
            // ── Intercept "all" commands from master ──────────────────────────────
            // Must run before base.OnSpeech, which calls BaseAI.OnSpeech and would
            // open one targeting cursor per bot for "all kill" / "all attack".
            if ( !e.Handled
                 && e.Mobile.Alive
                 && this.Controled
                 && this.ControlMaster == e.Mobile
                 && e.Mobile.InRange( this, 14 ) )
            {
                int[] keywords = e.Keywords;
                for ( int i = 0; i < keywords.Length; i++ )
                {
                    switch ( keywords[i] )
                    {
                        case 0x164: // all come
                            PlayerBotAllCommandHandler.BroadcastOrder( e.Mobile, OrderType.Come, null );
                            return;

                        case 0x16C: // all follow me
                            PlayerBotAllCommandHandler.BroadcastOrder( e.Mobile, OrderType.Follow, e.Mobile );
                            return;

                        case 0x167: // all stop
                            PlayerBotAllCommandHandler.BroadcastOrder( e.Mobile, OrderType.Stop, null );
                            return;

                        case 0x170: // all stay
                            PlayerBotAllCommandHandler.BroadcastOrder( e.Mobile, OrderType.Stay, null );
                            return;

                        case 0x166: // all guard
                        case 0x16B: // all guard me
                            PlayerBotAllCommandHandler.BroadcastOrder( e.Mobile, OrderType.Guard, null );
                            return;

                        case 0x168: // all kill
                        case 0x169: // all attack
                            // Single coordinator opens exactly one cursor for the master.
                            PlayerBotAllCommandHandler.TryBeginAllAttack( e.Mobile );
                            // Suppress per-bot BaseAI handling to prevent N cursors.
                            return;
                    }
                }

                // Custom text commands (no UO keyword IDs)
                string allCmd = e.Speech.ToLower().Trim();

                if ( allCmd == "all status" || allCmd == "all report" )
                {
                    PlayerBotAllCommandHandler.BroadcastStatusReport( e.Mobile );
                    e.Handled = true;
                    return;
                }

                if ( allCmd == "all heal me" || allCmd == "all heal" )
                {
                    PlayerBotAllCommandHandler.BroadcastHealMaster( e.Mobile );
                    e.Handled = true;
                    return;
                }

                if ( allCmd == "all move" )
                {
                    PlayerBotAllCommandHandler.BroadcastMove( e.Mobile );
                    e.Handled = true;
                    return;
                }

                if ( allCmd == "all release" || allCmd == "release all" )
                {
                    e.Mobile.SendMessage( "Use the bot management gump to release bots." );
                    e.Handled = true;
                    return;
                }
            }

            // ── Owner management trigger: "status" or "manage" ────────────────────
            if ( !e.Handled && e.Mobile.InRange( this, 6 ) )
            {
                string speech = e.Speech.ToLower();
                if ( speech.Contains( "status" ) || speech.Contains( "manage" ) )
                {
                    e.Handled = true;

                    if ( this.Controled && this.ControlMaster == e.Mobile )
                        e.Mobile.SendGump( new PlayerBotManageGump( e.Mobile, this ) );
                    else
                        Say( "I don't think we've been introduced..." );

                    base.OnSpeech( e );
                    return;
                }
            }

            // ── Loot command: "loot" or "<botname> loot" ─────────────────────────
            if ( !e.Handled && e.Mobile.InRange( this, 6 ) )
            {
                string lootSpeech = e.Speech.ToLower().Trim();
                string botNameLow = this.Name != null ? this.Name.ToLower() : "";

                bool isLoot = lootSpeech == "loot" || lootSpeech == botNameLow + " loot";

                if ( isLoot )
                {
                    e.Handled = true;

                    if ( this.Controled && this.ControlMaster == e.Mobile )
                    {
                        if ( this.IsDeadPet )
                        {
                            e.Mobile.SendMessage( "Your bot is dead and cannot carry loot." );
                        }
                        else
                        {
                            e.Mobile.SendMessage( this.Name + " is ready to collect loot. Target a fallen bot's remains." );
                            e.Mobile.Target = new PlayerBotLootTarget( e.Mobile, this );
                        }
                    }
                    else
                    {
                        Say( "I don't think we've been introduced..." );
                    }

                    base.OnSpeech( e );
                    return;
                }
            }

            if ( !e.Handled && e.Mobile.InRange( this, 4 ) )
            {
                // Hire keyword
                if ( e.HasKeyword( 0x003B ) || e.HasKeyword( 0x0162 ) )
                {
                    e.Handled = true;

                    if ( this.Controled )
                    {
                        if ( this.ControlMaster != e.Mobile )
                            Say( "I don't think I've agreed to work with you...yet?" );
                    }
                    else
                    {
                        AddHire( e.Mobile );
                    }
                }
                else
                {
                    // React to nearby player speech
                    PlayerBotSpeaker.ReactToPlayer( this, e.Mobile, e.Speech );
                }
            }

            base.OnSpeech( e );
        }

        public override void OnDelete()
        {
            StopGhostSpeechTimer();

            if ( m_SkillTimer != null )
            {
                m_SkillTimer.Stop();
                m_SkillTimer = null;
            }

            if ( m_Group != null )
                m_Group.RemoveMember( this );

            if ( PlayerBotDirector.Instance != null )
                PlayerBotDirector.Instance.UnregisterBot( this );

            base.OnDelete();
        }

        // ── Ghost death ──────────────────────────────────────────────────────────
        public override void OnDeath( Container c )
        {
            if ( Controled && ControlMaster != null )
            {
                int sound = GetDeathSound();
                if ( sound >= 0 )
                    Effects.PlaySound( this, Map, sound );

                Warmode   = false;
                Poison    = null;
                Combatant = null;
                Hits = 0; Stam = 0; Mana = 0;

                IsDeadPet     = true;
                Warmode       = true;   // ghost visible via CanSee(m.Warmode) check
                ControlTarget = ControlMaster;
                ControlOrder  = OrderType.Follow;

                StripEquipmentToPack();
                m_GhostRobe = new DeathShroud();
                AddItem( m_GhostRobe );

                ProcessDeltaQueue();
                SendIncomingPacket();
                SendIncomingPacket();

                StartGhostSpeechTimer( firstFire: true );
                CheckStatTimers();
                return;
            }

            base.OnDeath( c );
        }

        public override void OnAfterResurrect()
        {
            base.OnAfterResurrect();
            StopGhostSpeechTimer();
            Warmode = false;

            if ( m_GhostRobe != null )
            {
                m_GhostRobe.Delete();
                m_GhostRobe = null;
            }
            AddItem( new DeathRobe() );
        }

        private void StripEquipmentToPack()
        {
            var toStrip = new System.Collections.Generic.List<Item>();
            foreach ( Item item in Items )
            {
                Layer l = item.Layer;
                if ( l != Layer.Hair && l != Layer.FacialHair && l != Layer.Backpack && l != Layer.Bank )
                    toStrip.Add( item );
            }
            foreach ( Item item in toStrip )
                AddToBackpack( item );
        }

        private void StartGhostSpeechTimer( bool firstFire = false )
        {
            StopGhostSpeechTimer();
            int delay = firstFire ? Utility.RandomMinMax( 5, 10 ) : Utility.RandomMinMax( 45, 90 );
            m_GhostSpeechTimer = Timer.DelayCall(
                TimeSpan.FromSeconds( delay ),
                new TimerCallback( OnGhostSpeak ) );
        }

        private void StopGhostSpeechTimer()
        {
            if ( m_GhostSpeechTimer != null )
            {
                m_GhostSpeechTimer.Stop();
                m_GhostSpeechTimer = null;
            }
        }

        private void OnGhostSpeak()
        {
            if ( !IsDeadPet || Deleted || Map == null ) return;

            string real    = PickGhostLine();
            string garbled = GarbleSpeech( real );

            IPooledEnumerable eable = Map.GetClientsInRange( Location, 12 );
            foreach ( Server.Network.NetState ns in eable )
            {
                Mobile listener = ns.Mobile;
                if ( listener == null ) continue;
                bool hearGarbled = listener.Alive && !listener.CanHearGhosts;
                PrivateOverheadMessage( MessageType.Regular, SpeechHue, true, hearGarbled ? garbled : real, ns );
            }
            eable.Free();

            StartGhostSpeechTimer();
        }

        private static string GarbleSpeech( string text )
        {
            char[] gc = Mobile.GhostChars;
            System.Text.StringBuilder sb = new System.Text.StringBuilder( text.Length );
            for ( int i = 0; i < text.Length; ++i )
                sb.Append( text[i] == ' ' ? ' ' : gc[Utility.Random( gc.Length )] );
            return sb.ToString();
        }

        private static readonly string[][] m_GhostLines = new string[][]
        {
            // [0] generic
            new string[] {
                "Could have used a heal back there.",
                "Is anyone listening?",
                "I can see the shrine from here.",
                "It's cold.",
                "Don't leave me here.",
                "I feel... lighter somehow.",
                "Next time, maybe stay closer.",
            },
            // [1] PlayerKiller (Profile=0)
            new string[] {
                "I didn't see that mage coming. Well. I did.",
                "Mark my words.",
                "Find whoever did this.",
                "I'll be back. Don't go anywhere.",
            },
            // [2] Crafter (Profile=1)
            new string[] {
                "That was my best armor.",
                "Do you know how long that gorget took to make?",
                "I could have been at the forge right now.",
                "Someone is going to pay for this leather.",
            },
            // [3] Adventurer (Profile=2)
            new string[] {
                "Tell me you at least looted the chest.",
                "I dropped my sword back there somewhere.",
                "We were so close.",
                "I've died in worse places. Barely.",
            },
        };

        private string PickGhostLine()
        {
            string[] pool = Utility.RandomBool()
                ? m_GhostLines[0]
                : m_GhostLines[(int)m_Persona.Profile + 1];
            return pool[Utility.Random( pool.Length )];
        }

        // ── Hire / ownership ────────────────────────────────────────────────────
        public virtual Mobile GetOwner()
        {
            if ( !Controled || Deleted )
                return null;

            Mobile owner = ControlMaster;
            if ( owner == null || owner.Deleted )
            {
                Say( 1005653 ); // Hmmm.  I seem to have lost my master.
                Delta( MobileDelta.Noto );
                SetControlMaster( null );
                SummonMaster = null;
                BondingBegin = DateTime.MinValue;
                OwnerAbandonTime = DateTime.MinValue;
                IsBonded = false;
                return null;
            }

            return owner;
        }

        public virtual bool AddHire( Mobile m )
        {
            Mobile owner = GetOwner();

            if ( owner != null )
            {
                m.SendLocalizedMessage( 1043283, owner.Name ); // I am following ~1_NAME~.
                return false;
            }

            bool success = SetControlMaster( m );
            if ( success )
                ActivityState.SetActivity( BotActivity.Recruited );

            return success;
        }
    }
}
