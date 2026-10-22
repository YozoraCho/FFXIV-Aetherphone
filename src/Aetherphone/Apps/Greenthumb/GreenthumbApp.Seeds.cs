using Aetherphone.Core;
using Aetherphone.Core.Apps;
using Aetherphone.Core.Gardening;
using Aetherphone.Core.Localization;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Apps.Greenthumb;

internal sealed partial class GreenthumbApp
{
    private const int SearchMaxLength = 48;
    private const int FilterOwned = 0;
    private const int FilterCrossOnly = 1;
    private const int FilterGatherable = 2;
    private const float SearchGapUnits = 10f;

    private readonly ChipRail filterRail = new();
    private readonly string[] filterLabels = new string[3];
    private readonly bool[] filterActive = new bool[3];
    private string seedSearch = string.Empty;
    private string pickerSearch = string.Empty;
    private string builtSearch = string.Empty;
    private string builtPickerSearch = string.Empty;
    private bool pickerDirty = true;
    private int[] seedRows = Array.Empty<int>();
    private int[] pickerOwnedRows = Array.Empty<int>();
    private int[] pickerAllRows = Array.Empty<int>();
    private string[] seedRowIds = Array.Empty<string>();
    private bool seedListDirty = true;

    private void DrawSeeds(Rect body)
    {
        EnsureSeedRowIds();
        DrawSearchField(ref seedSearch, "##greenthumbSeeds");
        filterLabels[FilterOwned] = Loc.T(L.Greenthumb.FilterOwned);
        filterLabels[FilterCrossOnly] = Loc.T(L.Greenthumb.CrossOnly);
        filterLabels[FilterGatherable] = Loc.T(L.Greenthumb.FilterGatherable);
        var tapped = filterRail.Draw(ui, filterLabels, filterActive, "greenthumb.seeds.filters");
        if (tapped >= 0)
        {
            filterActive[tapped] = !filterActive[tapped];
            seedListDirty = true;
        }

        Gap(Metrics.Space.Sm);
        RebuildSeedRows(seedSearch);
        RefreshOwnedLabel();
        if (seedRows.Length == 0)
        {
            SettingsSection.Hint(Loc.T(L.Greenthumb.NoMatch), theme);
            return;
        }

        SettingsSection.Header(ownedLabel, theme);
        var card = GroupCard.Begin(theme, seedRows.Length);
        for (var index = 0; index < seedRows.Length; index++)
        {
            var row = card.NextRow();
            if (!ImGui.IsRectVisible(row.Min, row.Max))
            {
                continue;
            }

            var seed = seedRows[index];
            var value = SettingsRow.Bool(row, catalog.Name(seed), owned[seed], theme, seedRowIds[seed]);
            if (value != owned[seed])
            {
                SetOwned(seed, value);
            }
        }

        card.End();
    }

    private void DrawSeedPicker(in PhoneContext context, GreenthumbRoute route)
    {
        var navBar = AppHeader.BeginLargeTitle(context);
        string backTitle;
        GardenBed? bed = null;
        if (route.Purpose == SeedPickPurpose.Bed)
        {
            var patch = FindPatch(route.PatchId);
            if (patch is null || route.Bed < 0 || route.Bed >= patch.Beds.Count)
            {
                router.Pop(false);
                return;
            }

            bed = patch.Beds[route.Bed];
            backTitle = bedTitle;
        }
        else
        {
            backTitle = Loc.T(L.Greenthumb.PlannerTab);
        }

        using (AppSurface.Begin(navBar.Body))
        {
            if (catalog.State == GardenCatalogState.Ready)
            {
                EnsureSeedRowIds();
                DrawSearchField(ref pickerSearch, "##greenthumbPicker");
                RebuildPickerRows(pickerSearch);
                var picked = DrawPickerSection(Loc.T(L.Greenthumb.MySeeds), pickerOwnedRows, bed);
                if (picked < 0)
                {
                    picked = DrawPickerSection(Loc.T(L.Greenthumb.AllSeeds), pickerAllRows, bed);
                }

                if (picked >= 0)
                {
                    ApplyPick(picked, bed);
                }
            }

            Gap(BottomBreathing);
        }

        AppHeader.EndLargeTitle(in navBar, context, "greenthumb.pick.nav", Loc.T(L.Greenthumb.ChooseSeed),
            NavBarStyle.From(ui), ReadOnlySpan<NavBarButton>.Empty, backTitle, back);
    }

    private int DrawPickerSection(string title, int[] rows, GardenBed? bed)
    {
        if (rows.Length == 0)
        {
            return -1;
        }

        SettingsSection.Header(title, theme);
        var picked = -1;
        var card = GroupCard.Begin(theme, rows.Length);
        for (var index = 0; index < rows.Length; index++)
        {
            var row = card.NextRow();
            if (!ImGui.IsRectVisible(row.Min, row.Max))
            {
                continue;
            }

            var seed = rows[index];
            var selected = bed is not null
                ? bed.IsPlanted && string.Equals(bed.Seed, catalog.Book.Seeds[seed].Key, StringComparison.Ordinal)
                : seed == target;
            if (SettingsRow.Selectable(row, catalog.Name(seed), selected, theme, seedRowIds[seed]))
            {
                picked = seed;
            }
        }

        card.End();
        Gap(Metrics.Space.Lg);
        return picked;
    }

    private void ApplyPick(int seed, GardenBed? bed)
    {
        pickerSearch = string.Empty;
        pickerDirty = true;
        if (bed is null)
        {
            target = seed;
            plannerDirty = true;
            router.Pop();
            return;
        }

        var entry = catalog.Book.Seeds[seed];
        if (!bed.IsPlanted || !string.Equals(bed.Seed, entry.Key, StringComparison.Ordinal))
        {
            bed.Plant(entry.Key, entry.GrowHours, DateTime.UtcNow);
            SaveGarden();
        }

        gardenVersion++;
        router.Pop();
    }

    private void DrawSearchField(ref string text, string imguiId)
    {
        var scale = UiScale.Current;
        var origin = ImGui.GetCursorScreenPos();
        var width = ScrollLayout.StableContentWidth();
        var field = new Rect(origin, origin + new Vector2(width, GlassField.HeightUnits * scale));
        var ink = ControlInk.From(theme);
        SearchBar.Surface(ImGui.GetWindowDrawList(), field, ink);
        GlassField.Search(ImGui.GetWindowDrawList(), field, imguiId, Loc.T(L.Greenthumb.SearchSeeds), ref text,
            theme, scale, SearchMaxLength, false);
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, (GlassField.HeightUnits + SearchGapUnits) * scale));
    }

    private void EnsureSeedRowIds()
    {
        if (seedRowIds.Length == catalog.Count)
        {
            return;
        }

        seedRowIds = new string[catalog.Count];
        for (var index = 0; index < seedRowIds.Length; index++)
        {
            seedRowIds[index] = string.Concat("greenthumb.seed.", catalog.Book.Seeds[index].Key);
        }
    }

    private void RebuildSeedRows(string search)
    {
        if (!seedListDirty && string.Equals(search, builtSearch, StringComparison.Ordinal))
        {
            return;
        }

        seedListDirty = false;
        builtSearch = search;
        var folded = search.Trim().ToLowerInvariant();
        var rows = new List<int>(catalog.Count);
        var byName = catalog.SortedByName;
        for (var nameIndex = 0; nameIndex < byName.Length; nameIndex++)
        {
            var seed = byName[nameIndex];
            if (Matches(seed, folded) && PassesFilters(seed))
            {
                rows.Add(seed);
            }
        }

        seedRows = rows.ToArray();
    }

    private void RebuildPickerRows(string search)
    {
        if (!pickerDirty && string.Equals(search, builtPickerSearch, StringComparison.Ordinal))
        {
            return;
        }

        pickerDirty = false;
        builtPickerSearch = search;
        var folded = search.Trim().ToLowerInvariant();
        var ownedRows = new List<int>();
        var allRows = new List<int>(catalog.Count);
        var byName = catalog.SortedByName;
        for (var nameIndex = 0; nameIndex < byName.Length; nameIndex++)
        {
            var seed = byName[nameIndex];
            if (!Matches(seed, folded))
            {
                continue;
            }

            if (owned[seed])
            {
                ownedRows.Add(seed);
            }
            else
            {
                allRows.Add(seed);
            }
        }

        pickerOwnedRows = ownedRows.ToArray();
        pickerAllRows = allRows.ToArray();
    }

    private bool Matches(int seed, string folded) =>
        folded.Length == 0 || catalog.SearchKey(seed).Contains(folded, StringComparison.Ordinal);

    private bool PassesFilters(int seed)
    {
        var entry = catalog.Book.Seeds[seed];
        if (filterActive[FilterOwned] && !owned[seed])
        {
            return false;
        }

        if (filterActive[FilterCrossOnly] && (entry.CanGather || !entry.CanCross))
        {
            return false;
        }

        return !filterActive[FilterGatherable] || entry.CanGather;
    }
}
