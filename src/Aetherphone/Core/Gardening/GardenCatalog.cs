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

internal sealed class GardenCatalog : IGardenItems
{
    public const string AppId = "greenthumb";
    private const string DataFolder = "Gardening";
    private const string DataFile = "Seeds.json";
    private const string FishmealName = "Fishmeal";

    private static readonly string[] SeedSuffixes =
    {
        " Seeds", " Kernels", " Bulbs", " Pips", " Sapling", " Pits", " Sori", " Set", " Cloves",
    };

    private readonly IDataManager data;
    private readonly string dataPath;
    private SeedBook? book;
    private string[] names = Array.Empty<string>();
    private uint[] icons = Array.Empty<uint>();
    private Dictionary<uint, int> seedByItem = new();
    private Dictionary<uint, int> seedByCrop = new();
    private uint fishmealItem;
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

    public uint Icon(int index) => icons[index];

    public uint IconOf(string key) => TryIndex(key, out var index) ? icons[index] : 0;

    public uint FishmealItem => state == GardenCatalogState.Ready ? fishmealItem : 0;

    public bool TryIndexCrop(uint itemId, out int index)
    {
        if (state == GardenCatalogState.Ready)
        {
            return seedByCrop.TryGetValue(itemId, out index);
        }

        index = -1;
        return false;
    }

    public bool TryIndexItem(uint itemId, out int index)
    {
        if (state == GardenCatalogState.Ready)
        {
            return seedByItem.TryGetValue(itemId, out index);
        }

        index = -1;
        return false;
    }

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
            var itemIcons = new uint[loaded.Count];
            var items = new Dictionary<uint, int>(loaded.Count);
            var cropItems = new Dictionary<uint, int>(loaded.Count);
            var localized = LocalizedNames(loaded, itemIcons, items, cropItems);
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
            icons = itemIcons;
            seedByItem = items;
            seedByCrop = cropItems;
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

    private string[] LocalizedNames(SeedBook loaded, uint[] itemIcons, Dictionary<uint, int> items,
        Dictionary<uint, int> cropItems)
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

            var crops = new Dictionary<string, int>(loaded.Count, StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < result.Length; index++)
            {
                var crop = CropName(result[index]);
                if (crop.Length == 0)
                {
                    continue;
                }

                crops.TryAdd(crop, index);
                crops.TryAdd(string.Concat(crop, "s"), index);
                crops.TryAdd(string.Concat(crop, "es"), index);
                if (crop.EndsWith('y'))
                {
                    crops.TryAdd(string.Concat(crop[..^1], "ies"), index);
                }
            }

            var cropIcons = new uint[result.Length];
            var matched = new bool[result.Length];
            var english = data.GetExcelSheet<Item>(ClientLanguage.English);
            var local = data.GetExcelSheet<Item>();
            foreach (var item in english)
            {
                var englishName = item.Name.ExtractText();
                if (crops.TryGetValue(englishName, out var cropIndex))
                {
                    cropIcons[cropIndex] = item.Icon;
                    cropItems.TryAdd(item.RowId, cropIndex);
                }

                if (fishmealItem == 0 && string.Equals(englishName, FishmealName, StringComparison.Ordinal))
                {
                    fishmealItem = item.RowId;
                }

                if (!wanted.TryGetValue(englishName, out var index))
                {
                    continue;
                }

                matched[index] = true;
                itemIcons[index] = item.Icon;
                items[item.RowId] = index;
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
                if (cropIcons[index] != 0)
                {
                    itemIcons[index] = cropIcons[index];
                }

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

    private static string CropName(string seed)
    {
        for (var index = 0; index < SeedSuffixes.Length; index++)
        {
            var suffix = SeedSuffixes[index];
            if (seed.EndsWith(suffix, StringComparison.Ordinal))
            {
                return seed[..^suffix.Length];
            }
        }

        return string.Empty;
    }
}
