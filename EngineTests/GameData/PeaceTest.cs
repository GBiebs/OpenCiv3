using System;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7Engine.AI;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// The AI making peace: each war keeps a record of what each side has
// destroyed or taken (in shields); an AI that's losing wants peace and pays
// for it, an even war ends after a few turns, and a winner wants paying
// unless its people are tired of the war or it has gone on long.
public class PeaceTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly City ourCity;
	private readonly City theirCity;

	public PeaceTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		(us, them) = (ais[0], ais[1]);
		ourCity = CityInteractions.BuildCity(us.units[0].location, us, "Home");
		theirCity = CityInteractions.BuildCity(them.units[0].location, them, "Abroad");
		// Equal armies (none), and a war from turn 10 they'll talk about.
		foreach (MapUnit unit in us.units.Concat(them.units).ToList()) {
			gameData.RemoveUnit(unit);
		}
		gameData.turn = 10;
		us.DeclareWarOn(them, gameData.turn);
		Talk();
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	// Neither refuses contact.
	private void Talk() {
		us.playerRelationships[them.id].refuseContactUntilTurn = -1;
		them.playerRelationships[us.id].refuseContactUntilTurn = -1;
	}

	private PlayerRelationship Ours => us.playerRelationships[them.id];
	private PlayerRelationship Theirs => them.playerRelationships[us.id];

	private UnitPrototype Unit(string name) => gameData.unitPrototypes.Find(u => u.name == name);

	[Fact]
	public void AWarStartsItsRecord() {
		Assert.Equal(10, Ours.warStartTurn);
		Assert.Equal(10, Theirs.warStartTurn);
		Assert.Equal(0, Ours.warGains);
		Assert.Equal(0, Theirs.warLosses);
	}

	[Fact]
	public void KilledAndCapturedUnitsCountTheirCost() {
		MapUnit warrior = gameData.SpawnUnit(them, Unit("Warrior"), theirCity.location);
		WarRecord.OnUnitLost(warrior, us);
		Assert.Equal(warrior.unitType.shieldCost, Ours.warGains);
		Assert.Equal(warrior.unitType.shieldCost, Theirs.warLosses);
		Assert.Equal(0, Ours.warLosses);

		// Barbarians keep no record.
		Player barbarians = gameData.players.First(p => p.isBarbarians);
		MapUnit barbarian = gameData.SpawnUnit(barbarians, Unit("Warrior"), theirCity.location);
		WarRecord.OnUnitLost(barbarian, us);
		Assert.Equal(warrior.unitType.shieldCost, Ours.warGains);
	}

	[Fact]
	public void ABattleWonCountsTheLoser() {
		Tile next = ourCity.location.neighbors.Values.First(t => t.IsLand() && !t.HasCity() && t.unitsOnTile.Count == 0);
		MapUnit tank = gameData.SpawnUnit(us, Unit("Modern Armor"), ourCity.location);
		MapUnit warrior = gameData.SpawnUnit(them, Unit("Warrior"), next);
		CombatResult result = tank.Fight(warrior).Result;
		Assert.Equal(CombatResult.DefenderKilled, result);
		Assert.Equal(warrior.unitType.shieldCost, Ours.warGains);
		Assert.Equal(warrior.unitType.shieldCost, Theirs.warLosses);
	}

	[Fact]
	public void ATakenCityCountsItsCitizensAndBuildings() {
		// Not their last city (that would end them).
		CityInteractions.BuildCity(gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && t.unitsOnTile.Count == 0
			&& gameData.cities.All(c => c.location.DistanceTo(t) > 3)), them, "Refuge");
		int value = WarRecord.CityValue(gameData, theirCity);
		Assert.Equal(theirCity.residents.Count * gameData.rules.CitizenValueInShields
			+ theirCity.constructed_buildings.Sum(cb => cb.building.shieldCost), value);
		MapUnit tank = gameData.SpawnUnit(us, Unit("Modern Armor"), theirCity.location.neighbors.Values.First(t => t.IsLand() && !t.HasCity()));
		foreach (MapUnit defender in theirCity.location.unitsOnTile.ToList()) {
			gameData.RemoveUnit(defender);
		}
		CityInteractions.CaptureCity(theirCity, tank);
		Assert.Equal(value, Ours.warGains);
		Assert.Equal(value, Theirs.warLosses);
	}

	[Fact]
	public void AnArmyCountsItsMembers() {
		MapUnit army = gameData.SpawnUnit(them, gameData.unitPrototypes.Find(u => u.name == gameData.rules.ArmyUnitType), theirCity.location);
		MapUnit member = gameData.SpawnUnit(them, Unit("Swordsman"), theirCity.location);
		member.BoardTransport(army);
		Assert.Equal(army.unitType.shieldCost + member.unitType.shieldCost, WarRecord.UnitValue(army));
	}

	[Fact]
	public void TheBattlesDecideWhoIsWinningThenTheArmies() {
		Assert.Equal(PeaceAI.Standing.Even, PeaceAI.StandingIn(us, them));
		Ours.warGains = 100;
		Assert.Equal(PeaceAI.Standing.Winning, PeaceAI.StandingIn(us, them));
		Ours.warLosses = 85;
		// A lead under a fifth of all lost is no lead.
		Assert.Equal(PeaceAI.Standing.Even, PeaceAI.StandingIn(us, them));
		Ours.warLosses = 200;
		Assert.Equal(PeaceAI.Standing.Losing, PeaceAI.StandingIn(us, them));

		// No battles yet: twice the army wins.
		Ours.warGains = 0;
		Ours.warLosses = 0;
		for (int i = 0; i < 3; i++) {
			gameData.SpawnUnit(us, Unit("Swordsman"), ourCity.location);
		}
		Assert.Equal(PeaceAI.Standing.Winning, PeaceAI.StandingIn(us, them));
		Assert.Equal(PeaceAI.Standing.Losing, PeaceAI.StandingIn(them, us));
		// Ahead in the battles, but with less than half their army: even.
		Theirs.warGains = 100;
		Assert.Equal(PeaceAI.Standing.Even, PeaceAI.StandingIn(them, us));
	}

	[Fact]
	public void WhatPeaceIsWorth() {
		// Even: not before the war has lasted a while.
		Assert.Null(PeaceAI.PeaceValue(gameData, us, them));
		gameData.turn = 10 + PeaceAI.MinimumWarTurns;
		Assert.Equal(0, PeaceAI.PeaceValue(gameData, us, them));

		// Winning: pay us what we've gained, at least the minimum.
		Ours.warGains = 120;
		Assert.Equal(-120, PeaceAI.PeaceValue(gameData, us, them));
		Ours.warGains = 30;
		Assert.Equal(-PeaceAI.MinimumTribute, PeaceAI.PeaceValue(gameData, us, them));

		// Losing: we pay up to half our gold, no more than we've lost.
		Ours.warGains = 0;
		Ours.warLosses = 90;
		us.gold = 100;
		Assert.Equal(50, PeaceAI.PeaceValue(gameData, us, them));
		us.gold = 1000;
		Assert.Equal(90, PeaceAI.PeaceValue(gameData, us, them));
	}

	[Fact]
	public void AWinnerSettlesWhenItsPeopleTireOrTheWarDragsOn() {
		Ours.warGains = 120;
		Assert.Equal(-120, PeaceAI.PeaceValue(gameData, us, them));
		Ours.warWearinessPoints = PeaceAI.WearyLevel * WarWeariness.RulesFor(us).pointsPerLevel + 1;
		Assert.Equal(0, PeaceAI.PeaceValue(gameData, us, them));
		Ours.warWearinessPoints = 0;
		gameData.turn = 10 + PeaceAI.LongWarTurns;
		Assert.Equal(0, PeaceAI.PeaceValue(gameData, us, them));
	}

	[Fact]
	public void TiredPeopleWantPeaceEvenEarly() {
		Assert.Null(PeaceAI.PeaceValue(gameData, us, them));
		Ours.warWearinessPoints = PeaceAI.WearyLevel * WarWeariness.RulesFor(us).pointsPerLevel + 1;
		Assert.Equal(0, PeaceAI.PeaceValue(gameData, us, them));
	}

	[Fact]
	public void NoPeaceInALockedWarOrWithoutAWar() {
		gameData.turn = 10 + PeaceAI.MinimumWarTurns;
		PlayerRelationship.SignPeaceAfterWar(us, them, gameData);
		Assert.Null(PeaceAI.PeaceValue(gameData, us, them));
	}

	[Fact]
	public void AnAllyMakesNoSeparatePeaceUnlessLosing() {
		Player ally = gameData.players.First(p => !p.isHuman && !p.isBarbarians && p != us && p != them);
		ally.EnsureRelationshipExists(us);
		PlayerRelationship.RegisterMultiTurnDeal(us, ally, new MultiTurnDeal(DealType.Alliance, DealSubType.MilitaryAlliance,
			DealDetails.Exchange, 0, null, 20, gameData.turn, them.id));
		gameData.turn = 10 + PeaceAI.MinimumWarTurns;
		Assert.Null(PeaceAI.PeaceValue(gameData, us, them));
		Ours.warWearinessPoints = PeaceAI.WearyLevel * WarWeariness.RulesFor(us).pointsPerLevel + 1;
		Assert.Null(PeaceAI.PeaceValue(gameData, us, them));
		Ours.warLosses = 100;
		us.gold = 1000;
		Assert.Equal(100, PeaceAI.PeaceValue(gameData, us, them));

		// Not for ever.
		Ours.warLosses = 0;
		Ours.warWearinessPoints = 0;
		gameData.turn = 10 + PeaceAI.LongWarTurns;
		Assert.Equal(0, PeaceAI.PeaceValue(gameData, us, them));
	}

	[Fact]
	public void TwoAIsInAnEvenWarMakePeace() {
		gameData.turn = 10 + PeaceAI.MinimumWarTurns - 1;
		PeaceAI.OfferPeace(us, gameData).Wait();
		Assert.True(Ours.AtWar());

		gameData.turn += 1;
		us.turnsUntilPriorityReevaluation = 7;
		them.turnsUntilPriorityReevaluation = 7;
		PeaceAI.OfferPeace(us, gameData).Wait();
		Assert.False(Ours.AtWar());
		Assert.Null(Ours.warStartTurn);
		// Both rethink their plans now the war is over.
		Assert.Equal(0, us.turnsUntilPriorityReevaluation);
		Assert.Equal(0, them.turnsUntilPriorityReevaluation);
		Assert.Contains(EngineStorage.messagesToUI, m => m is MsgPeaceTreatySigned);
	}

	[Fact]
	public void ALoserBuysPeaceFromAWinner() {
		gameData.turn = 10 + PeaceAI.MinimumWarTurns;
		Ours.warLosses = 80;
		Theirs.warGains = 80;
		us.gold = 100;
		them.gold = 0;
		// Half our gold (50) is less than they want (80): no peace.
		PeaceAI.OfferPeace(us, gameData).Wait();
		Assert.True(Ours.AtWar());

		us.gold = 300;
		gameData.turn += PeaceAI.OfferEveryTurns;
		PeaceAI.OfferPeace(us, gameData).Wait();
		Assert.False(Ours.AtWar());
		Assert.Equal(220, us.gold);
		Assert.Equal(80, them.gold);
	}

	[Fact]
	public void TheAIAsksForPeaceOnlyEverySoOftenAndWhenTheyTalk() {
		gameData.turn = 10 + PeaceAI.MinimumWarTurns;
		them.playerRelationships[us.id].refuseContactUntilTurn = gameData.turn + 3;
		PeaceAI.OfferPeace(us, gameData).Wait();
		Assert.True(Ours.AtWar());
		Assert.Null(Ours.lastPeaceOfferTurn);

		Talk();
		// They want too much: we asked and were refused, and won't ask again
		// for a while.
		Theirs.warGains = 500;
		Ours.warLosses = 500;
		us.gold = 0;
		PeaceAI.OfferPeace(us, gameData).Wait();
		Assert.True(Ours.AtWar());
		Assert.Equal(gameData.turn, Ours.lastPeaceOfferTurn);
		Theirs.warGains = 0;
		Ours.warLosses = 0;
		gameData.turn += PeaceAI.OfferEveryTurns - 1;
		PeaceAI.OfferPeace(us, gameData).Wait();
		Assert.True(Ours.AtWar());
		gameData.turn += 1;
		PeaceAI.OfferPeace(us, gameData).Wait();
		Assert.False(Ours.AtWar());
	}

	[Fact]
	public async Task AHumanSeesTheOfferAndItsTerms() {
		them.isHuman = true;
		gameData.turn = 10 + PeaceAI.MinimumWarTurns;
		Ours.warGains = 100;
		them.gold = 500;
		Task offer = PeaceAI.OfferPeace(us, gameData);
		MsgShowTradeOffer shown = Assert.IsType<MsgShowTradeOffer>(EngineStorage.messagesToUI.Single(m => m is MsgShowTradeOffer));
		Assert.True(shown.aiGive.partOfPeaceTreaty);
		Assert.True(shown.aiWant.partOfPeaceTreaty);
		Assert.Equal(100, shown.aiWant.gold);
		Assert.Null(shown.aiGive.gold);

		new MsgDiplomacyCompleted().send();
		EngineStorage.ProcessNextMessageToEngine();
		await offer;
		Assert.True(Ours.AtWar());
	}

	[Fact]
	public void AHumansOfferOfPeaceIsWeighedByTheWar() {
		Player human = them;
		human.isHuman = true;
		human.gold = 500;
		// Too early.
		Assert.False(us.WouldAcceptDealFrom(gameData, human, new TradeOffer { partOfPeaceTreaty = true, gold = 400 }, new TradeOffer { partOfPeaceTreaty = true }));

		gameData.turn = 10 + PeaceAI.MinimumWarTurns;
		Assert.True(us.WouldAcceptDealFrom(gameData, human, new TradeOffer { partOfPeaceTreaty = true }, new TradeOffer { partOfPeaceTreaty = true }));

		// We're winning: pay us.
		Ours.warGains = 100;
		Assert.False(us.WouldAcceptDealFrom(gameData, human, new TradeOffer { partOfPeaceTreaty = true, gold = 99 }, new TradeOffer { partOfPeaceTreaty = true }));
		Assert.True(us.WouldAcceptDealFrom(gameData, human, new TradeOffer { partOfPeaceTreaty = true, gold = 100 }, new TradeOffer { partOfPeaceTreaty = true }));
	}

	[Fact]
	public void OnPeaceUnitsInTheOthersLandGoToTheNearestOfTheirOwn() {
		gameData.UpdateTileOwners();
		Tile theirField = theirCity.location.neighbors.Values.First(t => t.IsLand() && t.OwningPlayer() == them && !t.HasCity());
		Tile ourField = ourCity.location.neighbors.Values.First(t => t.IsLand() && t.OwningPlayer() == us && !t.HasCity());
		MapUnit ours = gameData.SpawnUnit(us, Unit("Warrior"), theirField);
		MapUnit theirs = gameData.SpawnUnit(them, Unit("Warrior"), ourField);
		Tile neutral = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && t.OwningPlayer() == null && t.unitsOnTile.Count == 0);
		MapUnit wanderer = gameData.SpawnUnit(us, Unit("Warrior"), neutral);
		// A second town of ours nearer their land than the first.
		Tile site = gameData.map.tiles.Where(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && t.unitsOnTile.Count == 0
				&& gameData.cities.All(c => c.location.DistanceTo(t) > 3))
			.MinBy(t => t.DistanceTo(theirField));
		CityInteractions.BuildCity(site, us, "Near");
		gameData.UpdateTileOwners();
		Assert.Equal(them, theirField.OwningPlayer());

		gameData.turn = 10 + PeaceAI.MinimumWarTurns;
		PlayerRelationship.SignPeaceAfterWar(us, them, gameData);
		Assert.Equal(us, ours.location.OwningPlayer());
		Assert.True(ours.location.IsLand());
		int nearest = gameData.map.tiles.Where(t => Tile.IsTileValid(t) && t.IsLand() && t.OwningPlayer() == us)
			.Min(t => t.DistanceTo(theirField));
		Assert.Equal(nearest, ours.location.DistanceTo(theirField));
		Assert.Contains(ours, ours.location.unitsOnTile);
		Assert.DoesNotContain(ours, theirField.unitsOnTile);
		Assert.Equal(them, theirs.location.OwningPlayer());
		// Units on no one's land stay.
		Assert.Equal(neutral, wanderer.location);
	}

	[Fact]
	public void OnPeaceShipsGoToOurWatersAndLandUnitsToOurLand() {
		// Two ports whose waters touch: a water tile next to theirs only
		// (theirs) beside a water tile next to ours only (ours).
		bool Coastal(Tile t) => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && t.unitsOnTile.Count == 0
			&& gameData.cities.All(c => c.location.DistanceTo(t) > 4);
		(Tile theirSite, Tile ourSite, Tile theirWaters) = gameData.map.tiles.Where(Coastal)
			.SelectMany(t => t.neighbors.Values.Where(w => w.IsWater())
				.SelectMany(w1 => w1.neighbors.Values.Where(w2 => w2.IsWater() && w2.DistanceTo(t) == 2)
					.SelectMany(w2 => w2.neighbors.Values.Where(o => Coastal(o) && o != t && o.DistanceTo(t) >= 3 && w1.DistanceTo(o) >= 2)
						.Select(o => (t, o, w1)))))
			.First();
		City theirPort = CityInteractions.BuildCity(theirSite, them, "Their port");
		CityInteractions.BuildCity(ourSite, us, "Our port");
		gameData.UpdateTileOwners();
		Assert.Equal(them, theirWaters.OwningPlayer());
		Tile theirShore = theirPort.location.neighbors.Values.FirstOrDefault(t => t.IsLand() && t.OwningPlayer() == them
			&& t.neighbors.Values.Any(n => n.IsWater() && n.OwningPlayer() == us));
		Assert.NotNull(theirWaters);
		MapUnit galley = gameData.SpawnUnit(us, Unit("Galley"), theirWaters);
		MapUnit warrior = theirShore == null ? null : gameData.SpawnUnit(us, Unit("Warrior"), theirShore);

		gameData.turn = 10 + PeaceAI.MinimumWarTurns;
		PlayerRelationship.SignPeaceAfterWar(us, them, gameData);
		// Our waters are next to theirs: the galley is on water, one step away.
		Assert.True(galley.location.IsWater());
		Assert.Equal(us, galley.location.OwningPlayer());
		Assert.Equal(1, galley.location.DistanceTo(theirWaters));
		if (warrior != null) {
			// Our water is nearer, but a warrior goes to land.
			Assert.True(warrior.location.IsLand());
			Assert.Equal(us, warrior.location.OwningPlayer());
		}
	}

	[Fact]
	public void TheWarRecordIsSaved() {
		Ours.warGains = 70;
		Ours.warLosses = 20;
		Ours.lastPeaceOfferTurn = 12;
		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		PlayerRelationship again = reloaded.GetPlayer(us.id).playerRelationships[them.id];
		Assert.Equal(10, again.warStartTurn);
		Assert.Equal(70, again.warGains);
		Assert.Equal(20, again.warLosses);
		Assert.Equal(12, again.lastPeaceOfferTurn);
	}
}
