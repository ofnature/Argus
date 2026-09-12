using System.Collections.Generic;

using Argus.Core.Model;

namespace Argus.Core.Data;

/// <summary>
/// Part sheet row to the inventory item that installs it. The parts windows list items by name, so swapping a
/// part needs the item behind the <c>SubmarinePart</c> / <c>AirshipExplorationPart</c> row.
///
/// <para>Submarine ids come from SubmarineTracker (MIT, Infi). Airship ids were derived from the sheet layout
/// (six classes by rank per slot, Viltgance appended) and every one was verified against the item name.</para>
/// </summary>
public static class PartItems
{
    private static readonly Dictionary<uint, uint> SubmarineParts = new()
    {
        { 1, 21792 },
        { 2, 21793 },
        { 3, 21794 },
        { 4, 21795 },
        { 5, 21796 },
        { 6, 21797 },
        { 7, 21798 },
        { 8, 21799 },
        { 9, 22526 },
        { 10, 22527 },
        { 11, 22528 },
        { 12, 22529 },
        { 13, 23903 },
        { 14, 23904 },
        { 15, 23905 },
        { 16, 23906 },
        { 17, 24344 },
        { 18, 24345 },
        { 19, 24346 },
        { 20, 24347 },
        { 21, 24348 },
        { 22, 24349 },
        { 23, 24350 },
        { 24, 24351 },
        { 25, 24352 },
        { 26, 24353 },
        { 27, 24354 },
        { 28, 24355 },
        { 29, 24356 },
        { 30, 24357 },
        { 31, 24358 },
        { 32, 24359 },
        { 33, 24360 },
        { 34, 24361 },
        { 35, 24362 },
        { 36, 24363 },
        { 37, 24364 },
        { 38, 24365 },
        { 39, 24366 },
        { 40, 24367 },
    };

    private static readonly Dictionary<uint, uint> AirshipParts = new()
    {
        { 1, 10156 }, // Bronco-type Hull
        { 2, 10157 }, // Invincible-type Hull
        { 3, 10158 }, // Enterprise-type Hull
        { 4, 10159 }, // Invincible II-type Hull
        { 5, 10160 }, // Odyssey-type Hull
        { 6, 10161 }, // Tatanora-type Hull
        { 7, 10162 }, // Bronco-type Sail
        { 8, 10163 }, // Invincible-type Propellers
        { 9, 10164 }, // Enterprise-type Bladder
        { 10, 10165 }, // Invincible II-type Propellers
        { 11, 10166 }, // Odyssey-type Bladders
        { 12, 10167 }, // Tatanora-type Propellers
        { 13, 10168 }, // Bronco-type Forecastle
        { 14, 10169 }, // Invincible-type Forecastle
        { 15, 10170 }, // Enterprise-type Forecastle
        { 16, 10171 }, // Invincible II-type Forecastle
        { 17, 10172 }, // Odyssey-type Forecastle
        { 18, 10173 }, // Tatanora-type Forecastle
        { 19, 10174 }, // Bronco-type Aftcastle
        { 20, 10175 }, // Invincible-type Aftcastle
        { 21, 10176 }, // Enterprise-type Aftcastle
        { 22, 10177 }, // Invincible II-type Aftcastle
        { 23, 10178 }, // Odyssey-type Aftcastle
        { 24, 10179 }, // Tatanora-type Aftcastle
        { 25, 14003 }, // Viltgance-type Hull
        { 26, 14004 }, // Viltgance-type Aetherwings
        { 27, 14005 }, // Viltgance-type Forecastle
        { 28, 14006 }, // Viltgance-type Aftcastle
    };

    /// <summary>The item that installs a part, or 0 when the row is unknown.</summary>
    public static uint ItemFor(VesselType type, uint partRow)
    {
        var table = type == VesselType.Airship ? AirshipParts : SubmarineParts;
        return table.TryGetValue(partRow, out var item) ? item : 0;
    }

    public static int Count(VesselType type) => (type == VesselType.Airship ? AirshipParts : SubmarineParts).Count;
}
