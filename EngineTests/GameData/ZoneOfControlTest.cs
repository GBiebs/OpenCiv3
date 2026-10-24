using System;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Civ3's zone of control: units with the ability take a free shot at enemies
// passing through the eight tiles around them. See ZoneOfControl.
public class ZoneOfControlTest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	// A 3x3 block of empty land: the enemy stands in the middle.
	private readonly Tile middle;

	public ZoneOfControlTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		(us, them) = (ais[0], ais[1]);
		us.DeclareWarOn(them, gameData.turn);
		middle = gameData.map.tiles.First(t => IsEmptyLand(t)
			&& t.GetTilesWithinRankDistance(2).All(n => Tile.IsTileValid(n) && IsEmptyLand(n))
			&& gameData.mapUnits.All(u => u.location.DistanceTo(t) > 5));
	}

	private static bool IsEmptyLand(Tile t) {
		return Tile.IsTileValid(t) && t.IsLand() && t.unitsOnTile.Count == 0 && !t.HasCity() && !t.hasBarbarianCamp;
	}

	private MapUnit Spawn(Player owner, string type, Tile tile) {
		return gameData.SpawnUnit(owner, gameData.unitPrototypes.Find(u => u.name == type), tile);
	}

	private Tile At(TileDirection direction) => middle.neighbors[direction];

	[Fact]
	public void TheCiv3RulesetGivesZoneOfControlToItsFourteenUnits() {
		string[] expected = ["AEGIS Cruiser", "Army", "Cavalry", "Conquistador", "Cossack", "Keshik", "Mech Infantry",
			"Modern Armor", "Modern Paratrooper", "Panzer", "Paratrooper", "Radar Artillery", "Sipahi", "Tank"];
		Assert.Equal(expected, gameData.unitPrototypes.Where(u => u.zoneOfControl).Select(u => u.name).Distinct().Order());
	}

	[Fact]
	public async Task AUnitPassingByLosesAHitPoint() {
		Spawn(them, "Cavalry", middle);
		// A worker can't defend at all, so the shot always lands.
		MapUnit worker = Spawn(us, "Worker", At(TileDirection.NORTHWEST));

		Assert.True(await worker.Move(TileDirection.NORTHEAST)); // to the north of the cavalry

		Assert.Equal(At(TileDirection.NORTH), worker.location);
		Assert.Equal(2, worker.hitPointsRemaining);
	}

	[Fact]
	public async Task MovingTowardsOrAwayIsNotPassing() {
		Spawn(them, "Cavalry", middle);
		Tile twoAway = At(TileDirection.NORTHWEST).neighbors[TileDirection.NORTHWEST];
		MapUnit worker = Spawn(us, "Worker", twoAway);

		Assert.True(await worker.Move(TileDirection.SOUTHEAST)); // toward: next to the cavalry
		worker.movementPoints.reset(worker.unitType.movement);
		Assert.True(await worker.Move(TileDirection.NORTHWEST)); // away again

		Assert.Equal(twoAway, worker.location);

		Assert.Equal(3, worker.hitPointsRemaining);
	}

	[Fact]
	public async Task AUnitOnItsLastHitPointIsSpared() {
		Spawn(them, "Cavalry", middle);
		MapUnit worker = Spawn(us, "Worker", At(TileDirection.NORTHWEST));
		worker.hitPointsRemaining = 1;

		await worker.Move(TileDirection.NORTHEAST);

		Assert.Equal(1, worker.hitPointsRemaining);
		Assert.Contains(worker, gameData.mapUnits);
	}

	[Fact]
	public void OnlyEnemiesWithTheAbilityExertIt() {
		MapUnit worker = Spawn(us, "Worker", At(TileDirection.NORTHWEST));
		Spawn(them, "Spearman", middle);
		Assert.Equal(MapUnit.NONE, ZoneOfControl.Interceptor(worker, At(TileDirection.NORTHWEST), At(TileDirection.NORTH)));

		Player neutral = gameData.players.First(p => p != us && p != them && !p.isBarbarians);
		Spawn(neutral, "Cavalry", middle);
		Assert.False(PlayerRelationship.AtWar(neutral, us));
		Assert.Equal(MapUnit.NONE, ZoneOfControl.Interceptor(worker, At(TileDirection.NORTHWEST), At(TileDirection.NORTH)));
	}

	[Fact]
	public void TheStrongestUnitTakesTheShotUsingAttackOrBombard() {
		Tile from = At(TileDirection.NORTHWEST);
		Tile to = At(TileDirection.NORTH);
		MapUnit worker = Spawn(us, "Worker", from);
		Spawn(them, "Cavalry", middle); // attack 6
		Assert.Equal("Cavalry", ZoneOfControl.Interceptor(worker, from, to).unitType.name);

		// The tile north-east of the middle borders both ends of the move too.
		// Radar artillery can't attack but bombards at 16, so it shoots instead.
		Tile northEast = At(TileDirection.NORTHEAST);
		Assert.True(ZoneOfControl.PassesThrough(from, to, northEast));
		MapUnit artillery = Spawn(them, "Radar Artillery", northEast);
		Assert.True(artillery.unitType.bombard > 6);

		Assert.Equal(artillery, ZoneOfControl.Interceptor(worker, from, to));
	}

	[Fact]
	public void SeaUnitsDoNotExertItOnLandUnits() {
		MapUnit worker = Spawn(us, "Worker", At(TileDirection.NORTHWEST));
		Spawn(them, "AEGIS Cruiser", middle); // never mind that it's on land

		Assert.Equal(MapUnit.NONE, ZoneOfControl.Interceptor(worker, At(TileDirection.NORTHWEST), At(TileDirection.NORTH)));
	}

	[Fact]
	public async Task TheShotLandsWithChanceAOverSixteenDPlusA() {
		Spawn(them, "Cavalry", middle); // attack 6
		MapUnit spearman = Spawn(us, "Spearman", At(TileDirection.NORTHWEST)); // defense 2
		double chance = ZoneOfControl.HitChance(6, 2);
		Assert.Equal(6.0 / 38.0, chance, 9);

		const int trials = 4000;
		int hits = 0;
		for (int i = 0; i < trials; i++) {
			spearman.hitPointsRemaining = 3;
			if (await ZoneOfControl.Apply(spearman, At(TileDirection.NORTHWEST), At(TileDirection.NORTH)) != MapUnit.NONE) {
				hits++;
				Assert.Equal(2, spearman.hitPointsRemaining);
			}
		}
		double sigma = Math.Sqrt(trials * chance * (1 - chance));
		Assert.InRange(hits, trials * chance - 4 * sigma, trials * chance + 4 * sigma);
	}
}
