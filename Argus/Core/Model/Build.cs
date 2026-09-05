using System;
using System.Collections.Generic;
using System.Linq;

namespace Argus.Core.Model;

/// <summary>
/// A vessel configuration: rank plus four parts, with the resulting stats. Submarines add the rank's flat bonuses on
/// top of the parts; airships get stats from parts only (rank just raises airframe capacity).
/// </summary>
public sealed record Build(VesselType Type, int Rank, PartInfo Hull, PartInfo Stern, PartInfo Bow, PartInfo Bridge, RankInfo RankRow)
{
    public static Build From(GameData data, VesselType type, int rank, uint hull, uint stern, uint bow, uint bridge)
        => new(type, rank, data.Part(type, hull), data.Part(type, stern), data.Part(type, bow), data.Part(type, bridge), data.Rank(type, rank));

    public static Build From(GameData data, Vessel vessel)
        => From(data, vessel.Type, vessel.Rank, vessel.Hull, vessel.Stern, vessel.Bow, vessel.Bridge);

    public Build WithRank(GameData data, int rank) => this with { Rank = rank, RankRow = data.Rank(Type, rank) };

    public IEnumerable<PartInfo> Parts
    {
        get
        {
            yield return Hull;
            yield return Stern;
            yield return Bow;
            yield return Bridge;
        }
    }

    public int Surveillance => RankRow.SurveillanceBonus + Hull.Surveillance + Stern.Surveillance + Bow.Surveillance + Bridge.Surveillance;
    public int Retrieval => RankRow.RetrievalBonus + Hull.Retrieval + Stern.Retrieval + Bow.Retrieval + Bridge.Retrieval;
    public int Speed => RankRow.SpeedBonus + Hull.Speed + Stern.Speed + Bow.Speed + Bridge.Speed;
    public int Range => RankRow.RangeBonus + Hull.Range + Stern.Range + Bow.Range + Bridge.Range;
    public int Favor => RankRow.FavorBonus + Hull.Favor + Stern.Favor + Bow.Favor + Bridge.Favor;

    public int Cost => Hull.Components + Stern.Components + Bow.Components + Bridge.Components;
    public int Capacity => RankRow.Capacity;
    public bool FitsCapacity => Cost <= Capacity;
    public int RepairMaterials => Hull.RepairMaterials + Stern.RepairMaterials + Bow.RepairMaterials + Bridge.RepairMaterials;
    public int HighestPartRank => Parts.Max(p => p.Rank);

    /// <summary>Every part is usable at this rank.</summary>
    public bool PartsAllowed => Parts.All(p => p.Rank <= Rank);

    /// <summary>Short identifier like "WSSC" / "WSSC+" for subs, or class letters for airships.</summary>
    public string Identifier => Type == VesselType.Submarine ? SubmarineIdentifier() : AirshipIdentifier();

    private string SubmarineIdentifier()
    {
        var parts = Parts.Select(p => SubmarinePartLetter(p.Id)).ToArray();
        var id = string.Concat(parts);
        return id.Count(c => c == '+') == 4 ? id.Replace("+", string.Empty) + "++" : id;
    }

    /// <summary>Submarine part row ids run 1-40: five classes × four slots, then the same five classes modified (+20).</summary>
    public static string SubmarinePartLetter(uint partId)
    {
        var cls = (int)((partId - 1) / 4);
        return cls switch
        {
            0 => "S",
            1 => "U",
            2 => "W",
            3 => "C",
            4 => "Y",
            >= 5 and <= 9 => SubmarinePartLetter(partId - 20) + "+",
            _ => "?",
        };
    }

    private string AirshipIdentifier()
        => string.Concat(Parts.Select(p => AirshipClassLetter(p.Class)));

    /// <summary>AirshipExplorationPart.Class: 1 Bronco, 4 Invincible, 2 Enterprise, 5 Invincible II, 3 Odyssey, 6 Tatanora, 7 Viltgance.</summary>
    public static string AirshipClassLetter(int cls) => cls switch
    {
        1 => "B",
        4 => "I",
        2 => "E",
        5 => "I2",
        3 => "O",
        6 => "T",
        7 => "V",
        _ => "?",
    };

    public static string AirshipClassName(int cls) => cls switch
    {
        1 => "Bronco",
        4 => "Invincible",
        2 => "Enterprise",
        5 => "Invincible II",
        3 => "Odyssey",
        6 => "Tatanora",
        7 => "Viltgance",
        _ => "Unknown",
    };

    public static string SubmarineClassName(uint partId)
    {
        var cls = (int)((partId - 1) / 4) % 5;
        var modified = partId > 20;
        var name = cls switch
        {
            0 => "Shark",
            1 => "Unkiu",
            2 => "Whale",
            3 => "Coelacanth",
            4 => "Syldra",
            _ => "Unknown",
        };
        return modified ? $"Modified {name}" : name;
    }

    public static string SlotName(VesselType type, int slot) => (type, slot) switch
    {
        (VesselType.Submarine, 0) => "Hull",
        (VesselType.Submarine, 1) => "Stern",
        (VesselType.Submarine, 2) => "Bow",
        (VesselType.Submarine, 3) => "Bridge",
        (VesselType.Airship, 0) => "Hull",
        (VesselType.Airship, 1) => "Rigging",
        (VesselType.Airship, 2) => "Forecastle",
        (VesselType.Airship, 3) => "Aftcastle",
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };

    public bool SameParts(Build other)
        => Hull.Id == other.Hull.Id && Stern.Id == other.Stern.Id && Bow.Id == other.Bow.Id && Bridge.Id == other.Bridge.Id;
}
