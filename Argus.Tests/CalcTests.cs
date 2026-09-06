using Argus.Core.Calc;
using Argus.Core.Data;
using Argus.Core.Model;

namespace Argus.Tests;

public class VoyageMathTests
{
    private readonly GameData data = GameDataFixture.Load();

    [Fact]
    public void SubmarineSingleSectorRouteMatchesReferenceFormula()
    {
        // Deep-sea Site start (0) → the Ivory Shoals (1), Shark speed 110.
        var start = data.StartFor(VesselType.Submarine, 1);
        var a = data.Sector(VesselType.Submarine, 1);
        var raw = Math.Sqrt(Math.Pow(start.X - a.X, 2) + Math.Pow(start.Y - a.Y, 2) + Math.Pow(start.Z - a.Z, 2));
        var expectedTravel = (int)Math.Floor(raw * 3990 / (110 * 100.0) * 60);
        var expectedSurvey = (int)Math.Floor(a.SurveyMinutes * 7000 / (110 * 100.0) * 60);

        var duration = VoyageMath.RouteDuration(start, new[] { a }, 110);
        Assert.Equal(expectedTravel + expectedSurvey + VoyageMath.FixedVoyageSeconds, (int)duration.TotalSeconds);
        Assert.Equal((int)Math.Floor(raw * 0.035) + a.SurveyDistance, VoyageMath.RouteDistance(start, new[] { a }));
    }

    [Fact]
    public void AirshipDistancesMatchTheCommunityTable()
    {
        // Airship Log Data sheet: HOME→SC01 7, HOME→SC02 9, HOME→SC03 18, HOME→SC08 19, SC01→SC02 12, SC01→SC03 11.
        var start = data.AirshipStart;
        Assert.Equal(7, VoyageMath.LegDistance(start, data.Sector(VesselType.Airship, 0)));
        Assert.Equal(9, VoyageMath.LegDistance(start, data.Sector(VesselType.Airship, 1)));
        Assert.Equal(18, VoyageMath.LegDistance(start, data.Sector(VesselType.Airship, 2)));
        Assert.Equal(19, VoyageMath.LegDistance(start, data.Sector(VesselType.Airship, 7)));
        Assert.Equal(12, VoyageMath.LegDistance(data.Sector(VesselType.Airship, 0), data.Sector(VesselType.Airship, 1)));
        Assert.Equal(11, VoyageMath.LegDistance(data.Sector(VesselType.Airship, 0), data.Sector(VesselType.Airship, 2)));
    }

    [Fact]
    public void AirshipRouteFiveEightThirteenIsSeventyFiveUnits()
    {
        // "A route through sectors 5, 8 and 13 has a flight distance of 75" (Lodestone airship guide).
        var route = new[] { data.Sector(VesselType.Airship, 4), data.Sector(VesselType.Airship, 7), data.Sector(VesselType.Airship, 12) };
        Assert.Equal(75, VoyageMath.RouteDistance(data.AirshipStart, route));
    }

    [Fact]
    public void AirshipSurveyTimeScalesWithSpeed()
    {
        var a = data.Sector(VesselType.Airship, 0); // 180 min at speed 70
        Assert.Equal(180 * 60, VoyageMath.SurveySeconds(a, 70));
        Assert.Equal(90 * 60, VoyageMath.SurveySeconds(a, 140));
    }
}

public class ExpModelTests
{
    private readonly GameData data = GameDataFixture.Load();

    [Fact]
    public void SubmarineBonusStepsAreTwentyFivePercent()
    {
        Assert.Equal(1000u, ExpModel.SubmarineExp(1000, 0));
        Assert.Equal(1250u, ExpModel.SubmarineExp(1000, 1));
        Assert.Equal(2000u, ExpModel.SubmarineExp(1000, 4));
        Assert.Equal(2000u, ExpModel.SubmarineExp(1000, 9));
    }

    [Fact]
    public void SubmarineBonusFollowsBreakpoints()
    {
        var br = SubmarineData.MapBreakpoints[1]; // T2 20, T3 80, Normal 50, Optimal 80, Favor 70
        var none = ExpModel.SubmarineBonus(1, 0, 0, 0);
        Assert.Equal(new SectorBonus(0, 0, 0), none);

        var optimalOnly = ExpModel.SubmarineBonus(1, 0, br.Optimal, 0);
        Assert.Equal(new SectorBonus(1, 1, 1), optimalOnly);

        var everything = ExpModel.SubmarineBonus(1, br.T3, br.Optimal, br.Favor);
        Assert.Equal(1, everything.Guaranteed);
        Assert.Equal(4, everything.Maximum);
        Assert.Equal(2, everything.Average);
    }

    [Fact]
    public void AirshipRatingAddsFiftyPercentPerStep()
    {
        Assert.Equal(10610u, ExpModel.AirshipExp(10610, 0));
        Assert.Equal(15915u, ExpModel.AirshipExp(10610, 1));
        Assert.Equal(26525u, ExpModel.AirshipExp(10610, 3));
        Assert.Equal(26525u, ExpModel.AirshipExp(10610, 5));
    }

    [Fact]
    public void AirshipRouteExpIsBaseExpectedMax()
    {
        var bronco = GameDataFixture.Bronco(data, 10);
        var route = new[] { data.Sector(VesselType.Airship, 0), data.Sector(VesselType.Airship, 1) };
        var exp = ExpModel.Route(bronco, route);
        Assert.Equal(21220u, exp.Guaranteed);
        Assert.Equal(53050u, exp.Maximum);
        Assert.InRange(exp.Average, exp.Guaranteed, exp.Maximum);
        Assert.True(ExpModel.IsEstimate(VesselType.Airship));
        Assert.False(ExpModel.IsEstimate(VesselType.Submarine));
    }
}

public class RankSimTests
{
    private readonly GameData data = GameDataFixture.Load();

    [Fact]
    public void ApplyRollsThroughRanks()
    {
        var r1 = data.Rank(VesselType.Airship, 1).ExpToNext; // 6180
        var r2 = data.Rank(VesselType.Airship, 2).ExpToNext; // 12504
        var (rank, rest) = RankSim.Apply(data, VesselType.Airship, 1, 100, r1 + r2 + 5);
        Assert.Equal(3, rank);
        Assert.Equal(105u, rest);
    }

    [Fact]
    public void ApplyCapsAtLastRank()
    {
        var (rank, rest) = RankSim.Apply(data, VesselType.Airship, 49, 0, 10_000_000);
        Assert.Equal(50, rank);
        Assert.Equal(0u, rest);
    }

    [Fact]
    public void OriginalRankInvertsApply()
    {
        var (rank, rest) = RankSim.Apply(data, VesselType.Submarine, 10, 500, 200_000);
        Assert.Equal(10, RankSim.OriginalRank(data, VesselType.Submarine, rank, rest, 200_000));
    }

    [Fact]
    public void VoyagesToRankRoundsUp()
    {
        var needed = RankSim.ExpToRank(data, VesselType.Airship, 1, 0, 3); // 6180 + 12504
        Assert.Equal(18684ul, needed);
        Assert.Equal(2, RankSim.VoyagesToRank(data, VesselType.Airship, 1, 0, 3, 10_000));
        Assert.Equal(0, RankSim.VoyagesToRank(data, VesselType.Airship, 5, 0, 3, 10_000));
    }
}

public class ProgressionTests
{
    private readonly GameData data = GameDataFixture.Load();

    private static HashSet<uint> Set(params uint[] ids) => ids.ToHashSet();

    [Fact]
    public void AirshipChainStartsAtAandB()
    {
        Assert.Null(Progression.UnlockedBy(VesselType.Airship, 0));
        Assert.Null(Progression.UnlockedBy(VesselType.Airship, 1));
        Assert.Equal(0u, Progression.UnlockedBy(VesselType.Airship, 2));
        Assert.Equal(new List<uint> { 1, 4, 6, 7 }, Progression.PathTo(VesselType.Airship, 7));
    }

    [Fact]
    public void AirshipSlotSectorsAreEightFourteenEighteen()
    {
        Assert.True(Progression.GrantsSlot(VesselType.Airship, 7));
        Assert.True(Progression.GrantsSlot(VesselType.Airship, 13));
        Assert.True(Progression.GrantsSlot(VesselType.Airship, 17));
        Assert.False(Progression.GrantsSlot(VesselType.Airship, 0));

        var g = Progression.GrantsOf(VesselType.Airship, 6);   // G unlocks H and J
        Assert.Equal(new List<uint> { 7, 9 }, g.Sectors);
        Assert.False(g.NewSlot);

        var h = Progression.GrantsOf(VesselType.Airship, 7);   // H unlocks I and registers airship #2
        Assert.Equal(new List<uint> { 8 }, h.Sectors);
        Assert.Equal(2, h.SlotNumber);
    }

    [Fact]
    public void NextAirshipStepFromScratchIsVisitAtoUnlockC()
    {
        var next = Progression.Next(data, VesselType.Airship, Set(0, 1), Set(), 1, 100, 1);
        Assert.NotNull(next);
        Assert.Equal(0u, next!.VisitSector);
        Assert.Contains(2u, next.Rewards.Sectors);
    }

    [Fact]
    public void NextAirshipStepRespectsRankAndReportsSlots()
    {
        // A-D unlocked: E (row 4) comes from B (rank 1).
        var next = Progression.Next(data, VesselType.Airship, Set(0, 1, 2, 3), Set(), 1, 100, 1);
        Assert.Equal(1u, next!.VisitSector);
        Assert.Contains(4u, next.Rewards.Sectors);

        // A-G unlocked: H comes from G (rank 17), so nothing at rank 5 ...
        Assert.Null(Progression.Next(data, VesselType.Airship, Set(0, 1, 2, 3, 4, 5, 6), Set(0, 1, 2, 3, 4, 5, 6), 5, 100, 1));
        // ... and G at rank 17.
        var g = Progression.Next(data, VesselType.Airship, Set(0, 1, 2, 3, 4, 5, 6), Set(0, 1, 2, 3, 4, 5, 6), 17, 100, 1);
        Assert.Equal(6u, g!.VisitSector);

        // A-H unlocked: exploring H (rank 20) unlocks I and registers the second airship.
        var h = Progression.Next(data, VesselType.Airship, Set(0, 1, 2, 3, 4, 5, 6, 7), Set(0, 1, 2, 3, 4, 5, 6), 20, 100, 1);
        Assert.Equal(7u, h!.VisitSector);
        Assert.Equal(2, h.Rewards.SlotNumber);
    }

    [Fact]
    public void DeadEndSlotSectorIsStillSuggested()
    {
        // N (row 13) unlocks nothing but registers airship #3; unlocked and unexplored → suggested.
        var all = Set(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 23, 24);
        var explored = new HashSet<uint>(all);
        explored.Remove(13);
        var next = Progression.Next(data, VesselType.Airship, all, explored, 50, 100, 1);
        Assert.Equal(13u, next!.VisitSector);
        Assert.Equal(3, next.Rewards.SlotNumber);
    }

    [Fact]
    public void SubmarineChainIsPorted()
    {
        Assert.Null(Progression.UnlockedBy(VesselType.Submarine, 1));
        Assert.Equal(1u, Progression.UnlockedBy(VesselType.Submarine, 3));
        Assert.True(Progression.MainLine(VesselType.Submarine).Count() > 100);
        Assert.Equal(2, Progression.SubmarineSlotNumber(10));   // J registers the 2nd submarine
        Assert.Equal(4, Progression.SubmarineSlotNumber(20));   // T the 4th
        Assert.True(Progression.GrantsOf(VesselType.Submarine, 30).NewMap); // AD opens the Sea of Ash

        var first = Progression.Next(data, VesselType.Submarine, Set(1, 2), Set(), 1, 100, 1);
        Assert.NotNull(first);
        Assert.Contains(first!.VisitSector, new uint[] { 1, 2 });
    }
}

public class RouteSearchTests
{
    private readonly GameData data = GameDataFixture.Load();

    private RouteRequest Request(Build build, IEnumerable<uint> unlocked, IEnumerable<uint>? must = null, TimeSpan? cap = null, RouteGoal goal = RouteGoal.ExpPerHour, int fuel = -1)
        => new(build.Type, 1, build, unlocked.ToHashSet(), (must ?? []).ToHashSet(), goal, cap, false, fuel);

    [Fact]
    public void AirshipRankOneOnlySeesAandB()
    {
        var bronco = GameDataFixture.Bronco(data, 1);
        var req = Request(bronco, new uint[] { 0, 1, 2 });
        var candidates = RouteSearch.Candidates(data, req);
        Assert.Equal(new uint[] { 0, 1 }, candidates.Select(c => c.Id).OrderBy(x => x));
    }

    [Fact]
    public void BestRouteRespectsRange()
    {
        var bronco = GameDataFixture.Bronco(data, 1); // range 70
        var best = RouteSearch.FindBest(data, Request(bronco, new uint[] { 0, 1 }));
        Assert.NotNull(best);
        Assert.True(best!.Distance <= bronco.Range);
        Assert.NotEmpty(best.Sectors);
    }

    [Fact]
    public void MustIncludeIsHonoured()
    {
        var bronco = GameDataFixture.Bronco(data, 10);
        var top = RouteSearch.FindTop(data, Request(bronco, new uint[] { 0, 1, 2, 3, 4 }, must: new uint[] { 3 }), 5);
        Assert.NotEmpty(top);
        Assert.All(top, r => Assert.Contains(3u, r.Sectors));
    }

    [Fact]
    public void DurationCapFiltersRoutes()
    {
        var bronco = GameDataFixture.Bronco(data, 10);
        var uncapped = RouteSearch.FindBest(data, Request(bronco, new uint[] { 0, 1, 2, 3, 4 }, goal: RouteGoal.ExpPerVoyage));
        var capped = RouteSearch.FindBest(data, Request(bronco, new uint[] { 0, 1, 2, 3, 4 }, cap: TimeSpan.FromHours(20), goal: RouteGoal.ExpPerVoyage));
        Assert.NotNull(uncapped);
        Assert.NotNull(capped);
        Assert.True(capped!.Duration <= TimeSpan.FromHours(20));
        Assert.True(capped.Exp.Guaranteed <= uncapped!.Exp.Guaranteed);
    }

    [Fact]
    public void FuelLimitFiltersRoutes()
    {
        var bronco = GameDataFixture.Bronco(data, 10);
        var best = RouteSearch.FindBest(data, Request(bronco, new uint[] { 0, 1, 2, 3, 4 }, fuel: 1));
        Assert.NotNull(best);
        Assert.Equal(1, best!.Fuel);
    }

    [Fact]
    public void ImpossibleMustIncludeReportsIssues()
    {
        var bronco = GameDataFixture.Bronco(data, 1);
        var req = Request(bronco, new uint[] { 0, 1 }, must: new uint[] { 7 });
        Assert.NotEmpty(RouteSearch.Issues(data, req));
        Assert.Empty(RouteSearch.FindTop(data, req, 3));
    }

    [Fact]
    public void SubmarineFullMapSearchFinishesQuickly()
    {
        var sub = Build.From(data, VesselType.Submarine, 145, 23, 24, 21, 22);
        var all = data.DestinationsOf(VesselType.Submarine, 1).Select(s => s.Id).ToHashSet();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var best = RouteSearch.FindBest(data, new RouteRequest(VesselType.Submarine, 1, sub, all, new HashSet<uint>(), RouteGoal.ExpPerHour, null, false));
        sw.Stop();
        Assert.NotNull(best);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), $"took {sw.Elapsed}");
    }

    [Fact]
    public void DescribeMatchesSearchForSameRoute()
    {
        var bronco = GameDataFixture.Bronco(data, 10);
        var best = RouteSearch.FindBest(data, Request(bronco, new uint[] { 0, 1, 2, 3, 4 }))!;
        var described = RouteSearch.Describe(data, bronco, 1, best.Sectors, RouteGoal.ExpPerHour, false);
        Assert.Equal(best.Distance, described.Distance);
        Assert.Equal(best.Duration, described.Duration);
        Assert.Equal(best.Exp, described.Exp);
    }
}

public class ClientReadoutTests
{
    private readonly GameData data = GameDataFixture.Load();

    // (row, distance, travel seconds, survey seconds) from HousingManager.GetAirshipVoyageTimeAndDistance /
    // GetAirshipSurveyDuration at speed 100, start point 127, captured in-game on 2026-09-05.
    private static readonly (uint Row, int Distance, int Seconds, int Survey)[] AirshipStartLegs =
    {
        (0, 7, 5280, 7560), (1, 9, 6540, 7560), (2, 18, 12600, 7560), (3, 16, 11340, 7560),
        (4, 10, 7380, 15120), (5, 16, 11160, 15120), (6, 15, 10740, 15120),
        (7, 19, 13740, 22680), (8, 23, 15960, 22680), (9, 19, 13620, 22680),
        (10, 24, 16740, 30240), (11, 26, 18300, 30240), (12, 29, 20280, 30240),
        (13, 24, 16860, 37800), (14, 36, 24840, 37800), (15, 32, 22500, 37800),
        (16, 33, 22860, 60480), (17, 29, 19980, 60480), (18, 38, 26220, 60480), (19, 40, 28260, 60480),
        (20, 37, 25920, 60480), (21, 43, 30180, 60480), (23, 45, 31620, 60480), (24, 28, 19860, 60480),
    };

    [Fact]
    public void AirshipFormulasMatchTheClientReadout()
    {
        var start = data.AirshipStart;
        foreach (var (row, distance, seconds, survey) in AirshipStartLegs)
        {
            var s = data.Sector(VesselType.Airship, row);
            Assert.Equal(distance, VoyageMath.LegDistanceFormula(start, s));
            Assert.InRange(VoyageMath.LegSecondsFormula(start, s, 100), seconds - 60, seconds + 60);
            Assert.Equal(survey, VoyageMath.SurveySecondsFormula(s, 100));
        }
    }

    [Fact]
    public void OverridesTakePrecedenceOverFormulas()
    {
        var start = data.AirshipStart;
        var a = data.Sector(VesselType.Airship, 0);
        try
        {
            VoyageMath.LegOverride = (_, _, _) => (123, 4560);
            VoyageMath.SurveyOverride = (_, _) => 789;
            Assert.Equal(123 + a.SurveyDistance, VoyageMath.RouteDistance(start, new[] { a }));
            Assert.Equal(4560 + 789 + VoyageMath.FixedVoyageSeconds, (int)VoyageMath.RouteDuration(start, new[] { a }, 100).TotalSeconds);
        }
        finally
        {
            VoyageMath.LegOverride = null;
            VoyageMath.SurveyOverride = null;
        }

        Assert.Equal(7 + a.SurveyDistance, VoyageMath.RouteDistance(start, new[] { a }));
    }
}

public class UnlockFocusTests
{
    private readonly GameData data = GameDataFixture.Load();

    [Fact]
    public void ShortestVoyageGoalPrefersTheShortRoute()
    {
        var bronco = GameDataFixture.Bronco(data, 10);
        var req = new RouteRequest(VesselType.Airship, 1, bronco, new HashSet<uint> { 0, 1, 2, 3, 4 }, new HashSet<uint> { 1 },
            RouteGoal.ShortestVoyage, null, false);
        var top = RouteSearch.FindTop(data, req, 5);
        Assert.NotEmpty(top);
        Assert.Equal(new uint[] { 1 }, top[0].Sectors);
        Assert.True(top.Zip(top.Skip(1)).All(pair => pair.First.Duration <= pair.Second.Duration));
    }

    [Fact]
    public void UnlockBuildsRankSurveillanceTierFirst()
    {
        // Airship sector B (row 1): tier 2 at 46, tier 3 at 54, no favor line.
        var top = PartOptimizer.BestForUnlock(data, VesselType.Airship, 50, 1, new uint[] { 1 }, 1, 10);
        Assert.NotEmpty(top);
        Assert.Equal(2, top[0].SurveillanceTier);
        Assert.True(top[0].Build.Surveillance >= 54);
        Assert.True(top[0].Distance <= top[0].Build.Range);
        Assert.True(top.Zip(top.Skip(1)).All(pair => pair.First.SurveillanceTier >= pair.Second.SurveillanceTier));
    }

    [Fact]
    public void UnlockBuildsHonourFavorLineAndRolls()
    {
        // Submarine sector A (row 1): T2 20, T3 80, favor 70. A Shark at rank 1 has favor 70 → double-dip rolls.
        var top = PartOptimizer.BestForUnlock(data, VesselType.Submarine, 1, 1, new uint[] { 1 }, 1, 5);
        Assert.NotEmpty(top);
        Assert.True(top[0].FavorMet);
        Assert.True(top[0].RollsPerDay > 24.0 / top[0].Duration.TotalHours * 1.5);
    }

    [Fact]
    public void SurveillanceTierUsesThresholds()
    {
        var t = ExpModel.ThresholdsFor(VesselType.Submarine, 1); // T2 20, T3 80
        Assert.Equal(0, PartOptimizer.SurveillanceTier(t, 10));
        Assert.Equal(1, PartOptimizer.SurveillanceTier(t, 20));
        Assert.Equal(2, PartOptimizer.SurveillanceTier(t, 80));
        Assert.Equal(0, PartOptimizer.SurveillanceTier(default, 999));
    }
}
