using System.Collections.Generic;
using System.Linq;

namespace Argus.Core.Model;

/// <summary>A voyage destination (submarine or airship), as plain data so the calculators run without Lumina.</summary>
public sealed record SectorInfo(
    uint Id,
    VesselType Type,
    uint Map,
    string Name,
    string Letter,
    uint Exp,
    int Fuel,
    int SurveyMinutes,
    int SurveyDistance,
    int X,
    int Y,
    int Z,
    int RankReq,
    int SurveillanceReq,
    int Stars,
    bool IsStart)
{
    /// <summary>Airship row 22 (the Diadem) is a passenger destination, never part of a voyage route.</summary>
    public bool Passengers { get; init; }

    public bool IsDestination => !IsStart && !Passengers && RankReq > 0;
}

/// <summary>One part (hull/stern/bow/bridge; airships: hull/rigging/forecastle/aftcastle in the same slot order).</summary>
public sealed record PartInfo(
    uint Id,
    VesselType Type,
    int Slot,
    int Class,
    int Rank,
    int Surveillance,
    int Retrieval,
    int Speed,
    int Range,
    int Favor,
    int Components,
    int RepairMaterials);

/// <summary>One rank row. Submarine ranks add flat stat bonuses; airship ranks only raise capacity.</summary>
public sealed record RankInfo(
    int Rank,
    VesselType Type,
    uint ExpToNext,
    int Capacity,
    int SurveillanceBonus,
    int RetrievalBonus,
    int SpeedBonus,
    int RangeBonus,
    int FavorBonus);

public sealed record MapInfo(uint Id, string Name);

/// <summary>
/// The static game tables the calculators need, keyed for fast lookup. Built from Lumina in-game and from the JSON
/// fixture in tests. Never contains anything per-FC.
/// </summary>
public sealed class GameData
{
    public IReadOnlyDictionary<uint, SectorInfo> SubmarineSectors { get; }
    public IReadOnlyDictionary<uint, SectorInfo> AirshipSectors { get; }
    public IReadOnlyDictionary<uint, PartInfo> SubmarineParts { get; }
    public IReadOnlyDictionary<uint, PartInfo> AirshipParts { get; }
    public IReadOnlyList<RankInfo> SubmarineRanks { get; }
    public IReadOnlyList<RankInfo> AirshipRanks { get; }
    public IReadOnlyList<MapInfo> Maps { get; }

    /// <summary>Submarine start sector per map, keyed by map id.</summary>
    public IReadOnlyDictionary<uint, SectorInfo> SubmarineStarts { get; }

    public SectorInfo AirshipStart { get; }

    public int LastSubmarineRank { get; }
    public int LastAirshipRank { get; }

    public GameData(IEnumerable<SectorInfo> sectors, IEnumerable<PartInfo> parts, IEnumerable<RankInfo> ranks, IEnumerable<MapInfo> maps)
    {
        var sectorList = sectors.ToList();
        SubmarineSectors = sectorList.Where(s => s.Type == VesselType.Submarine).ToDictionary(s => s.Id);
        AirshipSectors = sectorList.Where(s => s.Type == VesselType.Airship).ToDictionary(s => s.Id);

        var partList = parts.ToList();
        SubmarineParts = partList.Where(p => p.Type == VesselType.Submarine).ToDictionary(p => p.Id);
        AirshipParts = partList.Where(p => p.Type == VesselType.Airship).ToDictionary(p => p.Id);

        var rankList = ranks.ToList();
        SubmarineRanks = rankList.Where(r => r.Type == VesselType.Submarine).OrderBy(r => r.Rank).ToList();
        AirshipRanks = rankList.Where(r => r.Type == VesselType.Airship).OrderBy(r => r.Rank).ToList();
        Maps = maps.OrderBy(m => m.Id).ToList();

        SubmarineStarts = SubmarineSectors.Values.Where(s => s.IsStart).ToDictionary(s => s.Map);
        AirshipStart = AirshipSectors.Values.First(s => s.IsStart);

        LastSubmarineRank = SubmarineRanks.Where(r => r.Capacity != 0).Max(r => r.Rank);
        LastAirshipRank = AirshipRanks.Where(r => r.Capacity != 0).Max(r => r.Rank);
    }

    public IReadOnlyDictionary<uint, SectorInfo> Sectors(VesselType type)
        => type == VesselType.Airship ? AirshipSectors : SubmarineSectors;

    public IReadOnlyDictionary<uint, PartInfo> Parts(VesselType type)
        => type == VesselType.Airship ? AirshipParts : SubmarineParts;

    public IReadOnlyList<RankInfo> Ranks(VesselType type)
        => type == VesselType.Airship ? AirshipRanks : SubmarineRanks;

    public int LastRank(VesselType type)
        => type == VesselType.Airship ? LastAirshipRank : LastSubmarineRank;

    public SectorInfo Sector(VesselType type, uint id) => Sectors(type)[id];

    public PartInfo Part(VesselType type, uint id) => Parts(type)[id];

    /// <summary>Rank row, extrapolated past the table the way SubmarineTracker does (+1 per rank on every bonus).</summary>
    public RankInfo Rank(VesselType type, int rank)
    {
        var ranks = Ranks(type);
        var last = LastRank(type);
        if (rank <= last)
            return ranks[rank];

        var l = ranks[last];
        var extra = rank - last;
        return l with
        {
            Rank = rank,
            SurveillanceBonus = l.SurveillanceBonus + extra,
            RetrievalBonus = l.RetrievalBonus + extra,
            SpeedBonus = l.SpeedBonus + extra,
            RangeBonus = l.RangeBonus + extra,
            FavorBonus = l.FavorBonus + extra,
        };
    }

    /// <summary>Start sector for a submarine map, or the single airship start.</summary>
    public SectorInfo StartFor(VesselType type, uint map)
        => type == VesselType.Airship ? AirshipStart : SubmarineStarts[map];

    /// <summary>Map a submarine sector belongs to (airships: always 1).</summary>
    public uint MapOf(VesselType type, uint sectorId)
        => type == VesselType.Airship ? 1u : SubmarineSectors[sectorId].Map;

    /// <summary>Real destinations of a map, in row order.</summary>
    public IEnumerable<SectorInfo> DestinationsOf(VesselType type, uint map)
        => Sectors(type).Values.Where(s => s.IsDestination && (type == VesselType.Airship || s.Map == map)).OrderBy(s => s.Id);
}
