using System.Collections.Generic;
using System.Linq;
using Argus.Core.Calc;
using Argus.Core.Data;
using Argus.Core.Model;

namespace Argus.Tests;

public class LootTableTests
{
    /// <summary>Balsa Wood Lumber, present in both the submarine records and the airship tier lists.</summary>
    private const uint BalsaWoodLumber = 12925;

    [Fact]
    public void EmbeddedTableLoads()
    {
        Assert.True(LootTable.HasData(VesselType.Submarine));
        Assert.True(LootTable.HasData(VesselType.Airship));
        Assert.Contains("girin", LootTable.Source);
        Assert.True(LootTable.Items(VesselType.Submarine).Count > 300);
        Assert.Equal(66, LootTable.Items(VesselType.Airship).Count);
    }

    [Fact]
    public void SubmarineDropMatchesTheSourceRecords()
    {
        // Sector 28 (the Rimilala Trench Bottom), base surveillance pool: 64413 draws of 202126.
        var drop = LootTable.Drops(VesselType.Submarine, 28, 0).Single(d => d.ItemId == BalsaWoodLumber);
        Assert.Equal(0.31868, drop.Chance, 5);
        Assert.True(drop.HasRates);
        Assert.Equal((1, 5), drop.Range(0));
        Assert.Equal((5, 10), drop.Range(1));
        Assert.Equal((10, 15), drop.Range(2));
    }

    [Fact]
    public void AirshipDropsCarryNoRates()
    {
        // Row 7 is Sea of Clouds sector 8, whose tier 1 list includes Balsa Wood Lumber.
        var drop = LootTable.Drops(VesselType.Airship, 7, 0).Single(d => d.ItemId == BalsaWoodLumber);
        Assert.False(drop.HasRates);
        Assert.Equal(0, drop.Chance);
    }

    [Fact]
    public void ReverseIndexFindsEverySource()
    {
        var sectors = LootTable.SectorsFor(VesselType.Submarine, BalsaWoodLumber);
        Assert.Contains(28u, sectors);
        Assert.All(sectors, id => Assert.Contains(LootTable.Drops(VesselType.Submarine, id, 0)
            .Concat(LootTable.Drops(VesselType.Submarine, id, 1))
            .Concat(LootTable.Drops(VesselType.Submarine, id, 2)), d => d.ItemId == BalsaWoodLumber));
    }
}

public class ItemYieldTests
{
    private const uint BalsaWoodLumber = 12925;

    private readonly GameData data = GameDataFixture.Load();

    [Fact]
    public void RetrievalTierFollowsTheBreakpoints()
    {
        var t = ExpModel.ThresholdsFor(VesselType.Submarine, 28);
        Assert.Equal(0, ItemYield.RetrievalTier(t, t.Normal - 1));
        Assert.Equal(1, ItemYield.RetrievalTier(t, t.Normal));
        Assert.Equal(2, ItemYield.RetrievalTier(t, t.Optimal));
        Assert.Equal(-1, ItemYield.RetrievalTier(default, 999));
    }

    [Fact]
    public void ExpectedUnitsRiseWithRetrieval()
    {
        var t = ExpModel.ThresholdsFor(VesselType.Submarine, 28);
        var poor = ItemYield.PerVisit(VesselType.Submarine, 28, BalsaWoodLumber, 0, 0);
        var optimal = ItemYield.PerVisit(VesselType.Submarine, 28, BalsaWoodLumber, 0, t.Optimal);

        // Chance 0.31868 against the poor (1-5) and optimal (10-15) ranges.
        Assert.Equal(0.31868 * 3.0, poor, 3);
        Assert.Equal(0.31868 * 12.5, optimal, 3);
        Assert.True(optimal > poor);
    }

    [Fact]
    public void ItemTheSectorCannotDropYieldsNothing()
        => Assert.Equal(0, ItemYield.PerVisit(VesselType.Submarine, 28, 1, 0, 999));

    [Fact]
    public void AirshipYieldCountsSourcesNotUnits()
        => Assert.Equal(1, ItemYield.PerVisit(VesselType.Airship, 7, BalsaWoodLumber, 0, 0));

    [Fact]
    public void SourcesAreRankedAndReachable()
    {
        var sources = ItemYield.Sources(data, VesselType.Submarine, 1, BalsaWoodLumber, 0, 200);
        Assert.NotEmpty(sources);
        Assert.All(sources, s => Assert.Equal(1u, data.Sector(VesselType.Submarine, s.Sector).Map));
        Assert.True(sources.Zip(sources.Skip(1)).All(p => p.First.PerVisit >= p.Second.PerVisit));
    }
}

public class FarmRouteTests
{
    /// <summary>Unaspected Crystal: present in the tier-3 pools a maxed submarine draws from on map 1.</summary>
    private const uint TierThreeItem = 10099;

    private readonly GameData data = GameDataFixture.Load();

    private RouteRequest Request(uint targetItem)
    {
        var sub = Build.From(data, VesselType.Submarine, 145, 23, 24, 21, 22);
        var unlocked = data.DestinationsOf(VesselType.Submarine, 1).Select(s => s.Id).ToHashSet();
        return new RouteRequest(VesselType.Submarine, 1, sub, unlocked, new HashSet<uint>(),
            RouteGoal.ExpPerVoyage, null, false, TargetItem: targetItem);
    }

    [Fact]
    public void FarmingBeatsPlanningForExpOnTheTargetItem()
    {
        var farming = RouteSearch.FindBest(data, Request(TierThreeItem));
        var forExp = RouteSearch.FindBest(data, Request(0));
        Assert.NotNull(farming);
        Assert.NotNull(forExp);

        Assert.True(farming!.ItemUnits > 0);
        Assert.True(farming.ItemUnits >= forExp!.ItemUnits,
            $"farming route yields {farming.ItemUnits}, the EXP route {forExp.ItemUnits}");
    }

    [Fact]
    public void RouteUnitsAreTheSumOfItsSectors()
    {
        var request = Request(TierThreeItem);
        var best = RouteSearch.FindBest(data, request)!;
        var expected = ItemYield.PerVoyage(VesselType.Submarine, best.Sectors, TierThreeItem,
            request.Build.Surveillance, request.Build.Retrieval);
        Assert.Equal(expected, best.ItemUnits, 6);
    }

    [Fact]
    public void HighSurveillanceLosesAccessToLowTierItems()
    {
        // Balsa Wood Lumber only appears in tier-1 pools on this map, so a maxed build never rolls on it.
        var maxed = Build.From(data, VesselType.Submarine, 145, 23, 24, 21, 22);
        Assert.Empty(ItemYield.Sources(data, VesselType.Submarine, 1, 12925, maxed.Surveillance, maxed.Retrieval));
        Assert.NotEmpty(ItemYield.Sources(data, VesselType.Submarine, 1, 12925, 0, maxed.Retrieval));
    }

    [Fact]
    public void PlanningForExpLeavesUnitsAtZero()
        => Assert.Equal(0, RouteSearch.FindBest(data, Request(0))!.ItemUnits);
}
