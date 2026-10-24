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

// Civ3's golden age (Civilopedia "Golden Age"): once a game, for 20 turns
// (RULE), every tile a civ's cities work that makes a shield makes one more,
// and so for commerce. It starts when the civ's unique unit wins a battle
// against another civ (not barbarians), or when the civ builds a great wonder
// and the great wonders it owns between them have both of its traits. The
// despotism penalty applies after the bonus.
public class GoldenAgeTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly City city;

	public GoldenAgeTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		// A clone, because some tests change unit types, which the game shares with the save.
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		(us, them) = (ais[0], ais[1]);
		city = CityInteractions.BuildCity(us.units[0].location, us, "Home");
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private UnitPrototype Type(string name) => gameData.unitPrototypes.Find(u => u.name == name);
	private Building Building(string name) => gameData.Buildings.Find(b => b.name == name);
	private TerrainType Terrain(string key) => gameData.terrainTypes.Find(t => t.Key == key);

	private static bool IsEmptyLand(Tile t) {
		return Tile.IsTileValid(t) && t.IsLand() && t.unitsOnTile.Count == 0 && !t.HasCity() && !t.hasBarbarianCamp;
	}

	// Two empty land tiles side by side, away from everyone.
	private (Tile west, Tile east) Battlefield() {
		Tile west = gameData.map.tiles.First(t => IsEmptyLand(t)
			&& t.neighbors.TryGetValue(TileDirection.EAST, out Tile east) && IsEmptyLand(east)
			&& gameData.mapUnits.All(u => u.location.DistanceTo(t) > 3) && gameData.cities.All(c => c.location.DistanceTo(t) > 3));
		return (west, west.neighbors[TileDirection.EAST]);
	}

	// An empty land tile next to our city with the given terrain and no
	// resource, river or improvement.
	private Tile Plot(string terrain) {
		Tile tile = city.location.neighbors.Values.First(t => IsEmptyLand(t) && !t.BordersRiver());
		tile.overlayTerrainType = Terrain(terrain);
		tile.baseTerrainType = Terrain(terrain);
		tile.Resource = Resource.NONE;
		tile.isBonusShield = false;
		tile.overlays.Clear();
		return tile;
	}

	private (int food, int shields, int commerce) Yields(Tile tile) {
		return (tile.FoodYield(us).yield, tile.ProductionYield(us).yield, tile.CommerceYield(us).yield);
	}

	[Fact]
	public void TheRulesetHasTheUniqueUnitsAndTheLength() {
		Assert.Equal(20, gameData.rules.GoldenAgeDuration);
		Assert.True(Type("Legionary").startsGoldenAge);
		Assert.True(Type("Impi").startsGoldenAge);
		Assert.True(Type("Panzer").startsGoldenAge);
		Assert.False(Type("Swordsman").startsGoldenAge);
		Assert.Equal(31, gameData.unitPrototypes.Count(u => u.startsGoldenAge));
	}

	[Fact]
	public void TilesMakingAShieldOrCommerceMakeOneMore() {
		us.government = gameData.governments.Find(g => g.name == "Monarchy");
		Tile plains = Plot("plains");
		plains.overlays.Add(gameData.terrainImprovements.Find(i => i.key == "road"));
		Tile grassland = city.location.neighbors.Values.First(t => IsEmptyLand(t) && t != plains && !t.BordersRiver());
		grassland.overlayTerrainType = grassland.baseTerrainType = Terrain("grassland");
		grassland.Resource = Resource.NONE;
		grassland.isBonusShield = false;
		grassland.overlays.Clear();
		(int food, int shields, int commerce) plainsBefore = Yields(plains);
		(int food, int shields, int commerce) grassBefore = Yields(grassland);
		Assert.True(plainsBefore.shields >= 1 && plainsBefore.commerce >= 1);
		Assert.Equal(0, grassBefore.shields);
		Assert.Equal(0, grassBefore.commerce);

		us.goldenAgeTurnsLeft = 5;

		Assert.Equal((plainsBefore.food, plainsBefore.shields + 1, plainsBefore.commerce + 1), Yields(plains));
		Assert.Equal(grassBefore, Yields(grassland));
	}

	[Fact]
	public void TheDespotismPenaltyComesAfter() {
		Assert.Equal("Despotism", us.government.name);
		Tile forest = Plot("forest");
		Assert.Equal(2, Yields(forest).shields);

		us.goldenAgeTurnsLeft = 5;
		// 2 + 1 = 3, and despotism takes a shield from tiles making 3 or more.
		Assert.Equal(2, Yields(forest).shields);

		us.government = gameData.governments.Find(g => g.name == "Monarchy");
		Assert.Equal(3, Yields(forest).shields);
	}

	[Fact]
	public async Task AUniqueUnitsVictoryOverAnotherCivStartsIt() {
		us.DeclareWarOn(them, gameData.turn);
		(Tile west, Tile east) = Battlefield();
		Type("Legionary").attack = 1000;
		MapUnit legionary = gameData.SpawnUnit(us, Type("Legionary"), west);

		Assert.Equal(CombatResult.DefenderKilled, await legionary.Fight(gameData.SpawnUnit(them, Type("Warrior"), east)));

		Assert.True(us.InGoldenAge());
		Assert.True(us.hadGoldenAge);
		Assert.Equal(20, us.goldenAgeTurnsLeft);
		Assert.False(them.InGoldenAge());
	}

	[Fact]
	public async Task DefendingWinsCount() {
		us.DeclareWarOn(them, gameData.turn);
		(Tile west, Tile east) = Battlefield();
		Type("Legionary").defense = 1000;
		gameData.SpawnUnit(us, Type("Legionary"), east);
		MapUnit attacker = gameData.SpawnUnit(them, Type("Warrior"), west);

		Assert.Equal(CombatResult.AttackerKilled, await attacker.Fight(east.unitsOnTile.First(u => u.owner == us)));
		Assert.True(us.InGoldenAge());
	}

	[Fact]
	public async Task BarbariansAndOtherUnitsDont() {
		Player barbarians = gameData.players.First(p => p.isBarbarians);
		(Tile west, Tile east) = Battlefield();
		Type("Legionary").attack = 1000;
		Type("Swordsman").attack = 1000;

		await gameData.SpawnUnit(us, Type("Legionary"), west).Fight(gameData.SpawnUnit(barbarians, Type("Warrior"), east));
		Assert.False(us.hadGoldenAge);

		us.DeclareWarOn(them, gameData.turn);
		foreach (MapUnit unit in west.unitsOnTile.ToList()) {
			gameData.RemoveUnit(unit);
		}
		await gameData.SpawnUnit(us, Type("Swordsman"), west).Fight(gameData.SpawnUnit(them, Type("Warrior"), east));
		Assert.False(us.hadGoldenAge);
	}

	[Fact]
	public async Task ItComesOnceAGame() {
		us.hadGoldenAge = true;
		us.DeclareWarOn(them, gameData.turn);
		(Tile west, Tile east) = Battlefield();
		Type("Legionary").attack = 1000;

		await gameData.SpawnUnit(us, Type("Legionary"), west).Fight(gameData.SpawnUnit(them, Type("Warrior"), east));

		Assert.False(us.InGoldenAge());
		Assert.False(us.StartGoldenAge(gameData));
	}

	private void Build(Building wonder) {
		city.SetItemBeingProduced(wonder);
		city.SetStoredShields(us.ShieldCost(wonder));
		city.HandleCityProduction(gameData);
		Assert.Contains(city.constructed_buildings, cb => cb.building == wonder);
	}

	[Fact]
	public void BuildingGreatWondersWithBothOfOurTraitsStartsIt() {
		us.civilization.traits = [Civilization.Trait.Militaristic, Civilization.Trait.Commercial];

		Build(Building("The Colossus"));
		Assert.False(us.hadGoldenAge);

		Build(Building("Sun Tzu's Art of War"));
		Assert.True(us.InGoldenAge());
	}

	[Fact]
	public void OwningTheWondersIsntEnoughWithoutBuildingOne() {
		us.civilization.traits = [Civilization.Trait.Militaristic, Civilization.Trait.Commercial];
		// As if captured: their traits count, but only building one starts it.
		city.AddBuilding(Building("The Colossus"), gameData);
		city.AddBuilding(Building("Sun Tzu's Art of War"), gameData);
		Assert.False(us.hadGoldenAge);

		Build(Building("The Great Library"));
		Assert.True(us.InGoldenAge());
	}

	[Fact]
	public void ItLastsTheRulesTurnsAndAHumanIsTold() {
		Player human = gameData.players.First(p => p.isHuman);
		Assert.True(human.StartGoldenAge(gameData));
		MsgGoldenAge started = Assert.Single(EngineStorage.messagesToUI.OfType<MsgGoldenAge>());
		Assert.True(started.started);
		Assert.Equal(human.id, started.playerId);

		for (int i = 0; i < 19; i++) {
			human.CountDownGoldenAge();
		}
		Assert.True(human.InGoldenAge());
		human.CountDownGoldenAge();
		Assert.False(human.InGoldenAge());
		MsgGoldenAge ended = EngineStorage.messagesToUI.OfType<MsgGoldenAge>().Last();
		Assert.False(ended.started);
		human.CountDownGoldenAge();
		Assert.Equal(2, EngineStorage.messagesToUI.OfType<MsgGoldenAge>().Count());

		// The AI isn't told.
		us.StartGoldenAge(gameData);
		Assert.Equal(2, EngineStorage.messagesToUI.OfType<MsgGoldenAge>().Count());
	}

	[Fact]
	public async Task EachTurnEndCountsDown() {
		Player human = await CreateGame.createGame(fixture.saveGame.Clone(), (_) => fixture.behaviors);
		C7GameData.GameData game = EngineStorage.gameData;
		TurnHandling.OnBeginTurn();
		TurnHandling.InitTurnData();
		await TurnHandling.AdvanceTurn();
		human.StartGoldenAge(game);

		TurnHandling.OnEndTurn(human);
		human.hasPlayedThisTurn = true;
		await TurnHandling.AdvanceTurn();

		Assert.Equal(game.rules.GoldenAgeDuration - 1, human.goldenAgeTurnsLeft);
	}

	[Fact]
	public void AUniqueUnitStaysWorthBuildingUntilTheGoldenAge() {
		// The Greek Hoplite, which upgrades to the Pikeman, as if we were Greece.
		UnitPrototype unique = Type("Hoplite");
		Assert.True(unique.startsGoldenAge);
		unique.producibleBy.Add(us.civilization);
		foreach (Tech tech in gameData.techs) {
			us.knownTechs.Add(tech.id);
		}
		HashSet<Resource> everything = gameData.Resources.ToHashSet();
		Assert.NotNull(unique.GetProducibleUpgrade(city, everything));

		Assert.True(unique.CanProduce(city, everything));
		us.hadGoldenAge = true;
		Assert.False(unique.CanProduce(city, everything));
	}

	[Fact]
	public void TheGoldenAgeIsSaved() {
		us.StartGoldenAge(gameData);
		us.CountDownGoldenAge();

		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		Player loaded = reloaded.players.Find(p => p.id == us.id);
		Assert.Equal(19, loaded.goldenAgeTurnsLeft);
		Assert.True(loaded.hadGoldenAge);
		Assert.False(reloaded.players.Find(p => p.id == them.id).hadGoldenAge);
	}
}

// A golden age from a real Civ3 save: a turn into the Napoleonic Europe
// scenario, France's ends on turn 20, so 19 turns are left.
public class GoldenAgeImportTest : RemoteSaveLoader {
	[SkippableFact]
	public async Task GoldenAgesComeFromCiv3Saves() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), "No Civ3 install found.");
		string scenarioBiqPath = Path.Combine(Civ3Location.GetCiv3Path(), "Conquests", "Scenarios", "8 MP Napoleonic Europe.biq");
		string scenarioPediaPath = Path.Combine(Civ3Location.GetCiv3Path(), "Conquests", "Conquests", "Napoleonic Europe", "Text", "PediaIcons.txt");
		string uri = "https://www.dropbox.com/scl/fi/l8bm8dhacd85cwyxn3nn7/MultiTurnDeal_Save_F.SAV?rlkey=mr7itejsucesk14h859jwjffu&st=fuo8n4d9&dl=1";

		(SaveGame game, Exception ex, string _) = await LoadGameAndData("MultiTurnDeal_Save_F.SAV", "saves/multi-turn-deals", uri, scenarioBiqPath, scenarioPediaPath);
		Assert.Null(ex);

		SavePlayer france = game.Players[3];
		Assert.Equal("France", france.civilization);
		Assert.True(france.hadGoldenAge);
		Assert.Equal(19, france.goldenAgeTurnsLeft);
		Assert.All(game.Players.Where(p => p != france), p => Assert.False(p.hadGoldenAge));
	}

	[SkippableFact]
	public void TheUnitsThatStartOneComeFromTheBiq() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), "No Civ3 install found.");
		// The stock rules have no map to import, so use a scenario with them.
		string scenario = Path.Combine(Civ3Location.GetCiv3Path(), "Conquests", "Conquests", "5 Mesoamerica.biq");
		SaveGame save = ImportCiv3.ImportBiq(scenario, PathUtils.defaultBicPath,
			_ => PathUtils.defaultPediaIconsPath, _ => PathUtils.defaultCivilopediaTextPath);

		QueryCiv3.BiqData scenarioBiq = QueryCiv3.BiqData.LoadFile(scenario);
		QueryCiv3.BiqData units = scenarioBiq.Prto is null ? QueryCiv3.BiqData.LoadFile(PathUtils.defaultBicPath) : scenarioBiq;
		List<string> expected = units.Prto.Where(p => p.StartsGoldenAge).Select(p => p.Name).Distinct().Order().ToList();
		Assert.NotEmpty(expected);
		Assert.Equal(expected, save.UnitPrototypes.Where(u => u.flags.Contains(SaveUnitPrototype.Flag.StartsGoldenAge)).Select(u => u.name).Distinct().Order());
	}
}
