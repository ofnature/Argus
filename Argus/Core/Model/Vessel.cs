using System;
using System.Collections.Generic;

namespace Argus.Core.Model;

public enum VesselType
{
    Submarine,
    Airship,
}

/// <summary>
/// One submarine or airship as last read from the workshop. Plain data so the store can serialize it and the
/// calculators can run on it without Dalamud.
/// </summary>
[Serializable]
public sealed class Vessel
{
    public ulong FreeCompanyId;
    public VesselType Type;

    /// <summary>Slot in the workshop (0-3). Stable for the life of the vessel.</summary>
    public int Slot;

    public string Name = string.Empty;
    public int Rank;
    public uint CurrentExp;
    public uint NextLevelExp;

    /// <summary>Part row ids: SubmarinePart for subs, AirshipExplorationPart for airships. Slot order hull/stern/bow/bridge
    /// (airships: hull/rigging/forecastle/aftcastle in the same field order).</summary>
    public ushort Hull;
    public ushort Stern;
    public ushort Bow;
    public ushort Bridge;

    /// <summary>Unix seconds. RegisterTime is the dispatch time; ReturnTime is 0 when not deployed.</summary>
    public uint RegisterTime;
    public uint ReturnTime;

    /// <summary>Current route as exploration row ids (submarines only; airships do not expose it).</summary>
    public List<uint> Points = new();

    // Totals as the game reports them (rank bonus already applied).
    public int Surveillance;
    public int Retrieval;
    public int Speed;
    public int Range;
    public int Favor;

    public DateTime LastSeenUtc;

    public bool IsDeployed => ReturnTime != 0;

    public DateTime ReturnUtc => DateTimeOffset.FromUnixTimeSeconds(ReturnTime).UtcDateTime;

    public DateTime RegisterUtc => DateTimeOffset.FromUnixTimeSeconds(RegisterTime).UtcDateTime;

    /// <summary>Deployed and the timer has run out — waiting for the player to collect it.</summary>
    public bool IsReturned(DateTime nowUtc) => IsDeployed && nowUtc >= ReturnUtc;

    /// <summary>Deployed and still travelling.</summary>
    public bool IsOut(DateTime nowUtc) => IsDeployed && nowUtc < ReturnUtc;

    /// <summary>Sitting in the workshop, never dispatched or already collected.</summary>
    public bool IsIdle => !IsDeployed;

    public TimeSpan Remaining(DateTime nowUtc) => IsDeployed ? ReturnUtc - nowUtc : TimeSpan.Zero;

    /// <summary>Identity across reads: same FC, type and slot.</summary>
    public bool SameIdentity(Vessel other) => FreeCompanyId == other.FreeCompanyId && Type == other.Type && Slot == other.Slot;

    public bool SameState(Vessel other)
    {
        if (!SameIdentity(other)
            || Name != other.Name || Rank != other.Rank || CurrentExp != other.CurrentExp
            || Hull != other.Hull || Stern != other.Stern || Bow != other.Bow || Bridge != other.Bridge
            || RegisterTime != other.RegisterTime || ReturnTime != other.ReturnTime
            || Surveillance != other.Surveillance || Retrieval != other.Retrieval || Speed != other.Speed
            || Range != other.Range || Favor != other.Favor
            || Points.Count != other.Points.Count)
            return false;

        for (var i = 0; i < Points.Count; i++)
        {
            if (Points[i] != other.Points[i])
                return false;
        }

        return true;
    }
}

/// <summary>Everything Argus remembers about one Free Company.</summary>
[Serializable]
public sealed class FreeCompanyRecord
{
    public ulong Id;
    public string Tag = string.Empty;
    public string World = string.Empty;

    /// <summary>The character that last read this FC's workshop, for the overview label.</summary>
    public string CharacterName = string.Empty;
    public ulong ContentId;

    public List<Vessel> Vessels = new();

    /// <summary>SubmarineExploration row ids the FC has unlocked / explored (from HousingManager).</summary>
    public HashSet<uint> UnlockedSubSectors = new();
    public HashSet<uint> ExploredSubSectors = new();

    /// <summary>AirshipExplorationPoint row ids seen selectable in the planner. No direct game query exists.</summary>
    public HashSet<uint> UnlockedAirshipSectors = new();

    // Supplies last counted (inventory + FC chest snapshot), -1 = never seen.
    public int CeruleumTanks = -1;
    public int MagitekRepairMaterials = -1;
    public DateTime SuppliesSeenUtc;

    public DateTime LastSeenUtc;

    public IEnumerable<Vessel> OfType(VesselType type)
    {
        foreach (var v in Vessels)
            if (v.Type == type)
                yield return v;
    }
}
