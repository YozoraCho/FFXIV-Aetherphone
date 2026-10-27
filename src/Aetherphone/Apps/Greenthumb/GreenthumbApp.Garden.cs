using Aetherphone.Core;
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
    private const int StatCount = 3;
    private const float StatHeightUnits = 84f;
    private const int StatLabelLines = 2;
    private const float StatGapUnits = 10f;
    private const float CardPadUnits = 14f;
    private const float PatchCardHeightUnits = 96f;
    private const float PatchCardGapUnits = 10f;
    private const float ThumbUnits = 68f;
    private const float ThumbPadUnits = 6f;
    private const float ThumbGapUnits = 3f;
    private const float ThumbRadiusUnits = 12f;
    private const float ThumbCellRadiusUnits = 4f;
    private const float ThumbIconMinUnits = 14f;
    private const float ThumbIconFraction = 0.8f;
    private const float ThumbBackdropMix = 0.6f;
    private const float AddCardHeightUnits = 54f;
    private const float AddStrokeAlpha = 0.22f;
    private const string NoValue = "-";

    private static readonly GardenPatchKind[] KindOrder =
    {
        GardenPatchKind.Round, GardenPatchKind.Oblong, GardenPatchKind.Deluxe, GardenPatchKind.Flowerpot,
    };

    private readonly ActionSheet kindSheet = new();
    private readonly ActionSheet.Item[] kindItems = new ActionSheet.Item[4];
    private readonly NavBarButton[] gardenButtons = new NavBarButton[2];
    private readonly string[] statValues = new string[StatCount];
    private readonly string[] statLabels = new string[StatCount];
    private readonly StatusTone[] statTones = new StatusTone[StatCount];
    private readonly string[][] statLabelLines = new string[StatCount][];
    private float statWrapWidth;
    private string[] patchTitles = Array.Empty<string>();
    private string[] patchSubtitles = Array.Empty<string>();
    private string[] patchStatus = Array.Empty<string>();
    private StatusTone[] patchTones = Array.Empty<StatusTone>();
    private BedState[][] patchBedStates = Array.Empty<BedState[]>();
    private uint[][] patchBedIcons = Array.Empty<uint[]>();
    private int gardenVersion = 1;
    private int listBuiltVersion;

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
        var scale = UiScale.Current;
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = ScrollLayout.StableContentWidth();
        var y = origin.Y;
        DrawPlaceBanner(drawList, origin, width, scale, ref y);
        y = DrawStats(drawList, new Vector2(origin.X, y), width, scale) + Metrics.Space.Lg * scale;
        var cardHeight = PatchCardHeightUnits * scale;
        var gap = PatchCardGapUnits * scale;
        for (var index = 0; index < patches.Count; index++)
        {
            var min = new Vector2(origin.X, y);
            var max = new Vector2(origin.X + width, y + cardHeight);
            if (ImGui.IsRectVisible(min, max) && DrawPatchCard(drawList, index, min, max, scale))
            {
                router.Push(GreenthumbRoute.Patch(patches[index].Id));
            }

            y += cardHeight + gap;
        }

        var addMin = new Vector2(origin.X, y);
        var addMax = new Vector2(origin.X + width, y + AddCardHeightUnits * scale);
        if (DrawAddCard(drawList, addMin, addMax, scale))
        {
            OpenKindSheet();
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, addMax.Y - origin.Y));
    }

    private float DrawStats(ImDrawListPtr drawList, Vector2 origin, float width, float scale)
    {
        var gap = StatGapUnits * scale;
        var tileWidth = (width - gap * (StatCount - 1)) / StatCount;
        var height = StatHeightUnits * scale;
        var pad = CardPadUnits * scale;
        var inner = tileWidth - pad * 2f;
        var valueHeight = Typography.LineHeight(TextStyles.Title2);
        var labelHeight = Typography.LineHeight(TextStyles.Footnote);
        if (MathF.Abs(statWrapWidth - inner) > 0.5f)
        {
            statWrapWidth = inner;
            for (var index = 0; index < StatCount; index++)
            {
                var wrapped = Typography.WrapText(statLabels[index], TextStyles.Footnote, inner);
                if (wrapped.Length > StatLabelLines)
                {
                    var last = Typography.FitText(string.Concat(wrapped[StatLabelLines - 1], " ",
                        wrapped[StatLabelLines]), inner, TextStyles.Footnote);
                    wrapped = new[] { wrapped[0], last };
                }

                statLabelLines[index] = wrapped;
            }
        }

        var top = pad;
        for (var index = 0; index < StatCount; index++)
        {
            var min = new Vector2(origin.X + index * (tileWidth + gap), origin.Y);
            var max = min + new Vector2(tileWidth, height);
            GroupCard.DrawSurface(drawList, min, max, theme, scale);
            var ink = statTones[index] == StatusTone.Muted ? theme.TextStrong : ToneInk(statTones[index]);
            Typography.Draw(drawList, new Vector2(min.X + pad, min.Y + top),
                Typography.FitText(statValues[index], inner, TextStyles.Title2), ink, TextStyles.Title2);
            var lines = statLabelLines[index];
            for (var line = 0; line < lines.Length; line++)
            {
                Typography.Draw(drawList, new Vector2(min.X + pad, min.Y + top + valueHeight + line * labelHeight),
                    lines[line], theme.TextMuted, TextStyles.Footnote);
            }
        }

        return origin.Y + height;
    }

    private bool DrawPatchCard(ImDrawListPtr drawList, int index, Vector2 min, Vector2 max, float scale)
    {
        var clicked = DrawCardButton(drawList, min, max, scale);
        var pad = CardPadUnits * scale;
        var thumbSize = ThumbUnits * scale;
        var thumbMin = new Vector2(min.X + pad, (min.Y + max.Y - thumbSize) * 0.5f);
        DrawPatchThumb(drawList, configuration.GreenthumbPatches[index].Kind, patchBedStates[index],
            patchBedIcons[index], thumbMin, thumbSize, scale);
        var chevronWidth = ChevronUnits * scale;
        var textLeft = thumbMin.X + thumbSize + pad;
        var textWidth = max.X - pad - chevronWidth - Metrics.Space.Sm * scale - textLeft;
        var titleHeight = Typography.LineHeight(TextStyles.Headline);
        var lineHeight = Typography.LineHeight(TextStyles.Subheadline);
        var statusHeight = Typography.LineHeight(TextStyles.FootnoteEmphasized);
        var top = (min.Y + max.Y - titleHeight - lineHeight - statusHeight) * 0.5f;
        Typography.Draw(drawList, new Vector2(textLeft, top),
            Typography.FitText(patchTitles[index], textWidth, TextStyles.Headline), theme.TextStrong,
            TextStyles.Headline);
        Typography.Draw(drawList, new Vector2(textLeft, top + titleHeight),
            Typography.FitText(patchSubtitles[index], textWidth, TextStyles.Subheadline), theme.TextMuted,
            TextStyles.Subheadline);
        Typography.Draw(drawList, new Vector2(textLeft, top + titleHeight + lineHeight),
            Typography.FitText(patchStatus[index], textWidth, TextStyles.FootnoteEmphasized),
            ToneInk(patchTones[index]), TextStyles.FootnoteEmphasized);
        DrawChevron(drawList, new Vector2(max.X - pad - chevronWidth * 0.5f, (min.Y + max.Y) * 0.5f), scale);
        return clicked;
    }

    private void DrawPatchThumb(ImDrawListPtr drawList, GardenPatchKind kind, BedState[] states, uint[] icons,
        Vector2 min, float size, float scale)
    {
        var max = min + new Vector2(size, size);
        Squircle.Fill(drawList, min, max, ThumbRadiusUnits * scale,
            ImGui.GetColorU32(Palette.Mix(theme.GroupedCard, theme.AppBackground, ThumbBackdropMix)));
        var columns = GardenLayout.Columns(kind);
        var rows = GardenLayout.Rows(kind);
        var span = Math.Max(Math.Max(columns, rows), 2);
        var pad = ThumbPadUnits * scale;
        var gap = ThumbGapUnits * scale;
        var cell = (size - pad * 2f - gap * (span - 1)) / span;
        var gridWidth = cell * columns + gap * (columns - 1);
        var gridHeight = cell * rows + gap * (rows - 1);
        var gridMin = new Vector2(min.X + (size - gridWidth) * 0.5f, min.Y + (size - gridHeight) * 0.5f);
        var radius = ThumbCellRadiusUnits * scale;
        var drawIcons = cell >= ThumbIconMinUnits * scale;
        for (var cellIndex = 0; cellIndex < columns * rows; cellIndex++)
        {
            var bed = GardenLayout.BedAtCell(kind, cellIndex);
            if (bed < 0 || bed >= states.Length)
            {
                continue;
            }

            var cellMin = gridMin + new Vector2(cellIndex % columns * (cell + gap), cellIndex / columns * (cell + gap));
            var cellMax = cellMin + new Vector2(cell, cell);
            Squircle.Fill(drawList, cellMin, cellMax, radius, ImGui.GetColorU32(CellFill(states[bed])));
            if (drawIcons && states[bed] != BedState.Empty && icons[bed] != 0)
            {
                DrawSeedIcon(drawList, icons[bed], (cellMin + cellMax) * 0.5f, cell * ThumbIconFraction);
            }
        }
    }

    private bool DrawAddCard(ImDrawListPtr drawList, Vector2 min, Vector2 max, float scale)
    {
        var hovered = UiInteract.Hover(min, max);
        var radius = Metrics.Radius.Grouped * scale;
        if (hovered)
        {
            Squircle.Fill(drawList, min, max, radius,
                ImGui.GetColorU32(Palette.WithAlpha(theme.TextStrong, CardHoverMix)));
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        Squircle.Stroke(drawList, min, max, radius,
            ImGui.GetColorU32(Palette.WithAlpha(ui.Palette.Accent, AddStrokeAlpha)), StateStrokeUnits * scale);
        Typography.DrawCentered(drawList, (min + max) * 0.5f, Loc.T(L.Greenthumb.AddPatch), ui.Palette.Accent,
            TextStyles.BodyEmphasized);
        return UiInteract.Click(min, max, hovered);
    }

    private void RebuildGardenText()
    {
        var patches = configuration.GreenthumbPatches;
        if (listBuiltVersion == gardenVersion && patchTitles.Length == patches.Count)
        {
            return;
        }

        if (patchTitles.Length != patches.Count)
        {
            patchTitles = new string[patches.Count];
            patchSubtitles = new string[patches.Count];
            patchStatus = new string[patches.Count];
            patchTones = new StatusTone[patches.Count];
            patchBedStates = new BedState[patches.Count][];
            patchBedIcons = new uint[patches.Count][];
        }

        var nowUtc = DateTime.UtcNow;
        var tendingTotal = 0;
        var readyTotal = 0;
        DateTime? nextHarvest = null;
        for (var index = 0; index < patches.Count; index++)
        {
            var patch = patches[index];
            patch.EnsureBeds();
            var count = patch.Beds.Count;
            if (patchBedStates[index] is null || patchBedStates[index].Length != count)
            {
                patchBedStates[index] = new BedState[count];
                patchBedIcons[index] = new uint[count];
            }

            var planted = 0;
            var tending = 0;
            var ready = 0;
            DateTime? patchNext = null;
            for (var bedIndex = 0; bedIndex < count; bedIndex++)
            {
                var bed = patch.Beds[bedIndex];
                var state = StateOf(bed, nowUtc);
                patchBedStates[index][bedIndex] = state;
                patchBedIcons[index][bedIndex] = bed.IsPlanted ? catalog.IconOf(bed.Seed) : 0;
                if (state == BedState.Empty)
                {
                    continue;
                }

                planted++;
                if (state is BedState.TendSoon or BedState.Wilting)
                {
                    tending++;
                }
                else if (state == BedState.Ready)
                {
                    ready++;
                }

                if (state != BedState.Ready && GardenTimers.ReadyAtUtc(bed) is { } readyAt &&
                    (patchNext is null || readyAt < patchNext))
                {
                    patchNext = readyAt;
                }
            }

            tendingTotal += tending;
            readyTotal += ready;
            if (patchNext is { } next && (nextHarvest is null || next < nextHarvest))
            {
                nextHarvest = next;
            }

            patchTitles[index] = PatchLabel.For(patch);
            patchSubtitles[index] = string.Concat(Loc.Plural(L.Greenthumb.Beds, count), " · ",
                Loc.T(L.Greenthumb.PlantedCount, planted));
            SetPatchStatus(index, planted, tending, ready, patchNext, nowUtc);
        }

        statValues[0] = tendingTotal.ToString(Loc.Culture);
        statLabels[0] = Loc.T(L.Greenthumb.StatTending);
        statTones[0] = tendingTotal > 0 ? StatusTone.Warn : StatusTone.Muted;
        statValues[1] = readyTotal.ToString(Loc.Culture);
        statLabels[1] = Loc.T(L.Greenthumb.StatReady);
        statTones[1] = readyTotal > 0 ? StatusTone.Good : StatusTone.Muted;
        statValues[2] = nextHarvest is { } soonest ? ShortSpan(soonest - nowUtc) : NoValue;
        statLabels[2] = Loc.T(L.Greenthumb.StatNextHarvest);
        statTones[2] = StatusTone.Muted;
        statWrapWidth = 0f;
        listBuiltVersion = gardenVersion;
    }

    private void SetPatchStatus(int index, int planted, int tending, int ready, DateTime? next, DateTime nowUtc)
    {
        if (tending > 0)
        {
            patchStatus[index] = Loc.Plural(L.Greenthumb.NeedTending, tending);
            patchTones[index] = StatusTone.Warn;
        }
        else if (ready > 0)
        {
            patchStatus[index] = Loc.Plural(L.Greenthumb.ReadyToHarvest, ready);
            patchTones[index] = StatusTone.Good;
        }
        else if (next is { } readyAt)
        {
            patchStatus[index] = Loc.T(L.Greenthumb.NextHarvestIn, GardenUntil(readyAt - nowUtc));
            patchTones[index] = StatusTone.Muted;
        }
        else
        {
            patchStatus[index] = planted > 0 ? Loc.T(L.Greenthumb.Growing) : Loc.T(L.Greenthumb.NothingPlanted);
            patchTones[index] = StatusTone.Muted;
        }
    }

    private static string ShortSpan(TimeSpan span)
    {
        if (span.TotalHours < 1)
        {
            return Loc.T(L.Time.MinutesShort, Math.Max(1, (int)span.TotalMinutes));
        }

        if (span.TotalDays < 1)
        {
            return Loc.T(L.Time.HoursShort, (int)span.TotalHours);
        }

        var days = Loc.T(L.Time.DaysShort, (int)span.TotalDays);
        return span.Hours > 0 ? string.Concat(days, " ", Loc.T(L.Time.HoursShort, span.Hours)) : days;
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
}
