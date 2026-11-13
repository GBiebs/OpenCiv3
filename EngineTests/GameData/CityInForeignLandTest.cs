using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;
using static C7GameData.PlayerRelationship;

namespace EngineTests.GameData;

// A city may be founded inside another civ's borders; at peace, founding it
// declares war on that civ, as in Civ3 (compendium 6.10).
public class CityInForeignLandTest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly Tile theirLand;

	public CityInForeignLandTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		Player[] civs = gameData.players.Where(p => !p.isBarbarians && p.units.Any(u => u.unitType.isSettler)).Take(2).ToArray();
		us = civs[0];
		them = civs[1];
		us.EnsureRelationshipExists(them);
		them.EnsureRelationshipExists(us);
		Tile site = them.units.First(u => u.unitType.isSettler).location;
		City city = CityInteractions.BuildCity(site, them, them.GetNextCityName());
		// Two tiles from their city (one free tile between cities), in their
		// borders.
		theirLand = gameData.map.tiles.First(t => t.IsLand() && t.IsAllowCities() && t.DistanceTo(site) == 2
			&& t.unitsOnTile.Count == 0 && !t.GetTilesWithinRankDistance(1).Any(n => n != Tile.NONE && n.HasCity()));
		theirLand.owningCity = city;
	}

	private MapUnit Settler(Tile tile) {
		return gameData.SpawnUnit(us, gameData.unitPrototypes.First(p => p.isSettler), tile);
	}

	[Fact]
	public async Task FoundingACityInTheLandOfACivAtPeaceDeclaresWar() {
		MapUnit settler = Settler(theirLand);
		Assert.True(AtPeace(us, them));
		Assert.Equal(them, theirLand.PeacefulOwnerFor(us));
		Assert.True(settler.canBuildCity());

		City city = await settler.BuildCity("Outpost");

		Assert.NotNull(city);
		Assert.Equal(us, city.owner);
		Assert.True(AtWar(us, them));
		Assert.Null(theirLand.PeacefulOwnerFor(us));
	}

	[Fact]
	public async Task AtWarACityMayBeFoundedInEnemyLand() {
		us.DeclareWarOn(them, gameData.turn);
		MapUnit settler = Settler(theirLand);
		Assert.Null(theirLand.PeacefulOwnerFor(us));

		City city = await settler.BuildCity("Outpost");

		Assert.NotNull(city);
		Assert.True(AtWar(us, them));
	}

	// War with a civ we're locked in peace with can't be declared, so no
	// city may be founded in its land.
	[Fact]
	public void NotInTheLandOfALockedAlly() {
		Alliance alliance = new(0, "Allies");
		Alliance usBefore = us.alliance;
		Alliance themBefore = them.alliance;
		us.alliance = alliance;
		them.alliance = alliance;
		try {
			Assert.False(Settler(theirLand).canBuildCity());
		} finally {
			us.alliance = usBefore;
			them.alliance = themBefore;
		}
	}
}
