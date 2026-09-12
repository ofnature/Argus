using System.Linq;
using Argus.Core.Calc;
using Argus.Core.Data;
using Argus.Core.Model;

namespace Argus.Tests;

public class ItemWindowTests
{
    /// <summary>Balsa Wood Lumber: tier-1 only on map 1, so its windows all have an upper bound.</summary>
    private const uint TierOneItem = 12925;

    /// <summary>Unaspected Crystal: present in tier-3 pools on map 1.</summary>
    private const uint TierThreeItem = 10099;

    private readonly GameData data = GameDataFixture.Load();

    [Fact]
    public void WindowsAreBandsNotFloors()
    {
        var windows = ItemYield.Windows(data, VesselType.Submarine, 1, TierOneItem);
        Assert.NotEmpty(windows);

        foreach (var w in windows)
        {
            var t = ExpModel.ThresholdsFor(VesselType.Submarine, w.Sector);

            // It never reaches the richest pool, so every window stops below a threshold rather than running open.
            Assert.True(w.Tier < 2, $"sector {w.Sector} unexpectedly lists it at the top tier");
            Assert.False(w.Unbounded);
            Assert.Equal(w.Tier == 0 ? t.T2 - 1 : t.T3 - 1, w.Max);
            Assert.Equal(w.Tier == 0 ? 0 : t.T2, w.Min);
        }
    }

    [Fact]
    public void TopTierWindowIsOpenEnded()
    {
        var windows = ItemYield.Windows(data, VesselType.Submarine, 1, TierThreeItem);
        var top = windows.Where(w => w.Tier == 2).ToList();
        Assert.NotEmpty(top);
        Assert.All(top, w => Assert.True(w.Unbounded));
        Assert.All(top, w => Assert.Equal(ExpModel.ThresholdsFor(VesselType.Submarine, w.Sector).T3, w.Min));
    }

    [Fact]
    public void WindowMatchesWhatTheYieldModelAccepts()
    {
        foreach (var w in ItemYield.Windows(data, VesselType.Submarine, 1, TierOneItem))
        {
            // Inside the band the item is obtainable; one past the top of it, it is not.
            Assert.True(ItemYield.PerVisit(VesselType.Submarine, w.Sector, TierOneItem, w.Min, 200) > 0);
            Assert.True(ItemYield.PerVisit(VesselType.Submarine, w.Sector, TierOneItem, w.Max, 200) > 0);
            Assert.Equal(0, ItemYield.PerVisit(VesselType.Submarine, w.Sector, TierOneItem, w.Max + 1, 200));
        }
    }

    [Fact]
    public void DescribeReadsAsARange()
    {
        Assert.Equal("under 130", new ItemYield.Window(1, 0, 0, 129).Describe());
        Assert.Equal("130-149", new ItemYield.Window(1, 1, 130, 149).Describe());
        Assert.Equal("150+", new ItemYield.Window(1, 2, 150, int.MaxValue).Describe());
    }

    [Fact]
    public void ItemBuildsLandInsideTheWindow()
    {
        // A maxed build cannot reach a tier-one item, so the optimizer has to pick a lower surveillance set.
        var sectors = ItemYield.Windows(data, VesselType.Submarine, 1, TierOneItem).Select(w => w.Sector).Distinct().Take(3).ToArray();
        var builds = PartOptimizer.BestForItem(data, VesselType.Submarine, 145, 1, sectors, TierOneItem, 5);

        Assert.NotEmpty(builds);
        Assert.True(builds[0].Units > 0);
        Assert.True(builds.Zip(builds.Skip(1)).All(p => p.First.UnitsPerDay >= p.Second.UnitsPerDay));

        // Every suggestion must actually sit in a band that lists the item for at least one of those sectors.
        foreach (var candidate in builds)
        {
            Assert.Contains(sectors, s => ItemYield.Windows(data, VesselType.Submarine, 1, TierOneItem)
                .Any(w => w.Sector == s && w.Contains(candidate.Build.Surveillance)));
        }
    }

    [Fact]
    public void ItemBuildsAreEmptyWhenNoSectorOnTheRouteSuppliesIt()
        => Assert.Empty(PartOptimizer.BestForItem(data, VesselType.Submarine, 145, 1, [1u], 99999, 5));
}
