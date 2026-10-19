namespace Aetherphone.Core.Gardening;

internal enum GardenPatchKind : byte
{
    Round,
    Oblong,
    Deluxe,
    Flowerpot,
}

[Serializable]
internal sealed class GardenBed
{
    public string Seed { get; set; } = string.Empty;
    public DateTime? PlantedAtUtc { get; set; }
    public DateTime? TendedAtUtc { get; set; }
    public int GrowHours { get; set; }
    public bool ReadyNotified { get; set; }
    public bool TendNotified { get; set; }

    public bool IsPlanted => Seed.Length > 0 && PlantedAtUtc.HasValue;

    public void Plant(string seed, DateTime nowUtc)
    {
        Seed = seed;
        PlantedAtUtc = nowUtc;
        TendedAtUtc = nowUtc;
        GrowHours = 0;
        ReadyNotified = false;
        TendNotified = false;
    }

    public void Tend(DateTime nowUtc)
    {
        TendedAtUtc = nowUtc;
        TendNotified = false;
    }

    public void Clear()
    {
        Seed = string.Empty;
        PlantedAtUtc = null;
        TendedAtUtc = null;
        GrowHours = 0;
        ReadyNotified = false;
        TendNotified = false;
    }
}

[Serializable]
internal sealed class GardenPatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public GardenPatchKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<GardenBed> Beds { get; set; } = new();

    public static GardenPatch Create(GardenPatchKind kind, string name)
    {
        var patch = new GardenPatch { Kind = kind, Name = name };
        patch.EnsureBeds();
        return patch;
    }

    public void EnsureBeds()
    {
        var count = GardenLayout.BedCount(Kind);
        while (Beds.Count < count)
        {
            Beds.Add(new GardenBed());
        }

        if (Beds.Count > count)
        {
            Beds.RemoveRange(count, Beds.Count - count);
        }
    }
}
