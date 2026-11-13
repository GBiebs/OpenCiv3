using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using C7Engine;
using C7Engine.AI;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;
using static C7GameData.PlayerRelationship;

namespace EngineTests.AI;

public class TerritoryDemandsTest : MapBase, IDisposable {
	private readonly C7GameData.GameData gameData = new();
	private readonly Player us = MakeCiv("player-1");
	private readonly Player them = MakeCiv("player-2");

	// A row of plains running east: the first three tiles are inside our
	// borders, the rest belong to no one.
	private readonly List<Tile> row = new();

	public TerritoryDemandsTest() {
		// Engine state is static; parallelization is disabled repo-wide (XunitSettings.cs)
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.pendingMessages.Clear();
		EngineStorage.messagesToUI.Clear();
		EngineStorage.ResetNetworking();
		gameData.players.Add(us);
		gameData.players.Add(them);
		us.EnsureRelationshipExists(them);

		InitilizeStartTile(MakePlainsTile(), new TileLocation(50, 50));
		row.Add(startTile);
		for (int i = 1; i < 6; ++i) {
			Tile next = AddNeighborsAndUpdateMap(row[i - 1], MakePlainsTile(), TileDirection.EAST);
			AddNeighborsAndUpdateMap(next, row[i - 1], TileDirection.WEST);
			row.Add(next);
		}
		City capital = new(Tile.NONE, us, "Capital", ID.None("city"));
		for (int i = 0; i < 3; ++i) {
			row[i].owningCity = capital;
		}
	}

	public void Dispose() {
		EngineStorage.ResetNetworking();
		EngineStorage.activePlayerID = null;
	}

	private static Player MakeCiv(string id) {
		return new Player() { id = ID.FromString(id), civilization = new Civilization(), government = new Government() };
	}

	private MapUnit MakeUnit(Player owner, Tile location) {
		MapUnit unit = new(ID.None("unit")) {
			owner = owner,
			unitType = new UnitPrototype() { attack = 1, defense = 1 },
			experienceLevel = new ExperienceLevel("regular", "Regular", 3, 0, 0),
			location = location,
		};
		unit.unitType.categories.Add("Land");
		location.unitsOnTile.Add(unit);
		owner.units.Add(unit);
		return unit;
	}

	[Fact]
	public void UnitsInOurTerritoryAreTrespassing() {
		MapUnit intruder = MakeUnit(them, row[1]);
		MakeUnit(them, row[4]);

		Dictionary<Player, List<MapUnit>> trespassers = TerritoryDemands.FindTrespassers(us, gameData);

		Assert.Equal([intruder], trespassers[them]);
	}

	[Fact]
	public void RightOfPassageLetsUnitsIn() {
		MakeUnit(them, row[1]);
		RegisterMultiTurnDeal(us, them, new MultiTurnDeal(DealType.DiplomaticAgreement,
			DealSubType.RightOfPassage, DealDetails.Exchange));

		Assert.Empty(TerritoryDemands.FindTrespassers(us, gameData));
	}

	[Fact]
	public void CivsAtWarFightInsteadOfTalking() {
		MakeUnit(them, row[1]);
		them.DeclareWarOn(us, gameData.turn);

		Assert.Empty(TerritoryDemands.FindTrespassers(us, gameData));
	}

	[Fact]
	public void MilitaryAlliesAreWelcome() {
		MakeUnit(them, row[1]);
		RegisterMultiTurnDeal(us, them, new MultiTurnDeal(DealType.DiplomaticAgreement,
			DealSubType.MilitaryAlliance, DealDetails.Exchange));

		Assert.Empty(TerritoryDemands.FindTrespassers(us, gameData));
	}

	[Fact]
	public async Task AnAiTrespasserWithdrawsToTheNearestFreeTile() {
		MapUnit intruder = MakeUnit(them, row[1]);
		MakeUnit(them, row[2]);

		await TerritoryDemands.MakeDemands(us, gameData);

		Assert.Same(row[3], intruder.location);
		Assert.Contains(intruder, row[3].unitsOnTile);
		Assert.DoesNotContain(intruder, row[1].unitsOnTile);
		Assert.True(AtPeace(us, them));
	}

	// The AI, us, tells the human, them, to leave: answers are what the
	// human sends back.
	private async Task<MsgShowTerritoryDemand> DemandOfHuman(params MessageToEngine[] answers) {
		them.isHuman = true;
		EngineStorage.messagesToUI.Clear();
		Task demands = TerritoryDemands.MakeDemands(us, gameData);

		MsgShowTerritoryDemand shown = null;
		while (EngineStorage.TryDequeueNextMessageToUI(out MessageToUI msg)) {
			shown ??= msg as MsgShowTerritoryDemand;
		}
		foreach (MessageToEngine answer in answers) {
			answer.playerID = them.id;
			answer.send();
			EngineStorage.ProcessNextMessageToEngine();
		}
		await demands;
		return shown;
	}

	// As in Civ3: a warning first, which the human may promise to heed;
	// then, if their units are still there next turn, leave or declare war,
	// and leaving moves them out. (Owner's ruling, 2026-10-31, reversing
	// that of 2026-10-22 that a human was never told to leave.)
	[Fact]
	public async Task AHumanIsWarnedThenToldToLeaveOrDeclareWar() {
		MapUnit intruder = MakeUnit(them, row[1]);
		MakeUnit(them, row[2]);
		City city = new(row[0], us, "Thebes", ID.None("city"));
		us.cities.Add(city);
		row[1].owningCity = city;

		MsgShowTerritoryDemand warning = await DemandOfHuman(new MsgRespondToTerritoryDemand(true), new MsgDiplomacyCompleted());
		Assert.NotNull(warning);
		Assert.False(warning.ultimatum);
		Assert.Same(us, warning.aiPlayer);
		Assert.Same(them, warning.humanPlayer);
		Assert.Equal("Thebes", warning.cityName);
		// They promised to move them themselves.
		Assert.Same(row[1], intruder.location);

		gameData.turn++;
		MsgShowTerritoryDemand ultimatum = await DemandOfHuman(new MsgRespondToTerritoryDemand(true), new MsgDiplomacyCompleted());
		Assert.NotNull(ultimatum);
		Assert.True(ultimatum.ultimatum);
		Assert.Same(row[3], intruder.location);
		Assert.True(AtPeace(us, them));
	}

	// A lone unit gets the ultimatum too once warned, rather than another
	// warning.
	[Fact]
	public async Task AWarnedHumansLoneUnitIsToldToLeaveOrDeclareWar() {
		MapUnit intruder = MakeUnit(them, row[1]);
		MakeUnit(them, row[2]);
		await DemandOfHuman(new MsgRespondToTerritoryDemand(true), new MsgDiplomacyCompleted());
		them.units.Remove(row[2].unitsOnTile[0]);
		row[2].unitsOnTile.Clear();

		gameData.turn++;
		MsgShowTerritoryDemand ultimatum = await DemandOfHuman(new MsgRespondToTerritoryDemand(true), new MsgDiplomacyCompleted());
		Assert.True(ultimatum.ultimatum);
		Assert.Same(row[3], intruder.location);
	}

	// "Remove your forces from our territory or declare WAR!": choosing war,
	// the human declares it, openly.
	[Fact]
	public async Task AHumanWhoChoosesWarDeclaresIt() {
		MapUnit intruder = MakeUnit(them, row[1]);
		MakeUnit(them, row[2]);
		us.tileKnowledge.knownTiles.Add(row[1]);

		await DemandOfHuman(new MsgRespondToTerritoryDemand(false), new MsgDiplomacyCompleted());

		Assert.True(AtWar(us, them));
		Assert.True(us.playerRelationships[them.id].otherStartedCurrentWar);
		Assert.False(us.playerRelationships[them.id].wasSneakAttacked);
		Assert.Same(row[1], intruder.location);
	}

	// The AI itself never goes to war over it, however often the human
	// comes back.
	[Fact]
	public async Task AnAiNeverDeclaresWarOnAHumanOverTrespassing() {
		for (int i = 0; i < 8; ++i) {
			MakeUnit(them, row[1]);
			MakeUnit(them, row[2]);
			await DemandOfHuman(new MsgRespondToTerritoryDemand(true), new MsgDiplomacyCompleted());
			gameData.turn++;
		}
		Assert.True(AtPeace(us, them));
	}

	[Fact]
	public async Task AHumanWhoCantBeReachedIsTakenToAgree() {
		MapUnit intruder = MakeUnit(them, row[1]);
		MakeUnit(them, row[2]);
		them.isHuman = true;
		// As for a LAN guest whose seat is empty.
		EngineStorage.playerReachable = player => player != them.id;

		await TerritoryDemands.MakeDemands(us, gameData);
		gameData.turn++;
		await TerritoryDemands.MakeDemands(us, gameData);

		Assert.DoesNotContain(EngineStorage.messagesToUI, msg => msg is MsgShowTerritoryDemand);
		Assert.Same(row[3], intruder.location);
		Assert.True(AtPeace(us, them));
	}

	[Fact]
	public async Task OnlyTheHumanAskedCanAnswer() {
		Player bystander = MakeCiv("player-3");
		bystander.isHuman = true;
		gameData.players.Add(bystander);
		// The last human to play is still the active player during AI turns.
		EngineStorage.activePlayerID = bystander.id;
		MapUnit intruder = MakeUnit(them, row[1]);
		MakeUnit(them, row[2]);
		them.isHuman = true;

		Task demands = TerritoryDemands.MakeDemands(us, gameData);
		EngineStorage.messagesToUI.Clear();
		new MsgRespondToTerritoryDemand(false) { playerID = bystander.id }.send();
		new MsgDiplomacyCompleted { playerID = bystander.id }.send();
		EngineStorage.ProcessNextMessageToEngine();
		EngineStorage.ProcessNextMessageToEngine();
		await Task.Delay(50);
		Assert.False(demands.IsCompleted);

		new MsgDiplomacyCompleted { playerID = them.id }.send();
		EngineStorage.ProcessNextMessageToEngine();
		await demands;

		Assert.Same(row[1], intruder.location);
		Assert.True(AtPeace(us, them));
	}

	// Only military units are told to leave, and not those of hidden
	// nationality.
	[Fact]
	public void WorkersAndHiddenNationalityUnitsAreNotTrespassers() {
		MapUnit worker = MakeUnit(them, row[1]);
		worker.unitType.attack = 0;
		worker.unitType.defense = 0;
		MapUnit privateer = MakeUnit(them, row[2]);
		privateer.unitType.hasHiddenNationality = true;

		Assert.Empty(TerritoryDemands.FindTrespassers(us, gameData));
	}

	// On peace, each side's units in the other's land are moved out at
	// once, workers too; hidden nationality units stay.
	[Fact]
	public void OnPeaceEachSidesUnitsLeaveTheOthersTerritory() {
		row[5].owningCity = new City(Tile.NONE, them, "Their Capital", ID.None("city"));
		MapUnit soldier = MakeUnit(them, row[1]);
		MapUnit worker = MakeUnit(them, row[2]);
		worker.unitType.attack = 0;
		worker.unitType.defense = 0;
		MapUnit privateer = MakeUnit(them, row[2]);
		privateer.unitType.hasHiddenNationality = true;
		MapUnit ours = MakeUnit(us, row[5]);
		gameData.rules = new Rules() { DefaultDealDuration = 20 };
		them.DeclareWarOn(us, gameData.turn);

		SignPeaceAfterWar(us, them, gameData);

		Assert.Same(row[3], soldier.location);
		Assert.Same(row[3], worker.location);
		Assert.Same(row[2], privateer.location);
		Assert.Same(row[4], ours.location);
		Assert.Contains(ours, row[4].unitsOnTile);
		Assert.DoesNotContain(ours, row[5].unitsOnTile);
	}

	[Fact]
	public async Task AnAiThatKeepsComingBackIsOnlyEverWarned() {
		for (int i = 0; i < 3 * 2; ++i) {
			MapUnit intruder = MakeUnit(them, row[1]);
			MakeUnit(them, row[2]);
			await TerritoryDemands.MakeDemands(us, gameData);
			Assert.Same(row[3], intruder.location);
			gameData.turn++;
		}

		Assert.True(AtPeace(us, them));
	}

	[Fact]
	public async Task UnitsWithNowhereToGoAreNotToldToLeave() {
		City capital = row[0].owningCity;
		foreach (Tile tile in row) {
			tile.owningCity = capital;
		}
		them.isHuman = true;
		MapUnit stuck = MakeUnit(them, row[1]);

		for (int i = 0; i < 3 + 1; ++i) {
			await TerritoryDemands.MakeDemands(us, gameData);
			gameData.turn++;
		}

		Assert.False(EngineStorage.TryDequeueNextMessageToUI(out _));
		Assert.Same(row[1], stuck.location);
		Assert.Equal(0, us.playerRelationships[them.id].recentWithdrawals);
		Assert.True(AtPeace(us, them));
	}

	[Fact]
	public void ALandUnitDoesNotWithdrawAcrossWater() {
		MakeWater(row[3]);
		MapUnit intruder = MakeUnit(them, row[1]);

		Assert.False(intruder.WithdrawToNearestFreeTile());
		Assert.Same(row[1], intruder.location);
	}

	[Fact]
	public void ACutOffLandUnitGoesHomeToItsCity() {
		MakeWater(row[3]);
		row[5].cityAtTile = new City(row[5], them, "Home", ID.None("city"));
		MapUnit intruder = MakeUnit(them, row[1]);

		Assert.True(intruder.WithdrawToNearestFreeTile());
		Assert.Same(row[5], intruder.location);
	}

	[Fact]
	public void AShipDoesNotWithdrawIntoAnotherBodyOfWater() {
		MakeWater(row[1]);
		MakeWater(row[4]);
		MapUnit ship = MakeUnit(them, row[1]);
		ship.unitType.categories = new() { "Sea" };

		Assert.False(ship.WithdrawToNearestFreeTile());
		Assert.Same(row[1], ship.location);

		// Joined up, the water leads somewhere.
		MakeWater(row[2]);
		MakeWater(row[3]);
		Assert.True(ship.WithdrawToNearestFreeTile());
		Assert.Same(row[3], ship.location);
	}

	private static void MakeWater(Tile tile) {
		tile.baseTerrainType = new() { Key = "coast" };
		tile.overlayTerrainType = new() { Key = "coast", movementCost = 1 };
	}

	// The human, us, tells the AI, them, to leave our territory.
	private MsgWithdrawalDemandResult DemandOfAi() {
		us.isHuman = true;
		EngineStorage.activePlayerID = us.id;
		new MsgDemandWithdrawal(them) { playerID = us.id }.send();
		EngineStorage.ProcessNextMessageToEngine();

		MsgWithdrawalDemandResult result = null;
		while (EngineStorage.TryDequeueNextMessageToUI(out MessageToUI msg)) {
			result ??= msg as MsgWithdrawalDemandResult;
		}
		return result;
	}

	[Fact]
	public void AnAiAHumanTellsToLeaveWithdraws() {
		MapUnit intruder = MakeUnit(them, row[1]);
		MakeUnit(us, row[0]);
		MakeUnit(us, row[0]);

		MsgWithdrawalDemandResult result = DemandOfAi();

		Assert.True(result.withdrew);
		Assert.Same(them, result.opponent);
		Assert.Same(us, result.recipient);
		Assert.Same(row[3], intruder.location);
		Assert.True(AtPeace(us, them));
	}

	// Workers don't give cause for a demand, but once their civ is told to
	// leave they go along with its troops.
	[Fact]
	public void AnAiToldToLeaveTakesItsWorkersToo() {
		MakeUnit(them, row[1]);
		MapUnit worker = MakeUnit(them, row[2]);
		worker.unitType.attack = 0;
		worker.unitType.defense = 0;
		MakeUnit(us, row[0]);
		MakeUnit(us, row[0]);

		Assert.True(DemandOfAi().withdrew);
		Assert.Same(row[3], worker.location);
	}

	[Fact]
	public void AStrongerAiRefusesToLeaveAndDeclaresWar() {
		MapUnit intruder = MakeUnit(them, row[1]);
		MakeUnit(them, row[4]);
		us.tileKnowledge.knownTiles.Add(row[1]);

		MsgWithdrawalDemandResult result = DemandOfAi();

		Assert.False(result.withdrew);
		Assert.Same(row[1], intruder.location);
		Assert.True(AtWar(us, them));
		// The AI started the war, but openly, having been warned.
		Assert.True(us.playerRelationships[them.id].otherStartedCurrentWar);
		Assert.False(us.playerRelationships[them.id].wasSneakAttacked);
		// The human didn't provoke it: their people rally against the AI.
		Assert.Equal(Player.WarWearinessWhenWarIsDeclaredOnUs, us.WarWearinessPointsAgainst(them));
	}

	[Fact]
	public void AnAiWithNoUnitsInOurTerritoryHasNothingToAnswer() {
		MakeUnit(them, row[4]);

		Assert.Null(DemandOfAi());
		Assert.True(AtPeace(us, them));
	}

	private static void Move(MapUnit unit, Tile to) {
		unit.location.unitsOnTile.Remove(unit);
		unit.location = to;
		to.unitsOnTile.Add(unit);
	}

	private static MapUnit Unit(string category, int attack, int defense) {
		MapUnit unit = new(ID.None("unit")) { unitType = new UnitPrototype() { attack = attack, defense = defense } };
		unit.unitType.categories.Add(category);
		return unit;
	}

	private static MapUnit Soldier() => Unit("Land", 1, 1);
	private static MapUnit Scout() => Unit("Land", 0, 0);
	private static MapUnit Ship() => Unit("Sea", 1, 1);

	private bool IsThreatening(params MapUnit[] units) {
		return TerritoryDemands.IsThreatening([.. units], us.playerRelationships[them.id], gameData.turn);
	}

	[Fact]
	public void TwoCombatUnitsAreToldToLeaveAtOnce() {
		Assert.True(IsThreatening(Soldier(), Soldier()));
		Assert.True(IsThreatening(Ship(), Ship()));
		Assert.True(IsThreatening(Soldier(), Ship()));
	}

	[Fact]
	public void ALoneShipIsLetBe() {
		for (int i = 0; i < 10; ++i) {
			Assert.False(IsThreatening(Ship()));
			gameData.turn++;
		}
	}

	[Fact]
	public void ScoutsAndWorkersDontCount() {
		for (int i = 0; i < 10; ++i) {
			Assert.False(IsThreatening(Scout(), Scout(), Unit("Land", 0, 0)));
			Assert.False(IsThreatening(Ship(), Scout()));
			gameData.turn++;
		}
	}

	[Fact]
	public void ALoneUnitIsToldToLeaveAfterTwoOrThreeTurns() {
		for (int attempt = 0; attempt < 20; ++attempt) {
			MapUnit soldier = Soldier();
			int turns = 1;
			while (!IsThreatening(soldier)) {
				gameData.turn++;
				turns++;
				Assert.True(turns <= TerritoryDemands.MAX_LONE_TRESPASSER_TURNS);
			}
			Assert.True(turns >= TerritoryDemands.MIN_LONE_TRESPASSER_TURNS);
			gameData.turn++;
		}
	}

	[Fact]
	public async Task ALoneUnitThatLeavesStartsTheCountOver() {
		MapUnit intruder = MakeUnit(them, row[1]);
		await TerritoryDemands.MakeDemands(us, gameData);
		Assert.Same(row[1], intruder.location);
		gameData.turn++;

		// It steps out for a turn, so a fresh count starts when it returns.
		Move(intruder, row[4]);
		await TerritoryDemands.MakeDemands(us, gameData);
		gameData.turn++;
		Move(intruder, row[1]);
		await TerritoryDemands.MakeDemands(us, gameData);
		Assert.Same(row[1], intruder.location);

		for (int i = 0; i < TerritoryDemands.MAX_LONE_TRESPASSER_TURNS; ++i) {
			gameData.turn++;
			await TerritoryDemands.MakeDemands(us, gameData);
		}
		Assert.Same(row[3], intruder.location);
		Assert.True(AtPeace(us, them));
	}
}
