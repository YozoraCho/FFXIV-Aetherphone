namespace Aetherphone.Apps.Greenthumb;

internal enum GreenthumbScreen : byte
{
    Root,
    Patch,
    Bed,
    PickSeed,
    Settings,
    Place,
    Layout,
}

internal enum GreenthumbTab : byte
{
    Garden,
    Planner,
    Seeds,
}

internal enum SeedPickPurpose : byte
{
    Bed,
    Target,
}

internal readonly record struct GreenthumbRoute(GreenthumbScreen Screen, Guid PatchId = default, int Bed = -1,
    SeedPickPurpose Purpose = SeedPickPurpose.Bed)
{
    public static readonly GreenthumbRoute Root = new(GreenthumbScreen.Root);

    public static GreenthumbRoute Patch(Guid patchId) => new(GreenthumbScreen.Patch, patchId);

    public static GreenthumbRoute BedOf(Guid patchId, int bed) => new(GreenthumbScreen.Bed, patchId, bed);

    public static GreenthumbRoute PickForBed(Guid patchId, int bed) =>
        new(GreenthumbScreen.PickSeed, patchId, bed, SeedPickPurpose.Bed);

    public static readonly GreenthumbRoute SettingsPage = new(GreenthumbScreen.Settings);
    public static readonly GreenthumbRoute PlacePage = new(GreenthumbScreen.Place);
    public static readonly GreenthumbRoute LayoutPage = new(GreenthumbScreen.Layout);

    public static GreenthumbRoute PickTarget() => new(GreenthumbScreen.PickSeed, Purpose: SeedPickPurpose.Target);
}
