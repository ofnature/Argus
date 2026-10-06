using System;
using Argus.Core.Model;
using Newtonsoft.Json;

namespace Argus.Core.Store;

/// <summary>
/// One vessel as shared over the Daedalus LAN relay, with enough of its Free Company to file it.
///
/// <para>One vessel per message, with short keys and Unix-second times: Daedalus wraps the payload twice with
/// System.Text.Json's default escaping, so every quote here costs seven bytes on the wire, and a datagram that
/// outgrows one network frame is more likely to be lost. <c>FleetSyncTests</c> holds the size to one frame.</para>
///
/// <para>Extend-only: receivers ignore keys they do not know, so keys may be added but never renamed or removed.
/// Never carries the character's ContentId.</para>
/// </summary>
public sealed class FleetSyncMessage
{
    public const int CurrentVersion = 1;

    [JsonProperty("v")] public int Version = CurrentVersion;

    [JsonProperty("fc")] public ulong Fc;
    [JsonProperty("tag")] public string Tag = string.Empty;
    [JsonProperty("w")] public string World = string.Empty;

    /// <summary>The character whose client last read the workshop, for the FC label.</summary>
    [JsonProperty("by")] public string Character = string.Empty;

    [JsonProperty("tk")] public int Tanks = -1;
    [JsonProperty("kt")] public int Kits = -1;
    [JsonProperty("ss")] public long SuppliesSeen;

    [JsonProperty("t")] public VesselType Type;
    [JsonProperty("s")] public int Slot;
    [JsonProperty("n")] public string Name = string.Empty;
    [JsonProperty("r")] public int Rank;
    [JsonProperty("x")] public uint Exp;
    [JsonProperty("nx")] public uint NextExp;
    [JsonProperty("p")] public ushort[] Parts = new ushort[4];
    [JsonProperty("rg")] public uint RegisterTime;
    [JsonProperty("rt")] public uint ReturnTime;
    [JsonProperty("pt")] public uint[] Points = [];
    [JsonProperty("st")] public int[] Stats = new int[5];
    [JsonProperty("c")] public int[] Condition = { -1, -1, -1, -1 };

    /// <summary>When the vessel was last read in a workshop, in Unix seconds: the newer reading wins.</summary>
    [JsonProperty("ls")] public long LastSeen;

    [JsonIgnore]
    public DateTime LastSeenUtc => DateTimeOffset.FromUnixTimeSeconds(LastSeen).UtcDateTime;

    [JsonIgnore]
    public DateTime SuppliesSeenUtc => SuppliesSeen <= 0 ? default : DateTimeOffset.FromUnixTimeSeconds(SuppliesSeen).UtcDateTime;

    public static FleetSyncMessage From(FreeCompanyRecord fc, Vessel v) => new()
    {
        Fc = fc.Id,
        Tag = fc.Tag,
        World = fc.World,
        Character = fc.CharacterName,
        Tanks = fc.CeruleumTanks,
        Kits = fc.MagitekRepairMaterials,
        SuppliesSeen = fc.SuppliesSeenUtc == default ? 0 : UnixSeconds(fc.SuppliesSeenUtc),
        Type = v.Type,
        Slot = v.Slot,
        Name = v.Name,
        Rank = v.Rank,
        Exp = v.CurrentExp,
        NextExp = v.NextLevelExp,
        Parts = [v.Hull, v.Stern, v.Bow, v.Bridge],
        RegisterTime = v.RegisterTime,
        ReturnTime = v.ReturnTime,
        Points = v.Points.ToArray(),
        Stats = [v.Surveillance, v.Retrieval, v.Speed, v.Range, v.Favor],
        Condition = (int[])v.Condition.Clone(),
        LastSeen = UnixSeconds(v.LastSeenUtc),
    };

    /// <summary>Stored times are UTC whatever kind they were loaded with; a local kind would shift them by the zone.</summary>
    private static long UnixSeconds(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();

    public Vessel ToVessel() => new()
    {
        FreeCompanyId = Fc,
        Type = Type,
        Slot = Slot,
        Name = Name,
        Rank = Rank,
        CurrentExp = Exp,
        NextLevelExp = NextExp,
        Hull = Parts[0],
        Stern = Parts[1],
        Bow = Parts[2],
        Bridge = Parts[3],
        RegisterTime = RegisterTime,
        ReturnTime = ReturnTime,
        Points = [.. Points],
        Surveillance = Stats[0],
        Retrieval = Stats[1],
        Speed = Stats[2],
        Range = Stats[3],
        Favor = Stats[4],
        Condition = (int[])Condition.Clone(),
        LastSeenUtc = LastSeenUtc,
    };

    /// <summary>
    /// Only a well-formed vessel goes into the store: anything on the LAN can publish on this channel, and the store
    /// feeds the planner and the info bar.
    /// </summary>
    [JsonIgnore]
    public bool IsValid
        => Version == CurrentVersion
           && Fc != 0
           && Enum.IsDefined(Type)
           && Slot is >= 0 and < 4
           && Name is { Length: > 0 and <= 32 }
           && Rank is > 0 and <= 255
           && Parts is { Length: 4 }
           && Points is { Length: <= 5 }
           && Stats is { Length: 5 }
           && Condition is { Length: 4 }
           && Array.TrueForAll(Condition, c => c is >= -1 and <= Vessel.FullCondition)
           && LastSeen > 0
           && Tag is not null && World is not null && Character is not null;

    public string ToJson() => JsonConvert.SerializeObject(this);

    /// <summary>The message, or null when the text is not a valid one.</summary>
    public static FleetSyncMessage? Parse(string json)
    {
        try
        {
            var message = JsonConvert.DeserializeObject<FleetSyncMessage>(json);
            return message is { IsValid: true } ? message : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
