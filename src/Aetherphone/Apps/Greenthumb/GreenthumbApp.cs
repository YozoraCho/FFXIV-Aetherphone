using Aetherphone.Core;
using Aetherphone.Core.Apps;
using Aetherphone.Core.Confirm;
using Aetherphone.Core.Gardening;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Theme;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace Aetherphone.Apps.Greenthumb;

internal sealed partial class GreenthumbApp : IPhoneApp
{
    private const int TabCount = 3;
    private const long RefreshMilliseconds = 1000;
    private const float BottomBreathing = 24f;
    private const float SkeletonRowUnits = 44f;
    private const float SkeletonGapUnits = 8f;
    private const float SkeletonAreaUnits = 300f;

    public string Id => GardenCatalog.AppId;
    public string DisplayName => Loc.T(L.Apps.Greenthumb);
    public string Glyph => "G";
    public Vector4 Accent => AppAccents.For(GardenCatalog.AppId);
    public int BadgeCount => alerts.AttentionCount;
    public bool HasBadge => true;
    public bool WantsSystemTheme => true;

    private readonly Configuration configuration;
    private readonly GardenCatalog catalog;
    private readonly GardenAlerts alerts;
    private readonly ConfirmService confirm;
    private readonly AppSkin ui = new(AppPalettes.Greenthumb(PhoneTheme.Default));
    private readonly ViewRouter<GreenthumbRoute> router;
    private readonly RouterDraw<GreenthumbRoute> drawView;
    private readonly Action back;
    private readonly Action<Rect> drawPlannerBody;
    private readonly Action<Rect> drawSeedsBody;
    private readonly TabBar tabBar = new();
    private readonly TabItem[] tabItems = new TabItem[TabCount];
    private PhoneTheme theme = PhoneTheme.Default;
    private INavigator navigation = null!;
    private GreenthumbTab activeTab;
    private bool[] owned = Array.Empty<bool>();
    private int ownedCount;
    private long nextRefresh;

    public GreenthumbApp(Configuration configuration, GardenCatalog catalog, GardenAlerts alerts,
        ConfirmService confirm)
    {
        this.configuration = configuration;
        this.catalog = catalog;
        this.alerts = alerts;
        this.confirm = confirm;
        router = new ViewRouter<GreenthumbRoute>(GreenthumbRoute.Root);
        drawView = DrawView;
        back = () => router.Pop();
        drawPlannerBody = DrawPlanner;
        drawSeedsBody = DrawSeeds;
        decrementDays = () => AdjustGrowHours(-HoursPerDay);
        incrementDays = () => AdjustGrowHours(HoursPerDay);
        decrementHours = () => AdjustGrowHours(-1);
        incrementHours = () => AdjustGrowHours(1);
        decrementDepth = () => AdjustDepth(-1);
        incrementDepth = () => AdjustDepth(1);
    }

    public void OnOpened()
    {
        router.Reset();
        catalog.EnsureLoaded();
        alerts.Refresh();
        MarkDirty();
    }

    public void OnClosed()
    {
        kindSheet.Close();
    }

    public void Draw(in PhoneContext context)
    {
        theme = context.Theme;
        navigation = context.Navigation;
        ui.Theme = theme;
        ui.Palette = AppPalettes.Greenthumb(theme);
        var scale = UiScale.Current;
        var screen = SceneChrome.ScreenFrom(context.Content, theme, scale);
        ui.Backdrop(screen);
        catalog.EnsureLoaded();
        SyncOwned();
        TickRefresh();
        kindSheet.Gate();
        router.Draw(context.Content, AppSkin.Transparent, ImGui.GetIO().DeltaTime, drawView);
        DrawKindSheet(screen);
    }

    private void DrawView(GreenthumbRoute route, Rect area, int depth)
    {
        ui.Body(area);
        var context = new PhoneContext(area, theme, navigation);
        switch (route.Screen)
        {
            case GreenthumbScreen.Patch:
                DrawPatch(context, route);
                return;
            case GreenthumbScreen.Bed:
                DrawBed(context, route);
                return;
            case GreenthumbScreen.PickSeed:
                DrawSeedPicker(context, route);
                return;
            default:
                DrawRoot(context);
                return;
        }
    }

    private void DrawRoot(in PhoneContext context)
    {
        var scale = UiScale.Current;
        using (TabBar.ReserveContent(scale))
        {
            var navBar = AppHeader.BeginLargeTitle(context, false);
            string title;
            using (AppSurface.Begin(navBar.Body))
            {
                switch (activeTab)
                {
                    case GreenthumbTab.Planner:
                        title = Loc.T(L.Greenthumb.PlannerTab);
                        DrawCatalogGated(navBar.Body, drawPlannerBody);
                        break;
                    case GreenthumbTab.Seeds:
                        title = Loc.T(L.Greenthumb.SeedsTab);
                        DrawCatalogGated(navBar.Body, drawSeedsBody);
                        break;
                    default:
                        title = Loc.T(L.Greenthumb.GardenTab);
                        DrawGarden(navBar.Body);
                        break;
                }

                ImGui.Dummy(new Vector2(0f, BottomBreathing * scale));
            }

            AppHeader.EndLargeTitle(in navBar, context, "greenthumb.nav", title, NavBarStyle.From(ui),
                ReadOnlySpan<NavBarButton>.Empty);
        }

        DrawTabBar(context.Content);
    }

    private void DrawCatalogGated(Rect body, Action<Rect> draw)
    {
        var state = catalog.State;
        if (state == GardenCatalogState.Failed)
        {
            if (EmptyState.Draw(body, ui, FontAwesomeIcon.ExclamationTriangle, Loc.T(L.Greenthumb.LoadFailedTitle),
                    Loc.T(L.Greenthumb.LoadFailedHint), Loc.T(L.Common.Retry)))
            {
                catalog.Retry();
            }

            return;
        }

        if (state != GardenCatalogState.Ready)
        {
            var scale = UiScale.Current;
            var origin = ImGui.GetCursorScreenPos();
            var width = ImGui.GetContentRegionAvail().X;
            var area = new Rect(origin, origin + new Vector2(width, SkeletonAreaUnits * scale));
            Skeleton.Rows(ImGui.GetWindowDrawList(), area, SkeletonRowUnits, SkeletonGapUnits, scale);
            ImGui.Dummy(area.Size);
            return;
        }

        draw(body);
    }

    private void DrawTabBar(Rect area)
    {
        tabItems[(int)GreenthumbTab.Garden] = new TabItem(Loc.T(L.Greenthumb.GardenTab),
            IconGlyph.Of(FontAwesomeIcon.Leaf), AnchorKey: "greenthumb.tab.garden");
        tabItems[(int)GreenthumbTab.Planner] = new TabItem(Loc.T(L.Greenthumb.PlannerTab),
            IconGlyph.Of(FontAwesomeIcon.Flask), AnchorKey: "greenthumb.tab.planner");
        tabItems[(int)GreenthumbTab.Seeds] = new TabItem(Loc.T(L.Greenthumb.SeedsTab), PhoneIcons.LayoutList,
            AnchorKey: "greenthumb.tab.seeds");
        var result = tabBar.Draw(area, ui, tabItems, (int)activeTab);
        if (result.Tapped < 0 || result.Tapped == (int)activeTab)
        {
            return;
        }

        activeTab = (GreenthumbTab)result.Tapped;
        MarkDirty();
    }

    private void SyncOwned()
    {
        if (catalog.State != GardenCatalogState.Ready || owned.Length == catalog.Count)
        {
            return;
        }

        owned = new bool[catalog.Count];
        ownedCount = 0;
        var keys = configuration.GreenthumbOwnedSeeds;
        for (var keyIndex = 0; keyIndex < keys.Count; keyIndex++)
        {
            if (catalog.TryIndex(keys[keyIndex], out var index) && !owned[index])
            {
                owned[index] = true;
                ownedCount++;
            }
        }

        MarkDirty();
    }

    private void SetOwned(int index, bool value)
    {
        if (owned[index] == value)
        {
            return;
        }

        owned[index] = value;
        var key = catalog.Book.Seeds[index].Key;
        if (value)
        {
            configuration.GreenthumbOwnedSeeds.Add(key);
            ownedCount++;
        }
        else
        {
            configuration.GreenthumbOwnedSeeds.Remove(key);
            ownedCount--;
        }

        configuration.Save();
        plannerDirty = true;
        pickerDirty = true;
        ownedLabelCount = -1;
    }

    private void TickRefresh()
    {
        var now = Environment.TickCount64;
        if (now < nextRefresh)
        {
            return;
        }

        nextRefresh = now + RefreshMilliseconds;
        gardenVersion++;
    }

    private void MarkDirty()
    {
        gardenVersion++;
        plannerDirty = true;
        seedListDirty = true;
        pickerDirty = true;
        nextRefresh = 0;
    }

    private void SaveGarden()
    {
        configuration.Save();
        alerts.Refresh();
        gardenVersion++;
    }

    private GardenPatch? FindPatch(Guid id)
    {
        var patches = configuration.GreenthumbPatches;
        for (var index = 0; index < patches.Count; index++)
        {
            if (patches[index].Id == id)
            {
                return patches[index];
            }
        }

        return null;
    }

    private static void Gap(float units) => ImGui.Dummy(new Vector2(0f, units * UiScale.Current));

    private void DrawTwoLineRow(Rect row, string title, string subtitle, Vector4 titleInk)
    {
        var drawList = ImGui.GetWindowDrawList();
        var titleHeight = Typography.LineHeight(TextStyles.BodyEmphasized);
        var subtitleHeight = Typography.LineHeight(TextStyles.Footnote);
        var top = row.Center.Y - (titleHeight + subtitleHeight) * 0.5f;
        var width = row.Width;
        Typography.Draw(drawList, new Vector2(row.Min.X, top),
            Typography.FitText(title, width, TextStyles.BodyEmphasized), titleInk, TextStyles.BodyEmphasized);
        if (subtitle.Length == 0)
        {
            return;
        }

        Typography.Draw(drawList, new Vector2(row.Min.X, top + titleHeight),
            Typography.FitText(subtitle, width, TextStyles.Footnote), theme.TextMuted, TextStyles.Footnote);
    }

    public void Dispose()
    {
    }
}
