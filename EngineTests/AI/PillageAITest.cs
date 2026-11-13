using System;
using System.Threading.Tasks;
using C7Engine;
using C7Engine.AI;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.AI;

// The AI pillages its enemies' land in wartime with its units' leftover
// moves (see PillageAI).
public class PillageAITest : MapBase, IDisposable {
	private readonly C7GameData.GameData gameData = new();
	private readonly Player us = MakeCiv("player-1");
	private readonly Player them = MakeCiv("player-2");
	private readonly TerrainImprovement road = new(Tile.TileOverlays.ROAD, TerrainImprovement.Layer.Roads, movementCost: 1.0f / 3);
	private readonly TerrainImprovement mine = new(Tile.TileOverlays.MINE, TerrainImprovement.Layer.ResourceDevelopment);

	// A row of plains running east: the first is ours, the rest theirs.
	private readonly Tile[] row = new Tile[4];
	private readonly bool fogBefore = EngineStorage.aiFogOfWar;

	public PillageAITest() {
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.aiFogOfWar = false;
		gameData.players.Add(us);
		gameData.players.Add(them);
		us.EnsureRelationshipExists(them);
		them.EnsureRelationshipExists(us);

		InitilizeStartTile(MakePlainsTile(), new TileLocation(50, 50));
		row[0] = startTile;
		for (int i = 1; i < row.Length; ++i) {
			row[i] = AddNeighborsAndUpdateMap(row[i - 1], MakePlainsTile(), TileDirection.EAST);
			AddNeighborsAndUpdateMap(row[i], row[i - 1], TileDirection.WEST);
		}
		row[0].owningCity = new City(Tile.NONE, us, "Ours", ID.None("city"));
		City theirs = new(Tile.NONE, them, "Theirs", ID.None("city"));
		for (int i = 1; i < row.Length; ++i) {
			row[i].owningCity = theirs;
			row[i].overlays.Add(road);
			row[i].overlays.Add(mine);
		}
		row[0].overlays.Add(road);
	}

	public void Dispose() {
		EngineStorage.aiFogOfWar = fogBefore;
	}

	private static Player MakeCiv(string id) {
		return new Player() { id = ID.FromString(id), civilization = new Civilization(), government = new Government() };
	}

	private MapUnit MakeUnit(Player owner, Tile location, int movement = 2) {
		MapUnit unit = new(ID.None("unit")) {
			owner = owner,
			nationality = owner.civilization,
			unitType = new UnitPrototype() { name = "Swordsman", attack = 3, defense = 2, movement = movement },
			experienceLevel = new ExperienceLevel("regular", "Regular", 3, 0, 0),
			location = location,
		};
		unit.unitType.categories.Add("Land");
		unit.unitType.actions.Add(UnitAction.Pillage);
		unit.hitPointsRemaining = unit.maxHitPoints;
		unit.movementPoints.reset(movement);
		location.unitsOnTile.Add(unit);
		owner.units.Add(unit);
		return unit;
	}

	private bool IsBare(Tile tile) => !tile.overlays.HasImprovement(road) && !tile.overlays.HasImprovement(mine);

	[Fact]
	public async Task AtWarAUnitWithMovesLeftPillagesEnemyLand() {
		MapUnit raider = MakeUnit(us, row[1]);
		us.DeclareWarOn(them, gameData.turn);

		Assert.Equal(1, await PillageAI.PlayTurn(us, gameData));
		Assert.True(IsBare(row[1]));
		Assert.True(raider.hasPillagedThisTurn);
	}

	[Fact]
	public async Task NotAtPeace() {
		MakeUnit(us, row[1]);

		Assert.Equal(0, await PillageAI.PlayTurn(us, gameData));
		Assert.False(IsBare(row[1]));
	}

	[Fact]
	public async Task NotOurOwnLand() {
		MakeUnit(us, row[0]);
		us.DeclareWarOn(them, gameData.turn);

		Assert.Equal(0, await PillageAI.PlayTurn(us, gameData));
		Assert.True(row[0].overlays.HasImprovement(road));
	}

	// A wounded unit that hasn't moved rests: acting would cost it its
	// healing.
	[Fact]
	public async Task NotAWoundedUnitThatIsResting() {
		MapUnit raider = MakeUnit(us, row[1]);
		raider.hitPointsRemaining = 1;
		us.DeclareWarOn(them, gameData.turn);

		Assert.Equal(0, await PillageAI.PlayTurn(us, gameData));
	}

	[Fact]
	public async Task AWoundedUnitThatHasMovedPillages() {
		MapUnit raider = MakeUnit(us, row[1]);
		raider.hitPointsRemaining = 1;
		raider.movementPoints.onUnitMove(1);
		us.DeclareWarOn(them, gameData.turn);

		Assert.Equal(1, await PillageAI.PlayTurn(us, gameData));
	}

	// With enemies about, too: its moves were spent anyway, and it
	// fortifies again.
	[Fact]
	public async Task EvenWithAnEnemyNextToIt() {
		MakeUnit(us, row[1]);
		MakeUnit(them, row[2]);
		us.DeclareWarOn(them, gameData.turn);

		Assert.Equal(1, await PillageAI.PlayTurn(us, gameData));
	}

	[Fact]
	public async Task NotWithNoMovesLeft() {
		MapUnit raider = MakeUnit(us, row[1]);
		raider.movementPoints.onConsumeAll();
		us.DeclareWarOn(them, gameData.turn);

		Assert.Equal(0, await PillageAI.PlayTurn(us, gameData));
	}

	[Fact]
	public async Task AHumansUnitsAreLeftAlone() {
		us.isHuman = true;
		MakeUnit(us, row[1]);
		us.DeclareWarOn(them, gameData.turn);

		Assert.Equal(0, await PillageAI.PlayTurn(us, gameData));
	}

	[Fact]
	public async Task AFortifiedUnitFortifiesAgain() {
		MapUnit raider = MakeUnit(us, row[1]);
		raider.Fortify();
		us.DeclareWarOn(them, gameData.turn);

		Assert.Equal(1, await PillageAI.PlayTurn(us, gameData));
		Assert.True(raider.isFortified);
	}

	// A unit on bare land would step onto a next improved tile to pillage
	// it, if it still has a move to pillage with. (The step itself is
	// MapUnit.Move.)
	[Fact]
	public void AUnitOnBareLandRaidsANextImprovedTile() {
		row[1].overlays.Remove(road);
		row[1].overlays.Remove(mine);
		MapUnit raider = MakeUnit(us, row[1], movement: 2);
		us.DeclareWarOn(them, gameData.turn);

		Assert.Same(row[2], PillageAI.RaidTarget(raider, us));
	}

	[Fact]
	public void NotAtPeaceEither() {
		row[1].overlays.Remove(road);
		row[1].overlays.Remove(mine);
		MapUnit raider = MakeUnit(us, row[1], movement: 2);

		Assert.Null(PillageAI.RaidTarget(raider, us));
	}

	[Fact]
	public void NotWithTooFewMovesToPillageAfterTheStep() {
		row[1].overlays.Remove(road);
		row[1].overlays.Remove(mine);
		MapUnit raider = MakeUnit(us, row[1], movement: 1);
		us.DeclareWarOn(them, gameData.turn);

		Assert.Null(PillageAI.RaidTarget(raider, us));
	}

	[Fact]
	public void NotNextToAnEnemyThatCouldAttack() {
		row[1].overlays.Remove(road);
		row[1].overlays.Remove(mine);
		MapUnit raider = MakeUnit(us, row[1], movement: 2);
		MakeUnit(them, row[3]);
		us.DeclareWarOn(them, gameData.turn);

		Assert.Null(PillageAI.RaidTarget(raider, us));
	}

	// Ours already there make it safe enough.
	[Fact]
	public void UnlessOursAreThereAlready() {
		row[1].overlays.Remove(road);
		row[1].overlays.Remove(mine);
		MapUnit raider = MakeUnit(us, row[1], movement: 2);
		MakeUnit(us, row[2]).movementPoints.onConsumeAll();
		MakeUnit(them, row[3]);
		us.DeclareWarOn(them, gameData.turn);

		Assert.Same(row[2], PillageAI.RaidTarget(raider, us));
	}

	// A road comes before a mine.
	[Fact]
	public void RoadsComeFirst() {
		row[1].overlays.Remove(road);
		row[1].overlays.Remove(mine);
		row[2].overlays.Remove(road);
		Tile north = AddNeighborsAndUpdateMap(row[1], MakePlainsTile(), TileDirection.NORTH);
		AddNeighborsAndUpdateMap(north, row[1], TileDirection.SOUTH);
		north.owningCity = row[2].owningCity;
		north.overlays.Add(road);
		MapUnit raider = MakeUnit(us, row[1], movement: 2);
		us.DeclareWarOn(them, gameData.turn);

		Assert.Same(north, PillageAI.RaidTarget(raider, us));
	}
}
