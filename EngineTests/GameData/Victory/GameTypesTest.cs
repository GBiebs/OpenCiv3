using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7Engine.AI.UnitAI;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using QueryCiv3;
using Xunit;

namespace EngineTests.GameData.Victory;

// Conquests' game types (Civilopedia "Short Game Modes"): victory points,
// regicide and mass regicide, elimination, capture the princess and the
// wonder victory.
public class GameTypesTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private C7GameData.GameData gameData;
	private Player us;
	private Player them;
	private City home;
	private City abroad;

	public GameTypesTest(SaveGameFixture fixture) {
		this.fixture = fixture;
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	// A new game with these conditions, as the game starts one (kings,
	// princesses and victory locations placed), then a capital each for two
	// civs at war.
	private void NewGame(VictoryConditions conditions) {
		SaveGame save = fixture.saveGame.Clone();
		save.VictoryConditions = conditions;
		gameData = save.ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		gameData.onGameCreation();
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		(us, them) = (ais[0], ais[1]);
		home = CityInteractions.BuildCity(us.units.First(u => u.unitType.isSettler).location, us, "Home");
		abroad = CityInteractions.BuildCity(them.units.First(u => u.unitType.isSettler).location, them, "Abroad");
		us.DeclareWarOn(them, gameData.turn);
	}

	private UnitPrototype Type(string name) => gameData.unitPrototypes.Find(u => u.name == name);

	private MapUnit Spawn(Player owner, string type, Tile tile) => gameData.SpawnUnit(owner, Type(type), tile);

	private Tile EmptyLandNear(Tile tile, int distance) {
		return gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && t.unitsOnTile.Count == 0
			&& !t.hasBarbarianCamp && !t.hasGoodyHut && t.DistanceTo(tile) == distance);
	}

	private void Grow(City city, int size) {
		CitizenType laborer = gameData.citizenTypes.Find(c => c.IsDefaultCitizen);
		while (city.residents.Count < size) {
			CityResident resident = new() { city = city, nationality = city.owner.civilization, citizenType = laborer };
			city.AddCitizen(resident);
			C7Engine.AI.CityTileAssignmentAI.AssignNewCitizenToTile(gameData, resident);
		}
	}

	// ---------------------------------------------------------------- victory points

	[Fact]
	public async Task KillsScoreTheUnitsCost() {
		NewGame(new VictoryConditions { VictoryLocations = true });
		Tile next = home.location.neighbors.Values.First(t => t.IsLand() && !t.HasCity());
		MapUnit tank = Spawn(us, "Tank", next);
		tank.unitType.attack = 1000;
		Assert.Equal(CombatResult.DefenderKilled, await tank.Fight(Spawn(them, "Musketman", next.neighbors.Values.First(t => t.IsLand() && !t.HasCity()))));
		// Musketman: 60 shields, times 5, divided by 10.
		Assert.Equal(60 * 5 / 10, us.victoryPoints);

		// Barbarians count half.
		MapUnit barbarian = Spawn(gameData.players.First(p => p.isBarbarians), "Musketman", next.neighbors.Values.First(t => t.IsLand() && !t.HasCity()));
		await tank.Fight(barbarian);
		Assert.Equal(30 + 15, us.victoryPoints);
	}

	[Fact]
	public void AdvancesWondersAndConquestsScore() {
		NewGame(new VictoryConditions { VictoryLocations = true });
		Tech tech = gameData.techs.First(t => !us.knownTechs.Contains(t.id) && t.Cost > 0);
		us.AcquireTechs(gameData, [tech]);
		Assert.Equal(tech.Cost * 5, us.victoryPoints);

		int before = us.victoryPoints;
		Building wonder = gameData.Buildings.First(b => b.IsGreatWonder() && b.requiredTech == null || b.IsGreatWonder());
		foreach (Tech t in gameData.techs) us.knownTechs.Add(t.id);
		home.SetItemBeingProduced(wonder);
		home.SetStoredShields(us.ShieldCost(wonder));
		home.HandleCityProduction(gameData);
		Assert.Equal(before + wonder.shieldCost * 10 / 10, us.victoryPoints);

		before = us.victoryPoints;
		Grow(abroad, 4);
		Tile next = abroad.location.neighbors.Values.First(t => t.IsLand() && !t.HasCity());
		CityInteractions.CaptureCity(abroad, Spawn(us, "Warrior", next));
		Assert.Equal(before + 4 * 100, us.victoryPoints);
	}

	[Fact]
	public void NothingScoresWithoutVictoryPointScoring() {
		NewGame(new VictoryConditions());
		us.AcquireTechs(gameData, [gameData.techs.First(t => !us.knownTechs.Contains(t.id) && t.Cost > 0)]);
		Assert.Equal(0, us.victoryPoints);
		Assert.DoesNotContain(gameData.map.tiles, t => t.victoryLocation);
	}

	[Fact]
	public void HoldingAVictoryLocationScoresEachTurn() {
		NewGame(new VictoryConditions { VictoryLocations = true });
		// The starting locations are victory locations.
		Assert.True(home.location.victoryLocation);
		Assert.True(abroad.location.victoryLocation);
		Assert.Equal(gameData.players.Count(p => !p.isBarbarians), gameData.map.tiles.Count(t => t.victoryLocation));

		foreach (MapUnit u in home.location.unitsOnTile.ToList()) u.RemoveFromPlay();
		Spawn(us, "Worker", home.location);
		VictoryPoints.UpdateForNewTurn(gameData);
		Assert.Equal(0, us.victoryPoints);

		Spawn(us, "Warrior", home.location);
		VictoryPoints.UpdateForNewTurn(gameData);
		Assert.Equal(25, us.victoryPoints);

		gameData.rules.victory.victoryLocationPoints = 40;
		VictoryPoints.UpdateForNewTurn(gameData);
		Assert.Equal(65, us.victoryPoints);
	}

	[Fact]
	public async Task LocationsScoreAtTheEndOfEveryTurn() {
		NewGame(new VictoryConditions { VictoryLocations = true });
		Player human = gameData.players.First(p => p.isHuman);
		Tile start = gameData.map.tiles.First(t => t.victoryLocation && t.unitsOnTile.Any(u => u.owner == human));
		Spawn(human, "Warrior", start);
		ID controller = EngineStorage.uiControllerID;
		EngineStorage.uiControllerID = human.id;
		try {
			human.hasPlayedThisTurn = true;
			Task turn = TurnHandling.AdvanceTurn();
			DateTime giveUp = DateTime.Now.AddSeconds(60);
			while (!turn.IsCompleted && DateTime.Now < giveUp) {
				EngineStorage.ProcessNextMessageToEngine();
				while (EngineStorage.TryDequeueNextMessageToUI(out MessageToUI message)) {
					if (message is MsgShowTradeOffer) new MsgDiplomacyCompleted().send();
				}
				await Task.Delay(1);
			}
			await turn;
		} finally {
			EngineStorage.uiControllerID = controller;
		}
		Assert.True(human.victoryPoints >= 25);
	}

	[Fact]
	public void TheVictoryPointLimitWins() {
		NewGame(new VictoryConditions { VictoryLocations = true });
		us.victoryPoints = gameData.rules.victory.victoryPointLimit - 1;
		TurnHandling.CheckVictory(gameData);
		Assert.False(gameData.gameOver);
		us.victoryPoints++;
		TurnHandling.CheckVictory(gameData);
		Assert.Equal(us, gameData.winner);
		Assert.IsType<VictoryPointsVictory>(EngineStorage.messagesToUI.OfType<MsgVictory>().Single().victory);
	}

	[Fact]
	public void AtTheEndTheMostVictoryPointsWin() {
		NewGame(new VictoryConditions { VictoryLocations = true });
		them.victoryPoints = 100;
		us.victoryPoints = 99;
		gameData.turn = gameData.timeOptions.turnLimit;
		TurnHandling.CheckVictory(gameData);
		Assert.Equal(them, gameData.winner);
	}

	[Fact]
	public void TheLastGreatWonderEndsTheGame() {
		NewGame(new VictoryConditions { AllowWonderVictory = true, VictoryLocations = true });
		List<Building> wonders = gameData.Buildings.Where(b => b.IsGreatWonder()).ToList();
		foreach (Building w in wonders.Skip(1)) gameData.GreatWondersBuilt.Add(w.name);
		them.victoryPoints = 10;
		TurnHandling.CheckVictory(gameData);
		Assert.False(gameData.gameOver);

		gameData.GreatWondersBuilt.Add(wonders[0].name);
		TurnHandling.CheckVictory(gameData);
		Assert.True(gameData.gameOver);
		Assert.Equal(them, gameData.winner);
	}

	// ---------------------------------------------------------------- regicide

	[Fact]
	public void EveryCivStartsWithItsKing() {
		NewGame(new VictoryConditions { Regicide = true });
		foreach (Player p in gameData.players.Where(p => !p.isBarbarians)) {
			MapUnit king = Assert.Single(p.units, u => u.unitType.isKing);
			Assert.Equal(p.civilization.kingUnitType, king.unitType.name);
		}
		Assert.Equal("Caesar", gameData.civilizations.Find(c => c.name == "Rome").kingUnitType);

		NewGame(new VictoryConditions { MassRegicide = true });
		Assert.Equal(7, us.units.Count(u => u.unitType.isKing));

		NewGame(new VictoryConditions());
		Assert.DoesNotContain(us.units, u => u.unitType.isKing);
	}

	[Fact]
	public async Task LosingTheKingLosesEverything() {
		NewGame(new VictoryConditions { Regicide = true });
		Tile second = EmptyLandNear(abroad.location, 4);
		CityInteractions.BuildCity(second, them, "Second");
		MapUnit king = them.units.Single(u => u.unitType.isKing);
		gameData.TeleportUnit(king, EmptyLandNear(abroad.location, 2));
		MapUnit tank = Spawn(us, "Tank", king.location.neighbors.Values.First(t => t.IsLand() && !t.HasCity() && t.unitsOnTile.Count == 0));
		tank.unitType.attack = 1000;

		Assert.Equal(CombatResult.DefenderKilled, await tank.Fight(king));
		Assert.True(them.defeated);
		Assert.Empty(them.cities);
		Assert.Empty(them.units);
		Assert.True(abroad.location.HasRuins());
		Assert.True(second.HasRuins());
		Assert.Contains(EngineStorage.messagesToUI.OfType<MsgCivilizationDestroyed>(), m => m.civilization == them.civilization);
	}

	[Fact]
	public async Task InMassRegicideTheLastKingCounts() {
		NewGame(new VictoryConditions { MassRegicide = true });
		List<MapUnit> kings = them.units.Where(u => u.unitType.isKing).ToList();
		Tile field = EmptyLandNear(abroad.location, 2);
		MapUnit tank = Spawn(us, "Tank", field.neighbors.Values.First(t => t.IsLand() && !t.HasCity() && t.unitsOnTile.Count == 0));
		tank.unitType.attack = 1000;
		foreach (MapUnit king in kings) {
			Assert.False(them.defeated);
			gameData.TeleportUnit(king, field);
			tank.hitPointsRemaining = tank.maxHitPoints;
			Assert.Equal(CombatResult.DefenderKilled, await tank.Fight(king));
		}
		Assert.True(them.defeated);
	}

	[Fact]
	public async Task KingsDontMatterOutsideRegicide() {
		NewGame(new VictoryConditions());
		MapUnit king = Spawn(them, them.civilization.kingUnitType, EmptyLandNear(abroad.location, 2));
		MapUnit tank = Spawn(us, "Tank", king.location.neighbors.Values.First(t => t.IsLand() && !t.HasCity() && t.unitsOnTile.Count == 0));
		tank.unitType.attack = 1000;
		await tank.Fight(king);
		Assert.False(them.defeated);
	}

	[Fact]
	public void TheAIKeepsItsKingInTheCapital() {
		NewGame(new VictoryConditions { Regicide = true });
		PlayerRelationship.SignPeaceAfterWar(us, them, gameData);
		MapUnit king = us.units.Single(u => u.unitType.isKing);
		gameData.TeleportUnit(king, EmptyLandNear(home.location, 3));
		DefenderAI ai = Assert.IsType<DefenderAI>(PlayerAI.GetAIForUnit(king, us));
		Assert.Equal(home.location, ai.data.destination);
	}

	// ---------------------------------------------------------------- elimination

	[Fact]
	public void LosingACityLosesTheGame() {
		NewGame(new VictoryConditions { CityElimination = true });
		City second = CityInteractions.BuildCity(EmptyLandNear(abroad.location, 4), them, "Second");
		Grow(second, 3);
		Tile next = second.location.neighbors.Values.First(t => t.IsLand() && !t.HasCity());
		CityInteractions.CaptureCity(second, Spawn(us, "Warrior", next));

		Assert.True(them.defeated);
		Assert.Equal(us, second.owner);
		Assert.True(abroad.location.HasRuins());
		Assert.Empty(them.units);
	}

	[Fact]
	public void TheLimitSaysHowManyCities() {
		NewGame(new VictoryConditions { CityElimination = true });
		gameData.rules.victory.cityEliminationCount = 2;
		City second = CityInteractions.BuildCity(EmptyLandNear(abroad.location, 4), them, "Second");
		City third = CityInteractions.BuildCity(EmptyLandNear(abroad.location, 8), them, "Third");
		Grow(second, 3);
		Grow(third, 3);
		CityInteractions.CaptureCity(second, Spawn(us, "Warrior", second.location.neighbors.Values.First(t => t.IsLand() && !t.HasCity())));
		Assert.False(them.defeated);
		Assert.Equal(1, them.citiesLostToEnemies);
		CityInteractions.CaptureCity(third, Spawn(us, "Warrior", third.location.neighbors.Values.First(t => t.IsLand() && !t.HasCity())));
		Assert.True(them.defeated);
	}

	[Fact]
	public void NoCitiesFromGoodyHutsInElimination() {
		NewGame(new VictoryConditions { CityElimination = true });
		Tile hut = EmptyLandNear(home.location, 6);
		MapUnit scout = Spawn(us, "Warrior", hut);
		Assert.False(GoodyHuts.IsPossible(GoodyHuts.Outcome.City, gameData, scout, hut));
		gameData.victoryConditions.CityElimination = false;
		Assert.True(GoodyHuts.IsPossible(GoodyHuts.Outcome.City, gameData, scout, hut));
	}

	// ---------------------------------------------------------------- the princess

	[Fact]
	public void EveryCivStartsWithAPrincessWhoStaysPut() {
		NewGame(new VictoryConditions { CaptureTheFlag = true });
		MapUnit princess = Assert.Single(them.units, u => Princess.Is(gameData, u));
		Assert.Equal(abroad.location, princess.location);
		Assert.False(princess.CanBeActive());
		Assert.False(princess.CanEnter(princess.location.neighbors.Values.First(t => t.IsLand()), out _));

		// The player isn't asked for her orders.
		Player human = gameData.players.Find(p => p.isHuman);
		Assert.Contains(human.units, u => Princess.Is(gameData, u));
		foreach (MapUnit u in human.units.Where(u => !Princess.Is(gameData, u))) {
			u.movementPoints.onConsumeAll();
		}
		UnitInteractions.ClearWaitQueue();
		Assert.Equal(MapUnit.NONE, UnitInteractions.getNextSelectedUnit());

		NewGame(new VictoryConditions());
		Assert.DoesNotContain(them.units, u => Princess.Is(gameData, u));
	}

	[Fact]
	public async Task ACapturedPrincessIsCarriedHome() {
		NewGame(new VictoryConditions { CaptureTheFlag = true, VictoryLocations = true });
		gameData.rules.victory.princessGold = 100;
		MapUnit princess = them.units.Single(u => Princess.Is(gameData, u));
		Tile field = EmptyLandNear(home.location, 2);
		gameData.TeleportUnit(princess, field);
		Tile start = field.neighbors.Values.First(t => t.IsLand() && !t.HasCity() && t.unitsOnTile.Count == 0 && t.DistanceTo(home.location) == 2);
		MapUnit horse = Spawn(us, "Horseman", start);

		// Taken by moving onto her, then carried.
		await horse.Move(start.DirectionTo(field));
		Assert.Equal(us, princess.owner);
		Assert.Equal(them.civilization, princess.nationality);
		Assert.True(princess.IsLoadedIn(horse));

		int gold = us.gold;
		horse.movementPoints.reset(horse.unitType.movement);
		Tile step = field.neighbors.Values.First(t => t.DistanceTo(home.location) == 1 && t.IsLand() && !t.HasCity());
		await horse.Move(field.DirectionTo(step));
		Assert.Equal(step, princess.location);
		horse.movementPoints.reset(horse.unitType.movement);
		await horse.Move(step.DirectionTo(home.location));

		// Delivered: points and gold, and she's home again.
		Assert.Equal(1000, us.victoryPoints);
		Assert.Equal(gold + 100, us.gold);
		Assert.Equal(them, princess.owner);
		Assert.Equal(abroad.location, princess.location);
		Assert.False(princess.IsLoaded());
		Assert.Single(EngineStorage.messagesToUI.OfType<MsgPrincessDelivered>(), m => m.captor == us && m.capital == home);
	}

	[Fact]
	public void WithNoCapitalToGoBackTo() {
		NewGame(new VictoryConditions { CaptureTheFlag = true, VictoryLocations = true });
		MapUnit princess = them.units.Single(u => Princess.Is(gameData, u));
		gameData.TransferUnit(princess, us);
		gameData.TeleportUnit(princess, home.location);
		abroad.capital = false;
		MapUnit horse = Spawn(us, "Horseman", home.location);
		princess.loadedOnUnitId = horse.id;
		Princess.OnCarried(gameData, horse);

		Assert.Equal(2000, us.victoryPoints);
		Assert.Equal(us, princess.owner);
		Assert.Equal(home.location, princess.location);
	}

	[Fact]
	public void TheAITakesAPrincessHome() {
		NewGame(new VictoryConditions { CaptureTheFlag = true });
		PlayerRelationship.SignPeaceAfterWar(us, them, gameData);
		MapUnit princess = them.units.Single(u => Princess.Is(gameData, u));
		Tile field = EmptyLandNear(home.location, 3);
		MapUnit horse = Spawn(us, "Horseman", field);
		gameData.TransferUnit(princess, us);
		gameData.TeleportUnit(princess, field);
		princess.loadedOnUnitId = horse.id;
		DefenderAI ai = Assert.IsType<DefenderAI>(PlayerAI.GetAIForUnit(horse, us));
		Assert.Equal(home.location, ai.data.destination);
		Assert.IsType<DefenderAI>(PlayerAI.GetAIForUnit(princess, us));
	}

	// ---------------------------------------------------------------- saves and options

	[Fact]
	public void TheGameTypesAreSaved() {
		NewGame(new VictoryConditions { VictoryLocations = true, Regicide = true, CityElimination = true, CaptureTheFlag = true });
		us.victoryPoints = 1234;
		them.citiesLostToEnemies = 1;
		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		Assert.Equal(1234, reloaded.GetPlayer(us.id).victoryPoints);
		Assert.Equal(1, reloaded.GetPlayer(them.id).citiesLostToEnemies);
		Assert.True(reloaded.map.tileAt(home.location.XCoordinate, home.location.YCoordinate).victoryLocation);
		Assert.Single(reloaded.victories.OfType<VictoryPointsVictory>());
		Assert.True(reloaded.victoryConditions.Regicide);
	}

	[Fact]
	public void TheNewGameScreenOffersThem() {
		List<string> keys = VictoryConditions.Options.Select(o => o.key).ToList();
		foreach (string key in new[] { "Regicide", "MassRegicide", "CityElimination", "VictoryLocations", "CaptureTheFlag", "AllowWonderVictory" }) {
			Assert.Contains(key, keys);
			Assert.False(VictoryConditions.Options.Single(o => o.key == key).onByDefault);
		}
	}
}

// The game types' data from Civ3's files.
public class GameTypesImportTest {
	[SkippableFact]
	public void TheLimitsComeFromTheBiq() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), "No Civ3 install found.");
		string scenario = Path.Combine(Civ3Location.GetCiv3Path(), "Conquests", "Conquests", "4 Middle Ages.biq");
		SaveGame save = ImportCiv3.ImportBiq(scenario, PathUtils.defaultBicPath,
			_ => PathUtils.defaultPediaIconsPath, _ => PathUtils.defaultCivilopediaTextPath);
		QueryCiv3.Biq.GAME game = BiqData.LoadFile(scenario).Game[0];
		Assert.Equal(game.VictoryPointLimit, save.Rules.victory.victoryPointLimit);
		Assert.Equal(30000, save.Rules.victory.victoryPointLimit);
		Assert.Equal(game.CapturingSpecialUnit, save.Rules.victory.princessPoints);
		Assert.Equal(game.CityConquestPopulation, save.Rules.victory.cityConquestPointsMultiple);

		// The flag unit (RULE): the Princess here; Age of Discovery has none.
		Assert.Equal("Princess", save.Rules.FlagUnitType);
		scenario = Path.Combine(Civ3Location.GetCiv3Path(), "Conquests", "Conquests", "6 Age of Discovery.biq");
		save = ImportCiv3.ImportBiq(scenario, PathUtils.defaultBicPath,
			_ => PathUtils.defaultPediaIconsPath, _ => PathUtils.defaultCivilopediaTextPath);
		Assert.Null(save.Rules.FlagUnitType);
	}

	[SkippableFact]
	public void KingsComeFromTheRules() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), "No Civ3 install found.");
		// One of the user's multiplayer regicide games.
		string file = Path.Combine(Civ3Location.GetCiv3Path(), "Conquests", "Saves", "claudia of the English, 1051 AD.SAV");
		Skip.If(!File.Exists(file), "The user's save isn't there.");
		SaveGame save = ImportCiv3.ImportSav(file, PathUtils.defaultBicPath,
			_ => PathUtils.defaultPediaIconsPath, _ => PathUtils.defaultCivilopediaTextPath);
		Assert.True(save.VictoryConditions.Regicide);
		Assert.Contains(save.UnitPrototypes, u => u.name == "Bismarck" && u.flags.Contains(SaveUnitPrototype.Flag.King));
		Assert.Contains(save.UnitPrototypes, u => u.name == "Fighter" && u.flags.Contains(SaveUnitPrototype.Flag.Immobile));
		Assert.Equal("Bismarck", save.Civilizations.Find(c => c.name == "Germany").kingUnitType);
	}

	[SkippableFact]
	public void TheLimitsComeFromTheSave() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), "No Civ3 install found.");
		// One of the user's multiplayer elimination games: two cities lost
		// eliminate a civ.
		string file = Path.Combine(Civ3Location.GetCiv3Path(), "Conquests", "Saves", "claudia of the Koreans, 100 AD.SAV");
		Skip.If(!File.Exists(file), "The user's save isn't there.");
		SaveGame save = ImportCiv3.ImportSav(file, PathUtils.defaultBicPath,
			_ => PathUtils.defaultPediaIconsPath, _ => PathUtils.defaultCivilopediaTextPath);
		Assert.True(save.VictoryConditions.CityElimination);
		Assert.Equal(2, save.Rules.victory.cityEliminationCount);

		// A game on the stock rules keeps its own limits: a kill worth twice
		// the BIQ's.
		string sample = Path.Combine(PathUtils.getDataPath("saves"), "12345.SAV");
		Skip.If(!File.Exists(sample), "The sample save isn't downloaded.");
		save = ImportCiv3.ImportSav(sample, PathUtils.defaultBicPath,
			_ => PathUtils.defaultPediaIconsPath, _ => PathUtils.defaultCivilopediaTextPath);
		Assert.Equal(10, save.Rules.victory.unitKillPointsMultiple);
	}
}
