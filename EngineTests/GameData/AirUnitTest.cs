using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Aircraft fly missions from their bases (see MapUnit_Air.cs), as the
// Conquests Civilopedia's GCON_Air_Missions describes.
public class AirUnitTest : IClassFixture<SaveGameFixture>, System.IDisposable {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;

	public AirUnitTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;

		Player[] civs = gameData.players.Where(p => !p.isBarbarians && p.units.Any(u => u.unitType.isSettler)).ToArray();
		us = civs[0];
		them = civs[1];
	}

	public void Dispose() {
		while (EngineStorage.TryDequeueNextMessageToUI(out _)) { }
	}

	// Rolls `next` for every Next, and `sample` for every NextDouble.
	private class FixedRandom(int next, double sample) : System.Random {
		protected override double Sample() => sample;
		public override int Next(int maxValue) => System.Math.Min(next, maxValue - 1);
		public override int Next(int minValue, int maxValue) => System.Math.Clamp(next, minValue, maxValue - 1);
	}

	private static T WithRandom<T>(int next, double sample, System.Func<T> action) {
		System.Random original = C7GameData.GameData.rng;
		C7GameData.GameData.rng = new FixedRandom(next, sample);
		try {
			return action();
		} finally {
			C7GameData.GameData.rng = original;
		}
	}

	private static async Task<T> WithRandomAsync<T>(int next, double sample, System.Func<Task<T>> action) {
		System.Random original = C7GameData.GameData.rng;
		C7GameData.GameData.rng = new FixedRandom(next, sample);
		try {
			return await action();
		} finally {
			C7GameData.GameData.rng = original;
		}
	}

	private UnitPrototype Prototype(string name) => gameData.unitPrototypes.Single(p => p.name == name);

	private MapUnit Spawn(Player owner, string prototype, Tile tile) => gameData.SpawnUnit(owner, Prototype(prototype), tile);

	private City FoundCity(Player player, Tile tile = null) {
		tile ??= player.units.First(u => u.unitType.isSettler).location;
		return CityInteractions.BuildCity(tile, player, player.GetNextCityName());
	}

	private Tile EmptyLandAt(Tile from, int distance) {
		return gameData.map.tiles.First(t => t.IsLand() && !t.HasCity() && !t.hasBarbarianCamp && from.DistanceTo(t) == distance
			&& t.unitsOnTile.Count == 0 && !t.IsImpassable());
	}

	private void GoToWar() {
		us.EnsureRelationshipExists(them);
		us.DeclareWarOn(them, gameData.turn);
	}

	[Fact]
	public void AircraftDontMoveLikeOtherUnits() {
		City city = FoundCity(us);
		MapUnit fighter = Spawn(us, "Fighter", city.location);
		Tile next = city.location.neighbors.Values.First(t => t.IsLand());

		Assert.False(fighter.CanEnter(next));
	}

	[Fact]
	public void AircraftRebaseToOwnCitiesWithinSixTimesTheirRange() {
		City home = FoundCity(us);
		MapUnit fighter = Spawn(us, "Fighter", home.location);
		int range = fighter.RebaseRange();
		Assert.Equal(6 * Prototype("Fighter").operationalRange, range);

		City near = FoundCity(us, EmptyLandAt(home.location, 5));
		Tile farTile = gameData.map.tiles.FirstOrDefault(t => t.IsLand() && !t.HasCity() && home.location.DistanceTo(t) > range
			&& t.unitsOnTile.Count == 0);
		Tile plain = EmptyLandAt(home.location, 3);

		Assert.True(fighter.CanRebaseTo(near.location));
		Assert.False(fighter.CanRebaseTo(plain));
		if (farTile != null) {
			City far = FoundCity(us, farTile);
			Assert.False(fighter.CanRebaseTo(far.location));
		}

		fighter.Rebase(near.location);

		Assert.Same(near.location, fighter.location);
		Assert.Contains(fighter, near.location.unitsOnTile);
		Assert.DoesNotContain(fighter, home.location.unitsOnTile);
		Assert.False(fighter.movementPoints.canMove);
	}

	// J with a plane selected re-bases the planes on its tile together:
	// every one of ours that can reach the base goes, whatever its type; a
	// plane out of moves, a land unit and someone else's plane stay.
	[Fact]
	public void AStackRebaseTakesEveryPlaneOfOursThatCanGo() {
		City home = FoundCity(us);
		City near = FoundCity(us, EmptyLandAt(home.location, 5));
		MapUnit fighter = Spawn(us, "Fighter", home.location);
		MapUnit bomber = Spawn(us, "Bomber", home.location);
		MapUnit tired = Spawn(us, "Fighter", home.location);
		tired.movementPoints.onConsumeAll();
		Spawn(us, "Warrior", home.location);
		Spawn(them, "Fighter", home.location);

		Assert.Equal([fighter, bomber], UnitInteractions.StackRebasers(fighter, near.location));
		// Nowhere to land: nothing goes.
		Assert.Empty(UnitInteractions.StackRebasers(fighter, EmptyLandAt(home.location, 3)));
		// A leader that can't go takes no one.
		Assert.Empty(UnitInteractions.StackRebasers(tired, near.location));
	}

	[Fact]
	public void AircraftRebaseOntoCarriersWithRoomAndFlyFromThem() {
		City home = FoundCity(us, gameData.map.tiles.First(t => t.IsLand() && !t.HasCity() && t.unitsOnTile.Count == 0
			&& t.IsAllowCities() && t.OwningPlayer() == null
			&& gameData.map.tiles.Any(w => w.IsWater() && t.DistanceTo(w) is >= 2 and <= 6)));
		Tile sea = gameData.map.tiles.First(t => t.IsWater() && home.location.DistanceTo(t) is >= 2 and <= 6);
		MapUnit carrier = Spawn(us, "Carrier", sea);
		MapUnit fighter = Spawn(us, "Fighter", home.location);

		Assert.True(fighter.CanRebaseTo(sea));
		fighter.Rebase(sea);

		Assert.Same(sea, fighter.location);
		Assert.True(fighter.IsLoadedIn(carrier));
		fighter.OnBeginTurn();
		Assert.True(fighter.IsAtAirBase());
		Assert.True(fighter.CanFlyMission(UnitAction.Recon));

		// A full carrier takes no more.
		for (int i = carrier.FreeCapacity(); i > 0; i--) {
			Spawn(us, "Fighter", sea).loadedOnUnitId = carrier.id;
		}
		MapUnit another = Spawn(us, "Fighter", home.location);
		Assert.False(another.CanRebaseTo(sea));
	}

	[Fact]
	public void AircraftAwayFromABaseFlyNoMissions() {
		City home = FoundCity(us);
		MapUnit bomber = Spawn(us, "Bomber", EmptyLandAt(home.location, 2));
		Tile target = EmptyLandAt(home.location, 4);
		Spawn(them, "Warrior", target);
		GoToWar();

		Assert.False(bomber.IsAtAirBase());
		Assert.False(bomber.CanAirBombTile(target));
		// It can still fly home.
		Assert.Equal([UnitAction.Rebase], bomber.GetAvailableActions().Where(a => a == UnitAction.Rebase || a == UnitAction.Bombing));
		Assert.True(bomber.CanRebaseTo(home.location));
	}

	[Fact]
	public async Task BombersBombWithinTheirOperationalRange() {
		City home = FoundCity(us);
		MapUnit bomber = Spawn(us, "Bomber", home.location);
		int range = bomber.OperationalRange();
		Tile inRange = EmptyLandAt(home.location, range);
		Tile outOfRange = gameData.map.tiles.FirstOrDefault(t => t.IsLand() && !t.HasCity() && home.location.DistanceTo(t) == range + 1);
		MapUnit target = Spawn(them, "Warrior", inRange);
		if (outOfRange != null) {
			Spawn(them, "Warrior", outOfRange);
			Assert.False(bomber.CanAirBombTile(outOfRange));
		}
		GoToWar();

		Assert.True(bomber.CanAirBombTile(inRange));
		// Nobody intercepts, and every shot hits: the Bomber's bombardment
		// is lethal on land.
		bool reached = await WithRandomAsync(0, 0.0, () => bomber.AirBomb(inRange));

		Assert.True(reached);
		Assert.True(target.hitPointsRemaining <= 0 || !inRange.unitsOnTile.Contains(target));
		Assert.False(bomber.movementPoints.canMove);
		Assert.False(bomber.CanAirBombTile(inRange));
	}

	[Fact]
	public async Task FightersOnAirSuperiorityInterceptBombersOncePerTurn() {
		GoToWar();
		City ours = FoundCity(us);
		City theirs = FoundCity(them, EmptyLandAt(ours.location, 4));
		MapUnit fighter = Spawn(them, "Fighter", theirs.location);
		fighter.FlyAirSuperiority();
		Assert.True(fighter.isOnAirSuperiority);

		Tile target = EmptyLandAt(theirs.location, 1);
		Assert.True(ours.location.DistanceTo(target) <= Prototype("Bomber").operationalRange);
		MapUnit defender = Spawn(them, "Warrior", target);
		MapUnit bomber = Spawn(us, "Bomber", ours.location);

		// The fighter tries (0 < 50) and, firing 0 against its chance,
		// shoots the bomber down. The run never reaches the target.
		bool reached = await WithRandomAsync(0, 0.0, () => bomber.AirBomb(target));

		Assert.False(reached);
		Assert.DoesNotContain(bomber, ours.location.unitsOnTile);
		Assert.Equal(defender.maxHitPoints, defender.hitPointsRemaining);
		Assert.True(fighter.hasInterceptedThisTurn);
		// One interception takes it off the mission, as in the base game
		// (C3X, patch_Unit_set_state_after_interception).
		Assert.False(fighter.isOnAirSuperiority);

		// Its interception for the turn is spent.
		MapUnit second = Spawn(us, "Bomber", ours.location);
		Assert.True(await WithRandomAsync(0, 0.0, () => second.AirBomb(target)));

		// The stance ends when its owner's turn begins.
		gameData.turn += 1;
		TurnHandling.BeginPlayersTurn(them, gameData);
		Assert.False(fighter.isOnAirSuperiority);
		Assert.False(fighter.hasInterceptedThisTurn);
	}

	// A fighter set on air superiority intercepts from the moment it is
	// set, through the whole interturn: the rest of the round, the round's
	// end, and the turns of the civs that move before its owner in the next
	// round. There is no lost turn (owner's decision, 2026-10-21).
	[Fact]
	public async Task AFighterSetOnAirSuperiorityInterceptsBeforeItsOwnersNextTurn() {
		GoToWar();
		City ours = FoundCity(us);
		City theirs = FoundCity(them, EmptyLandAt(ours.location, 4));
		MapUnit fighter = Spawn(them, "Fighter", theirs.location);
		Tile target = EmptyLandAt(theirs.location, 1);
		Spawn(them, "Warrior", target);
		MapUnit bomber = Spawn(us, "Bomber", ours.location);

		// Set this turn; then the round ends and the next round's other
		// civs begin their turns, while its owner's hasn't come yet.
		fighter.FlyAirSuperiority();
		TurnHandling.InitTurnData();
		gameData.turn += 1;
		TurnHandling.BeginPlayersTurn(us, gameData);
		Assert.True(fighter.isOnAirSuperiority);

		Assert.False(await WithRandomAsync(0, 0.0, () => bomber.AirBomb(target)));
		Assert.True(fighter.hasInterceptedThisTurn);

		// A stance set on the owner's current turn survives its turn
		// beginning again, as when a save made that turn is loaded.
		MapUnit second = Spawn(them, "Fighter", theirs.location);
		second.FlyAirSuperiority();
		TurnHandling.BeginPlayersTurn(them, gameData);
		Assert.True(second.isOnAirSuperiority);
		gameData.turn += 1;
		TurnHandling.BeginPlayersTurn(them, gameData);
		Assert.False(second.isOnAirSuperiority);
	}

	[Fact]
	public async Task AnInterceptedBomberThatSurvivesStillFailsItsRun() {
		GoToWar();
		City ours = FoundCity(us);
		City theirs = FoundCity(them, EmptyLandAt(ours.location, 4));
		MapUnit fighter = Spawn(them, "Fighter", theirs.location);
		fighter.FlyAirSuperiority();
		Tile target = EmptyLandAt(theirs.location, 1);
		MapUnit defender = Spawn(them, "Warrior", target);
		MapUnit bomber = Spawn(us, "Bomber", ours.location);
		Assert.True(bomber.CanAirBombTile(target));

		bool reached = await WithRandomAsync(0, 0.999, () => bomber.AirBomb(target));

		Assert.False(reached);
		Assert.Contains(bomber, ours.location.unitsOnTile);
		Assert.False(bomber.movementPoints.canMove);
		Assert.Equal(defender.maxHitPoints, defender.hitPointsRemaining);
		// The bomber won the fight, so the fighter went down: "If no,
		// Fighter will crash'n'burn" (tjs282,
		// https://forums.civfanatics.com/threads/air-battle-questions-only.565813/, #9).
		Assert.DoesNotContain(fighter, them.units);
		Assert.True(fighter.hitPointsRemaining <= 0);
	}

	// Rolls 0 for every Next, and the samples in turn, over and over, for
	// NextDouble.
	private class CyclingRandom(params double[] samples) : System.Random {
		private int i;
		protected override double Sample() => samples[i++ % samples.Length];
		public override int Next(int maxValue) => 0;
		public override int Next(int minValue, int maxValue) => minValue;
	}

	// An interception is a normal battle to the death, one hit point a
	// round, so the winner may come out of it damaged: "If yes, Bomber will
	// crash'n'burn (Fighter may be damaged)" (tjs282, #9).
	[Fact]
	public async Task AnInterceptionIsFoughtToTheDeath() {
		GoToWar();
		City ours = FoundCity(us);
		City theirs = FoundCity(them, EmptyLandAt(ours.location, 4));
		MapUnit fighter = Spawn(them, "Fighter", theirs.location);
		fighter.FlyAirSuperiority();
		Tile target = EmptyLandAt(theirs.location, 1);
		Spawn(them, "Warrior", target);
		MapUnit bomber = Spawn(us, "Bomber", ours.location);
		Assert.Equal(3, fighter.hitPointsRemaining);
		Assert.Equal(3, bomber.hitPointsRemaining);

		// The fighter (A4 against D2: 2 in 3) wins the rounds rolled 0 and
		// loses those rolled 0.9: bomber, fighter, bomber, fighter, bomber.
		System.Random original = C7GameData.GameData.rng;
		C7GameData.GameData.rng = new CyclingRandom(0.0, 0.9);
		try {
			Assert.False(await bomber.AirBomb(target));
		} finally {
			C7GameData.GameData.rng = original;
		}

		Assert.DoesNotContain(bomber, us.units);
		Assert.Contains(fighter, them.units);
		Assert.Equal(1, fighter.hitPointsRemaining);
	}

	[Fact]
	public void StealthAircraftAreInterceptedOnlyWithTheStealthChance() {
		GoToWar();
		City ours = FoundCity(us);
		City theirs = FoundCity(them, EmptyLandAt(ours.location, 3));
		MapUnit fighter = Spawn(them, "Fighter", theirs.location);
		fighter.FlyAirSuperiority();
		Tile target = EmptyLandAt(theirs.location, 1);

		MapUnit stealth = Spawn(us, "Stealth Bomber", ours.location);
		MapUnit plain = Spawn(us, "Bomber", ours.location);

		// A roll of 10: under the 50% for air missions, not the 5% for
		// stealth ones.
		Assert.False(WithRandom(10, 0.999, () => stealth.TryToIntercept(target, out _)));
		Assert.True(WithRandom(10, 0.999, () => plain.TryToIntercept(target, out _)));
	}

	[Fact]
	public async Task SamBatteriesFireAtAircraftBombingTheirCity() {
		GoToWar();
		City ours = FoundCity(us);
		City theirs = FoundCity(them, EmptyLandAt(ours.location, 4));
		Building sam = gameData.Buildings.Single(b => b.name == "SAM Missile Battery");
		Assert.Equal(8, sam.airPower);
		theirs.AddBuilding(sam);
		MapUnit bomber = Spawn(us, "Bomber", ours.location);
		MapUnit stealth = Spawn(us, "Stealth Bomber", ours.location);
		Assert.True(bomber.CanAirBombTile(theirs.location));

		// A roll of 10 fires at the bomber (under 50%) but not at the
		// stealth bomber (5%); 0.9 is over the SAM's 8/(8+2) to hit, so the
		// bomber survives but fails its run.
		Assert.True(WithRandom(10, 0.9, () => bomber.TryToIntercept(theirs.location, out bool down) && !down));
		Assert.False(WithRandom(10, 0.9, () => stealth.TryToIntercept(theirs.location, out _)));

		// Under 0.8 it's shot down.
		bool reached = await WithRandomAsync(0, 0.5, () => bomber.AirBomb(theirs.location));
		Assert.False(reached);
		Assert.DoesNotContain(bomber, us.units);
	}

	[Fact]
	public void ReconRevealsTheTargetAndItsNeighbours() {
		City home = FoundCity(us);
		MapUnit fighter = Spawn(us, "Fighter", home.location);
		Tile target = EmptyLandAt(home.location, fighter.OperationalRange());
		foreach (Tile t in target.neighbors.Values.Append(target)) {
			us.tileKnowledge.knownTiles.Remove(t);
		}

		Assert.True(fighter.CanReconTile(target));
		fighter.Recon(target);

		Assert.True(us.tileKnowledge.isTileKnown(target));
		Assert.True(us.tileKnowledge.isActiveTile(target));
		Assert.All(target.neighbors.Values.Where(Tile.IsTileValid), t => Assert.True(us.tileKnowledge.isTileKnown(t)));
		Assert.False(fighter.movementPoints.canMove);
	}

	[Fact]
	public async Task PrecisionStrikesNeedSmartWeaponsAndHitOnlyBuildings() {
		City ours = FoundCity(us);
		City theirs = FoundCity(them, EmptyLandAt(ours.location, 4));
		while (theirs.residents.Count < 4) {
			theirs.AddCitizen(new CityResident() {
				city = theirs,
				citizenType = theirs.residents[0].citizenType,
				tileWorked = theirs.residents[0].tileWorked,
			});
		}
		theirs.AddBuilding(gameData.Buildings.Single(b => b.name == "Granary"));
		MapUnit stealth = Spawn(us, "Stealth Fighter", ours.location);
		GoToWar();

		Assert.False(stealth.CanPrecisionBombTile(theirs.location));
		Tech smartWeapons = gameData.techs.First(t => t.HasFlag(SaveTech.Flag.EnablesPrecisionBombing));
		us.knownTechs.Add(smartWeapons.id);
		Assert.True(stealth.CanPrecisionBombTile(theirs.location));

		bool reached = await WithRandomAsync(99, 0.0, () => stealth.PrecisionBomb(theirs.location));

		Assert.True(reached);
		Assert.Equal(4, theirs.residents.Count);
		Assert.DoesNotContain(theirs.constructed_buildings, cb => cb.building.name == "Granary");
	}

	[Fact]
	public void ParatroopersDropFromCitiesWithAnAirport() {
		City home = FoundCity(us);
		MapUnit para = Spawn(us, "Paratrooper", home.location);
		Tile target = EmptyLandAt(home.location, para.OperationalRange());

		Assert.False(para.CanAirdropTo(target));
		home.AddBuilding(gameData.Buildings.Single(b => b.name == "Airport"));
		Assert.True(para.CanAirdropTo(target));
		Spawn(them, "Warrior", target);
		Assert.False(para.CanAirdropTo(target));
		Tile other = EmptyLandAt(home.location, 2);

		para.Airdrop(other);

		Assert.Same(other, para.location);
		Assert.False(para.movementPoints.canMove);
	}

	[Fact]
	public async Task TheAIBombsEnemiesNearItsCitiesOnlyAtWar() {
		City theirs = FoundCity(them);
		MapUnit bomber = Spawn(them, "Bomber", theirs.location);
		Tile near = EmptyLandAt(theirs.location, 2);
		MapUnit intruder = Spawn(us, "Warrior", near);

		// At peace nothing is bombed.
		us.EnsureRelationshipExists(them);
		await AirAI.PlayTurn(them, bomber);
		Assert.True(bomber.movementPoints.canMove);

		GoToWar();
		Assert.Same(near, AirAI.ChooseBombingTarget(them, bomber));
		await WithRandomAsync(0, 0.0, async () => { await AirAI.PlayTurn(them, bomber); return 0; });

		Assert.False(bomber.movementPoints.canMove);
		Assert.True(intruder.hitPointsRemaining <= 0 || !near.unitsOnTile.Contains(intruder));
	}

	[Fact]
	public async Task TheAIFliesAirSuperiorityOverThreatenedCities() {
		City theirs = FoundCity(them);
		MapUnit fighter = Spawn(them, "Fighter", theirs.location);
		Spawn(us, "Warrior", EmptyLandAt(theirs.location, 2));
		GoToWar();

		await AirAI.PlayTurn(them, fighter);

		Assert.True(fighter.isOnAirSuperiority);
	}

	[Fact]
	public void AirSuperiorityIsSaved() {
		City home = FoundCity(us);
		MapUnit fighter = Spawn(us, "Fighter", home.location);
		fighter.FlyAirSuperiority();
		fighter.hasInterceptedThisTurn = true;

		fighter.airSuperiorityTurn = 7;
		SaveUnit saved = new(fighter);

		Assert.True(saved.isOnAirSuperiority);
		Assert.True(saved.hasInterceptedThisTurn);
		Assert.Equal(7, saved.airSuperiorityTurn);
	}
}
