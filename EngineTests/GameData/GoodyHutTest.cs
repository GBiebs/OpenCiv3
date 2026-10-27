using System.Collections.Generic;
using System.IO;
using System.Linq;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using QueryCiv3;
using Xunit;

namespace EngineTests.GameData;

// Goody huts (Civ3's "minor tribes"): placed like the huts in Civ3's own
// maps at the start of a game, kept in saves and imported from Civ3's.
public class GoodyHutTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;

	public GoodyHutTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
	}

	private List<Tile> Huts(C7GameData.GameData game) => game.map.tiles.Where(t => t.hasGoodyHut).ToList();

	// One per 55 land tiles, on land without a resource, 4 or more tiles
	// apart, off the camps and the starting spots.
	[Fact]
	public void GeneratedMapsGetHutsLikeCiv3s() {
		List<Tile> huts = Huts(gameData);
		int wanted = gameData.map.tiles.Count(t => t.IsLand()) / MapGenerator.LandTilesPerGoodyHut;
		Assert.True(wanted > 0);
		Assert.InRange(huts.Count, wanted * 3 / 4, wanted);
		Assert.All(huts, t => Assert.True(t.IsLand() && t.Resource == Resource.NONE && !t.hasBarbarianCamp
			&& !gameData.map.startingLocations.Contains(t)));
		Assert.All(huts, h => Assert.All(huts.Where(o => o != h), o => Assert.True(h.DistanceTo(o) >= MapGenerator.GoodyHutSpacing)));
		Assert.All(huts, h => Assert.All(gameData.map.barbarianCamps, c => Assert.True(h.DistanceTo(c) >= MapGenerator.GoodyHutSpacing)));
	}

	[Fact]
	public void HutsStayInSaves() {
		List<Tile> huts = Huts(gameData);
		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		Assert.Equal(huts.Select(t => (t.XCoordinate, t.YCoordinate)), Huts(reloaded).Select(t => (t.XCoordinate, t.YCoordinate)));
	}

	[SkippableFact]
	public void Civ3SavesBringTheirHuts() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), "No Civ3 install found.");
		// A random-map Civ3 game at its start (the save tests download it).
		string file = Path.Combine(PathUtils.getDataPath("saves"), "12345.SAV");
		Skip.If(!File.Exists(file), "The sample save isn't downloaded.");
		byte[] biq = QueryCiv3.Util.ReadFile(Path.Combine(Civ3Location.GetCiv3Path(), "Conquests", "conquests.biq"));
		SavData sav = new(QueryCiv3.Util.ReadFile(file), biq);
		int inSave = sav.Tile.Count(t => t.GoodyHut);
		Assert.True(inSave > 0);

		SaveGame save = ImportCiv3.ImportSav(file, PathUtils.defaultBicPath,
			_ => PathUtils.defaultPediaIconsPath, _ => PathUtils.defaultCivilopediaTextPath);
		Assert.Equal(inSave, save.Map.tiles.Count(t => t.features.Contains(Tile.TileOverlays.GOODY_HUT)));
	}
}
