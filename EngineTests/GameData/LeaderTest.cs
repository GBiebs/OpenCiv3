using System;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Civ3's military great leaders (CivFanatics, "Creating and Using Leaders"):
// an elite unit that beats another civ's unit has a one in 16 chance of
// producing one (one in 12 with the Heroic Epic, half when defending), each
// unit only once and a player one leader at a time; a leader can hurry an
// improvement or small wonder in its city. See MapUnit_Leader.cs.
public class LeaderTest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly Tile from;
	private readonly Tile to;

	public LeaderTest(SaveGameFixture fixture) {
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
		Type("Swordsman").attack = 1000;
		Type("Swordsman").defense = 1000;
	}

	private static bool IsEmptyLand(Tile t) {
		return Tile.IsTileValid(t) && t.IsLand() && t.unitsOnTile.Count == 0 && !t.HasCity() && !t.hasBarbarianCamp;
	}

	private UnitPrototype Type(string name) => gameData.unitPrototypes.Find(u => u.name == name);

	private MapUnit Spawn(Player owner, string type, Tile tile) {
		return gameData.SpawnUnit(owner, Type(type), tile);
	}

	private MapUnit Elite(Player owner, string type, Tile tile) {
		MapUnit unit = Spawn(owner, type, tile);
		unit.experienceLevel = gameData.experienceLevels.Last();
		unit.experienceLevelKey = unit.experienceLevel.key;
		unit.hitPointsRemaining = unit.maxHitPoints;
		return unit;
	}

	private MapUnit OurLeader() => us.units.FirstOrDefault(u => u.unitType.isLeader);

	// Has `hero` beat a fresh warrior of `enemy`'s, attacking or defending.
	private async Task Win(MapUnit hero, bool attacking, Player enemy) {
		hero.hitPointsRemaining = hero.maxHitPoints;
		MapUnit warrior = Spawn(enemy, "Warrior", to);
		CombatResult result = attacking ? await hero.Fight(warrior) : await warrior.Fight(hero);
		Assert.Equal(attacking ? CombatResult.DefenderKilled : CombatResult.AttackerKilled, result);
	}

	// Wins `trials` battles with `hero`, clearing each leader it produces so
	// that the next battle can produce another. Returns how many it produced.
	private async Task<int> LeadersFromWins(MapUnit hero, int trials, bool attacking = true, Player enemy = null) {
		int leaders = 0;
		for (int i = 0; i < trials; i++) {
			await Win(hero, attacking, enemy ?? them);
			if (OurLeader() is MapUnit leader) {
				leaders++;
				leader.RemoveFromPlay();
				hero.hasProducedLeader = false;
			}
		}
		return leaders;
	}

	private async Task<MapUnit> WinUntilLeader(MapUnit hero) {
		for (int i = 0; i < 1000; i++) {
			await Win(hero, attacking: true, them);
			if (OurLeader() is MapUnit leader) {
				return leader;
			}
		}
		throw new Exception("No leader in 1000 elite wins");
	}

	private static void AssertNear(int observed, int trials, double chance) {
		double sigma = Math.Sqrt(trials * chance * (1 - chance));
		Assert.InRange(observed, trials * chance - 4 * sigma, trials * chance + 4 * sigma);
	}

	[Fact]
	public async Task AnEliteUnitWinningAnAttackProducesALeaderOnceInSixteen() {
		const int trials = 3200;
		int leaders = await LeadersFromWins(Elite(us, "Swordsman", from), trials);

		AssertNear(leaders, trials, 1.0 / 16);
		Assert.Equal(leaders, EngineStorage.messagesToUI.OfType<MsgLeaderBorn>().Count());
	}

	[Fact]
	public async Task DefendingHalvesTheChance() {
		const int trials = 3200;
		int leaders = await LeadersFromWins(Elite(us, "Swordsman", from), trials, attacking: false);

		AssertNear(leaders, trials, 1.0 / 32);
	}

	[Fact]
	public async Task TheHeroicEpicRaisesItToOneInTwelve() {
		MapUnit hero = Elite(us, "Swordsman", from);
		Assert.Equal((16, 32), (hero.LeaderChanceOneIn(attacking: true), hero.LeaderChanceOneIn(attacking: false)));
		gameData.rules.DefendingLeaderChanceFactor = 3;
		Assert.Equal(48, hero.LeaderChanceOneIn(attacking: false));
		gameData.rules.DefendingLeaderChanceFactor = 2;

		City city = CityInteractions.BuildCity(us.units[0].location, us, "Epic City");
		city.AddBuilding(gameData.Buildings.Find(b => b.name == "Heroic Epic"), gameData);
		Assert.Equal((12, 24), (hero.LeaderChanceOneIn(attacking: true), hero.LeaderChanceOneIn(attacking: false)));

		const int trials = 3000;
		int leaders = await LeadersFromWins(hero, trials);

		AssertNear(leaders, trials, 1.0 / 12);
	}

	[Fact]
	public async Task OnlyEliteUnitsProduceLeaders() {
		MapUnit veteran = Spawn(us, "Swordsman", from);
		ExperienceLevel veteranLevel = gameData.experienceLevels[^2];

		for (int i = 0; i < 300; i++) {
			veteran.experienceLevel = veteranLevel;
			veteran.experienceLevelKey = veteranLevel.key;
			await Win(veteran, attacking: true, them);
		}

		Assert.Null(OurLeader());
	}

	[Fact]
	public async Task FightingBarbariansProducesNoLeaders() {
		Player barbarians = gameData.players.Find(p => p.isBarbarians);

		Assert.Equal(0, await LeadersFromWins(Elite(us, "Swordsman", from), 300, enemy: barbarians));
	}

	[Fact]
	public async Task EachUnitProducesOneLeaderAndAPlayerHasOneAtATime() {
		MapUnit hero = Elite(us, "Swordsman", from);
		MapUnit leader = await WinUntilLeader(hero);
		Assert.True(hero.hasProducedLeader);

		// No second leader while the first lives, even from another elite.
		MapUnit other = Elite(us, "Swordsman", from);
		for (int i = 0; i < 300; i++) {
			await Win(other, attacking: true, them);
		}
		Assert.Equal([leader], us.units.Where(u => u.unitType.isLeader));

		// Once it's gone, the hero still can't produce another...
		leader.RemoveFromPlay();
		for (int i = 0; i < 300; i++) {
			await Win(hero, attacking: true, them);
		}
		Assert.Null(OurLeader());

		// ...but the other elite can.
		Assert.NotNull(await WinUntilLeader(other));
		Assert.True(other.hasProducedLeader);
	}

	[Fact]
	public async Task TheLeaderAppearsWithTheWinnerNamedFromItsCivsList() {
		MapUnit first = await WinUntilLeader(Elite(us, "Swordsman", from));

		Assert.Equal(us, first.owner);
		Assert.Equal(from, first.location);
		Assert.Equal(us.civilization.greatLeaderNames[0], first.name);
		Assert.Equal(1, us.leadersCreated);
		MsgLeaderBorn message = Assert.Single(EngineStorage.messagesToUI.OfType<MsgLeaderBorn>());
		Assert.Equal((us.id, from, first.name), (message.playerId, message.location, message.leaderName));

		first.RemoveFromPlay();
		MapUnit second = await WinUntilLeader(Elite(us, "Swordsman", from));
		Assert.Equal(us.civilization.greatLeaderNames[1], second.name);
	}

	[Fact]
	public void LeaderNamesStartOverAndCivsWithoutNamesUseTheUnitTypesName() {
		UnitPrototype leader = Type("Leader");
		us.civilization.greatLeaderNames = ["Alpha", "Beta"];

		Assert.Equal(["Alpha", "Beta", "Alpha"], Enumerable.Range(0, 3).Select(_ => us.NextGreatLeaderName(leader)));

		us.civilization.greatLeaderNames = [];
		Assert.Equal("Leader", us.NextGreatLeaderName(leader));
	}

	[Fact]
	public void ALeaderHurriesAnImprovementAndIsUsedUp() {
		City city = CityInteractions.BuildCity(us.units[0].location, us, "Rushed");
		Building temple = gameData.Buildings.Find(b => b.name == "Temple");
		city.SetItemBeingProduced(temple);
		MapUnit leader = Spawn(us, "Leader", city.location);

		Assert.Contains(UnitAction.HurryProduction, leader.GetAvailableActions());
		Assert.Equal(city, leader.HurryProduction());

		Assert.Equal(us.ShieldCost(temple), city.shieldsStored);
		Assert.DoesNotContain(leader, us.units);
		Assert.DoesNotContain(leader, city.location.unitsOnTile);

		city.HandleCityProduction(gameData);
		Assert.Contains(city.constructed_buildings, cb => cb.building == temple);
	}

	[Fact]
	public void LeadersHurrySmallWondersButNotGreatWondersOrUnits() {
		City city = CityInteractions.BuildCity(us.units[0].location, us, "Rushed");
		MapUnit leader = Spawn(us, "Leader", city.location);

		city.SetItemBeingProduced(gameData.Buildings.Find(b => b.isSmallWonder));
		Assert.True(leader.CanHurryProduction());

		city.SetItemBeingProduced(gameData.Buildings.Find(b => b.IsGreatWonder()));
		Assert.False(leader.CanHurryProduction());
		Assert.Null(leader.HurryProduction());
		Assert.Contains(leader, us.units);

		city.SetItemBeingProduced(Type("Warrior"));
		Assert.False(leader.CanHurryProduction());
		Assert.DoesNotContain(UnitAction.HurryProduction, leader.GetAvailableActions());
	}

	[Fact]
	public void LeadersHurryOnlyInOurOwnCitiesWithMovesLeftAndSomethingToFinish() {
		Building temple = gameData.Buildings.Find(b => b.name == "Temple");
		City ours = CityInteractions.BuildCity(us.units[0].location, us, "Ours");
		ours.SetItemBeingProduced(temple);
		City theirs = CityInteractions.BuildCity(them.units[0].location, them, "Theirs");
		theirs.SetItemBeingProduced(temple);

		Assert.False(Spawn(us, "Leader", from).CanHurryProduction());
		Assert.False(Spawn(us, "Leader", theirs.location).CanHurryProduction());
		Assert.False(Spawn(us, "Warrior", ours.location).CanHurryProduction());

		MapUnit tired = Spawn(us, "Leader", ours.location);
		tired.movementPoints.onConsumeAll();
		Assert.False(tired.CanHurryProduction());

		MapUnit leader = Spawn(us, "Leader", ours.location);
		Assert.True(leader.CanHurryProduction());
		ours.SetStoredShields(us.ShieldCost(temple));
		Assert.False(leader.CanHurryProduction());
	}
}
