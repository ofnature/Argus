using System;
using Argus.Core;

namespace Argus.Tests;

public class PartLocatorTests
{
    private static readonly DateTime Now = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

    private static string Duration(TimeSpan t) => $"{(int)t.TotalHours} h";

    [Fact]
    public void ReadsRetainersAndChestPagesFromCharon()
    {
        // Charon.Containers.GetItemJson's shape (ChestContentsIpc.ItemJson).
        const string json = """
            {"itemId":21794,
             "retainers":[{"retainer":"T'sola","nq":1,"hq":0,"capturedUtc":"2026-10-18T07:00:00Z","ageMinutes":1740},
                          {"retainer":"T'sala","nq":0,"hq":0,"capturedUtc":"2026-10-18T07:00:00Z","ageMinutes":1740}],
             "retainerKnown":2,
             "chest":{"known":true,"capturedUtc":"2026-10-19T10:00:00Z","pages":[{"page":3,"nq":2,"hq":0}]},
             "total":{"nq":3,"hq":0}}
            """;

        var found = PartLocator.Parse(json, Now);

        Assert.NotNull(found);
        Assert.Equal(2, found.RetainersKnown);
        Assert.Equal(2, found.Places.Count);
        Assert.Equal("T'sola", found.Places[0].Name);
        Assert.Equal(1, found.Places[0].Count);
        Assert.Equal("FC chest page 3", found.Places[1].Name);
        Assert.Equal(TimeSpan.FromHours(2), found.Places[1].Age);
        Assert.Equal("on T'sola (1, 29 h ago), FC chest page 3 (2, 2 h ago)", PartLocator.Describe(found, Duration));
    }

    [Fact]
    public void NothingFoundSaysWhatCharonHasRead()
    {
        var found = PartLocator.Parse("""{"retainers":[],"retainerKnown":2,"chest":{"known":false,"pages":[]}}""", Now);
        Assert.Equal("not on the 2 retainers Charon has read", PartLocator.Describe(found, Duration));

        var none = PartLocator.Parse("""{"retainers":[],"retainerKnown":0}""", Now);
        Assert.Equal("Charon has not read any retainer yet", PartLocator.Describe(none, Duration));
    }

    [Fact]
    public void ToleratesFailuresAndNewFields()
    {
        // A failed lookup is a payload without the arrays; garbage is no answer at all.
        Assert.Empty(PartLocator.Parse("""{"error":"no data"}""", Now)!.Places);
        Assert.Null(PartLocator.Parse("not json", Now));

        // Extend-only: unknown fields are ignored.
        var found = PartLocator.Parse("""{"retainers":[{"retainer":"T'sola","nq":2,"hq":1,"future":true}],"retainerKnown":1,"newField":[]}""", Now);
        Assert.Equal(3, found!.Places[0].Count);
        Assert.Null(found.Places[0].Age);

        // Without Charon there is nothing to say.
        Assert.Null(PartLocator.Describe(null, Duration));
    }
}
