using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Argus.Core.Calc;
using Argus.Core.Model;
using Argus.Windows;

namespace Argus.Core;

/// <summary>
/// The planner's state: which vessel and map are being planned for, the must-include list (manual plus the
/// progression auto-include), and the ranked results of the last search. Searches run on a background task; the
/// UI only ever reads the published snapshot.
/// </summary>
internal sealed class PlannerService : IDisposable
{
    private readonly Plugin plugin;
    private readonly HashSet<uint> manualMust = new();
    private CancellationTokenSource? running;
    private string lastSignature = string.Empty;

    public PlannerService(Plugin plugin)
    {
        this.plugin = plugin;
    }

    /// <summary>Identity of the vessel being planned (FC, type, slot); null until one is picked.</summary>
    public (ulong FcId, VesselType Type, int Slot)? Selected { get; private set; }

    public uint Map { get; private set; } = 1;

    public IReadOnlySet<uint> ManualMustInclude => manualMust;

    /// <summary>Sector the progression rule wants to include this voyage, if any.</summary>
    public Progression.NextStep? AutoStep { get; private set; }

    public IReadOnlyList<RouteResult> Results { get; private set; } = Array.Empty<RouteResult>();

    public IReadOnlyList<string> Issues { get; private set; } = Array.Empty<string>();

    public bool Computing { get; private set; }

    public DateTime ComputeStartedUtc { get; private set; }

    /// <summary>Index into <see cref="Results"/> the user chose (0 = best).</summary>
    public int Chosen { get; set; }

    public RouteResult? ChosenRoute => Results.Count == 0 ? null : Results[Math.Clamp(Chosen, 0, Results.Count - 1)];

    public Vessel? Vessel
    {
        get
        {
            if (Selected is not { } s || !plugin.Fleet.Store.TryGet(s.FcId, out var fc))
                return null;
            return fc.Vessels.FirstOrDefault(v => v.Type == s.Type && v.Slot == s.Slot);
        }
    }

    public FreeCompanyRecord? Company
        => Selected is { } s && plugin.Fleet.Store.TryGet(s.FcId, out var fc) ? fc : null;

    public void Select(Vessel v)
    {
        var key = (v.FreeCompanyId, v.Type, v.Slot);
        if (Selected == key)
            return;

        Selected = key;
        manualMust.Clear();
        Results = Array.Empty<RouteResult>();
        Chosen = 0;
        Map = DefaultMap(v);
    }

    public void SetMap(uint map)
    {
        if (map == Map)
            return;
        Map = map;
        manualMust.Clear();
        Results = Array.Empty<RouteResult>();
        Chosen = 0;
    }

    public bool AddMust(uint sector)
    {
        if (manualMust.Count + (AutoStep != null && !manualMust.Contains(AutoStep.VisitSector) ? 1 : 0) >= VoyageMath.MaxSectorsPerVoyage)
            return false;
        return manualMust.Add(sector);
    }

    public void RemoveMust(uint sector) => manualMust.Remove(sector);

    public void ClearMust() => manualMust.Clear();

    /// <summary>Submarines: map of the current route, else the highest map with an unlocked sector.</summary>
    private uint DefaultMap(Vessel v)
    {
        if (v.Type == VesselType.Airship)
            return 1;

        var data = plugin.Data;
        if (v.Points.Count > 0 && data.SubmarineSectors.TryGetValue(v.Points[0], out var s))
            return s.Map;

        if (plugin.Fleet.Store.TryGet(v.FreeCompanyId, out var fc))
        {
            var maps = fc.UnlockedSubSectors
                .Where(id => data.SubmarineSectors.ContainsKey(id))
                .Select(id => data.SubmarineSectors[id].Map)
                .DefaultIfEmpty(1u);
            return maps.Max();
        }

        return 1;
    }

    /// <summary>Unlocked sectors for the selected vessel's type and FC (airships assume A and B when nothing is known).</summary>
    public HashSet<uint> Unlocked(FreeCompanyRecord fc, VesselType type)
    {
        if (type == VesselType.Submarine)
            return new HashSet<uint>(fc.UnlockedSubSectors);

        var set = new HashSet<uint>(fc.UnlockedAirshipSectors) { 0, 1 };
        return set;
    }

    public HashSet<uint> Explored(FreeCompanyRecord fc, VesselType type)
        => type == VesselType.Submarine ? new HashSet<uint>(fc.ExploredSubSectors) : new HashSet<uint>(fc.UnlockedAirshipSectors);

    /// <summary>True while unlock focus is on and there is a sector left to discover from here.</summary>
    public bool UnlockFocusActive
        => Vessel is { } v && plugin.Config.PlannerFor(v).UnlockFocus && AutoStep != null;

    /// <summary>
    /// Null unless the selected vessel has nowhere to go: four vessels can be out at once per Free Company, so a
    /// route still plans and applies, but the game will not send it until one of the others is back.
    /// </summary>
    public string? VoyageSlotWarning(DateTime nowUtc)
    {
        var vessel = Vessel;
        if (vessel == null || vessel.IsOut(nowUtc))
            return null;

        var deployed = plugin.Fleet.DeployedIn(vessel.FreeCompanyId, nowUtc);
        if (deployed.Count < VoyageMath.MaxDeployedVessels)
            return null;

        var next = deployed[0];
        return $"All {VoyageMath.MaxDeployedVessels} voyage slots are in use, so {vessel.Name} cannot be sent yet. "
               + $"{next.Name} is back in {Formatting.Duration(next.Remaining(nowUtc))}.";
    }

    /// <summary>
    /// Null unless the selected vessel has a broken part. The game refuses the voyage ("One or more components require
    /// repairs"), so the route still plans, but the part has to be repaired before it can be sent.
    /// </summary>
    public string? RepairWarning()
    {
        var vessel = Vessel;
        if (vessel == null)
            return null;

        var broken = vessel.BrokenSlots();
        if (broken.Count == 0)
            return null;

        var parts = string.Join(" and ", broken.Select(s => Build.SlotName(vessel.Type, s).ToLowerInvariant()));
        return $"{vessel.Name}'s {parts} {(broken.Count == 1 ? "is" : "are")} broken, so the game will not send it. "
               + "Repair from the Vessels page.";
    }

    /// <summary>All must-includes that will be sent to the search: manual ones plus the auto step.</summary>
    public HashSet<uint> EffectiveMustInclude()
    {
        var set = new HashSet<uint>(manualMust);
        if (AutoStep != null && set.Count < VoyageMath.MaxSectorsPerVoyage)
            set.Add(AutoStep.VisitSector);
        return set;
    }

    public RouteRequest? BuildRequest()
    {
        var v = Vessel;
        var fc = Company;
        if (v == null || fc == null)
            return null;

        Build build;
        try
        {
            build = Build.From(plugin.Data, v);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }

        var prefs = plugin.Config.PlannerFor(v);
        var unlocked = Unlocked(fc, v.Type);
        var explored = Explored(fc, v.Type);

        AutoStep = prefs.ProgressionAutoInclude
            ? Progression.Next(plugin.Data, v.Type, unlocked, explored, build.Rank, build.Surveillance, Map)
            : null;

        var fuel = fc.CeruleumTanks;
        var goal = prefs.UnlockFocus && AutoStep != null ? RouteGoal.ShortestVoyage : prefs.Goal;
        return new RouteRequest(
            v.Type, Map, build, unlocked, EffectiveMustInclude(),
            goal,
            prefs.DurationCapHours > 0 ? TimeSpan.FromHours(prefs.DurationCapHours) : null,
            prefs.AverageBonus || v.Type == VesselType.Airship,
            fuel >= 0 ? fuel : -1,
            prefs.IgnoreUnlocks,
            VoyageMath.MaxSectorsPerVoyage,
            prefs.UnlockFocus ? 0u : prefs.FarmItem);
    }

    /// <summary>Re-run the search when anything relevant changed. Cheap to call every frame.</summary>
    public void Refresh(bool force = false)
    {
        var req = BuildRequest();
        if (req == null)
        {
            Results = Array.Empty<RouteResult>();
            Issues = Array.Empty<string>();
            return;
        }

        var signature = Signature(req);
        if (!force && signature == lastSignature)
            return;
        lastSignature = signature;

        Issues = RouteSearch.Issues(plugin.Data, req);
        running?.Cancel();
        var cts = new CancellationTokenSource();
        running = cts;
        Computing = true;
        ComputeStartedUtc = DateTime.UtcNow;
        // Leg tables call the client's voyage functions, so they are built here on the framework thread; only the
        // combinatorial search runs on the pool.
        var tables = RouteSearch.BuildTables(plugin.Data, req);

        Task.Run(() =>
        {
            try
            {
                var top = RouteSearch.FindTop(req, tables, 10, cts.Token);
                if (cts.IsCancellationRequested)
                    return;
                Results = top;
                Chosen = 0;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Service.Log.Error(ex, "Argus: route search failed");
                Results = Array.Empty<RouteResult>();
            }
            finally
            {
                if (running == cts)
                    Computing = false;
            }
        }, cts.Token);
    }

    private static string Signature(RouteRequest r)
        => string.Join("|",
            r.Type, r.Map, r.Build.Rank, r.Build.Hull.Id, r.Build.Stern.Id, r.Build.Bow.Id, r.Build.Bridge.Id,
            string.Join(",", r.Unlocked.OrderBy(x => x)), string.Join(",", r.MustInclude.OrderBy(x => x)),
            r.Goal, r.DurationCap?.TotalHours ?? 0, r.UseAverageBonus, r.FuelAvailable, r.IgnoreUnlocks, r.TargetItem);

    public void Dispose()
    {
        running?.Cancel();
        running?.Dispose();
        running = null;
    }
}
