using System.Collections.Generic;
using System.Linq;
using Argus.Core.Model;

namespace Argus.Core.Calc;

/// <summary>Ceruleum tanks and repair materials against what the fleet is about to consume.</summary>
public static class Supplies
{
    public const uint CeruleumTankItem = 10155;
    public const uint MagitekRepairMaterialsItem = 10373;

    public sealed record Report(
        int Tanks,
        int Kits,
        int TanksForNextDispatch,
        int KitsForFullRepair,
        int DispatchesCovered,
        int RepairsCovered)
    {
        public bool Known => Tanks >= 0 && Kits >= 0;
        public bool TanksShort => Known && TanksForNextDispatch > 0 && Tanks < TanksForNextDispatch;
        public bool KitsShort => Known && KitsForFullRepair > 0 && Kits < KitsForFullRepair;
    }

    /// <summary>
    /// Fuel for one more dispatch of every vessel on its current route (submarines) or the planner's chosen route
    /// (any vessel the caller supplies one for), and kits for one full repair of every vessel.
    /// </summary>
    public static Report Evaluate(GameData data, FreeCompanyRecord fc, IReadOnlyDictionary<(VesselType, int), uint[]>? chosenRoutes = null)
    {
        var fuelPerVessel = new List<int>();
        var kits = 0;
        foreach (var v in fc.Vessels)
        {
            var route = chosenRoutes != null && chosenRoutes.TryGetValue((v.Type, v.Slot), out var chosen)
                ? chosen
                : v.Type == VesselType.Submarine ? v.Points.ToArray() : [];
            var fuel = 0;
            foreach (var id in route)
            {
                if (data.Sectors(v.Type).TryGetValue(id, out var s))
                    fuel += s.Fuel;
            }

            if (fuel > 0)
                fuelPerVessel.Add(fuel);

            try
            {
                kits += Build.From(data, v).RepairMaterials;
            }
            catch (KeyNotFoundException)
            {
            }
        }

        var tanksNeeded = fuelPerVessel.Sum();
        var dispatches = tanksNeeded == 0 || fc.CeruleumTanks < 0 ? -1 : fc.CeruleumTanks / tanksNeeded;
        var repairs = kits == 0 || fc.MagitekRepairMaterials < 0 ? -1 : fc.MagitekRepairMaterials / kits;
        return new Report(fc.CeruleumTanks, fc.MagitekRepairMaterials, tanksNeeded, kits, dispatches, repairs);
    }
}
