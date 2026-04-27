using System;
using System.Collections.Generic;
using Server;
using Server.Commands;
using Server.Gumps;
using Server.Network;
using Server.Regions;

namespace Server.Mobiles
{
    // Persistent singleton Item stored on Map.Internal.
    // Owns all bot tracking and the population management timer.
    // No world-placed spawner objects required.
    public class PlayerBotDirector : Item
    {
        // ── Singleton ──────────────────────────────────────────────────────────
        private static PlayerBotDirector m_Instance;

        public static PlayerBotDirector Instance { get { return m_Instance; } }

        public static void EnsureExistence()
        {
            if ( m_Instance == null )
                m_Instance = new PlayerBotDirector();
        }

        // RunUO calls all public static Initialize() methods at script load.
        public static void Initialize()
        {
            EnsureExistence();
            PlayerBotNavigator.Initialize();

            CommandSystem.Register( "BotDirector",     AccessLevel.GameMaster, new CommandEventHandler( BotDirector_OnCommand ) );
            CommandSystem.Register( "SpawnBot",         AccessLevel.GameMaster, new CommandEventHandler( SpawnBot_OnCommand ) );
            CommandSystem.Register( "BotStatus",        AccessLevel.GameMaster, new CommandEventHandler( BotStatus_OnCommand ) );
            CommandSystem.Register( "HireNearbyBots",   AccessLevel.GameMaster, new CommandEventHandler( HireNearbyBots_OnCommand ) );
            CommandSystem.Register( "DismissBots",      AccessLevel.GameMaster, new CommandEventHandler( DismissBots_OnCommand ) );
            CommandSystem.Register( "MyBots",           AccessLevel.Player,     new CommandEventHandler( MyBots_OnCommand ) );
        }

        // ── Persistent state ───────────────────────────────────────────────────
        private bool         m_Enabled;
        private int          m_TargetBotCount;
        private List<Serial> m_BotSerials;
        private Timer        m_DirectorTimer;
        private Timer        m_POITimer;
        private Timer        m_EncounterTimer;
        private Timer        m_BurstTimer;

        // Tunable settings (serialized v1)
        private int m_PoiTimeoutMinutes       = 15;
        private int m_EncounterTimeoutMinutes  = 8;
        private int m_EncounterChancePct       = 40;
        private int m_EncounterTickSeconds     = 15;
        private int m_PoiTickMinutes           = 2;
        private int m_DirectorTickMinutes      = 1;
        private int m_MaxBurstPerTick          = 5;
        private int m_ObservationRadius        = 18;

        [CommandProperty(AccessLevel.GameMaster)]
        public bool Enabled
        {
            get { return m_Enabled; }
            set
            {
                m_Enabled = value;
                if ( m_Enabled )
                    StartDirectorTimer();
                else
                {
                    if ( m_DirectorTimer  != null ) m_DirectorTimer.Stop();
                    if ( m_POITimer       != null ) m_POITimer.Stop();
                    if ( m_EncounterTimer != null ) m_EncounterTimer.Stop();
                    if ( m_BurstTimer     != null ) m_BurstTimer.Stop();
                }
            }
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public int TargetBotCount
        {
            get { return m_TargetBotCount; }
            set
            {
                int capped = Math.Max( 0, value );
                bool raised = capped > m_TargetBotCount;
                m_TargetBotCount = capped;
                if ( raised && m_Enabled )
                    StartBurstTimer();
            }
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public int PoiTimeoutMinutes
        {
            get { return m_PoiTimeoutMinutes; }
            set { m_PoiTimeoutMinutes = Math.Max( 1, Math.Min( 60, value ) ); }
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public int EncounterTimeoutMinutes
        {
            get { return m_EncounterTimeoutMinutes; }
            set { m_EncounterTimeoutMinutes = Math.Max( 1, Math.Min( 60, value ) ); }
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public int EncounterChancePct
        {
            get { return m_EncounterChancePct; }
            set { m_EncounterChancePct = Math.Max( 5, Math.Min( 100, value ) ); }
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public int EncounterTickSeconds
        {
            get { return m_EncounterTickSeconds; }
            set
            {
                m_EncounterTickSeconds = Math.Max( 5, Math.Min( 300, value ) );
                if ( m_Enabled ) RestartEncounterTimer();
            }
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public int PoiTickMinutes
        {
            get { return m_PoiTickMinutes; }
            set
            {
                m_PoiTickMinutes = Math.Max( 1, Math.Min( 30, value ) );
                if ( m_Enabled ) RestartPoiTimer();
            }
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public int DirectorTickMinutes
        {
            get { return m_DirectorTickMinutes; }
            set
            {
                m_DirectorTickMinutes = Math.Max( 1, Math.Min( 30, value ) );
                if ( m_Enabled ) RestartDirectorTickTimer();
            }
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public int MaxBurstPerTick
        {
            get { return m_MaxBurstPerTick; }
            set { m_MaxBurstPerTick = Math.Max( 1, Math.Min( 20, value ) ); }
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public int ObservationRadius
        {
            get { return m_ObservationRadius; }
            set { m_ObservationRadius = Math.Max( 5, Math.Min( 50, value ) ); }
        }

        // ── Constructors ────────────────────────────────────────────────────────
        private PlayerBotDirector() : base( 1 )
        {
            Name            = "PlayerBot Director";
            Movable         = false;
            Visible         = false;
            m_BotSerials     = new List<Serial>();
            m_TargetBotCount = 20;
            m_Enabled        = true;
            MoveToWorld( new Point3D( 0, 0, 0 ), Map.Internal );
            StartDirectorTimer();
        }

        public PlayerBotDirector( Serial serial ) : base( serial )
        {
            m_Instance = this;
        }

        // ── Director loop ──────────────────────────────────────────────────────
        private void StartDirectorTimer()
        {
            if ( m_DirectorTimer  != null ) m_DirectorTimer.Stop();
            if ( m_POITimer       != null ) m_POITimer.Stop();
            if ( m_EncounterTimer != null ) m_EncounterTimer.Stop();

            m_DirectorTimer = Timer.DelayCall(
                TimeSpan.FromMinutes( m_DirectorTickMinutes ),
                TimeSpan.FromMinutes( m_DirectorTickMinutes ),
                new TimerCallback( DirectorTick ) );

            m_POITimer = Timer.DelayCall(
                TimeSpan.FromMinutes( m_PoiTickMinutes ),
                TimeSpan.FromMinutes( m_PoiTickMinutes ),
                new TimerCallback( POITick ) );

            m_EncounterTimer = Timer.DelayCall(
                TimeSpan.FromSeconds( 10.0 ),
                TimeSpan.FromSeconds( m_EncounterTickSeconds ),
                new TimerCallback( EncounterTick ) );

            StartBurstTimer();
        }

        private void RestartDirectorTickTimer()
        {
            if ( m_DirectorTimer != null ) m_DirectorTimer.Stop();
            m_DirectorTimer = Timer.DelayCall(
                TimeSpan.FromMinutes( m_DirectorTickMinutes ),
                TimeSpan.FromMinutes( m_DirectorTickMinutes ),
                new TimerCallback( DirectorTick ) );
        }

        private void RestartPoiTimer()
        {
            if ( m_POITimer != null ) m_POITimer.Stop();
            m_POITimer = Timer.DelayCall(
                TimeSpan.FromMinutes( m_PoiTickMinutes ),
                TimeSpan.FromMinutes( m_PoiTickMinutes ),
                new TimerCallback( POITick ) );
        }

        private void RestartEncounterTimer()
        {
            if ( m_EncounterTimer != null ) m_EncounterTimer.Stop();
            m_EncounterTimer = Timer.DelayCall(
                TimeSpan.FromSeconds( 10.0 ),
                TimeSpan.FromSeconds( m_EncounterTickSeconds ),
                new TimerCallback( EncounterTick ) );
        }

        private void StartBurstTimer()
        {
            if ( m_BurstTimer != null ) m_BurstTimer.Stop();
            m_BurstTimer = Timer.DelayCall(
                TimeSpan.Zero,
                TimeSpan.FromSeconds( 2.0 ),
                new TimerCallback( BurstSpawnTick ) );
        }

        private void BurstSpawnTick()
        {
            if ( !m_Enabled || Deleted )
            {
                if ( m_BurstTimer != null ) m_BurstTimer.Stop();
                return;
            }

            int live = GetRegularBotCount();
            if ( live >= m_TargetBotCount )
            {
                m_BurstTimer.Stop();
                return;
            }

            // Spawn up to m_MaxBurstPerTick bots per tick; prefer POI locations, fall back to Britain
            List<BotPOI> underpopulated = PlayerBotPOI.GetUnderpopulated();
            int toSpawn = Math.Min( m_MaxBurstPerTick, m_TargetBotCount - live );
            int spawned = 0;

            for ( int i = 0; i < toSpawn; i++ )
            {
                if ( underpopulated.Count > 0 )
                {
                    BotPOI poi = underpopulated[i % underpopulated.Count];
                    Point3D loc = PlayerBotPOI.RandomSpawnPoint( poi );
                    bool isGuarded = Region.Find( loc, poi.Map ) is GuardedRegion;
                    if ( isGuarded && Utility.Random( 3 ) == 0 )
                        continue;
                    PlayerBot bot = new PlayerBot();
                    if ( isGuarded && bot.PlayerBotProfile == PlayerBotPersona.PlayerBotProfile.PlayerKiller )
                    {
                        bot.Delete();
                        continue;
                    }
                    bot.MoveToWorld( loc, poi.Map );
                    bot.Home      = loc;
                    bot.RangeHome = poi.SpawnRadius;
                    bot.MarkObserved();
                    RegisterBot( bot );
                }
                else
                {
                    // Fallback: spawn near Britain, but never spawn a PK inside a guarded region
                    Point3D fallbackLoc = new Point3D(
                        1445 + Utility.RandomMinMax( -100, 100 ),
                        1599 + Utility.RandomMinMax( -100, 100 ),
                        0 );
                    fallbackLoc = new Point3D( fallbackLoc.X, fallbackLoc.Y, Map.Felucca.GetAverageZ( fallbackLoc.X, fallbackLoc.Y ) );
                    bool isGuardedFallback = Region.Find( fallbackLoc, Map.Felucca ) is GuardedRegion;
                    if ( isGuardedFallback && Utility.Random( 3 ) == 0 )
                        continue;
                    PlayerBot fallbackBot = new PlayerBot();
                    if ( isGuardedFallback && fallbackBot.PlayerBotProfile == PlayerBotPersona.PlayerBotProfile.PlayerKiller )
                    {
                        fallbackBot.Delete();
                        continue;
                    }
                    fallbackBot.MoveToWorld( fallbackLoc, Map.Felucca );
                    fallbackBot.Home      = fallbackLoc;
                    fallbackBot.RangeHome = 25;
                    RegisterBot( fallbackBot );
                }
                spawned++;
            }
        }

        private void DirectorTick()
        {
            if ( !m_Enabled || Deleted ) return;

            // Prune stale serials and remove ghost bots (alive but stranded on Map.Internal)
            for ( int i = m_BotSerials.Count - 1; i >= 0; i-- )
            {
                PlayerBot bot = World.FindMobile( m_BotSerials[i] ) as PlayerBot;
                if ( bot == null || bot.Deleted )
                    m_BotSerials.RemoveAt( i );
                else if ( bot.Map == Map.Internal )
                    bot.Delete(); // ghost bot — OnDelete() removes it from m_BotSerials
            }

            // Refresh observation timestamps; collect unobserved bots for despawn.
            // Encounter bots despawn after 8 min unobserved; POI bots after 15 min.
            DateTime now        = DateTime.Now;
            TimeSpan poiTimeout = TimeSpan.FromMinutes( m_PoiTimeoutMinutes );
            TimeSpan encTimeout = TimeSpan.FromMinutes( m_EncounterTimeoutMinutes );
            List<PlayerBot> toDelete = new List<PlayerBot>();

            foreach ( Serial s in m_BotSerials )
            {
                PlayerBot bot = World.FindMobile( s ) as PlayerBot;
                if ( bot == null || bot.Deleted || bot.Controled ) continue;
                if ( bot.Map == null || bot.Map == Map.Internal ) continue;

                bool observed = false;
                IPooledEnumerable eable = bot.Map.GetClientsInRange( bot.Location, m_ObservationRadius );
                foreach ( NetState ns in eable )
                {
                    if ( ns.Mobile != null && !ns.Mobile.Deleted && ns.Mobile.Alive )
                    {
                        observed = true;
                        break;
                    }
                }
                eable.Free();

                if ( observed )
                {
                    bot.MarkObserved();
                }
                else
                {
                    TimeSpan timeout = bot.IsEncounterBot ? encTimeout : poiTimeout;
                    if ( now - bot.LastObserved > timeout )
                        toDelete.Add( bot );
                }
            }

            foreach ( PlayerBot bot in toDelete )
                bot.Delete();

            // Occasionally form random groups
            if ( Utility.Random( 5 ) == 0 )
                TryFormRandomGroup();
        }

        // ── POI tick: ensure each point-of-interest has its target bot count ──
        private void POITick()
        {
            if ( !m_Enabled || Deleted ) return;
            if ( GetRegularBotCount() >= m_TargetBotCount ) return;

            List<BotPOI> underpopulated = PlayerBotPOI.GetUnderpopulated();
            int spawned = 0;

            foreach ( BotPOI poi in underpopulated )
            {
                if ( spawned >= 3 ) break;
                if ( GetRegularBotCount() >= m_TargetBotCount ) break;

                Point3D loc = PlayerBotPOI.RandomSpawnPoint( poi );
                bool isGuarded = Region.Find( loc, poi.Map ) is GuardedRegion;
                if ( isGuarded && Utility.Random( 3 ) == 0 )
                    continue;
                PlayerBot bot = new PlayerBot();
                if ( isGuarded && bot.PlayerBotProfile == PlayerBotPersona.PlayerBotProfile.PlayerKiller )
                {
                    bot.Delete();
                    continue;
                }
                bot.MoveToWorld( loc, poi.Map );
                bot.Home      = loc;
                bot.RangeHome = poi.SpawnRadius;
                bot.MarkObserved();
                RegisterBot( bot );
                spawned++;
            }
        }

        // ── Encounter tick: spawn bots near traveling players ──────────────────
        private void EncounterTick()
        {
            if ( !m_Enabled || Deleted ) return;

            // Snapshot the list to avoid issues if it changes during iteration
            List<NetState> states = new List<NetState>( NetState.Instances );
            foreach ( NetState ns in states )
            {
                if ( ns == null ) continue;
                Mobile player = ns.Mobile;
                if ( player == null || player.Deleted || !player.Alive ) continue;
                if ( player.Map != Map.Felucca ) continue;

                if ( Utility.Random( 100 ) >= m_EncounterChancePct ) continue;

                TrySpawnEncounter( player );
            }
        }

        private void TrySpawnEncounter( Mobile player )
        {
            Point3D? spawnPt = FindEncounterSpawnPoint( player );
            if ( !spawnPt.HasValue ) return;

            bool inTown       = Region.Find( spawnPt.Value,  player.Map ) is GuardedRegion;
            bool playerInTown = Region.Find( player.Location, player.Map ) is GuardedRegion;

            int count = 1 + Utility.Random( 3 ); // 1-3 bots per encounter
            for ( int i = 0; i < count; i++ )
            {
                int ox = i == 0 ? 0 : Utility.RandomMinMax( -3, 3 );
                int oy = i == 0 ? 0 : Utility.RandomMinMax( -3, 3 );
                Point3D loc = new Point3D( spawnPt.Value.X + ox, spawnPt.Value.Y + oy, spawnPt.Value.Z );

                if ( i > 0 && !Map.Felucca.CanSpawnMobile( loc ) )
                    loc = spawnPt.Value;

                if ( inTown && Utility.Random( 3 ) == 0 )
                    continue;
                PlayerBot bot = new PlayerBot();
                if ( (inTown || playerInTown) && bot.PlayerBotProfile == PlayerBotPersona.PlayerBotProfile.PlayerKiller )
                {
                    bot.Delete();
                    continue;
                }
                bot.MoveToWorld( loc, Map.Felucca );
                bot.IsEncounterBot = true;
                bot.MarkObserved();

                // Give the bot a natural travel destination so it appears to be passing through
                BotWaypoint wp = PlayerBotNavigator.PickDestination( bot.PlayerBotProfile );
                if ( wp != null )
                {
                    bot.ActivityState.TravelDestination = wp.Location;
                    bot.ActivityState.TravelMap         = wp.Map;
                    bot.SetAfterTravelActivity( BotActivity.TownVisit );
                    bot.ActivityState.SetActivity( BotActivity.Traveling );
                }

                RegisterBot( bot );
            }
        }

        private Point3D? FindEncounterSpawnPoint( Mobile player )
        {
            for ( int tries = 0; tries < 15; tries++ )
            {
                double angle = Utility.Random( 360 ) * Math.PI / 180.0;
                int dist = Utility.RandomMinMax( 20, 28 );
                int x = player.X + (int)(Math.Cos( angle ) * dist);
                int y = player.Y + (int)(Math.Sin( angle ) * dist);
                int z = Map.Felucca.GetAverageZ( x, y );
                Point3D p = new Point3D( x, y, z );
                if ( Map.Felucca.CanSpawnMobile( p ) )
                    return p;
            }
            return null;
        }

        // ── Bot management ─────────────────────────────────────────────────────
        public PlayerBot SpawnOneBot( Point3D? location )
        {
            // Default spawn area: Britain surroundings
            Point3D loc = location.HasValue
                ? location.Value
                : new Point3D(
                    1445 + Utility.RandomMinMax( -100, 100 ),
                    1599 + Utility.RandomMinMax( -100, 100 ),
                    0 );

            Map map = Map.Felucca;

            // Make sure location is valid (walk up/down a little if needed)
            int z = map.GetAverageZ( loc.X, loc.Y );
            loc = new Point3D( loc.X, loc.Y, z );

            PlayerBot bot = new PlayerBot();
            bot.MoveToWorld( loc, map );
            bot.Home      = loc;
            bot.RangeHome = 25;
            RegisterBot( bot );
            return bot;
        }

        public void RegisterBot( PlayerBot bot )
        {
            if ( !m_BotSerials.Contains( bot.Serial ) )
                m_BotSerials.Add( bot.Serial );
        }

        public void UnregisterBot( PlayerBot bot )
        {
            m_BotSerials.Remove( bot.Serial );
        }

        public List<PlayerBot> GetLiveBots()
        {
            List<PlayerBot> result = new List<PlayerBot>();
            foreach ( Serial s in m_BotSerials )
            {
                Mobile m = World.FindMobile( s );
                if ( m is PlayerBot && !m.Deleted )
                    result.Add( (PlayerBot)m );
            }
            return result;
        }

        public List<PlayerBot> GetEncounterBots()
        {
            List<PlayerBot> result = new List<PlayerBot>();
            foreach ( Serial s in m_BotSerials )
            {
                PlayerBot bot = World.FindMobile( s ) as PlayerBot;
                if ( bot != null && !bot.Deleted && bot.IsEncounterBot )
                    result.Add( bot );
            }
            return result;
        }

        public List<PlayerBot> GetRegularBots()
        {
            List<PlayerBot> result = new List<PlayerBot>();
            foreach ( Serial s in m_BotSerials )
            {
                PlayerBot bot = World.FindMobile( s ) as PlayerBot;
                if ( bot != null && !bot.Deleted && !bot.IsEncounterBot )
                    result.Add( bot );
            }
            return result;
        }

        public List<PlayerBot> GetControlledBots()
        {
            List<PlayerBot> result = new List<PlayerBot>();
            foreach ( Serial s in m_BotSerials )
            {
                PlayerBot bot = World.FindMobile( s ) as PlayerBot;
                if ( bot != null && !bot.Deleted && bot.Controled )
                    result.Add( bot );
            }
            return result;
        }

        public List<PlayerBot> GetBotsByPersona( PlayerBotPersona.PlayerBotProfile persona )
        {
            List<PlayerBot> result = new List<PlayerBot>();
            foreach ( PlayerBot bot in GetLiveBots() )
                if ( bot.PlayerBotProfile == persona )
                    result.Add( bot );
            return result;
        }

        public List<PlayerBot> GetBotsByActivity( BotActivity activity )
        {
            List<PlayerBot> result = new List<PlayerBot>();
            foreach ( PlayerBot bot in GetLiveBots() )
                if ( bot.ActivityState.Current == activity )
                    result.Add( bot );
            return result;
        }

        public List<PlayerBotGroup> GetActiveGroups()
        {
            List<PlayerBotGroup> seen = new List<PlayerBotGroup>();
            foreach ( PlayerBot bot in GetLiveBots() )
                if ( bot.Group != null && !seen.Contains( bot.Group ) )
                    seen.Add( bot.Group );
            return seen;
        }

        public int DeleteEncounterBots()
        {
            List<PlayerBot> bots = GetEncounterBots();
            foreach ( PlayerBot bot in bots )
                bot.Delete();
            return bots.Count;
        }

        public int DeleteRegularBots()
        {
            List<PlayerBot> bots = GetRegularBots();
            foreach ( PlayerBot bot in bots )
                bot.Delete();
            return bots.Count;
        }

        public PlayerBot SpawnOneBot( Point3D? location, PlayerBotPersona.PlayerBotProfile profile, PlayerBotPersona.PlayerBotExperience xp )
        {
            Point3D loc = location.HasValue
                ? location.Value
                : new Point3D(
                    1445 + Utility.RandomMinMax( -100, 100 ),
                    1599 + Utility.RandomMinMax( -100, 100 ),
                    0 );

            Map map = Map.Felucca;
            int z   = map.GetAverageZ( loc.X, loc.Y );
            loc = new Point3D( loc.X, loc.Y, z );

            PlayerBot bot = new PlayerBot( profile, xp );
            bot.MoveToWorld( loc, map );
            bot.Home      = loc;
            bot.RangeHome = 25;
            RegisterBot( bot );
            return bot;
        }

        public int SpawnNBots( int count, Point3D? location, PlayerBotPersona.PlayerBotProfile profile, PlayerBotPersona.PlayerBotExperience xp )
        {
            count = Math.Max( 1, Math.Min( 50, count ) );
            for ( int i = 0; i < count; i++ )
                SpawnOneBot( location, profile, xp );
            return count;
        }

        public void ResetSettingsToDefaults()
        {
            m_PoiTimeoutMinutes       = 15;
            m_EncounterTimeoutMinutes  = 8;
            m_EncounterChancePct      = 40;
            m_EncounterTickSeconds    = 15;
            m_PoiTickMinutes          = 2;
            m_DirectorTickMinutes     = 1;
            m_MaxBurstPerTick         = 5;
            m_ObservationRadius       = 18;
            if ( m_Enabled ) StartDirectorTimer();
        }

        // Encounter bots are excluded from the population cap
        private int GetRegularBotCount()
        {
            int count = 0;
            foreach ( Serial s in m_BotSerials )
            {
                PlayerBot bot = World.FindMobile( s ) as PlayerBot;
                if ( bot != null && !bot.Deleted && !bot.IsEncounterBot )
                    count++;
            }
            return count;
        }

        private void TryFormRandomGroup()
        {
            List<PlayerBot> bots = GetLiveBots();
            if ( bots.Count < 2 ) return;

            PlayerBot seed = bots[Utility.Random( bots.Count )];
            if ( seed.Group != null || seed.Controled ) return;

            PlayerBotGroup.TryForm( seed, 15 );
        }

        // ── Serialization ──────────────────────────────────────────────────────
        public override void Serialize( GenericWriter writer )
        {
            base.Serialize( writer );
            writer.Write( (int)1 ); // version

            writer.Write( (bool)m_Enabled );
            writer.Write( (int)m_TargetBotCount );

            writer.Write( (int)m_BotSerials.Count );
            foreach ( Serial s in m_BotSerials )
                writer.Write( (int)s );

            // version 1
            writer.Write( (int)m_PoiTimeoutMinutes );
            writer.Write( (int)m_EncounterTimeoutMinutes );
            writer.Write( (int)m_EncounterChancePct );
            writer.Write( (int)m_EncounterTickSeconds );
            writer.Write( (int)m_PoiTickMinutes );
            writer.Write( (int)m_DirectorTickMinutes );
            writer.Write( (int)m_MaxBurstPerTick );
            writer.Write( (int)m_ObservationRadius );
        }

        public override void Deserialize( GenericReader reader )
        {
            base.Deserialize( reader );
            int version = reader.ReadInt();

            m_Instance   = this;
            m_BotSerials = new List<Serial>();

            m_Enabled        = reader.ReadBool();
            m_TargetBotCount = reader.ReadInt();

            int count = reader.ReadInt();
            for ( int i = 0; i < count; i++ )
                m_BotSerials.Add( (Serial)reader.ReadInt() );

            if ( version >= 1 )
            {
                m_PoiTimeoutMinutes      = reader.ReadInt();
                m_EncounterTimeoutMinutes = reader.ReadInt();
                m_EncounterChancePct     = reader.ReadInt();
                m_EncounterTickSeconds   = reader.ReadInt();
                m_PoiTickMinutes         = reader.ReadInt();
                m_DirectorTickMinutes    = reader.ReadInt();
                m_MaxBurstPerTick        = reader.ReadInt();
                m_ObservationRadius      = reader.ReadInt();
            }
            else
            {
                m_PoiTimeoutMinutes       = 15;
                m_EncounterTimeoutMinutes  = 8;
                m_EncounterChancePct      = 40;
                m_EncounterTickSeconds    = 15;
                m_PoiTickMinutes          = 2;
                m_DirectorTickMinutes     = 1;
                m_MaxBurstPerTick         = 5;
                m_ObservationRadius       = 18;
            }

            // Re-initialize navigation landmarks after deserialization
            PlayerBotNavigator.Initialize();

            if ( m_Enabled )
                StartDirectorTimer();
        }

        // ── GM Commands ────────────────────────────────────────────────────────
        [Usage("BotDirector")]
        [Description("Opens the PlayerBot Director management gump.")]
        private static void BotDirector_OnCommand( CommandEventArgs e )
        {
            EnsureExistence();
            e.Mobile.SendGump( new PlayerBotDirectorGump( e.Mobile, m_Instance ) );
        }

        [Usage("SpawnBot")]
        [Description("Spawns one PlayerBot at your location.")]
        private static void SpawnBot_OnCommand( CommandEventArgs e )
        {
            EnsureExistence();
            PlayerBot bot = m_Instance.SpawnOneBot( e.Mobile.Location );
            e.Mobile.SendMessage( "Spawned: {0} (serial {1})", bot.Name, bot.Serial );
        }

        [Usage("BotStatus")]
        [Description("Reports live bot count and Director state.")]
        private static void BotStatus_OnCommand( CommandEventArgs e )
        {
            if ( m_Instance == null )
            {
                e.Mobile.SendMessage( "Director not initialized." );
                return;
            }

            int live = m_Instance.GetLiveBots().Count;
            e.Mobile.SendMessage( "Director: enabled={0}  target={1}  tracked={2}  live={3}",
                m_Instance.Enabled, m_Instance.TargetBotCount, m_Instance.m_BotSerials.Count, live );
        }

        [Usage("HireNearbyBots")]
        [Description("Recruits nearby uncontrolled bots to you.  Optional arg: count (default 10).")]
        private static void HireNearbyBots_OnCommand( CommandEventArgs e )
        {
            int count = (e.Length > 0) ? e.GetInt32( 0 ) : 10;
            int hired = 0;

            IPooledEnumerable eable = e.Mobile.Map.GetMobilesInRange( e.Mobile.Location, 20 );
            foreach ( Mobile m in eable )
            {
                if ( hired >= count ) break;
                PlayerBot bot = m as PlayerBot;
                if ( bot == null || bot.Controled ) continue;
                if ( bot.AddHire( e.Mobile ) )
                    hired++;
            }
            eable.Free();

            e.Mobile.SendMessage( "Hired {0} bot{1}.", hired, hired == 1 ? "" : "s" );
        }

        [Usage("MyBots")]
        [Description("Opens the PlayerBot management interface for your controlled bots.")]
        private static void MyBots_OnCommand( CommandEventArgs e )
        {
            e.Mobile.SendGump( new PlayerBotListGump( e.Mobile, 0 ) );
        }

        [Usage("DismissBots")]
        [Description("Releases all bots you currently control back to the world.")]
        private static void DismissBots_OnCommand( CommandEventArgs e )
        {
            int dismissed = 0;

            // Collect first to avoid modifying during iteration
            List<Mobile> controlled = new List<Mobile>();
            foreach ( Mobile m in e.Mobile.GetMobilesInRange( 30 ) )
            {
                PlayerBot bot = m as PlayerBot;
                if ( bot != null && bot.Controled && bot.ControlMaster == e.Mobile )
                    controlled.Add( bot );
            }

            foreach ( Mobile m in controlled )
            {
                PlayerBot bot = (PlayerBot)m;
                bot.SetControlMaster( null );
                bot.ActivityState.SetActivity( BotActivity.Wandering );
                dismissed++;
            }

            e.Mobile.SendMessage( "Dismissed {0} bot{1}.", dismissed, dismissed == 1 ? "" : "s" );
        }
    }
}
