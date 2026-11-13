using System;
using System.Linq;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Culture flipping: cities defecting to a civ whose culture outweighs their
// owner's (see CultureFlip).
public class CultureFlipTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly Random originalRng = C7GameData.GameData.rng;

	public CultureFlipTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		gameData.turn = 10;

		Player[] civs = gameData.players.Where(p => !p.isBarbarians && !p.isHuman).Take(2).ToArray();
		us = civs[0];
		them = civs[1];
		// Only our cities and theirs are in play.
		foreach (Player p in gameData.players) {
			foreach (City c in p.cities.ToList()) {
				CityInteractions.DestroyCity(c);
			}
		}
	}

	public void Dispose() {
		C7GameData.GameData.rng = originalRng;
		while (EngineStorage.TryDequeueNextMessageToUI(out _)) { }
	}

	// Every roll comes up 0.
	private class ZeroRandom : Random {
		public override int Next(int maxValue) => 0;
		public override int Next(int minValue, int maxValue) => minValue;
		protected override double Sample() => 0.0;
	}

	// No city's flip cooldown, as when they were all founded long ago.
	private void ClearCooldowns() {
		foreach (City c in gameData.cities) {
			c.flipCooldown = 0;
		}
	}

	private static bool IsEmptyLand(Tile t) {
		return t.IsLand() && t.unitsOnTile.Count == 0 && !t.HasCity() && !t.hasBarbarianCamp && !t.IsImpassable();
	}

	private City BuildCity(Player owner, Func<Tile, bool> where = null) {
		Tile tile = gameData.map.tiles.First(t => IsEmptyLand(t) && t.IsAllowCities()
			&& t.neighbors.Values.All(n => n == Tile.NONE || !n.HasCity())
			&& (where == null || where(t)));
		City city = CityInteractions.BuildCity(tile, owner, owner.GetNextCityName());
		city.ownerChangedTurn = -1;
		ClearCooldowns();
		return city;
	}

	private void AddResidents(City city, Civilization nationality, int count) {
		CitizenType worker = gameData.citizenTypes.First(c => c.IsDefaultCitizen);
		for (int i = 0; i < count; ++i) {
			city.residents.Add(new CityResident { city = city, nationality = nationality, citizenType = worker });
		}
	}

	// A single-player game whose one human is the given civ.
	private void MakeTheOnlyHuman(Player human) {
		foreach (Player p in gameData.players) {
			p.isHuman = false;
		}
		human.isHuman = true;
	}

	// Our capital, their capital, and one of our cities, with foreign
	// citizens of theirs, that they have much more culture than us.
	private (City ourCapital, City theirCapital, City city) Setup() {
		City ourCapital = BuildCity(us);
		City theirCapital = BuildCity(them, t => t.DistanceTo(ourCapital.location) > 6);
		City city = BuildCity(us, t => t.DistanceTo(ourCapital.location) > 3 && t.DistanceTo(theirCapital.location) > 3);
		ourCapital.perPlayerCulture[us] = 10;
		city.perPlayerCulture[us] = 0;
		theirCapital.perPlayerCulture[them] = 200;
		AddResidents(city, them.civilization, 6);
		return (ourCapital, theirCapital, city);
	}

	// The binary's order of operations, in integer math (Civ3 binary,
	// doc/perez-findings/Civ3_Findings.md, culture-flip appendix).
	[Fact]
	public void TheScoreFollowsTheBinarysFormula() {
		// The first two foreign tiles don't count; the base is capped at 10.
		Assert.Equal(0, CultureFlip.BaseScore(0, 2));
		Assert.Equal(3, CultureFlip.BaseScore(0, 5));
		Assert.Equal(6, CultureFlip.BaseScore(3, 5));
		Assert.Equal(10, CultureFlip.BaseScore(9, 8));

		// Perez's example: 6, doubled by local culture, equal totals.
		Assert.Equal(12, CultureFlip.ApplyMultipliers(6, false, true, 100, 100, false));
		// Disorder doubles, celebrating halves (after the culture scaling).
		Assert.Equal(24, CultureFlip.ApplyMultipliers(6, true, true, 100, 100, false));
		Assert.Equal(3, CultureFlip.ApplyMultipliers(6, false, false, 100, 100, true));
		// Over four times the culture is a fixed x4.
		Assert.Equal(40, CultureFlip.ApplyMultipliers(10, false, false, 0, 1000, false));
		Assert.Equal(160, CultureFlip.ApplyMultipliers(10, true, true, 0, 1000000, false));
		// Otherwise (B+1)*S/(A+1), which can shrink S to nothing.
		Assert.Equal(30, CultureFlip.ApplyMultipliers(10, false, false, 99, 299, false));
		Assert.Equal(1, CultureFlip.ApplyMultipliers(10, false, false, 99, 9, false));
		Assert.Equal(0, CultureFlip.ApplyMultipliers(10, false, false, 999, 9, false));
	}

	[Fact]
	public void TheChanceFollowsTheFormula() {
		(_, _, City city) = Setup();
		city.perPlayerCulture[them] = 50;
		city.isInCivilDisorder = true;

		CultureFlip.Chance chance = CultureFlip.ChanceOfFlippingTo(gameData, city, them);
		Assert.Equal(city.residents.Count(r => r.nationality == them.civilization), chance.foreigners);
		Assert.Equal(2, chance.memoryFactor);
		Assert.Equal(2, chance.moodFactor);
		Assert.Equal(0, chance.garrison);
		int a = CultureReport.TotalCulture(us);
		int b = CultureReport.TotalCulture(them);
		int s = Math.Min(10, Math.Max(chance.tiles - 2, 0) + chance.foreigners) * 2 * 2;
		s = (a + 1) * 4 < b + 1 ? s * 4 : (b + 1) * s / (a + 1);
		Assert.Equal(s, chance.score);
		Assert.Equal(CultureFlip.Range(city, us, them), chance.range);
		Assert.Equal((double)s / chance.range, chance.probability, 9);
	}

	// G = military land units times the difficulty's citizens quelled per
	// unit (DIFF+0x78, Difficulty.MilitaryLaw), subtracted from the score
	// whatever the culture ratio (superseding the owner's 2026-10-20 ruling
	// that the ratio scales it).
	[Fact]
	public void TheGarrisonIsSubtractedTimesTheQuellPerUnit() {
		(_, _, City city) = Setup();
		int unguarded = CultureFlip.ChanceOfFlippingTo(gameData, city, them).score;
		UnitPrototype spearman = gameData.unitPrototypes.First(u => u.name == "Spearman");
		for (int i = 0; i < 2; ++i) {
			gameData.SpawnUnit(us, spearman, city.location);
		}
		int quell = CultureFlip.QuellPerUnit(gameData, us);
		Assert.Equal(gameData.DifficultyFor(us)?.MilitaryLaw ?? 1, quell);
		CultureFlip.Chance chance = CultureFlip.ChanceOfFlippingTo(gameData, city, them);
		Assert.Equal(2 * quell, chance.garrison);
		Assert.Equal(Math.Max(0, unguarded - 2 * quell), chance.score);
	}

	// Soren Johnson: each civ that might take the city has its own roll,
	// with its own nationals.
	[Fact]
	public void OnlyTheOtherCivsOwnNationalsCount() {
		(_, _, City city) = Setup();
		Civilization third = gameData.players.First(p => !p.isBarbarians && p != us && p != them).civilization;
		int theirs = CultureFlip.ChanceOfFlippingTo(gameData, city, them).foreigners;
		AddResidents(city, third, 3);
		Assert.Equal(theirs, CultureFlip.ChanceOfFlippingTo(gameData, city, them).foreigners);
	}

	[Fact]
	public void ResistersCountTwiceAndCelebrationsHalveTheChance() {
		(_, _, City city) = Setup();
		int foreigners = CultureFlip.ChanceOfFlippingTo(gameData, city, them).foreigners;
		city.resisters = 2;
		Assert.Equal(foreigners + 2, CultureFlip.ChanceOfFlippingTo(gameData, city, them).foreigners);

		city.celebrating = true;
		Assert.Equal(0.5, CultureFlip.ChanceOfFlippingTo(gameData, city, them).moodFactor);
	}

	// Civ3's tile distance: max(dx,dy) - (((dx+dy)/2 - min(dx,dy)) + 1)/2.
	[Fact]
	public void TileDistanceIsCiv3s() {
		Tile origin = gameData.map.tileAt(20, 20);
		Assert.Equal(0, CultureFlip.TileDistance(origin, origin));
		// Side- and corner-adjacent tiles are 1 away.
		Assert.Equal(1, CultureFlip.TileDistance(origin, gameData.map.tileAt(21, 21)));
		Assert.Equal(1, CultureFlip.TileDistance(origin, gameData.map.tileAt(22, 20)));
		Assert.Equal(1, CultureFlip.TileDistance(origin, gameData.map.tileAt(20, 22)));
		// Two corner steps are 3, two side steps 2.
		Assert.Equal(3, CultureFlip.TileDistance(origin, gameData.map.tileAt(24, 20)));
		Assert.Equal(2, CultureFlip.TileDistance(origin, gameData.map.tileAt(22, 22)));
	}

	// R = clamp(2000 * d(theirs) / d(ours), 500, 8000), or 2000 if either
	// capital is missing.
	[Fact]
	public void TheRangeIsCappedAndDefaultsTo2000() {
		(City ourCapital, City theirCapital, City city) = Setup();
		int ours = CultureFlip.TileDistance(city.location, ourCapital.location);
		int theirs = CultureFlip.TileDistance(city.location, theirCapital.location);
		Assert.Equal(Math.Clamp(2000 * theirs / ours, 500, 8000), CultureFlip.Range(city, us, them));
		Assert.Equal(CultureFlip.MinRange, CultureFlip.Range(ourCapital, them, us));
		Assert.Equal(0, CultureFlip.DistanceToCapital(theirCapital, them));

		theirCapital.capital = false;
		Assert.Equal(-1, CultureFlip.DistanceToCapital(city, them));
		Assert.Equal(CultureFlip.DefaultRange, CultureFlip.Range(city, us, them));
		theirCapital.capital = true;
		ourCapital.capital = false;
		Assert.Equal(CultureFlip.DefaultRange, CultureFlip.Range(city, us, them));
	}

	[Fact]
	public void CapitalsNeverFlip() {
		(City ourCapital, _, _) = Setup();
		AddResidents(ourCapital, them.civilization, 10);
		Assert.True(CultureFlip.ChanceOfFlippingTo(gameData, ourCapital, them).baseScore > 0);
		Assert.Null(CultureFlip.BestChance(gameData, ourCapital));
		C7GameData.GameData.rng = new ZeroRandom();
		CultureFlip.ProcessEndOfRound(gameData);
		Assert.Equal(us, ourCapital.owner);
	}

	[Fact]
	public void AGarrisonCanStopAFlip() {
		(_, _, City city) = Setup();
		Assert.True(CultureFlip.ChanceOfFlippingTo(gameData, city, them).probability > 0);

		UnitPrototype warrior = gameData.unitPrototypes.First(p => p.IsLandUnit() && p.defense > 0 && p.attack > 0);
		for (int i = 0; i < 400; ++i) {
			city.AddUnit(warrior, gameData);
		}
		Assert.Equal(0, CultureFlip.ChanceOfFlippingTo(gameData, city, them).probability);
		Assert.Null(CultureFlip.BestChance(gameData, city));
	}

	// "The number of land-based combat units (e.g., any unit with at least 1
	// point of offensive and defensive capability) in the city in question
	// are subtracted"
	// (https://forums.civfanatics.com/threads/culture-flipping-exposed.14337/).
	[Fact]
	public void OnlyUnitsThatCanAttackAndDefendCountAsAGarrison() {
		(_, _, City city) = Setup();
		UnitPrototype warrior = gameData.unitPrototypes.First(p => p.IsLandUnit() && p.defense > 0 && p.attack > 0);
		UnitPrototype worker = gameData.unitPrototypes.First(p => p.IsLandUnit() && p.defense == 0 && p.attack == 0);
		city.AddUnit(warrior, gameData);
		city.AddUnit(worker, gameData);
		Assert.Equal(1, CultureFlip.Garrison(city));

		// A unit that can defend but not attack doesn't count.
		warrior.attack = 0;
		Assert.Equal(0, CultureFlip.Garrison(city));
		warrior.attack = 1;
		warrior.defense = 0;
		Assert.Equal(0, CultureFlip.Garrison(city));
	}

	// Military land units of any owner on the city tile count.
	[Fact]
	public void OtherCivsUnitsInTheCityCountAsAGarrison() {
		(_, _, City city) = Setup();
		UnitPrototype warrior = gameData.unitPrototypes.First(p => p.IsLandUnit() && p.defense > 0 && p.attack > 0);
		gameData.SpawnUnit(them, warrior, city.location);
		Assert.Equal(1, CultureFlip.Garrison(city));
	}

	// A city "remembers the total amount of culture generated by any civ who
	// has ever occupied it" (Magaha, above).
	[Fact]
	public void TheCityRemembersTheCultureEachOwnerMadeInIt() {
		(_, _, City city) = Setup();
		city.AddBuilding(gameData.Buildings.First(b => b.name == "Temple"));
		int ours = city.GetCulturePerTurn();
		Assert.True(ours > 0);
		city.UpdateCultureAndCheckForExpansion();
		Assert.Equal(ours, city.GetCultureFor(us));
		Assert.Equal(0, city.GetCultureFor(them));

		// Their culture adds to their own total, and ours is kept. (Our
		// temple makes them none, so they build a library.)
		CityInteractions.TransferCity(city, them);
		city.AddBuilding(gameData.Buildings.First(b => b.name == "Library"));
		int theirs = city.GetCulturePerTurn();
		Assert.True(theirs > 0);
		city.UpdateCultureAndCheckForExpansion();
		city.UpdateCultureAndCheckForExpansion();
		Assert.Equal(ours, city.GetCultureFor(us));
		Assert.Equal(2 * theirs, city.GetCultureFor(them));

		// It is saved.
		SaveCity saved = new(city);
		Assert.Equal(ours, saved.perPlayerCulture[us.id.ToString()]);
		Assert.Equal(2 * theirs, saved.perPlayerCulture[them.id.ToString()]);

		// "if the 'attacking' civ has more historical culture in the city
		// than the 'defender', the chance ... [is] doubled".
		city.ownerChangedTurn = -1;
		Assert.Equal(1, CultureFlip.ChanceOfFlippingTo(gameData, city, us).memoryFactor);
		city.perPlayerCulture[us] = 2 * theirs + 1;
		Assert.Equal(2, CultureFlip.ChanceOfFlippingTo(gameData, city, us).memoryFactor);
	}

	// A city closer to the other civ's capital than to its owner's gets a
	// smaller range, so a likelier flip.
	[Fact]
	public void ACityNearTheOtherCapitalIsLikelierToFlip() {
		(City ourCapital, City theirCapital, _) = Setup();
		City nearThem = BuildCity(us, t => t.DistanceTo(theirCapital.location) + 3 < t.DistanceTo(ourCapital.location));
		City nearUs = BuildCity(us, t => t.DistanceTo(ourCapital.location) + 3 < t.DistanceTo(theirCapital.location));
		Assert.True(CultureFlip.Range(nearThem, us, them) < CultureFlip.DefaultRange);
		Assert.True(CultureFlip.Range(nearUs, us, them) > CultureFlip.DefaultRange);
	}

	// The cooldown: founding sets 10, and the city is skipped while it is at
	// least 1, each check counting it down.
	[Fact]
	public void AFoundedCityWaitsTenChecks() {
		(_, _, City city) = Setup();
		CultureFlip.SetFlipCooldown(gameData, city, CultureFlip.Cooldown);
		Assert.Equal(10, city.flipCooldown);
		Assert.Null(CultureFlip.BestChance(gameData, city));
		C7GameData.GameData.rng = new ZeroRandom();
		for (int i = 0; i < 10; ++i) {
			CultureFlip.ProcessEndOfRound(gameData);
			Assert.Equal(us, city.owner);
		}
		Assert.Equal(0, city.flipCooldown);
		CultureFlip.ProcessEndOfRound(gameData);
		Assert.Equal(them, city.owner);
	}

	[Fact]
	public void BuildingACitySetsTheCooldown() {
		Setup();
		City founded = CityInteractions.BuildCity(gameData.map.tiles.First(t => IsEmptyLand(t) && t.IsAllowCities()
			&& t.neighbors.Values.All(n => n == Tile.NONE || !n.HasCity())), us, us.GetNextCityName());
		Assert.Equal(CultureFlip.Cooldown, founded.flipCooldown);
	}

	// A flip or peaceful transfer sets 10; a conquest 1, so a conquered
	// city can't flip on the first check after it was taken.
	[Fact]
	public void APeacefulTransferSetsTenAndAConquestOne() {
		(_, _, City city) = Setup();
		CityInteractions.TransferCity(city, them);
		Assert.Equal(CultureFlip.Cooldown, city.flipCooldown);

		ClearCooldowns();
		CityInteractions.TransferCity(city, us);
		ClearCooldowns();
		CultureFlip.Flip(gameData, city, them);
		Assert.Equal(CultureFlip.Cooldown, city.flipCooldown);

		ClearCooldowns();
		foreach (MapUnit u in city.location.unitsOnTile.ToList()) {
			gameData.RemoveUnit(u);
		}
		CityInteractions.CaptureCity(city, us);
		Assert.Equal(us, city.owner);
		Assert.Equal(CultureFlip.ConquestCooldown, city.flipCooldown);
	}

	// Setting a city's cooldown raises other owners' cities near it to at
	// least as much, but not the owner's own cities, nor far ones.
	[Fact]
	public void TheCooldownShieldsForeignNeighbours() {
		(City ourCapital, City theirCapital, City city) = Setup();
		City near = BuildCity(them, t => t.RankDistanceTo(city.location) == 3);
		City ownNear = BuildCity(us, t => t.RankDistanceTo(city.location) == 3 && t.RankDistanceTo(near.location) > 1);
		City far = BuildCity(them, t => t.RankDistanceTo(city.location) > 6);
		near.flipCooldown = 12;
		CultureFlip.SetFlipCooldown(gameData, city, 5);
		Assert.Equal(5, city.flipCooldown);
		Assert.Equal(12, near.flipCooldown);
		near.flipCooldown = 2;
		CultureFlip.SetFlipCooldown(gameData, city, 5);
		Assert.Equal(5, near.flipCooldown);
		Assert.Equal(0, ownNear.flipCooldown);
		Assert.Equal(0, far.flipCooldown);
		Assert.Equal(76, CultureFlip.CooldownTiles(gameData.map.tileAt(30, 30)).Count());
		Assert.All(CultureFlip.CooldownTiles(gameData.map.tileAt(30, 30)), t => Assert.True(t.RankDistanceTo(gameData.map.tileAt(30, 30)) <= 5));
	}

	[Fact]
	public void TheCooldownIsSaved() {
		(_, _, City city) = Setup();
		city.flipCooldown = 7;
		SaveCity saved = new(city);
		Assert.Equal(7, saved.flipCooldown);
		city.flipCooldown = 0;
		Assert.Null(new SaveCity(city).flipCooldown);
	}

	// Each civ rolls in player order, and the first whose roll succeeds
	// takes the city, however likelier a later one is.
	[Fact]
	public void TheCivsRollInPlayerOrder() {
		(_, _, City city) = Setup();
		Player third = gameData.players.First(p => !p.isBarbarians && p != us && p != them);
		// AIs all, so the first roll takes the city at once.
		third.isHuman = false;
		City thirdCapital = BuildCity(third, t => t.DistanceTo(city.location) > 8);
		thirdCapital.perPlayerCulture[third] = 1000;
		AddResidents(city, third.civilization, 3);
		Player first = gameData.players.IndexOf(third) < gameData.players.IndexOf(them) ? third : them;
		Assert.True(CultureFlip.ChanceOfFlippingTo(gameData, city, first).probability > 0);
		C7GameData.GameData.rng = new ZeroRandom();
		CultureFlip.ProcessEndOfRound(gameData);
		Assert.Equal(first, city.owner);
	}

	[Fact]
	public void ChangingHandsRecordsTheTurn() {
		(_, _, City city) = Setup();
		CityInteractions.TransferCity(city, them);
		Assert.Equal(gameData.turn, city.ownerChangedTurn);
		Assert.Null(CultureFlip.BestChance(gameData, city));
	}

	[Fact]
	public void FlippingKillsTheGarrisonAndGivesTheNewOwnerADefender() {
		(_, _, City city) = Setup();
		int population = city.residents.Count;
		UnitPrototype warrior = gameData.unitPrototypes.First(p => p.IsLandUnit() && p.defense > 0 && p.attack > 0);
		city.AddUnit(warrior, gameData);
		MapUnit garrison = city.location.unitsOnTile.Single();

		CultureFlip.Flip(gameData, city, them);

		Assert.Equal(them, city.owner);
		Assert.Contains(city, them.cities);
		Assert.DoesNotContain(city, us.cities);
		Assert.Equal(population, city.residents.Count);
		Assert.DoesNotContain(garrison, us.units);
		Assert.DoesNotContain(garrison, them.units);
		Assert.All(city.location.unitsOnTile, u => Assert.Equal(them, u.owner));
		Assert.Single(city.location.unitsOnTile);
		Assert.False(city.IsInResistance);
	}

	// Both sides are told of a flip, as one event, so a spectator watching
	// every civ hears of it once.
	[Fact]
	public void BothSidesHearOfAFlipAsOneEvent() {
		(_, _, City city) = Setup();
		EngineStorage.messagesToUI.Clear();
		EngineStorage.newsForComputerPlayers = true;
		try {
			CultureFlip.Flip(gameData, city, them);
		} finally {
			EngineStorage.newsForComputerPlayers = false;
		}

		MsgShowDomesticAdvisorPopup[] told = EngineStorage.messagesToUI.OfType<MsgShowDomesticAdvisorPopup>().ToArray();
		Assert.Equal(2, told.Length);
		Assert.Contains(told, m => m.recipient == us);
		Assert.Contains(told, m => m.recipient == them);
		Assert.NotNull(told[0].newsEvent);
		Assert.Same(told[0].newsEvent, told[1].newsEvent);
	}

	[Fact]
	public void NoFlipsWhenTheyAreTurnedOff() {
		(_, _, City city) = Setup();
		// Certain to flip, but for the rule.
		gameData.rules.AllowCultureFlips = false;
		C7GameData.GameData.rng = new ZeroRandom();
		CultureFlip.ProcessEndOfRound(gameData);
		Assert.Equal(us, city.owner);

		gameData.rules.AllowCultureFlips = true;
		Assert.True(CultureFlip.BestChance(gameData, city).probability > 0);
		CultureFlip.ProcessEndOfRound(gameData);
		Assert.Equal(them, city.owner);
	}

	[Fact]
	public void TheTurnACityChangedHandsIsSaved() {
		(_, _, City city) = Setup();
		city.ownerChangedTurn = 7;
		Assert.Equal(7, new SaveCity(city).ownerChangedTurn);
		city.ownerChangedTurn = -1;
		Assert.Null(new SaveCity(city).ownerChangedTurn);
	}

	// The city's people rise up for the other civ: every roll succeeds.
	private City CityCertainToFlip() {
		(_, _, City city) = Setup();
		Assert.True(CultureFlip.BestChance(gameData, city).probability > 0);
		C7GameData.GameData.rng = new ZeroRandom();
		return city;
	}

	// "We don't want $CITY1. Rebuff the rebels." (script.txt,
	// WE_CONVERT_CITY): the civ the city rose up for can refuse it, and
	// the owner is told "the $CIVADJ2 government refuses to acknowledge
	// their claim! Thus, we have regained control of $CITY1!"
	// (THEY_REFUSED_TO_CONVERT_CITY).
	[Fact]
	public void AHumanIsAskedAndMayRebuffTheRebels() {
		City city = CityCertainToFlip();
		MakeTheOnlyHuman(them);
		CultureFlip.ProcessEndOfRound(gameData);
		Assert.Equal(us, city.owner);
		Assert.True(CultureFlip.OfferStands(gameData, city, them));
		Assert.False(CultureFlip.OfferStands(gameData, city, us));

		EngineStorage.messagesToUI.Clear();
		CultureFlip.AskHuman(gameData, them);
		MsgShowCultureFlipOffer offer = Assert.Single(EngineStorage.messagesToUI.OfType<MsgShowCultureFlipOffer>());
		Assert.Same(city, offer.city);
		Assert.Equal(them, offer.recipient);

		EngineStorage.newsForComputerPlayers = true;
		try {
			Assert.True(CultureFlip.AnswerOffer(gameData, them, city, accept: false));
		} finally {
			EngineStorage.newsForComputerPlayers = false;
		}
		Assert.Equal(us, city.owner);
		Assert.False(CultureFlip.OfferStands(gameData, city, them));
		Assert.Contains(EngineStorage.messagesToUI.OfType<MsgShowMilitaryAdvisorPopup>(),
			m => m.recipient == us && m.message.Contains("refuses to acknowledge their claim"));
		// Only one answer.
		Assert.False(CultureFlip.AnswerOffer(gameData, them, city, accept: true));
		Assert.Equal(us, city.owner);
	}

	// With more than one human player (hotseat or LAN) the city flips to
	// a human at once, with no question (owner's decision, 2026-10-21).
	[Fact]
	public void InMultiplayerAHumanTakesTheCityUnasked() {
		City city = CityCertainToFlip();
		MakeTheOnlyHuman(them);
		us.isHuman = true;
		EngineStorage.messagesToUI.Clear();
		CultureFlip.ProcessEndOfRound(gameData);
		Assert.Equal(them, city.owner);
		Assert.False(CultureFlip.OfferStands(gameData, city, them));

		CultureFlip.AskHuman(gameData, them);
		Assert.Empty(EngineStorage.messagesToUI.OfType<MsgShowCultureFlipOffer>());
	}

	// "Great! Install a new governor.", and the wonders go with the city:
	// "the city contains $WONDER3!" (WE_CONVERT_CITY_WITH_WONDER).
	[Fact]
	public void AHumanWhoAcceptsTakesTheCityAndItsWonders() {
		City city = CityCertainToFlip();
		Building wonder = gameData.Buildings.First(b => b.IsGreatWonder());
		city.AddBuilding(wonder);
		MakeTheOnlyHuman(them);
		CultureFlip.ProcessEndOfRound(gameData);

		EngineStorage.messagesToUI.Clear();
		CultureFlip.AskHuman(gameData, them);
		MsgShowCultureFlipOffer offer = Assert.Single(EngineStorage.messagesToUI.OfType<MsgShowCultureFlipOffer>());
		Assert.Contains(wonder.name, offer.wonders);

		Assert.True(CultureFlip.AnswerOffer(gameData, them, city, accept: true));
		Assert.Equal(them, city.owner);
		Assert.Contains(city.constructed_buildings, cb => cb.building == wonder);
	}

	// An unanswered offer lapses when the turn ends, and is saved until
	// then.
	[Fact]
	public void AnOfferLastsTheTurnAndIsSaved() {
		City city = CityCertainToFlip();
		MakeTheOnlyHuman(them);
		CultureFlip.ProcessEndOfRound(gameData);
		SaveCity saved = new(city);
		Assert.Equal(them.id, saved.cultureFlipOfferedTo);
		Assert.Equal(gameData.turn, saved.cultureFlipOfferTurn);

		++gameData.turn;
		Assert.False(CultureFlip.OfferStands(gameData, city, them));
		Assert.False(CultureFlip.AnswerOffer(gameData, them, city, accept: true));
		Assert.Equal(us, city.owner);
	}

	// An AI may refuse too (THEY_REFUSED_TO_CONVERT_CITY); by our heuristic
	// it refuses a city of its military ally.
	[Fact]
	public void AnAiRefusesItsAllysCity() {
		City city = CityCertainToFlip();
		Player third = gameData.players.First(p => !p.isBarbarians && p != us && p != them);
		them.EnsureRelationshipExists(us);
		them.playerRelationships[us.id].multiTurnDeals.Add(new MultiTurnDeal(DealType.Alliance, DealSubType.MilitaryAlliance,
			DealDetails.Exchange, 0, null, 20, 10, third.id));
		Assert.False(CultureFlip.AiAcceptsFlip(city, them));
		CultureFlip.ProcessEndOfRound(gameData);
		Assert.Equal(us, city.owner);

		them.playerRelationships[us.id].multiTurnDeals.RemoveAll(d => d.dealSubType == DealSubType.MilitaryAlliance);
		CultureFlip.ProcessEndOfRound(gameData);
		Assert.Equal(them, city.owner);
	}
}
