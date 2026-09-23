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

    /// <summary>
    /// Merge a workshop read into an FC's vessel list, keyed on type and slot. Vessels the read did not contain are
    /// kept: the client does not always populate both arrays, and a partial read must never delete a known vessel
    /// (a registered submarine or airship cannot be given up in game, so there is nothing legitimate to remove).
    /// Returns true when anything actually changed.
    /// </summary>
    public bool UpdateVessels(ulong fcId, IReadOnlyList<Vessel> fresh, DateTime nowUtc)
    {
        lock (gate)
        {
            var record = GetOrCreate(fcId);
            record.LastSeenUtc = nowUtc;

            var changed = false;
            foreach (var v in fresh)
            {
                var index = record.Vessels.FindIndex(v.SameIdentity);
                if (index < 0)
                {
                    record.Vessels.Add(v);
                    changed = true;
                    continue;
                }

                // A read can see the vessel but not its parts' condition; keep what an earlier read saw.
                v.KeepConditionFrom(record.Vessels[index]);
                if (!record.Vessels[index].SameState(v))
                {
                    record.Vessels[index] = v;
                    changed = true;
                }
                else
                {
                    // Unchanged, but seen this tick: that is what marks a row as still confirmed.
                    record.Vessels[index].LastSeenUtc = nowUtc;
                }
            }

            if (!changed)
                return false;

            record.Vessels.Sort((a, b) => a.Type != b.Type ? a.Type.CompareTo(b.Type) : a.Slot.CompareTo(b.Slot));
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
