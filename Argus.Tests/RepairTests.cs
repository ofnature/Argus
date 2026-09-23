using System;
using System.IO;
using Argus.Core.Game;
using Argus.Core.Model;
using Argus.Core.Store;

namespace Argus.Tests;

public class RepairTests : IDisposable
{
    private const ulong Fc = 1;

    private readonly string dir = Path.Combine(Path.GetTempPath(), "argus-tests", Guid.NewGuid().ToString("N"));

    // Shark hull/stern/bow/bridge, as in GameDataFixture.Shark.
    private static Vessel Sub(params int[] condition) => new()
    {
        FreeCompanyId = Fc,
        Type = VesselType.Submarine,
        Slot = 0,
        Name = "Leviathan",
        Rank = 10,
        Hull = 3,
        Stern = 4,
        Bow = 1,
        Bridge = 2,
        Condition = condition.Length == 0 ? new[] { -1, -1, -1, -1 } : condition,
    };

    [Fact]
    public void OnlyAPartAtZeroIsBroken()
    {
        var v = Sub(30000, 1, 0, -1);
        Assert.Equal([2], v.BrokenSlots());
        Assert.True(v.ConditionKnown);

        // Never read: not known, and nothing counts as broken.
        Assert.False(Sub().ConditionKnown);
        Assert.Empty(Sub().BrokenSlots());
    }

    [Fact]
    public void ReadWithoutConditionKeepsTheStoredOne()
    {
        var store = new FleetStore(dir);
        var now = DateTime.UtcNow;
        store.UpdateVessels(Fc, [Sub(30000, 12000, 0, 500)], now);

        // The workshop saw the vessel but not its parts this tick.
        store.UpdateVessels(Fc, [Sub()], now);

        Assert.Equal(new[] { 30000, 12000, 0, 500 }, store.GetOrCreate(Fc).Vessels[0].Condition);
    }

    [Fact]
    public void SwappedPartDropsItsOldCondition()
    {
        var older = Sub(30000, 12000, 0, 500);
        var fresh = Sub();
        fresh.Bow = 9;

        fresh.KeepConditionFrom(older);

        // The new bow's condition is unknown; the old bow's 0 must not make the vessel look broken.
        Assert.Equal(new[] { 30000, 12000, -1, 500 }, fresh.Condition);
        Assert.Empty(fresh.BrokenSlots());
    }

    [Fact]
    public void NewReadingWinsOverTheStoredOne()
    {
        var fresh = Sub(30000, 30000, 30000, 30000);
        fresh.KeepConditionFrom(Sub(30000, 12000, 0, 500));
        Assert.Equal(new[] { 30000, 30000, 30000, 30000 }, fresh.Condition);
    }

    [Theory]
    [InlineData("Use your last 4 Magitek Repair Materials to repair your vessel's shark-class bow?", "Shark-class Bow", true)]
    [InlineData("Use 4 of your 12 Magitek Repair Materials to repair your vessel's Shark-class Bow?", "Shark-class Bow", true)]
    [InlineData("下記のアイテムを修理しますか？\nシャーク級艦首\n消費:魔導機械修理材×4(所持数 10)", "シャーク級艦首", true)]
    // A repair prompt for a different part: the run asked for the bow, so this is not its prompt.
    [InlineData("Use your last 4 Magitek Repair Materials to repair your vessel's unkiu-class bow?", "Shark-class Bow", false)]
    // The same menu offers decommissioning; its confirmation must never be accepted.
    [InlineData("Decommission Leviathan? All installed components will be lost.", "Shark-class Bow", false)]
    // Names the part but is not a repair.
    [InlineData("Remove the Shark-class Bow?", "Shark-class Bow", false)]
    [InlineData("Use your last 4 Magitek Repair Materials to repair your vessel's shark-class bow?", "", false)]
    public void ConfirmsOnlyTheRepairItAskedFor(string prompt, string part, bool expected)
        => Assert.Equal(expected, RepairInterop.IsRepairPrompt(prompt, part));

    [Fact]
    public void CostIsTheSumOfTheBrokenPartsRepairMaterials()
    {
        var data = GameDataFixture.Load();
        var v = Sub(0, 30000, 0, 30000);
        var expected = data.Part(VesselType.Submarine, v.Hull).RepairMaterials + data.Part(VesselType.Submarine, v.Bow).RepairMaterials;
        Assert.True(expected > 0);
        Assert.Equal(expected, RepairInterop.Cost(data, v, v.BrokenSlots()));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(dir, true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
