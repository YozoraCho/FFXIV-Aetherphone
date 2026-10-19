namespace Aetherphone.Core.Gardening;

internal enum PlanStepKind : byte
{
    Gather,
    Cross,
}

internal readonly record struct PlanStep(PlanStepKind Kind, int Seed, int First, int Second)
{
    public static PlanStep Gather(int seed) => new(PlanStepKind.Gather, seed, -1, -1);

    public static PlanStep Cross(int first, int second, int result) => new(PlanStepKind.Cross, result, first, second);
}

internal enum PlanStatus : byte
{
    AlreadyOwned,
    Unreachable,
    Found,
}

internal sealed class PlanResult
{
    public static readonly PlanResult Owned = new(PlanStatus.AlreadyOwned, 0, 0, false, Array.Empty<PlanStep[]>());
    public static readonly PlanResult None = new(PlanStatus.Unreachable, 0, 0, false, Array.Empty<PlanStep[]>());

    public PlanResult(PlanStatus status, int stepCount, int routeCount, bool capped, PlanStep[][] routes)
    {
        Status = status;
        StepCount = stepCount;
        RouteCount = routeCount;
        Capped = capped;
        Routes = routes;
    }

    public PlanStatus Status { get; }
    public int StepCount { get; }
    public int RouteCount { get; }
    public bool Capped { get; }
    public PlanStep[][] Routes { get; }
}

internal static class CrossbreedPlanner
{
    public const int MaxPlans = 500;
    public const int MaxShownRoutes = 25;
    public const int Unreachable = -1;

    public static int[] Costs(SeedBook book, ReadOnlySpan<bool> owned, bool gather)
    {
        var seeds = book.Seeds;
        var cost = new int[seeds.Length];
        for (var index = 0; index < seeds.Length; index++)
        {
            if (owned[index])
            {
                cost[index] = 0;
            }
            else if (gather && seeds[index].CanGather)
            {
                cost[index] = 1;
            }
            else
            {
                cost[index] = Unreachable;
            }
        }

        var changed = true;
        while (changed)
        {
            changed = false;
            for (var result = 0; result < seeds.Length; result++)
            {
                var best = cost[result];
                var crosses = seeds[result].Crosses;
                for (var crossIndex = 0; crossIndex < crosses.Length; crossIndex++)
                {
                    var first = cost[crosses[crossIndex].First];
                    var second = cost[crosses[crossIndex].Second];
                    if (first == Unreachable || second == Unreachable)
                    {
                        continue;
                    }

                    var candidate = 1 + first + second;
                    if (best == Unreachable || candidate < best)
                    {
                        best = candidate;
                    }
                }

                if (best != cost[result])
                {
                    cost[result] = best;
                    changed = true;
                }
            }
        }

        return cost;
    }

    public static PlanResult FindRoutes(SeedBook book, ReadOnlySpan<bool> owned, int target, bool gather)
    {
        if (owned[target])
        {
            return PlanResult.Owned;
        }

        var cost = Costs(book, owned, gather);
        if (cost[target] == Unreachable)
        {
            return PlanResult.None;
        }

        var memo = new Dictionary<int, List<List<PlanStep>>>();
        var ownedCopy = owned.ToArray();
        var raw = Plans(book, ownedCopy, cost, gather, target, memo);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var unique = new List<PlanStep[]>();
        for (var planIndex = 0; planIndex < raw.Count; planIndex++)
        {
            var steps = Distinct(raw[planIndex]);
            if (seen.Add(SetKey(steps)))
            {
                unique.Add(steps);
            }
        }

        var fewest = int.MaxValue;
        for (var planIndex = 0; planIndex < unique.Count; planIndex++)
        {
            fewest = Math.Min(fewest, unique[planIndex].Length);
        }

        var shortest = new List<PlanStep[]>();
        for (var planIndex = 0; planIndex < unique.Count; planIndex++)
        {
            if (unique[planIndex].Length == fewest)
            {
                shortest.Add(unique[planIndex]);
            }
        }

        var shown = Math.Min(shortest.Count, MaxShownRoutes);
        var routes = new PlanStep[shown][];
        for (var routeIndex = 0; routeIndex < shown; routeIndex++)
        {
            routes[routeIndex] = shortest[routeIndex];
        }

        return new PlanResult(PlanStatus.Found, fewest, shortest.Count, raw.Count >= MaxPlans, routes);
    }

    public static int[] Reachable(SeedBook book, ReadOnlySpan<bool> owned, int depth, bool gather)
    {
        var cost = Costs(book, owned, gather);
        var found = new List<int>();
        for (var index = 0; index < cost.Length; index++)
        {
            if (cost[index] > 0 && cost[index] <= depth)
            {
                found.Add(index);
            }
        }

        return found.ToArray();
    }

    private static List<List<PlanStep>> Plans(SeedBook book, bool[] owned, int[] cost, bool gather, int seed,
        Dictionary<int, List<List<PlanStep>>> memo)
    {
        if (memo.TryGetValue(seed, out var cached))
        {
            return cached;
        }

        var output = new List<List<PlanStep>>();
        if (owned[seed])
        {
            output.Add(new List<PlanStep>());
            memo[seed] = output;
            return output;
        }

        var entry = book.Seeds[seed];
        if (gather && entry.CanGather && cost[seed] == 1)
        {
            output.Add(new List<PlanStep> { PlanStep.Gather(seed) });
        }

        var crosses = entry.Crosses;
        for (var crossIndex = 0; crossIndex < crosses.Length && output.Count < MaxPlans; crossIndex++)
        {
            var first = crosses[crossIndex].First;
            var second = crosses[crossIndex].Second;
            if (cost[first] == Unreachable || cost[second] == Unreachable ||
                1 + cost[first] + cost[second] != cost[seed])
            {
                continue;
            }

            var firstPlans = Plans(book, owned, cost, gather, first, memo);
            var secondPlans = Plans(book, owned, cost, gather, second, memo);
            for (var firstIndex = 0; firstIndex < firstPlans.Count && output.Count < MaxPlans; firstIndex++)
            {
                for (var secondIndex = 0; secondIndex < secondPlans.Count && output.Count < MaxPlans; secondIndex++)
                {
                    var combined = new List<PlanStep>(firstPlans[firstIndex].Count + secondPlans[secondIndex].Count + 1);
                    combined.AddRange(firstPlans[firstIndex]);
                    combined.AddRange(secondPlans[secondIndex]);
                    combined.Add(PlanStep.Cross(first, second, seed));
                    output.Add(combined);
                }
            }
        }

        memo[seed] = output;
        return output;
    }

    private static PlanStep[] Distinct(List<PlanStep> steps)
    {
        var seen = new HashSet<PlanStep>();
        var kept = new List<PlanStep>(steps.Count);
        for (var stepIndex = 0; stepIndex < steps.Count; stepIndex++)
        {
            if (seen.Add(steps[stepIndex]))
            {
                kept.Add(steps[stepIndex]);
            }
        }

        return kept.ToArray();
    }

    private static string SetKey(PlanStep[] steps)
    {
        var codes = new long[steps.Length];
        for (var stepIndex = 0; stepIndex < steps.Length; stepIndex++)
        {
            var step = steps[stepIndex];
            codes[stepIndex] = ((long)step.Kind << 48) | ((long)(step.Seed & 0xFFFF) << 32) |
                ((long)(step.First & 0xFFFF) << 16) | (uint)(step.Second & 0xFFFF);
        }

        Array.Sort(codes);
        return string.Join(',', codes);
    }
}
