using System;
using Argus.Core.Calc;
using Argus.Core.Model;
using Newtonsoft.Json;

namespace Argus.Core.Store;

/// <summary>
/// One vessel's planner settings as the settings master's PC shares them over the Daedalus LAN relay (channel
/// <c>argus.prefs</c>), so every PC plans each vessel the way the master set it up: gil farmer, progression,
/// levelling. Only a PC told to follow that master takes them (<see cref="Configuration.SettingsMaster"/>).
///
/// <para>Short keys for the same reason as <see cref="FleetSyncMessage"/> (Daedalus escapes every quote twice), and
/// extend-only: receivers ignore keys they do not know, so keys may be added but never renamed or removed.</para>
/// </summary>
public sealed class PrefsSyncMessage
{
    public const int CurrentVersion = 1;

    [JsonProperty("v")] public int Version = CurrentVersion;

    /// <summary>The master character's name: a receiver takes the settings only when it follows this name.</summary>
    [JsonProperty("by")] public string Master = string.Empty;

    [JsonProperty("fc")] public ulong Fc;
    [JsonProperty("t")] public VesselType Type;
    [JsonProperty("s")] public int Slot;

    [JsonProperty("g")] public RouteGoal Goal;
    [JsonProperty("cap")] public int DurationCapHours;
    [JsonProperty("pa")] public bool ProgressionAutoInclude;
    [JsonProperty("iu")] public bool IgnoreUnlocks;
    [JsonProperty("ab")] public bool AverageBonus;
    [JsonProperty("uf")] public bool UnlockFocus;
    [JsonProperty("fi")] public uint FarmItem;
    [JsonProperty("fg")] public bool FarmGil;
    [JsonProperty("gt")] public int GilTier;
    [JsonProperty("m")] public uint Map;

    public static PrefsSyncMessage From(string master, ulong fc, VesselType type, int slot, PlannerPrefs p) => new()
    {
        Master = master,
        Fc = fc,
        Type = type,
        Slot = slot,
        Goal = p.Goal,
        DurationCapHours = p.DurationCapHours,
        ProgressionAutoInclude = p.ProgressionAutoInclude,
        IgnoreUnlocks = p.IgnoreUnlocks,
        AverageBonus = p.AverageBonus,
        UnlockFocus = p.UnlockFocus,
        FarmItem = p.FarmItem,
        FarmGil = p.FarmGil,
        GilTier = p.GilTier,
        Map = p.Map,
    };

    public PlannerPrefs ToPrefs() => new()
    {
        Goal = Goal,
        DurationCapHours = DurationCapHours,
        ProgressionAutoInclude = ProgressionAutoInclude,
        IgnoreUnlocks = IgnoreUnlocks,
        AverageBonus = AverageBonus,
        UnlockFocus = UnlockFocus,
        FarmItem = FarmItem,
        FarmGil = FarmGil,
        GilTier = GilTier,
        Map = Map,
    };

    /// <summary>The key the settings are filed under, as <see cref="Configuration.VesselKey"/> writes it.</summary>
    [JsonIgnore]
    public string Key => $"{Fc}:{Type}:{Slot}";

    /// <summary>A <see cref="Configuration.VesselKey"/> back into its parts; false for anything else.</summary>
    public static bool TryParseKey(string key, out ulong fc, out VesselType type, out int slot)
    {
        fc = 0;
        type = default;
        slot = -1;
        var parts = key.Split(':');
        return parts.Length == 3
               && ulong.TryParse(parts[0], out fc) && fc != 0
               && Enum.TryParse(parts[1], out type) && Enum.IsDefined(type)
               && int.TryParse(parts[2], out slot) && slot is >= 0 and < 4;
    }

    /// <summary>Only well-formed settings are filed: anything on the LAN can publish on this channel.</summary>
    [JsonIgnore]
    public bool IsValid
        => Version == CurrentVersion
           && Master is { Length: > 0 and <= 32 }
           && Fc != 0
           && Enum.IsDefined(Type)
           && Slot is >= 0 and < 4
           && Enum.IsDefined(Goal)
           && DurationCapHours is >= 0 and <= 240
           && GilTier is >= 1 and <= 3;

    /// <summary>Whether two sets of settings plan the same, field for field.</summary>
    public static bool Same(PlannerPrefs a, PlannerPrefs b)
        => a.Goal == b.Goal && a.DurationCapHours == b.DurationCapHours
           && a.ProgressionAutoInclude == b.ProgressionAutoInclude && a.IgnoreUnlocks == b.IgnoreUnlocks
           && a.AverageBonus == b.AverageBonus && a.UnlockFocus == b.UnlockFocus
           && a.FarmItem == b.FarmItem && a.FarmGil == b.FarmGil && a.GilTier == b.GilTier && a.Map == b.Map;

    /// <summary>Names compare as the game writes them, ignoring case and stray spaces.</summary>
    public static bool SameName(string? a, string? b)
        => !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
           && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    public string ToJson() => JsonConvert.SerializeObject(this);

    /// <summary>The message, or null when the text is not a valid one.</summary>
    public static PrefsSyncMessage? Parse(string json)
    {
        try
        {
            var message = JsonConvert.DeserializeObject<PrefsSyncMessage>(json);
            return message is { IsValid: true } ? message : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
