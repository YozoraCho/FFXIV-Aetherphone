using System.Globalization;
using Aetherphone.Core.Apps;
using Aetherphone.Core.Game;
using Aetherphone.Core.Home;
using Aetherphone.Core.Inventory;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Notifications;
using Aetherphone.Core.Runtime;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Chat;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace Aetherphone.Core.Gardening;

internal sealed class GardenTracker : IDisposable
{
    private const long TickIntervalMilliseconds = 1000;
    private const string LogPrefix = "[Greenthumb] ";
    private const string MenuAddon = "SelectString";
    private const string IconMenuAddon = "SelectIconString";
    private const int MaxLoggedOptions = 12;
    private const uint FirstGardenMessage = 4005;
    private const uint LastGardenMessage = 4026;
    private const uint TendMessage = 4017;
    private const int MaxLoggedParameters = 4;
    private const uint GardenObjectId = 2003757;
    private const string HousingAddonPrefix = "Housing";
    private const string NoticeGroup = "greenthumb.track";
    private static readonly TimeSpan InteractionWindow = TimeSpan.FromSeconds(30);
    private static readonly Vector4 Accent = AppAccents.For(GardenCatalog.AppId);

    private readonly Configuration configuration;
    private readonly GardenCatalog catalog;
    private readonly GardenInbox inbox;
    private readonly NotificationService notifications;
    private readonly IChatGui chat;
    private readonly IAddonLifecycle lifecycle;
    private readonly FrameworkTicker ticker;
    private readonly InventoryLocalRead localRead = new();
    private readonly List<GardenSignal> signals = new();
    private Dictionary<uint, int> before = new();
    private Dictionary<uint, int> after = new();
    private bool hasBaseline;
    private ushort saddlebagPages;
    private DateTime interactionUtc;

    public GardenTracker(Configuration configuration, GardenCatalog catalog, GardenInbox inbox,
        NotificationService notifications, IChatGui chat, IAddonLifecycle lifecycle, IFramework framework,
        AppGate gate)
    {
        this.configuration = configuration;
        this.catalog = catalog;
        this.inbox = inbox;
        this.notifications = notifications;
        this.chat = chat;
        this.lifecycle = lifecycle;
        ticker = new FrameworkTicker(framework, TickIntervalMilliseconds, OnTick, gate);
        chat.LogMessage += OnLogMessage;
        lifecycle.RegisterListener(AddonEvent.PostSetup, OnAddonSetup);
    }

    private bool Tracking => configuration.GreenthumbTrackGarden;

#if DEBUG
    private bool Logging => configuration.GreenthumbTrackingLog;
#else
    private static bool Logging => false;
#endif

    private bool Watching => Tracking || Logging;

    private void OnAddonSetup(AddonEvent type, AddonArgs args)
    {
        if (!Watching || !InHousingArea())
        {
            return;
        }

        var name = args.AddonName;
        if (!IsMenu(name) || !TargetIsGarden())
        {
            return;
        }

        interactionUtc = DateTime.UtcNow;
        if (Logging)
        {
            AepLog.Info(string.Concat(LogPrefix, "menu opened: ", name, " | target: ", DescribeTarget(),
                " | options: ", DescribeOptions(args)));
        }
    }

    private static bool IsMenu(string name) =>
        string.Equals(name, MenuAddon, StringComparison.Ordinal) ||
        string.Equals(name, IconMenuAddon, StringComparison.Ordinal) ||
        name.StartsWith(HousingAddonPrefix, StringComparison.Ordinal);

    private static unsafe bool TargetIsGarden()
    {
        var targets = TargetSystem.Instance();
        if (targets is null || targets->Target is null)
        {
            return false;
        }

        var target = targets->Target;
        return target->ObjectKind == FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind.EventObj &&
               (target->BaseId == GardenObjectId || target->NameString.Length == 0);
    }

    private static unsafe string DescribeTarget()
    {
        var targets = TargetSystem.Instance();
        if (targets is null || targets->Target is null)
        {
            return "none";
        }

        var target = targets->Target;
        return string.Concat(target->NameString, " (", target->ObjectKind.ToString(), " ",
            target->BaseId.ToString(CultureInfo.InvariantCulture), ")");
    }

    private static string DescribeOptions(AddonArgs args)
    {
        var parts = new List<string>();
        foreach (var value in args.Addon.AtkValues)
        {
            if (parts.Count >= MaxLoggedOptions)
            {
                break;
            }

            var text = value.GetValue()?.ToString();
            if (!string.IsNullOrWhiteSpace(text) && !double.TryParse(text, out _) && text is not "True" and not "False")
            {
                parts.Add(text);
            }
        }

        return string.Join(" / ", parts);
    }

    private void OnLogMessage(ILogMessage message)
    {
        var id = message.LogMessageId;
        if (!Watching || id < FirstGardenMessage || id > LastGardenMessage || !InHousingArea())
        {
            return;
        }

        if (Logging)
        {
            AepLog.Info(string.Concat(LogPrefix, "garden message ", id.ToString(CultureInfo.InvariantCulture),
                DescribeParameters(message)));
        }

        if (id == TendMessage)
        {
            Report(new GardenSignal(GardenActionKind.Tend, -1, DateTime.UtcNow));
        }
    }

    private static string DescribeParameters(ILogMessage message)
    {
        var count = Math.Min(message.ParameterCount, MaxLoggedParameters);
        var text = string.Empty;
        for (var index = 0; index < count; index++)
        {
            if (message.TryGetIntParameter(index, out var value))
            {
                text = string.Concat(text, " | ", value.ToString(CultureInfo.InvariantCulture));
            }
        }

        return text;
    }

    private void OnTick()
    {
        if (!Watching)
        {
            hasBaseline = false;
            return;
        }

        catalog.EnsureLoaded();
        if (catalog.State != GardenCatalogState.Ready || !InHousingArea() || !InventoryReader.ReadLocal(localRead))
        {
            hasBaseline = false;
            interactionUtc = default;
            return;
        }

        CountGardenItems();
        var nowUtc = DateTime.UtcNow;
        if (localRead.Saddlebag.LoadedPages != saddlebagPages)
        {
            saddlebagPages = localRead.Saddlebag.LoadedPages;
            hasBaseline = false;
        }

        if (hasBaseline)
        {
            signals.Clear();
            GardenDelta.Classify(before, after, catalog, nowUtc, signals);
            if (signals.Count > 0 && InWindow(nowUtc))
            {
                for (var index = 0; index < signals.Count; index++)
                {
                    Report(signals[index]);
                }
            }
        }

        (before, after) = (after, before);
        hasBaseline = true;
    }

    private void CountGardenItems()
    {
        after.Clear();
        AddCounts(localRead.Bags.Stacks);
        AddCounts(localRead.Saddlebag.Stacks);
    }

    private void AddCounts(List<InventoryStack> stacks)
    {
        for (var index = 0; index < stacks.Count; index++)
        {
            var stack = stacks[index];
            if (!catalog.TryIndexItem(stack.ItemId, out _) &&
                !catalog.TryIndexCrop(stack.ItemId, out _))
            {
                continue;
            }

            after.TryGetValue(stack.ItemId, out var count);
            after[stack.ItemId] = count + stack.Quantity;
        }
    }

    private void Report(GardenSignal signal)
    {
        if (Logging)
        {
            var seed = signal.Seed >= 0 ? string.Concat(": ", catalog.Book.Seeds[signal.Seed].Key) : string.Empty;
            AepLog.Info(string.Concat(LogPrefix, "noticed ", signal.Kind.ToString(), seed));
        }

        if (!Tracking)
        {
            return;
        }

        inbox.Add(signal);
        notifications.Notify(new PhoneNotification(GardenCatalog.AppId, ActionLabel(signal),
            Loc.T(L.Greenthumb.PlaceNotice), DateTime.Now, Accent, NoticeGroup));
    }

    public string ActionLabel(GardenSignal signal) => signal.Kind switch
    {
        GardenActionKind.Plant when signal.Seed >= 0 => Loc.T(L.Greenthumb.ActionPlanted, catalog.Name(signal.Seed)),
        GardenActionKind.Harvest when signal.Seed >= 0 =>
            Loc.T(L.Greenthumb.ActionHarvested, catalog.Name(signal.Seed)),
        _ => Loc.T(L.Greenthumb.ActionTended),
    };

    private bool InWindow(DateTime nowUtc) =>
        interactionUtc != default && nowUtc - interactionUtc <= InteractionWindow;

    private static unsafe bool InHousingArea()
    {
        if (!GameMemory.Attached)
        {
            return false;
        }

        var housing = HousingManager.Instance();
        return housing is not null && housing->CurrentTerritory is not null;
    }

    public void Dispose()
    {
        ticker.Dispose();
        chat.LogMessage -= OnLogMessage;
        lifecycle.UnregisterListener(AddonEvent.PostSetup, OnAddonSetup);
    }
}
