using System.Collections.Generic;
using System.Linq;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace Argus.Core;

/// <summary>Excel sheets Argus reads, resolved once. Static because the sheets never change during a session.</summary>
internal static class Sheets
{
    public static ExcelSheet<Item> Items { get; private set; } = null!;
    public static ExcelSheet<TerritoryType> Territories { get; private set; } = null!;

    public static ExcelSheet<SubmarineExploration> SubmarineSectors { get; private set; } = null!;
    public static ExcelSheet<SubmarinePart> SubmarineParts { get; private set; } = null!;
    public static ExcelSheet<SubmarineRank> SubmarineRanks { get; private set; } = null!;
    public static ExcelSheet<SubmarineMap> SubmarineMaps { get; private set; } = null!;

    public static ExcelSheet<AirshipExplorationPoint> AirshipSectors { get; private set; } = null!;
    public static ExcelSheet<AirshipExplorationPart> AirshipParts { get; private set; } = null!;
    public static ExcelSheet<AirshipExplorationLevel> AirshipLevels { get; private set; } = null!;

    /// <summary>Row ids of every submarine sector that is a real destination (has a rank requirement).</summary>
    public static uint[] SubmarineSectorIds { get; private set; } = [];

    /// <summary>Highest submarine rank the game defines (last row with a non-zero capacity).</summary>
    public static uint LastSubmarineRank { get; private set; }

    /// <summary>Highest airship rank (last row with a non-zero capacity).</summary>
    public static uint LastAirshipRank { get; private set; }

    public static void Initialize()
    {
        var data = Service.DataManager;
        Items = data.GetExcelSheet<Item>();
        Territories = data.GetExcelSheet<TerritoryType>();
        SubmarineSectors = data.GetExcelSheet<SubmarineExploration>();
        SubmarineParts = data.GetExcelSheet<SubmarinePart>();
        SubmarineRanks = data.GetExcelSheet<SubmarineRank>();
        SubmarineMaps = data.GetExcelSheet<SubmarineMap>();
        AirshipSectors = data.GetExcelSheet<AirshipExplorationPoint>();
        AirshipParts = data.GetExcelSheet<AirshipExplorationPart>();
        AirshipLevels = data.GetExcelSheet<AirshipExplorationLevel>();

        SubmarineSectorIds = SubmarineSectors.Where(s => s.RankReq > 0 || s.StartingPoint).Select(s => s.RowId).ToArray();
        LastSubmarineRank = SubmarineRanks.Last(r => r.Capacity != 0).RowId;
        LastAirshipRank = AirshipLevels.Last(r => r.Capacity != 0).RowId;
    }

    public static string ItemName(uint itemId)
        => Items.GetRowOrDefault(itemId)?.Name.ExtractText() ?? $"Item {itemId}";

    public static IEnumerable<SubmarineExploration> SubmarineSectorsOfMap(uint mapRowId)
        => SubmarineSectors.Where(s => s.Map.RowId == mapRowId && !s.StartingPoint);
}
