using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using QueryCiv3;
using Xunit;

namespace EngineTests.GameData;

// Goody huts (Civ3's "minor tribes", #934): placed like the huts in Civ3's
// own maps at the start of a game, kept in saves and imported from Civ3's;
// a unit entering one gets what CivFanatics' "Probabilities of goody huts
// (C3C)" says, with Civ3's messages.
public class GoodyHutTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;

	public GoodyHutTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		us = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		us.civilization.traits.Remove(Civilization.Trait.Expansionist);
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private List<Tile> Huts(C7GameData.GameData game) => game.map.tiles.Where(t => t.hasGoodyHut).ToList();

	private void Difficulty(string name) => gameData.gameDifficulty = gameData.difficulties.Find(d => d.Name == name);

	// A hut on open land away from every city and unit, and one of our
	// warriors on it.
	private (Tile hut, MapUnit warrior) Hut() {
		Tile hut = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsAllowCities() && !t.hasGoodyHut && t.Resource == Resource.NONE
			&& gameData.cities.All(c => c.location.DistanceTo(t) > 4) && gameData.mapUnits.All(u => u.location.DistanceTo(t) > 2)
			&& t.neighbors.Values.Count(n => n.IsLand() && !n.overlayTerrainType.impassable) >= 3);
		hut.hasGoodyHut = true;
		MapUnit warrior = gameData.SpawnUnit(us, gameData.unitPrototypes.Find(u => u.name == "Warrior"), hut);
		return (hut, warrior);
	}

	private void HomeCity() => CityInteractions.BuildCity(us.units.First(u => u.location.unitsOnTile.All(o => o.owner == us)
		&& !u.location.hasGoodyHut).location, us, "Home");

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

	// The table by difficulty, the outcomes that can't happen left out.
	[Fact]
	public void ChancesFollowCiv3sTable() {
		(Tile hut, MapUnit warrior) = Hut();
		// No city yet: no barbarians (and no settler, with no more cities
		// than the average).
		Difficulty("Chieftain");
		Dictionary<GoodyHuts.Outcome, double> chances = GoodyHuts.OutcomeChances(gameData, warrior, hut);
		Assert.DoesNotContain(GoodyHuts.Outcome.Barbarians, chances.Keys);
		Assert.Equal(1, chances.Values.Sum(), 6);
		Assert.Equal(35.0 / 15, chances[GoodyHuts.Outcome.Tech] / chances[GoodyHuts.Outcome.Gold], 6);

		// With a city, at Creator (Civ3's Sid) only angry tribesmen remain.
		HomeCity();
		Difficulty("Creator");
		Assert.Equal(1, GoodyHuts.OutcomeChances(gameData, warrior, hut)[GoodyHuts.Outcome.Barbarians], 6);
		Difficulty("Deity");
		chances = GoodyHuts.OutcomeChances(gameData, warrior, hut);
		Assert.Equal(85.0 / 5, chances[GoodyHuts.Outcome.Barbarians] / chances[GoodyHuts.Outcome.Gold], 6);

		// An Expansionist civ gets the next easier level's, no barbarians.
		us.civilization.traits.Add(Civilization.Trait.Expansionist);
		chances = GoodyHuts.OutcomeChances(gameData, warrior, hut);
		Assert.DoesNotContain(GoodyHuts.Outcome.Barbarians, chances.Keys);
		Assert.Equal(5.0 / 5, chances[GoodyHuts.Outcome.Tech] / chances[GoodyHuts.Outcome.Gold], 6);
	}

	// The article's conditions for each outcome.
	[Fact]
	public void EachOutcomeHasItsConditions() {
		(Tile hut, MapUnit warrior) = Hut();
		bool Possible(GoodyHuts.Outcome o) => GoodyHuts.IsPossible(o, gameData, warrior, hut);

		Assert.True(Possible(GoodyHuts.Outcome.Gold));
		hut.Resource = gameData.Resources.First(r => r != Resource.NONE);
		Assert.False(Possible(GoodyHuts.Outcome.Gold));
		hut.Resource = Resource.NONE;

		Assert.True(Possible(GoodyHuts.Outcome.Tech));
		string era = us.eraCivilopediaName;
		us.eraCivilopediaName = gameData.techs.Select(t => t.EraCivilopediaName).First(e => e != era);
		Assert.False(Possible(GoodyHuts.Outcome.Tech));
		us.eraCivilopediaName = era;

		Assert.True(Possible(GoodyHuts.Outcome.City));
		Assert.True(Possible(GoodyHuts.Outcome.Mercenaries));

		// Barbarians: we need a city and a soldier, and no city by the hut.
		Assert.False(Possible(GoodyHuts.Outcome.Barbarians));
		HomeCity();
		Assert.True(Possible(GoodyHuts.Outcome.Barbarians));
		gameData.barbarianInfo.barbarianActivity = BarbarianActivity.None;
		Assert.False(Possible(GoodyHuts.Outcome.Barbarians));
		gameData.barbarianInfo.barbarianActivity = BarbarianActivity.Roaming;
		us.civilization.traits.Add(Civilization.Trait.Expansionist);
		Assert.False(Possible(GoodyHuts.Outcome.Barbarians));
		us.civilization.traits.Remove(Civilization.Trait.Expansionist);

		// Settler: none of ours about, and fewer cities than the average.
		Assert.False(Possible(GoodyHuts.Outcome.Settler));
		foreach (MapUnit settler in us.units.Where(u => u.unitType.actions.Contains(UnitAction.BuildCity)).ToList()) {
			gameData.RemoveUnit(settler);
		}
		// Our one city is more than the average.
		Assert.False(Possible(GoodyHuts.Outcome.Settler));
		Player rival = gameData.players.First(p => p != us && !p.isBarbarians);
		foreach (Tile site in gameData.map.tiles.Where(t => Tile.IsTileValid(t) && t.IsAllowCities() && t.unitsOnTile.Count == 0
			&& gameData.cities.All(c => c.location.DistanceTo(t) > 3) && t.DistanceTo(hut) > 3).Take(gameData.players.Count * 2)) {
			CityInteractions.BuildCity(site, rival, rival.GetNextCityName());
		}
		Assert.True(Possible(GoodyHuts.Outcome.Settler));

		Tile next = hut.neighbors.Values.First(n => n.IsAllowCities());
		CityInteractions.BuildCity(next, rival, "Neighbor");
		Assert.False(Possible(GoodyHuts.Outcome.Barbarians));
		Assert.False(Possible(GoodyHuts.Outcome.City));
	}

	[Fact]
	public void EachOutcomeDoesWhatItSays() {
		(Tile hut, MapUnit warrior) = Hut();
		string Apply(GoodyHuts.Outcome o) => GoodyHuts.Apply(o, gameData, warrior, hut, "Olmec");

		int gold = us.gold;
		Assert.StartsWith("We got ", Apply(GoodyHuts.Outcome.Gold));
		Assert.Contains(us.gold - gold, gameData.rules.GoodyHutGold);

		int techs = us.knownTechs.Count;
		Assert.StartsWith("The Olmec tribe has taught us ", Apply(GoodyHuts.Outcome.Tech));
		Assert.Equal(techs + 1, us.knownTechs.Count);

		Tile far = hut.GetTilesWithinRankDistance(gameData.rules.GoodyHutMapRadius).First(t => !us.tileKnowledge.isTileKnown(t));
		Apply(GoodyHuts.Outcome.Map);
		Assert.True(us.tileKnowledge.isTileKnown(far));

		Assert.Equal("This friendly Olmec village gave us a skilled Warrior.", Apply(GoodyHuts.Outcome.Mercenaries));
		MapUnit mercenary = hut.unitsOnTile.Last();
		Assert.Equal((us, "Warrior"), (mercenary.owner, mercenary.unitType.name));
		Assert.NotEqual(gameData.defaultExperienceLevelKey, mercenary.experienceLevelKey);

		Apply(GoodyHuts.Outcome.Settler);
		Assert.Contains(hut.unitsOnTile, u => u.owner == us && u.unitType.actions.Contains(UnitAction.BuildCity));

		Player barbarians = gameData.players.Find(p => p.isBarbarians);
		Assert.StartsWith("We have disturbed an angry Olmec ", Apply(GoodyHuts.Outcome.Barbarians));
		Assert.Equal(gameData.rules.GoodyHutBarbarians, hut.neighbors.Values.Sum(n => n.unitsOnTile.Count(u => u.owner == barbarians)));

		Assert.Equal("This Olmec village is deserted.", Apply(GoodyHuts.Outcome.Nothing));

		Assert.Equal("An advanced Olmec village has joined us!", Apply(GoodyHuts.Outcome.City));
		Assert.Equal(us, hut.cityAtTile.owner);
	}

	// A unit walking in finds it, the hut is gone, and we're told.
	[Fact]
	public async Task WalkingIntoAHutFindsIt() {
		(Tile hut, MapUnit warrior) = Hut();
		gameData.RemoveUnit(warrior);
		Tile from = hut.neighbors.Values.First(n => n.IsLand() && n.unitsOnTile.Count == 0 && !n.HasCity() && !n.overlayTerrainType.impassable);
		MapUnit scout = gameData.SpawnUnit(us, gameData.unitPrototypes.Find(u => u.name == "Warrior"), from);

		await scout.Move(from.DirectionTo(hut));

		Assert.Equal(hut, scout.location);
		Assert.False(hut.hasGoodyHut);
		MsgGoodyHut told = Assert.Single(EngineStorage.messagesToUI.OfType<MsgGoodyHut>());
		Assert.Equal((us.id, hut), (told.playerId, told.tile));
		Assert.NotEmpty(told.text);
	}
}
