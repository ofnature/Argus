using System;
using System.IO;
using System.Text;
using Argus.Core.Model;
using Argus.Core.Store;

namespace Argus.Tests;

public class FleetSyncTests : IDisposable
{
    private const ulong Fc = 0x80CB8000000084FB;

    private readonly string dir = Path.Combine(Path.GetTempPath(), "argus-tests", Guid.NewGuid().ToString("N"));

    private static readonly DateTime Seen = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    private static FreeCompanyRecord Company() => new()
    {
        Id = Fc,
        Tag = "Void",
        World = "Maduin",
        CharacterName = "Roomie Mate",
        ContentId = 0x0123456789ABCDEF,
        CeruleumTanks = 269,
        MagitekRepairMaterials = 40,
        SuppliesSeenUtc = Seen,
    };

    private static Vessel Sub(int slot, string name, DateTime seen, uint returnTime = 1789875836) => new()
    {
        FreeCompanyId = Fc,
        Type = VesselType.Submarine,
        Slot = slot,
        Name = name,
        Rank = 43,
        CurrentExp = 578390,
        NextLevelExp = 1200000,
        Hull = 3,
        Stern = 4,
        Bow = 1,
        Bridge = 2,
        RegisterTime = 1789800000,
        ReturnTime = returnTime,
        Points = [15, 16, 20],
        Surveillance = 120,
        Retrieval = 110,
        Speed = 130,
        Range = 140,
        Favor = 105,
        Condition = [22419, 22419, 22419, 22419],
        LastSeenUtc = seen,
    };

    [Fact]
    public void RoundTripsAVessel()
    {
        var sent = Sub(2, "Money_go_bye", Seen);
        var message = FleetSyncMessage.Parse(FleetSyncMessage.From(Company(), sent).ToJson());

        Assert.NotNull(message);
        var received = message.ToVessel();
        Assert.True(received.SameState(sent));
        Assert.Equal(Seen, received.LastSeenUtc);
        Assert.Equal("Void", message.Tag);
        Assert.Equal(269, message.Tanks);
        Assert.Equal(Seen, message.SuppliesSeenUtc);
    }

    [Fact]
    public void NeverCarriesTheContentId()
    {
        var json = FleetSyncMessage.From(Company(), Sub(0, "Leviathan", Seen)).ToJson();
        Assert.DoesNotContain(0x0123456789ABCDEFUL.ToString(), json);
        Assert.DoesNotContain("ContentId", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"v":2,"fc":1,"t":0,"s":0,"n":"A","r":1,"p":[1,2,3,4],"st":[1,1,1,1,1],"c":[0,0,0,0],"ls":1}""")]
    [InlineData("""{"v":1,"fc":1,"t":0,"s":7,"n":"A","r":1,"p":[1,2,3,4],"st":[1,1,1,1,1],"c":[0,0,0,0],"ls":1}""")]
    [InlineData("""{"v":1,"fc":1,"t":9,"s":0,"n":"A","r":1,"p":[1,2,3,4],"st":[1,1,1,1,1],"c":[0,0,0,0],"ls":1}""")]
    [InlineData("""{"v":1,"fc":1,"t":0,"s":0,"n":"","r":1,"p":[1,2,3,4],"st":[1,1,1,1,1],"c":[0,0,0,0],"ls":1}""")]
    [InlineData("""{"v":1,"fc":1,"t":0,"s":0,"n":"A","r":1,"p":[1,2],"st":[1,1,1,1,1],"c":[0,0,0,0],"ls":1}""")]
    [InlineData("""{"v":1,"fc":1,"t":0,"s":0,"n":"A","r":1,"p":[1,2,3,4],"st":[1,1,1,1,1],"c":[0,0,0,99999],"ls":1}""")]
    public void RejectsWhatIsNotAValidVessel(string json)
        => Assert.Null(FleetSyncMessage.Parse(json));

    [Fact]
    public void IgnoresKeysItDoesNotKnow()
    {
        // Extend-only: a newer Argus may add keys, and this one must still read the vessel.
        var json = FleetSyncMessage.From(Company(), Sub(0, "Leviathan", Seen)).ToJson().Replace("{\"v\":1,", "{\"v\":1,\"future\":[1,2],");
        Assert.NotNull(FleetSyncMessage.Parse(json));
    }

    /// <summary>
    /// Daedalus puts the message in a relay payload and that in its LAN envelope, both with System.Text.Json's default
    /// escaping, which writes every quote as ". A UDP datagram larger than one Ethernet frame (1472 bytes of
    /// payload) is split, and a broadcast that loses either half is lost whole.
    /// </summary>
    [Fact]
    public void FitsInOneNetworkFrameAfterDaedalusWrapsIt()
    {
        var fc = Company();
        fc.Tag = "Voids";
        fc.World = "Ravana";
        fc.CharacterName = "Firstnameabc Lastname";
        var vessel = Sub(3, "Twenty_char_name_xyz", Seen, uint.MaxValue);
        vessel.Points = [100, 101, 102, 103, 104];
        vessel.Condition = [29999, 29999, 29999, 29999];

        var data = FleetSyncMessage.From(fc, vessel).ToJson();
        var relay = System.Text.Json.JsonSerializer.Serialize(new { ch = "argus.fleet", data });
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

    [Fact]
    public void NewerReadingReplacesAndOlderIsIgnored()
    {
        var store = new FleetStore(dir);
        store.UpdateVessels(Fc, [Sub(0, "Leviathan", Seen, returnTime: 100)], Seen);

        // The FC label and supplies still file (this client never had them), but the vessel keeps its newer reading.
        var older = FleetSyncMessage.From(Company(), Sub(0, "Leviathan", Seen.AddMinutes(-5), returnTime: 200));
        store.MergeRemote(older);
        Assert.Equal(100u, store.GetOrCreate(Fc).Vessels[0].ReturnTime);

        var newer = FleetSyncMessage.From(Company(), Sub(0, "Leviathan", Seen.AddMinutes(5), returnTime: 300));
        Assert.True(store.MergeRemote(newer));
        Assert.Equal(300u, store.GetOrCreate(Fc).Vessels[0].ReturnTime);

        // The same reading heard again (another client relaying it) changes nothing.
        Assert.False(store.MergeRemote(newer));
    }

    [Fact]
    public void UnknownCompanyArrivesWithItsLabelAndSupplies()
    {
        var store = new FleetStore(dir);
        Assert.True(store.MergeRemote(FleetSyncMessage.From(Company(), Sub(1, "Nautilus", Seen))));

        var record = store.GetOrCreate(Fc);
        Assert.Equal("Void", record.Tag);
        Assert.Equal("Roomie Mate", record.CharacterName);
        Assert.Equal(269, record.CeruleumTanks);
        Assert.Equal(0UL, record.ContentId);
        Assert.Single(record.Vessels);
    }

    [Fact]
    public void SuppliesTakeTheNewerCount()
    {
        var store = new FleetStore(dir);
        var record = store.GetOrCreate(Fc);
        record.CeruleumTanks = 10;
        record.MagitekRepairMaterials = 2;
        record.SuppliesSeenUtc = Seen.AddHours(1);

        // Counted before this client's own count: ignored.
        store.MergeRemote(FleetSyncMessage.From(Company(), Sub(0, "Leviathan", Seen)));
        Assert.Equal(10, record.CeruleumTanks);

        var later = Company();
        later.SuppliesSeenUtc = Seen.AddHours(2);
        store.MergeRemote(FleetSyncMessage.From(later, Sub(0, "Leviathan", Seen.AddHours(2))));
        Assert.Equal(269, record.CeruleumTanks);
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
