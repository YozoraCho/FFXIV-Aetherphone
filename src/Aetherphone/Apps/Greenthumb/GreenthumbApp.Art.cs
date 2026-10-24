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
    private const float IconRadiusFraction = 0.22f;
    private const float IconGlyphFraction = 0.62f;
    private const float TrackAlpha = 0.14f;
    private const float EmptyCellAlpha = 0.07f;
    private const float GrowingCellAlpha = 0.32f;
    private const float StateStrokeUnits = 1.6f;
    private const float CardHoverMix = 0.05f;
    private const float ChevronUnits = 14f;

    private static readonly Vector4 TendInk = Aetherphone.Core.Theme.Accent.Amber;

    private enum BedState : byte
    {
        Empty,
        Growing,
        TendSoon,
        Wilting,
        Ready,
    }

    private enum StatusTone : byte
    {
        Muted,
        Good,
        Warn,
        Bad,
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

    private Vector4 StateInk(BedState state) => state switch
    {
        BedState.Ready => ui.Palette.Accent,
        BedState.TendSoon => TendInk,
        BedState.Wilting => theme.Danger,
        _ => theme.TextMuted,
    };

    private Vector4 ToneInk(StatusTone tone) => tone switch
    {
        StatusTone.Good => ui.Palette.Accent,
        StatusTone.Warn => TendInk,
        StatusTone.Bad => theme.Danger,
        _ => theme.TextMuted,
    };

    private Vector4 CellFill(BedState state) => state switch
    {
        BedState.Empty => Palette.WithAlpha(theme.TextStrong, EmptyCellAlpha),
        BedState.Ready => ui.Palette.Accent,
        BedState.TendSoon => TendInk,
        BedState.Wilting => theme.Danger,
        _ => Palette.WithAlpha(ui.Palette.Accent, GrowingCellAlpha),
    };

    private void DrawSeedIcon(ImDrawListPtr drawList, uint icon, Vector2 center, float size)
    {
        var half = size * 0.5f;
        var min = center - new Vector2(half, half);
        var max = center + new Vector2(half, half);
        if (icon != 0 && GameIconTile.Draw(drawList, Plugin.TextureProvider, icon, min, max,
                size * IconRadiusFraction, UiScale.Current, requireIcon: true))
        {
            return;
        }

        ProgressRing.CenterIcon(drawList, center, FontAwesomeIcon.Seedling, ui.Palette.Accent,
            size * IconGlyphFraction);
    }

    private void DrawProgressBar(ImDrawListPtr drawList, Vector2 min, Vector2 max, float fraction, Vector4 fill)
    {
        var radius = (max.Y - min.Y) * 0.5f;
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(Palette.WithAlpha(theme.TextStrong, TrackAlpha)), radius);
        var clamped = Math.Clamp(fraction, 0f, 1f);
        if (clamped <= 0f)
        {
            return;
        }

        var width = MathF.Max((max.X - min.X) * clamped, radius * 2f);
        drawList.AddRectFilled(min, new Vector2(min.X + width, max.Y), ImGui.GetColorU32(fill), radius);
    }

    private bool DrawCardButton(ImDrawListPtr drawList, Vector2 min, Vector2 max, float scale)
    {
        var hovered = UiInteract.Hover(min, max);
        GroupCard.DrawSurface(drawList, min, max, theme, scale);
        if (hovered)
        {
            Squircle.Fill(drawList, min, max, Metrics.Radius.Grouped * scale,
                ImGui.GetColorU32(Palette.WithAlpha(theme.TextStrong, CardHoverMix)));
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        return UiInteract.Click(min, max, hovered);
    }

    private void DrawChevron(ImDrawListPtr drawList, Vector2 center, float scale) =>
        PhoneIcon.Draw(drawList, center, PhoneIcons.ChevronRight, theme.TextMuted, ChevronUnits * scale);

    private static float Progress(GardenBed bed, DateTime nowUtc)
    {
        if (!bed.IsPlanted || bed.GrowHours <= 0)
        {
            return -1f;
        }

        var elapsed = (nowUtc - bed.PlantedAtUtc!.Value).TotalHours;
        return (float)Math.Clamp(elapsed / bed.GrowHours, 0d, 1d);
    }

    private static string Percent(float fraction) =>
        string.Concat(((int)MathF.Round(fraction * 100f)).ToString(Loc.Culture), "%");
}
