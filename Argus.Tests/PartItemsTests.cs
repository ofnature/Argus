using System.Linq;
using Argus.Core.Data;
using Argus.Core.Model;

namespace Argus.Tests;

public class PartItemsTests
{
    private readonly GameData data = GameDataFixture.Load();

    [Fact]
    public void EveryPartRowMapsToAnItem()
    {
        Assert.Equal(40, PartItems.Count(VesselType.Submarine));
        Assert.Equal(28, PartItems.Count(VesselType.Airship));

        foreach (var type in new[] { VesselType.Submarine, VesselType.Airship })
        {
            foreach (var part in data.Parts(type).Values)
                Assert.True(PartItems.ItemFor(type, part.Id) != 0, $"{type} part row {part.Id} has no item");
        }
    }

    [Fact]
    public void KnownPartsResolveToTheRightItems()
    {
        // Verified against the game's item names when the table was baked.
        Assert.Equal(21792u, PartItems.ItemFor(VesselType.Submarine, 1));   // Shark-class Bow
        Assert.Equal(10156u, PartItems.ItemFor(VesselType.Airship, 1));     // Bronco-type Hull
        Assert.Equal(14006u, PartItems.ItemFor(VesselType.Airship, 28));    // Viltgance-type Aftcastle
        Assert.Equal(0u, PartItems.ItemFor(VesselType.Airship, 999));
    }

    [Fact]
    public void ItemsAreUniquePerVesselType()
    {
        foreach (var type in new[] { VesselType.Submarine, VesselType.Airship })
        {
            var items = data.Parts(type).Values.Select(p => PartItems.ItemFor(type, p.Id)).ToList();
            Assert.Equal(items.Count, items.Distinct().Count());
        }
    }
}
