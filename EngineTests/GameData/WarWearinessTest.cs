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

// Civ3's war weariness, as CivFanatics measured it
// (https://civfanatics.com/civ3/strategy/game-mechanics/how-does-war-weariness-work/):
// every civ counts points against each enemy. Being attacked, losing
// attackers, units, improvements and cities, and keeping units in the
// enemy's land add points; quiet turns and peace take them away. At 31, 61,
// 91 and 121 points a Republic's cities lose a quarter, half, half and all of
// their citizens to unhappiness, a Democracy's half and all, and at 91 the
// Democracy falls. A civ that is declared war on starts 30 points down,
// which makes a quarter of its citizens happy under any government.
public class WarWearinessTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly Player third;
	private readonly City ours;
	private readonly City theirs;

	public WarWearinessTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		// A clone, because some tests change unit types, which the game shares with the save.
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(3).ToArray();
		(us, them, third) = (ais[0], ais[1], ais[2]);
		ours = CityInteractions.BuildCity(us.units[0].location, us, "Home");
		theirs = CityInteractions.BuildCity(them.units[0].location, them, "Away");
		// Only the units each test places.
		foreach (MapUnit unit in us.units.Concat(them.units).ToList()) {
			gameData.RemoveUnit(unit);
		}
		Grow(ours, 12);
	}

	// Leaves only the citizens working tiles (the city is too small to work
	// tiles for 12), so the faces show the whole effect: with no entertainers
	// or buildings, some citizens are content and the rest unhappy.
	private static void OnlyLaborers(City city) {
		foreach (CityResident specialist in city.residents.Where(r => !r.citizenType.IsDefaultCitizen).ToList()) {
			city.RemoveCitizen(specialist);
		}
	}

	private static int Count(City city, CityResident.Mood mood) {
		return city.residents.Count(r => r.mood == mood);
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private void Grow(City city, int size) {
		CitizenType laborer = gameData.citizenTypes.Find(c => c.IsDefaultCitizen);
		while (city.residents.Count < size) {
			CityResident resident = new() { city = city, nationality = city.owner.civilization, citizenType = laborer };
			city.AddCitizen(resident);
			C7Engine.AI.CityTileAssignmentAI.AssignNewCitizenToTile(gameData, resident);
		}
	}

	private Government Govt(string name) => gameData.governments.Find(g => g.name == name);
	private Building Building(string name) => gameData.Buildings.Find(b => b.name == name);
	private UnitPrototype Type(string name) => gameData.unitPrototypes.Find(u => u.name == name);
	private MapUnit Spawn(Player owner, string type, Tile tile) => gameData.SpawnUnit(owner, Type(type), tile);

	private static int Points(Player weary, Player enemy) => weary.playerRelationships[enemy.id].warWearinessPoints;
	private static void SetPoints(Player weary, Player enemy, int points) => weary.playerRelationships[enemy.id].warWearinessPoints = points;

	private int Unhappy(City city) => WarWeariness.CitizensAffected(gameData, city).unhappy;
	private int Happy(City city) => WarWeariness.CitizensAffected(gameData, city).happy;

	private static bool IsEmptyLand(Tile t) {
		return Tile.IsTileValid(t) && t.IsLand() && t.unitsOnTile.Count == 0 && !t.HasCity() && !t.hasBarbarianCamp;
	}

	// An empty land tile in `city`'s borders.
	private static Tile LandOf(City city) {
		Tile tile = city.location.neighbors.Values.First(t => IsEmptyLand(t) && t.OwningPlayer() == city.owner);
		return tile;
	}

	// Two empty land tiles side by side, west and east, in nobody's land and
	// away from everyone's units.
	private (Tile west, Tile east) Battlefield() {
		Tile west = gameData.map.tiles.First(t => IsEmptyLand(t) && t.OwningPlayer() == null
			&& t.neighbors.TryGetValue(TileDirection.EAST, out Tile east) && IsEmptyLand(east) && east.OwningPlayer() == null
			&& gameData.mapUnits.All(u => u.location.DistanceTo(t) > 3) && gameData.cities.All(c => c.location.DistanceTo(t) > 3));
		return (west, west.neighbors[TileDirection.EAST]);
	}

	[Fact]
	public void LevelsComeEveryThirtyPoints() {
		Assert.Equal(-1, WarWeariness.Level(-1));
		Assert.Equal(0, WarWeariness.Level(0));
		Assert.Equal(0, WarWeariness.Level(30));
		Assert.Equal(1, WarWeariness.Level(31));
		Assert.Equal(1, WarWeariness.Level(60));
		Assert.Equal(2, WarWeariness.Level(61));
		Assert.Equal(2, WarWeariness.Level(90));
		Assert.Equal(3, WarWeariness.Level(91));
		Assert.Equal(3, WarWeariness.Level(120));
		Assert.Equal(4, WarWeariness.Level(121));
		Assert.Equal(4, WarWeariness.Level(1000));
	}

	[Fact]
	public void TheRepublicAndFeudalismHaveLowWarWearinessAndDemocracyHigh() {
		List<string> Named(Government.WarWearinessType type) =>
			gameData.governments.Where(g => g.warWeariness == type).Select(g => g.name).Order().ToList();
		Assert.Equal(["Democracy"], Named(Government.WarWearinessType.High));
		Assert.Equal(["Feudalism", "Republic"], Named(Government.WarWearinessType.Low));
	}

	[Theory]
	[InlineData("Republic", 30, 0)]
	[InlineData("Republic", 31, 3)]
	[InlineData("Republic", 61, 6)]
	[InlineData("Republic", 91, 6)]
	[InlineData("Republic", 121, 12)]
	[InlineData("Feudalism", 61, 6)]
	[InlineData("Democracy", 31, 6)]
	[InlineData("Democracy", 61, 12)]
	[InlineData("Monarchy", 500, 0)]
	[InlineData("Despotism", 500, 0)]
	public void AtWarAShareOfEachCityGrowsWearyByGovernmentAndLevel(string government, int points, int unhappy) {
		us.government = Govt(government);
		us.DeclareWarOn(them, gameData.turn);
		SetPoints(us, them, points);

		Assert.Equal(unhappy, Unhappy(ours));
		Assert.Equal(0, Happy(ours));
	}

	[Fact]
	public void TheShareCountsSpecialistsAndRoundsDown() {
		us.government = Govt("Republic");
		us.DeclareWarOn(them, gameData.turn);
		SetPoints(us, them, 31);
		ours.residents[0].citizenType = us.GetKnownSpecialists(gameData).First();
		// A quarter of all 12 citizens, as in the user's Civ3 saves.
		Assert.Equal(3, Unhappy(ours));

		while (ours.residents.Count > 11) {
			ours.RemoveCitizen(ours.residents.Last());
		}
		Assert.Equal(2, Unhappy(ours));

		// The rules' shares and levels.
		gameData.rules.warWeariness.lowUnhappyPercent = [0, 100];
		Assert.Equal(11, Unhappy(ours));
		gameData.rules.warWeariness.pointsPerLevel = 40;
		Assert.Equal(0, Unhappy(ours));
	}

	[Fact]
	public void TheWearyCitizensAreUnhappyFaces() {
		us.government = Govt("Democracy");
		us.DeclareWarOn(them, gameData.turn);
		SetPoints(us, them, 0);
		OnlyLaborers(ours);
		ours.RecalculateCitizenMoods(gameData);
		int unhappyBefore = Count(ours, CityResident.Mood.Unhappy);

		SetPoints(us, them, 31);
		int weary = Unhappy(ours);
		Assert.Equal(ours.residents.Count / 2, weary);
		Assert.True(Count(ours, CityResident.Mood.Content) >= weary);
		ours.RecalculateCitizenMoods(gameData);
		Assert.Equal(unhappyBefore + weary, Count(ours, CityResident.Mood.Unhappy));
	}

	[Fact]
	public void EachEnemyCountsAndTheTotalIsAtMostTheCitysSize() {
		us.government = Govt("Republic");
		us.DeclareWarOn(them, gameData.turn);
		us.DeclareWarOn(third, gameData.turn);
		SetPoints(us, them, 31);
		SetPoints(us, third, 31);
		Assert.Equal(6, Unhappy(ours));

		SetPoints(us, them, 121);
		SetPoints(us, third, 121);
		Assert.Equal(12, Unhappy(ours));
	}

	[Fact]
	public void ThereIsNoWarWearinessInPeace() {
		us.government = Govt("Republic");
		us.DeclareWarOn(them, gameData.turn);
		SetPoints(us, them, 100);
		Assert.Equal(6, Unhappy(ours));

		PlayerRelationship.SignPeaceAfterWar(us, them, gameData);
		Assert.Equal(0, Unhappy(ours));
	}

	[Fact]
	public void APoliceStationTakesAQuarterAwayAndUniversalSuffrageOneMoreInEveryCity() {
		us.government = Govt("Democracy");
		us.DeclareWarOn(them, gameData.turn);
		SetPoints(us, them, 31);
		Assert.Equal(6, Unhappy(ours));

		ours.AddBuilding(Building("Police Station"), gameData);
		// Three quarters of 6, rounded down.
		Assert.Equal(4, Unhappy(ours));

		Tile site = gameData.map.tiles.First(t => IsEmptyLand(t) && t.DistanceTo(ours.location) == 4);
		City other = CityInteractions.BuildCity(site, us, "Suffrage");
		other.AddBuilding(Building("Universal Suffrage"), gameData);
		Assert.Equal(3, Unhappy(ours));
	}

	[Fact]
	public void BeingDeclaredWarOnStartsThirtyPointsDownAndCheersAQuarterOfEachCity() {
		them.DeclareWarOn(us, gameData.turn);

		Assert.Equal(-30, Points(us, them));
		Assert.Equal(0, Points(them, us));
		// Under any government: we're in Despotism.
		Assert.Equal(Government.WarWearinessType.None, us.government.warWeariness);
		Assert.Equal(3, Happy(ours));

		// Not in peace.
		PlayerRelationship.SignPeaceAfterWar(us, them, gameData);
		Assert.Equal(0, Happy(ours));

		// The rules' numbers.
		gameData.rules.warWeariness.declaredWarOn = -50;
		gameData.rules.warWeariness.happyPercent = 50;
		them.DeclareWarOn(us, gameData.turn);
		Assert.Equal(-80, Points(us, them));
		Assert.Equal(6, Happy(ours));
	}

	[Fact]
	public void TheCheeredCitizensAreHappyFaces() {
		OnlyLaborers(ours);
		ours.RecalculateCitizenMoods(gameData);
		int happyBefore = Count(ours, CityResident.Mood.Happy);

		them.DeclareWarOn(us, gameData.turn);
		int cheered = Happy(ours);
		Assert.Equal(ours.residents.Count / 4, cheered);
		Assert.True(Count(ours, CityResident.Mood.Content) >= cheered);
		ours.RecalculateCitizenMoods(gameData);
		Assert.Equal(happyBefore + cheered, Count(ours, CityResident.Mood.Happy));
	}

	[Fact]
	public async Task TheDefenderTiresOfBeingAttackedWinOrLoseAndTheAttackerOfLosing() {
		us.DeclareWarOn(them, gameData.turn);
		SetPoints(them, us, 0);
		(Tile west, Tile east) = Battlefield();

		Type("Archer").attack = 1000;
		MapUnit winner = Spawn(us, "Archer", west);
		Assert.Equal(CombatResult.DefenderKilled, await winner.Fight(Spawn(them, "Spearman", east)));
		Assert.Equal(2, Points(them, us));
		Assert.Equal(0, Points(us, them));
		gameData.RemoveUnit(winner);

		Type("Spearman").defense = 1000;
		MapUnit loser = Spawn(us, "Warrior", west);
		Assert.Equal(CombatResult.AttackerKilled, await loser.Fight(Spawn(them, "Spearman", east)));
		Assert.Equal(4, Points(them, us));
		Assert.Equal(2, Points(us, them));
	}

	[Fact]
	public async Task BombardingAUnitDownToItsLastHitPointCounts() {
		us.DeclareWarOn(them, gameData.turn);
		SetPoints(them, us, 0);
		(Tile west, Tile east) = Battlefield();
		Type("Catapult").bombard = 1000;
		Type("Catapult").rateOfFire = 1;
		MapUnit catapult = Spawn(us, "Catapult", west);
		MapUnit target = Spawn(them, "Spearman", east);
		target.hitPointsRemaining = 3;

		await catapult.Bombard(east);
		Assert.Equal(2, target.hitPointsRemaining);
		Assert.Equal(0, Points(them, us));

		await catapult.Bombard(east);
		Assert.Equal(1, target.hitPointsRemaining);
		Assert.Equal(1, Points(them, us));
	}

	[Fact]
	public async Task PillagingOrBombingImprovementsTiresTheLandsOwnerOnceForEach() {
		us.DeclareWarOn(them, gameData.turn);
		SetPoints(them, us, 0);
		Tile land = LandOf(theirs);
		land.overlays.Add(gameData.terrainImprovements.Find(i => i.key == "road"));
		land.overlays.Add(gameData.terrainImprovements.Find(i => i.key == "mine"));

		Assert.Equal(2, Spawn(us, "Warrior", land).Pillage().Count);
		Assert.Equal(2, Points(them, us));

		land.overlays.Add(gameData.terrainImprovements.Find(i => i.key == "fortress"));
		Type("Catapult").bombard = 1000;
		Tile beside = land.neighbors.Values.First(t => IsEmptyLand(t) && t != land);
		await Spawn(us, "Catapult", beside).Bombard(land);
		Assert.Equal(3, Points(them, us));
	}

	[Fact]
	public async Task LosingUnitsThatCantDefendTiresTheirOwner() {
		us.DeclareWarOn(them, gameData.turn);
		SetPoints(them, us, 0);
		(Tile west, Tile east) = Battlefield();
		Spawn(them, "Worker", east);
		Spawn(them, "Explorer", east);

		Assert.True(await Spawn(us, "Warrior", west).Move(TileDirection.EAST));
		Assert.DoesNotContain(east.unitsOnTile, u => u.owner == them);
		Assert.Equal(2, Points(them, us));
	}

	[Fact]
	public void LosingACityCostsSixteenPointsOrSeventeenForABiggerOne() {
		us.DeclareWarOn(them, gameData.turn);
		SetPoints(them, us, 0);
		// A second city, so they're still in the game after the first.
		Tile site = gameData.map.tiles.First(t => IsEmptyLand(t) && t.DistanceTo(theirs.location) == 4 && t.DistanceTo(ours.location) > 4);
		City town = CityInteractions.BuildCity(site, them, "Village");
		Grow(theirs, 3);
		CityInteractions.CaptureCity(theirs, Spawn(us, "Warrior", theirs.location));
		Assert.Equal(17, Points(them, us));

		Assert.Equal(CityInteractions.CaptureOutcome.Destroyed, CityInteractions.CaptureCity(town, Spawn(us, "Warrior", site)));
		Assert.Equal(33, Points(them, us));
	}

	[Fact]
	public void TheCostOfLosingACityIsARule() {
		us.DeclareWarOn(them, gameData.turn);
		SetPoints(them, us, 0);
		gameData.rules.warWeariness.cityLost = 40;
		gameData.rules.warWeariness.townOfSizeOneLost = 5;
		WarWeariness.OnCityLost(them, us, 2);
		WarWeariness.OnCityLost(them, us, 1);
		Assert.Equal(45, Points(them, us));
	}

	[Fact]
	public void UnitsInEnemyLandAddAPointEachTurn() {
		us.DeclareWarOn(them, gameData.turn);
		SetPoints(us, them, 0);
		Spawn(us, "Warrior", LandOf(theirs));

		WarWeariness.UpdateForNewTurn(gameData);
		Assert.Equal(1, Points(us, them));
		WarWeariness.UpdateForNewTurn(gameData);
		Assert.Equal(2, Points(us, them));
		// Our units in their land don't tire them.
		Assert.Equal(-30, Points(them, us));
	}

	[Fact]
	public void AQuietWarWearsDownToThirtyButNoFurther() {
		us.DeclareWarOn(them, gameData.turn);
		SetPoints(us, them, 32);

		WarWeariness.UpdateForNewTurn(gameData);
		Assert.Equal(31, Points(us, them));
		WarWeariness.UpdateForNewTurn(gameData);
		Assert.Equal(30, Points(us, them));
		WarWeariness.UpdateForNewTurn(gameData);
		Assert.Equal(30, Points(us, them));

		// An enemy in our land keeps it up.
		SetPoints(us, them, 40);
		Spawn(them, "Warrior", LandOf(ours));
		WarWeariness.UpdateForNewTurn(gameData);
		Assert.Equal(40, Points(us, them));
	}

	[Fact]
	public void PeaceWearsItAwayAsInTheArticlesExample() {
		us.DeclareWarOn(them, gameData.turn);
		PlayerRelationship.SignPeaceAfterWar(us, them, gameData);
		SetPoints(us, them, 121);
		List<int> points = [121];
		while (points.Last() > 0) {
			WarWeariness.UpdateForNewTurn(gameData);
			points.Add(Points(us, them));
		}

		Assert.Equal([121, 113, 106, 99, 93, 87, 81, 75, 70, 65, 60, 56, 52, 48, 44, 40, 37, 34, 31, 28, 26, 24, 22, 20,
			19, 18, 17, 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0], points);
		// Being declared war on isn't forgotten in peace.
		Assert.Equal(-30, Points(them, us));
	}

	[Fact]
	public void ADemocracyFallsAtNinetyOnePointsAgainstAnyEnemy() {
		us.government = Govt("Democracy");
		us.DeclareWarOn(them, gameData.turn);
		us.DeclareWarOn(third, gameData.turn);
		// The units keep the points from changing.
		Spawn(us, "Warrior", LandOf(theirs));
		SetPoints(us, them, 89);
		SetPoints(us, third, 60);

		WarWeariness.UpdateForNewTurn(gameData);
		Assert.Equal(90, Points(us, them));
		Assert.Equal("Democracy", us.government.name);

		WarWeariness.UpdateForNewTurn(gameData);
		Assert.Equal(91, Points(us, them));
		Assert.True(us.government.transitionType);
		Assert.True(us.inAnarchyUntilTurn > gameData.turn);
		// An AI isn't shown the popup.
		Assert.Empty(EngineStorage.messagesToUI.OfType<MsgDescendIntoAnarchy>());
	}

	[Fact]
	public void ARepublicNeverFalls() {
		us.government = Govt("Republic");
		us.DeclareWarOn(them, gameData.turn);
		SetPoints(us, them, 500);

		WarWeariness.UpdateForNewTurn(gameData);
		Assert.Equal("Republic", us.government.name);
	}

	[Fact]
	public async Task AHumansDemocracyFallsAtTheNextTurnAndTheyAreTold() {
		Player human = await CreateGame.createGame(fixture.saveGame.Clone(), (_) => fixture.behaviors);
		C7GameData.GameData game = EngineStorage.gameData;
		TurnHandling.OnBeginTurn();
		TurnHandling.InitTurnData();
		await TurnHandling.AdvanceTurn();
		Player enemy = game.players.First(p => !p.isHuman && !p.isBarbarians);
		human.government = game.governments.Find(g => g.name == "Democracy");
		human.DeclareWarOn(enemy, game.turn);
		SetPoints(human, enemy, 95);
		EngineStorage.messagesToUI.Clear();

		TurnHandling.OnEndTurn(human);
		human.hasPlayedThisTurn = true;
		await TurnHandling.AdvanceTurn();

		Assert.True(human.government.transitionType);
		MsgDescendIntoAnarchy told = Assert.Single(EngineStorage.messagesToUI.OfType<MsgDescendIntoAnarchy>());
		Assert.Equal(human.id, told.controllerId);
		Assert.Equal("Democracy", told.government.name);
	}

	[Fact]
	public void WarWearinessIsSaved() {
		us.DeclareWarOn(them, gameData.turn);
		SetPoints(us, them, 77);

		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		Player reloadedUs = reloaded.players.Find(p => p.id == us.id);
		Assert.Equal(77, reloadedUs.playerRelationships[them.id].warWearinessPoints);
		Assert.Equal(-30, reloaded.players.Find(p => p.id == them.id).playerRelationships[us.id].warWearinessPoints);
		Assert.Equal(Government.WarWearinessType.High, reloaded.governments.Find(g => g.name == "Democracy").warWeariness);
	}
}

// War weariness from a real Civ3 save: a turn into the Napoleonic Europe
// scenario, the French Republic (leader 3) has 121, 141, 119 and 119 points
// against its four enemies.
public class WarWearinessImportTest : RemoteSaveLoader {
	[SkippableFact]
	public async Task GovernmentsAndPointsComeFromCiv3Saves() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), "No Civ3 install found.");
		string scenarioBiqPath = Path.Combine(Civ3Location.GetCiv3Path(), "Conquests", "Scenarios", "8 MP Napoleonic Europe.biq");
		string scenarioPediaPath = Path.Combine(Civ3Location.GetCiv3Path(), "Conquests", "Conquests", "Napoleonic Europe", "Text", "PediaIcons.txt");
		string uri = "https://www.dropbox.com/scl/fi/l8bm8dhacd85cwyxn3nn7/MultiTurnDeal_Save_F.SAV?rlkey=mr7itejsucesk14h859jwjffu&st=fuo8n4d9&dl=1";

		(SaveGame game, Exception ex, string _) = await LoadGameAndData("MultiTurnDeal_Save_F.SAV", "saves/multi-turn-deals", uri, scenarioBiqPath, scenarioPediaPath);
		Assert.Null(ex);

		Assert.Equal(Government.WarWearinessType.None, game.Governments.Find(g => g.name == "Monarchy").warWeariness);
		SavePlayer france = game.Players[3];
		Assert.Equal("France", france.civilization);
		Assert.Equal(Government.WarWearinessType.Low, game.Governments.Find(g => g.id == france.governmentId).warWeariness);
		Assert.Equal(121, france.playerRelationships["player-5"].warWearinessPoints);
		Assert.Equal(141, france.playerRelationships["player-9"].warWearinessPoints);
		Assert.Equal(119, france.playerRelationships["player-12"].warWearinessPoints);
		Assert.Equal(119, france.playerRelationships["player-13"].warWearinessPoints);
	}
}
