using Dalamud.Game;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace Aetherphone.Core.Gardening;

internal enum GardenCatalogState : byte
{
    Idle,
    Loading,
    Ready,
    Failed,
}

internal sealed class GardenCatalog
{
    public const string AppId = "greenthumb";
    private const string DataFolder = "Gardening";
    private const string DataFile = "Seeds.json";

    private readonly IDataManager data;
    private readonly string dataPath;
    private SeedBook? book;
    private string[] names = Array.Empty<string>();
    private string[] searchKeys = Array.Empty<string>();
    private int[] byName = Array.Empty<int>();
    private volatile GardenCatalogState state;

    public GardenCatalog(IDataManager data, string pluginDirectory)
    {
        this.data = data;
        dataPath = Path.Combine(pluginDirectory, DataFolder, DataFile);
    }

    public GardenCatalogState State => state;

    public SeedBook Book => book!;

    public int Count => state == GardenCatalogState.Ready ? names.Length : 0;

    public ReadOnlySpan<int> SortedByName => byName;

    public string Name(int index) => names[index];

    public string SearchKey(int index) => searchKeys[index];

    public void EnsureLoaded()
    {
        if (state != GardenCatalogState.Idle)
        {
            return;
        }

        state = GardenCatalogState.Loading;
        _ = Task.Run(Load);
    }

    public void Retry()
    {
        if (state != GardenCatalogState.Failed)
        {
            return;
        }

        state = GardenCatalogState.Idle;
        EnsureLoaded();
    }

    public bool TryIndex(string key, out int index)
    {
        if (state == GardenCatalogState.Ready && key.Length > 0)
        {
            return book!.TryIndex(key, out index);
        }

        index = -1;
        return false;
    }

    public int WiltHours(string key) =>
        TryIndex(key, out var index) ? book!.Seeds[index].WiltHours : GardenTimers.DefaultWiltHours;

    public string NameOf(string key) => TryIndex(key, out var index) ? names[index] : key;

    private void Load()
    {
        try
        {
            var loaded = SeedBook.Parse(File.ReadAllText(dataPath));
            var localized = LocalizedNames(loaded);
            var keys = new string[localized.Length];
            for (var index = 0; index < localized.Length; index++)
            {
                keys[index] = localized[index].ToLowerInvariant();
            }

            var order = new int[localized.Length];
            for (var index = 0; index < order.Length; index++)
            {
                order[index] = index;
            }

            Array.Sort(order, (first, second) => string.Compare(localized[first], localized[second],
                StringComparison.CurrentCultureIgnoreCase));
            book = loaded;
            names = localized;
            searchKeys = keys;
            byName = order;
            state = GardenCatalogState.Ready;
        }
        catch (Exception exception)
        {
            AepLog.Warning(exception, "[Greenthumb] seed data failed to load");
            state = GardenCatalogState.Failed;
        }
    }

    private string[] LocalizedNames(SeedBook loaded)
    {
        var result = new string[loaded.Count];
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = loaded.Seeds[index].Key;
        }

        try
        {
            var wanted = new Dictionary<string, int>(loaded.Count, StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < result.Length; index++)
            {
                wanted[result[index]] = index;
            }

            var matched = new bool[result.Length];
            var english = data.GetExcelSheet<Item>(ClientLanguage.English);
            var local = data.GetExcelSheet<Item>();
            foreach (var item in english)
            {
                if (!wanted.TryGetValue(item.Name.ExtractText(), out var index))
                {
                    continue;
                }

                matched[index] = true;
                if (local.TryGetRow(item.RowId, out var localItem))
                {
                    var name = localItem.Name.ExtractText();
                    if (name.Length > 0)
                    {
                        result[index] = name;
                    }
                }
            }

            for (var index = 0; index < matched.Length; index++)
            {
                if (!matched[index])
                {
                    AepLog.Warning(string.Concat("[Greenthumb] no game item named ", result[index]));
                }
            }
        }
        catch (Exception exception)
        {
            AepLog.Warning(exception, "[Greenthumb] seed names fell back to English");
        }

        return result;
    }
}
