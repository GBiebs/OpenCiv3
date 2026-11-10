using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Civ3's automated bombarding (Shift-B; AutoBombard): once a turn at the
// best target in range of a civ at war with us, never one at peace.
public class AutoBombardTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly Tile ours;
	private readonly Tile theirs;

	public AutoBombardTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		(us, them) = (ais[0], ais[1]);
		us.EnsureRelationshipExists(them);

		bool Empty(Tile t) => Tile.IsTileValid(t) && t.IsLand() && t.unitsOnTile.Count == 0 && !t.HasCity() && !t.hasBarbarianCamp && !t.hasGoodyHut;
		ours = gameData.map.tiles.First(t => Empty(t) && t.neighbors.Values.Any(Empty));
		theirs = ours.neighbors.Values.First(Empty);
	}

	private MapUnit Spawn(Player owner, string type, Tile tile) => gameData.SpawnUnit(owner, gameData.unitPrototypes.Find(u => u.name == type), tile);

	[Fact]
	public async Task ItBombardsAnEnemyInRangeOnceATurn() {
		MapUnit catapult = Spawn(us, "Catapult", ours);
		MapUnit enemy = Spawn(them, "Warrior", theirs);
		PlayerRelationship.DeclareWar(us, them, false, gameData.turn, -1);
		us.tileKnowledge.AddTilesToKnown(theirs);

		Assert.True(AutoBombard.CanAutomate(catapult, precision: false));
		AutoBombard.Start(catapult, precision: false);
		Assert.Equal(MapUnit.AutoBombardMode.Bombard, catapult.autoBombard);
		Assert.True(catapult.IsBusy());
		Assert.Same(theirs, AutoBombard.Target(gameData, catapult));

		await catapult.PerformBusyAction();
		Assert.True(catapult.attackedThisTurn);
		Assert.Equal(gameData.turn, catapult.autoBombardTurn);
		// Still on the order, for the next turn.
		Assert.Equal(MapUnit.AutoBombardMode.Bombard, catapult.autoBombard);
	}

	[Fact]
	public void ACivAtPeaceIsLeftAlone() {
		MapUnit catapult = Spawn(us, "Catapult", ours);
		Spawn(them, "Warrior", theirs);
		us.tileKnowledge.AddTilesToKnown(theirs);
		Assert.True(us.IsAtPeaceWith(them));

		AutoBombard.Start(catapult, precision: false);
		Assert.Null(AutoBombard.Target(gameData, catapult));
	}

	[Fact]
	public void TheOrderIsSavedAndEndsWhenWoken() {
		MapUnit catapult = Spawn(us, "Catapult", ours);
		AutoBombard.Start(catapult, precision: false);

		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		Assert.Equal(MapUnit.AutoBombardMode.Bombard, reloaded.GetPlayer(us.id).units.Single(u => u.id == catapult.id).autoBombard);

		catapult.Wake();
		Assert.Equal(MapUnit.AutoBombardMode.None, catapult.autoBombard);
		Assert.False(AutoBombard.CanAutomate(catapult, precision: true));
	}
}
