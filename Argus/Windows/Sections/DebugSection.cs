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
