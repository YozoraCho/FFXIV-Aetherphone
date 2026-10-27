using System.Text.Json;

namespace Aetherphone.Core.Gardening;

internal readonly record struct SeedPair(int First, int Second);

internal enum SeedSourceKind : byte
{
    Node,
    Unspoiled,
    Supplier,
    Hunt,
    Company,
    Airship,
}

internal enum SeedSourceNote : byte
{
    None,
    NoApartment,
    Flowerpot,
}

internal sealed class SeedSource
{
    public required SeedSourceKind Kind { get; init; }
    public int Level { get; init; }
    public int Stars { get; init; }
    public uint Place { get; init; }
    public uint Zone { get; init; }
    public uint Npc { get; init; }
    public uint Item { get; init; }
    public int Cost { get; init; }
    public int Credits { get; init; }
    public int Rank { get; init; }
    public int[] Sectors { get; init; } = Array.Empty<int>();
    public SeedSourceNote Note { get; init; }
}

internal sealed class GardenSeed
{
    public required string Key { get; init; }
    public required SeedSource[] Sources { get; init; }
    public required SeedPair[] Crosses { get; init; }
    public required uint Item { get; init; }
    public required uint[] Crops { get; init; }
    public required int WiltHours { get; init; }
    public required int GrowHours { get; init; }

    public bool CanGather => Sources.Length > 0;

    public bool CanCross => Crosses.Length > 0;
}

internal sealed class SeedBook
{
    private readonly Dictionary<string, int> indexByKey;
    private readonly Dictionary<long, int[]> resultsByPair;

    private SeedBook(GardenSeed[] seeds)
    {
        Seeds = seeds;
        indexByKey = new Dictionary<string, int>(seeds.Length, StringComparer.Ordinal);
        for (var index = 0; index < seeds.Length; index++)
        {
            indexByKey[seeds[index].Key] = index;
        }

        var results = new Dictionary<long, List<int>>();
        for (var index = 0; index < seeds.Length; index++)
        {
            var crosses = seeds[index].Crosses;
            for (var crossIndex = 0; crossIndex < crosses.Length; crossIndex++)
            {
                var pairKey = PairKey(crosses[crossIndex].First, crosses[crossIndex].Second);
                if (!results.TryGetValue(pairKey, out var list))
                {
                    list = new List<int>();
                    results[pairKey] = list;
                }

                list.Add(index);
            }
        }

        resultsByPair = new Dictionary<long, int[]>(results.Count);
        foreach (var entry in results)
        {
            resultsByPair[entry.Key] = entry.Value.ToArray();
        }
    }

    public GardenSeed[] Seeds { get; }

    public int Count => Seeds.Length;

    public bool TryIndex(string key, out int index) => indexByKey.TryGetValue(key, out index);

    public ReadOnlySpan<int> CrossResults(int first, int second) =>
        resultsByPair.TryGetValue(PairKey(first, second), out var results) ? results : ReadOnlySpan<int>.Empty;

    public static SeedBook Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var array = document.RootElement.GetProperty("seeds");
        var seeds = new GardenSeed[array.GetArrayLength()];
        var index = 0;
        foreach (var element in array.EnumerateArray())
        {
            seeds[index] = new GardenSeed
            {
                Key = element.GetProperty("name").GetString() ?? string.Empty,
                Sources = ReadSources(element.GetProperty("sources")),
                Crosses = ReadPairs(element.GetProperty("crosses")),
                Item = element.TryGetProperty("item", out var item) ? item.GetUInt32() : 0,
                Crops = element.TryGetProperty("crops", out var crops) ? ReadItems(crops) : Array.Empty<uint>(),
                WiltHours = element.GetProperty("wiltHours").GetInt32(),
                GrowHours = element.TryGetProperty("growHours", out var grow) ? grow.GetInt32() : 0,
            };
            index++;
        }

        return new SeedBook(seeds);
    }

    private static long PairKey(int first, int second) =>
        first < second ? ((long)first << 32) | (uint)second : ((long)second << 32) | (uint)first;

    private static SeedSource[] ReadSources(JsonElement array)
    {
        var sources = new SeedSource[array.GetArrayLength()];
        var index = 0;
        foreach (var element in array.EnumerateArray())
        {
            sources[index] = new SeedSource
            {
                Kind = ParseKind(element.GetProperty("kind").GetString()),
                Level = ReadInt(element, "level"),
                Stars = ReadInt(element, "stars"),
                Place = (uint)ReadInt(element, "place"),
                Zone = (uint)ReadInt(element, "zone"),
                Npc = (uint)ReadInt(element, "npc"),
                Item = (uint)ReadInt(element, "item"),
                Cost = ReadInt(element, "cost"),
                Credits = ReadInt(element, "credits"),
                Rank = ReadInt(element, "rank"),
                Sectors = element.TryGetProperty("sectors", out var sectors) ? ReadInts(sectors) : Array.Empty<int>(),
                Note = ParseNote(element.TryGetProperty("note", out var note) ? note.GetString() : null),
            };
            index++;
        }

        return sources;
    }

    private static int ReadInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetInt32() : 0;

    private static int[] ReadInts(JsonElement array)
    {
        var values = new int[array.GetArrayLength()];
        var index = 0;
        foreach (var element in array.EnumerateArray())
        {
            values[index] = element.GetInt32();
            index++;
        }

        return values;
    }

    private static SeedSourceKind ParseKind(string? kind) => kind switch
    {
        "node" => SeedSourceKind.Node,
        "unspoiled" => SeedSourceKind.Unspoiled,
        "supplier" => SeedSourceKind.Supplier,
        "hunt" => SeedSourceKind.Hunt,
        "company" => SeedSourceKind.Company,
        "airship" => SeedSourceKind.Airship,
        _ => throw new FormatException(string.Concat("unknown seed source ", kind)),
    };

    private static SeedSourceNote ParseNote(string? note) => note switch
    {
        null => SeedSourceNote.None,
        "noApartment" => SeedSourceNote.NoApartment,
        "flowerpot" => SeedSourceNote.Flowerpot,
        _ => throw new FormatException(string.Concat("unknown seed source note ", note)),
    };

    private static SeedPair[] ReadPairs(JsonElement array)
    {
        var pairs = new SeedPair[array.GetArrayLength()];
        var index = 0;
        foreach (var element in array.EnumerateArray())
        {
            pairs[index] = new SeedPair(element[0].GetInt32(), element[1].GetInt32());
            index++;
        }

        return pairs;
    }

    private static uint[] ReadItems(JsonElement array)
    {
        var items = new uint[array.GetArrayLength()];
        var index = 0;
        foreach (var element in array.EnumerateArray())
        {
            items[index] = element.GetUInt32();
            index++;
        }

        return items;
    }
}
