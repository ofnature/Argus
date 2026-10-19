using System;
using Argus.Core;
using Newtonsoft.Json.Linq;

namespace Argus.Tests;

public class CharonFetchTests
{
    [Fact]
    public void ListMatchesCharonsShape()
    {
        // Charon's FetchList.Parse wants { "items": [ { "itemId": n, "quantity": n } ] } and drops zero entries.
        var json = CharonFetch.ListJson([(21794u, 1), (0u, 1), (21795u, 0), (21796u, 2)]);
        var items = (JArray)JObject.Parse(json)["items"]!;

        Assert.Equal(2, items.Count);
        Assert.Equal(21794u, (uint)items[0]["itemId"]!);
        Assert.Equal(1, (int)items[0]["quantity"]!);
        Assert.Equal(21796u, (uint)items[1]["itemId"]!);
        Assert.Null(items[0]["hq"]);
    }

    [Fact]
    public void ReadsCharonsFetchReport()
    {
        // FetchReport.ToJson's wire form.
        var status = CharonFetch.ParseStatus("""
            {"state":"waitingForPerson","retainer":"T'sola","itemId":21794,"wanted":1,"movedNq":0,"movedHq":0,"moved":0,"queued":2,"reason":"open T'sola at a bell","future":1}
            """);

        Assert.NotNull(status);
        Assert.Equal("waitingForPerson", status.State);
        Assert.Equal("T'sola", status.Retainer);
        Assert.Equal(2, status.Queued);

        Assert.Equal("idle", CharonFetch.ParseStatus("{}")!.State);
        Assert.Null(CharonFetch.ParseStatus("not json"));
    }

    [Fact]
    public void ChestPagesAreNotFetchable()
    {
        var found = PartLocator.Parse("""
            {"retainers":[{"retainer":"T'sola","nq":1,"hq":0,"ageMinutes":60}],"retainerKnown":1,
             "chest":{"known":true,"capturedUtc":"2026-10-19T10:00:00Z","pages":[{"page":2,"nq":1,"hq":0}]}}
            """, new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc));

        Assert.True(found!.Places[0].IsRetainer);
        Assert.False(found.Places[1].IsRetainer);
    }
}
