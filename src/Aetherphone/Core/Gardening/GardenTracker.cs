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
    private const int SystemMessageKind = 57;
    private const int FirstSystemKind = 56;
    private const int LastSystemKind = 60;
    private const uint GardenObjectId = 2003757;
    private const string HousingAddonPrefix = "Housing";
    private const string NoticeGroup = "greenthumb.track";
    private static readonly TimeSpan InteractionWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StatusSettle = TimeSpan.FromSeconds(3);
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
    private DateTime interactionUtc;
    private DateTime statusUtc;
    private bool changedSinceInteraction;

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
        chat.ChatMessage += OnChatMessage;
        lifecycle.RegisterListener(AddonEvent.PostSetup, OnAddonSetup);
    }

    private bool Tracking => configuration.GreenthumbTrackGarden;

    private bool Logging => configuration.GreenthumbTrackingLog;

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
        statusUtc = default;
        changedSinceInteraction = false;
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

    private void OnChatMessage(IHandleableChatMessage message)
    {
        var kind = (int)message.LogKind;
        if (!Watching || kind < FirstSystemKind || kind > LastSystemKind || !InWindow(DateTime.UtcNow))
        {
            return;
        }

        if (Logging)
        {
            AepLog.Info(string.Concat(LogPrefix, "game message (", kind.ToString(CultureInfo.InvariantCulture),
                "): ", message.Message.TextValue));
        }

        if (kind == SystemMessageKind && statusUtc == default)
        {
            statusUtc = DateTime.UtcNow;
        }
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
            statusUtc = default;
            return;
        }

        if (!InWindow(DateTime.UtcNow))
        {
            statusUtc = default;
        }

        CountGardenItems();
        var nowUtc = DateTime.UtcNow;
        if (hasBaseline)
        {
            signals.Clear();
            GardenDelta.Classify(before, after, catalog, nowUtc, signals);
            if (signals.Count > 0 && InWindow(nowUtc))
            {
                changedSinceInteraction = true;
                for (var index = 0; index < signals.Count; index++)
                {
                    Report(signals[index]);
                }
            }
        }

        if (statusUtc != default && nowUtc - statusUtc >= StatusSettle)
        {
            if (!changedSinceInteraction)
            {
                Report(new GardenSignal(GardenActionKind.Tend, -1, statusUtc, true));
            }

            statusUtc = default;
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
        var fishmeal = catalog.FishmealItem;
        for (var index = 0; index < stacks.Count; index++)
        {
            var stack = stacks[index];
            if (stack.ItemId != fishmeal && !catalog.TryIndexItem(stack.ItemId, out _) &&
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
            AepLog.Info(string.Concat(LogPrefix, "noticed ", signal.Kind.ToString(),
                signal.Guessed ? " (guess)" : string.Empty, seed));
        }

        if (!Tracking || signal.Kind == GardenActionKind.Fertilize)
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
        chat.ChatMessage -= OnChatMessage;
        lifecycle.UnregisterListener(AddonEvent.PostSetup, OnAddonSetup);
    }
}
