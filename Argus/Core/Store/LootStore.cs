using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Argus.Core.Model;
using Newtonsoft.Json;

namespace Argus.Core.Store;

/// <summary>Append-only loot history as one JSON file next to the fleet store.</summary>
internal sealed class LootStore
{
    private const string FileName = "loot.json";

    private readonly string path;
    private readonly object gate = new();
    private readonly List<LootEntry> entries = new();
    private readonly HashSet<string> keys = new();
    private bool dirty;

    public LootStore(string configDirectory)
    {
        Directory.CreateDirectory(configDirectory);
        path = Path.Combine(configDirectory, FileName);
        Load();
    }

    public int Count
    {
        get
        {
            lock (gate)
                return entries.Count;
        }
    }

    /// <summary>Newest first.</summary>
    public List<LootEntry> Snapshot()
    {
        lock (gate)
            return entries.OrderByDescending(e => e.CollectedUtc).ToList();
    }

    public bool Contains(string key)
    {
        lock (gate)
            return keys.Contains(key);
    }

    /// <summary>Adds entries not seen before; returns how many were new.</summary>
    public int Add(IEnumerable<LootEntry> batch)
    {
        var added = 0;
        lock (gate)
        {
            foreach (var e in batch)
            {
                if (!keys.Add(e.Key))
                    continue;
                entries.Add(e);
                added++;
            }

            if (added > 0)
                dirty = true;
        }

        return added;
    }

    public void SaveIfDirty()
    {
        string json;
        lock (gate)
        {
            if (!dirty)
                return;
            dirty = false;
            json = JsonConvert.SerializeObject(entries);
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
            var list = JsonConvert.DeserializeObject<List<LootEntry>>(File.ReadAllText(path)) ?? new();
            lock (gate)
            {
                entries.Clear();
                keys.Clear();
                foreach (var e in list)
                {
                    if (keys.Add(e.Key))
                        entries.Add(e);
                }
            }
        }
        catch (Exception ex)
        {
            Service.Log.Error(ex, "Argus: could not read {Path}; starting with an empty loot history", path);
        }
    }

    /// <summary>CSV with one row per sector haul, oldest first.</summary>
    public string ToCsv(GameData data, Func<uint, string> itemName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("collected_utc,fc,type,vessel,rank,build,surveillance,retrieval,speed,range,favor,sector,sector_name,exp,rating,item,count,hq,item2,count2,hq2,double_dip,first_exploration,unlocked_sector");
        foreach (var e in Snapshot().OrderBy(e => e.CollectedUtc))
        {
            var name = data.Sectors(e.Type).TryGetValue(e.Sector, out var s) ? s.Name : e.Sector.ToString();
            string build;
            try { build = Build.From(data, e.Type, e.Rank, e.Hull, e.Stern, e.Bow, e.Bridge).Identifier; }
            catch (KeyNotFoundException) { build = $"{e.Hull}/{e.Stern}/{e.Bow}/{e.Bridge}"; }

            sb.Append(e.CollectedUtc.ToString("O")).Append(',')
              .Append(e.FreeCompanyId).Append(',')
              .Append(e.Type).Append(',')
              .Append(Quote(e.VesselName)).Append(',')
              .Append(e.Rank).Append(',')
              .Append(build).Append(',')
              .Append(e.Surveillance).Append(',').Append(e.Retrieval).Append(',').Append(e.Speed).Append(',').Append(e.Range).Append(',').Append(e.Favor).Append(',')
              .Append(e.Sector).Append(',').Append(Quote(name)).Append(',')
              .Append(e.ExpGained).Append(',').Append(e.Rating).Append(',')
              .Append(Quote(e.PrimaryItem == 0 ? "" : itemName(e.PrimaryItem))).Append(',').Append(e.PrimaryCount).Append(',').Append(e.PrimaryHq).Append(',')
              .Append(Quote(e.AdditionalItem == 0 ? "" : itemName(e.AdditionalItem))).Append(',').Append(e.AdditionalCount).Append(',').Append(e.AdditionalHq).Append(',')
              .Append(e.DoubleDip).Append(',').Append(e.FirstExploration).Append(',').Append(e.UnlockedSector)
              .AppendLine();
        }

        return sb.ToString();
    }

    private static string Quote(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
}
