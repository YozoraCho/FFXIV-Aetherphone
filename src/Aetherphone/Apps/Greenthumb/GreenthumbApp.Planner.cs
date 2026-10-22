using Aetherphone.Core;
using Aetherphone.Core.Gardening;
using Aetherphone.Core.Localization;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Apps.Greenthumb;

internal sealed partial class GreenthumbApp
{
    private const int ModeMake = 0;
    private const int ModeReachable = 1;
    private const int ModeStart = 2;
    private const int MaxRouteChips = 10;
    private const int MaxStarterSets = 10;
    private const int MinDepth = 1;
    private const int MaxDepth = 8;
    private const float StepRowUnits = 60f;
    private const float SegmentHeightUnits = 34f;

    private readonly string[] modeLabels = new string[3];
    private readonly string[] routeLabels = new string[MaxRouteChips];
    private readonly bool[] routeActive = new bool[MaxRouteChips];
    private readonly Action decrementDepth;
    private readonly Action incrementDepth;
    private readonly ChipRail routeRail = new();
    private int plannerMode;
    private int target = -1;
    private int routeIndex;
    private int depth = 2;
    private bool plannerDirty = true;
    private PlanResult plan = PlanResult.None;
    private string targetName = string.Empty;
    private string ownedLabel = string.Empty;
    private int ownedLabelCount = -1;
    private string planHeader = string.Empty;
    private string depthText = string.Empty;
    private string[] stepTitles = Array.Empty<string>();
    private string[] stepSubtitles = Array.Empty<string>();
    private int[] reachable = Array.Empty<int>();
    private string[] reachableHeaders = Array.Empty<string>();
    private int[] reachableStarts = Array.Empty<int>();
    private string[] reachableIds = Array.Empty<string>();
    private string[] starterHeaders = Array.Empty<string>();
    private string[][] starterNames = Array.Empty<string[]>();
    private string[][] starterSources = Array.Empty<string[]>();

    private void DrawPlanner(Rect body)
    {
        RebuildPlanner();
        var scale = UiScale.Current;
        modeLabels[ModeMake] = Loc.T(L.Greenthumb.MakeSeed);
        modeLabels[ModeReachable] = Loc.T(L.Greenthumb.WhatCanIMake);
        modeLabels[ModeStart] = Loc.T(L.Greenthumb.WhereToStart);
        var origin = ImGui.GetCursorScreenPos();
        var width = ScrollLayout.StableContentWidth();
        var strip = new Rect(origin, origin + new Vector2(width, SegmentHeightUnits * scale));
        var mode = SegmentStrip.Draw("greenthumb.planner.mode", strip, modeLabels, plannerMode, theme);
        ImGui.Dummy(strip.Size);
        if (mode != plannerMode)
        {
            plannerMode = mode;
            plannerDirty = true;
            RebuildPlanner();
        }

        Gap(Metrics.Space.Lg);
        if (ownedCount == 0)
        {
            SettingsSection.Hint(Loc.T(L.Greenthumb.NoOwnedHint), theme);
            Gap(Metrics.Space.Md);
        }

        switch (plannerMode)
        {
            case ModeReachable:
                DrawReachable();
                break;
            case ModeStart:
                DrawStart();
                break;
            default:
                DrawMake();
                break;
        }
    }

    private void DrawPlannerInputs(bool withTarget)
    {
        var rows = withTarget ? 3 : 2;
        var card = GroupCard.Begin(theme, rows);
        if (withTarget && SettingsRow.Disclosure(card.NextRow(), Loc.T(L.Greenthumb.TargetSeed), targetName, theme,
                "greenthumb.planner.target"))
        {
            router.Push(GreenthumbRoute.PickTarget());
            pickerDirty = true;
        }

        var gather = SettingsRow.Bool(card.NextRow(), Loc.T(L.Greenthumb.IncludeGatherable),
            configuration.GreenthumbIncludeGatherable, theme, "greenthumb.planner.gather",
            Loc.T(L.Greenthumb.IncludeGatherableHint));
        if (gather != configuration.GreenthumbIncludeGatherable)
        {
            configuration.GreenthumbIncludeGatherable = gather;
            configuration.Save();
            plannerDirty = true;
        }

        if (SettingsRow.Disclosure(card.NextRow(), Loc.T(L.Greenthumb.StartingFrom), ownedLabel, theme,
                "greenthumb.planner.owned"))
        {
            activeTab = GreenthumbTab.Seeds;
            seedListDirty = true;
        }

        card.End();
        Gap(Metrics.Space.Lg);
    }

    private void DrawMake()
    {
        DrawPlannerInputs(true);
        if (target < 0)
        {
            return;
        }

        switch (plan.Status)
        {
            case PlanStatus.AlreadyOwned:
                SettingsSection.Hint(Loc.T(L.Greenthumb.AlreadyOwned), theme);
                return;
            case PlanStatus.Unreachable:
                SettingsSection.Hint(Loc.T(L.Greenthumb.Unreachable), theme);
                if (!configuration.GreenthumbIncludeGatherable)
                {
                    SettingsSection.Hint(Loc.T(L.Greenthumb.UnreachableHint), theme);
                }

                return;
        }

        SettingsSection.Header(planHeader, theme);
        var chips = Math.Min(plan.Routes.Length, MaxRouteChips);
        if (chips > 1)
        {
            for (var index = 0; index < chips; index++)
            {
                routeActive[index] = index == routeIndex;
            }

            var tapped = routeRail.Draw(ui, routeLabels.AsSpan(0, chips), routeActive.AsSpan(0, chips),
                "greenthumb.planner.routes");
            if (tapped >= 0 && tapped != routeIndex)
            {
                routeIndex = tapped;
                BuildSteps();
            }

            Gap(Metrics.Space.Sm);
        }

        var card = GroupCard.Begin(theme, stepTitles.Length, StepRowUnits);
        for (var index = 0; index < stepTitles.Length; index++)
        {
            DrawTwoLineRow(card.NextRow(), stepTitles[index], stepSubtitles[index], theme.TextStrong);
        }

        card.End();
    }

    private void DrawReachable()
    {
        DrawPlannerInputs(false);
        var scale = UiScale.Current;
        var card = GroupCard.Begin(theme, 1);
        var row = card.NextRow();
        var labelWidth = Typography.Measure(Loc.T(L.Greenthumb.Depth), TextStyles.BodyEmphasized).X;
        Typography.Draw(ImGui.GetWindowDrawList(),
            new Vector2(row.Min.X, row.Center.Y - Typography.LineHeight(TextStyles.BodyEmphasized) * 0.5f),
            Loc.T(L.Greenthumb.Depth), theme.TextStrong, TextStyles.BodyEmphasized);
        var height = StepperHeightUnits * scale;
        var left = row.Min.X + labelWidth + Metrics.Space.Lg * scale;
        StepperField.Draw(theme, new Rect(new Vector2(left, row.Center.Y - height * 0.5f),
            new Vector2(row.Max.X, row.Center.Y + height * 0.5f)), depthText, scale, decrementDepth, incrementDepth);
        card.End();
        Gap(Metrics.Space.Lg);
        if (reachable.Length == 0)
        {
            SettingsSection.Hint(Loc.T(L.Greenthumb.NothingNew), theme);
            return;
        }

        for (var group = 0; group < reachableHeaders.Length; group++)
        {
            var start = reachableStarts[group];
            var end = reachableStarts[group + 1];
            SettingsSection.Header(reachableHeaders[group], theme);
            var listCard = GroupCard.Begin(theme, end - start);
            for (var index = start; index < end; index++)
            {
                var listRow = listCard.NextRow();
                if (!ImGui.IsRectVisible(listRow.Min, listRow.Max))
                {
                    continue;
                }

                if (SettingsRow.Disclosure(listRow, catalog.Name(reachable[index]), string.Empty, theme,
                        reachableIds[index]))
                {
                    target = reachable[index];
                    plannerMode = ModeMake;
                    plannerDirty = true;
                }
            }

            listCard.End();
            Gap(Metrics.Space.Lg);
        }
    }

    private void DrawStart()
    {
        DrawPlannerInputs(true);
        if (target < 0)
        {
            return;
        }

        var seed = catalog.Book.Seeds[target];
        if (seed.CanGather)
        {
            SettingsSection.Hint(Loc.T(L.Greenthumb.CanGather), theme);
            for (var index = 0; index < seed.Sources.Length; index++)
            {
                SettingsSection.Hint(seed.Sources[index], theme);
            }

            return;
        }

        if (starterHeaders.Length == 0)
        {
            SettingsSection.Hint(Loc.T(L.Greenthumb.NoSourceInfo), theme);
            return;
        }

        for (var setIndex = 0; setIndex < starterHeaders.Length; setIndex++)
        {
            SettingsSection.Header(starterHeaders[setIndex], theme);
            var names = starterNames[setIndex];
            var card = GroupCard.Begin(theme, names.Length, StepRowUnits);
            for (var index = 0; index < names.Length; index++)
            {
                DrawTwoLineRow(card.NextRow(), names[index], starterSources[setIndex][index], theme.TextStrong);
            }

            card.End();
            Gap(Metrics.Space.Md);
        }
    }

    private void AdjustDepth(int delta)
    {
        var next = Math.Clamp(depth + delta, MinDepth, MaxDepth);
        if (next == depth)
        {
            return;
        }

        depth = next;
        plannerDirty = true;
    }

    private void RefreshOwnedLabel()
    {
        if (ownedLabelCount == ownedCount)
        {
            return;
        }

        ownedLabelCount = ownedCount;
        ownedLabel = Loc.T(L.Greenthumb.OwnedCount, ownedCount);
    }

    private void RebuildPlanner()
    {
        RefreshOwnedLabel();
        if (!plannerDirty || catalog.State != GardenCatalogState.Ready)
        {
            return;
        }

        plannerDirty = false;
        targetName = target >= 0 ? catalog.Name(target) : Loc.T(L.Greenthumb.Choose);
        var gather = configuration.GreenthumbIncludeGatherable;
        switch (plannerMode)
        {
            case ModeReachable:
                BuildReachable(gather);
                break;
            case ModeStart:
                BuildStarters();
                break;
            default:
                BuildPlan(gather);
                break;
        }
    }

    private void BuildPlan(bool gather)
    {
        plan = target >= 0 ? CrossbreedPlanner.FindRoutes(catalog.Book, owned, target, gather) : PlanResult.None;
        routeIndex = 0;
        if (plan.Status != PlanStatus.Found)
        {
            stepTitles = Array.Empty<string>();
            stepSubtitles = Array.Empty<string>();
            return;
        }

        planHeader = string.Concat(Loc.Plural(L.Greenthumb.Steps, plan.StepCount), " · ",
            Loc.Plural(L.Greenthumb.Routes, plan.RouteCount));
        var chips = Math.Min(plan.Routes.Length, MaxRouteChips);
        for (var index = 0; index < chips; index++)
        {
            routeLabels[index] = Loc.T(L.Greenthumb.RouteChip, index + 1);
        }

        BuildSteps();
    }

    private void BuildSteps()
    {
        var steps = plan.Routes[routeIndex];
        stepTitles = new string[steps.Length];
        stepSubtitles = new string[steps.Length];
        for (var index = 0; index < steps.Length; index++)
        {
            var step = steps[index];
            var number = string.Concat((index + 1).ToString(Loc.Culture), ". ");
            if (step.Kind == PlanStepKind.Gather)
            {
                var sources = catalog.Book.Seeds[step.Seed].Sources;
                stepTitles[index] = string.Concat(number, Loc.T(L.Greenthumb.GatherStep, catalog.Name(step.Seed)));
                stepSubtitles[index] = sources.Length > 0 ? sources[0] : string.Empty;
                continue;
            }

            stepTitles[index] = string.Concat(number,
                Loc.T(L.Greenthumb.CrossStep, catalog.Name(step.First), catalog.Name(step.Second)));
            stepSubtitles[index] = Loc.T(L.Greenthumb.Gives, catalog.Name(step.Seed));
        }
    }

    private void BuildReachable(bool gather)
    {
        depthText = Loc.Plural(L.Greenthumb.Steps, depth);
        var costs = CrossbreedPlanner.Costs(catalog.Book, owned, gather);
        var found = CrossbreedPlanner.Reachable(catalog.Book, owned, depth, gather);
        var sorted = new List<int>(found.Length);
        var headers = new List<string>();
        var starts = new List<int>();
        var byName = catalog.SortedByName;
        for (var steps = 1; steps <= depth; steps++)
        {
            var groupStart = sorted.Count;
            for (var nameIndex = 0; nameIndex < byName.Length; nameIndex++)
            {
                var seed = byName[nameIndex];
                if (costs[seed] != steps || Array.IndexOf(found, seed) < 0)
                {
                    continue;
                }

                if (gather && catalog.Book.Seeds[seed].CanGather)
                {
                    continue;
                }

                sorted.Add(seed);
            }

            if (sorted.Count > groupStart)
            {
                headers.Add(Loc.Plural(L.Greenthumb.Steps, steps));
                starts.Add(groupStart);
            }
        }

        starts.Add(sorted.Count);
        reachable = sorted.ToArray();
        reachableHeaders = headers.ToArray();
        reachableStarts = starts.ToArray();
        reachableIds = new string[reachable.Length];
        for (var index = 0; index < reachable.Length; index++)
        {
            reachableIds[index] = string.Concat("greenthumb.reach.", catalog.Book.Seeds[reachable[index]].Key);
        }
    }

    private void BuildStarters()
    {
        if (target < 0)
        {
            starterHeaders = Array.Empty<string>();
            return;
        }

        var sets = catalog.Book.Seeds[target].Starters;
        var count = Math.Min(sets.Length, MaxStarterSets);
        starterHeaders = new string[count];
        starterNames = new string[count][];
        starterSources = new string[count][];
        for (var setIndex = 0; setIndex < count; setIndex++)
        {
            var set = sets[setIndex];
            starterHeaders[setIndex] = Loc.T(L.Greenthumb.StarterOption, setIndex + 1);
            starterNames[setIndex] = new string[set.Length];
            starterSources[setIndex] = new string[set.Length];
            for (var index = 0; index < set.Length; index++)
            {
                var seed = catalog.Book.Seeds[set[index]];
                starterNames[setIndex][index] = catalog.Name(set[index]);
                starterSources[setIndex][index] = seed.CanGather ? seed.Sources[0] : Loc.T(L.Greenthumb.CrossOnly);
            }
        }
    }
}
