using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using QueryCiv3;
using Xunit;

namespace EngineTests.GameData.Victory;

// Civ3's space race: with the Apollo Program a civ builds its spaceship's
// parts (one of each of ten in Conquests); they're kept at the capital, so
// losing it loses them; launching a complete ship wins the game.
public class SpaceRaceVictoryTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly City capital;
	private readonly City second;
	private readonly List<Building> parts;
	private readonly Building apollo;

	public SpaceRaceVictoryTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		SaveGame save = fixture.saveGame.Clone();
		save.VictoryConditions = new VictoryConditions { AllowSpaceRaceVictory = true };
		gameData = save.ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		(us, them) = (ais[0], ais[1]);
		capital = CityInteractions.BuildCity(us.units[0].location, us, "Capital");
		Tile site = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && t.unitsOnTile.Count == 0
			&& t.DistanceTo(capital.location) >= 4 && t.DistanceTo(capital.location) <= 8 && gameData.cities.All(c => c.location.DistanceTo(t) >= 4));
		second = CityInteractions.BuildCity(site, us, "Second");
		CityInteractions.BuildCity(them.units[0].location, them, "Theirs");
		us.EnsureRelationshipExists(them);
		parts = gameData.Buildings.Where(b => b.spaceshipPart != null).OrderBy(b => b.spaceshipPart).ToList();
		apollo = gameData.Buildings.Single(b => b.HasWonderFlag(SaveBuilding.WonderFlag.BuildSpaceshipParts));
		// Every advance and resource the parts need.
		foreach (Tech tech in gameData.techs) {
			us.knownTechs.Add(tech.id);
		}
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private static HashSet<Resource> Everything(C7GameData.GameData gameData) => gameData.Resources.ToHashSet();

	private void Finish(City city, Building building) {
		city.SetItemBeingProduced(building);
		city.SetStoredShields(city.owner.ShieldCost(building));
		city.HandleCityProduction(gameData);
	}

	[Fact]
	public void ConquestsShipHasOneOfEachOfTenParts() {
		Assert.Equal(10, parts.Count);
		Assert.Equal(Enumerable.Repeat(1, 10), gameData.rules.victory.spaceshipParts);
		Assert.Equal(10, Spaceship.PartsNeeded(gameData.rules));
		Assert.Single(gameData.victories.OfType<SpaceRaceVictory>());
	}

	[Fact]
	public void PartsNeedTheApolloProgram() {
		Assert.False(parts[0].CanProduce(capital, Everything(gameData)));
		capital.AddBuilding(apollo, gameData);
		Assert.True(parts[0].CanProduce(capital, Everything(gameData)));
		Assert.True(parts[0].CanProduce(second, Everything(gameData)));
	}

	[Fact]
	public void AFinishedPartGoesIntoTheShip() {
		capital.AddBuilding(apollo, gameData);
		Finish(second, parts[3]);

		Assert.Equal(1, Spaceship.Built(us, 3));
		Assert.DoesNotContain(second.constructed_buildings, cb => cb.building == parts[3]);
		MsgSpaceshipPart message = Assert.Single(EngineStorage.messagesToUI.OfType<MsgSpaceshipPart>());
		Assert.Equal(us, message.owner);
		Assert.Equal(parts[3], message.part);

		// The ship needs no more of it.
		Assert.False(parts[3].CanProduce(capital, Everything(gameData)));
		Assert.True(parts[4].CanProduce(capital, Everything(gameData)));
	}

	[Fact]
	public void TheShipNeedsAsManyOfAPartAsTheRulesSay() {
		capital.AddBuilding(apollo, gameData);
		us.rules.victory.spaceshipParts[2] = 2;

		// Two cities may build it at once, but not a third.
		second.SetItemBeingProduced(parts[2]);
		Assert.True(parts[2].CanProduce(capital, Everything(gameData)));
		capital.SetItemBeingProduced(parts[2]);
		Tile site = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && t.unitsOnTile.Count == 0
			&& gameData.cities.All(c => c.location.DistanceTo(t) >= 4));
		City third = CityInteractions.BuildCity(site, us, "Third");
		Assert.False(parts[2].CanProduce(third, Everything(gameData)));

		Finish(second, parts[2]);
		Assert.Equal(1, Spaceship.Built(us, 2));
		Assert.False(parts[2].CanProduce(third, Everything(gameData)));
		Finish(capital, parts[2]);
		Assert.Equal(2, Spaceship.Built(us, 2));
		Assert.Equal(2, Spaceship.PartsBuilt(us, us.rules));
		Assert.Equal(11, Spaceship.PartsNeeded(us.rules));
	}

	[Fact]
	public void TheAILaunchesItsCompleteShipAndWins() {
		capital.AddBuilding(apollo, gameData);
		foreach (Building part in parts.Skip(1)) {
			Finish(second, part);
		}
		Assert.False(gameData.gameOver);
		Assert.False(Spaceship.IsComplete(us, us.rules));

		Finish(second, parts[0]);
		Assert.True(us.spaceshipLaunched);
		Assert.True(gameData.gameOver);
		Assert.Equal(us, gameData.winner);
		Assert.IsType<SpaceRaceVictory>(Assert.Single(EngineStorage.messagesToUI.OfType<MsgVictory>()).victory);
	}

	[Fact]
	public void AHumanLaunchesWhenTheyChoose() {
		us.isHuman = true;
		capital.AddBuilding(apollo, gameData);
		foreach (Building part in parts) {
			Finish(second, part);
		}
		Assert.Single(EngineStorage.messagesToUI.OfType<MsgSpaceshipComplete>(), m => m.playerId == us.id);
		Assert.False(us.spaceshipLaunched);
		Assert.False(gameData.gameOver);
		Assert.True(Spaceship.CanLaunch(gameData, us));

		new MsgLaunchSpaceship(us.id).send();
		EngineStorage.ProcessNextMessageToEngine();
		Assert.True(us.spaceshipLaunched);
		Assert.Equal(us, gameData.winner);
		Assert.False(Spaceship.CanLaunch(gameData, us));
	}

	[Fact]
	public void AnIncompleteShipCantBeLaunched() {
		capital.AddBuilding(apollo, gameData);
		Finish(second, parts[0]);
		Spaceship.Launch(gameData, us);
		Assert.False(us.spaceshipLaunched);
		Assert.False(gameData.gameOver);
	}

	[Fact]
	public void WithoutTheSpaceRaceTheShipDoesntWin() {
		gameData.victoryConditions.AllowSpaceRaceVictory = false;
		us.isHuman = true;
		capital.AddBuilding(apollo, gameData);
		foreach (Building part in parts) {
			Finish(second, part);
		}
		Assert.False(Spaceship.CanLaunch(gameData, us));
		Spaceship.Launch(gameData, us);
		Assert.False(gameData.gameOver);
	}

	[Fact]
	public void LosingTheCapitalLosesTheShip() {
		capital.AddBuilding(apollo, gameData);
		Finish(second, parts[0]);
		Finish(second, parts[1]);
		us.DeclareWarOn(them, gameData.turn);

		// Another city doesn't matter.
		CityInteractions.CaptureCity(second, gameData.SpawnUnit(them, gameData.unitPrototypes.Find(u => u.name == "Warrior"), second.location));
		Assert.Equal(2, Spaceship.PartsBuilt(us, us.rules));

		Grow(capital, 3);
		CityInteractions.CaptureCity(capital, gameData.SpawnUnit(them, gameData.unitPrototypes.Find(u => u.name == "Warrior"), capital.location));
		Assert.Equal(0, Spaceship.PartsBuilt(us, us.rules));
		MsgSpaceshipLost lost = Assert.Single(EngineStorage.messagesToUI.OfType<MsgSpaceshipLost>());
		Assert.Equal(us, lost.loser);
		Assert.Equal(them, lost.captor);
	}

	private void Grow(City city, int size) {
		CitizenType laborer = gameData.citizenTypes.Find(c => c.IsDefaultCitizen);
		while (city.residents.Count < size) {
			CityResident resident = new() { city = city, nationality = city.owner.civilization, citizenType = laborer };
			city.AddCitizen(resident);
			C7Engine.AI.CityTileAssignmentAI.AssignNewCitizenToTile(gameData, resident);
		}
	}

	[Fact]
	public void TheShipIsSaved() {
		us.spaceshipParts[4] = 1;
		us.spaceshipParts[7] = 1;
		us.spaceshipLaunched = true;
		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		Player again = reloaded.GetPlayer(us.id);
		Assert.Equal(2, Spaceship.PartsBuilt(again, again.rules));
		Assert.Equal(1, Spaceship.Built(again, 7));
		Assert.True(again.spaceshipLaunched);
	}

	[Fact]
	public void TheVictoryStatusCountsTheParts() {
		// Parts beyond what the ship needs don't count (a game whose rules
		// changed).
		us.spaceshipParts[4] = 3;
		SpaceRaceVictory victory = gameData.victories.OfType<SpaceRaceVictory>().Single();
		VictoryStatus status = victory.Evaluate(us, gameData);
		Assert.Equal(1, status.SpaceshipParts);
		Assert.Equal(10, status.SpaceshipPartsNeeded);
		Assert.False(victory.HasVictory(status));
	}

	[Fact]
	public void TheAIBuildsTheShipOnlyInASpaceRace() {
		capital.AddBuilding(apollo, gameData);
		float withRace = ChooseProducible.ScoreBuilding(second, us, parts[0]);
		gameData.victoryConditions.AllowSpaceRaceVictory = false;
		Assert.Equal(int.MinValue, ChooseProducible.ScoreBuilding(second, us, parts[0]));
		Assert.Equal(int.MinValue, ChooseProducible.ScoreBuilding(second, us, apollo));
		gameData.victoryConditions.AllowSpaceRaceVictory = true;

		// Worth more than the same building that isn't a part.
		parts[0].spaceshipPart = null;
		float asABuilding = ChooseProducible.ScoreBuilding(second, us, parts[0]);
		parts[0].spaceshipPart = 0;
		Assert.Equal(ChooseProducible.VictoryBuildingBoost, withRace - asABuilding);
	}
}

// The parts the ship needs come from Civ3's RULE section.
public class SpaceshipImportTest {
	[SkippableFact]
	public void ThePartCountsComeFromTheBiq() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), "No Civ3 install found.");
		string scenario = System.IO.Path.Combine(Civ3Location.GetCiv3Path(), "Conquests", "Conquests", "5 Mesoamerica.biq");
		SaveGame save = ImportCiv3.ImportBiq(scenario, PathUtils.defaultBicPath,
			_ => PathUtils.defaultPediaIconsPath, _ => PathUtils.defaultCivilopediaTextPath);
		Assert.Equal(BiqData.LoadFile(PathUtils.defaultBicPath).RuleSpaceship[0], save.Rules.victory.spaceshipParts);
	}
}
