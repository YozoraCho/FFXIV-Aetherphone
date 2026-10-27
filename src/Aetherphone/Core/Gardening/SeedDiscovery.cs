using Aetherphone.Core.Apps;
using Aetherphone.Core.Home;
using Aetherphone.Core.Inventory;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Notifications;
using Aetherphone.Core.Runtime;
using Dalamud.Plugin.Services;

namespace Aetherphone.Core.Gardening;

internal enum SeedScanResult : byte
{
    NoCharacter,
    NothingNew,
    Found,
}

internal sealed class SeedDiscovery : IDisposable
{
    private const long TickIntervalMilliseconds = 5000;
    private const int NoticeNameLimit = 3;
    private const string NoticeGroup = "greenthumb.found";
    private static readonly Vector4 Accent = AppAccents.For(GardenCatalog.AppId);

    private readonly Configuration configuration;
    private readonly GardenCatalog catalog;
    private readonly NotificationService notifications;
    private readonly FrameworkTicker ticker;
    private readonly InventoryLocalRead localRead = new();
    private readonly InventoryReadBuffer retainerRead = new();
    private readonly List<int> found = new();
    private bool[] owned = Array.Empty<bool>();

    public SeedDiscovery(Configuration configuration, GardenCatalog catalog, NotificationService notifications,
        IFramework framework, AppGate gate)
    {
        this.configuration = configuration;
        this.catalog = catalog;
        this.notifications = notifications;
        ticker = new FrameworkTicker(framework, TickIntervalMilliseconds, OnTick, gate);
    }

    public int Version { get; private set; }

    public int LastFoundCount { get; private set; }

    public SeedScanResult ScanNow() => Scan();

    private void OnTick()
    {
        if (configuration.GreenthumbFindOwnedSeeds)
        {
            Scan();
        }
    }

    private SeedScanResult Scan()
    {
        catalog.EnsureLoaded();
        if (catalog.State != GardenCatalogState.Ready)
        {
            return SeedScanResult.NothingNew;
        }

        if (!InventoryReader.ReadLocal(localRead))
        {
            return SeedScanResult.NoCharacter;
        }

        RefreshOwned();
        found.Clear();
        Collect(localRead.Bags.Stacks);
        Collect(localRead.Saddlebag.Stacks);
        if (InventoryReader.ReadActiveRetainer(retainerRead, out _, out _))
        {
            Collect(retainerRead.Stacks);
        }

        if (found.Count == 0)
        {
            return SeedScanResult.NothingNew;
        }

        var keys = configuration.GreenthumbOwnedSeeds;
        for (var index = 0; index < found.Count; index++)
        {
            keys.Add(catalog.Book.Seeds[found[index]].Key);
        }

        configuration.Save();
        LastFoundCount = found.Count;
        Version++;
        Notify();
        return SeedScanResult.Found;
    }

    private void RefreshOwned()
    {
        if (owned.Length != catalog.Count)
        {
            owned = new bool[catalog.Count];
        }

        Array.Clear(owned);
        var keys = configuration.GreenthumbOwnedSeeds;
        for (var index = 0; index < keys.Count; index++)
        {
            if (catalog.TryIndex(keys[index], out var seed))
            {
                owned[seed] = true;
            }
        }
    }

    private void Collect(List<InventoryStack> stacks)
    {
        for (var index = 0; index < stacks.Count; index++)
        {
            if (catalog.TryIndexItem(stacks[index].ItemId, out var seed))
            {
                SeedMatcher.AddIfNew(seed, owned, found);
            }
        }
    }

    private void Notify()
    {
        var title = Loc.Plural(L.Greenthumb.FoundSeeds, found.Count);
        var shown = Math.Min(found.Count, NoticeNameLimit);
        var names = new string[shown];
        for (var index = 0; index < shown; index++)
        {
            names[index] = catalog.Name(found[index]);
        }

        var body = string.Join(Loc.T(L.Greenthumb.ListSeparator), names);
        if (found.Count > shown)
        {
            body = Loc.T(L.Greenthumb.AndMore, body, found.Count - shown);
        }

        notifications.Notify(new PhoneNotification(GardenCatalog.AppId, title, body, DateTime.Now, Accent,
            NoticeGroup));
    }

    public void Dispose() => ticker.Dispose();
}

internal static class SeedMatcher
{
    public static bool AddIfNew(int seed, bool[] owned, List<int> found)
    {
        if (seed < 0 || seed >= owned.Length || owned[seed] || found.Contains(seed))
        {
            return false;
        }

        found.Add(seed);
        return true;
    }
}
