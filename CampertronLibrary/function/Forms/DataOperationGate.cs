using System;

namespace CampertronLibrary.function.Forms
{
    // what is using the campground data and the cache folder in this window
    internal enum DataOperation
    {
        None,
        // a run of the Results tab (GS2): it reads the database and writes the cache folder's campsite details
        Search,
        // a refresh of the campground data (V011-1): it clears the cache folder and installs a new database
        Refresh
    }

    // One data operation at a time in this window (V011-1, decision D61): the Results tab's run and the RIDB refresh never
    // overlap, because the refresh clears the cache folder a run writes. Each side asks before it starts and leaves when it
    // has ended; the other side's action is disabled meanwhile, with its reason. Navigation, the settings editors and the
    // lookup tabs are not part of it. The refresh's own lock (RecreationDotOrg.refresh.lock) still keeps refreshes of other
    // processes apart. Used on the UI thread only.
    public sealed class DataOperationGate
    {
        internal DataOperation Current { get; private set; } = DataOperation.None;

        // after Current changed
        internal event Action? Changed;

        // true if Operation may start now (nothing else runs); it then holds the gate until Leave
        internal bool TryEnter(DataOperation Operation)
        {
            if (Operation == DataOperation.None || Current != DataOperation.None)
            {
                return false;
            }
            Current = Operation;
            Changed?.Invoke();
            return true;
        }

        // Operation has ended; leaving a gate held by another operation (or none) changes nothing
        internal void Leave(DataOperation Operation)
        {
            if (Operation == DataOperation.None || Current != Operation)
            {
                return;
            }
            Current = DataOperation.None;
            Changed?.Invoke();
        }
    }
}
