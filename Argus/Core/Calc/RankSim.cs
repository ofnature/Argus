using System;
using Argus.Core.Model;

namespace Argus.Core.Calc;

/// <summary>Rank arithmetic shared by the vessels page, the planner and the leveling projection.</summary>
public static class RankSim
{
    /// <summary>Rank and leftover EXP after adding <paramref name="gained"/> to a vessel at <paramref name="rank"/> with <paramref name="currentExp"/>.</summary>
    public static (int Rank, uint RemainingExp) Apply(GameData data, VesselType type, int rank, uint currentExp, uint gained)
    {
        var last = data.LastRank(type);
        var total = (ulong)currentExp + gained;
        while (rank < last)
        {
            var toNext = data.Rank(type, rank).ExpToNext;
            if (toNext == 0 || total < toNext)
                break;
            total -= toNext;
            rank++;
        }

        if (rank >= last)
            return (last, 0);
        return (rank, (uint)total);
    }

    /// <summary>The rank a vessel was at before it earned <paramref name="expReceived"/> and ended at <paramref name="rank"/>/<paramref name="currentExp"/>.</summary>
    public static int OriginalRank(GameData data, VesselType type, int rank, uint currentExp, uint expReceived)
    {
        if (rank >= data.LastRank(type))
            return rank;

        if (currentExp >= expReceived)
            return rank;

        expReceived -= currentExp;
        rank--;
        while (rank >= 2)
        {
            var toNext = data.Rank(type, rank).ExpToNext;
            if (toNext >= expReceived)
                break;
            expReceived -= toNext;
            rank--;
        }

        return Math.Max(1, rank);
    }

    /// <summary>EXP still needed to reach <paramref name="targetRank"/> from the given state.</summary>
    public static ulong ExpToRank(GameData data, VesselType type, int rank, uint currentExp, int targetRank)
    {
        var last = data.LastRank(type);
        targetRank = Math.Min(targetRank, last);
        if (targetRank <= rank)
            return 0;

        ulong needed = 0;
        for (var r = rank; r < targetRank; r++)
            needed += data.Rank(type, r).ExpToNext;
        return needed > currentExp ? needed - currentExp : 0;
    }

    /// <summary>
    /// Number of voyages of <paramref name="expPerVoyage"/> to reach <paramref name="targetRank"/>. Re-evaluates nothing
    /// per rank (the caller re-plans when the build's rank bonus would change the route).
    /// </summary>
    public static int VoyagesToRank(GameData data, VesselType type, int rank, uint currentExp, int targetRank, uint expPerVoyage)
    {
        if (expPerVoyage == 0)
            return -1;
        var needed = ExpToRank(data, type, rank, currentExp, targetRank);
        return (int)((needed + expPerVoyage - 1) / expPerVoyage);
    }
}
