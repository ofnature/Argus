using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Argus.Core.Model;
using Newtonsoft.Json.Linq;

namespace Argus.Core.Data;

/// <summary>What a sector can bring back, per surveillance tier.</summary>
public sealed record LootDrop(uint ItemId, double Chance, int[] Quantity)
{
    /// <summary>Quantity range for a retrieval tier (0 poor, 1 normal, 2 optimal); empty for airships.</summary>
    public (int Min, int Max) Range(int retrievalTier)
        => Quantity.Length < 6 ? (0, 0) : (Quantity[retrievalTier * 2], Quantity[(retrievalTier * 2) + 1]);

    /// <summary>Whether a recorded drop rate and quantity exist, or only the fact that this item can appear.</summary>
    public bool HasRates => Chance > 0 && Quantity.Length >= 6;
}

/// <summary>
/// Per-sector loot, baked from <c>LootTable.json</c> (embedded).
///
/// <para>Submarines come from the crowd-sourced voyage records in Infiziert90/FFXIVGachaSpreadsheet, which record
/// how often each item was drawn from a sector's surveillance pool and the quantity range at each retrieval tier,
/// so both a per-draw chance and an expected quantity are known.</para>
///
/// <para>Airships come from submarine.girin.dev, which lists only which items a sector can produce at each tier.
/// There is no public airship drop-rate data, so those entries carry no rates and only answer "can this sector
/// drop it".</para>
/// </summary>
public static class LootTable
{
    private const string ResourceName = "Argus.Core.Data.LootTable.json";

    private static readonly Dictionary<(VesselType Type, uint Sector, int Tier), List<LootDrop>> Table = new();
    private static readonly Dictionary<(VesselType Type, uint Item), List<uint>> ItemToSectors = new();
    private static readonly Dictionary<VesselType, List<uint>> AllItems = new();

    public static string Source { get; private set; } = string.Empty;

    static LootTable()
    {
        try
        {
            Load();
        }
        catch (Exception ex)
        {
            // A missing or malformed table only costs the farming feature; everything else keeps working.
            Service.Log.Error(ex, "Argus: could not load the loot table");
        }
    }

    private static void Load()
    {
        using var stream = typeof(LootTable).Assembly.GetManifestResourceStream(ResourceName)
                           ?? throw new FileNotFoundException($"embedded resource {ResourceName} is missing");
        using var reader = new StreamReader(stream);
        var root = JObject.Parse(reader.ReadToEnd());
        Source = root.Value<string>("Source") ?? string.Empty;

        foreach (var type in new[] { VesselType.Submarine, VesselType.Airship })
        {
            var items = new HashSet<uint>();
            if (root[type.ToString()] is not JObject sectors)
                continue;

            foreach (var (sectorKey, tiers) in sectors)
            {
                var sector = uint.Parse(sectorKey);
                foreach (var (tierKey, entries) in (JObject)tiers!)
                {
                    // The file numbers tiers 1..3; the code uses 0..2 to match PartOptimizer.SurveillanceTier.
                    var tier = int.Parse(tierKey) - 1;
                    var drops = new List<LootDrop>();
                    foreach (var entry in (JArray)entries!)
                    {
                        var drop = entry.Type == JTokenType.Object
                            ? new LootDrop(entry.Value<uint>("i"), entry.Value<double>("p"), entry["q"]!.Values<int>().ToArray())
                            : new LootDrop(entry.Value<uint>(), 0, []);
                        drops.Add(drop);
                        items.Add(drop.ItemId);

                        var key = (type, drop.ItemId);
                        if (!ItemToSectors.TryGetValue(key, out var list))
                            ItemToSectors[key] = list = new List<uint>();
                        if (!list.Contains(sector))
                            list.Add(sector);
                    }

                    Table[(type, sector, tier)] = drops;
                }
            }

            AllItems[type] = items.ToList();
        }
    }

    /// <summary>What a sector can drop at a surveillance tier (0 base, 1 tier two, 2 tier three).</summary>
    public static IReadOnlyList<LootDrop> Drops(VesselType type, uint sector, int surveillanceTier)
        => Table.TryGetValue((type, sector, surveillanceTier), out var drops) ? drops : [];

    /// <summary>Every sector that can produce an item, at any tier.</summary>
    public static IReadOnlyList<uint> SectorsFor(VesselType type, uint itemId)
        => ItemToSectors.TryGetValue((type, itemId), out var sectors) ? sectors : [];

    /// <summary>Every item this vessel type can bring back anywhere.</summary>
    public static IReadOnlyList<uint> Items(VesselType type)
        => AllItems.TryGetValue(type, out var items) ? items : [];

    /// <summary>Items obtainable from the given sectors, for the planner's item picker.</summary>
    public static List<uint> ItemsFrom(VesselType type, IEnumerable<uint> sectors)
    {
        var items = new HashSet<uint>();
        foreach (var sector in sectors)
        {
            for (var tier = 0; tier < 3; tier++)
            {
                foreach (var drop in Drops(type, sector, tier))
                    items.Add(drop.ItemId);
            }
        }

        return items.ToList();
    }

    public static bool HasData(VesselType type) => AllItems.TryGetValue(type, out var items) && items.Count > 0;
}
