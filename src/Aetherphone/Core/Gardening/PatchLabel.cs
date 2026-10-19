using Aetherphone.Core.Localization;

namespace Aetherphone.Core.Gardening;

internal static class PatchLabel
{
    public static LocString KindName(GardenPatchKind kind) => kind switch
    {
        GardenPatchKind.Round => L.Greenthumb.RoundPatch,
        GardenPatchKind.Oblong => L.Greenthumb.OblongPatch,
        GardenPatchKind.Deluxe => L.Greenthumb.DeluxePatch,
        _ => L.Greenthumb.Flowerpot,
    };

    public static string For(GardenPatch patch) =>
        patch.Name.Length > 0 ? patch.Name : Loc.T(KindName(patch.Kind));
}
