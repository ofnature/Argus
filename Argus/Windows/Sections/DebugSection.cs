#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using Argus.Core.Calc;
using Argus.Core.Model;
using Argus.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace Argus.Windows.Sections;

/// <summary>
/// DEBUG-only page for the first in-game verification pass: compares the offline distance/time formulas with the
/// client's own functions, dumps the planner's destination rows and status flags, and shows raw workshop state.
/// </summary>
internal static unsafe class DebugSection
{
    private static List<string> distanceReport = new();

    public static void Draw(Plugin plugin)
    {
        MainWindow.PageHeader("Debug", "Verification helpers. Not compiled into Release. Live planning already uses the client voyage functions; the comparisons below test the offline formulas.");
        var scale = ImGuiHelpers.GlobalScale;

        if (Buttons.Action("Compare airship legs with the game", true, 280f * scale))
            distanceReport = CompareAirship(plugin.Data);
        ImGui.SameLine();
        if (Buttons.Action("Compare submarine legs (map 1)", true, 280f * scale))
            distanceReport = CompareSubmarine(plugin.Data, 1);

        foreach (var line in distanceReport.Take(60))
            Styling.Text(line, line.Contains("MISMATCH") ? Styling.AccentRose : Styling.TextSecondary);

        Styling.VSpace(8f);
        Styling.SectionLabel("Workshop watch");
        var p = plugin.Fleet.LastProbe;
        Styling.Text($"territory {p.Territory} · HousingManager {(p.HousingManager ? "ok" : "null")} · WorkshopTerritory {(p.WorkshopTerritory ? "ok" : "null")}{(p.IslandSanctuary ? " · island sanctuary (skipped)" : string.Empty)}",
            p.WorkshopTerritory ? Styling.TextSecondary : Styling.AccentRose);
        Styling.Text($"FC {p.FreeCompanyId:X} · {p.Submarines} subs (first return {p.FirstSubReturn}) · {p.Airships} airships (first return {p.FirstAirReturn}) · in workshop = {plugin.Fleet.InWorkshop} · vessel data loaded = {plugin.Fleet.HasLiveVessels}", Styling.TextSecondary);
        Styling.TextWrapped("Verified 2026-09-07: the client leaves both arrays zeroed until the Voyage Control Panel has been used this visit, and it fills only the side you opened. Zeros here before touching the panel are expected.", Styling.TextMuted);
        foreach (var line in plugin.Fleet.Log)
            Styling.Text(line, Styling.TextDim);

        Styling.VSpace(8f);
        Styling.SectionLabel("Workshop");
        var hm = HousingManager.Instance();
        if (hm == null || hm->WorkshopTerritory == null)
        {
            Styling.Text("WorkshopTerritory is null (not in a workshop).", Styling.TextMuted);
        }
        else
        {
            var ws = hm->WorkshopTerritory;
            Styling.Text($"ActiveAirshipId = {ws->Airship.ActiveAirshipId}, AirshipCount = {ws->Airship.AirshipCount}, FC = {Core.Game.WorkshopReader.CurrentFreeCompanyId():X}", Styling.TextSecondary);
            var sub = ws->Submersible.DataPointers[4].Value;
            Styling.Text(sub == null ? "Current sub pointer: null" : $"Current sub: rank {sub->RankId}, register {sub->RegisterTime}, return {sub->ReturnTime}", Styling.TextSecondary);
            var agent = FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentSubmersibleExploration.Instance();
            if (agent != null)
                Styling.Text($"Sub agent: active={agent->IsAgentActive()} map={agent->MapId} selected={agent->SelectedPointsCount} distance={agent->VoyageDistance}/{agent->VoyageDistanceMax} tanks={agent->CeruleumTanks} inv={agent->CeruleumTanksInInventory} exp={agent->Exp}", Styling.TextSecondary);
        }

        Styling.VSpace(8f);
        Styling.SectionLabel("Parts");
        var parts = plugin.PartsInterop;
        Styling.Text($"menu open = {parts.IsMenuOpen} · parts window open = {parts.IsPartsWindowOpen} · can start = {parts.CanStart} · stage {parts.StageName} ({parts.Installed} done, {parts.Remaining} left)",
            parts.CanStart || parts.Running ? Styling.TextSecondary : Styling.AccentRose);
        if (parts.Blocker is { } partsBlocker)
            Styling.Text($"blocked: {partsBlocker}", Styling.AccentRose);
        if (parts.LastError != null)
            Styling.TextWrapped($"last error: {parts.LastError}", Styling.AccentRose);
        if (parts.LastResult != null)
            Styling.TextWrapped($"last result: {parts.LastResult}", Styling.AccentMint);

        if (parts.IsPartsWindowOpen)
        {
            for (var slot = 0; slot < 4; slot++)
            {
                if (slot > 0)
                    ImGui.SameLine();
                var captured = slot;
                if (Buttons.Action($"Slot {captured}", true, 70f * scale))
                    Service.Log.Information("Argus: slot {Slot} request sent = {Sent}", captured, parts.RequestSlot(captured));
            }

            Styling.TextWrapped($"picker open = {parts.IsPickerOpen}. Press a slot: the game should open its part list.", Styling.TextMuted);
        }

        var entries = parts.MenuEntries();
        if (entries.Count == 0)
            Styling.Text("No SelectString menu open.", Styling.TextMuted);
        else
            foreach (var (entry, i) in entries.Select((e, i) => (e, i)))
                Styling.Text($"  [{i}] {entry}", Styling.TextDim);

        Styling.VSpace(8f);
        Styling.SectionLabel("Planner addon");
        var interop = plugin.PlannerInterop;
        if (!interop.IsPlannerOpen)
        {
            Styling.Text("AirShipExploration is not open.", Styling.TextMuted);
            return;
        }

        var summary = interop.Summary();
        if (summary is { } s)
            Styling.Text($"fuel '{s.Fuel}' distance '{s.Distance}' returns '{s.ReturnsAt}' time '{s.VoyageTime}' · selection empty = {interop.SelectionIsEmpty()}", Styling.TextSecondary);
        var current = interop.CurrentVessel();
        Styling.Text(current == null ? "vessel: unknown" : $"vessel: {current.Value.Type} slot {current.Value.Slot} map {current.Value.Map}", Styling.TextSecondary);

        using var table = ImRaii.Table("##dbg_rows", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingFixedFit);
        if (!table.Success)
            return;
        ImGui.TableSetupColumn("#");
        ImGui.TableSetupColumn("Full", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Short");
        ImGui.TableSetupColumn("Rank");
        ImGui.TableSetupColumn("Flag");
        ImGui.TableHeadersRow();
        foreach (var row in interop.ReadDestinations())
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); Styling.Text(row.Index.ToString(), Styling.TextDim);
            ImGui.TableNextColumn(); Styling.Text(row.NameFull, Styling.TextStrong);
            ImGui.TableNextColumn(); Styling.Text(row.NameShort, Styling.TextSecondary);
            ImGui.TableNextColumn(); Styling.Text(row.RequiredRank.ToString(), Styling.TextSecondary);
            ImGui.TableNextColumn(); Styling.Text(row.StatusFlag.ToString(), row.CanBeSelected ? Styling.AccentMint : Styling.AccentRose);
        }
    }

    /// <summary>Offline formula vs HousingManager.GetAirshipVoyageTimeAndDistance for every start→sector leg at speed 100.</summary>
    private static List<string> CompareAirship(GameData data)
    {
        var lines = new List<string>();
        const short speed = 100;
        var start = data.AirshipStart;
        foreach (var s in data.AirshipSectors.Values.Where(x => x.IsDestination).OrderBy(x => x.Id))
        {
            uint time = 0, distance = 0;
            HousingManager.GetAirshipVoyageTimeAndDistance(127, (byte)s.Id, speed, &time, &distance);
            var survey = HousingManager.GetAirshipSurveyDuration((byte)s.Id, speed);
            var mineD = VoyageMath.LegDistanceFormula(start, s);
            var mineT = VoyageMath.LegSecondsFormula(start, s, speed);
            var mineS = VoyageMath.SurveySecondsFormula(s, speed);
            var flag = mineD == distance && Math.Abs(mineT - (int)time) <= 60 && Math.Abs(mineS - (int)survey) <= 60 ? "ok" : "MISMATCH";
            lines.Add($"{s.Letter}: game d={distance} t={time}s survey={survey}s | formula d={mineD} t={mineT}s survey={mineS}s  {flag}");
        }

        return lines;
    }

    private static List<string> CompareSubmarine(GameData data, uint map)
    {
        var lines = new List<string>();
        const short speed = 100;
        var start = data.StartFor(VesselType.Submarine, map);
        foreach (var s in data.DestinationsOf(VesselType.Submarine, map))
        {
            var distance = HousingManager.GetSubmarineVoyageDistance((byte)start.Id, (byte)s.Id);
            var time = HousingManager.GetSubmarineVoyageTime((byte)start.Id, (byte)s.Id, speed);
            var survey = HousingManager.GetSubmarineSurveyDuration((byte)s.Id, speed);
            var mineD = VoyageMath.LegDistanceFormula(start, s);
            var mineT = VoyageMath.LegSecondsFormula(start, s, speed);
            var mineS = VoyageMath.SurveySecondsFormula(s, speed);
            var flag = mineD == distance && Math.Abs(mineT - (int)time) <= 60 && Math.Abs(mineS - (int)survey) <= 60 ? "ok" : "MISMATCH";
            lines.Add($"{s.Letter}: game d={distance} t={time}s survey={survey}s | formula d={mineD} t={mineT}s survey={mineS}s  {flag}");
        }

        return lines;
    }
}
#endif
