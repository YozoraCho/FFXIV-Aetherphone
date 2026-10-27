using System.Reflection;
using Aetherphone.Core.Gardening;
using Xunit;

namespace Aetherphone.Tests;

public sealed class GreenthumbPlannerTests
{
    private static readonly Lazy<SeedBook> Book = new(() =>
        SeedBook.Parse(File.ReadAllText(Path.Combine(PluginSourceDirectory(), "Gardening", "Seeds.json"))));

    [Fact]
    public void BundledSeedDataParses()
    {
        var book = Book.Value;

        Assert.Equal(107, book.Count);
        Assert.True(book.TryIndex("Thavnairian Onion Seeds", out var onion));
        Assert.True(book.Seeds[onion].CanCross);
        Assert.False(book.Seeds[onion].CanGather);
        Assert.Equal(24, book.Seeds[onion].WiltHours);
        Assert.True(book.TryIndex("Chamomile Seeds", out var chamomile));
        Assert.Equal(48, book.Seeds[chamomile].WiltHours);
        Assert.Equal(240, book.Seeds[onion].GrowHours);
        Assert.Equal(120, book.Seeds[chamomile].GrowHours);
        foreach (var seed in book.Seeds)
        {
            Assert.True(seed.GrowHours > 0, seed.Key);
        }
    }

    [Fact]
    public void FindsEveryOneStepCrossLikeTheDesktopHelper()
    {
        var owned = Owned("Krakka Root Seeds", "Mirror Apple Seeds", "Thavnairian Onion Seeds", "Chamomile Seeds",
            "Gysahl Greens Seeds");

        var result = CrossbreedPlanner.FindRoutes(Book.Value, owned, Index("Curiel Root Seeds"), false);

        Assert.Equal(PlanStatus.Found, result.Status);
        Assert.Equal(1, result.StepCount);
        Assert.Equal(6, result.RouteCount);
    }

    [Fact]
    public void FindsTheShortestGatherRouteLikeTheDesktopHelper()
    {
        var owned = Owned("Krakka Root Seeds", "Mirror Apple Seeds", "Chamomile Seeds", "Gysahl Greens Seeds");

        var result = CrossbreedPlanner.FindRoutes(Book.Value, owned, Index("Thavnairian Onion Seeds"), true);

        Assert.Equal(PlanStatus.Found, result.Status);
        Assert.Equal(4, result.StepCount);
        Assert.Equal(28, result.RouteCount);
        var desktopFirst = new[]
        {
            PlanStep.Cross(Index("Chamomile Seeds"), Index("Gysahl Greens Seeds"), Index("Curiel Root Seeds")),
            PlanStep.Gather(Index("Mandrake Seeds")),
            PlanStep.Cross(Index("Gysahl Greens Seeds"), Index("Mandrake Seeds"), Index("Mimett Gourd Seeds")),
            PlanStep.Cross(Index("Curiel Root Seeds"), Index("Mimett Gourd Seeds"), Index("Thavnairian Onion Seeds")),
        };
        Assert.Contains(result.Routes, route => route.SequenceEqual(desktopFirst));
    }

    [Fact]
    public void RoutesComeOutInTheSameOrderEveryTime()
    {
        var owned = Owned("Mandrake Seeds");
        var target = Index("Jute Seeds");

        var first = CrossbreedPlanner.FindRoutes(Book.Value, owned, target, true);
        var second = CrossbreedPlanner.FindRoutes(Book.Value, owned, target, true);

        Assert.Equal(first.Routes.Length, second.Routes.Length);
        for (var index = 0; index < first.Routes.Length; index++)
        {
            Assert.Equal(first.Routes[index], second.Routes[index]);
        }

        var gathers = first.Routes.Select(route => route.Count(step => step.Kind == PlanStepKind.Gather)).ToArray();
        Assert.Equal(gathers.OrderBy(count => count).ToArray(), gathers);
    }

    [Fact]
    public void WhereToStartListsWhatEachRouteGathers()
    {
        var owned = Owned("Mandrake Seeds");
        var result = CrossbreedPlanner.FindRoutes(Book.Value, owned, Index("Jute Seeds"), true);

        var sets = CrossbreedPlanner.GatherSets(Book.Value, result);

        Assert.True(sets.Count > 1);
        var sizes = sets.Select(set => set.Length).ToArray();
        Assert.Equal(sizes.OrderBy(size => size).ToArray(), sizes);
        foreach (var set in sets)
        {
            Assert.All(set, seed => Assert.True(Book.Value.Seeds[seed].CanGather));
        }
    }

    [Fact]
    public void ReportsOwnedAndUnreachableTargets()
    {
        var owned = Owned("Chamomile Seeds");

        Assert.Equal(PlanStatus.AlreadyOwned,
            CrossbreedPlanner.FindRoutes(Book.Value, owned, Index("Chamomile Seeds"), false).Status);
        Assert.Equal(PlanStatus.Unreachable,
            CrossbreedPlanner.FindRoutes(Book.Value, owned, Index("Thavnairian Onion Seeds"), false).Status);
    }

    [Fact]
    public void ListsNewSeedsWithinTheStepLimit()
    {
        var owned = Owned("Krakka Root Seeds", "Mirror Apple Seeds", "Thavnairian Onion Seeds", "Chamomile Seeds",
            "Gysahl Greens Seeds");

        var found = CrossbreedPlanner.Reachable(Book.Value, owned, 2, false);

        var expected = new[]
        {
            "Ala Mhigan Mustard Seeds", "Blood Currant Seeds", "Curiel Root Seeds", "Garlic Jester Seeds", "Linseed",
            "Midland Basil Seeds", "Pahsana Fruit Seeds", "Pearl Ginger Root", "Pixie Plum Seeds", "Rolanberry Seeds",
            "Sun Lemon Seeds",
        };
        Assert.Equal(expected, found.Select(index => Book.Value.Seeds[index].Key).ToArray());
    }

    [Theory]
    [InlineData((int)GardenPatchKind.Deluxe, 0, new[] { 1, 7 })]
    [InlineData((int)GardenPatchKind.Deluxe, 1, new[] { 2, 0 })]
    [InlineData((int)GardenPatchKind.Deluxe, 3, new[] { 4, 2 })]
    [InlineData((int)GardenPatchKind.Oblong, 1, new[] { 2, 4, 0 })]
    [InlineData((int)GardenPatchKind.Oblong, 3, new[] { 4, 0 })]
    [InlineData((int)GardenPatchKind.Round, 0, new[] { 1, 2 })]
    [InlineData((int)GardenPatchKind.Round, 3, new[] { 1, 2 })]
    [InlineData((int)GardenPatchKind.Flowerpot, 0, new int[0])]
    public void NeighborsShareASideOnly(int kind, int bed, int[] expected)
    {
        Span<int> buffer = stackalloc int[GardenLayout.MaxNeighbors];

        var count = GardenLayout.Neighbors((GardenPatchKind)kind, bed, buffer);

        Assert.Equal(expected, buffer[..count].ToArray());
    }

    [Fact]
    public void WarnsBeforeWiltingAndStopsOnceReady()
    {
        var planted = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var bed = new GardenBed();
        bed.Plant("Gysahl Greens Seeds", 0, planted);

        Assert.False(GardenTimers.NeedsTending(bed, 24, planted.AddHours(19)));
        Assert.True(GardenTimers.NeedsTending(bed, 24, planted.AddHours(21)));
        Assert.Equal(planted.AddHours(24), GardenTimers.WiltsAtUtc(bed, 24, planted));

        bed.Tend(planted.AddHours(21));
        Assert.False(GardenTimers.NeedsTending(bed, 24, planted.AddHours(22)));

        bed.GrowHours = 30;
        Assert.True(GardenTimers.IsReady(bed, planted.AddHours(30)));
        Assert.Null(GardenTimers.WiltsAtUtc(bed, 24, planted.AddHours(31)));
    }

    [Fact]
    public void FoundSeedsSkipOwnedAndRepeatedOnes()
    {
        var owned = new bool[5];
        owned[1] = true;
        var found = new List<int>();

        Assert.True(SeedMatcher.AddIfNew(3, owned, found));
        Assert.False(SeedMatcher.AddIfNew(3, owned, found));
        Assert.False(SeedMatcher.AddIfNew(1, owned, found));
        Assert.False(SeedMatcher.AddIfNew(-1, owned, found));
        Assert.True(SeedMatcher.AddIfNew(0, owned, found));
        Assert.Equal(new[] { 3, 0 }, found.ToArray());
    }

    [Fact]
    public void InventoryChangesBecomeGardenActions()
    {
        var items = new FakeGardenItems();
        var before = new Dictionary<uint, int> { [100] = 3, [200] = 5, [300] = 2, [101] = 4 };
        var after = new Dictionary<uint, int> { [100] = 2, [200] = 4, [300] = 4, [101] = 1 };
        var signals = new List<GardenSignal>();

        GardenDelta.Classify(before, after, items, DateTime.UnixEpoch, signals);

        Assert.Contains(new GardenSignal(GardenActionKind.Plant, 0, DateTime.UnixEpoch, false), signals);
        Assert.Contains(new GardenSignal(GardenActionKind.Fertilize, -1, DateTime.UnixEpoch, false), signals);
        Assert.Contains(new GardenSignal(GardenActionKind.Harvest, 0, DateTime.UnixEpoch, false), signals);
        Assert.Equal(3, signals.Count);
    }

    [Fact]
    public void UsingTheLastSeedStillCountsAsPlanting()
    {
        var before = new Dictionary<uint, int> { [100] = 1 };
        var signals = new List<GardenSignal>();

        GardenDelta.Classify(before, new Dictionary<uint, int>(), new FakeGardenItems(), DateTime.UnixEpoch, signals);

        Assert.Equal(new GardenSignal(GardenActionKind.Plant, 0, DateTime.UnixEpoch, false), Assert.Single(signals));
    }

    [Fact]
    public void TheInboxKeepsTheNewestActionsInOrder()
    {
        var inbox = new GardenInbox();
        var now = DateTime.UtcNow;
        for (var index = 0; index < GardenInbox.Capacity + 2; index++)
        {
            inbox.Add(new GardenSignal(GardenActionKind.Plant, index, now, false));
        }

        Assert.Equal(GardenInbox.Capacity, inbox.Count);
        Assert.True(inbox.TryPeek(out var first));
        Assert.Equal(2, first.Seed);
        inbox.RemoveFirst();
        Assert.True(inbox.TryPeek(out var second));
        Assert.Equal(3, second.Seed);
    }

    private sealed class FakeGardenItems : IGardenItems
    {
        public uint FishmealItem => 200;

        public bool TryIndexItem(uint itemId, out int index)
        {
            index = itemId == 100 ? 0 : itemId == 101 ? 1 : -1;
            return index >= 0;
        }

        public bool TryIndexCrop(uint itemId, out int index)
        {
            index = itemId == 300 ? 0 : -1;
            return index >= 0;
        }
    }

    [Theory]
    [InlineData((int)GardenPatchKind.Round)]
    [InlineData((int)GardenPatchKind.Oblong)]
    [InlineData((int)GardenPatchKind.Deluxe)]
    public void TheLayoutPutsTheOtherSeedOnEverySide(int kindValue)
    {
        var kind = (GardenPatchKind)kindValue;
        Span<int> neighbors = stackalloc int[GardenLayout.MaxNeighbors];
        var first = 0;
        for (var bed = 0; bed < GardenLayout.BedCount(kind); bed++)
        {
            var parent = GardenLayout.LayoutParent(kind, bed);
            first += parent == 0 ? 1 : 0;
            var count = GardenLayout.Neighbors(kind, bed, neighbors);
            Assert.True(count > 0);
            for (var index = 0; index < count; index++)
            {
                Assert.NotEqual(parent, GardenLayout.LayoutParent(kind, neighbors[index]));
            }
        }

        Assert.Equal(GardenLayout.BedCount(kind) / 2, first);
    }

    private static int Index(string key)
    {
        Assert.True(Book.Value.TryIndex(key, out var index), key);
        return index;
    }

    private static bool[] Owned(params string[] keys)
    {
        var owned = new bool[Book.Value.Count];
        foreach (var key in keys)
        {
            owned[Index(key)] = true;
        }

        return owned;
    }

    private static string PluginSourceDirectory()
    {
        var directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        while (!string.IsNullOrEmpty(directory))
        {
            var candidate = Path.Combine(directory, "src", "Aetherphone");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = Path.GetDirectoryName(directory)!;
        }

        throw new DirectoryNotFoundException("Could not locate src/Aetherphone from the test assembly.");
    }
}
