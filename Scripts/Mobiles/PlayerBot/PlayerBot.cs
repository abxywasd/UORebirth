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

        [NonSerialized]
        public DateTime NextCraftTime;

        [NonSerialized]
        public bool InConversation;

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
        public PlayerBot( AIType AI ) : base( AI, FightMode.Agressor, 10, 1, 0.5, 0.75 )
        {
            InitPersona();
            InitBody();
            InitStats();
            InitSkills();
            InitOutfit();
            if ( m_UsesMagic )
                InitReagents();
            StartSkillTimer();
            m_LastObserved = DateTime.Now;
        }

        [Constructable]
        public PlayerBot() : this( AIType.AI_PlayerBot )
        {
        }

        public PlayerBot( Serial serial ) : base( serial )
        {
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
        }

        // ── Init helpers ────────────────────────────────────────────────────────
        private void InitPersona()
        {
            m_Persona = new PlayerBotPersona();

            switch ( Utility.Random( 3 ) )
            {
                case 0: m_Persona.Profile = PlayerBotPersona.PlayerBotProfile.PlayerKiller; break;
                case 1: m_Persona.Profile = PlayerBotPersona.PlayerBotProfile.Crafter;      break;
                case 2: m_Persona.Profile = PlayerBotPersona.PlayerBotProfile.Adventurer;   break;
            }

            switch ( Utility.Random( 4 ) )
            {
                case 0: m_Persona.Experience = PlayerBotPersona.PlayerBotExperience.Newbie;      break;
                case 1: m_Persona.Experience = PlayerBotPersona.PlayerBotExperience.Average;     break;
                case 2: m_Persona.Experience = PlayerBotPersona.PlayerBotExperience.Proficient;  break;
                case 3: m_Persona.Experience = PlayerBotPersona.PlayerBotExperience.Grandmaster; break;
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
        }

        public virtual void InitBody()
        {
            Hue = Utility.RandomSkinHue();

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

            SetHits( Str );
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
            InitWeapon();
        }

        private void InitClothing()
        {
            int hue = Utility.RandomNondyedHue();

            // Everyone gets basic clothing
            if ( Female )
            {
                switch ( Utility.Random( 2 ) )
                {
                    case 0: AddItem( new Kilt( hue ) ); break;
                    default: AddItem( new PlainDress( hue ) ); break;
                }
            }
            else
            {
                AddItem( new LongPants( hue ) );
            }

            AddItem( new Shirt( Utility.RandomNondyedHue() ) );
            AddItem( new Shoes( Utility.RandomNondyedHue() ) );

            // Mages and crafters get a robe over their clothes
            if ( m_UsesMagic || m_Persona.Profile == PlayerBotPersona.PlayerBotProfile.Crafter )
                AddItem( new Robe( Utility.RandomNondyedHue() ) );
        }

        private void InitArmor()
        {
            // Crafters and newbies wear no armor
            if ( m_Persona.Profile == PlayerBotPersona.PlayerBotProfile.Crafter )
                return;
            if ( m_Persona.Experience == PlayerBotPersona.PlayerBotExperience.Newbie )
                return;

            switch ( m_Persona.Experience )
            {
                case PlayerBotPersona.PlayerBotExperience.Average:
                    // Partial leather
                    if ( Utility.RandomBool() ) AddItem( new LeatherChest() );
                    if ( Utility.RandomBool() ) AddItem( new LeatherLegs() );
                    break;

                case PlayerBotPersona.PlayerBotExperience.Proficient:
                    // Full leather
                    AddItem( new LeatherChest() );
                    AddItem( new LeatherLegs() );
                    AddItem( new LeatherArms() );
                    AddItem( new LeatherGloves() );
                    if ( Utility.RandomBool() ) AddItem( new LeatherGorget() );
                    if ( Utility.RandomBool() ) AddItem( new LeatherCap() );
                    break;

                case PlayerBotPersona.PlayerBotExperience.Grandmaster:
                    // Full leather; PKs and fighters get a chance at chain/plate pieces
                    if ( m_UsesMagic )
                    {
                        // Mages stay light
                        AddItem( new LeatherChest() );
                        AddItem( new LeatherLegs() );
                        AddItem( new LeatherGloves() );
                    }
                    else if ( m_Persona.Profile == PlayerBotPersona.PlayerBotProfile.PlayerKiller )
                    {
                        AddItem( new ChainChest() );
                        AddItem( new ChainLegs() );
                        AddItem( new RingmailArms() );
                        AddItem( new RingmailGloves() );
                        if ( Utility.RandomBool() ) AddItem( new PlateHelm() );
                    }
                    else
                    {
                        AddItem( new LeatherChest() );
                        AddItem( new LeatherLegs() );
                        AddItem( new LeatherArms() );
                        AddItem( new LeatherGloves() );
                        AddItem( new LeatherGorget() );
                        AddItem( new LeatherCap() );
                    }
                    break;
            }
        }

        private void InitWeapon()
        {
            if ( m_UsesMagic && !m_PrefersMelee )
            {
                // Mage-only: no weapon, just spellbook
                var book = new Spellbook();
                book.Content = GetSpellbookContent();
                PackItem( book );
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
                    AddItem( GenerateWeapon() );
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
                if ( Str >= 35 ) pool.Add( new Longsword() );
                if ( Str >= 40 ) pool.Add( new VikingSword() );
            }
            else if ( PreferedCombatSkill == SkillName.Macing )
            {
                if ( Str >= 10 ) pool.Add( new Club() );
                if ( Str >= 20 ) { pool.Add( new Mace() ); pool.Add( new Maul() ); }
                if ( Str >= 30 ) pool.Add( new WarMace() );
                if ( Str >= 35 ) pool.Add( new HammerPick() );
                if ( Str >= 40 ) { pool.Add( new Scepter() ); pool.Add( new WarHammer() ); }
            }
            else if ( PreferedCombatSkill == SkillName.Fencing )
            {
                if ( Str >= 10 ) pool.Add( new Pitchfork() );
                if ( Str >= 15 ) pool.Add( new ShortSpear() );
                if ( Str >= 30 ) pool.Add( new Spear() );
                if ( Str >= 35 ) pool.Add( new WarFork() );
                if ( Str >= 50 ) pool.Add( new Pike() );
            }

            if ( pool.Count == 0 )
                return new Dagger();

            return pool[m_Rnd.Next( pool.Count )];
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
            BotWaypoint wp = PlayerBotNavigator.PickDestination( m_Persona.Profile );
            if ( wp == null )
            {
                ActivityState.SetActivity( BotActivity.Wandering );
                return;
            }

            // Don't travel to where we already are
            if ( GetDistanceToSqrt( wp.Location ) <= 10 )
            {
                ActivityState.SetActivity( afterArrival );
                return;
            }

            ActivityState.TravelDestination = wp.Location;
            ActivityState.TravelMap         = wp.Map;
            m_AfterTravelActivity           = afterArrival;
            m_TravelMapIndex                = (wp.Map == Map.Felucca) ? 0 : 1;
            ActivityState.SetActivity( BotActivity.Traveling );
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

            // PKs attack everything else
            if ( m_Persona.Profile == PlayerBotPersona.PlayerBotProfile.PlayerKiller )
                return true;

            // Don't fight players unless they attacked first (handled by FightMode.Agressor)
            if ( target is PlayerMobile )
                return false;

            // Non-PK bots attack hostile PlayerBots but leave neutral ones alone
            if ( target is PlayerBot )
            {
                PlayerBot other = (PlayerBot)target;
                return other.m_IsPlayerKiller;
            }

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
