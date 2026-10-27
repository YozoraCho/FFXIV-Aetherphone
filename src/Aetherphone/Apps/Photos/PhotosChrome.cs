using Aetherphone.Core;
using Aetherphone.Core.Theme;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;

namespace Aetherphone.Apps.Photos;

internal static class PhotosChrome
{
    public const float BadgeRadius = 13f;

    private const float GlassDiscAlpha = 0.30f;
    private const float GlassDiscHoverAlpha = 0.48f;
    private const float CoverGlyph = 30f;
    private const float NewTileDiscRadius = 22f;
    private const float NewTileGlyph = 24f;
    private const float NewTileLabelGap = 9f;
    private const float BadgeGlyph = 16f;
    private const float FavoriteBadgeGlyph = 13f;
    private const float FavoriteBadgeInset = 7f;
    private const float GifBadgePadX = 5f;
    private const float GifBadgePadY = 1.5f;
    private const float SelectionGlyph = 22f;
    private const float SelectionInset = 6f;
    private const float SelectionVeilAlpha = 0.22f;
    private const float SelectionRingInset = 2f;
    private const float SelectionRingStroke = 2.5f;
    private const float ShadowAlpha = 0.45f;
    private const float IdleMarkAlpha = 0.9f;
    private const float TileCaptionHeight = 30f;
    private const float TileCaptionCenter = 0.42f;
    private const float FaintAlpha = 0.45f;
    private const float AccentWashAlpha = 0.16f;

    private static readonly Vector4 HoverWash = new(1f, 1f, 1f, 0.10f);
    private static readonly Vector4 ScrimInk = new(0f, 0f, 0f, 0.52f);
    private static readonly Vector4 Clear = new(0f, 0f, 0f, 0f);
    private static readonly Vector4 Black = new(0f, 0f, 0f, 1f);
    private static readonly Vector4 White = new(1f, 1f, 1f, 1f);

    public static Vector4 Placeholder(AppSkin ui) => ui.Palette.CardFill;

    public static void Thumbnail(ImDrawListPtr drawList, IDalamudTextureWrap? texture, Vector2 min, Vector2 max,
        bool hovered, Vector4 placeholder, bool fit = false)
    {
        if (texture is null)
        {
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(placeholder));
        }
        else if (fit)
        {
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(placeholder));
            var frame = ImageFit.CenteredRect(new Rect(min, max), texture.Size.X / MathF.Max(1f, texture.Size.Y));
            drawList.AddImage(texture.Handle, frame.Min, frame.Max);
        }
        else
        {
            var (uv0, uv1) = ImageFit.Cover(texture.Size.X, texture.Size.Y, max.X - min.X, max.Y - min.Y);
            drawList.AddImage(texture.Handle, min, max, uv0, uv1);
        }

        if (hovered)
        {
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(HoverWash));
        }
    }

    public static void Cover(ImDrawListPtr drawList, IDalamudTextureWrap? texture, Vector2 min, Vector2 max,
        float rounding, AppSkin ui, float scale, bool hovered)
    {
        if (texture is null)
        {
            Squircle.Fill(drawList, min, max, rounding, ImGui.GetColorU32(Placeholder(ui)));
            PhoneIcon.Draw(drawList, (min + max) * 0.5f, PhoneIcons.Photo,
                Palette.WithAlpha(ui.MutedInk, FaintAlpha), CoverGlyph * scale);
        }
        else
        {
            var (uv0, uv1) = ImageFit.Cover(texture.Size.X, texture.Size.Y, max.X - min.X, max.Y - min.Y);
            Squircle.FillImage(drawList, min, max, rounding, texture.Handle, 0xFFFFFFFFu, uv0, uv1);
        }

        if (hovered)
        {
            Squircle.Fill(drawList, min, max, rounding, ImGui.GetColorU32(HoverWash));
        }
    }

    public static bool NewAlbumTile(ImDrawListPtr drawList, Vector2 min, Vector2 max, float rounding, string label,
        AppSkin ui, float scale)
    {
        var hovered = UiInteract.Hover(min, max);
        var down = hovered && ImGui.IsMouseDown(ImGuiMouseButton.Left);
        var press = PressFx.Scale(ImGui.GetID("photos.newAlbumTile"), down, PressFx.CardPressedScale);
        var center = (min + max) * 0.5f;
        var half = (max - min) * 0.5f * press;
        var drawnMin = center - half;
        var drawnMax = center + half;
        Squircle.Fill(drawList, drawnMin, drawnMax, rounding, ImGui.GetColorU32(Placeholder(ui)));
        if (hovered)
        {
            Squircle.Fill(drawList, drawnMin, drawnMax, rounding, ImGui.GetColorU32(ui.HoverTint));
        }

        var discRadius = NewTileDiscRadius * scale;
        var labelHeight = Typography.LineHeight(TextStyles.SubheadlineEmphasized);
        var stackHeight = discRadius * 2f + NewTileLabelGap * scale + labelHeight;
        var discCenter = new Vector2(center.X, center.Y - stackHeight * 0.5f + discRadius);
        drawList.AddCircleFilled(discCenter, discRadius, ImGui.GetColorU32(Palette.WithAlpha(ui.Accent, AccentWashAlpha)),
            32);
        PhoneIcon.Draw(drawList, discCenter, PhoneIcons.Plus, ui.Accent, NewTileGlyph * scale);
        var fitted = Typography.FitText(label, max.X - min.X - Metrics.Space.Lg * scale, TextStyles.SubheadlineEmphasized);
        Typography.DrawCentered(drawList,
            new Vector2(center.X, discCenter.Y + discRadius + NewTileLabelGap * scale + labelHeight * 0.5f), fitted,
            ui.Accent, TextStyles.SubheadlineEmphasized);
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        return UiInteract.Click(min, max, hovered);
    }

    public static bool CoverBadge(ImDrawListPtr drawList, Vector2 center, string tooltip, float scale)
    {
        var radius = BadgeRadius * scale;
        var extent = new Vector2(radius, radius);
        var hovered = UiInteract.Hover(center - extent, center + extent);
        drawList.AddCircleFilled(center, radius,
            ImGui.GetColorU32(Palette.WithAlpha(Black, hovered ? GlassDiscHoverAlpha : GlassDiscAlpha)), 24);
        PhoneIcon.Draw(drawList, center, PhoneIcons.Dots, White, BadgeGlyph * scale);
        HoverTooltip.Show(new Rect(center - extent, center + extent), tooltip, HoverLabelSide.Below);
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        return UiInteract.Click(center - extent, center + extent, hovered);
    }

    public static void TileCaption(ImDrawListPtr drawList, Vector2 min, Vector2 max, string text, float scale)
    {
        var height = TileCaptionHeight * scale;
        var solid = ImGui.GetColorU32(ScrimInk);
        var clear = ImGui.GetColorU32(Clear);
        drawList.AddRectFilledMultiColor(new Vector2(min.X, max.Y - height), max, clear, clear, solid, solid);
        var fitted = Typography.FitText(text, max.X - min.X - Metrics.Space.Sm * scale, TextStyles.FootnoteEmphasized);
        Typography.DrawCentered(drawList, new Vector2((min.X + max.X) * 0.5f, max.Y - height * TileCaptionCenter),
            fitted, White, TextStyles.FootnoteEmphasized);
    }

    public static void FavoriteBadge(ImDrawListPtr drawList, Vector2 min, Vector2 max, float scale)
    {
        var glyph = FavoriteBadgeGlyph * scale;
        var inset = FavoriteBadgeInset * scale;
        var center = new Vector2(min.X + inset + glyph * 0.5f, max.Y - inset - glyph * 0.5f);
        PhoneIcon.Draw(drawList, center + new Vector2(0f, 1f * scale), PhoneIcons.HeartFilled,
            Palette.WithAlpha(Black, ShadowAlpha), glyph);
        PhoneIcon.Draw(drawList, center, PhoneIcons.HeartFilled, White, glyph);
    }

    public static void GifBadge(ImDrawListPtr drawList, Vector2 min, Vector2 max, string label, float scale)
    {
        var size = Typography.Measure(label, TextStyles.Caption2);
        var padX = GifBadgePadX * scale;
        var padY = GifBadgePadY * scale;
        var inset = FavoriteBadgeInset * scale;
        var badgeMax = new Vector2(max.X - inset, max.Y - inset);
        var badgeMin = badgeMax - size - new Vector2(padX * 2f, padY * 2f);
        drawList.AddRectFilled(badgeMin, badgeMax, ImGui.GetColorU32(ScrimInk), (badgeMax.Y - badgeMin.Y) * 0.5f);
        Typography.Draw(drawList, badgeMin + new Vector2(padX, padY), label, White, TextStyles.Caption2);
    }

    public static void SelectionMark(ImDrawListPtr drawList, Vector2 min, Vector2 max, bool selected, Vector4 accent,
        float scale)
    {
        var glyph = SelectionGlyph * scale;
        var inset = SelectionInset * scale;
        var center = new Vector2(max.X - inset - glyph * 0.5f, max.Y - inset - glyph * 0.5f);
        if (!selected)
        {
            PhoneIcon.Draw(drawList, center + new Vector2(0f, 1f * scale), PhoneIcons.Circle,
                Palette.WithAlpha(Black, ShadowAlpha), glyph);
            PhoneIcon.Draw(drawList, center, PhoneIcons.Circle, Palette.WithAlpha(White, IdleMarkAlpha), glyph);
            return;
        }

        drawList.AddRectFilled(min, max, ImGui.GetColorU32(Palette.WithAlpha(White, SelectionVeilAlpha)));
        var ringInset = new Vector2(SelectionRingInset * scale, SelectionRingInset * scale);
        drawList.AddRect(min + ringInset, max - ringInset, ImGui.GetColorU32(accent), 0f, ImDrawFlags.None,
            SelectionRingStroke * scale);
        drawList.AddCircleFilled(center, glyph * 0.5f, ImGui.GetColorU32(White), 24);
        PhoneIcon.Draw(drawList, center, PhoneIcons.CircleCheckFilled, accent, glyph);
    }

    public static void TopScrim(ImDrawListPtr drawList, Vector2 min, Vector2 max, float height, float alpha = 1f)
    {
        var solid = ImGui.GetColorU32(ScrimInk with { W = ScrimInk.W * alpha });
        var clear = ImGui.GetColorU32(Clear);
        drawList.AddRectFilledMultiColor(min, new Vector2(max.X, min.Y + height), solid, solid, clear, clear);
    }

    public static void BottomScrim(ImDrawListPtr drawList, Vector2 min, Vector2 max, float height, float alpha = 1f)
    {
        var solid = ImGui.GetColorU32(ScrimInk with { W = ScrimInk.W * alpha });
        var clear = ImGui.GetColorU32(Clear);
        drawList.AddRectFilledMultiColor(new Vector2(min.X, max.Y - height), max, clear, clear, solid, solid);
    }
}
