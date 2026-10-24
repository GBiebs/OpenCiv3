using System;
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

// Civ3's enslavement (Civilopedia "Enslavement"): a unit with the ability has
// a one in three chance, each time it wins a battle, of turning the unit it
// defeated into a unit of its enslave type, which joins the winner at once.
// See MapUnit.MaybeEnslave.
public class EnslavementTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly Tile from;
	private readonly Tile to;

	public EnslavementTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		// A clone, because the tests change unit types, which the game shares with the save.
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		(us, them) = (ais[0], ais[1]);
		us.DeclareWarOn(them, gameData.turn);
		// Two empty land tiles side by side, away from everyone's units.
		from = gameData.map.tiles.First(t => IsEmptyLand(t)
			&& t.neighbors.TryGetValue(TileDirection.EAST, out Tile east) && IsEmptyLand(east)
			&& gameData.mapUnits.All(u => u.location.DistanceTo(t) > 3));
		to = from.neighbors[TileDirection.EAST];
		// Strong enough to win every battle, attacking or defending.
		Type("Javelin Thrower").attack = 1000;
		Type("Javelin Thrower").defense = 1000;
	}

	private static bool IsEmptyLand(Tile t) {
		return Tile.IsTileValid(t) && t.IsLand() && t.unitsOnTile.Count == 0 && !t.HasCity() && !t.hasBarbarianCamp;
	}

	private UnitPrototype Type(string name) => gameData.unitPrototypes.Find(u => u.name == name);

	private MapUnit Spawn(Player owner, string type, Tile tile) {
		return gameData.SpawnUnit(owner, Type(type), tile);
	}

	// Has `attacker` beat fresh warriors of theirs until one is enslaved, and
	// returns the new unit.
	private async Task<MapUnit> AttackUntilEnslaved(MapUnit attacker) {
		for (int i = 0; i < 100; i++) {
			attacker.hitPointsRemaining = 3;
			int unitsBefore = attacker.owner.units.Count;
			Assert.Equal(CombatResult.DefenderKilled, await attacker.Fight(Spawn(them, "Warrior", to)));
			if (attacker.owner.units.Count > unitsBefore) {
				return attacker.owner.units.Last();
			}
		}
		throw new Exception("Nothing was enslaved in 100 won battles");
	}

	[Fact]
	public void TheCiv3RulesetHasThreeEnslavers() {
		Assert.Equal(["Javelin Thrower: Worker", "Man-O-War: Man-O-War", "Privateer: Privateer"],
			gameData.unitPrototypes.Where(u => u.enslaveResult != null).Select(u => $"{u.name}: {u.enslaveResult.name}").Distinct().Order());
		Assert.Same(Type("Worker"), Type("Javelin Thrower").enslaveResult);
	}

	[Fact]
	public async Task AWinnerEnslavesOneDefeatedUnitInThree() {
		MapUnit javelin = Spawn(us, "Javelin Thrower", from);

		const int trials = 3000;
		for (int i = 0; i < trials; i++) {
			javelin.hitPointsRemaining = 3;
			Assert.Equal(CombatResult.DefenderKilled, await javelin.Fight(Spawn(them, "Warrior", to)));
		}

		int enslaved = from.unitsOnTile.Count(u => u.unitType.name == "Worker");
		Assert.Equal(enslaved, EngineStorage.messagesToUI.OfType<MsgUnitEnslaved>().Count());
		double chance = 1.0 / 3;
		double sigma = Math.Sqrt(trials * chance * (1 - chance));
		Assert.InRange(enslaved, trials * chance - 4 * sigma, trials * chance + 4 * sigma);
	}

	[Fact]
	public async Task TheSlaveJoinsTheWinnersStackKeepsItsNationalityAndCanMove() {
		MapUnit javelin = Spawn(us, "Javelin Thrower", from);

		MapUnit slave = await AttackUntilEnslaved(javelin);

		Assert.Equal("Worker", slave.unitType.name);
		Assert.Equal(us, slave.owner);
		Assert.Equal(from, slave.location);
		Assert.Contains(slave, from.unitsOnTile);
		Assert.Contains(slave, gameData.mapUnits);
		Assert.Equal(them.civilization, slave.nationality);
		Assert.True(slave.movementPoints.canMove);
		Assert.Empty(to.unitsOnTile);

		MsgUnitEnslaved message = Assert.Single(EngineStorage.messagesToUI.OfType<MsgUnitEnslaved>());
		Assert.Equal(from, message.location);
		Assert.Equal("Our Javelin Thrower enslaved the defeated Warrior! It now serves us as a Worker.", message.TextFor(us.id));
		Assert.Equal("Our defeated Warrior was enslaved by the enemy!", message.TextFor(them.id));
		Player bystander = gameData.players.First(p => p != us && p != them);
		Assert.Null(message.TextFor(bystander.id));
	}

	[Fact]
	public async Task DefendersEnslaveToo() {
		MapUnit javelin = Spawn(us, "Javelin Thrower", from);

		for (int i = 0; i < 100 && us.units.Count(u => u.location == from) == 1; i++) {
			javelin.hitPointsRemaining = 3;
			Assert.Equal(CombatResult.AttackerKilled, await Spawn(them, "Warrior", to).Fight(javelin));
		}

		MapUnit slave = Assert.Single(from.unitsOnTile, u => u != javelin);
		Assert.Equal("Worker", slave.unitType.name);
		Assert.Equal(us, slave.owner);
		Assert.Equal(them.civilization, slave.nationality);
	}

	[Fact]
	public async Task UnitsWithoutAnEnslaveResultNeverEnslave() {
		Type("Archer").attack = 1000;
		MapUnit archer = Spawn(us, "Archer", from);

		for (int i = 0; i < 100; i++) {
			archer.hitPointsRemaining = 3;
			Assert.Equal(CombatResult.DefenderKilled, await archer.Fight(Spawn(them, "Warrior", to)));
		}

		Assert.Equal([archer], from.unitsOnTile);
		Assert.Empty(EngineStorage.messagesToUI.OfType<MsgUnitEnslaved>());
	}

	[Fact]
	public async Task BarbariansDoNotEnslave() {
		Player barbarians = gameData.players.Find(p => p.isBarbarians);
		MapUnit javelin = Spawn(barbarians, "Javelin Thrower", from);

		for (int i = 0; i < 100; i++) {
			javelin.hitPointsRemaining = 3;
			Assert.Equal(CombatResult.DefenderKilled, await javelin.Fight(Spawn(them, "Warrior", to)));
		}

		Assert.Equal([javelin], from.unitsOnTile);
	}

	[Fact]
	public async Task NoShipIsMadeOnLand() {
		// A mod's land unit that enslaves into ships makes none away from water.
		Type("Javelin Thrower").enslaveResult = Type("Galley");
		MapUnit javelin = Spawn(us, "Javelin Thrower", from);

		for (int i = 0; i < 100; i++) {
			javelin.hitPointsRemaining = 3;
			await javelin.Fight(Spawn(them, "Warrior", to));
		}

		Assert.Equal([javelin], from.unitsOnTile);
	}

	[Fact]
	public async Task EnslaveResultsAndSlavesAreSaved() {
		MapUnit slave = await AttackUntilEnslaved(Spawn(us, "Javelin Thrower", from));

		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);

		UnitPrototype javelin = reloaded.unitPrototypes.Find(u => u.name == "Javelin Thrower");
		Assert.Same(reloaded.unitPrototypes.Find(u => u.name == "Worker"), javelin.enslaveResult);
		Assert.Null(reloaded.unitPrototypes.Find(u => u.name == "Warrior").enslaveResult);
		MapUnit loaded = reloaded.mapUnits.Find(u => u.id == slave.id);
		Assert.Equal(us.id, loaded.owner.id);
		Assert.Equal(them.civilization.name, loaded.nationality.name);
	}

	[SkippableFact]
	public void TheEnslaveResultsAreImportedFromCiv3() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), "No Civ3 install found.");
		// The stock rules (conquests.biq) have no map to import, so use a
		// scenario. In Mesoamerica the Maya, Inca and Aztec unique units
		// enslave into workers (CivFanatics, "Anyone know exactly how
		// enslavement works??").
		string scenario = Path.Combine(Civ3Location.GetCiv3Path(), "Conquests", "Conquests", "5 Mesoamerica.biq");

		SaveGame save = ImportCiv3.ImportBiq(scenario, PathUtils.defaultBicPath,
			_ => PathUtils.defaultPediaIconsPath, _ => PathUtils.defaultCivilopediaTextPath);

		Assert.Equal(["Chasqui Scout: Worker", "Jaguar Warrior: Worker", "Javelin Thrower: Worker"],
			save.UnitPrototypes.Where(u => u.enslaveResult != null).Select(u => $"{u.name}: {u.enslaveResult}").Order());
	}
}
