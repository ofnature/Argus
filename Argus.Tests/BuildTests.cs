using Argus.Core.Model;

namespace Argus.Tests;

public class BuildTests
{
    private readonly GameData data = GameDataFixture.Load();

    [Fact]
    public void FixtureHasBothVesselTypes()
    {
        Assert.Equal(7, data.Maps.Count);
        Assert.Equal(30, data.DestinationsOf(VesselType.Submarine, 1).Count());
        Assert.Equal(24, data.DestinationsOf(VesselType.Airship, 1).Count()); // rows 0-24 minus the Diadem
        Assert.Equal(145, data.LastSubmarineRank);
        Assert.Equal(50, data.LastAirshipRank);
    }

    [Fact]
    public void SharkAtRankOneSumsPartsOnly()
    {
        var shark = GameDataFixture.Shark(data, 1);
        // hull(3) -10/30/20/40/20 + stern(4) -30/20/60/30/15 + bow(1) 50/40/10/-20/15 + bridge(2) 20/20/20/20/20
        Assert.Equal(30, shark.Surveillance);
        Assert.Equal(110, shark.Retrieval);
        Assert.Equal(110, shark.Speed);
        Assert.Equal(70, shark.Range);
        Assert.Equal(70, shark.Favor);
        Assert.Equal(20, shark.Cost);
        Assert.True(shark.FitsCapacity);
        Assert.Equal("SSSS", shark.Identifier);
    }

    [Fact]
    public void SubmarineRankBonusIsAdded()
    {
        var r1 = GameDataFixture.Shark(data, 1);
        var r50 = GameDataFixture.Shark(data, 50);
        var bonus = data.Rank(VesselType.Submarine, 50);
        Assert.Equal(r1.Surveillance + bonus.SurveillanceBonus, r50.Surveillance);
        Assert.Equal(r1.Speed + bonus.SpeedBonus, r50.Speed);
    }

    [Fact]
    public void RankExtrapolatesPastTheTable()
    {
        var last = data.Rank(VesselType.Submarine, data.LastSubmarineRank);
        var beyond = data.Rank(VesselType.Submarine, data.LastSubmarineRank + 3);
        Assert.Equal(last.SurveillanceBonus + 3, beyond.SurveillanceBonus);
        Assert.Equal(last.Capacity, beyond.Capacity);
    }

    [Fact]
    public void AirshipStatsComeFromPartsOnly()
    {
        var bronco = GameDataFixture.Bronco(data, 50);
        Assert.Equal(80 - 10, bronco.Surveillance);   // forecastle 80, aftcastle -10
        Assert.Equal(80 - 10, bronco.Retrieval);      // aftcastle 80, rigging -10
        Assert.Equal(80 - 10, bronco.Speed);          // rigging 80, aftcastle -10
        Assert.Equal(80 - 10, bronco.Range);          // hull 80, rigging -10
        Assert.Equal(80 - 10, bronco.Favor);          // forecastle 80, hull -10
        Assert.Equal(12, bronco.Cost);
        Assert.Equal("BBBB", bronco.Identifier);
    }

    [Fact]
    public void ModifiedSubmarinePartsGetPlusIdentifier()
    {
        var build = Build.From(data, VesselType.Submarine, 100, 23, 24, 21, 22); // modified Shark set
        Assert.Equal("SSSS++", build.Identifier);
        Assert.Equal("Modified Shark", Build.SubmarineClassName(23));
    }
}
