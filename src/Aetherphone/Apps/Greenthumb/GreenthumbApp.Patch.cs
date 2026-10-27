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
    private const int MaxPatchNameLength = 32;
    private const int MaxCrossPairs = 6;
    private const int MaxNameLines = 2;
    private const float CellGapUnits = 8f;
    private const float CellMaxUnits = 112f;
    private const float CellPaddingUnits = 8f;
    private const float CellRadiusUnits = 14f;
    private const float CellIconFraction = 0.34f;
    private const float CellPlusUnits = 20f;
    private const float EmptyStrokeAlpha = 0.14f;
    private const float CenterGlyphAlpha = 0.18f;
    private const float CenterGlyphFraction = 0.3f;
    private const float PairIconUnits = 18f;
    private const float PairIconColumnUnits = 30f;

    private readonly string[] crossPairTitles = new string[MaxCrossPairs];
    private readonly string[] crossPairBodies = new string[MaxCrossPairs];
    private readonly string[][] crossPairLines = new string[MaxCrossPairs][];
    private string[] bedNumbers = Array.Empty<string>();
    private string[] bedNames = Array.Empty<string>();
    private string[][] bedNameLines = Array.Empty<string[]>();
    private float bedNameWrapWidth;
    private string[] bedStatus = Array.Empty<string>();
    private BedState[] bedStates = Array.Empty<BedState>();
    private uint[] bedIcons = Array.Empty<uint>();
    private int crossPairCount;
    private float crossPairWrapWidth;
    private int patchBuiltVersion;
    private Guid patchBuiltId;
    private string patchNameDraft = string.Empty;
    private Guid patchNameId;
    private bool patchNameDirty;

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
            var tapped = DrawBedGrid(patch);
            if (tapped >= 0)
            {
                router.Push(GreenthumbRoute.BedOf(patch.Id, tapped));
                gardenVersion++;
            }
            Gap(Metrics.Space.Xl);
            DrawCrossPairs();
            if (HasPlanted(patch))
            {
                var card = GroupCard.Begin(theme, 1);
                if (SettingsRow.Action(card.NextRow(), Loc.T(L.Greenthumb.TendAll), ui.Palette.Accent, theme))
                {
                    TendAll(patch);
                }

                card.End();
                Gap(Metrics.Space.Lg);
            }

            DrawPatchName(patch);
            Gap(Metrics.Space.Lg);
            var removeCard = GroupCard.Begin(theme, 1);
            if (SettingsRow.Action(removeCard.NextRow(), Loc.T(L.Greenthumb.RemovePatch), theme.Danger, theme))
            {
                AskRemovePatch(patch);
            }

            removeCard.End();
            Gap(BottomBreathing);
        }

        AppHeader.EndLargeTitle(in navBar, context, "greenthumb.patch.nav", PatchLabel.For(patch),
            NavBarStyle.From(ui), ReadOnlySpan<NavBarButton>.Empty, Loc.T(L.Greenthumb.GardenTab), back);
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
            bedNameLines = new string[count][];
            bedStatus = new string[count];
            bedStates = new BedState[count];
            bedIcons = new uint[count];
        }

        var nowUtc = DateTime.UtcNow;
        for (var index = 0; index < count; index++)
        {
            var bed = patch.Beds[index];
            var state = StateOf(bed, nowUtc);
            bedNumbers[index] = (index + 1).ToString(CultureInfo.CurrentCulture);
            bedNames[index] = bed.IsPlanted ? catalog.NameOf(bed.Seed) : string.Empty;
            bedIcons[index] = bed.IsPlanted ? catalog.IconOf(bed.Seed) : 0;
            bedStates[index] = state;
            var progress = Progress(bed, nowUtc);
            bedStatus[index] = state switch
            {
                BedState.Empty => Loc.T(L.Greenthumb.Plant),
                BedState.Growing when progress >= 0f => Percent(progress),
                _ => StateLabel(state),
            };
        }

        bedNameWrapWidth = 0f;
        RebuildCrossPairs(patch);
        patchBuiltId = patch.Id;
        patchBuiltVersion = gardenVersion;
    }

    private void RebuildCrossPairs(GardenPatch patch)
    {
        crossPairCount = 0;
        crossPairWrapWidth = 0f;
        if (!GardenLayout.CanCross(patch.Kind) || catalog.State != GardenCatalogState.Ready)
        {
            return;
        }

        Span<int> neighbors = stackalloc int[GardenLayout.MaxNeighbors];
        for (var bed = 0; bed < patch.Beds.Count && crossPairCount < MaxCrossPairs; bed++)
        {
            if (!catalog.TryIndex(patch.Beds[bed].Seed, out var first))
            {
                continue;
            }

            var found = GardenLayout.Neighbors(patch.Kind, bed, neighbors);
            for (var index = 0; index < found && crossPairCount < MaxCrossPairs; index++)
            {
                var other = neighbors[index];
                if (other <= bed || !catalog.TryIndex(patch.Beds[other].Seed, out var second))
                {
                    continue;
                }

                var results = catalog.Book.CrossResults(first, second);
                if (results.Length == 0)
                {
                    continue;
                }

                var names = new string[results.Length];
                for (var resultIndex = 0; resultIndex < results.Length; resultIndex++)
                {
                    names[resultIndex] = catalog.Name(results[resultIndex]);
                }

                crossPairTitles[crossPairCount] = Loc.T(L.Greenthumb.CrossPairTitle, bed + 1, other + 1);
                crossPairBodies[crossPairCount] = Loc.T(L.Greenthumb.CrossPairBody, catalog.Name(first),
                    catalog.Name(second), string.Join(", ", names));
                crossPairCount++;
            }
        }
    }

    private string StateLabel(BedState state) => state switch
    {
        BedState.Empty => Loc.T(L.Greenthumb.EmptyBed),
        BedState.Ready => Loc.T(L.Greenthumb.Ready),
        BedState.TendSoon => Loc.T(L.Greenthumb.TendSoon),
        BedState.Wilting => Loc.T(L.Greenthumb.Wilting),
        _ => Loc.T(L.Greenthumb.Growing),
    };

    private int DrawBedGrid(GardenPatch patch)
    {
        var tapped = -1;
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
        WrapBedNames(cell - CellPaddingUnits * scale * 2f);
        for (var cellIndex = 0; cellIndex < columns * rows; cellIndex++)
        {
            var min = origin + new Vector2(cellIndex % columns * (cell + gap), cellIndex / columns * (cell + gap));
            var max = min + new Vector2(cell, cell);
            var bed = GardenLayout.BedAtCell(kind, cellIndex);
            if (bed < 0)
            {
                ProgressRing.CenterIcon(drawList, (min + max) * 0.5f, FontAwesomeIcon.Leaf,
                    Palette.WithAlpha(ui.Palette.Accent, CenterGlyphAlpha), cell * CenterGlyphFraction);
                continue;
            }

            if (DrawBedTile(drawList, bed, min, max, scale))
            {
                tapped = bed;
            }
        }

        ImGui.Dummy(new Vector2(width, cell * rows + gap * (rows - 1)));
        return tapped;
    }

    private bool DrawBedTile(ImDrawListPtr drawList, int bed, Vector2 min, Vector2 max, float scale)
    {
        var hovered = UiInteract.Hover(min, max);
        var radius = CellRadiusUnits * scale;
        var state = bedStates[bed];
        var empty = state == BedState.Empty;
        var fill = empty ? Palette.WithAlpha(theme.GroupedCard, 0.5f) : theme.GroupedCard;
        if (hovered)
        {
            fill = Palette.Mix(fill, theme.TextStrong, CardHoverMix);
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        Squircle.Fill(drawList, min, max, radius, ImGui.GetColorU32(fill));
        var stroke = state switch
        {
            BedState.Empty => Palette.WithAlpha(theme.TextStrong, EmptyStrokeAlpha),
            BedState.Growing => Palette.WithAlpha(theme.TextStrong, EmptyStrokeAlpha * 0.5f),
            _ => StateInk(state),
        };
        Squircle.Stroke(drawList, min, max, radius, ImGui.GetColorU32(stroke),
            (state is BedState.Empty or BedState.Growing ? Metrics.Stroke.Hairline : StateStrokeUnits) * scale);
        var pad = CellPaddingUnits * scale;
        var inner = max.X - min.X - pad * 2f;
        var centerX = (min.X + max.X) * 0.5f;
        Typography.Draw(drawList, min + new Vector2(pad, pad), bedNumbers[bed], theme.TextMuted, TextStyles.Footnote);
        var statusHeight = Typography.LineHeight(TextStyles.FootnoteEmphasized);
        var statusCenter = new Vector2(centerX, max.Y - pad - statusHeight * 0.5f);
        var statusInk = state == BedState.Growing ? theme.TextMuted : StateInk(state);
        if (empty)
        {
            PhoneIcon.Draw(drawList, new Vector2(centerX, (min.Y + max.Y) * 0.5f - statusHeight * 0.3f),
                PhoneIcons.Plus, theme.TextMuted, CellPlusUnits * scale);
            Typography.DrawCentered(drawList, statusCenter, bedStatus[bed], theme.TextMuted,
                TextStyles.FootnoteEmphasized);
            return UiInteract.Click(min, max, hovered);
        }

        var nameHeight = Typography.LineHeight(TextStyles.Caption1);
        var lines = bedNameLines[bed];
        var iconSize = (max.Y - min.Y) * CellIconFraction;
        var numberBottom = min.Y + pad + Typography.LineHeight(TextStyles.Footnote);
        var nameTop = statusCenter.Y - statusHeight * 0.5f - nameHeight * lines.Length;
        var iconCenter = new Vector2(centerX, (numberBottom + nameTop) * 0.5f);
        DrawSeedIcon(drawList, bedIcons[bed], iconCenter, MathF.Min(iconSize, nameTop - numberBottom));
        for (var line = 0; line < lines.Length; line++)
        {
            Typography.DrawCentered(drawList, new Vector2(centerX, nameTop + nameHeight * (line + 0.5f)), lines[line],
                theme.TextStrong, TextStyles.Caption1);
        }
        Typography.DrawCentered(drawList, statusCenter,
            Typography.FitText(bedStatus[bed], inner, TextStyles.FootnoteEmphasized), statusInk,
            TextStyles.FootnoteEmphasized);
        return UiInteract.Click(min, max, hovered);
    }

    private void WrapBedNames(float width)
    {
        if (MathF.Abs(bedNameWrapWidth - width) <= 0.5f)
        {
            return;
        }

        bedNameWrapWidth = width;
        for (var index = 0; index < bedNames.Length; index++)
        {
            var lines = Typography.WrapText(bedNames[index], TextStyles.Caption1, width);
            if (lines.Length > MaxNameLines)
            {
                var last = Typography.FitText(string.Concat(lines[MaxNameLines - 1], " ", lines[MaxNameLines]), width,
                    TextStyles.Caption1);
                lines = new[] { lines[0], last };
            }

            bedNameLines[index] = lines;
        }
    }

    private void DrawCrossPairs()
    {
        if (crossPairCount == 0)
        {
            return;
        }

        var scale = UiScale.Current;
        var width = ScrollLayout.StableContentWidth();
        var pad = CardPadUnits * scale;
        var iconColumn = PairIconColumnUnits * scale;
        var textWidth = width - pad * 2f - iconColumn;
        if (MathF.Abs(crossPairWrapWidth - textWidth) > 0.5f)
        {
            crossPairWrapWidth = textWidth;
            for (var index = 0; index < crossPairCount; index++)
            {
                crossPairLines[index] = Typography.WrapText(crossPairBodies[index], TextStyles.Footnote, textWidth);
            }
        }

        SettingsSection.Header(Loc.T(L.Greenthumb.CrossSection), theme);
        var drawList = ImGui.GetWindowDrawList();
        var titleHeight = Typography.LineHeight(TextStyles.SubheadlineEmphasized);
        var lineHeight = Typography.LineHeight(TextStyles.Footnote);
        for (var index = 0; index < crossPairCount; index++)
        {
            var lines = crossPairLines[index];
            var height = pad * 2f + titleHeight + lineHeight * lines.Length;
            var min = ImGui.GetCursorScreenPos();
            var max = min + new Vector2(width, height);
            GroupCard.DrawSurface(drawList, min, max, theme, scale);
            var iconCenter = new Vector2(min.X + pad + iconColumn * 0.35f, min.Y + pad + titleHeight * 0.5f);
            ProgressRing.CenterIcon(drawList, iconCenter, FontAwesomeIcon.CodeBranch, ui.Palette.Accent,
                PairIconUnits * scale);
            var textLeft = min.X + pad + iconColumn;
            Typography.Draw(drawList, new Vector2(textLeft, min.Y + pad), crossPairTitles[index], theme.TextStrong,
                TextStyles.SubheadlineEmphasized);
            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                Typography.Draw(drawList, new Vector2(textLeft, min.Y + pad + titleHeight + lineIndex * lineHeight),
                    lines[lineIndex], theme.TextMuted, TextStyles.Footnote);
            }

            ImGui.Dummy(new Vector2(width, height));
            Gap(Metrics.Space.Sm);
        }

        Gap(Metrics.Space.Md);
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
}
