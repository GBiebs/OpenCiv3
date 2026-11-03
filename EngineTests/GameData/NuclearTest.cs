using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7Engine.AI;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using QueryCiv3;
using Xunit;

namespace EngineTests.GameData;

// Nuclear weapons: the Manhattan Project allows them for everyone; a
// Tactical Nuke strikes within 6 tiles, an ICBM anywhere; a missile defense
// downs 75% aimed at its owner's cities; a strike kills every unit on its
// tile and around, half the city, some buildings, and leaves fallout; the
// world remembers.
public class NuclearTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly Player bystander;
	private readonly City home;
	private readonly City target;

	public NuclearTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(3).ToArray();
		(us, them, bystander) = (ais[0], ais[1], ais[2]);
		home = CityInteractions.BuildCity(us.units.First(u => u.unitType.isSettler).location, us, "Home");
		target = CityInteractions.BuildCity(LandAt(home.location, 5), them, "Target");
		Grow(target, 8);
		us.DeclareWarOn(them, gameData.turn);
		bystander.EnsureRelationshipExists(us);
		gameData.GreatWondersBuilt.Add("The Manhattan Project");
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private UnitPrototype Type(string name) => gameData.unitPrototypes.Find(u => u.name == name);

	private MapUnit Spawn(Player owner, string type, Tile tile) => gameData.SpawnUnit(owner, Type(type), tile);

	private Tile LandAt(Tile from, int distance) {
		return gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && t.unitsOnTile.Count == 0
			&& t.DistanceTo(from) == distance && !t.hasGoodyHut && !t.hasBarbarianCamp && t.neighbors.Values.All(n => !n.HasCity()));
	}

	private void Grow(City city, int size) {
		CitizenType laborer = gameData.citizenTypes.Find(c => c.IsDefaultCitizen);
		while (city.residents.Count < size) {
			CityResident resident = new() { city = city, nationality = city.owner.civilization, citizenType = laborer };
			city.AddCitizen(resident);
			CityTileAssignmentAI.AssignNewCitizenToTile(gameData, resident);
		}
	}

	[Fact]
	public void TheManhattanProjectAllowsThemForEveryone() {
		Assert.True(Nuclear.Allowed(gameData));
		gameData.GreatWondersBuilt.Clear();
		Assert.False(Nuclear.Allowed(gameData));
		foreach (Tech t in gameData.techs) us.knownTechs.Add(t.id);
		Assert.False(Type("Tactical Nuke").CanProduce(home, gameData.Resources.ToHashSet()));
		gameData.GreatWondersBuilt.Add("The Manhattan Project");
		Assert.True(Type("Tactical Nuke").CanProduce(home, gameData.Resources.ToHashSet()));
	}

	[Fact]
	public void TheRulesetFlagsItsNukes() {
		Assert.True(Nuclear.IsNuke(Type("ICBM")));
		Assert.Contains(SaveUnitPrototype.Flag.ICBM, Type("ICBM").flags);
		Assert.True(Nuclear.IsNuke(Type("Tactical Nuke")));
		Assert.DoesNotContain(SaveUnitPrototype.Flag.ICBM, Type("Tactical Nuke").flags);
		Assert.False(Nuclear.IsNuke(Type("Cruise Missile")));
	}

	[Fact]
	public void TheirOwnerCanSelectThem() {
		// An ICBM can't move, but it fires from where it stands.
		MapUnit icbm = Spawn(us, "ICBM", home.location);
		Assert.True(icbm.unitType.immobile);
		Assert.True(icbm.CanBeActive());
		Assert.True(Spawn(us, "Tactical Nuke", home.location).CanBeActive());

		// The player is asked for its orders (in its turn: it has the UI).
		Player human = gameData.players.Find(p => p.isHuman);
		EngineStorage.uiControllerID = human.id;
		foreach (MapUnit u in human.units) {
			u.movementPoints.onConsumeAll();
		}
		MapUnit ours = Spawn(human, "ICBM", human.units[0].location);
		UnitInteractions.ClearWaitQueue();
		Assert.Equal(ours, UnitInteractions.getNextSelectedUnit());
	}

	[Fact]
	public void TacticalNukesReachSixTilesAndICBMsAnywhere() {
		MapUnit tactical = Spawn(us, "Tactical Nuke", home.location);
		Assert.True(Nuclear.CanStrike(tactical, target.location));
		Tile far = LandAt(home.location, 7);
		Spawn(them, "Warrior", far);
		Assert.False(Nuclear.CanStrike(tactical, far));
		MapUnit icbm = Spawn(us, "ICBM", home.location);
		Assert.True(Nuclear.CanStrike(icbm, far));
		Assert.True(icbm.CanBombardTile(far));

		// Any tile, seen or not, even with no one there (user, 2026-10-29).
		Tile empty = LandAt(home.location, 9);
		Assert.False(us.tileKnowledge.isActiveTile(empty));
		Assert.True(Nuclear.CanStrike(icbm, empty));
		Assert.Equal(Nuclear.Outcome.Detonated, Nuclear.Strike(gameData, icbm, empty));
	}

	[Fact]
	public void ACivAtPeaceNeedsWarFirst() {
		Tile far = LandAt(home.location, 7);
		Spawn(them, "Warrior", far);
		PlayerRelationship.SignPeaceAfterWar(us, them, gameData);
		MapUnit icbm = Spawn(us, "ICBM", home.location);
		// It can be aimed there (the player may not know who's there), but
		// it won't go off until we're at war.
		Assert.True(Nuclear.CanStrike(icbm, far));
		Assert.Equal([them], Nuclear.AtPeace(us, far));
		Assert.Equal(Nuclear.Outcome.Impossible, Nuclear.Strike(gameData, icbm, far));
		Assert.Contains(icbm, us.units);
		us.DeclareWarOn(them, gameData.turn);
		Assert.Empty(Nuclear.AtPeace(us, far));
		Assert.Equal(Nuclear.Outcome.Detonated, Nuclear.Strike(gameData, icbm, far));
	}

	[Fact]
	public async Task AStrikeDevastatesTheTargetAndAround() {
		foreach (string b in new[] { "Temple", "Granary", "Library" }) target.AddBuilding(gameData.Buildings.Find(x => x.name == b), gameData);
		target.AddBuilding(gameData.Buildings.Find(x => x.name == "The Pyramids"), gameData);
		us.rules.nuclear.buildingDestroyedPercent = 100;
		us.rules.nuclear.falloutPercent = 100;
		MapUnit defender = Spawn(them, "Rifleman", target.location);
		Tile next = target.location.neighbors.Values.First(t => t.IsLand() && !t.HasCity());
		MapUnit nearby = Spawn(them, "Tank", next);
		Tile outside = target.location.neighbors.Values.First(t => t.IsLand()).neighbors.Values.First(t => t.IsLand() && t.DistanceTo(target.location) == 2);
		MapUnit spared = Spawn(them, "Tank", outside);
		MapUnit nuke = Spawn(us, "ICBM", home.location);

		await nuke.Bombard(target.location);
		Assert.DoesNotContain(nuke, us.units);
		Assert.DoesNotContain(defender, them.units);
		Assert.DoesNotContain(nearby, them.units);
		Assert.Contains(spared, them.units);
		Assert.Equal(4, target.residents.Count);
		// Only the wonder and the palace (it's their capital) are left.
		Assert.All(target.constructed_buildings, b => Assert.True(b.building.IsWonder() || b.building.isCenterOfEmpire));
		Assert.Contains(target.constructed_buildings, b => b.building.name == "The Pyramids");
		Assert.True(next.HasPollution());
		Assert.Equal(1, gameData.nukesUsed);
		MsgNuclearAttack news = Assert.Single(EngineStorage.messagesToUI.OfType<MsgNuclearAttack>());
		Assert.Equal(Nuclear.Outcome.Detonated, news.outcome);
		Assert.Equal(2, news.unitsKilled);
		Assert.Equal(4, news.citizensKilled);
	}

	[Fact]
	public void TheRulesSetTheDamage() {
		us.rules.nuclear.populationKilledPercent = 25;
		us.rules.nuclear.buildingDestroyedPercent = 0;
		us.rules.nuclear.falloutPercent = 0;
		target.AddBuilding(gameData.Buildings.Find(x => x.name == "Temple"), gameData);
		Tile next = target.location.neighbors.Values.First(t => t.IsLand() && !t.HasCity());
		Nuclear.Strike(gameData, Spawn(us, "ICBM", home.location), target.location);
		Assert.Equal(6, target.residents.Count);
		Assert.Contains(target.constructed_buildings, b => b.building.name == "Temple");
		Assert.False(next.HasPollution());
	}

	[Fact]
	public void AMissileDefenseShootsThemDown() {
		Building smd = gameData.Buildings.Find(b => b.HasWonderFlag(SaveBuilding.WonderFlag.DecreasesMissileSuccess));
		City theirOther = CityInteractions.BuildCity(LandAt(home.location, 12), them, "Shield");
		theirOther.AddBuilding(smd, gameData);
		Assert.True(Nuclear.Defended(us, target.location));
		// Aimed beside the city the blast still reaches it, so it's covered;
		// two tiles away it isn't.
		Tile next = target.location.neighbors.Values.First(t => t.IsLand() && !t.HasCity());
		Spawn(them, "Tank", next);
		Assert.True(Nuclear.Defended(us, next));
		Tile beyond = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && t.DistanceTo(target.location) == 2
			&& t.neighbors.Values.All(n => !n.HasCity()));
		Assert.False(Nuclear.Defended(us, beyond));
		// Our own missile defense doesn't shoot down our nukes.
		Assert.False(Nuclear.Defended(them, target.location));

		us.rules.nuclear.missileDefensePercent = 100;
		Assert.Equal(Nuclear.Outcome.ShotDown, Nuclear.Strike(gameData, Spawn(us, "ICBM", home.location), target.location));
		Assert.Equal(8, target.residents.Count);
		Assert.Equal(Nuclear.Outcome.ShotDown, Nuclear.Strike(gameData, Spawn(us, "ICBM", home.location), next));
		Assert.Equal(Nuclear.Outcome.Detonated, Nuclear.Strike(gameData, Spawn(us, "ICBM", home.location), beyond));
		us.rules.nuclear.missileDefensePercent = 0;
		Assert.Equal(Nuclear.Outcome.Detonated, Nuclear.Strike(gameData, Spawn(us, "ICBM", home.location), target.location));
	}

	[Fact]
	public void TheWorldRemembers() {
		int victim = Attitude.Score(gameData, them, us);
		int other = Attitude.Score(gameData, bystander, us);
		Nuclear.Strike(gameData, Spawn(us, "ICBM", home.location), target.location);
		Assert.Equal(victim + 15, Attitude.Score(gameData, them, us));
		Assert.Equal(other + 6, Attitude.Score(gameData, bystander, us));
	}

	[Fact]
	public void NukesUsedAreSaved() {
		gameData.nukesUsed = 3;
		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		Assert.Equal(3, reloaded.nukesUsed);
	}

	[Fact]
	public void TheAINukesOnlyEnemies() {
		MapUnit nuke = Spawn(us, "ICBM", home.location);
		gameData.nukesUsed = 1;
		Assert.Equal(target.location, NuclearAI.Target(gameData, nuke));
		// Not empty land, though it may now aim there, nor a civ at peace.
		PlayerRelationship.SignPeaceAfterWar(us, them, gameData);
		Assert.Null(NuclearAI.Target(gameData, nuke));
	}

	[Fact]
	public void TheAIDoesntFireFirst() {
		MapUnit nuke = Spawn(us, "ICBM", home.location);
		NuclearAI.Act(us, gameData);
		Assert.Contains(nuke, us.units);

		gameData.nukesUsed = 1;
		Assert.Equal(target.location, NuclearAI.Target(gameData, nuke));
		NuclearAI.Act(us, gameData);
		Assert.DoesNotContain(nuke, us.units);
		Assert.Equal(4, target.residents.Count);
	}

	[Fact]
	public void TheAISparesItsOwn() {
		gameData.nukesUsed = 1;
		MapUnit nuke = Spawn(us, "ICBM", home.location);
		Spawn(us, "Warrior", target.location.neighbors.Values.First(t => t.IsLand() && !t.HasCity()));
		Assert.NotEqual(target.location, NuclearAI.Target(gameData, nuke));
	}
}

// The nuclear flags from Civ3's PRTO.
public class NuclearImportTest {
	[SkippableFact]
	public void TheFlagsComeFromTheBiq() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), "No Civ3 install found.");
		// No stock scenario has an ICBM, so also a game on the standard rules
		// (the sample save).
		string pacific = Path.Combine(Civ3Location.GetCiv3Path(), "Conquests", "Conquests", "9 WWII in the Pacific.biq");
		string sample = Path.Combine(PathUtils.getDataPath("saves"), "12345.SAV");
		Skip.If(!File.Exists(sample), "The sample save isn't downloaded.");
		(SaveGame, QueryCiv3.Biq.PRTO[])[] imports = [
			(ImportCiv3.ImportBiq(pacific, PathUtils.defaultBicPath, _ => PathUtils.defaultPediaIconsPath, _ => PathUtils.defaultCivilopediaTextPath),
				BiqData.LoadFile(pacific).Prto),
			(ImportCiv3.ImportSav(sample, PathUtils.defaultBicPath, _ => PathUtils.defaultPediaIconsPath, _ => PathUtils.defaultCivilopediaTextPath),
				BiqData.LoadFile(PathUtils.defaultBicPath).Prto),
		];
		int nukes = 0, icbms = 0;
		foreach ((SaveGame save, QueryCiv3.Biq.PRTO[] prtos) in imports) {
			foreach (QueryCiv3.Biq.PRTO prto in prtos) {
				SaveUnitPrototype unit = save.UnitPrototypes.Find(u => u.name == prto.Name);
				Assert.Equal(prto.NuclearWeapon, unit.flags.Contains(SaveUnitPrototype.Flag.NuclearWeapon));
				Assert.Equal(prto.ICBM, unit.flags.Contains(SaveUnitPrototype.Flag.ICBM));
				nukes += prto.NuclearWeapon ? 1 : 0;
				icbms += prto.ICBM ? 1 : 0;
			}
		}
		Assert.True(nukes > 0 && icbms > 0);
	}
}
