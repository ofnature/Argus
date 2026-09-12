using System.Collections.Generic;
using System.Linq;
using Argus.Core.Data;
using Argus.Core.Model;

namespace Argus.Core.Calc;

/// <summary>
/// How much of one item a route is expected to bring back.
///
/// <para>Submarines: the surveillance the build reaches at a sector chooses the loot pool, the item's recorded
/// per-draw chance times the quantity range for the build's retrieval tier gives the expected units. Each sector is
/// counted as one draw, so the figure is a lower bound; the second item a high surveillance build can pull is not
/// modelled, and it would scale every sector alike anyway.</para>
///
/// <para>Airships: no public drop-rate data exists, so a sector counts as 1 when it can produce the item at the
/// reached tier and 0 otherwise. The result is a count of sources, not units, and the UI says so.</para>
/// </summary>
public static class ItemYield
{
    /// <summary>Retrieval tier at a sector: 0 poor, 1 normal, 2 optimal. -1 when the sector has no known thresholds.</summary>
    public static int RetrievalTier(ExpModel.Thresholds t, int retrieval)
    {
        if (!t.Known || t.Optimal <= 0)
            return -1;
        if (retrieval >= t.Optimal)
            return 2;
        return retrieval >= t.Normal ? 1 : 0;
    }

    /// <summary>Expected units of an item from one visit to a sector, or 1/0 for airships (see the class remarks).</summary>
    public static double PerVisit(VesselType type, uint sector, uint itemId, int surveillance, int retrieval)
    {
        var thresholds = ExpModel.ThresholdsFor(type, sector);
        var tier = PartOptimizer.SurveillanceTier(thresholds, surveillance);
        var drop = LootTable.Drops(type, sector, tier).FirstOrDefault(d => d.ItemId == itemId);
        if (drop == null)
            return 0;
        if (!drop.HasRates)
            return 1;

        var retrievalTier = RetrievalTier(thresholds, retrieval);
        var (min, max) = drop.Range(retrievalTier < 0 ? 1 : retrievalTier);
        return drop.Chance * ((min + max) / 2.0);
    }

    /// <summary>Expected units (or sources, for airships) over a whole route.</summary>
    public static double PerVoyage(VesselType type, IEnumerable<uint> sectors, uint itemId, int surveillance, int retrieval)
        => sectors.Sum(s => PerVisit(type, s, itemId, surveillance, retrieval));

    /// <summary>A surveillance range that puts a sector on the loot tier which lists an item.</summary>
    public sealed record Window(uint Sector, int Tier, int Min, int Max)
    {
        public bool Unbounded => Max == int.MaxValue;

        public bool Contains(int surveillance) => surveillance >= Min && surveillance <= Max;

        /// <summary>"under 130", "130-149", "150+".</summary>
        public string Describe()
            => Min <= 0 && Unbounded ? "any"
                : Unbounded ? $"{Min}+"
                : Min <= 0 ? $"under {Max + 1}"
                : $"{Min}-{Max}";
    }

    /// <summary>
    /// The surveillance range a tier occupies at a sector. Surveillance promotes a sector to a richer pool rather
    /// than adding to it, so each tier is a band rather than a floor, and the lower tiers have an upper bound.
    /// </summary>
    private static (int Min, int Max)? BandFor(ExpModel.Thresholds t, int tier)
    {
        if (tier == 2)
            return t.T3 > 0 ? (t.T3, int.MaxValue) : null;
        if (tier == 1)
            return t.T2 > 0 ? (t.T2, t.T3 > 0 ? t.T3 - 1 : int.MaxValue) : null;
        return (0, t.T2 > 0 ? t.T2 - 1 : int.MaxValue);
    }

    /// <summary>Every sector and surveillance range on this map that can produce the item, richest tier first.</summary>
    public static List<Window> Windows(GameData data, VesselType type, uint map, uint itemId)
    {
        var windows = new List<Window>();
        foreach (var sector in data.DestinationsOf(type, map))
        {
            var thresholds = ExpModel.ThresholdsFor(type, sector.Id);
            if (!thresholds.Known)
                continue;

            for (var tier = 0; tier < 3; tier++)
            {
                if (LootTable.Drops(type, sector.Id, tier).All(d => d.ItemId != itemId))
                    continue;
                if (BandFor(thresholds, tier) is not { } band)
                    continue;
                windows.Add(new Window(sector.Id, tier, band.Min, band.Max));
            }
        }

        return windows.OrderByDescending(w => w.Tier).ThenBy(w => w.Sector).ToList();
    }

    public sealed record SectorSource(uint Sector, int Tier, double PerVisit, int Min, int Max);

    /// <summary>
    /// Sectors on this map that can produce the item for this build, best first. Used to explain a farming route and
    /// to show what is out of reach.
    /// </summary>
    public static List<SectorSource> Sources(GameData data, VesselType type, uint map, uint itemId, int surveillance, int retrieval)
    {
        var sources = new List<SectorSource>();
        foreach (var sector in data.DestinationsOf(type, map))
        {
            var thresholds = ExpModel.ThresholdsFor(type, sector.Id);
            var tier = PartOptimizer.SurveillanceTier(thresholds, surveillance);
            var drop = LootTable.Drops(type, sector.Id, tier).FirstOrDefault(d => d.ItemId == itemId);
            if (drop == null)
                continue;

            var retrievalTier = RetrievalTier(thresholds, retrieval);
            var (min, max) = drop.Range(retrievalTier < 0 ? 1 : retrievalTier);
            sources.Add(new SectorSource(sector.Id, tier, PerVisit(type, sector.Id, itemId, surveillance, retrieval), min, max));
        }

        return sources.OrderByDescending(s => s.PerVisit).ThenBy(s => s.Sector).ToList();
    }
}
