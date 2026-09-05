using System.Collections.Generic;
using Argus.Core.Model;
using Lumina.Excel.Sheets;

namespace Argus.Core.Game;

/// <summary>Builds the Dalamud-free <see cref="GameData"/> from the Lumina sheets once at load.</summary>
internal static class GameDataLoader
{
    public static GameData Load()
    {
        var sectors = new List<SectorInfo>();
        foreach (var s in Sheets.SubmarineSectors)
        {
            if (s.RankReq == 0 && !s.StartingPoint)
                continue;

            sectors.Add(new SectorInfo(s.RowId, VesselType.Submarine, s.Map.RowId, s.Destination.ExtractText(), s.Location.ExtractText(),
                s.ExpReward, s.CeruleumTankReq, s.SurveyDurationmin, s.SurveyDistance, s.X, s.Y, s.Z, s.RankReq, 0, s.Stars, s.StartingPoint));
        }

        foreach (var a in Sheets.AirshipSectors)
        {
            var id = a.RowId;
            var isStart = id == AirshipStartRow;
            if (!isStart && (id > 24 || a.RankReq == 0))
                continue;

            var name = isStart ? "Sea of Clouds" : a.Name.ExtractText();
            sectors.Add(new SectorInfo(id, VesselType.Airship, 1, name, AirshipLetter(id), a.ExpReward, a.CeruleumTankReq,
                a.SurveyDurationmin, a.SurveyDistance, a.X, a.Y, 0, a.RankReq, a.SurveillanceReq, 0, isStart) { Passengers = a.Passengers });
        }

        var parts = new List<PartInfo>();
        foreach (var p in Sheets.SubmarineParts)
        {
            if (p.RowId == 0) continue;
            parts.Add(new PartInfo(p.RowId, VesselType.Submarine, p.Slot, p.Class, p.Rank, p.Surveillance, p.Retrieval, p.Speed, p.Range, p.Favor, p.Components, p.RepairMaterials));
        }

        foreach (var p in Sheets.AirshipParts)
        {
            if (p.RowId == 0) continue;
            parts.Add(new PartInfo(p.RowId, VesselType.Airship, p.Slot, p.Class, p.Rank, p.Surveillance, p.Retrieval, p.Speed, p.Range, p.Favor, p.Components, p.RepairMaterials));
        }

        var ranks = new List<RankInfo>();
        foreach (var r in Sheets.SubmarineRanks)
            ranks.Add(new RankInfo((int)r.RowId, VesselType.Submarine, r.ExpToNext, r.Capacity, r.SurveillanceBonus, r.RetrievalBonus, r.SpeedBonus, r.RangeBonus, r.FavorBonus));
        foreach (var r in Sheets.AirshipLevels)
            ranks.Add(new RankInfo((int)r.RowId, VesselType.Airship, r.ExpToNext, r.Capacity, 0, 0, 0, 0, 0));

        var maps = new List<MapInfo>();
        foreach (var m in Sheets.SubmarineMaps)
        {
            var name = m.Name.ExtractText();
            if (name.Length > 0)
                maps.Add(new MapInfo(m.RowId, name));
        }

        return new GameData(sectors, parts, ranks, maps);
    }

    /// <summary>AirshipExplorationPoint row 127 is the workshop (start); rows 0-24 are sectors, 22 is the Diadem.</summary>
    public const uint AirshipStartRow = 127;

    public static string AirshipLetter(uint row) => row switch
    {
        <= 21 => ((char)('A' + row)).ToString(),
        23 => "W",
        24 => "X",
        _ => string.Empty,
    };
}
