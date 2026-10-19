using Aetherphone.Core.Apps;
using Aetherphone.Core.Home;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Notifications;
using Aetherphone.Core.Runtime;
using Dalamud.Plugin.Services;

namespace Aetherphone.Core.Gardening;

internal sealed class GardenAlerts : IDisposable
{
    private const long TickIntervalMilliseconds = 30000;
    private const string GroupPrefix = "greenthumb.bed.";
    private static readonly Vector4 Accent = AppAccents.For(GardenCatalog.AppId);

    private readonly Configuration configuration;
    private readonly GardenCatalog catalog;
    private readonly NotificationService notifications;
    private readonly FrameworkTicker ticker;

    public GardenAlerts(Configuration configuration, GardenCatalog catalog, NotificationService notifications,
        IFramework framework, AppGate gate)
    {
        this.configuration = configuration;
        this.catalog = catalog;
        this.notifications = notifications;
        ticker = new FrameworkTicker(framework, TickIntervalMilliseconds, OnTick, gate);
    }

    public int AttentionCount { get; private set; }

    public void Refresh() => OnTick();

    private void OnTick()
    {
        var patches = configuration.GreenthumbPatches;
        if (patches.Count == 0)
        {
            AttentionCount = 0;
            return;
        }

        catalog.EnsureLoaded();
        if (catalog.State != GardenCatalogState.Ready)
        {
            return;
        }

        var nowUtc = DateTime.UtcNow;
        var attention = 0;
        var changed = false;
        for (var patchIndex = 0; patchIndex < patches.Count; patchIndex++)
        {
            var patch = patches[patchIndex];
            for (var bedIndex = 0; bedIndex < patch.Beds.Count; bedIndex++)
            {
                var bed = patch.Beds[bedIndex];
                if (!bed.IsPlanted)
                {
                    continue;
                }

                if (GardenTimers.IsReady(bed, nowUtc))
                {
                    attention++;
                    if (!bed.ReadyNotified)
                    {
                        bed.ReadyNotified = true;
                        changed = true;
                        Post(patch, bedIndex, bed, L.Greenthumb.ReadyAlert);
                    }

                    continue;
                }

                if (!GardenTimers.NeedsTending(bed, catalog.WiltHours(bed.Seed), nowUtc))
                {
                    continue;
                }

                attention++;
                if (!bed.TendNotified)
                {
                    bed.TendNotified = true;
                    changed = true;
                    Post(patch, bedIndex, bed, L.Greenthumb.TendAlert);
                }
            }
        }

        AttentionCount = attention;
        if (changed)
        {
            configuration.Save();
        }
    }

    private void Post(GardenPatch patch, int bedIndex, GardenBed bed, LocString body)
    {
        var title = catalog.NameOf(bed.Seed);
        var text = Loc.T(body, PatchLabel.For(patch), bedIndex + 1);
        var group = string.Concat(GroupPrefix, patch.Id.ToString("N"),
            bedIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
        notifications.Notify(new PhoneNotification(GardenCatalog.AppId, title, text, DateTime.Now, Accent, group));
    }

    public void Dispose() => ticker.Dispose();
}
