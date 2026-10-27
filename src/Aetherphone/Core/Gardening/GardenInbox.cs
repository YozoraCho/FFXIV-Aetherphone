namespace Aetherphone.Core.Gardening;

internal sealed class GardenInbox
{
    public const int Capacity = 10;
    private static readonly TimeSpan KeepFor = TimeSpan.FromHours(24);

    private readonly List<GardenSignal> pending = new(Capacity);
    private readonly object sync = new();

    public int Version { get; private set; }

    public int Count
    {
        get
        {
            lock (sync)
            {
                return pending.Count;
            }
        }
    }

    public void Add(GardenSignal signal)
    {
        lock (sync)
        {
            if (pending.Count >= Capacity)
            {
                pending.RemoveAt(0);
            }

            pending.Add(signal);
            Version++;
        }
    }

    public bool TryPeek(out GardenSignal signal)
    {
        lock (sync)
        {
            Prune(DateTime.UtcNow);
            if (pending.Count == 0)
            {
                signal = default;
                return false;
            }

            signal = pending[0];
            return true;
        }
    }

    public void RemoveFirst()
    {
        lock (sync)
        {
            if (pending.Count == 0)
            {
                return;
            }

            pending.RemoveAt(0);
            Version++;
        }
    }

    private void Prune(DateTime nowUtc)
    {
        for (var index = pending.Count - 1; index >= 0; index--)
        {
            if (nowUtc - pending[index].Utc > KeepFor)
            {
                pending.RemoveAt(index);
                Version++;
            }
        }
    }
}
