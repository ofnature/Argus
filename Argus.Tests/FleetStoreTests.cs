using System;
using System.IO;
using System.Linq;
using Argus.Core.Model;
using Argus.Core.Store;

namespace Argus.Tests;

public class FleetStoreTests : IDisposable
{
    private const ulong Fc = 1;

    private readonly string dir = Path.Combine(Path.GetTempPath(), "argus-tests", Guid.NewGuid().ToString("N"));

    private static Vessel Make(VesselType type, int slot, string name, uint returnTime = 0) => new()
    {
        FreeCompanyId = Fc,
        Type = type,
        Slot = slot,
        Name = name,
        Rank = 10,
        ReturnTime = returnTime,
        LastSeenUtc = DateTime.UtcNow,
    };

    [Fact]
    public void PartialReadKeepsVesselsItCouldNotSee()
    {
        var store = new FleetStore(dir);
        var now = DateTime.UtcNow;
        store.UpdateVessels(Fc, [Make(VesselType.Submarine, 0, "Leviathan"), Make(VesselType.Submarine, 1, "Nautilus"), Make(VesselType.Airship, 0, "Company Airship-1")], now);
        Assert.Equal(3, store.GetOrCreate(Fc).Vessels.Count);

        // The client reported only the airship this tick; the submarines must survive it.
        store.UpdateVessels(Fc, [Make(VesselType.Airship, 0, "Company Airship-1")], now);

        var vessels = store.GetOrCreate(Fc).Vessels;
        Assert.Equal(3, vessels.Count);
        Assert.Equal(2, vessels.Count(v => v.Type == VesselType.Submarine));
    }

    [Fact]
    public void ChangedVesselReplacesTheStoredOne()
    {
        var store = new FleetStore(dir);
        var now = DateTime.UtcNow;
        store.UpdateVessels(Fc, [Make(VesselType.Submarine, 0, "Leviathan")], now);

        Assert.False(store.UpdateVessels(Fc, [Make(VesselType.Submarine, 0, "Leviathan")], now));
        Assert.True(store.UpdateVessels(Fc, [Make(VesselType.Submarine, 0, "Leviathan", 12345)], now));
        Assert.Equal(12345u, store.GetOrCreate(Fc).Vessels[0].ReturnTime);
    }

    [Fact]
    public void UnchangedVesselStillCountsAsSeen()
    {
        var store = new FleetStore(dir);
        var stale = DateTime.UtcNow.AddHours(-2);
        store.UpdateVessels(Fc, [Make(VesselType.Submarine, 0, "Leviathan")], stale);
        store.GetOrCreate(Fc).Vessels[0].LastSeenUtc = stale;

        var now = DateTime.UtcNow;
        store.UpdateVessels(Fc, [Make(VesselType.Submarine, 0, "Leviathan")], now);
        Assert.Equal(now, store.GetOrCreate(Fc).Vessels[0].LastSeenUtc);
    }

    public void Dispose()
    {
        if (Directory.Exists(dir))
            Directory.Delete(dir, true);
    }
}
