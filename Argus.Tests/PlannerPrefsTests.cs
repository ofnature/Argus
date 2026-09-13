using Argus.Core.Calc;
using Argus.Core.Model;

namespace Argus.Tests;

public class PlannerPrefsTests
{
    private static Vessel Sub(int slot, string name) => new() { FreeCompanyId = 7, Type = VesselType.Submarine, Slot = slot, Name = name };

    [Fact]
    public void EachVesselKeepsItsOwnRouteType()
    {
        var config = new Configuration();
        var progression = Sub(0, "Leviathan");
        var farming = Sub(1, "Nautilus");
        var levelling = Sub(2, "Money_go_bye");

        config.PlannerFor(progression).UnlockFocus = true;
        config.PlannerFor(farming).FarmItem = 12925;
        config.PlannerFor(levelling).Goal = RouteGoal.ExpPerVoyage;

        Assert.True(config.PlannerFor(progression).UnlockFocus);
        Assert.Equal(0u, config.PlannerFor(progression).FarmItem);

        Assert.Equal(12925u, config.PlannerFor(farming).FarmItem);
        Assert.False(config.PlannerFor(farming).UnlockFocus);

        Assert.Equal(RouteGoal.ExpPerVoyage, config.PlannerFor(levelling).Goal);
        Assert.Equal(0u, config.PlannerFor(levelling).FarmItem);
    }

    [Fact]
    public void NewVesselStartsFromTheSharedSettingsWithoutChangingThem()
    {
        var config = new Configuration();
        config.SubmarinePlanner.DurationCapHours = 36;
        config.AirshipPlanner.Goal = RouteGoal.ExpPerVoyage;

        var sub = config.PlannerFor(Sub(0, "Leviathan"));
        var airship = config.PlannerFor(new Vessel { FreeCompanyId = 7, Type = VesselType.Airship, Slot = 0 });
        Assert.Equal(36, sub.DurationCapHours);
        Assert.Equal(RouteGoal.ExpPerVoyage, airship.Goal);

        // A copy, not a reference: editing one vessel must not leak into the defaults or other vessels.
        sub.DurationCapHours = 12;
        Assert.Equal(36, config.SubmarinePlanner.DurationCapHours);
        Assert.Equal(36, config.PlannerFor(Sub(1, "Nautilus")).DurationCapHours);
    }

    [Fact]
    public void SettingsFollowTheSlotNotTheName()
    {
        var config = new Configuration();
        config.PlannerFor(Sub(1, "Nautilus")).FarmItem = 10099;
        Assert.Equal(10099u, config.PlannerFor(Sub(1, "Renamed")).FarmItem);

        // Same slot in another Free Company is a different vessel.
        Assert.Equal(0u, config.PlannerFor(new Vessel { FreeCompanyId = 8, Type = VesselType.Submarine, Slot = 1 }).FarmItem);
    }
}
