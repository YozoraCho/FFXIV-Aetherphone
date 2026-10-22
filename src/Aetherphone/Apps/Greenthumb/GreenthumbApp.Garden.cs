using System.Globalization;
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

internal sealed partial class GreenthumbApp
{
    private const int HoursPerDay = 24;
    private const int MaxGrowHours = 30 * HoursPerDay;
    private const float CellGapUnits = 8f;
    private const float CellMaxUnits = 112f;
    private const float CellPaddingUnits = 8f;
    private const float CellRadiusUnits = 14f;
    private const float StepperHeightUnits = 32f;
    private const int MaxPatchNameLength = 32;
    private const int MaxCrossResults = 16;

    private static readonly GardenPatchKind[] KindOrder =
    {
        GardenPatchKind.Round, GardenPatchKind.Oblong, GardenPatchKind.Deluxe, GardenPatchKind.Flowerpot,
    };

    private readonly ActionSheet kindSheet = new();
    private readonly ActionSheet.Item[] kindItems = new ActionSheet.Item[4];
    private readonly ActionSheet harvestSheet = new();
    private readonly int[] crossResults = new int[MaxCrossResults];
    private ActionSheet.Item[] harvestItems = Array.Empty<ActionSheet.Item>();
    private int[] harvestSeeds = Array.Empty<int>();
    private int crossResultCount;
    private string patchNameDraft = string.Empty;
    private Guid patchNameId;
    private bool patchNameDirty;
    private readonly Action decrementDays;
    private readonly Action incrementDays;
    private readonly Action decrementHours;
    private readonly Action incrementHours;
    private readonly int[] neighborBuffer = new int[GardenLayout.MaxNeighbors];
    private string[] patchStatus = Array.Empty<string>();
    private string[] patchRowIds = Array.Empty<string>();
    private string[] bedNumbers = Array.Empty<string>();
    private string[] bedNames = Array.Empty<string>();
    private string[] bedStatus = Array.Empty<string>();
    private BedState[] bedStates = Array.Empty<BedState>();
    private string[] crossLabels = Array.Empty<string>();
    private string[] crossValues = Array.Empty<string>();
    private int crossCount;
    private int gardenVersion = 1;
    private int listBuiltVersion;
    private int patchBuiltVersion;
    private Guid patchBuiltId;
    private int bedBuiltVersion;
    private Guid bedBuiltPatch;
    private int bedBuiltIndex = -1;
    private string bedTitle = string.Empty;
    private string bedSeedName = string.Empty;
    private string bedPlanted = string.Empty;
    private string bedTended = string.Empty;
    private string bedWilts = string.Empty;
    private string bedReady = string.Empty;
    private string growDaysText = string.Empty;
    private string growHoursText = string.Empty;
    private GardenBed? editingBed;

    private enum BedState : byte
    {
        Empty,
        Growing,
        TendSoon,
        Wilting,
        Ready,
    }

    private void DrawGarden(Rect body)
    {
        var patches = configuration.GreenthumbPatches;
        if (patches.Count == 0)
        {
            if (EmptyState.Draw(body, ui, FontAwesomeIcon.Leaf, Loc.T(L.Greenthumb.NoPatchesTitle),
                    Loc.T(L.Greenthumb.NoPatchesHint), Loc.T(L.Greenthumb.AddPatch)))
            {
                OpenKindSheet();
            }

            return;
        }

        RebuildGardenText();
        var card = GroupCard.Begin(theme, patches.Count);
        for (var index = 0; index < patches.Count; index++)
        {
            var row = card.NextRow();
            if (SettingsRow.Disclosure(row, PatchLabel.For(patches[index]), patchStatus[index], theme,
                    patchRowIds[index]))
            {
                router.Push(GreenthumbRoute.Patch(patches[index].Id));
            }
        }

        card.End();
        Gap(Metrics.Space.Lg);
        var addCard = GroupCard.Begin(theme, 1);
        if (SettingsRow.Action(addCard.NextRow(), Loc.T(L.Greenthumb.AddPatch), theme.Accent, theme))
        {
            OpenKindSheet();
        }

        addCard.End();
    }

    private void RebuildGardenText()
    {
        var patches = configuration.GreenthumbPatches;
        if (listBuiltVersion == gardenVersion && patchStatus.Length == patches.Count)
        {
            return;
        }

        if (patchStatus.Length != patches.Count)
        {
            patchStatus = new string[patches.Count];
            patchRowIds = new string[patches.Count];
        }

        var nowUtc = DateTime.UtcNow;
        for (var index = 0; index < patches.Count; index++)
        {
            var patch = patches[index];
            patchRowIds[index] = string.Concat("greenthumb.patch.", patch.Id.ToString("N"));
            var planted = 0;
            var attention = 0;
            for (var bedIndex = 0; bedIndex < patch.Beds.Count; bedIndex++)
            {
                var state = StateOf(patch.Beds[bedIndex], nowUtc);
                if (state != BedState.Empty)
                {
                    planted++;
                }

                if (state is BedState.Ready or BedState.TendSoon or BedState.Wilting)
                {
                    attention++;
                }
            }

            patchStatus[index] = attention > 0
                ? Loc.T(L.Greenthumb.NeedsAttention, attention)
                : Loc.T(L.Greenthumb.PlantedOf, planted, patch.Beds.Count);
        }

        listBuiltVersion = gardenVersion;
    }

    private BedState StateOf(GardenBed bed, DateTime nowUtc)
    {
        if (!bed.IsPlanted)
        {
            return BedState.Empty;
        }

        if (GardenTimers.IsReady(bed, nowUtc))
        {
            return BedState.Ready;
        }

        var wiltHours = catalog.WiltHours(bed.Seed);
        if (GardenTimers.WiltsAtUtc(bed, wiltHours, nowUtc) is { } wilts && nowUtc >= wilts)
        {
            return BedState.Wilting;
        }

        return GardenTimers.NeedsTending(bed, wiltHours, nowUtc) ? BedState.TendSoon : BedState.Growing;
    }

    private string StateLabel(BedState state) => state switch
    {
        BedState.Empty => Loc.T(L.Greenthumb.EmptyBed),
        BedState.Ready => Loc.T(L.Greenthumb.Ready),
        BedState.TendSoon => Loc.T(L.Greenthumb.TendSoon),
        BedState.Wilting => Loc.T(L.Greenthumb.Wilting),
        _ => Loc.T(L.Greenthumb.Growing),
    };

    private Vector4 StateInk(BedState state) => state switch
    {
        BedState.Ready => ui.Palette.Accent,
        BedState.TendSoon or BedState.Wilting => theme.Danger,
        _ => theme.TextMuted,
    };

    private void OpenKindSheet()
    {
        for (var index = 0; index < KindOrder.Length; index++)
        {
            kindItems[index] = new ActionSheet.Item(Loc.T(PatchLabel.KindName(KindOrder[index])));
        }

        kindSheet.Open();
    }

    private void DrawKindSheet(Rect screen)
    {
        if (!kindSheet.CapturesPointer)
        {
            return;
        }

        var picked = kindSheet.Draw(screen, ActionSheetStyle.From(theme), kindItems, Loc.T(L.Common.Cancel), false,
            Loc.T(L.Greenthumb.AddPatch));
        if (picked < 0 || picked >= KindOrder.Length)
        {
            return;
        }

        var patch = GardenPatch.Create(KindOrder[picked], string.Empty);
        configuration.GreenthumbPatches.Add(patch);
        SaveGarden();
        if (router.Depth == 1)
        {
            router.Push(GreenthumbRoute.Patch(patch.Id));
        }
    }

    private void DrawPatch(in PhoneContext context, GreenthumbRoute route)
    {
        var patch = FindPatch(route.PatchId);
        if (patch is null)
        {
            router.Pop(false);
            return;
        }

        patch.EnsureBeds();
        RebuildPatchText(patch);
        var navBar = AppHeader.BeginLargeTitle(context);
        using (AppSurface.Begin(navBar.Body))
        {
            DrawBedGrid(patch);
            Gap(Metrics.Space.Xl);
            DrawPatchName(patch);
            Gap(Metrics.Space.Lg);
            var hasPlanted = HasPlanted(patch);
            var card = GroupCard.Begin(theme, hasPlanted ? 2 : 1);
            if (hasPlanted && SettingsRow.Action(card.NextRow(), Loc.T(L.Greenthumb.TendAll), theme.Accent, theme))
            {
                TendAll(patch);
            }

            if (SettingsRow.Action(card.NextRow(), Loc.T(L.Greenthumb.RemovePatch), theme.Danger, theme))
            {
                AskRemovePatch(patch);
            }

            card.End();
            Gap(BottomBreathing);
        }

        AppHeader.EndLargeTitle(in navBar, context, "greenthumb.patch.nav", PatchLabel.For(patch),
            NavBarStyle.From(ui), ReadOnlySpan<NavBarButton>.Empty, Loc.T(L.Greenthumb.GardenTab), back);
    }

    private void DrawPatchName(GardenPatch patch)
    {
        if (patchNameId != patch.Id)
        {
            patchNameId = patch.Id;
            patchNameDraft = patch.Name;
        }

        SettingsSection.Header(Loc.T(L.Greenthumb.PatchName), theme);
        var scale = UiScale.Current;
        var origin = ImGui.GetCursorScreenPos();
        var width = ScrollLayout.StableContentWidth();
        var field = new Rect(origin, origin + new Vector2(width, GlassField.HeightUnits * scale));
        SearchBar.Surface(ImGui.GetWindowDrawList(), field, ControlInk.From(theme));
        if (GlassField.Text(field, "##greenthumbPatchName", Loc.T(PatchLabel.KindName(patch.Kind)),
                ref patchNameDraft, theme, scale, MaxPatchNameLength, false, ImGuiInputTextFlags.None))
        {
            patch.Name = patchNameDraft.Trim();
            patchNameDirty = true;
            gardenVersion++;
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, field.Height));
    }

    private void SavePatchNameWhenIdle()
    {
        if (!patchNameDirty || ImGui.IsAnyItemActive())
        {
            return;
        }

        patchNameDirty = false;
        SaveGarden();
    }

    private static bool HasPlanted(GardenPatch patch)
    {
        for (var index = 0; index < patch.Beds.Count; index++)
        {
            if (patch.Beds[index].IsPlanted)
            {
                return true;
            }
        }

        return false;
    }

    private void RebuildPatchText(GardenPatch patch)
    {
        if (patchBuiltVersion == gardenVersion && patchBuiltId == patch.Id && bedNames.Length == patch.Beds.Count)
        {
            return;
        }

        var count = patch.Beds.Count;
        if (bedNames.Length != count)
        {
            bedNumbers = new string[count];
            bedNames = new string[count];
            bedStatus = new string[count];
            bedStates = new BedState[count];
        }

        var nowUtc = DateTime.UtcNow;
        for (var index = 0; index < count; index++)
        {
            var bed = patch.Beds[index];
            var state = StateOf(bed, nowUtc);
            bedNumbers[index] = (index + 1).ToString(CultureInfo.CurrentCulture);
            bedNames[index] = bed.IsPlanted ? catalog.NameOf(bed.Seed) : string.Empty;
            bedStates[index] = state;
            bedStatus[index] = StateLabel(state);
        }

        patchBuiltId = patch.Id;
        patchBuiltVersion = gardenVersion;
    }

    private void DrawBedGrid(GardenPatch patch)
    {
        var scale = UiScale.Current;
        var kind = patch.Kind;
        var columns = GardenLayout.Columns(kind);
        var rows = GardenLayout.Rows(kind);
        var width = ScrollLayout.StableContentWidth();
        var gap = CellGapUnits * scale;
        var cell = MathF.Min((width - gap * (columns - 1)) / columns, CellMaxUnits * scale);
        var gridWidth = cell * columns + gap * (columns - 1);
        var origin = ImGui.GetCursorScreenPos();
        origin.X += (width - gridWidth) * 0.5f;
        var drawList = ImGui.GetWindowDrawList();
        var padding = CellPaddingUnits * scale;
        var radius = CellRadiusUnits * scale;
        for (var cellIndex = 0; cellIndex < columns * rows; cellIndex++)
        {
            var bed = GardenLayout.BedAtCell(kind, cellIndex);
            if (bed < 0)
            {
                continue;
            }

            var min = origin + new Vector2((cellIndex % columns) * (cell + gap), (cellIndex / columns) * (cell + gap));
            var max = min + new Vector2(cell, cell);
            var hovered = UiInteract.Hover(min, max);
            var fill = hovered ? Palette.Mix(theme.GroupedCard, theme.TextStrong, 0.06f) : theme.GroupedCard;
            Squircle.Fill(drawList, min, max, radius, ImGui.GetColorU32(fill));
            var state = bedStates[bed];
            if (state != BedState.Empty && state != BedState.Growing)
            {
                Squircle.Stroke(drawList, min, max, radius, ImGui.GetColorU32(StateInk(state)),
                    Metrics.Stroke.Hairline * 2f);
            }

            var inner = max.X - min.X - padding * 2f;
            Typography.Draw(drawList, min + new Vector2(padding, padding), bedNumbers[bed], theme.TextMuted,
                TextStyles.Footnote);
            var statusHeight = Typography.LineHeight(TextStyles.Footnote);
            var statusTop = max.Y - padding - statusHeight;
            if (bedNames[bed].Length > 0)
            {
                var numberBottom = min.Y + padding + Typography.LineHeight(TextStyles.Footnote);
                DrawBedName(drawList, bedNames[bed], new Vector2(min.X + padding, numberBottom), statusTop, inner);
            }

            Typography.Draw(drawList, new Vector2(min.X + padding, statusTop),
                Typography.FitText(bedStatus[bed], inner, TextStyles.Footnote), StateInk(state), TextStyles.Footnote);
            if (hovered)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            }

            if (UiInteract.Click(min, max, hovered))
            {
                router.Push(GreenthumbRoute.BedOf(patch.Id, bed));
                gardenVersion++;
            }
        }

        ImGui.Dummy(new Vector2(width, cell * rows + gap * (rows - 1)));
    }

    private void DrawBedName(ImDrawListPtr drawList, string name, Vector2 topLeft, float bottom, float width)
    {
        var style = TextStyles.SubheadlineEmphasized;
        var lineHeight = Typography.LineHeight(style);
        var maxLines = Math.Max(1, (int)((bottom - topLeft.Y) / lineHeight));
        var lines = Typography.WrapText(name, style, width);
        var count = Math.Min(lines.Length, maxLines);
        var top = bottom - count * lineHeight;
        for (var lineIndex = 0; lineIndex < count; lineIndex++)
        {
            var line = lines[lineIndex];
            if (lineIndex == count - 1 && count < lines.Length)
            {
                line = Typography.FitText(string.Concat(line, " ", lines[lineIndex + 1]), width, style);
            }

            Typography.Draw(drawList, new Vector2(topLeft.X, top + lineIndex * lineHeight), line, theme.TextStrong,
                style);
        }
    }

    private void TendAll(GardenPatch patch)
    {
        var nowUtc = DateTime.UtcNow;
        for (var index = 0; index < patch.Beds.Count; index++)
        {
            if (patch.Beds[index].IsPlanted)
            {
                patch.Beds[index].Tend(nowUtc);
            }
        }

        SaveGarden();
    }

    private void AskRemovePatch(GardenPatch patch)
    {
        var id = patch.Id;
        confirm.Ask(new ConfirmRequest
        {
            Message = Loc.T(L.Greenthumb.RemovePatchConfirm),
            ConfirmLabel = Loc.T(L.Greenthumb.Remove),
            CancelLabel = Loc.T(L.Greenthumb.Keep),
            Sheet = true,
            Confirm = () => RemovePatch(id),
        });
    }

    private void RemovePatch(Guid id)
    {
        var patches = configuration.GreenthumbPatches;
        for (var index = 0; index < patches.Count; index++)
        {
            if (patches[index].Id != id)
            {
                continue;
            }

            patches.RemoveAt(index);
            SaveGarden();
            router.Reset();
            return;
        }
    }

    private void DrawBed(in PhoneContext context, GreenthumbRoute route)
    {
        var patch = FindPatch(route.PatchId);
        if (patch is null || route.Bed < 0 || route.Bed >= patch.Beds.Count)
        {
            router.Pop(false);
            return;
        }

        var bed = patch.Beds[route.Bed];
        editingBed = bed;
        RebuildBedText(patch, route.Bed, bed);
        var navBar = AppHeader.BeginLargeTitle(context);
        using (AppSurface.Begin(navBar.Body))
        {
            DrawBedDetails(patch, route.Bed, bed);
            Gap(BottomBreathing);
        }

        AppHeader.EndLargeTitle(in navBar, context, "greenthumb.bed.nav", bedTitle, NavBarStyle.From(ui),
            ReadOnlySpan<NavBarButton>.Empty, PatchLabel.For(patch), back);
    }

    private void DrawBedDetails(GardenPatch patch, int bedIndex, GardenBed bed)
    {
        var planted = bed.IsPlanted;
        var card = GroupCard.Begin(theme, planted ? 5 : 1);
        if (SettingsRow.Disclosure(card.NextRow(), Loc.T(L.Greenthumb.Seed), bedSeedName, theme,
                "greenthumb.bed.seed"))
        {
            router.Push(GreenthumbRoute.PickForBed(patch.Id, bedIndex));
            pickerDirty = true;
        }

        if (planted)
        {
            SettingsRow.Info(card.NextRow(), Loc.T(L.Greenthumb.Planted), bedPlanted, theme, "greenthumb.bed.planted");
            SettingsRow.Info(card.NextRow(), Loc.T(L.Greenthumb.Tended), bedTended, theme, "greenthumb.bed.tended");
            SettingsRow.Info(card.NextRow(), Loc.T(L.Greenthumb.Wilts), bedWilts, theme, "greenthumb.bed.wilts");
            if (SettingsRow.Action(card.NextRow(), Loc.T(L.Greenthumb.MarkTended), theme.Accent, theme))
            {
                bed.Tend(DateTime.UtcNow);
                SaveGarden();
                gardenVersion++;
            }
        }

        card.End();
        if (!planted)
        {
            return;
        }

        Gap(Metrics.Space.Lg);
        SettingsSection.Header(Loc.T(L.Greenthumb.GrowingTime), theme, Loc.T(L.Greenthumb.GrowingTimeHint));
        var growCard = GroupCard.Begin(theme, 3);
        DrawStepperRow(growCard.NextRow(), growDaysText, decrementDays, incrementDays);
        DrawStepperRow(growCard.NextRow(), growHoursText, decrementHours, incrementHours);
        SettingsRow.Info(growCard.NextRow(), Loc.T(L.Greenthumb.Ready), bedReady, theme, "greenthumb.bed.ready");
        growCard.End();

        Gap(Metrics.Space.Lg);
        SettingsSection.Header(Loc.T(L.Greenthumb.CrossSection), theme);
        if (!GardenLayout.CanCross(patch.Kind))
        {
            SettingsSection.Hint(Loc.T(L.Greenthumb.NoCrossPot), theme);
        }
        else if (crossCount == 0)
        {
            SettingsSection.Hint(Loc.T(L.Greenthumb.NoCross), theme);
        }
        else
        {
            var crossCard = GroupCard.Begin(theme, crossCount, 60f);
            for (var index = 0; index < crossCount; index++)
            {
                DrawTwoLineRow(crossCard.NextRow(), crossLabels[index], crossValues[index], theme.TextStrong);
            }

            crossCard.End();
        }

        Gap(Metrics.Space.Lg);
        var ready = GardenTimers.IsReady(bed, DateTime.UtcNow);
        var clearCard = GroupCard.Begin(theme, ready ? 2 : 1);
        if (ready && SettingsRow.Action(clearCard.NextRow(), Loc.T(L.Greenthumb.Harvest), theme.Accent, theme))
        {
            Harvest(bed);
        }

        if (SettingsRow.Action(clearCard.NextRow(), Loc.T(L.Greenthumb.ClearBed), theme.Danger, theme))
        {
            AskClearBed(bed);
        }

        clearCard.End();
    }

    private void DrawStepperRow(Rect row, string valueText, Action decrement, Action increment)
    {
        var scale = UiScale.Current;
        var height = StepperHeightUnits * scale;
        var rect = new Rect(new Vector2(row.Min.X, row.Center.Y - height * 0.5f),
            new Vector2(row.Max.X, row.Center.Y + height * 0.5f));
        StepperField.Draw(theme, rect, valueText, scale, decrement, increment);
    }

    private void AdjustGrowHours(int delta)
    {
        if (editingBed is not { } bed)
        {
            return;
        }

        bed.GrowHours = Math.Clamp(bed.GrowHours + delta, 0, MaxGrowHours);
        bed.ReadyNotified = false;
        SaveGarden();
        gardenVersion++;
    }

    private void Harvest(GardenBed bed)
    {
        var count = 0;
        for (var index = 0; index < crossResultCount; index++)
        {
            if (!owned[crossResults[index]])
            {
                count++;
            }
        }

        harvestSeeds = new int[count];
        harvestItems = new ActionSheet.Item[count];
        count = 0;
        for (var index = 0; index < crossResultCount; index++)
        {
            var seed = crossResults[index];
            if (owned[seed])
            {
                continue;
            }

            harvestSeeds[count] = seed;
            harvestItems[count] = new ActionSheet.Item(catalog.Name(seed));
            count++;
        }

        bed.Clear();
        SaveGarden();
        gardenVersion++;
        if (count > 0)
        {
            harvestSheet.Open();
        }
    }

    private void DrawHarvestSheet(Rect screen)
    {
        if (!harvestSheet.CapturesPointer)
        {
            return;
        }

        var picked = harvestSheet.Draw(screen, ActionSheetStyle.From(theme), harvestItems,
            Loc.T(L.Greenthumb.NoNewSeeds), false, Loc.T(L.Greenthumb.HarvestPrompt));
        if (picked >= 0 && picked < harvestSeeds.Length)
        {
            SetOwned(harvestSeeds[picked], true);
        }
    }

    private void AskClearBed(GardenBed bed)
    {
        confirm.Ask(new ConfirmRequest
        {
            Message = Loc.T(L.Greenthumb.ClearBedConfirm),
            ConfirmLabel = Loc.T(L.Greenthumb.Clear),
            CancelLabel = Loc.T(L.Greenthumb.Keep),
            Sheet = true,
            Confirm = () =>
            {
                bed.Clear();
                SaveGarden();
                gardenVersion++;
            },
        });
    }

    private void RebuildBedText(GardenPatch patch, int bedIndex, GardenBed bed)
    {
        if (bedBuiltVersion == gardenVersion && bedBuiltIndex == bedIndex && bedBuiltPatch == patch.Id)
        {
            return;
        }

        var nowUtc = DateTime.UtcNow;
        bedTitle = Loc.T(L.Greenthumb.BedTitle, bedIndex + 1);
        bedSeedName = bed.IsPlanted ? catalog.NameOf(bed.Seed) : Loc.T(L.Greenthumb.EmptyBed);
        bedPlanted = bed.PlantedAtUtc is { } plantedAt ? TimeText.Ago(plantedAt) : string.Empty;
        bedTended = bed.TendedAtUtc is { } tendedAt ? TimeText.Ago(tendedAt) : string.Empty;
        var wilts = GardenTimers.WiltsAtUtc(bed, catalog.WiltHours(bed.Seed), nowUtc);
        bedWilts = wilts switch
        {
            null => GardenTimers.IsReady(bed, nowUtc) ? Loc.T(L.Greenthumb.Ready) : string.Empty,
            { } moment when nowUtc >= moment => Loc.T(L.Greenthumb.Wilting),
            { } moment => GardenUntil(moment - nowUtc),
        };
        var ready = GardenTimers.ReadyAtUtc(bed);
        bedReady = ready switch
        {
            null => Loc.T(L.Greenthumb.NotSet),
            { } moment when nowUtc >= moment => Loc.T(L.Greenthumb.Ready),
            { } moment => GardenUntil(moment - nowUtc),
        };
        growDaysText = Loc.Plural(L.Greenthumb.Days, bed.GrowHours / HoursPerDay);
        growHoursText = Loc.Plural(L.Greenthumb.Hours, bed.GrowHours % HoursPerDay);
        RebuildCrossText(patch, bedIndex, bed);
        bedBuiltIndex = bedIndex;
        bedBuiltPatch = patch.Id;
        bedBuiltVersion = gardenVersion;
    }

    private static string GardenUntil(TimeSpan span)
    {
        if (span.TotalDays < 1)
        {
            return TimeText.Until(span);
        }

        var days = (int)span.TotalDays;
        return span.Hours > 0 ? Loc.T(L.Timers.InDaysHours, days, span.Hours) : Loc.T(L.Timers.InDays, days);
    }

    private void RebuildCrossText(GardenPatch patch, int bedIndex, GardenBed bed)
    {
        crossCount = 0;
        crossResultCount = 0;
        if (!bed.IsPlanted || !catalog.TryIndex(bed.Seed, out var seed))
        {
            return;
        }

        var neighbors = GardenLayout.Neighbors(patch.Kind, bedIndex, neighborBuffer);
        if (crossLabels.Length < GardenLayout.MaxNeighbors)
        {
            crossLabels = new string[GardenLayout.MaxNeighbors];
            crossValues = new string[GardenLayout.MaxNeighbors];
        }

        for (var index = 0; index < neighbors; index++)
        {
            var neighborBed = patch.Beds[neighborBuffer[index]];
            if (!neighborBed.IsPlanted || !catalog.TryIndex(neighborBed.Seed, out var other))
            {
                continue;
            }

            var results = catalog.Book.CrossResults(seed, other);
            if (results.Length == 0)
            {
                continue;
            }

            var names = new string[results.Length];
            for (var resultIndex = 0; resultIndex < results.Length; resultIndex++)
            {
                names[resultIndex] = catalog.Name(results[resultIndex]);
                AddCrossResult(results[resultIndex]);
            }

            crossLabels[crossCount] = string.Concat(Loc.T(L.Greenthumb.CrossWith, neighborBuffer[index] + 1), ": ",
                catalog.Name(other));
            crossValues[crossCount] = string.Join(", ", names);
            crossCount++;
        }
    }

    private void AddCrossResult(int seed)
    {
        if (crossResultCount >= crossResults.Length)
        {
            return;
        }

        for (var index = 0; index < crossResultCount; index++)
        {
            if (crossResults[index] == seed)
            {
                return;
            }
        }

        crossResults[crossResultCount] = seed;
        crossResultCount++;
    }
}
