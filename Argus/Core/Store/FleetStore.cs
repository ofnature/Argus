using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Argus.Core.Model;
using Newtonsoft.Json;

namespace Argus.Core.Store;

/// <summary>
/// Every FC this client has ever read a workshop for, persisted as one JSON file in the plugin config directory.
/// Reads happen on the framework thread; the file write is the only slow part and is deferred to a background task.
/// </summary>
internal sealed class FleetStore
{
    private const string FileName = "fleet.json";

    private readonly string path;
    private readonly object gate = new();
    private readonly Dictionary<ulong, FreeCompanyRecord> companies = new();
    private bool dirty;

    public FleetStore(string configDirectory)
    {
        Directory.CreateDirectory(configDirectory);
        path = Path.Combine(configDirectory, FileName);
        Load();
    }

    public IReadOnlyCollection<FreeCompanyRecord> Companies
    {
        get
        {
            lock (gate)
                return companies.Values.OrderBy(c => c.Tag, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public bool TryGet(ulong fcId, out FreeCompanyRecord record)
    {
        lock (gate)
            return companies.TryGetValue(fcId, out record!);
    }

    public FreeCompanyRecord GetOrCreate(ulong fcId)
    {
        lock (gate)
        {
            if (!companies.TryGetValue(fcId, out var record))
            {
                record = new FreeCompanyRecord { Id = fcId };
                companies[fcId] = record;
                dirty = true;
            }

            return record;
        }
    }

    /// <summary>All vessels across every non-hidden FC.</summary>
    public IEnumerable<Vessel> AllVessels(ISet<ulong> hidden)
    {
        lock (gate)
            return companies.Values.Where(c => !hidden.Contains(c.Id)).SelectMany(c => c.Vessels).ToList();
    }

    /// <summary>Replace the vessel list for an FC. Returns true when anything actually changed.</summary>
    public bool UpdateVessels(ulong fcId, IReadOnlyList<Vessel> fresh, DateTime nowUtc)
    {
        lock (gate)
        {
            var record = GetOrCreate(fcId);
            var changed = record.Vessels.Count != fresh.Count;
            if (!changed)
            {
                foreach (var v in fresh)
                {
                    var existing = record.Vessels.FirstOrDefault(v.SameIdentity);
                    if (existing == null || !existing.SameState(v))
                    {
                        changed = true;
                        break;
                    }
                }
            }

            record.LastSeenUtc = nowUtc;
            if (!changed)
                return false;

            record.Vessels = fresh.OrderBy(v => v.Type).ThenBy(v => v.Slot).ToList();
            dirty = true;
            return true;
        }
    }

    public void MarkDirty()
    {
        lock (gate)
            dirty = true;
    }

    public void Remove(ulong fcId)
    {
        lock (gate)
        {
            if (companies.Remove(fcId))
                dirty = true;
        }
    }

    /// <summary>Write the file if anything changed since the last save. Safe to call every few seconds.</summary>
    public void SaveIfDirty()
    {
        string json;
        lock (gate)
        {
            if (!dirty)
                return;
            dirty = false;
            json = JsonConvert.SerializeObject(companies.Values.ToList(), Formatting.Indented);
        }

        try
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            Service.Log.Error(ex, "Argus: failed to write {Path}", path);
            lock (gate)
                dirty = true;
        }
    }

    private void Load()
    {
        if (!File.Exists(path))
            return;

        try
        {
            var list = JsonConvert.DeserializeObject<List<FreeCompanyRecord>>(File.ReadAllText(path)) ?? new();
            lock (gate)
            {
                companies.Clear();
                foreach (var record in list)
                    companies[record.Id] = record;
            }
        }
        catch (Exception ex)
        {
            Service.Log.Error(ex, "Argus: could not read {Path}; starting with an empty fleet", path);
        }
    }
}
