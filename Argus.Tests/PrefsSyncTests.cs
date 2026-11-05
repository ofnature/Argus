using System.Text;
using Argus;
using Argus.Core.Calc;
using Argus.Core.Model;
using Argus.Core.Store;

namespace Argus.Tests;

/// <summary>The settings master's planner settings, as they travel over the LAN to the PCs that follow it.</summary>
public class PrefsSyncTests
{
    private const ulong Fc = 0x1234_5678_9ABC;

    private static PlannerPrefs GilFarmer() => new()
    {
        Goal = RouteGoal.ExpPerVoyage,
        DurationCapHours = 48,
        ProgressionAutoInclude = false,
        FarmGil = true,
        GilTier = 2,
        Map = 1,
    };

    [Fact]
    public void SettingsComeBackAsSent()
    {
        var sent = PrefsSyncMessage.From("Korha Example", Fc, VesselType.Submarine, 2, GilFarmer());
        var heard = PrefsSyncMessage.Parse(sent.ToJson());

        Assert.NotNull(heard);
        Assert.Equal("Korha Example", heard!.Master);
        Assert.Equal($"{Fc}:Submarine:2", heard.Key);
        Assert.True(PrefsSyncMessage.Same(GilFarmer(), heard.ToPrefs()));
        Assert.False(PrefsSyncMessage.Same(new PlannerPrefs(), heard.ToPrefs()));
    }

    [Fact]
    public void TheKeyIsTheOneTheConfigurationFilesUnder()
    {
        var vessel = new Vessel { FreeCompanyId = Fc, Type = VesselType.Airship, Slot = 0 };
        var key = Configuration.VesselKey(vessel);

        Assert.True(PrefsSyncMessage.TryParseKey(key, out var fc, out var type, out var slot));
        Assert.Equal((Fc, VesselType.Airship, 0), (fc, type, slot));
        Assert.Equal(key, PrefsSyncMessage.From("Korha Example", fc, type, slot, new PlannerPrefs()).Key);

        Assert.False(PrefsSyncMessage.TryParseKey("0:Submarine:1", out _, out _, out _));
        Assert.False(PrefsSyncMessage.TryParseKey($"{Fc}:Rowboat:1", out _, out _, out _));
        Assert.False(PrefsSyncMessage.TryParseKey($"{Fc}:Submarine:4", out _, out _, out _));
    }

    [Fact]
    public void OnlyWellFormedSettingsAreTaken()
    {
        Assert.Null(PrefsSyncMessage.Parse("not json"));
        Assert.Null(PrefsSyncMessage.Parse(PrefsSyncMessage.From("", Fc, VesselType.Submarine, 0, GilFarmer()).ToJson()));

        var badTier = PrefsSyncMessage.From("Korha Example", Fc, VesselType.Submarine, 0, GilFarmer());
        badTier.GilTier = 9;
        Assert.Null(PrefsSyncMessage.Parse(badTier.ToJson()));

        var badSlot = PrefsSyncMessage.From("Korha Example", Fc, VesselType.Submarine, 0, GilFarmer());
        badSlot.Slot = 7;
        Assert.Null(PrefsSyncMessage.Parse(badSlot.ToJson()));
    }

    [Fact]
    public void MasterNamesMatchIgnoringCaseAndSpacesButNeverEmpty()
    {
        Assert.True(PrefsSyncMessage.SameName("Korha Example", " korha example "));
        Assert.False(PrefsSyncMessage.SameName("Korha Example", "Korha Other"));
        Assert.False(PrefsSyncMessage.SameName("", ""));
        Assert.False(PrefsSyncMessage.SameName(null, "Korha Example"));
    }

    /// <summary>The same envelope Daedalus builds around a relay payload (see FleetSyncTests): one Ethernet frame.</summary>
    [Fact]
    public void FitsInOneNetworkFrameAfterDaedalusWrapsIt()
    {
        var prefs = GilFarmer();
        prefs.FarmItem = uint.MaxValue;
        prefs.Map = uint.MaxValue;
        var data = PrefsSyncMessage.From("Firstnameabc Lastnameabcdefgh", ulong.MaxValue, VesselType.Submarine, 3, prefs).ToJson();
        var relay = System.Text.Json.JsonSerializer.Serialize(new { ch = "argus.prefs", data });
        var envelope = System.Text.Json.JsonSerializer.Serialize(new
        {
            s = "Firstnameabc Lastname@Ravana",
            m = Guid.NewGuid().ToString(),
            t = 13,
            p = relay,
            ts = long.MaxValue,
            v = 1,
        });

        var bytes = Encoding.UTF8.GetByteCount(envelope);
        Assert.True(bytes <= 1472, $"{bytes} bytes on the wire");
    }
}
