using System.IO;
using System.Linq;
using C7Engine;
using C7Engine.Editor;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using QueryCiv3;
using Xunit;

namespace EngineTests.Editor;

// Phase 8: Civ3's and Play the World's files (.bic, .bix) open as Conquests'
// do. The whole of the user's civ3PTW/Scenarios folder plays through
// ScenarioCorpusTest (C7_EDITOR_CORPUS_DIR).
public class LegacyBiqTest {
	private static string Ptw(string name) => Path.Join(Civ3Location.GetCiv3Path() ?? "", "civ3PTW", "Scenarios", name);

	private static SaveGame Open(string path) {
		string conquests = Path.Join(Civ3Location.GetCiv3Path(), "Conquests");
		return ScenarioFiles.Open(path, PathUtils.defaultBicPath, ScenarioCorpusTest.PediaIconsFor(conquests, Path.GetDirectoryName(path)));
	}

	[SkippableFact]
	public void AVanillaMapHasItsSeaWhereItWas() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), Civ3TestData.SkipReason);
		string path = Ptw("8 Kingdoms.bic");
		Skip.If(!File.Exists(path), "No 8 Kingdoms");
		BiqData raw = BiqData.LoadFile(path);
		Assert.True(raw.IsLegacy);
		SaveGame save = Open(path);
		// Civ3's 12 terrains have no marsh or volcano: their coast, sea and
		// ocean are Conquests' 11 to 13.
		Assert.DoesNotContain(save.Map.tiles, t => t.baseTerrain is "marsh" or "volcano");
		Assert.Contains(save.Map.tiles, t => t.baseTerrain == "ocean");
		// A map without player data: the players are chosen at setup, where
		// the generator finds them starts.
		Assert.Empty(save.Players);
		Assert.NotEmpty(save.WorldSizes);
		WorldSize size = save.WorldSizes.OrderBy(w => w.width * w.height).First();
		new GameSetup {
			playerCivilization = save.Civilizations.First(c => !c.isBarbarian),
			difficulty = save.Difficulties.First(),
			worldCharacteristics = new WorldCharacteristics(save) { worldSize = size, mapSeed = 1 },
			opponents = Enumerable.Repeat(new SelectedOpponent { isRandom = true }, 3).ToList(),
			victoryConditions = save.VictoryConditions ?? new VictoryConditions(),
		}.Populate(save);
		Assert.True(save.Map.startingLocations.Count >= 4);
		Assert.All(save.Units, u => Assert.DoesNotContain(save.Map.tiles.First(t => t.X == u.currentLocation.X && t.Y == u.currentLocation.Y).baseTerrain,
			new[] { "coast", "sea", "ocean" }));
	}

	[SkippableFact]
	public void PlayTheWorldsRulesOpen() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), Civ3TestData.SkipReason);
		string path = Path.Join(Civ3Location.GetCiv3Path(), "civ3PTW", "civ3X.bix");
		Skip.If(!File.Exists(path), "No civ3X.bix");
		SaveGame save = Open(path);
		Assert.Contains(save.UnitPrototypes, u => u.name == "Warrior");
		Assert.Contains(save.Buildings, b => b.name == "Palace");
		// Conquests' 14 terrains, whatever the file's 12.
		Assert.Equal(14, save.TerrainTypes.Count(t => t != TerrainType.NONE && !string.IsNullOrEmpty(t.Key)));
	}
}
