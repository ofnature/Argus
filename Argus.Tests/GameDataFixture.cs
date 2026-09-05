using System.IO;
using Argus.Core.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Argus.Tests;

/// <summary>Loads <c>Fixtures/gamedata.json</c> (dumped from the game sheets via XIVAPI) into a <see cref="GameData"/>.</summary>
public static class GameDataFixture
{
    private static GameData? cached;

    private sealed class Dto
    {
        public SectorInfo[] Sectors = [];
        public PartInfo[] Parts = [];
        public RankInfo[] Ranks = [];
        public MapInfo[] Maps = [];
    }

    public static GameData Load()
    {
        if (cached != null)
            return cached;

        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "gamedata.json");
        var settings = new JsonSerializerSettings { Converters = { new StringEnumConverter() } };
        var dto = JsonConvert.DeserializeObject<Dto>(File.ReadAllText(path), settings)!;
        cached = new GameData(dto.Sectors, dto.Parts, dto.Ranks, dto.Maps);
        return cached;
    }

    /// <summary>A stock Shark submarine at the given rank (hull 3, stern 4, bow 1, bridge 2).</summary>
    public static Build Shark(GameData data, int rank) => Build.From(data, VesselType.Submarine, rank, 3, 4, 1, 2);

    /// <summary>Full Bronco airship at the given rank (hull 1, rigging 7, forecastle 13, aftcastle 19).</summary>
    public static Build Bronco(GameData data, int rank) => Build.From(data, VesselType.Airship, rank, 1, 7, 13, 19);
}
