using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Civ3's sentry (Y, Shift-Y; Sentries): a unit sits out its turns until a
// unit comes next to it; with Y any other unit, with Shift-Y an enemy's.
public class SentryTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	// A row of three empty land tiles: the sentry, the tile next to it, and
	// one further on from which a unit steps next to the sentry.
	private readonly Tile post;
	private readonly Tile beside;
	private readonly Tile start;
	private readonly TileDirection towardPost;

	public SentryTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		(us, them) = (ais[0], ais[1]);
		us.EnsureRelationshipExists(them);

		bool Empty(Tile t) => Tile.IsTileValid(t) && t.IsLand() && t.unitsOnTile.Count == 0 && !t.HasCity()
			&& !t.hasBarbarianCamp && !t.hasGoodyHut && t.overlayTerrainType.movementCost <= 1;
		foreach (Tile t in gameData.map.tiles.Where(Empty)) {
			foreach ((TileDirection dir, Tile next) in t.neighbors) {
				if (Empty(next) && next.neighbors.TryGetValue(dir, out Tile far) && Empty(far) && far.DistanceTo(t) == 2) {
					(post, beside, start) = (t, next, far);
					towardPost = Opposite(dir);
					break;
				}
			}
			if (post != null) {
				break;
			}
		}
	}

	private static TileDirection Opposite(TileDirection dir) => (TileDirection)(((int)dir + 4) % 8);

	private MapUnit Spawn(Player owner, string type, Tile tile) => gameData.SpawnUnit(owner, gameData.unitPrototypes.Find(u => u.name == type), tile);

	private async Task Arrive(Player owner) {
		MapUnit mover = Spawn(owner, "Warrior", start);
		Assert.True(await mover.Move(towardPost));
		Assert.Same(beside, mover.location);
	}

	[Fact]
	public void ASentrySitsOutUntilWoken() {
		MapUnit sentry = Spawn(us, "Warrior", post);
		Assert.Contains(UnitAction.Sentry, sentry.GetAvailableActions());

		sentry.StandSentry(MapUnit.SentryMode.AnyUnit);
		Assert.True(sentry.IsBusy());
		Assert.DoesNotContain(UnitAction.Sentry, sentry.GetAvailableActions());
		Assert.DoesNotContain(UnitAction.SentryEnemyOnly, sentry.GetAvailableActions());
	}

	[Fact]
	public async Task AnyUnitComingNearWakesASentry() {
		MapUnit sentry = Spawn(us, "Warrior", post);
		sentry.StandSentry(MapUnit.SentryMode.AnyUnit);

		await Arrive(us);
		Assert.Equal(MapUnit.SentryMode.None, sentry.sentry);
		Assert.False(sentry.IsBusy());
	}

	[Fact]
	public async Task OnlyAnEnemyWakesASentryForEnemies() {
		MapUnit sentry = Spawn(us, "Warrior", post);
		sentry.StandSentry(MapUnit.SentryMode.EnemyOnly);

		await Arrive(us);
		Assert.Equal(MapUnit.SentryMode.EnemyOnly, sentry.sentry);
		Assert.True(us.IsAtPeaceWith(them));
		beside.unitsOnTile.ToList().ForEach(u => u.RemoveFromPlay());
		await Arrive(them);
		Assert.Equal(MapUnit.SentryMode.EnemyOnly, sentry.sentry);

		beside.unitsOnTile.ToList().ForEach(u => u.RemoveFromPlay());
		PlayerRelationship.DeclareWar(them, us, false, gameData.turn, -1);
		await Arrive(them);
		Assert.Equal(MapUnit.SentryMode.None, sentry.sentry);
	}

	[Fact]
	public async Task MovingOrOrderingTheUnitEndsItsSentry() {
		MapUnit sentry = Spawn(us, "Warrior", post);
		sentry.StandSentry(MapUnit.SentryMode.AnyUnit);
		Assert.True(await sentry.Move(Opposite(towardPost)));
		Assert.Equal(MapUnit.SentryMode.None, sentry.sentry);

		sentry.StandSentry(MapUnit.SentryMode.EnemyOnly);
		sentry.Fortify();
		Assert.Equal(MapUnit.SentryMode.None, sentry.sentry);
		Assert.True(sentry.isFortified);
	}

	[Theory]
	[InlineData(MapUnit.SentryMode.AnyUnit)]
	[InlineData(MapUnit.SentryMode.EnemyOnly)]
	public void ASentryIsSaved(MapUnit.SentryMode mode) {
		MapUnit sentry = Spawn(us, "Warrior", post);
		sentry.StandSentry(mode);

		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		Assert.Equal(mode, reloaded.GetPlayer(us.id).units.Single(u => u.id == sentry.id).sentry);
	}
}
