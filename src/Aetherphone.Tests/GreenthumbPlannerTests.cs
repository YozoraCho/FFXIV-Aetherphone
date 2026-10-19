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
        var first = result.Routes[0];
        Assert.Equal(PlanStep.Cross(Index("Chamomile Seeds"), Index("Gysahl Greens Seeds"), Index("Curiel Root Seeds")),
            first[0]);
        Assert.Equal(PlanStep.Gather(Index("Mandrake Seeds")), first[1]);
        Assert.Equal(PlanStep.Cross(Index("Gysahl Greens Seeds"), Index("Mandrake Seeds"), Index("Mimett Gourd Seeds")),
            first[2]);
        Assert.Equal(
            PlanStep.Cross(Index("Curiel Root Seeds"), Index("Mimett Gourd Seeds"), Index("Thavnairian Onion Seeds")),
            first[3]);
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
        bed.Plant("Gysahl Greens Seeds", planted);

        Assert.False(GardenTimers.NeedsTending(bed, 24, planted.AddHours(19)));
        Assert.True(GardenTimers.NeedsTending(bed, 24, planted.AddHours(21)));
        Assert.Equal(planted.AddHours(24), GardenTimers.WiltsAtUtc(bed, 24, planted));

        bed.Tend(planted.AddHours(21));
        Assert.False(GardenTimers.NeedsTending(bed, 24, planted.AddHours(22)));

        bed.GrowHours = 30;
        Assert.True(GardenTimers.IsReady(bed, planted.AddHours(30)));
        Assert.Null(GardenTimers.WiltsAtUtc(bed, 24, planted.AddHours(31)));
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
