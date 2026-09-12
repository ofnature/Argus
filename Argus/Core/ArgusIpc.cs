using System;
using System.Collections.Generic;
using System.Linq;
using Argus.Core.Calc;
using Argus.Core.Data;
using Argus.Core.Game;
using Argus.Core.Model;
using Dalamud.Plugin.Ipc;
using Newtonsoft.Json;

namespace Argus.Core;

/// <summary>
/// Lets other plugins drive Argus: ask for a route or a build, and apply either one in game.
///
/// <para>Every endpoint takes and returns JSON strings so the contract can grow without breaking callers. Requests
/// name a vessel by <c>Name</c>, or by <c>Fc</c> + <c>Type</c> + <c>Slot</c>; when a request names nothing, the
/// vessel currently selected in the planner is used.</para>
///
/// <para>The apply endpoints do exactly what the buttons do, including their guards: a route is only selected into
/// an open voyage planner, and a build is only installed from an open vessel menu with the parts in the bags. Call
/// them from the framework thread. <c>Argus.ApplyRoute</c> can deploy, and <c>Argus.ApplyBuild</c> consumes crafted
/// parts, so both are deliberate actions rather than something to poll.</para>
/// </summary>
internal sealed class ArgusIpc : IDisposable
{
    /// <summary>Bumped when an existing shape changes meaning. Additions do not bump it.</summary>
    public const int ApiVersion = 1;

    private readonly Plugin plugin;

    private readonly ICallGateProvider<int> apiVersion;
    private readonly ICallGateProvider<bool> isBusy;
    private readonly ICallGateProvider<string> getFleet;
    private readonly ICallGateProvider<string, string> suggestRoute;
    private readonly ICallGateProvider<string, bool> applyRoute;
    private readonly ICallGateProvider<string, string> suggestBuild;
    private readonly ICallGateProvider<string, bool> applyBuild;
    private readonly ICallGateProvider<string> lastError;

    public ArgusIpc(Plugin plugin)
    {
        this.plugin = plugin;
        var pi = Service.PluginInterface;

        apiVersion = pi.GetIpcProvider<int>("Argus.ApiVersion");
        isBusy = pi.GetIpcProvider<bool>("Argus.IsBusy");
        getFleet = pi.GetIpcProvider<string>("Argus.GetFleet");
        suggestRoute = pi.GetIpcProvider<string, string>("Argus.SuggestRoute");
        applyRoute = pi.GetIpcProvider<string, bool>("Argus.ApplyRoute");
        suggestBuild = pi.GetIpcProvider<string, string>("Argus.SuggestBuild");
        applyBuild = pi.GetIpcProvider<string, bool>("Argus.ApplyBuild");
        lastError = pi.GetIpcProvider<string>("Argus.LastError");

        apiVersion.RegisterFunc(() => ApiVersion);
        isBusy.RegisterFunc(() => plugin.PlannerInterop.Applying || plugin.PartsInterop.Running);
        getFleet.RegisterFunc(GetFleet);
        suggestRoute.RegisterFunc(SuggestRoute);
        applyRoute.RegisterFunc(ApplyRoute);
        suggestBuild.RegisterFunc(SuggestBuild);
        applyBuild.RegisterFunc(ApplyBuild);
        lastError.RegisterFunc(() => plugin.PartsInterop.LastError ?? plugin.PlannerInterop.LastError ?? string.Empty);
    }

    public void Dispose()
    {
        apiVersion.UnregisterFunc();
        isBusy.UnregisterFunc();
        getFleet.UnregisterFunc();
        suggestRoute.UnregisterFunc();
        applyRoute.UnregisterFunc();
        suggestBuild.UnregisterFunc();
        applyBuild.UnregisterFunc();
        lastError.UnregisterFunc();
    }

    #region contracts

    // These fields are only ever written by the JSON deserializer.
#pragma warning disable CS0649
    private class VesselRef
    {
        public string? Name;
        public ulong Fc;
        public string? Type;
        public int Slot = -1;
    }

    private sealed class RouteRequestDto : VesselRef
    {
        public uint Map;
        public string? Goal;
        public int CapHours = -1;
        public uint[]? MustInclude;
        public uint TargetItem;
        public bool IgnoreUnlocks;
        public int Count = 5;
    }

    private sealed class ApplyRouteDto : VesselRef
    {
        public uint[]? Sectors;
        public bool Deploy;
    }

    private sealed class BuildRequestDto : VesselRef
    {
        public int Rank;
        public uint[]? Route;
        public string? Goal;
        public uint UnlockSector;
        public int Count = 5;
    }

    private sealed class ApplyBuildDto : VesselRef
    {
        /// <summary>Part sheet rows in slot order: hull, stern, bow, bridge.</summary>
        public uint[]? Parts;
    }

#pragma warning restore CS0649
    #endregion

    private static string Fail(string reason) => JsonConvert.SerializeObject(new { Ok = false, Error = reason });

    private static T? Parse<T>(string json) where T : class
    {
        try
        {
            return string.IsNullOrWhiteSpace(json) ? Activator.CreateInstance<T>() : JsonConvert.DeserializeObject<T>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Resolve a request's vessel, falling back to whatever the planner has selected.</summary>
    private Vessel? Resolve(VesselRef? reference)
    {
        var vessels = plugin.Fleet.VisibleVessels().ToList();
        if (reference != null)
        {
            if (!string.IsNullOrWhiteSpace(reference.Name))
                return vessels.FirstOrDefault(v => string.Equals(v.Name, reference.Name, StringComparison.OrdinalIgnoreCase));

            if (reference.Slot >= 0 && Enum.TryParse<VesselType>(reference.Type, true, out var type))
            {
                return vessels.FirstOrDefault(v => v.Type == type && v.Slot == reference.Slot
                                                   && (reference.Fc == 0 || v.FreeCompanyId == reference.Fc));
            }
        }

        return plugin.Planner.Vessel;
    }

    private string GetFleet()
    {
        var now = DateTime.UtcNow;
        var fleet = plugin.Fleet.VisibleCompanies().Select(fc => new
        {
            fc.Id,
            fc.Tag,
            fc.CharacterName,
            fc.CeruleumTanks,
            fc.MagitekRepairMaterials,
            Vessels = fc.Vessels.Select(v => new
            {
                v.Name,
                Type = v.Type.ToString(),
                v.Slot,
                v.Rank,
                v.CurrentExp,
                v.NextLevelExp,
                Parts = new[] { v.Hull, v.Stern, v.Bow, v.Bridge },
                v.Surveillance,
                v.Retrieval,
                v.Speed,
                v.Range,
                v.Favor,
                v.ReturnTime,
                Returned = v.IsReturned(now),
                Out = v.IsOut(now),
                Route = v.Points,
            }),
        });

        return JsonConvert.SerializeObject(new { Ok = true, Companies = fleet });
    }

    private string SuggestRoute(string json)
    {
        var request = Parse<RouteRequestDto>(json);
        if (request == null)
            return Fail("bad request json");

        var vessel = Resolve(request);
        if (vessel == null)
            return Fail("vessel not found");

        Build build;
        try
        {
            build = Build.From(plugin.Data, vessel);
        }
        catch (KeyNotFoundException)
        {
            return Fail("unknown parts on that vessel");
        }

        var prefs = plugin.Config.PlannerFor(vessel.Type);
        var map = request.Map != 0 ? request.Map : vessel.Type == VesselType.Airship ? 1 : plugin.Planner.Map;
        var fc = plugin.Fleet.Store.TryGet(vessel.FreeCompanyId, out var record) ? record : null;
        var unlocked = fc == null ? new HashSet<uint>() : plugin.Planner.Unlocked(fc, vessel.Type);
        var goal = Enum.TryParse<RouteGoal>(request.Goal, true, out var parsed) ? parsed : prefs.Goal;
        var cap = request.CapHours >= 0 ? request.CapHours : prefs.DurationCapHours;

        var routeRequest = new RouteRequest(
            vessel.Type, map, build, unlocked,
            (request.MustInclude ?? []).ToHashSet(),
            goal,
            cap > 0 ? TimeSpan.FromHours(cap) : null,
            prefs.AverageBonus || vessel.Type == VesselType.Airship,
            fc?.CeruleumTanks ?? -1,
            request.IgnoreUnlocks,
            VoyageMath.MaxSectorsPerVoyage,
            request.TargetItem);

        var issues = RouteSearch.Issues(plugin.Data, routeRequest);
        var results = RouteSearch.FindTop(plugin.Data, routeRequest, Math.Clamp(request.Count, 1, 20));

        return JsonConvert.SerializeObject(new
        {
            Ok = true,
            Vessel = vessel.Name,
            Type = vessel.Type.ToString(),
            Map = map,
            Issues = issues,
            Routes = results.Select(r => new
            {
                r.Sectors,
                Letters = r.Sectors.Select(id => plugin.Data.Sector(vessel.Type, id).Letter).ToArray(),
                Names = r.Sectors.Select(id => plugin.Data.Sector(vessel.Type, id).Name).ToArray(),
                r.Distance,
                DurationSeconds = (int)r.Duration.TotalSeconds,
                r.Fuel,
                Exp = r.Exp.Guaranteed,
                ExpMax = r.Exp.Maximum,
                r.ItemUnits,
            }),
        });
    }

    private bool ApplyRoute(string json)
    {
        var request = Parse<ApplyRouteDto>(json);
        if (request?.Sectors == null || request.Sectors.Length == 0)
            return false;

        var vessel = Resolve(request);
        if (vessel == null)
            return false;

        return plugin.PlannerInterop.ApplyRoute(plugin.Data, vessel.Type, request.Sectors, request.Deploy);
    }

    private string SuggestBuild(string json)
    {
        var request = Parse<BuildRequestDto>(json);
        if (request == null)
            return Fail("bad request json");

        var vessel = Resolve(request);
        if (vessel == null)
            return Fail("vessel not found");

        var route = request.Route is { Length: > 0 }
            ? request.Route
            : plugin.Planner.ChosenRoute?.Sectors ?? vessel.Points.ToArray();
        if (route.Length == 0)
            return Fail("no route to build for");

        var prefs = plugin.Config.PlannerFor(vessel.Type);
        var rank = request.Rank > 0 ? request.Rank : vessel.Rank;
        var map = vessel.Type == VesselType.Airship ? 1 : plugin.Data.MapOf(vessel.Type, route[0]);
        var goal = Enum.TryParse<RouteGoal>(request.Goal, true, out var parsed) ? parsed : prefs.Goal;
        var useAverage = prefs.AverageBonus || vessel.Type == VesselType.Airship;
        var count = Math.Clamp(request.Count, 1, 20);

        var builds = request.UnlockSector != 0
            ? PartOptimizer.BestForUnlock(plugin.Data, vessel.Type, rank, map, route, request.UnlockSector, count)
                .Select(c => Describe(vessel, c.Build, c.Distance, c.Duration, c.Exp))
            : PartOptimizer.Best(plugin.Data, vessel.Type, rank, map, route, goal, useAverage, count)
                .Select(c => Describe(vessel, c.Build, c.Distance, c.Duration, c.Exp));

        return JsonConvert.SerializeObject(new { Ok = true, Vessel = vessel.Name, Rank = rank, Builds = builds });
    }

    private object Describe(Vessel vessel, Build build, int distance, TimeSpan duration, RouteExp exp)
    {
        var parts = build.Parts.ToArray();
        return new
        {
            build.Identifier,
            Parts = parts.Select(p => p.Id).ToArray(),
            Items = parts.Select(p => PartItems.ItemFor(vessel.Type, p.Id)).ToArray(),
            ItemNames = parts.Select(p => Sheets.ItemName(PartItems.ItemFor(vessel.Type, p.Id))).ToArray(),
            build.Surveillance,
            build.Retrieval,
            build.Speed,
            build.Range,
            build.Favor,
            build.Cost,
            build.Capacity,
            Distance = distance,
            DurationSeconds = (int)duration.TotalSeconds,
            Exp = exp.Guaranteed,
        };
    }

    private bool ApplyBuild(string json)
    {
        var request = Parse<ApplyBuildDto>(json);
        if (request?.Parts == null || request.Parts.Length != 4)
            return false;

        var vessel = Resolve(request);
        if (vessel == null)
            return false;

        Build target;
        try
        {
            target = Build.From(plugin.Data, vessel.Type, vessel.Rank, request.Parts[0], request.Parts[1], request.Parts[2], request.Parts[3]);
        }
        catch (KeyNotFoundException)
        {
            return false;
        }

        return plugin.PartsInterop.ApplyBuild(vessel, target);
    }
}
