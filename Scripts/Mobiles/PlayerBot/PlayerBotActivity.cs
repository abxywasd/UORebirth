using System;
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
        public DateTime LastChange;
        public Point3D TravelDestination;
        public Map TravelMap;
        public int ActivityTimer;

        public ActivityState()
        {
            Current = BotActivity.Wandering;
            LastChange = DateTime.Now;
        }

        public void SetActivity( BotActivity next )
        {
            Previous = Current;
            Current = next;
            LastChange = DateTime.Now;
            ActivityTimer = 0;
        }

        public TimeSpan TimeInCurrentActivity
        {
            get { return DateTime.Now - LastChange; }
        }
    }
}
