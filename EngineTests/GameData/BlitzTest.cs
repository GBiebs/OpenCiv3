using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Civ3's blitz: a unit attacks (or bombards) once a turn unless its type has
// the blitz ability (Civilopedia: blitz units "can attack multiple times in a
// single turn"); it can still move with the moves it has left. Conquests:
// Tank, Panzer, Modern Armor, Cossack and the Army.
public class BlitzTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	// Three empty land tiles in a row, west to east.
	private readonly Tile a, b, c;

	public BlitzTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		// A clone, because the tests change unit types, which the game shares with the save.
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		(us, them) = (ais[0], ais[1]);
		us.DeclareWarOn(them, gameData.turn);
		a = gameData.map.tiles.First(t => IsEmptyLand(t) && gameData.mapUnits.All(u => u.location.DistanceTo(t) > 4)
			&& t.neighbors.TryGetValue(TileDirection.EAST, out Tile east) && IsEmptyLand(east)
			&& east.neighbors.TryGetValue(TileDirection.EAST, out Tile farEast) && IsEmptyLand(farEast));
		b = a.neighbors[TileDirection.EAST];
		c = b.neighbors[TileDirection.EAST];
		// A fast raider that wins every battle.
		Type("Horseman").attack = 1000;
		Type("Horseman").movement = 5;
	}

	private static bool IsEmptyLand(Tile t) {
		return Tile.IsTileValid(t) && t.IsLand() && t.unitsOnTile.Count == 0 && !t.HasCity() && !t.hasBarbarianCamp;
	}

	private UnitPrototype Type(string name) => gameData.unitPrototypes.Find(u => u.name == name);

	private MapUnit Spawn(Player owner, string type, Tile tile) {
		return gameData.SpawnUnit(owner, Type(type), tile);
	}

	[Fact]
	public void TheCiv3RulesetHasFiveBlitzUnits() {
		Assert.Equal(["Army", "Cossack", "Modern Armor", "Panzer", "Tank"],
			gameData.unitPrototypes.Where(u => u.blitz).Select(u => u.name).Distinct().Order());
	}

	[Fact]
	public async Task WithoutBlitzAUnitFightsOnceATurnButCanStillMove() {
		MapUnit raider = Spawn(us, "Horseman", a);
		Spawn(them, "Warrior", b);
		MapUnit second = Spawn(them, "Warrior", c);

		Assert.True(await raider.Move(TileDirection.EAST));
		Assert.Equal(b, raider.location);
		Assert.True(raider.movementPoints.canMove);

		Assert.False(raider.CanEnter(c, out MapUnit.Intent intent));
		Assert.Equal(MapUnit.Intent.NoticeAttacked, intent);
		await raider.Move(TileDirection.EAST);
		Assert.Equal(b, raider.location);
		Assert.Equal(3, second.hitPointsRemaining);

		// It can still move away...
		Assert.True(await raider.Move(TileDirection.WEST));
		Assert.Equal(a, raider.location);

		// ...and fight again next turn.
		raider.OnBeginTurn();
		Assert.False(raider.attackedThisTurn);
		Assert.True(raider.CanEnter(b));
	}

	[Fact]
	public async Task BlitzUnitsFightAsLongAsTheyHaveMoves() {
		Type("Horseman").blitz = true;
		MapUnit raider = Spawn(us, "Horseman", a);
		Spawn(them, "Warrior", b);
		Spawn(them, "Warrior", c);

		Assert.True(await raider.Move(TileDirection.EAST));
		Assert.True(raider.CanEnter(c, out MapUnit.Intent intent));
		Assert.Equal(MapUnit.Intent.Fight, intent);
		Assert.True(await raider.Move(TileDirection.EAST));
		Assert.Equal(c, raider.location);
		Assert.Empty(them.units.Where(u => u.location == c));
	}

	[Fact]
	public async Task TakingUndefendedUnitsIsNoBattle() {
		MapUnit raider = Spawn(us, "Horseman", a);
		Spawn(them, "Warrior", b);
		Spawn(them, "Worker", c);

		Assert.True(await raider.Move(TileDirection.EAST));
		Assert.True(raider.CanEnter(c));
		Assert.True(await raider.Move(TileDirection.EAST));
		Assert.Equal(c, raider.location);
	}

	[Fact]
	public async Task BombardingIsTheTurnsAttack() {
		Type("Catapult").movement = 3;
		MapUnit catapult = Spawn(us, "Catapult", a);
		Spawn(them, "Warrior", b);
		Assert.True(catapult.CanBombardTile(b));

		await catapult.Bombard(b);

		Assert.True(catapult.attackedThisTurn);
		Assert.True(catapult.movementPoints.canMove);
		Assert.False(catapult.CanBombardTile(b));

		Type("Catapult").blitz = true;
		Assert.True(catapult.CanBombardTile(b));
	}

	[Fact]
	public void HavingAttackedIsSaved() {
		MapUnit raider = Spawn(us, "Horseman", a);
		raider.attackedThisTurn = true;

		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);

		Assert.True(reloaded.mapUnits.Find(u => u.id == raider.id).attackedThisTurn);
		Assert.True(reloaded.unitPrototypes.Find(u => u.name == "Tank").blitz);
	}
}
