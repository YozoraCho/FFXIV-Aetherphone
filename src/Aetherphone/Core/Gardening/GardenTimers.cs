namespace Aetherphone.Core.Gardening;

internal static class GardenTimers
{
    public const int DefaultWiltHours = 48;
    public const int TendWarningHours = 4;

    public static DateTime? WiltsAtUtc(GardenBed bed, int wiltHours, DateTime nowUtc)
    {
        if (!bed.IsPlanted || IsReady(bed, nowUtc))
        {
            return null;
        }

        var tended = bed.TendedAtUtc ?? bed.PlantedAtUtc!.Value;
        return tended.AddHours(wiltHours);
    }

    public static DateTime? ReadyAtUtc(GardenBed bed)
    {
        if (!bed.IsPlanted || bed.GrowHours <= 0)
        {
            return null;
        }

        return bed.PlantedAtUtc!.Value.AddHours(bed.GrowHours);
    }

    public static bool IsReady(GardenBed bed, DateTime nowUtc) => ReadyAtUtc(bed) is { } ready && nowUtc >= ready;

    public static bool NeedsTending(GardenBed bed, int wiltHours, DateTime nowUtc) =>
        WiltsAtUtc(bed, wiltHours, nowUtc) is { } wilts && nowUtc >= wilts.AddHours(-TendWarningHours);
}
