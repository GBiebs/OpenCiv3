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

// Air units (Civilopedia "Air Missions"): missions from a base within the
// operational range; interception by enemies on air superiority within half
// their range; anti-aircraft fire; re-basing to cities and carriers within
// six times the range; airdrops and airlifts; cruise missiles are spent.
public class AirTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly City home;
	private readonly City abroad;

	public AirTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		(us, them) = (ais[0], ais[1]);
		home = CityInteractions.BuildCity(us.units.First(u => u.unitType.isSettler).location, us, "Home");
		abroad = CityInteractions.BuildCity(them.units.First(u => u.unitType.isSettler).location, them, "Abroad");
		us.DeclareWarOn(them, gameData.turn);
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private UnitPrototype Type(string name) => gameData.unitPrototypes.Find(u => u.name == name);

	private MapUnit Spawn(Player owner, string type, Tile tile) => gameData.SpawnUnit(owner, Type(type), tile);

	private Tile LandAt(Tile from, int distance) {
		return gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && t.unitsOnTile.Count == 0
			&& t.DistanceTo(from) == distance && !t.hasGoodyHut && !t.hasBarbarianCamp);
	}

	private Building BuildingNamed(string name) => gameData.Buildings.Find(b => b.name == name);

	// ---------------------------------------------------------------- missions

	[Fact]
	public void MissionsReachTheOperationalRange() {
		MapUnit fighter = Spawn(us, "Fighter", home.location);
		Assert.Equal(6, fighter.unitType.operationalRange);
		Tile near = LandAt(home.location, 6);
		Tile far = LandAt(home.location, 7);
		Spawn(them, "Warrior", near);
		Spawn(them, "Warrior", far);

		Assert.True(Air.CanFly(fighter, Air.Mission.Bombing, near));
		Assert.False(Air.CanFly(fighter, Air.Mission.Bombing, far));
		Assert.True(Air.CanFly(fighter, Air.Mission.Recon, far.neighbors.Values.First(t => t.DistanceTo(home.location) == 6)));
		Assert.False(Air.CanFly(fighter, Air.Mission.Recon, far));

		// Not at peace; not without the order; not without moves.
		PlayerRelationship.SignPeaceAfterWar(us, them, gameData);
		Assert.False(Air.CanFly(fighter, Air.Mission.Bombing, near));
		us.DeclareWarOn(them, gameData.turn);
		Assert.False(Air.CanFly(Spawn(us, "Bomber", home.location), Air.Mission.Recon, near));
		fighter.movementPoints.onConsumeAll();
		Assert.False(Air.CanFly(fighter, Air.Mission.Bombing, near));
	}

	[Fact]
	public async Task ABomberBombsAndItsTurnIsOver() {
		MapUnit bomber = Spawn(us, "Bomber", home.location);
		Tile target = LandAt(home.location, 5);
		MapUnit tank = Spawn(them, "Tank", target);
		bomber.unitType.bombard = 1000;

		Assert.Equal(Air.Outcome.Done, await Air.Fly(gameData, bomber, Air.Mission.Bombing, target));
		Assert.True(tank.hitPointsRemaining < tank.maxHitPoints || !them.units.Contains(tank));
		Assert.False(bomber.movementPoints.canMove);
		Assert.Equal(home.location, bomber.location);
		Assert.Single(EngineStorage.messagesToUI.OfType<MsgAirMission>(), m => m.outcome == Air.Outcome.Done);
	}

	[Fact]
	public async Task ReconRevealsAroundTheTarget() {
		MapUnit fighter = Spawn(us, "Fighter", home.location);
		// A mission takes the whole turn, however many moves are left.
		fighter.movementPoints.reset(3);
		Tile target = LandAt(home.location, 6);
		Tile edge = target.GetTilesWithinTileSquare(2).First(t => Tile.IsTileValid(t) && t.DistanceTo(target) == 2 && !us.tileKnowledge.isTileKnown(t));
		await Air.Fly(gameData, fighter, Air.Mission.Recon, target);
		Assert.True(us.tileKnowledge.isTileKnown(target));
		Assert.True(us.tileKnowledge.isTileKnown(edge));
		Assert.False(fighter.movementPoints.canMove);
	}

	[Fact]
	public async Task APrecisionStrikeHitsABuilding() {
		City target = CityInteractions.BuildCity(LandAt(home.location, 8), them, "Target");
		target.AddBuilding(BuildingNamed("Temple"), gameData);
		target.AddBuilding(BuildingNamed("Granary"), gameData);
		target.AddBuilding(BuildingNamed("The Pyramids"), gameData);
		MapUnit stealth = Spawn(us, "Stealth Bomber", home.location);
		stealth.unitType.bombard = 100000;
		Assert.True(Air.CanFly(stealth, Air.Mission.PrecisionStrike, target.location));
		Assert.False(Air.CanFly(Spawn(us, "Bomber", home.location), Air.Mission.PrecisionStrike, target.location));
		Assert.False(Air.CanFly(stealth, Air.Mission.PrecisionStrike, LandAt(home.location, 8)));

		await Air.Fly(gameData, stealth, Air.Mission.PrecisionStrike, target.location);
		Assert.Single(target.constructed_buildings, b => b.building.name is "Temple" or "Granary");
		Assert.Contains(target.GetBuildings(), b => b.building.name == "The Pyramids");

		// Wonders are spared: with none but the Pyramids left, a citizen dies.
		foreach (CityBuilding b in target.constructed_buildings.Where(b => b.building.name != "The Pyramids").ToList()) {
			target.RemoveBuilding(b);
		}
		CitizenType laborer = gameData.citizenTypes.Find(c => c.IsDefaultCitizen);
		while (target.residents.Count < 3) {
			target.AddCitizen(new CityResident { city = target, nationality = them.civilization, citizenType = laborer });
		}
		stealth.movementPoints.reset(1);
		Assert.Equal(Air.Outcome.Done, await Air.Fly(gameData, stealth, Air.Mission.PrecisionStrike, target.location));
		Assert.Contains(target.GetBuildings(), b => b.building.name == "The Pyramids");
		Assert.Equal(2, target.residents.Count);
	}

	// ---------------------------------------------------------------- interception and anti-aircraft fire

	[Fact]
	public async Task FightersOnAirSuperiorityIntercept() {
		MapUnit bomber = Spawn(us, "Bomber", home.location);
		Tile target = LandAt(home.location, 5);
		Spawn(them, "Warrior", target);
		MapUnit fighter = Spawn(them, "Jet Fighter", LandAt(target, 4));
		Assert.Equal(Air.Outcome.Done, await Air.Fly(gameData, fighter, Air.Mission.AirSuperiority));
		Assert.True(fighter.airSuperiority);
		Assert.True(fighter.IsBusy());

		them.rules.air.interceptPercent = 100;
		us.rules.air.interceptPercent = 100;
		// Within half its range (9 / 2 = 4).
		Assert.Equal(fighter, Air.Interceptor(gameData, bomber, target));
		Assert.Equal(Air.Outcome.Intercepted, await Air.Fly(gameData, bomber, Air.Mission.Bombing, target));
		Assert.True(them.units.Single(u => u.unitType.name == "Warrior").hitPointsRemaining == 3);

		// Further out, or at no chance, it doesn't.
		MapUnit another = Spawn(us, "Bomber", home.location);
		gameData.TeleportUnit(fighter, LandAt(target, 5));
		Assert.Null(Air.Interceptor(gameData, another, target));
		gameData.TeleportUnit(fighter, LandAt(target, 3));
		us.rules.air.interceptPercent = 0;
		Assert.Null(Air.Interceptor(gameData, another, target));

		// Stealth has its own chance.
		us.rules.air.interceptPercent = 100;
		us.rules.air.stealthInterceptPercent = 0;
		Assert.Null(Air.Interceptor(gameData, Spawn(us, "Stealth Bomber", home.location), target));
	}

	[Fact]
	public async Task AntiAircraftFireShootsPlanesDown() {
		MapUnit bomber = Spawn(us, "Bomber", home.location);
		Tile target = LandAt(home.location, 5);
		Spawn(them, "Mobile SAM", target);
		Assert.Equal(4, Air.AntiAir(bomber, target));
		Assert.Equal(20, Air.ShootDownPercent(gameData, bomber, target));

		// A city's SAM battery: 8 × 5% = 40%; a stealth plane a tenth.
		abroad.AddBuilding(BuildingNamed("SAM Missile Battery"), gameData);
		Assert.Equal(40, Air.ShootDownPercent(gameData, bomber, abroad.location));
		Assert.Equal(4, Air.ShootDownPercent(gameData, Spawn(us, "Stealth Bomber", home.location), abroad.location));

		us.rules.air.antiAirPercentPerPoint = 25;
		Assert.Equal(Air.Outcome.ShotDown, await Air.Fly(gameData, bomber, Air.Mission.Bombing, target));
		Assert.DoesNotContain(bomber, us.units);
	}

	// ---------------------------------------------------------------- bases

	[Fact]
	public async Task PlanesRebaseToCitiesAndCarriers() {
		MapUnit fighter = Spawn(us, "Fighter", home.location);
		City second = CityInteractions.BuildCity(LandAt(home.location, 30), us, "Far");
		Assert.True(Air.CanFly(fighter, Air.Mission.Rebase, second.location));
		Assert.False(Air.CanFly(fighter, Air.Mission.Rebase, LandAt(home.location, 3)));
		Assert.False(Air.CanFly(fighter, Air.Mission.Rebase, abroad.location));
		us.rules.air.rebaseRangeFactor = 4;
		Assert.False(Air.CanFly(fighter, Air.Mission.Rebase, second.location));
		us.rules.air.rebaseRangeFactor = 6;

		await Air.Fly(gameData, fighter, Air.Mission.Rebase, second.location);
		Assert.Equal(second.location, fighter.location);
		Assert.False(fighter.movementPoints.canMove);

		// Onto a carrier at sea, which then carries it.
		Tile sea = gameData.map.tiles.First(t => t.IsWater() && t.unitsOnTile.Count == 0 && t.DistanceTo(second.location) <= 10
			&& t.neighbors.Values.Any(n => n.IsWater() && n.unitsOnTile.Count == 0));
		MapUnit carrier = Spawn(us, "Carrier", sea);
		fighter.movementPoints.reset(1);
		Assert.True(Air.CanFly(fighter, Air.Mission.Rebase, sea));
		await Air.Fly(gameData, fighter, Air.Mission.Rebase, sea);
		Assert.True(fighter.IsLoadedIn(carrier));
		Tile next = sea.neighbors.Values.First(n => n.IsWater() && n.unitsOnTile.Count == 0);
		await carrier.Move(sea.DirectionTo(next));
		Assert.Equal(next, fighter.location);
	}

	[Fact]
	public void AirSuperiorityIsSaved() {
		MapUnit fighter = Spawn(us, "Fighter", home.location);
		fighter.airSuperiority = true;
		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		Assert.True(reloaded.GetUnit(fighter.id).airSuperiority);
		fighter.Wake();
		Assert.False(fighter.airSuperiority);
	}

	// ---------------------------------------------------------------- airdrops, airlifts, missiles

	[Fact]
	public void ParatroopersDropWithinTheirRange() {
		MapUnit para = Spawn(us, "Paratrooper", home.location);
		Tile near = LandAt(home.location, 6);
		Assert.True(Air.CanAirdrop(para, near));
		Assert.False(Air.CanAirdrop(para, LandAt(home.location, 7)));
		Spawn(them, "Warrior", near);
		Assert.False(Air.CanAirdrop(para, near));
		Tile empty = LandAt(home.location, 4);
		Assert.True(Air.Airdrop(gameData, para, empty));
		Assert.Equal(empty, para.location);
		Assert.False(para.movementPoints.canMove);
		// Only from a city.
		para.movementPoints.reset(1);
		Assert.False(Air.CanAirdrop(para, LandAt(empty, 2)));
	}

	[Fact]
	public void AirportsAirliftOneUnitATurn() {
		City second = CityInteractions.BuildCity(LandAt(home.location, 20), us, "Far");
		MapUnit a = Spawn(us, "Rifleman", home.location);
		MapUnit b = Spawn(us, "Rifleman", home.location);
		Assert.False(Air.CanAirlift(gameData, a, second.location));
		home.AddBuilding(BuildingNamed("Airport"), gameData);
		second.AddBuilding(BuildingNamed("Airport"), gameData);
		Assert.True(Air.Airlift(gameData, a, second.location));
		Assert.Equal(second.location, a.location);
		Assert.False(Air.CanAirlift(gameData, b, second.location));
		gameData.turn++;
		Assert.True(Air.CanAirlift(gameData, b, second.location));
	}

	[Fact]
	public async Task ACruiseMissileIsSpent() {
		MapUnit missile = Spawn(us, "Cruise Missile", home.location);
		Tile target = LandAt(home.location, 3);
		Spawn(them, "Warrior", target);
		Assert.True(missile.CanBombardTile(target));
		await missile.Bombard(target);
		Assert.DoesNotContain(missile, us.units);
	}

	// ---------------------------------------------------------------- the AI

	[Fact]
	public async Task TheAIBombsWhatThreatensIt() {
		MapUnit bomber = Spawn(us, "Bomber", home.location);
		Tile threat = home.location.neighbors.Values.First(t => t.IsLand() && !t.HasCity() && t.unitsOnTile.Count == 0);
		MapUnit tank = Spawn(them, "Tank", threat);
		us.tileKnowledge.AddTilesToKnown(threat);
		Assert.Equal(threat, AirAI.BombingTarget(gameData, bomber, us));
		bomber.unitType.bombard = 1000;
		await AirAI.Act(us, gameData);
		Assert.False(bomber.movementPoints.canMove);
		Assert.True(tank.hitPointsRemaining < tank.maxHitPoints || !them.units.Contains(tank));
	}

	[Fact]
	public async Task TheAIFightersGuardWhenThereIsNothingToBomb() {
		MapUnit fighter = Spawn(us, "Fighter", home.location);
		await AirAI.Act(us, gameData);
		Assert.True(fighter.airSuperiority);
	}

	[Fact]
	public void TheAIMovesBombersToTheFront() {
		MapUnit bomber = Spawn(us, "Bomber", home.location);
		Tile nearer = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && t.unitsOnTile.Count == 0
			&& t.DistanceTo(abroad.location) < home.location.DistanceTo(abroad.location) - 3 && t.DistanceTo(home.location) <= 30
			&& gameData.cities.All(c => c.location.DistanceTo(t) > 3));
		City front = CityInteractions.BuildCity(nearer, us, "Front");
		// A city between them doesn't do.
		Tile between = gameData.map.tiles.FirstOrDefault(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && t.unitsOnTile.Count == 0
			&& t.DistanceTo(abroad.location) > nearer.DistanceTo(abroad.location) && t.DistanceTo(abroad.location) < home.location.DistanceTo(abroad.location)
			&& gameData.cities.All(c => c.location.DistanceTo(t) > 3));
		if (between != null) {
			CityInteractions.BuildCity(between, us, "Between");
		}
		Assert.Equal(front.location, AirAI.FrontBase(bomber, us, [them]));
	}
}

// Air units' data from Civ3's PRTO and RULE.
public class AirImportTest {
	[SkippableFact]
	public void TheAirDataComesFromTheBiq() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), "No Civ3 install found.");
		string scenario = Path.Combine(Civ3Location.GetCiv3Path(), "Conquests", "Conquests", "9 WWII in the Pacific.biq");
		SaveGame save = ImportCiv3.ImportBiq(scenario, PathUtils.defaultBicPath,
			_ => PathUtils.defaultPediaIconsPath, _ => PathUtils.defaultCivilopediaTextPath);
		BiqData biq = BiqData.LoadFile(scenario);
		foreach (QueryCiv3.Biq.PRTO prto in biq.Prto) {
			SaveUnitPrototype unit = save.UnitPrototypes.Find(u => u.name == prto.Name);
			Assert.Equal(prto.OperationalRange, unit.operationalRange);
			Assert.Equal(prto.AirDefense, unit.airDefense);
			Assert.Equal(prto.Bombing, unit.actions.Contains(UnitAction.Bombing));
			Assert.Equal(prto.Intercept, unit.actions.Contains(UnitAction.AirSuperiority));
			Assert.Equal(prto.Rebase, unit.actions.Contains(UnitAction.Rebase));
			Assert.Equal(prto.Airdrop, unit.actions.Contains(UnitAction.Airdrop));
		}
		Assert.Contains(biq.Prto, p => p.Bombing && p.OperationalRange > 0);
		QueryCiv3.Biq.RULE rule = (biq.Rule ?? BiqData.LoadFile(PathUtils.defaultBicPath).Rule)[0];
		Assert.Equal(rule.ChanceToInterceptEnemyAirMissions, save.Rules.air.interceptPercent);
		Assert.Equal(rule.ChanceToInterceptEnemyStealthMissions, save.Rules.air.stealthInterceptPercent);
	}
}
