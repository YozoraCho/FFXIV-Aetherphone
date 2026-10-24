using Aetherphone.Core;
using Aetherphone.Core.Apps;
using Aetherphone.Core.Gardening;
using Aetherphone.Core.Localization;
using Aetherphone.Windows.Components;

namespace Aetherphone.Apps.Greenthumb;

internal sealed partial class GreenthumbApp
{
    private string scanResult = string.Empty;

    private void DrawSettings(in PhoneContext context)
    {
        var navBar = AppHeader.BeginLargeTitle(context);
        using (AppSurface.Begin(navBar.Body))
        {
            SettingsSection.Header(Loc.T(L.Greenthumb.AutoSection), theme);
            var card = GroupCard.Begin(theme, 2);
            var find = SettingsRow.Bool(card.NextRow(), Loc.T(L.Greenthumb.FindSeeds),
                configuration.GreenthumbFindOwnedSeeds, theme, "greenthumb.settings.findSeeds",
                Loc.T(L.Greenthumb.FindSeedsHint));
            if (find != configuration.GreenthumbFindOwnedSeeds)
            {
                configuration.GreenthumbFindOwnedSeeds = find;
                configuration.Save();
                scanResult = string.Empty;
            }

            if (SettingsRow.Action(card.NextRow(), Loc.T(L.Greenthumb.CheckNow), ui.Palette.Accent, theme))
            {
                scanResult = discovery.ScanNow() switch
                {
                    SeedScanResult.NoCharacter => Loc.T(L.Greenthumb.NeedCharacter),
                    SeedScanResult.Found => Loc.Plural(L.Greenthumb.FoundSeeds, discovery.LastFoundCount),
                    _ => Loc.T(L.Greenthumb.NoNewSeedsFound),
                };
            }

            card.End();
            if (scanResult.Length > 0)
            {
                SettingsSection.Hint(scanResult, theme);
            }

            SettingsSection.Hint(Loc.T(L.Greenthumb.AutoHint), theme);
            Gap(BottomBreathing);
        }

        AppHeader.EndLargeTitle(in navBar, context, "greenthumb.settings.nav", Loc.T(L.Greenthumb.Settings),
            NavBarStyle.From(ui), ReadOnlySpan<NavBarButton>.Empty, Loc.T(L.Greenthumb.GardenTab), back);
    }
}
