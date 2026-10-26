using System.Collections.Generic;
using System.IO;
using System.Linq;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using QueryCiv3;
using Xunit;

namespace EngineTests.GameData;

// The great wonders' effects, as the Conquests Civilopedia gives them.
public class WonderEffectsTest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly City city;

	public WonderEffectsTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		us = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		city = CityInteractions.BuildCity(us.units[0].location, us, "Wonders");
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
		int galleyMoves = galley.unitType.movement;

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
}
