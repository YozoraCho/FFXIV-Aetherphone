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
    private const float LegendRowUnits = 56f;
    private const float LegendIconUnits = 30f;
    private const float BadgeUnits = 22f;
    private const float LayoutFillAlpha = 0.16f;
    private const float PlantButtonUnits = 48f;
    private const string ParentA = "A";
    private const string ParentB = "B";

    private static readonly string[] LayoutBedNumbers = { "1", "2", "3", "4", "5", "6", "7", "8" };

    private readonly ChipRail layoutRail = new();
    private readonly List<int> layoutPatches = new();
    private string[] layoutPatchLabels = Array.Empty<string>();
    private bool[] layoutPatchActive = Array.Empty<bool>();
    private PlanStep layoutStep;
    private int layoutPatch;
    private int layoutBuiltVersion = -1;
    private string layoutFirstName = string.Empty;
    private string layoutSecondName = string.Empty;
    private string layoutResult = string.Empty;
    private string[] layoutHintLines = Array.Empty<string>();
    private float layoutHintWidth;

    private void OpenLayout(PlanStep step)
    {
        layoutStep = step;
        layoutBuiltVersion = -1;
        router.Push(GreenthumbRoute.LayoutPage);
    }

    private void DrawLayout(in PhoneContext context)
    {
        RebuildLayoutText();
        var navBar = AppHeader.BeginLargeTitle(context);
        using (AppSurface.Begin(navBar.Body))
        {
            DrawLayoutLegend();
            Gap(Metrics.Space.Lg);
            if (layoutPatches.Count == 0)
            {
                SettingsSection.Hint(Loc.T(configuration.GreenthumbPatches.Count == 0
                    ? L.Greenthumb.NoPatchesHint
                    : L.Greenthumb.NoCrossPot), theme);
                Gap(Metrics.Space.Md);
                var addCard = GroupCard.Begin(theme, 1);
                if (SettingsRow.Action(addCard.NextRow(), Loc.T(L.Greenthumb.AddPatch), ui.Palette.Accent, theme))
                {
                    OpenKindSheet();
                }

                addCard.End();
            }
            else
            {
                DrawLayoutPatch();
            }

            Gap(BottomBreathing);
        }

        AppHeader.EndLargeTitle(in navBar, context, "greenthumb.layout.nav", Loc.T(L.Greenthumb.HowToPlant),
            NavBarStyle.From(ui), ReadOnlySpan<NavBarButton>.Empty, Loc.T(L.Greenthumb.PlannerTab), back);
    }

    private void DrawLayoutLegend()
    {
        var card = GroupCard.Begin(theme, 2, LegendRowUnits);
        DrawLegendRow(card.NextRow(), ParentA, layoutStep.First, layoutFirstName, ui.Palette.Accent);
        DrawLegendRow(card.NextRow(), ParentB, layoutStep.Second, layoutSecondName, TendInk);
        card.End();
        SettingsSection.Hint(layoutResult, theme);
    }

    private void DrawLegendRow(Rect row, string badge, int seed, string name, Vector4 ink)
    {
        var scale = UiScale.Current;
        var drawList = ImGui.GetWindowDrawList();
        var badgeSize = BadgeUnits * scale;
        var badgeCenter = new Vector2(row.Min.X + badgeSize * 0.5f, row.Center.Y);
        DrawParentBadge(drawList, badgeCenter, badgeSize, badge, ink);
        var iconSize = LegendIconUnits * scale;
        var iconCenter = new Vector2(row.Min.X + badgeSize + Metrics.Space.Md * scale + iconSize * 0.5f, row.Center.Y);
        DrawSeedIcon(drawList, catalog.Icon(seed), iconCenter, iconSize);
        var textLeft = iconCenter.X + iconSize * 0.5f + Metrics.Space.Md * scale;
        var height = Typography.LineHeight(TextStyles.BodyEmphasized);
        Typography.Draw(drawList, new Vector2(textLeft, row.Center.Y - height * 0.5f),
            Typography.FitText(name, row.Max.X - textLeft, TextStyles.BodyEmphasized), theme.TextStrong,
            TextStyles.BodyEmphasized);
    }

    private void DrawParentBadge(ImDrawListPtr drawList, Vector2 center, float size, string badge, Vector4 ink)
    {
        ProgressRing.Disc(center, size * 0.5f, ink);
        Typography.DrawCentered(drawList, center, badge, theme.AppBackground, TextStyles.FootnoteEmphasized);
    }

    private void DrawLayoutPatch()
    {
        layoutPatch = Math.Clamp(layoutPatch, 0, layoutPatches.Count - 1);
        if (layoutPatches.Count > 1)
        {
            for (var index = 0; index < layoutPatchActive.Length; index++)
            {
                layoutPatchActive[index] = index == layoutPatch;
            }

            var picked = layoutRail.Draw(ui, layoutPatchLabels, layoutPatchActive, "greenthumb.layout.patches");
            if (picked >= 0 && picked < layoutPatches.Count)
            {
                layoutPatch = picked;
            }

            Gap(Metrics.Space.Md);
        }

        var patch = configuration.GreenthumbPatches[layoutPatches[layoutPatch]];
        patch.EnsureBeds();
        DrawLayoutGrid(patch.Kind);
        Gap(Metrics.Space.Md);
        DrawLayoutHint();
        Gap(Metrics.Space.Lg);
        var scale = UiScale.Current;
        var origin = ImGui.GetCursorScreenPos();
        var width = ScrollLayout.StableContentWidth();
        var button = new Rect(origin, origin + new Vector2(width, PlantButtonUnits * scale));
        if (ui.AccentPill(button, Loc.T(L.Greenthumb.PlantLayout), true, TextStyles.Headline))
        {
            AskPlantLayout(patch);
        }

        ImGui.Dummy(button.Size);
    }

    private void DrawLayoutGrid(GardenPatchKind kind)
    {
        var scale = UiScale.Current;
        var columns = GardenLayout.Columns(kind);
        var rows = GardenLayout.Rows(kind);
        var width = ScrollLayout.StableContentWidth();
        var gap = CellGapUnits * scale;
        var cell = MathF.Min((width - gap * (columns - 1)) / columns, CellMaxUnits * scale);
        var gridWidth = cell * columns + gap * (columns - 1);
        var origin = ImGui.GetCursorScreenPos();
        origin.X += (width - gridWidth) * 0.5f;
        var drawList = ImGui.GetWindowDrawList();
        var radius = CellRadiusUnits * scale;
        var pad = CellPaddingUnits * scale;
        var badgeSize = BadgeUnits * scale;
        for (var cellIndex = 0; cellIndex < columns * rows; cellIndex++)
        {
            var min = origin + new Vector2(cellIndex % columns * (cell + gap), cellIndex / columns * (cell + gap));
            var max = min + new Vector2(cell, cell);
            var bed = GardenLayout.BedAtCell(kind, cellIndex);
            if (bed < 0)
            {
                continue;
            }

            var parent = GardenLayout.LayoutParent(kind, bed);
            var ink = parent == 0 ? ui.Palette.Accent : TendInk;
            var seed = parent == 0 ? layoutStep.First : layoutStep.Second;
            Squircle.Fill(drawList, min, max, radius, ImGui.GetColorU32(Palette.WithAlpha(ink, LayoutFillAlpha)));
            Squircle.Stroke(drawList, min, max, radius, ImGui.GetColorU32(ink), StateStrokeUnits * scale);
            Typography.Draw(drawList, min + new Vector2(pad, pad), LayoutBedNumbers[bed], theme.TextMuted,
                TextStyles.Footnote);
            DrawParentBadge(drawList, new Vector2(max.X - pad - badgeSize * 0.5f, min.Y + pad + badgeSize * 0.5f),
                badgeSize, parent == 0 ? ParentA : ParentB, ink);
            DrawSeedIcon(drawList, catalog.Icon(seed), new Vector2((min.X + max.X) * 0.5f, min.Y + (max.Y - min.Y) * 0.58f),
                cell * CellIconFraction * 1.2f);
        }

        ImGui.Dummy(new Vector2(width, cell * rows + gap * (rows - 1)));
    }

    private void DrawLayoutHint()
    {
        var width = ScrollLayout.StableContentWidth();
        if (MathF.Abs(layoutHintWidth - width) > 0.5f)
        {
            layoutHintWidth = width;
            layoutHintLines = Typography.WrapText(Loc.T(L.Greenthumb.LayoutHint), TextStyles.Footnote, width);
        }

        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var lineHeight = Typography.LineHeight(TextStyles.Footnote);
        for (var index = 0; index < layoutHintLines.Length; index++)
        {
            Typography.Draw(drawList, origin + new Vector2(0f, index * lineHeight), layoutHintLines[index],
                theme.TextMuted, TextStyles.Footnote);
        }

        ImGui.Dummy(new Vector2(width, lineHeight * layoutHintLines.Length));
    }

    private void RebuildLayoutText()
    {
        if (layoutBuiltVersion == gardenVersion)
        {
            return;
        }

        layoutBuiltVersion = gardenVersion;
        layoutHintWidth = 0f;
        layoutFirstName = catalog.Name(layoutStep.First);
        layoutSecondName = catalog.Name(layoutStep.Second);
        layoutResult = Loc.T(L.Greenthumb.Gives, catalog.Name(layoutStep.Seed));
        layoutPatches.Clear();
        var patches = configuration.GreenthumbPatches;
        for (var index = 0; index < patches.Count; index++)
        {
            if (GardenLayout.CanCross(patches[index].Kind))
            {
                layoutPatches.Add(index);
            }
        }

        if (layoutPatchLabels.Length != layoutPatches.Count)
        {
            layoutPatchLabels = new string[layoutPatches.Count];
            layoutPatchActive = new bool[layoutPatches.Count];
        }

        for (var index = 0; index < layoutPatches.Count; index++)
        {
            layoutPatchLabels[index] = PatchLabel.For(patches[layoutPatches[index]]);
        }
    }

    private void AskPlantLayout(GardenPatch patch)
    {
        var id = patch.Id;
        var first = layoutStep.First;
        var second = layoutStep.Second;
        confirm.Ask(new ConfirmRequest
        {
            Message = Loc.T(L.Greenthumb.LayoutConfirm, layoutFirstName, layoutSecondName, PatchLabel.For(patch)),
            ConfirmLabel = Loc.T(L.Greenthumb.Plant),
            CancelLabel = Loc.T(L.Common.Cancel),
            Sheet = true,
            Confirm = () => PlantLayout(id, first, second),
        });
    }

    private void PlantLayout(Guid patchId, int first, int second)
    {
        var patch = FindPatch(patchId);
        if (patch is null)
        {
            return;
        }

        patch.EnsureBeds();
        var nowUtc = DateTime.UtcNow;
        for (var bed = 0; bed < patch.Beds.Count; bed++)
        {
            var seed = catalog.Book.Seeds[GardenLayout.LayoutParent(patch.Kind, bed) == 0 ? first : second];
            patch.Beds[bed].Plant(seed.Key, seed.GrowHours, nowUtc);
        }

        SaveGarden();
        gardenVersion++;
        router.Reset();
        activeTab = GreenthumbTab.Garden;
        router.Push(GreenthumbRoute.Patch(patchId));
    }
}
