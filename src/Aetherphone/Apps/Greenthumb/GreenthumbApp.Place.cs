using Aetherphone.Core;
using Aetherphone.Core.Apps;
using Aetherphone.Core.Gardening;
using Aetherphone.Core.Localization;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Apps.Greenthumb;

internal sealed partial class GreenthumbApp
{
    private const float PlaceIconUnits = 44f;
    private const float BannerHeightUnits = 56f;

    private readonly ChipRail placeRail = new();
    private string[] placePatchLabels = Array.Empty<string>();
    private bool[] placePatchActive = Array.Empty<bool>();
    private int placePatch;
    private int placeBuiltInbox = -1;
    private int placeBuiltGarden = -1;
    private int bannerBuiltInbox = -1;
    private string bannerText = string.Empty;
    private string placeTitle = string.Empty;
    private string placeAgo = string.Empty;
    private uint placeIcon;

    private void DrawPlaceBanner(ImDrawListPtr drawList, Vector2 origin, float width, float scale, ref float y)
    {
        var count = inbox.Count;
        if (count == 0)
        {
            return;
        }

        if (bannerBuiltInbox != inbox.Version)
        {
            bannerBuiltInbox = inbox.Version;
            bannerText = Loc.Plural(L.Greenthumb.PendingBanner, count);
        }

        var min = new Vector2(origin.X, y);
        var max = new Vector2(origin.X + width, y + BannerHeightUnits * scale);
        if (DrawCardButton(drawList, min, max, scale))
        {
            router.Push(GreenthumbRoute.PlacePage);
        }

        var pad = CardPadUnits * scale;
        var center = (min.Y + max.Y) * 0.5f;
        var height = Typography.LineHeight(TextStyles.BodyEmphasized);
        Typography.Draw(drawList, new Vector2(min.X + pad, center - height * 0.5f),
            Typography.FitText(bannerText, width - pad * 3f - ChevronUnits * scale, TextStyles.BodyEmphasized),
            ui.Palette.Accent, TextStyles.BodyEmphasized);
        DrawChevron(drawList, new Vector2(max.X - pad - ChevronUnits * scale * 0.5f, center), scale);
        y = max.Y + Metrics.Space.Lg * scale;
    }

    private void DrawPlace(in PhoneContext context)
    {
        if (!inbox.TryPeek(out var signal))
        {
            router.Pop(false);
            return;
        }

        RebuildPlaceText(signal);
        var navBar = AppHeader.BeginLargeTitle(context);
        using (AppSurface.Begin(navBar.Body))
        {
            DrawPlaceHero();
            Gap(Metrics.Space.Lg);
            var patches = configuration.GreenthumbPatches;
            if (patches.Count == 0)
            {
                SettingsSection.Hint(Loc.T(L.Greenthumb.NoPatchesHint), theme);
            }
            else
            {
                DrawPlacePatches(patches, signal);
            }

            Gap(Metrics.Space.Lg);
            var card = GroupCard.Begin(theme, 1);
            if (SettingsRow.Action(card.NextRow(), Loc.T(L.Greenthumb.Skip), theme.TextMuted, theme))
            {
                inbox.RemoveFirst();
            }

            card.End();
            Gap(BottomBreathing);
        }

        AppHeader.EndLargeTitle(in navBar, context, "greenthumb.place.nav", Loc.T(L.Greenthumb.PlaceTitle),
            NavBarStyle.From(ui), ReadOnlySpan<NavBarButton>.Empty, Loc.T(L.Greenthumb.GardenTab), back);
    }

    private void DrawPlacePatches(List<GardenPatch> patches, GardenSignal signal)
    {
        placePatch = Math.Clamp(placePatch, 0, patches.Count - 1);
        if (patches.Count > 1)
        {
            for (var index = 0; index < placePatchActive.Length; index++)
            {
                placePatchActive[index] = index == placePatch;
            }

            var picked = placeRail.Draw(ui, placePatchLabels, placePatchActive, "greenthumb.place.patches");
            if (picked >= 0 && picked < patches.Count)
            {
                placePatch = picked;
            }

            Gap(Metrics.Space.Md);
        }

        SettingsSection.Hint(Loc.T(L.Greenthumb.PlaceHint), theme);
        Gap(Metrics.Space.Sm);
        var patch = patches[placePatch];
        patch.EnsureBeds();
        RebuildPatchText(patch);
        var bed = DrawBedGrid(patch);
        if (bed >= 0)
        {
            ApplySignal(signal, patch, bed);
        }
    }

    private void DrawPlaceHero()
    {
        var scale = UiScale.Current;
        var drawList = ImGui.GetWindowDrawList();
        var width = ScrollLayout.StableContentWidth();
        var pad = CardPadUnits * scale;
        var iconSize = PlaceIconUnits * scale;
        var titleHeight = Typography.LineHeight(TextStyles.Headline);
        var lineHeight = Typography.LineHeight(TextStyles.Footnote);
        var textHeight = titleHeight + lineHeight;
        var height = pad * 2f + MathF.Max(iconSize, textHeight);
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(width, height);
        GroupCard.DrawSurface(drawList, min, max, theme, scale);
        DrawSeedIcon(drawList, placeIcon, new Vector2(min.X + pad + iconSize * 0.5f, (min.Y + max.Y) * 0.5f),
            iconSize);
        var textLeft = min.X + pad * 2f + iconSize;
        var textWidth = max.X - pad - textLeft;
        var top = (min.Y + max.Y - textHeight) * 0.5f;
        Typography.Draw(drawList, new Vector2(textLeft, top),
            Typography.FitText(placeTitle, textWidth, TextStyles.Headline), theme.TextStrong, TextStyles.Headline);
        Typography.Draw(drawList, new Vector2(textLeft, top + titleHeight), placeAgo, theme.TextMuted,
            TextStyles.Footnote);
        ImGui.Dummy(new Vector2(width, height));
    }

    private void RebuildPlaceText(GardenSignal signal)
    {
        if (placeBuiltInbox == inbox.Version && placeBuiltGarden == gardenVersion)
        {
            return;
        }

        placeBuiltInbox = inbox.Version;
        placeBuiltGarden = gardenVersion;
        placeTitle = tracker.ActionLabel(signal);
        placeAgo = TimeText.Ago(signal.Utc);
        placeIcon = signal.Seed >= 0 ? catalog.Icon(signal.Seed) : 0;
        var patches = configuration.GreenthumbPatches;
        if (placePatchLabels.Length != patches.Count)
        {
            placePatchLabels = new string[patches.Count];
            placePatchActive = new bool[patches.Count];
        }

        for (var index = 0; index < patches.Count; index++)
        {
            placePatchLabels[index] = PatchLabel.For(patches[index]);
        }
    }

    private void ApplySignal(GardenSignal signal, GardenPatch patch, int bedIndex)
    {
        var bed = patch.Beds[bedIndex];
        switch (signal.Kind)
        {
            case GardenActionKind.Plant when signal.Seed >= 0:
                var seed = catalog.Book.Seeds[signal.Seed];
                bed.Plant(seed.Key, seed.GrowHours, signal.Utc);
                break;
            case GardenActionKind.Harvest:
                inbox.RemoveFirst();
                RebuildCrossText(patch, bedIndex, bed);
                Harvest(bed);
                return;
            default:
                if (bed.IsPlanted)
                {
                    bed.Tend(signal.Utc);
                }

                break;
        }

        inbox.RemoveFirst();
        SaveGarden();
        gardenVersion++;
    }
}
