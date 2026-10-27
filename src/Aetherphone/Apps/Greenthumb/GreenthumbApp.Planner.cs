using Aetherphone.Core;
using Aetherphone.Core.Gardening;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Theme;
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
    private const float NodeRadiusUnits = 13f;
    private const float NodeColumnUnits = 38f;
    private const float StepPadUnits = 12f;
    private const float StepIconUnits = 34f;
    private const float StepGapUnits = 10f;
    private const float StepLineAlpha = 0.35f;
    private const float StepLineUnits = 2f;
    private const float SegmentHeightUnits = 34f;
    private const float TargetIconUnits = 44f;
    private const int InputGather = 0;
    private const int InputOwned = 1;

    private readonly string[] modeLabels = new string[3];
    private readonly string[] routeLabels = new string[MaxRouteChips];
    private readonly bool[] routeActive = new bool[MaxRouteChips];
    private readonly Action decrementDepth;
    private readonly Action incrementDepth;
    private readonly ChipRail routeRail = new();
    private readonly ChipRail inputRail = new();
    private readonly string[] inputLabels = new string[2];
    private readonly bool[] inputActive = new bool[2];
    private int plannerMode;
    private int target = -1;
    private int routeIndex;
    private int plannedTarget = -1;
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
    private string[] stepKinds = Array.Empty<string>();
    private bool[] stepGathers = Array.Empty<bool>();
    private uint[] stepIcons = Array.Empty<uint>();
    private string[] stepNumbers = Array.Empty<string>();
    private string[][] stepTitleLines = Array.Empty<string[]>();
    private string[][] stepSubtitleLines = Array.Empty<string[]>();
    private float stepWrapWidth;
    private int[] reachable = Array.Empty<int>();
    private string[] reachableHeaders = Array.Empty<string>();
    private int[] reachableStarts = Array.Empty<int>();
    private string[] reachableIds = Array.Empty<string>();
    private SourceCard[] starterCards = Array.Empty<SourceCard>();
    private SourceCard targetSourceCard = new(string.Empty, Array.Empty<int>(), Array.Empty<string>(),
        Array.Empty<string[]>());
    private PlanStatus starterStatus;
    private bool startFromOwned;

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
        var mode = SegmentStrip.Draw("greenthumb.planner.mode", strip, modeLabels, plannerMode, ui.Palette);
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
        if (withTarget)
        {
            DrawTargetCard();
            Gap(Metrics.Space.Md);
        }

        inputLabels[InputGather] = Loc.T(L.Greenthumb.IncludeGatherable);
        inputLabels[InputOwned] = ownedLabel;
        inputActive[InputGather] = configuration.GreenthumbIncludeGatherable;
        inputActive[InputOwned] = false;
        var tapped = inputRail.Draw(ui, inputLabels, inputActive, "greenthumb.planner.inputs");
        if (tapped == InputGather)
        {
            configuration.GreenthumbIncludeGatherable = !configuration.GreenthumbIncludeGatherable;
            configuration.Save();
            plannerDirty = true;
        }
        else if (tapped == InputOwned)
        {
            activeTab = GreenthumbTab.Seeds;
            seedListDirty = true;
        }

        SettingsSection.Hint(Loc.T(L.Greenthumb.IncludeGatherableHint), theme);
        Gap(Metrics.Space.Lg);
    }

    private void DrawTargetCard()
    {
        var scale = UiScale.Current;
        var drawList = ImGui.GetWindowDrawList();
        var width = ScrollLayout.StableContentWidth();
        var pad = CardPadUnits * scale;
        var iconSize = TargetIconUnits * scale;
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(width, iconSize + pad * 2f);
        if (DrawCardButton(drawList, min, max, scale))
        {
            router.Push(GreenthumbRoute.PickTarget());
            pickerDirty = true;
        }

        var centerY = (min.Y + max.Y) * 0.5f;
        DrawSeedIcon(drawList, target >= 0 ? catalog.Icon(target) : 0,
            new Vector2(min.X + pad + iconSize * 0.5f, centerY), iconSize);
        var chevronX = max.X - pad - ChevronUnits * scale * 0.5f;
        DrawChevron(drawList, new Vector2(chevronX, centerY), scale);
        var textLeft = min.X + pad * 2f + iconSize;
        var textWidth = chevronX - ChevronUnits * scale - textLeft;
        var overline = Typography.LineHeight(TextStyles.Footnote);
        var title = Typography.LineHeight(TextStyles.Headline);
        var top = centerY - (overline + title) * 0.5f;
        Typography.Draw(drawList, new Vector2(textLeft, top),
            Typography.FitText(Loc.T(L.Greenthumb.TargetSeed), textWidth, TextStyles.Footnote), theme.TextMuted,
            TextStyles.Footnote);
        Typography.Draw(drawList, new Vector2(textLeft, top + overline),
            Typography.FitText(targetName, textWidth, TextStyles.Headline),
            target >= 0 ? theme.TextStrong : ui.Palette.Accent, TextStyles.Headline);
        ImGui.Dummy(max - min);
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

        DrawSteps();
    }

    private void DrawSteps()
    {
        var scale = UiScale.Current;
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = ScrollLayout.StableContentWidth();
        var pad = StepPadUnits * scale;
        var iconSize = StepIconUnits * scale;
        var cardLeft = origin.X + NodeColumnUnits * scale;
        var textLeft = cardLeft + pad + iconSize + pad;
        var textWidth = origin.X + width - pad - textLeft - ChevronUnits * scale - pad;
        WrapSteps(textWidth);
        var kindHeight = Typography.LineHeight(TextStyles.Caption1);
        var titleHeight = Typography.LineHeight(TextStyles.SubheadlineEmphasized);
        var lineHeight = Typography.LineHeight(TextStyles.Footnote);
        var gap = StepGapUnits * scale;
        var nodeRadius = NodeRadiusUnits * scale;
        var nodeX = origin.X + nodeRadius;
        var y = origin.Y;
        var previousNode = 0f;
        for (var index = 0; index < stepTitles.Length; index++)
        {
            var textHeight = kindHeight + stepTitleLines[index].Length * titleHeight +
                             stepSubtitleLines[index].Length * lineHeight;
            var height = MathF.Max(textHeight, iconSize) + pad * 2f;
            var min = new Vector2(cardLeft, y);
            var max = new Vector2(origin.X + width, y + height);
            var nodeY = y + pad + MathF.Min(iconSize, textHeight) * 0.5f;
            var ink = stepGathers[index] ? TendInk : ui.Palette.Accent;
            if (index > 0)
            {
                drawList.AddLine(new Vector2(nodeX, previousNode), new Vector2(nodeX, nodeY),
                    ImGui.GetColorU32(Palette.WithAlpha(ui.Palette.Accent, StepLineAlpha)), StepLineUnits * scale);
            }

            var cross = !stepGathers[index];
            if (cross)
            {
                if (DrawCardButton(drawList, min, max, scale))
                {
                    OpenLayout(plan.Routes[routeIndex][index]);
                }

                DrawChevron(drawList, new Vector2(max.X - pad - ChevronUnits * scale * 0.5f, (min.Y + max.Y) * 0.5f),
                    scale);
            }
            else
            {
                GroupCard.DrawSurface(drawList, min, max, theme, scale);
            }

            var iconCenter = min + new Vector2(pad + iconSize * 0.5f, pad + iconSize * 0.5f);
            DrawSeedIcon(drawList, stepIcons[index], iconCenter, iconSize);
            var textTop = min.Y + pad + MathF.Max(0f, (iconSize - textHeight) * 0.5f);
            Typography.Draw(drawList, new Vector2(textLeft, textTop), stepKinds[index], ink, TextStyles.Caption1);
            textTop += kindHeight;
            var titles = stepTitleLines[index];
            for (var line = 0; line < titles.Length; line++)
            {
                Typography.Draw(drawList, new Vector2(textLeft, textTop), titles[line], theme.TextStrong,
                    TextStyles.SubheadlineEmphasized);
                textTop += titleHeight;
            }

            var subtitles = stepSubtitleLines[index];
            for (var line = 0; line < subtitles.Length; line++)
            {
                Typography.Draw(drawList, new Vector2(textLeft, textTop), subtitles[line], theme.TextMuted,
                    TextStyles.Footnote);
                textTop += lineHeight;
            }

            ProgressRing.Disc(new Vector2(nodeX, nodeY), nodeRadius, ink);
            Typography.DrawCentered(drawList, new Vector2(nodeX, nodeY), stepNumbers[index], theme.AppBackground,
                TextStyles.FootnoteEmphasized);
            previousNode = nodeY;
            y = max.Y + gap;
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, MathF.Max(0f, y - gap - origin.Y)));
    }

    private void WrapSteps(float textWidth)
    {
        if (MathF.Abs(stepWrapWidth - textWidth) <= 0.5f && stepTitleLines.Length == stepTitles.Length)
        {
            return;
        }

        stepWrapWidth = textWidth;
        stepTitleLines = new string[stepTitles.Length][];
        stepSubtitleLines = new string[stepTitles.Length][];
        for (var index = 0; index < stepTitles.Length; index++)
        {
            stepTitleLines[index] = Typography.WrapText(stepTitles[index], TextStyles.SubheadlineEmphasized,
                textWidth);
            stepSubtitleLines[index] = stepSubtitles[index].Length > 0
                ? Typography.WrapText(stepSubtitles[index], TextStyles.Footnote, textWidth)
                : Array.Empty<string>();
        }
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

        if (catalog.Book.Seeds[target].CanGather)
        {
            DrawSourceCard(targetSourceCard);
            return;
        }

        if (starterStatus == PlanStatus.AlreadyOwned)
        {
            SettingsSection.Hint(Loc.T(L.Greenthumb.AlreadyOwned), theme);
            return;
        }

        if (startFromOwned)
        {
            SettingsSection.Hint(Loc.T(L.Greenthumb.StartFromOwned), theme);
            return;
        }

        if (starterCards.Length == 0)
        {
            SettingsSection.Hint(Loc.T(L.Greenthumb.NoSourceInfo), theme);
            return;
        }

        for (var setIndex = 0; setIndex < starterCards.Length; setIndex++)
        {
            DrawSourceCard(starterCards[setIndex]);
            Gap(Metrics.Space.Md);
        }
    }

    private void DrawSourceCard(SourceCard card)
    {
        var scale = UiScale.Current;
        var drawList = ImGui.GetWindowDrawList();
        var width = ScrollLayout.StableContentWidth();
        var pad = CardPadUnits * scale;
        var iconSize = StepIconUnits * scale;
        var gap = Metrics.Space.Md * scale;
        var textWidth = width - pad * 2f - iconSize - gap;
        card.Wrap(textWidth);
        var titleHeight = Typography.LineHeight(TextStyles.Headline);
        var nameHeight = Typography.LineHeight(TextStyles.BodyEmphasized);
        var lineHeight = Typography.LineHeight(TextStyles.Footnote);
        var height = pad * 2f + titleHeight + Metrics.Space.Sm * scale;
        for (var index = 0; index < card.Seeds.Length; index++)
        {
            height += MathF.Max(iconSize, nameHeight + card.Lines[index].Length * lineHeight);
        }

        height += gap * Math.Max(0, card.Seeds.Length - 1);
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(width, height);
        GroupCard.DrawSurface(drawList, min, max, theme, scale);
        var left = min.X + pad;
        Typography.Draw(drawList, new Vector2(left, min.Y + pad),
            Typography.FitText(card.Title, width - pad * 2f, TextStyles.Headline), theme.TextStrong,
            TextStyles.Headline);
        var y = min.Y + pad + titleHeight + Metrics.Space.Sm * scale;
        for (var index = 0; index < card.Seeds.Length; index++)
        {
            DrawSeedIcon(drawList, catalog.Icon(card.Seeds[index]), new Vector2(left + iconSize * 0.5f, y + iconSize * 0.5f), iconSize);
            var textLeft = left + iconSize + gap;
            Typography.Draw(drawList, new Vector2(textLeft, y),
                Typography.FitText(card.Names[index], textWidth, TextStyles.BodyEmphasized), theme.TextStrong,
                TextStyles.BodyEmphasized);
            var lines = card.Lines[index];
            for (var line = 0; line < lines.Length; line++)
            {
                Typography.Draw(drawList, new Vector2(textLeft, y + nameHeight + line * lineHeight), lines[line],
                    theme.TextMuted, TextStyles.Footnote);
            }

            y += MathF.Max(iconSize, nameHeight + lines.Length * lineHeight) + gap;
        }

        ImGui.Dummy(new Vector2(width, height));
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
        var previous = plan.Status == PlanStatus.Found && routeIndex < plan.Routes.Length && plannedTarget == target
            ? plan.Routes[routeIndex]
            : null;
        plan = target >= 0 ? CrossbreedPlanner.FindRoutes(catalog.Book, owned, target, gather) : PlanResult.None;
        plannedTarget = target;
        routeIndex = previous is null ? 0 : MatchingRoute(previous);
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

    private int MatchingRoute(PlanStep[] previous)
    {
        if (plan.Status != PlanStatus.Found)
        {
            return 0;
        }

        var best = 0;
        var bestShared = 0;
        var limit = Math.Min(plan.Routes.Length, MaxRouteChips);
        for (var index = 0; index < limit; index++)
        {
            var shared = SharedCrosses(previous, plan.Routes[index]);
            if (shared > bestShared)
            {
                best = index;
                bestShared = shared;
            }
        }

        return best;
    }

    private static int SharedCrosses(PlanStep[] first, PlanStep[] second)
    {
        var shared = 0;
        for (var index = 0; index < first.Length; index++)
        {
            if (first[index].Kind == PlanStepKind.Cross && Array.IndexOf(second, first[index]) >= 0)
            {
                shared++;
            }
        }

        return shared;
    }

    private void BuildSteps()
    {
        var steps = plan.Routes[routeIndex];
        stepTitles = new string[steps.Length];
        stepSubtitles = new string[steps.Length];
        stepKinds = new string[steps.Length];
        stepGathers = new bool[steps.Length];
        stepIcons = new uint[steps.Length];
        stepNumbers = new string[steps.Length];
        stepWrapWidth = 0f;
        for (var index = 0; index < steps.Length; index++)
        {
            var step = steps[index];
            stepNumbers[index] = (index + 1).ToString(Loc.Culture);
            stepIcons[index] = catalog.Icon(step.Seed);
            if (step.Kind == PlanStepKind.Gather)
            {
                var sources = catalog.Book.Seeds[step.Seed].Sources;
                stepKinds[index] = Loc.T(L.Greenthumb.KindGather);
                stepGathers[index] = true;
                stepTitles[index] = catalog.Name(step.Seed);
                stepSubtitles[index] = sources.Length > 0 ? catalog.SourceText(sources[0]) : string.Empty;
                continue;
            }

            stepKinds[index] = Loc.T(L.Greenthumb.KindCross);
            stepTitles[index] = Loc.T(L.Greenthumb.CrossStep, catalog.Name(step.First), catalog.Name(step.Second));
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
            starterCards = Array.Empty<SourceCard>();
            return;
        }

        BuildTargetSources();

        var routes = CrossbreedPlanner.FindRoutes(catalog.Book, owned, target, true);
        starterStatus = routes.Status;
        var sets = CrossbreedPlanner.GatherSets(catalog.Book, routes);
        startFromOwned = routes.Status == PlanStatus.Found && sets.Count > 0 && sets[0].Length == 0;
        if (startFromOwned)
        {
            sets.Clear();
        }

        var count = Math.Min(sets.Count, MaxStarterSets);
        var paragraphs = new List<string>();
        starterCards = new SourceCard[count];
        for (var setIndex = 0; setIndex < count; setIndex++)
        {
            var set = sets[setIndex];
            var details = new string[set.Length][];
            var names = new string[set.Length];
            for (var index = 0; index < set.Length; index++)
            {
                names[index] = catalog.Name(set[index]);
                var seed = catalog.Book.Seeds[set[index]];
                paragraphs.Clear();
                if (seed.CanGather)
                {
                    AddSourceDetail(seed.Sources[0], paragraphs);
                }
                else
                {
                    paragraphs.Add(Loc.T(L.Greenthumb.CrossOnly));
                }

                details[index] = paragraphs.ToArray();
            }

            starterCards[setIndex] = new SourceCard(Loc.T(L.Greenthumb.StarterOption, setIndex + 1), set, names,
                details);
        }
    }

    private void BuildTargetSources()
    {
        if (target < 0 || !catalog.Book.Seeds[target].CanGather)
        {
            return;
        }

        var sources = catalog.Book.Seeds[target].Sources;
        var details = new List<string>();
        for (var index = 0; index < sources.Length; index++)
        {
            AddSourceDetail(sources[index], details);
        }

        targetSourceCard = new SourceCard(Loc.T(L.Greenthumb.CanGather), new[] { target },
            new[] { catalog.Name(target) }, new[] { details.ToArray() });
    }

    private void AddSourceDetail(SeedSource source, List<string> into)
    {
        into.Add(catalog.SourceText(source));
        var note = GardenCatalog.SourceNote(source);
        if (note.Length > 0)
        {
            into.Add(note);
        }
    }

    private sealed class SourceCard
    {
        private float wrapWidth = -1f;

        public SourceCard(string title, int[] seeds, string[] names, string[][] details)
        {
            Title = title;
            Seeds = seeds;
            Names = names;
            Details = details;
            Lines = new string[details.Length][];
        }

        public string Title { get; }

        public int[] Seeds { get; }

        public string[] Names { get; }

        public string[][] Details { get; }

        public string[][] Lines { get; }

        public void Wrap(float textWidth)
        {
            if (MathF.Abs(wrapWidth - textWidth) <= 0.5f)
            {
                return;
            }

            wrapWidth = textWidth;
            var lines = new List<string>();
            for (var index = 0; index < Details.Length; index++)
            {
                lines.Clear();
                var paragraphs = Details[index];
                for (var paragraph = 0; paragraph < paragraphs.Length; paragraph++)
                {
                    lines.AddRange(Typography.WrapText(paragraphs[paragraph], TextStyles.Footnote, textWidth));
                }

                Lines[index] = lines.ToArray();
            }
        }
    }
}
