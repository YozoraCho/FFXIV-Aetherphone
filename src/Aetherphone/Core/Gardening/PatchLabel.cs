using Aetherphone.Core.Localization;

namespace Aetherphone.Core.Gardening;

internal static class PatchLabel
{
    public static readonly uint[] KindItems = { 7128, 7129, 7130 };

    private static string[] gameNames = Array.Empty<string>();

    public static void SetGameNames(string[] names) => gameNames = names;

    public static string KindName(GardenPatchKind kind)
    {
        var names = gameNames;
        var index = (int)kind;
        if (index < names.Length && names[index] is { Length: > 0 } name)
        {
            return name;
        }

        return Loc.T(kind switch
        {
            GardenPatchKind.Round => L.Greenthumb.RoundPatch,
            GardenPatchKind.Oblong => L.Greenthumb.OblongPatch,
            GardenPatchKind.Deluxe => L.Greenthumb.DeluxePatch,
            _ => L.Greenthumb.Flowerpot,
        });
    }

    public static string For(GardenPatch patch) => patch.Name.Length > 0 ? patch.Name : KindName(patch.Kind);
}
