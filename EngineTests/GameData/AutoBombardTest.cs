using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Civ3's automated bombarding (see MapUnit_AutoBombard.cs): the unit keeps
// bombarding the tile it was set on, once a turn, until there's nothing
// there to hit, only ever hitting civs it is at war with. And the wording
// of a bombing run's result (script.txt's BOMBFAILED and UNITBOMBSUCEEDED).
public class AutoBombardTest : IClassFixture<SaveGameFixture>, System.IDisposable {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;

	public AutoBombardTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;

		Player[] civs = gameData.players.Where(p => !p.isBarbarians && p.units.Any(u => u.unitType.isSettler)).ToArray();
		us = civs[0];
		them = civs[1];
		us.EnsureRelationshipExists(them);
	}

	public void Dispose() {
		while (EngineStorage.TryDequeueNextMessageToUI(out _)) { }
	}

	// Rolls `sample` for every NextDouble, and 0 for every Next.
	private class FixedRandom(double sample) : System.Random {
		protected override double Sample() => sample;
		public override int Next(int maxValue) => 0;
		public override int Next(int minValue, int maxValue) => minValue;
	}

	private static async Task WithRandom(double sample, System.Func<Task> action) {
		System.Random original = C7GameData.GameData.rng;
		C7GameData.GameData.rng = new FixedRandom(sample);
		try {
			await action();
		} finally {
			C7GameData.GameData.rng = original;
		}
	}

	private MapUnit Spawn(Player owner, string prototype, Tile tile) =>
		gameData.SpawnUnit(owner, gameData.unitPrototypes.Single(p => p.name == prototype), tile);

	// Two neighboring land tiles away from everything, with no improvements.
	private (Tile, Tile) LandPair() {
		foreach (Tile t in gameData.map.tiles) {
			if (!Free(t)) continue;
			Tile n = t.neighbors.Values.FirstOrDefault(Free);
			if (n != null) return (t, n);
		}
		throw new System.Exception("No free land pair");
	}

	private static bool Free(Tile t) => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && !t.hasBarbarianCamp && !t.IsImpassable()
		&& t.unitsOnTile.Count == 0 && !t.overlays.HasBeenImproved() && t.OwningPlayer() == null;

	private void GoToWar() => us.DeclareWarOn(them, gameData.turn);

	private void NextTurn(MapUnit unit) {
		gameData.turn += 1;
		unit.OnBeginTurn();
	}

	[Fact]
	public async Task ItBombardsTheTargetOnceATurnUntilNothingIsLeft() {
		(Tile from, Tile to) = LandPair();
		MapUnit catapult = Spawn(us, "Catapult", from);
		MapUnit warrior = Spawn(them, "Warrior", to);
		MapUnit spearman = Spawn(them, "Spearman", to);
		GoToWar();

		Assert.Contains(UnitAction.AutoBombard, catapult.GetAvailableActions());
		Assert.True(catapult.CanAutoBombardTile(MapUnit.AutoBombardMode.Bombard, to));

		// Every shot hits; it fires as soon as it's set.
		await WithRandom(0.0, () => catapult.StartAutoBombard(MapUnit.AutoBombardMode.Bombard, to));
		Assert.True(catapult.IsAutoBombarding());
		Assert.True(catapult.IsBusy());
		Assert.Same(to, catapult.autoBombardTarget);
		int hurt = warrior.hitPointsRemaining + spearman.hitPointsRemaining;
		Assert.True(hurt < warrior.maxHitPoints + spearman.maxHitPoints);

		// Not again this turn.
		catapult.hasAttackedThisTurn = false;
		catapult.movementPoints.reset(catapult.MaxMovementPoints());
		await WithRandom(0.0, () => catapult.PerformBusyAction());
		Assert.Equal(hurt, warrior.hitPointsRemaining + spearman.hitPointsRemaining);

		// Next turn it fires again, by itself.
		NextTurn(catapult);
		await WithRandom(0.0, () => catapult.PerformBusyAction());
		Assert.True(warrior.hitPointsRemaining + spearman.hitPointsRemaining < hurt);

		// With nothing left there, it stops and waits for orders.
		warrior.RemoveFromPlay();
		spearman.RemoveFromPlay();
		NextTurn(catapult);
		await catapult.PerformBusyAction();
		Assert.False(catapult.IsAutoBombarding());
		Assert.True(catapult.CanBeActive());
	}

	[Fact]
	public async Task ItNeverHitsACivWeAreNotAtWarWith() {
		(Tile from, Tile to) = LandPair();
		MapUnit catapult = Spawn(us, "Catapult", from);
		MapUnit warrior = Spawn(them, "Warrior", to);

		// At peace: a plain bombardment could start a war, this can't.
		Assert.True(catapult.CanBombardTile(to));
		Assert.False(catapult.CanAutoBombardTile(MapUnit.AutoBombardMode.Bombard, to));
		await catapult.StartAutoBombard(MapUnit.AutoBombardMode.Bombard, to);
		Assert.False(catapult.IsAutoBombarding());
		Assert.Equal(warrior.maxHitPoints, warrior.hitPointsRemaining);

		// Set while at war, it stops once there's peace.
		GoToWar();
		await WithRandom(0.99, () => catapult.StartAutoBombard(MapUnit.AutoBombardMode.Bombard, to));
		Assert.True(catapult.IsAutoBombarding());
		PlayerRelationship.SignPeaceAfterWar(us, them, gameData);
		NextTurn(catapult);
		await catapult.PerformBusyAction();
		Assert.False(catapult.IsAutoBombarding());
	}

	[Fact]
	public async Task WakingOrSelectingTheUnitStopsIt() {
		(Tile from, Tile to) = LandPair();
		MapUnit catapult = Spawn(us, "Catapult", from);
		Spawn(them, "Warrior", to);
		GoToWar();

		await WithRandom(0.99, () => catapult.StartAutoBombard(MapUnit.AutoBombardMode.Bombard, to));
		Assert.True(catapult.IsAutoBombarding());
		catapult.Wake();
		Assert.False(catapult.IsAutoBombarding());

		NextTurn(catapult);
		await WithRandom(0.99, () => catapult.StartAutoBombard(MapUnit.AutoBombardMode.Bombard, to));
		catapult.Fortify();
		Assert.False(catapult.IsAutoBombarding());
	}

	[Fact]
	public async Task ItIsSavedAndOlderSavesHaveNone() {
		(Tile from, Tile to) = LandPair();
		MapUnit catapult = Spawn(us, "Catapult", from);
		Spawn(them, "Warrior", to);
		GoToWar();
		await WithRandom(0.99, () => catapult.StartAutoBombard(MapUnit.AutoBombardMode.Bombard, to));

		SaveUnit saved = new(catapult);
		Assert.Equal("autoBombard", saved.action);
		saved.id = ID.None("copy");
		MapUnit loaded = saved.ToMapUnit(gameData.unitPrototypes, gameData.experienceLevels, gameData.players, gameData.Terraforms, gameData.map);
		Assert.Equal(MapUnit.AutoBombardMode.Bombard, loaded.autoBombard);
		Assert.Same(to, loaded.autoBombardTarget);
		Assert.Equal(gameData.turn, loaded.autoBombardTurn);
		from.unitsOnTile.Remove(loaded);

		// A save from before automated bombarding.
		SaveUnit old = new(catapult) { action = "", autoBombardTarget = null, autoBombardTurn = -1 };
		old.id = ID.None("old");
		MapUnit fromOld = old.ToMapUnit(gameData.unitPrototypes, gameData.experienceLevels, gameData.players, gameData.Terraforms, gameData.map);
		Assert.False(fromOld.IsAutoBombarding());
		from.unitsOnTile.Remove(fromOld);
	}

	[Fact]
	public async Task ABomberKeepsFlyingItsMissionsWithCiv3sWording() {
		(Tile from, Tile to) = LandPair();
		City home = CityInteractions.BuildCity(from, us, us.GetNextCityName());
		MapUnit bomber = Spawn(us, "Bomber", home.location);
		MapUnit tank = Spawn(them, "Tank", to);
		GoToWar();
		while (EngineStorage.TryDequeueNextMessageToUI(out _)) { }

		Assert.Contains(UnitAction.AutoBombing, bomber.GetAvailableActions());
		Assert.DoesNotContain(UnitAction.AutoBombard, bomber.GetAvailableActions());

		// Every shot misses.
		await WithRandom(0.99, () => bomber.StartAutoBombard(MapUnit.AutoBombardMode.Bombing, to));
		Assert.Contains("Our bombing run failed.", Popups());
		// The mission's end doesn't end the order.
		Assert.Equal(MapUnit.AutoBombardMode.Bombing, bomber.autoBombard);

		NextTurn(bomber);
		bomber.BeginOwnersTurn(gameData.turn);
		await WithRandom(0.0, () => bomber.PerformBusyAction());
		Assert.True(tank.hitPointsRemaining < tank.maxHitPoints || !to.unitsOnTile.Contains(tank));
		Assert.Contains("Bombing run successful! Enemy units injured.", Popups());
	}

	private static System.Collections.Generic.List<string> Popups() {
		System.Collections.Generic.List<string> messages = new();
		while (EngineStorage.TryDequeueNextMessageToUI(out MessageToUI msg)) {
			if (msg is MsgShowTemporaryPopup popup) {
				messages.Add(popup.message);
			}
		}
		return messages;
	}
}
