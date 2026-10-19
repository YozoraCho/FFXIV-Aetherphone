using System.Text.Json;

namespace Aetherphone.Core.Gardening;

internal readonly record struct SeedPair(int First, int Second);

internal sealed class GardenSeed
{
    public required string Key { get; init; }
    public required string[] Sources { get; init; }
    public required SeedPair[] Crosses { get; init; }
    public required int[][] Starters { get; init; }
    public required int WiltHours { get; init; }

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
                Sources = ReadStrings(element.GetProperty("sources")),
                Crosses = ReadPairs(element.GetProperty("crosses")),
                Starters = ReadSets(element.GetProperty("starters")),
                WiltHours = element.GetProperty("wiltHours").GetInt32(),
            };
            index++;
        }

        return new SeedBook(seeds);
    }

    private static long PairKey(int first, int second) =>
        first < second ? ((long)first << 32) | (uint)second : ((long)second << 32) | (uint)first;

    private static string[] ReadStrings(JsonElement array)
    {
        var values = new string[array.GetArrayLength()];
        var index = 0;
        foreach (var element in array.EnumerateArray())
        {
            values[index] = element.GetString() ?? string.Empty;
            index++;
        }

        return values;
    }

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

    private static int[][] ReadSets(JsonElement array)
    {
        var sets = new int[array.GetArrayLength()][];
        var index = 0;
        foreach (var element in array.EnumerateArray())
        {
            var set = new int[element.GetArrayLength()];
            var memberIndex = 0;
            foreach (var member in element.EnumerateArray())
            {
                set[memberIndex] = member.GetInt32();
                memberIndex++;
            }

            sets[index] = set;
            index++;
        }

        return sets;
    }
}
