using System;
using System.Collections.Generic;
using System.Linq;
using Argus.Core.Model;

namespace Argus.Core.Data;

/// <summary>
/// What the Voyage Control Panel's menus say across the client languages (AutoRetainer's <c>Lang</c> lists), and how
/// to tell the panel's menus apart. The panel opens on a type selector (submarines or airships), then a list of that
/// type's vessels, then the vessel's own menu; all three are the same <c>SelectString</c> addon. Pure strings, so the
/// matching is tested without the game.
/// </summary>
public static class PanelText
{
    public enum Screen
    {
        /// <summary>Not one of the panel's lists (a vessel's own menu is told by its repair entry instead).</summary>
        Unknown,

        /// <summary>The first menu: submersible management, airship management, cancel.</summary>
        TypeSelector,

        SubmarineList,
        AirshipList,
    }

    public static readonly string[] SubmarineManagement =
        { "Submersible Management", "潜水艦の管理", "管理潜水艇", "管理潛水艇", "Tauchboot verwalten", "Contrôle sous-marin", "잠수함 관리" };

    public static readonly string[] AirshipManagement =
        { "Airship Management", "飛空艇の管理", "管理飞空艇", "管理飛空艇", "Luftschiff verwalten", "Contrôle aérien", "비공정 관리" };

    /// <summary>Leaves the panel from the type selector.</summary>
    public static readonly string[] CancelPanel = { "Cancel", "キャンセル", "取消", "Abbrechen", "Annuler", "취소" };

    /// <summary>Leaves a vessel list, back to the type selector.</summary>
    public static readonly string[] NothingList = { "Nothing.", "やめる", "取消", "Nichts", "Annuler", "그만두기" };

    /// <summary>Leaves a vessel's own menu, back to its list.</summary>
    public static readonly string[] QuitVesselMenu = { "Quit", "やめる", "取消", "Beenden", "Annuler", "그만두기" };

    /// <summary>The prompt above a vessel list.</summary>
    public static readonly string[] SubmarinePrompt =
        { "Select a submersible.", "潜水艦を選択してください。", "请选择潜水艇。", "請選擇潛水艇。", "Wähle ein Tauchboot.", "Choisissez un sous-marin.", "잠수함을 선택하십시오." };

    public static readonly string[] AirshipPrompt =
        { "Select an airship.", "飛空艇を選択してください。", "请选择飞空艇。", "請選擇飛空艇。", "Wähle ein Luftschiff.", "Choisissez un aéronef.", "비공정을 선택하십시오." };

    /// <summary>
    /// The deploy entry on a submarine's menu (AutoRetainer's list). AutoRetainer never sends airships this way, so the
    /// airship wording is English only (seen in game 2026-11-05); <see cref="FindDeploy"/> also accepts the one English
    /// entry that starts with "Deploy".
    /// </summary>
    public static readonly string[] DeploySubmarine =
        { "Deploy submersible on subaquatic voyage", "ボイジャー出港", "出发", "出發", "Auf Erkundung gehen", "Expédier le sous-marin", "탐사 출항" };

    public static readonly string[] DeployAirship = { "Deploy airship on exploratory voyage" };

    /// <summary>Trimmed, lower case, no trailing full stop: menus and the lists above differ in exactly that.</summary>
    public static string Normalise(string text) => text.Trim().TrimEnd('.', '。').Trim().ToLowerInvariant();

    /// <summary>Index of the entry that is one of <paramref name="phrases"/> (whole entry, not a part of it), or -1.</summary>
    public static int Find(IReadOnlyList<string> entries, IEnumerable<string> phrases)
    {
        var wanted = phrases.Select(Normalise).ToHashSet();
        for (var i = 0; i < entries.Count; i++)
        {
            if (wanted.Contains(Normalise(entries[i])))
                return i;
        }

        return -1;
    }

    /// <summary>The deploy entry on a vessel's menu, or -1.</summary>
    public static int FindDeploy(IReadOnlyList<string> entries, VesselType type)
    {
        var exact = Find(entries, type == VesselType.Airship ? DeployAirship : DeploySubmarine);
        if (exact >= 0)
            return exact;

        // English names the vessel type in its deploy entry, and no other entry on the menu starts with the word.
        var english = -1;
        for (var i = 0; i < entries.Count; i++)
        {
            if (!Normalise(entries[i]).StartsWith("deploy", StringComparison.Ordinal))
                continue;
            if (english >= 0)
                return -1;
            english = i;
        }

        return english;
    }

    /// <summary>
    /// Which of the panel's lists is open, from its prompt and entries. When the prompt does not say (another language,
    /// or the text sits elsewhere), a list whose entries carry every known vessel of one type at its slot is that type's.
    /// </summary>
    public static Screen Classify(string prompt, IReadOnlyList<string> entries, IReadOnlyList<Vessel> known)
    {
        if (Find(entries, SubmarineManagement) >= 0 || Find(entries, AirshipManagement) >= 0)
            return Screen.TypeSelector;

        var p = Normalise(prompt);
        if (p.Length > 0)
        {
            if (SubmarinePrompt.Any(s => p.Contains(Normalise(s), StringComparison.Ordinal)))
                return Screen.SubmarineList;
            if (AirshipPrompt.Any(s => p.Contains(Normalise(s), StringComparison.Ordinal)))
                return Screen.AirshipList;
        }

        var subs = known.Where(v => v.Type == VesselType.Submarine).ToList();
        var airships = known.Where(v => v.Type == VesselType.Airship).ToList();
        var subList = subs.Count > 0 && subs.All(v => VesselEntry(entries, v.Slot, v.Name) >= 0);
        var airList = airships.Count > 0 && airships.All(v => VesselEntry(entries, v.Slot, v.Name) >= 0);
        if (subList != airList)
            return subList ? Screen.SubmarineList : Screen.AirshipList;

        return Screen.Unknown;
    }

    /// <summary>
    /// Index of a vessel's entry in its list. The list is in workshop slot order (AutoRetainer selects by that index),
    /// so the entry at the vessel's slot has to carry its name; anything else is not trusted.
    /// </summary>
    public static int VesselEntry(IReadOnlyList<string> entries, int slot, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || slot < 0 || slot >= entries.Count)
            return -1;
        return entries[slot].Contains(name.Trim(), StringComparison.OrdinalIgnoreCase) ? slot : -1;
    }

    /// <summary>The vessels a collect-and-resend run takes: back from a voyage and waiting, submarines first, by slot.</summary>
    public static List<Vessel> Returned(IEnumerable<Vessel> vessels, DateTime nowUtc)
        => vessels.Where(v => v.IsReturned(nowUtc)).OrderBy(v => v.Type).ThenBy(v => v.Slot).ToList();
}
