using Aetherphone.Core.Localization;
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

    private readonly IDataManager data;
    private readonly string dataPath;
    private SeedBook? book;
    private string[] names = Array.Empty<string>();
    private uint[] icons = Array.Empty<uint>();
    private Dictionary<uint, int> seedByItem = new();
    private Dictionary<uint, int> seedByCrop = new();
    private Dictionary<uint, string> placeNames = new();
    private Dictionary<uint, string> npcNames = new();
    private Dictionary<uint, string> itemNames = new();
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

    public string SourceText(SeedSource source) => source.Kind switch
    {
        SeedSourceKind.Node => Loc.T(L.Greenthumb.SourceNode, source.Level, Place(source.Place), Place(source.Zone)),
        SeedSourceKind.Unspoiled when source.Stars > 0 => Loc.T(L.Greenthumb.SourceUnspoiledStars, source.Level,
            source.Stars, Place(source.Place), Place(source.Zone)),
        SeedSourceKind.Unspoiled => Loc.T(L.Greenthumb.SourceUnspoiled, source.Level, Place(source.Place),
            Place(source.Zone)),
        SeedSourceKind.Supplier when source.Npc != 0 => Loc.T(L.Greenthumb.SourceSupplierNpc,
            Lookup(npcNames, source.Npc), Place(source.Place)),
        SeedSourceKind.Supplier => Loc.T(L.Greenthumb.SourceSupplier),
        SeedSourceKind.Hunt => Loc.T(L.Greenthumb.SourceHunt, Lookup(itemNames, source.Item), source.Cost),
        SeedSourceKind.Company => Loc.T(L.Greenthumb.SourceCompany, source.Cost, source.Credits, source.Rank),
        _ => Loc.T(L.Greenthumb.SourceAirship, Place(source.Zone), JoinSectors(source.Sectors)),
    };

    public static string SourceNote(SeedSource source) => source.Note switch
    {
        SeedSourceNote.NoApartment => Loc.T(L.Greenthumb.NoteNoApartment),
        SeedSourceNote.Flowerpot => Loc.T(L.Greenthumb.NoteFlowerpot),
        _ => string.Empty,
    };

    private string Place(uint id) => Lookup(placeNames, id);

    private static string Lookup(Dictionary<uint, string> names, uint id) =>
        names.TryGetValue(id, out var name) ? name : string.Empty;

    private static string JoinSectors(int[] sectors)
    {
        var parts = new string[sectors.Length];
        for (var index = 0; index < sectors.Length; index++)
        {
            parts[index] = sectors[index].ToString(Loc.Culture);
        }

        return string.Join(Loc.T(L.Greenthumb.ListSeparator), parts);
    }

    private void LoadSourceNames(SeedBook loaded)
    {
        var places = new Dictionary<uint, string>();
        var npcs = new Dictionary<uint, string>();
        var itemTexts = new Dictionary<uint, string>();
        var patchNames = new string[PatchLabel.KindItems.Length];
        Array.Fill(patchNames, string.Empty);
        try
        {
            var placeSheet = data.GetExcelSheet<PlaceName>();
            var npcSheet = data.GetExcelSheet<ENpcResident>();
            var itemSheet = data.GetExcelSheet<Item>();
            for (var seedIndex = 0; seedIndex < loaded.Count; seedIndex++)
            {
                var sources = loaded.Seeds[seedIndex].Sources;
                for (var index = 0; index < sources.Length; index++)
                {
                    var source = sources[index];
                    AddPlace(placeSheet, places, source.Place);
                    AddPlace(placeSheet, places, source.Zone);
                    if (source.Npc != 0 && !npcs.ContainsKey(source.Npc) &&
                        npcSheet.TryGetRow(source.Npc, out var npc))
                    {
                        npcs[source.Npc] = npc.Singular.ExtractText();
                    }

                    if (source.Item != 0 && !itemTexts.ContainsKey(source.Item) &&
                        itemSheet.TryGetRow(source.Item, out var item))
                    {
                        itemTexts[source.Item] = item.Name.ExtractText();
                    }
                }
            }

            for (var index = 0; index < patchNames.Length; index++)
            {
                patchNames[index] = itemSheet.TryGetRow(PatchLabel.KindItems[index], out var patch)
                    ? patch.Name.ExtractText()
                    : string.Empty;
            }
        }
        catch (Exception exception)
        {
            AepLog.Warning(exception, "[Greenthumb] seed source names failed to load");
        }

        placeNames = places;
        npcNames = npcs;
        itemNames = itemTexts;
        PatchLabel.SetGameNames(patchNames);
    }

    private static void AddPlace(Lumina.Excel.ExcelSheet<PlaceName> sheet, Dictionary<uint, string> places, uint id)
    {
        if (id != 0 && !places.ContainsKey(id) && sheet.TryGetRow(id, out var place))
        {
            places[id] = place.Name.ExtractText();
        }
    }

    private void Load()
    {
        try
        {
            var loaded = SeedBook.Parse(File.ReadAllText(dataPath));
            var itemIcons = new uint[loaded.Count];
            var items = new Dictionary<uint, int>(loaded.Count);
            var cropItems = new Dictionary<uint, int>(loaded.Count);
            var localized = LocalizedNames(loaded, itemIcons, items, cropItems);
            LoadSourceNames(loaded);
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
            var seed = loaded.Seeds[index];
            result[index] = seed.Key;
            if (seed.Item != 0)
            {
                items[seed.Item] = index;
            }

            for (var cropIndex = 0; cropIndex < seed.Crops.Length; cropIndex++)
            {
                cropItems.TryAdd(seed.Crops[cropIndex], index);
            }
        }

        try
        {
            var sheet = data.GetExcelSheet<Item>();
            for (var index = 0; index < result.Length; index++)
            {
                var seed = loaded.Seeds[index];
                if (seed.Item == 0 || !sheet.TryGetRow(seed.Item, out var item))
                {
                    AepLog.Warning(string.Concat("[Greenthumb] no game item for ", seed.Key));
                    continue;
                }

                itemIcons[index] = item.Icon;
                var name = item.Name.ExtractText();
                if (name.Length > 0)
                {
                    result[index] = name;
                }

                if (seed.Crops.Length == 1 && sheet.TryGetRow(seed.Crops[0], out var crop) && crop.Icon != 0)
                {
                    itemIcons[index] = crop.Icon;
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
