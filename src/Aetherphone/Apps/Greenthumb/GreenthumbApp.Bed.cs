using Aetherphone.Core;
using Aetherphone.Core.Apps;
using Aetherphone.Core.Confirm;
using Aetherphone.Core.Gardening;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Theme;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Apps.Greenthumb;

internal sealed partial class GreenthumbApp
{
    private const int MaxGrowHours = 30 * HoursPerDay;
    private const int MaxCrossResults = 16;
    private const float StepperHeightUnits = 32f;
    private const float HeroIconUnits = 56f;
    private const float HeroIconPadUnits = 6f;
    private const float HeroGapUnits = 14f;
    private const float BarHeightUnits = 8f;
    private const float BarGapUnits = 14f;
    private const float CaptionGapUnits = 6f;
    private const float ActionRowUnits = 66f;
    private const float PillHeightUnits = 36f;
    private const float PillPadUnits = 18f;
    private const float HarvestHeightUnits = 48f;
    private const float IconBackdropAlpha = 0.12f;

    private readonly ActionSheet harvestSheet = new();
    private readonly Action decrementDays;
    private readonly Action incrementDays;
    private readonly Action decrementHours;
    private readonly Action incrementHours;
    private readonly int[] neighborBuffer = new int[GardenLayout.MaxNeighbors];
    private readonly int[] crossResults = new int[MaxCrossResults];
    private ActionSheet.Item[] harvestItems = Array.Empty<ActionSheet.Item>();
    private int[] harvestSeeds = Array.Empty<int>();
    private string[] crossLabels = Array.Empty<string>();
    private string[] crossValues = Array.Empty<string>();
    private int crossCount;
    private int crossResultCount;
    private int bedBuiltVersion;
    private Guid bedBuiltPatch;
    private int bedBuiltIndex = -1;
    private BedState bedState;
    private float bedProgress = -1f;
    private uint bedIcon;
    private string bedTitle = string.Empty;
    private string bedSeedName = string.Empty;
    private string bedStateText = string.Empty;
    private string bedPercent = string.Empty;
    private string bedReadyText = string.Empty;
    private string bedTendText = string.Empty;
    private string bedPlanted = string.Empty;
    private string bedTended = string.Empty;
    private string growDaysText = string.Empty;
    private string growHoursText = string.Empty;
    private GardenBed? editingBed;

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
            if (bed.IsPlanted)
            {
                DrawPlantedBed(patch, route.Bed, bed);
            }
            else
            {
                DrawEmptyBed(patch, route.Bed);
            }

            Gap(BottomBreathing);
        }

        AppHeader.EndLargeTitle(in navBar, context, "greenthumb.bed.nav", bedTitle, NavBarStyle.From(ui),
            ReadOnlySpan<NavBarButton>.Empty, PatchLabel.For(patch), back);
    }

    private void DrawEmptyBed(GardenPatch patch, int bedIndex)
    {
        var scale = UiScale.Current;
        var drawList = ImGui.GetWindowDrawList();
        var width = ScrollLayout.StableContentWidth();
        var pad = CardPadUnits * scale;
        var titleHeight = Typography.LineHeight(TextStyles.Title3);
        var lineHeight = Typography.LineHeight(TextStyles.Subheadline);
        var pillHeight = HarvestHeightUnits * scale;
        var height = pad * 3f + titleHeight + lineHeight + pillHeight;
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(width, height);
        GroupCard.DrawSurface(drawList, min, max, theme, scale);
        Typography.Draw(drawList, min + new Vector2(pad, pad), Loc.T(L.Greenthumb.EmptyBed), theme.TextStrong,
            TextStyles.Title3);
        Typography.Draw(drawList, min + new Vector2(pad, pad + titleHeight), Loc.T(L.Greenthumb.NothingPlanted),
            theme.TextMuted, TextStyles.Subheadline);
        var pillTop = min.Y + pad * 2f + titleHeight + lineHeight;
        var pill = new Rect(new Vector2(min.X + pad, pillTop), new Vector2(max.X - pad, pillTop + pillHeight));
        if (ui.AccentPill(pill, Loc.T(L.Greenthumb.PlantSeed), true, TextStyles.Headline))
        {
            router.Push(GreenthumbRoute.PickForBed(patch.Id, bedIndex));
            pickerDirty = true;
        }

        ImGui.Dummy(new Vector2(width, height));
    }

    private void DrawPlantedBed(GardenPatch patch, int bedIndex, GardenBed bed)
    {
        DrawBedHero();
        Gap(Metrics.Space.Md);
        DrawBedAction(bed);
        Gap(Metrics.Space.Lg);
        var card = GroupCard.Begin(theme, 3);
        if (SettingsRow.Disclosure(card.NextRow(), Loc.T(L.Greenthumb.Seed), bedSeedName, theme,
                "greenthumb.bed.seed"))
        {
            router.Push(GreenthumbRoute.PickForBed(patch.Id, bedIndex));
            pickerDirty = true;
        }

        SettingsRow.Info(card.NextRow(), Loc.T(L.Greenthumb.Planted), bedPlanted, theme, "greenthumb.bed.planted");
        SettingsRow.Info(card.NextRow(), Loc.T(L.Greenthumb.Tended), bedTended, theme, "greenthumb.bed.tended");
        card.End();

        Gap(Metrics.Space.Lg);
        SettingsSection.Header(Loc.T(L.Greenthumb.GrowingTime), theme, Loc.T(L.Greenthumb.GrowingTimeHint));
        var growCard = GroupCard.Begin(theme, 2);
        DrawStepperRow(growCard.NextRow(), growDaysText, decrementDays, incrementDays);
        DrawStepperRow(growCard.NextRow(), growHoursText, decrementHours, incrementHours);
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
        var clearCard = GroupCard.Begin(theme, 1);
        if (SettingsRow.Action(clearCard.NextRow(), Loc.T(L.Greenthumb.ClearBed), theme.Danger, theme))
        {
            AskClearBed(bed);
        }

        clearCard.End();
    }

    private void DrawBedHero()
    {
        var scale = UiScale.Current;
        var drawList = ImGui.GetWindowDrawList();
        var width = ScrollLayout.StableContentWidth();
        var pad = CardPadUnits * scale;
        var iconSize = HeroIconUnits * scale;
        var captionHeight = Typography.LineHeight(TextStyles.Footnote);
        var height = pad * 2f + iconSize + BarGapUnits * scale + BarHeightUnits * scale +
                     CaptionGapUnits * scale + captionHeight;
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(width, height);
        GroupCard.DrawSurface(drawList, min, max, theme, scale);
        var iconMin = min + new Vector2(pad, pad);
        var iconMax = iconMin + new Vector2(iconSize, iconSize);
        Squircle.Fill(drawList, iconMin, iconMax, iconSize * IconRadiusFraction,
            ImGui.GetColorU32(Palette.WithAlpha(ui.Palette.Accent, IconBackdropAlpha)));
        DrawSeedIcon(drawList, bedIcon, (iconMin + iconMax) * 0.5f, iconSize - HeroIconPadUnits * 2f * scale);
        var textLeft = iconMax.X + HeroGapUnits * scale;
        var textWidth = max.X - pad - textLeft;
        var titleHeight = Typography.LineHeight(TextStyles.Title3);
        var stateHeight = Typography.LineHeight(TextStyles.SubheadlineEmphasized);
        var textTop = iconMin.Y + (iconSize - titleHeight - stateHeight) * 0.5f;
        Typography.Draw(drawList, new Vector2(textLeft, textTop),
            Typography.FitText(bedSeedName, textWidth, TextStyles.Title3), theme.TextStrong, TextStyles.Title3);
        var stateInk = bedState == BedState.Growing ? ui.Palette.Accent : StateInk(bedState);
        Typography.Draw(drawList, new Vector2(textLeft, textTop + titleHeight),
            Typography.FitText(bedStateText, textWidth, TextStyles.SubheadlineEmphasized), stateInk,
            TextStyles.SubheadlineEmphasized);
        var barTop = iconMax.Y + BarGapUnits * scale;
        var barMin = new Vector2(min.X + pad, barTop);
        var barMax = new Vector2(max.X - pad, barTop + BarHeightUnits * scale);
        var barInk = bedState is BedState.TendSoon or BedState.Wilting ? StateInk(bedState) : ui.Palette.Accent;
        DrawProgressBar(drawList, barMin, barMax, bedState == BedState.Ready ? 1f : bedProgress, barInk);
        var captionTop = barMax.Y + CaptionGapUnits * scale;
        if (bedPercent.Length > 0)
        {
            Typography.Draw(drawList, new Vector2(barMin.X, captionTop), bedPercent, theme.TextMuted,
                TextStyles.Footnote);
        }

        var readyWidth = Typography.Measure(bedReadyText, TextStyles.Footnote).X;
        var readyInk = bedState == BedState.Ready ? ui.Palette.Accent : theme.TextMuted;
        Typography.Draw(drawList, new Vector2(barMax.X - readyWidth, captionTop), bedReadyText, readyInk,
            TextStyles.Footnote);
        ImGui.Dummy(new Vector2(width, height));
    }

    private void DrawBedAction(GardenBed bed)
    {
        var scale = UiScale.Current;
        var width = ScrollLayout.StableContentWidth();
        var min = ImGui.GetCursorScreenPos();
        if (bedState == BedState.Ready)
        {
            var harvest = new Rect(min, min + new Vector2(width, HarvestHeightUnits * scale));
            if (ui.AccentPill(harvest, Loc.T(L.Greenthumb.Harvest), true, TextStyles.Headline))
            {
                Harvest(bed);
            }

            ImGui.Dummy(new Vector2(width, harvest.Height));
            return;
        }

        var drawList = ImGui.GetWindowDrawList();
        var height = ActionRowUnits * scale;
        var max = min + new Vector2(width, height);
        GroupCard.DrawSurface(drawList, min, max, theme, scale);
        var pad = CardPadUnits * scale;
        var label = Loc.T(L.Greenthumb.MarkTended);
        var pillWidth = Typography.Measure(label, TextStyles.SubheadlineEmphasized).X + PillPadUnits * 2f * scale;
        var pillHeight = PillHeightUnits * scale;
        var centerY = (min.Y + max.Y) * 0.5f;
        var pill = new Rect(new Vector2(max.X - pad - pillWidth, centerY - pillHeight * 0.5f),
            new Vector2(max.X - pad, centerY + pillHeight * 0.5f));
        var titleHeight = Typography.LineHeight(TextStyles.BodyEmphasized);
        var subtitleHeight = Typography.LineHeight(TextStyles.Footnote);
        var top = centerY - (titleHeight + subtitleHeight) * 0.5f;
        var textWidth = pill.Min.X - pad - min.X - pad;
        Typography.Draw(drawList, new Vector2(min.X + pad, top), Loc.T(L.Greenthumb.Tend), theme.TextStrong,
            TextStyles.BodyEmphasized);
        var tendInk = bedState is BedState.TendSoon or BedState.Wilting ? StateInk(bedState) : theme.TextMuted;
        Typography.Draw(drawList, new Vector2(min.X + pad, top + titleHeight),
            Typography.FitText(bedTendText, textWidth, TextStyles.Footnote), tendInk, TextStyles.Footnote);
        if (ui.AccentPill(pill, label, true, TextStyles.SubheadlineEmphasized))
        {
            bed.Tend(DateTime.UtcNow);
            SaveGarden();
            gardenVersion++;
        }

        ImGui.Dummy(new Vector2(width, height));
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
        bedIcon = bed.IsPlanted ? catalog.IconOf(bed.Seed) : 0;
        bedState = StateOf(bed, nowUtc);
        bedProgress = Progress(bed, nowUtc);
        bedStateText = StateLabel(bedState);
        bedPercent = bedProgress >= 0f && bedState != BedState.Ready ? Percent(bedProgress) : string.Empty;
        bedReadyText = GardenTimers.ReadyAtUtc(bed) switch
        {
            null => Loc.T(L.Greenthumb.SetGrowTime),
            { } moment when nowUtc >= moment => Loc.T(L.Greenthumb.StatReady),
            { } moment => Loc.T(L.Greenthumb.ReadyIn, GardenUntil(moment - nowUtc)),
        };
        bedTendText = GardenTimers.WiltsAtUtc(bed, catalog.WiltHours(bed.Seed), nowUtc) switch
        {
            null => string.Empty,
            { } moment when nowUtc >= moment => Loc.T(L.Greenthumb.Wilting),
            { } moment => Loc.T(L.Greenthumb.WiltsIn, GardenUntil(moment - nowUtc)),
        };
        bedPlanted = bed.PlantedAtUtc is { } plantedAt ? TimeText.Ago(plantedAt) : string.Empty;
        bedTended = bed.TendedAtUtc is { } tendedAt ? TimeText.Ago(tendedAt) : string.Empty;
        growDaysText = Loc.Plural(L.Greenthumb.Days, bed.GrowHours / HoursPerDay);
        growHoursText = Loc.Plural(L.Greenthumb.Hours, bed.GrowHours % HoursPerDay);
        RebuildCrossText(patch, bedIndex, bed);
        bedBuiltIndex = bedIndex;
        bedBuiltPatch = patch.Id;
        bedBuiltVersion = gardenVersion;
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
