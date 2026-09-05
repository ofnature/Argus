using System;
using System.Collections.Generic;
using Argus.Core.Model;

namespace Argus.Core.Calc;

/// <summary>Part wear per voyage (SubmarineTracker's model; submarines only — airship wear is not characterised).</summary>
public static class Repair
{
    public const int PartCondition = 30000;

    /// <summary>Condition lost by the worst-hit part over one run of <paramref name="route"/>.</summary>
    public static int VoyageDamage(Build build, IReadOnlyList<SectorInfo> route)
    {
        if (build.Type != VesselType.Submarine || route.Count == 0)
            return 0;

        var worst = 0;
        foreach (var part in build.Parts)
        {
            var damage = 0;
            foreach (var sector in route)
                damage += (335 + sector.RankReq - part.Rank) * 7;
            worst = Math.Max(worst, damage);
        }

        return worst;
    }

    /// <summary>How many more runs of the route before a part needs repairing from full condition; -1 when unknown.</summary>
    public static int VoyagesUntilRepair(Build build, IReadOnlyList<SectorInfo> route)
    {
        var damage = VoyageDamage(build, route);
        if (damage <= 0)
            return -1;
        return (PartCondition + damage - 1) / damage;
    }
}
