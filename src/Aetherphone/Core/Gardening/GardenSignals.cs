namespace Aetherphone.Core.Gardening;

internal enum GardenActionKind : byte
{
    Plant,
    Tend,
    Harvest,
}

internal readonly record struct GardenSignal(GardenActionKind Kind, int Seed, DateTime Utc);

internal interface IGardenItems
{
    bool TryIndexItem(uint itemId, out int index);

    bool TryIndexCrop(uint itemId, out int index);
}

internal static class GardenDelta
{
    public static void Classify(Dictionary<uint, int> before, Dictionary<uint, int> after, IGardenItems items,
        DateTime utc, List<GardenSignal> into)
    {
        foreach (var entry in after)
        {
            before.TryGetValue(entry.Key, out var previous);
            AddSignal(entry.Key, entry.Value - previous, items, utc, into);
        }

        foreach (var entry in before)
        {
            if (!after.ContainsKey(entry.Key))
            {
                AddSignal(entry.Key, -entry.Value, items, utc, into);
            }
        }
    }

    private static void AddSignal(uint itemId, int change, IGardenItems items, DateTime utc, List<GardenSignal> into)
    {
        if (change == 0)
        {
            return;
        }

        if (change == -1 && items.TryIndexItem(itemId, out var seed))
        {
            into.Add(new GardenSignal(GardenActionKind.Plant, seed, utc));
            return;
        }

        if (change > 0 && items.TryIndexCrop(itemId, out var crop))
        {
            into.Add(new GardenSignal(GardenActionKind.Harvest, crop, utc));
        }
    }
}
