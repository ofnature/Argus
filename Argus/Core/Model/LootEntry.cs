using System;

namespace Argus.Core.Model;

/// <summary>One sector's haul from one voyage, captured from the voyage result screen.</summary>
[Serializable]
public sealed class LootEntry
{
    public ulong FreeCompanyId;
    public VesselType Type;
    public int Slot;
    public string VesselName = string.Empty;

    /// <summary>
    /// The voyage: its return time (see <c>LootHook.VoyageId</c>), so (FC, type, slot, voyage, sector) is one sector of
    /// one run. Entries filed before 2026-11-05 carry the vessel's RegisterTime, which never changes, so only a
    /// vessel's first visit to each sector was kept.
    /// </summary>
    public uint Voyage;

    public DateTime CollectedUtc;

    // Build as it was when the voyage was dispatched.
    public int Rank;
    public ushort Hull;
    public ushort Stern;
    public ushort Bow;
    public ushort Bridge;
    public int Surveillance;
    public int Retrieval;
    public int Speed;
    public int Range;
    public int Favor;

    public uint Sector;
    public uint ExpGained;

    public uint PrimaryItem;
    public int PrimaryCount;
    public bool PrimaryHq;
    public uint AdditionalItem;
    public int AdditionalCount;
    public bool AdditionalHq;

    /// <summary>Submarines: the sector rating the game showed (SS, S, A, B, C). Airships: not exposed.</summary>
    public string Rating = string.Empty;

    public bool DoubleDip;
    public bool FirstExploration;
    public uint UnlockedSector;
    public bool SlotUnlocked;

    public string Key => $"{FreeCompanyId}:{Type}:{Slot}:{Voyage}:{Sector}";
}
