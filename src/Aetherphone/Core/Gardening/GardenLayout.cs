namespace Aetherphone.Core.Gardening;

internal static class GardenLayout
{
    public const int MaxNeighbors = 4;
    private const int DeluxeColumns = 3;

    private static readonly int[] DeluxeCells = { 0, 1, 2, 5, 8, 7, 6, 3 };

    public static int BedCount(GardenPatchKind kind) => kind switch
    {
        GardenPatchKind.Round => 4,
        GardenPatchKind.Oblong => 6,
        GardenPatchKind.Deluxe => 8,
        _ => 1,
    };

    public static int Columns(GardenPatchKind kind) => kind switch
    {
        GardenPatchKind.Round => 2,
        GardenPatchKind.Oblong => 3,
        GardenPatchKind.Deluxe => DeluxeColumns,
        _ => 1,
    };

    public static int Rows(GardenPatchKind kind) => kind switch
    {
        GardenPatchKind.Round => 2,
        GardenPatchKind.Oblong => 2,
        GardenPatchKind.Deluxe => DeluxeColumns,
        _ => 1,
    };

    public static bool CanCross(GardenPatchKind kind) => kind != GardenPatchKind.Flowerpot;

    public static int LayoutParent(GardenPatchKind kind, int bed)
    {
        var cell = CellOf(kind, bed);
        var columns = Columns(kind);
        return (cell / columns + cell % columns) % 2;
    }

    public static int CellOf(GardenPatchKind kind, int bed) => kind == GardenPatchKind.Deluxe ? DeluxeCells[bed] : bed;

    public static int BedAtCell(GardenPatchKind kind, int cell)
    {
        if (kind != GardenPatchKind.Deluxe)
        {
            return cell < BedCount(kind) ? cell : -1;
        }

        for (var bed = 0; bed < DeluxeCells.Length; bed++)
        {
            if (DeluxeCells[bed] == cell)
            {
                return bed;
            }
        }

        return -1;
    }

    public static int Neighbors(GardenPatchKind kind, int bed, Span<int> output)
    {
        if (!CanCross(kind))
        {
            return 0;
        }

        var columns = Columns(kind);
        var rows = Rows(kind);
        var cell = CellOf(kind, bed);
        var column = cell % columns;
        var row = cell / columns;
        var count = 0;
        count = Add(kind, column + 1, row, columns, rows, output, count);
        count = Add(kind, column, row + 1, columns, rows, output, count);
        count = Add(kind, column, row - 1, columns, rows, output, count);
        count = Add(kind, column - 1, row, columns, rows, output, count);
        return count;
    }

    private static int Add(GardenPatchKind kind, int column, int row, int columns, int rows, Span<int> output,
        int count)
    {
        if (column < 0 || row < 0 || column >= columns || row >= rows)
        {
            return count;
        }

        var neighbor = BedAtCell(kind, row * columns + column);
        if (neighbor < 0)
        {
            return count;
        }

        output[count] = neighbor;
        return count + 1;
    }
}
