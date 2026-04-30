using System;
using System.Collections.Generic;
using Server;

namespace Server.Mobiles
{
    public enum BotActivity
    {
        Idle        = 0,
        Wandering   = 1,
        Traveling   = 2,
        Hunting     = 3,
        Combat      = 4,
        Fleeing     = 5,
        Crafting    = 6,
        TownVisit   = 7,
        Grouped     = 9,
        Recruited   = 10
    }

    public class ActivityState
    {
        public BotActivity Current;
        public BotActivity Previous;
        public DateTime    LastChange;
        public Point3D     TravelDestination;  // current hop target (updated each hop advance)
        public Map         TravelMap;
        public int         ActivityTimer;

        // Runtime-only hop queue — not serialized (re-computed from TravelToRandom on next tick)
        public Queue<BotWaypoint> WaypointHops;
        public BotWaypoint        FinalDestination;

        public ActivityState()
        {
            Current    = BotActivity.Wandering;
            LastChange = DateTime.Now;
        }

        public void SetActivity( BotActivity next )
        {
            Previous     = Current;
            Current      = next;
            LastChange   = DateTime.Now;
            ActivityTimer = 0;
        }

        // Start a routed journey — graph path was found.
        // TravelDestination tracks the *current hop*, not the final destination.
        public void SetTravelRoute( BotWaypoint finalDest, List<BotWaypoint> hops )
        {
            FinalDestination  = finalDest;
            WaypointHops      = new Queue<BotWaypoint>( hops );
            TravelDestination = hops.Count > 0 ? hops[0].Location : finalDest.Location;
            TravelMap         = finalDest.Map;
            SetActivity( BotActivity.Traveling );
        }

        // Start a direct journey — no graph path (island, disconnected, fallback).
        public void SetTravelDirect( BotWaypoint dest )
        {
            FinalDestination  = dest;
            WaypointHops      = null;
            TravelDestination = dest.Location;
            TravelMap         = dest.Map;
            SetActivity( BotActivity.Traveling );
        }

        public TimeSpan TimeInCurrentActivity
        {
            get { return DateTime.Now - LastChange; }
        }
    }
}
