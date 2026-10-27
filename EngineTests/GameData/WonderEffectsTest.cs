using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using QueryCiv3;
using Xunit;

namespace EngineTests.GameData;

// The great wonders' effects, as the Conquests Civilopedia gives them.
public class WonderEffectsTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly City city;

	public WonderEffectsTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		us = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		city = CityInteractions.BuildCity(us.units[0].location, us, "Wonders");
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private Building Building(string name) => gameData.Buildings.Find(b => b.name == name);

	private void Add(string name) => city.AddBuilding(Building(name), gameData);

	private void MakeObsolete(string name) => us.knownTechs.Add(Building(name).renderedObsoleteBy.id);

	private MapUnit Spawn(string type) => gameData.SpawnUnit(us, gameData.unitPrototypes.Find(u => u.name == type), city.location);

	// "All naval unit movement rates are increased by one" (the Great
	// Lighthouse, and Magellan's Voyage).
	[Fact]
	public void LighthouseAndMagellansSpeedUpShips() {
		MapUnit galley = Spawn("Galley");
		MapUnit warrior = Spawn("Warrior");
		int galleyMoves = galley.MovementPerTurn();

		Add("The Great Lighthouse");
		Assert.Equal(galleyMoves + 1, galley.MovementPerTurn());
		Add("Magellan's Voyage");
		Assert.Equal(galleyMoves + 2, galley.MovementPerTurn());
		Assert.Equal(warrior.unitType.movement, warrior.MovementPerTurn());

		// The moves come with the turn.
		galley.OnBeginTurn();
		Assert.Equal(galleyMoves + 2, galley.movementPoints.remaining);

		MakeObsolete("The Great Lighthouse");
		Assert.Equal(galleyMoves + 1, galley.MovementPerTurn());
	}

	// "One extra Commerce in every square that already produces at least one",
	// in the Colossus's city, until Flight.
	[Fact]
	public void TheColossusAddsCommerceWhereThereIsSome() {
		// No despotism penalty to hide it.
		us.government = gameData.governments.Find(g => g.name == "Monarchy");
		List<Tile> tiles = [.. city.location.GetTilesWithinRankDistance(2)];
		Dictionary<Tile, int> before = tiles.ToDictionary(t => t, t => t.CommerceYield(city).yield);
		Assert.Contains(before.Values, v => v == 0);
		Assert.Contains(before.Values, v => v >= 1);

		Add("The Colossus");
		Assert.All(tiles, t => Assert.Equal(before[t] >= 1 ? before[t] + 1 : 0, t.CommerceYield(city).yield));

		MakeObsolete("The Colossus");
		Assert.All(tiles, t => Assert.Equal(before[t], t.CommerceYield(city).yield));
	}

	[SkippableFact]
	public void TheColossusEffectComesFromTheBiq() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), "No Civ3 install found.");
		string scenario = Path.Combine(Civ3Location.GetCiv3Path(), "Conquests", "Conquests", "2 Rise of Rome.biq");
		SaveGame save = ImportCiv3.ImportBiq(scenario, PathUtils.defaultBicPath,
			_ => PathUtils.defaultPediaIconsPath, _ => PathUtils.defaultCivilopediaTextPath);
		List<SaveBuilding> withFlag = save.Buildings.Where(b => b.wonderFlags.Contains(SaveBuilding.WonderFlag.IncreasedTradeInTilesProducingCommerce)).ToList();
		Assert.NotEmpty(withFlag);
		Assert.All(withFlag, b => Assert.Contains("buildings.tile_modifiers.increases_trade_in_tiles_producing_commerce", b.tileModifiers));
	}

	// "Doubles unit combat values versus Barbarians", attacking or defending,
	// until Metallurgy.
	[Fact]
	public void TheGreatWallDoublesOurUnitsAgainstBarbarians() {
		Player barbarians = gameData.players.Find(p => p.isBarbarians);
		Player rival = gameData.players.First(p => p != us && !p.isBarbarians);
		// Outside the city, which the Great Wall also gives Walls.
		Tile field = city.location.neighbors.Values.First(t => t.IsLand());
		Tile beyond = field.neighbors.Values.First(t => t.IsLand() && t != city.location);
		UnitPrototype spear = gameData.unitPrototypes.Find(u => u.name == "Spearman");
		MapUnit spearman = gameData.SpawnUnit(us, spear, field);
		MapUnit barbarian = gameData.SpawnUnit(barbarians, spear, beyond);
		MapUnit foreigner = gameData.SpawnUnit(rival, spear, beyond);
		CombatRole[] roles = [CombatRole.Attack, CombatRole.Defense];
		Dictionary<CombatRole, double> before = roles.ToDictionary(r => r, r => spearman.StrengthVersus(barbarian, r, null));
		double theirs = barbarian.StrengthVersus(spearman, CombatRole.Attack, null);

		Add("The Great Wall");
		Assert.All(roles, r => Assert.Equal(before[r] + spearman.BaseCombatStrength(r), spearman.StrengthVersus(barbarian, r, null), 6));
		Assert.All(roles, r => Assert.Equal(before[r], spearman.StrengthVersus(foreigner, r, null), 6));
		Assert.Equal(theirs, barbarian.StrengthVersus(spearman, CombatRole.Attack, null), 6);

		MakeObsolete("The Great Wall");
		Assert.All(roles, r => Assert.Equal(before[r], spearman.StrengthVersus(barbarian, r, null), 6));
	}

	// Smith's "pays maintenance costs for all trade-based city improvements
	// (Harbors, Marketplaces, Banks, Airports, Commercial Dock and Stock
	// Exchanges)", in every city.
	[Fact]
	public void SmithsPaysForTradeBuildings() {
		string[] trade = ["Harbor", "Marketplace", "Bank", "Airport", "Commercial Dock", "Stock Exchange"];
		foreach (string name in trade.Append("Barracks").Append("Temple")) {
			Add(name);
		}
		int upkeep = city.MaintenanceCostsRaw();
		int tradeUpkeep = trade.Sum(name => Building(name).maintenanceCost);
		Assert.True(tradeUpkeep > 0 && upkeep > tradeUpkeep);

		Tile site = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity()
			&& gameData.cities.All(c => c.location.DistanceTo(t) > 3));
		CityInteractions.BuildCity(site, us, "Trading").AddBuilding(Building("Smith's Trading Company"), gameData);
		Assert.Equal(upkeep - tradeUpkeep, city.MaintenanceCostsRaw());
	}

	// A full food box: overfilled, since it's capped at full after the turn's
	// food (which may be short) goes in.
	private void GrowOnce() {
		city.foodStored = city.FoodNeededToGrow() + 1000;
		city.HandleCityGrowth(gameData);
	}

	// "All cities get a population increase of two instead of one when the
	// Food Storage box is full", up to the size limit.
	[Fact]
	public void LongevityGrowsCitiesByTwo() {
		GrowOnce();
		Assert.Equal(2, city.residents.Count);

		Tile site = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity()
			&& gameData.cities.All(c => c.location.DistanceTo(t) > 3));
		CityInteractions.BuildCity(site, us, "Old Age").AddBuilding(Building("Longevity"), gameData);
		GrowOnce();
		Assert.Equal(4, city.residents.Count);

		int limit = city.SizeLimit(gameData);
		CitizenType laborer = gameData.citizenTypes.Find(c => c.IsDefaultCitizen);
		while (city.residents.Count < limit - 1) {
			city.AddCitizen(new CityResident { city = city, nationality = us.civilization, citizenType = laborer });
		}
		GrowOnce();
		Assert.Equal(limit, city.residents.Count);
	}

	// Two civs we've met that know the next tech we could research, and one
	// civ we haven't met. All computer players: a human newly met would be
	// offered trades, which wait for an answer.
	private (Tech tech, Player first, Player second, Player stranger) KnownToTwo(C7GameData.GameData game, Player owner) {
		List<Player> others = game.players.Where(p => p != owner && !p.isBarbarians).ToList();
		List<Player> computers = others.Where(p => !p.isHuman).ToList();
		foreach (Player p in others) {
			owner.playerRelationships.Remove(p.id);
			p.playerRelationships.Remove(owner.id);
		}
		owner.EnsureRelationshipExists(computers[0]);
		owner.EnsureRelationshipExists(computers[1]);
		// Civs start with different techs: only ours, for nothing else to pass on.
		foreach (Player p in others) {
			p.knownTechs.Clear();
			p.knownTechs.UnionWith(owner.knownTechs);
		}
		Tech tech = owner.GetAvailableTechsToResearch(game.techs).First();
		return (tech, computers[0], computers[1], computers[2]);
	}

	// "Gets any Civilization Advance already discovered by two other known
	// civilizations for free", until Education.
	[Fact]
	public void TheGreatLibraryGivesWhatTwoKnownCivsKnow() {
		Add("The Great Library");
		(Tech tech, Player first, Player second, Player stranger) = KnownToTwo(gameData, us);

		first.knownTechs.Add(tech.id);
		stranger.knownTechs.Add(tech.id);
		us.CollectGreatLibraryTechs(gameData);
		Assert.DoesNotContain(tech.id, us.knownTechs);

		second.knownTechs.Add(tech.id);
		MakeObsolete("The Great Library");
		us.CollectGreatLibraryTechs(gameData);
		Assert.DoesNotContain(tech.id, us.knownTechs);

		us.knownTechs.Remove(Building("The Great Library").renderedObsoleteBy.id);
		us.CollectGreatLibraryTechs(gameData);
		Assert.Contains(tech.id, us.knownTechs);
		MsgTechsFromWonder told = Assert.Single(EngineStorage.messagesToUI.OfType<MsgTechsFromWonder>());
		Assert.Equal((us.id, "The Great Library"), (told.playerId, told.wonder));
		Assert.Equal([tech.Name], told.techs);
	}

	// One advance may lead to the next: a tech whose prerequisites include
	// the first comes too, if two known civs have it.
	[Fact]
	public void TheGreatLibraryFollowsTheTree() {
		Add("The Great Library");
		(_, Player first, Player second, _) = KnownToTwo(gameData, us);
		bool Opens(Tech tech, Tech next) => !us.knownTechs.Contains(next.id)
			&& next.Prerequisites.Any(p => p.id == tech.id)
			&& next.Prerequisites.All(p => p.id == tech.id || us.knownTechs.Contains(p.id))
			&& EraUtils.GetEraIndex(next.EraCivilopediaName) <= us.EraIndex();
		(Tech tech, Tech next) = us.GetAvailableTechsToResearch(gameData.techs)
			.SelectMany(a => gameData.techs.Where(t => Opens(a, t)).Select(t => (a, t)))
			.First();
		foreach (Player p in new[] { first, second }) {
			p.knownTechs.Add(tech.id);
			p.knownTechs.Add(next.id);
		}

		us.CollectGreatLibraryTechs(gameData);
		Assert.Contains(tech.id, us.knownTechs);
		Assert.Contains(next.id, us.knownTechs);
	}

	[Fact]
	public async Task TheGreatLibraryWorksAtTheEndOfTheTurn() {
		Player human = await CreateGame.createGame(fixture.saveGame.Clone(), (_) => fixture.behaviors);
		C7GameData.GameData game = EngineStorage.gameData;
		TurnHandling.OnBeginTurn();
		TurnHandling.InitTurnData();
		await TurnHandling.AdvanceTurn();
		Player owner = game.players.First(p => !p.isHuman && !p.isBarbarians);
		City home = CityInteractions.BuildCity(owner.units[0].location, owner, "Alexandria");
		home.AddBuilding(game.Buildings.Find(b => b.name == "The Great Library"), game);
		(Tech tech, Player first, Player second, _) = KnownToTwo(game, owner);
		first.knownTechs.Add(tech.id);
		second.knownTechs.Add(tech.id);
		// No trading for it first.
		game.gameDifficulty.AIToAITradeRate = 0;
		EngineStorage.messagesToUI.Clear();

		TurnHandling.OnEndTurn(human);
		human.hasPlayedThisTurn = true;
		await TurnHandling.AdvanceTurn();

		Assert.Contains(tech.id, owner.knownTechs);
		Assert.Contains(EngineStorage.messagesToUI.OfType<MsgTechsFromWonder>(), m => m.playerId == owner.id);
	}

	private void Finish(City where, string name) {
		where.SetItemBeingProduced(Building(name));
		where.SetStoredShields(where.owner.ShieldCost(where.itemBeingProduced));
		where.HandleCityProduction(gameData);
		Assert.Contains(where.constructed_buildings, cb => cb.building.name == name);
	}

	// "Grants two Civilization Advances as soon as completed": the computer
	// takes them at once, a human chooses.
	[Fact]
	public void TheTheoryOfEvolutionGivesTwoAdvances() {
		int known = us.knownTechs.Count;
		Finish(city, "Theory of Evolution");
		Assert.Equal(known + 2, us.knownTechs.Count);
		Assert.Equal(0, us.freeTechsRemaining);

		Player human = gameData.players.First(p => !p.isBarbarians && p != us);
		human.isHuman = true;
		Tile site = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity()
			&& gameData.cities.All(c => c.location.DistanceTo(t) > 3));
		City theirs = CityInteractions.BuildCity(site, human, "Galapagos");
		gameData.GreatWondersBuilt.Remove("Theory of Evolution");
		Finish(theirs, "Theory of Evolution");
		Assert.Equal(2, human.freeTechsRemaining);

		// Other buildings give none.
		Finish(city, "Temple");
		Assert.Equal(known + 2, us.knownTechs.Count);
	}

	// The hit points an elite unit of ours at `where`, down to 1, gets back
	// for staying put a turn; and what it had lost.
	private (int healed, int lost) Heal(string type, Tile where) {
		MapUnit unit = gameData.SpawnUnit(us, gameData.unitPrototypes.Find(u => u.name == type), where);
		unit.Promote();
		unit.Promote();
		int lost = unit.maxHitPoints - 1;
		unit.hitPointsRemaining = 1;
		unit.OnBeginTurn();
		int healed = unit.hitPointsRemaining - 1;
		gameData.RemoveUnit(unit);
		return (healed, lost);
	}

	private int Healed(string type, Tile where) => Heal(type, where).healed;

	private Tile FarSite() => gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity()
		&& t.unitsOnTile.Count == 0 && gameData.cities.All(c => c.location.DistanceTo(t) > 4));

	// Civilopedia "Repairing Damage": a unit heals "in friendly or neutral
	// territory"; "a city with a barracks heals ground units completely in one
	// turn", a harbor sea units; "a civilization with Battlefield Medicine
	// heals in enemy territory".
	[Fact]
	public void UnitsHealByTerritoryAndBuildings() {
		Assert.True(gameData.healRateInHostileField < gameData.healRateInNeutralField);
		Player rival = gameData.players.First(p => p != us && !p.isBarbarians && !p.isHuman);
		City theirs = CityInteractions.BuildCity(FarSite(), rival, "Over There");
		Tile ours = city.location.neighbors.Values.First(t => t.IsLand() && t.unitsOnTile.Count == 0);
		Tile their = theirs.location.neighbors.Values.First(t => t.IsLand() && t.unitsOnTile.Count == 0);
		Tile nobodys = FarSite();
		Assert.Equal((us, rival, null), (ours.OwningPlayer(), their.OwningPlayer(), nobodys.OwningPlayer()));

		Assert.Equal(gameData.healRateInFriendlyField, Healed("Warrior", ours));
		Assert.Equal(gameData.healRateInNeutralField, Healed("Warrior", nobodys));
		us.EnsureRelationshipExists(rival);
		Assert.Equal(gameData.healRateInNeutralField, Healed("Warrior", their));
		PlayerRelationship.DeclareWar(us, rival, false, gameData.turn, -1);
		Assert.Equal(gameData.healRateInHostileField, Healed("Warrior", their));

		Tile site = FarSite();
		CityInteractions.BuildCity(site, us, "Field Hospital").AddBuilding(Building("Battlefield Medicine"), gameData);
		Assert.Equal(gameData.healRateInNeutralField, Healed("Warrior", their));

		Assert.Equal(gameData.healRateInCity, Healed("Warrior", city.location));
		Assert.Equal(gameData.healRateInCity, Healed("Galley", city.location));
		Add("Barracks");
		(int healed, int lost) = Heal("Warrior", city.location);
		Assert.True(lost > gameData.healRateInCity);
		Assert.Equal(lost, healed);
		Assert.Equal(gameData.healRateInCity, Healed("Galley", city.location));
		Add("Harbor");
		(healed, lost) = Heal("Galley", city.location);
		Assert.Equal(lost, healed);
	}

	private void SetAge(string name, int years) {
		int now = gameData.timeOptions.GetAbsoluteYear(gameData.turn);
		city.constructed_buildings.Find(cb => cb.building.name == name).year = now - years;
	}

	// "A Great Wonder can become a Tourist Attraction once it has stood the
	// test of time": 2 gold from 1000 years up to 14 from 2501, by the
	// Civilopedia's table, into the treasury.
	[Fact]
	public void OldWondersDrawTourists() {
		Add("The Pyramids");
		Add("Magellan's Voyage");
		// Not a tourist attraction.
		SetAge("Magellan's Voyage", 3000);
		(int years, int gold)[] table = [(999, 0), (1000, 2), (1500, 2), (1501, 4), (1750, 4), (1751, 6), (1875, 6),
			(1876, 8), (2000, 8), (2001, 10), (2250, 10), (2251, 12), (2500, 12), (2501, 14), (5000, 14)];
		foreach ((int years, int gold) in table) {
			SetAge("The Pyramids", years);
			Assert.Equal(gold, city.TourismGold());
		}

		// Obsolete wonders still draw them.
		Add("The Great Library");
		SetAge("The Great Library", 1000);
		MakeObsolete("The Great Library");
		Assert.Equal(16, city.TourismGold());

		PlayerCommerceBreakdown flows = us.AggregateFlows();
		Assert.Equal(16, flows.tourism);
		SetAge("The Pyramids", 0);
		SetAge("The Great Library", 0);
		Assert.Equal(16, flows.Netflows() - us.AggregateFlows().Netflows());
	}

	[Fact]
	public void TheTourismTableComesFromTheRuleset() {
		using JsonDocument ruleset = JsonDocument.Parse(File.ReadAllText(Path.Combine(PathUtils.GameModesDir, "civ3", "ruleset.json")));
		List<(int, int)> steps = ruleset.RootElement.GetProperty("rules").GetProperty("touristAttractionGold").EnumerateArray()
			.Select(e => (e.GetProperty("years").GetInt32(), e.GetProperty("gold").GetInt32())).ToList();
		Assert.Equal(7, steps.Count);
		Assert.Equal(steps, gameData.rules.TouristAttractionGold.Select(step => (step.years, step.gold)));
		Assert.Equal(steps, new Rules().TouristAttractionGold.Select(step => (step.years, step.gold)));
	}
}
