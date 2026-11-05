using Argus.Core.Data;
using Argus.Core.Model;

namespace Argus.Tests;

/// <summary>Telling the Voyage Control Panel's menus apart, and finding entries in them, for collect and resend.</summary>
public class PanelTextTests
{
    private static Vessel Sub(int slot, string name, uint returnTime = 0)
        => new() { Type = VesselType.Submarine, Slot = slot, Name = name, ReturnTime = returnTime };

    private static Vessel Air(int slot, string name, uint returnTime = 0)
        => new() { Type = VesselType.Airship, Slot = slot, Name = name, ReturnTime = returnTime };

    [Fact]
    public void TheFirstMenuIsToldByItsManagementEntries()
    {
        var entries = new[] { "Airship Management", "Submersible Management", "Cancel" };
        Assert.Equal(PanelText.Screen.TypeSelector, PanelText.Classify("", entries, []));
        Assert.Equal(1, PanelText.Find(entries, PanelText.SubmarineManagement));
        Assert.Equal(2, PanelText.Find(entries, PanelText.CancelPanel));
    }

    [Fact]
    public void ListsAreToldByTheirPrompt()
    {
        var entries = new[] { "Nautilus", "Leviathan", "Nothing." };
        Assert.Equal(PanelText.Screen.SubmarineList, PanelText.Classify("Select a submersible.", entries, []));
        Assert.Equal(PanelText.Screen.AirshipList, PanelText.Classify("Select an airship.", entries, []));
    }

    [Fact]
    public void WithoutAPromptAListIsToldByTheVesselsAtTheirSlots()
    {
        var known = new[] { Sub(0, "Nautilus"), Sub(1, "Leviathan"), Air(0, "Skylark") };
        var subs = new[] { "Nautilus  [Rank 54]  [Voyage complete]", "Leviathan  [Rank 40]", "Nothing." };
        var airships = new[] { "Skylark  [Rank 34]", "Nothing." };

        Assert.Equal(PanelText.Screen.SubmarineList, PanelText.Classify("", subs, known));
        Assert.Equal(PanelText.Screen.AirshipList, PanelText.Classify("", airships, known));
        Assert.Equal(PanelText.Screen.Unknown, PanelText.Classify("", new[] { "Yes", "No" }, known));
    }

    [Fact]
    public void AVesselIsOnlyTakenFromItsOwnSlot()
    {
        var entries = new[] { "Nautilus  [Rank 54]", "Nautilus II  [Rank 12]", "Nothing." };
        Assert.Equal(0, PanelText.VesselEntry(entries, 0, "Nautilus"));
        Assert.Equal(1, PanelText.VesselEntry(entries, 1, "Nautilus II"));

        // The entry at the slot must carry the name: a list that is not in slot order is not trusted.
        Assert.Equal(-1, PanelText.VesselEntry(entries, 0, "Nautilus II"));
        Assert.Equal(-1, PanelText.VesselEntry(entries, 3, "Nautilus"));
        Assert.Equal(-1, PanelText.VesselEntry(entries, 0, ""));
    }

    [Fact]
    public void EntriesMatchWholeNotInPart()
    {
        // "Quit" must not find "Quit and discard": a menu entry is matched whole, ignoring case and a full stop.
        var entries = new[] { "Quit and discard", "QUIT." };
        Assert.Equal(1, PanelText.Find(entries, PanelText.QuitVesselMenu));
        Assert.Equal(-1, PanelText.Find(new[] { "Quit and discard" }, PanelText.QuitVesselMenu));
    }

    [Fact]
    public void TheDeployEntryIsFoundForBothTypesAndNeverGuessed()
    {
        var sub = new[] { "Deploy submersible on subaquatic voyage.", "View previous voyage log.", "Repair submersible components.", "Quit" };
        Assert.Equal(0, PanelText.FindDeploy(sub, VesselType.Submarine));

        var airship = new[] { "View previous voyage log.", "Deploy airship on exploratory voyage.", "Repair airship components.", "Quit" };
        Assert.Equal(1, PanelText.FindDeploy(airship, VesselType.Airship));

        // Two candidates: not ours to pick between.
        var two = new[] { "Deploy airship.", "Deploy airship on previous route.", "Quit" };
        Assert.Equal(-1, PanelText.FindDeploy(two, VesselType.Airship));
        Assert.Equal(-1, PanelText.FindDeploy(new[] { "Repair submersible components.", "Quit" }, VesselType.Submarine));
    }

    [Fact]
    public void TheRunTakesReturnedVesselsSubmarinesFirst()
    {
        var now = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
        var past = (uint)new DateTimeOffset(now.AddHours(-1)).ToUnixTimeSeconds();
        var future = (uint)new DateTimeOffset(now.AddHours(5)).ToUnixTimeSeconds();
        var vessels = new[]
        {
            Air(0, "Skylark", past),
            Sub(2, "Kraken", past),
            Sub(0, "Nautilus", past),
            Sub(1, "Leviathan", future),
            Sub(3, "Idle"),
        };

        var run = PanelText.Returned(vessels, now).Select(v => v.Name).ToArray();
        Assert.Equal(new[] { "Nautilus", "Kraken", "Skylark" }, run);
    }
}
