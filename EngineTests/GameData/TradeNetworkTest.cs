using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Civilopedia "Trade Route Connections": two cities are connected by a road
// or railroad between them, by "harbors in both cities and a visible water
// route between them" that the civ can sail safely (Map Making, Navigation,
// Magnetism), or by airports in both; "not connected if the only road/rail
// that connects them passes through enemy territory, or if a harbor is
// blockaded by enemy naval forces".
public class TradeNetworkTest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly City capital;

	public TradeNetworkTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		us = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		them = gameData.players.First(p => p != us && !p.isHuman && !p.isBarbarians);
		capital = CityInteractions.BuildCity(us.units[0].location, us, "Capital");
	}

	private bool Connected(City town) => gameData.GetTradeNetwork().ConnectedToCapital(us, town);

	private Building Building(string name) => gameData.Buildings.Find(b => b.name == name);

	private void SetTerrain(Tile t, string key) {
		TerrainType type = gameData.terrainTypes.Find(tt => tt.Key == key);
		t.baseTerrainType = type;
		t.overlayTerrainType = type;
		gameData.InvalidateCachedTradeNetwork();
	}

	private Tile FarSite() => gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity()
		&& !t.hasBarbarianCamp && gameData.cities.All(c => c.location.DistanceTo(t) > 6));

	// A town five tiles east of the capital, and the four tiles between them
	// turned into a channel of coast, with land all round it.
	private (City town, List<Tile> channel) Channel() {
		List<Tile> channel = [];
		Tile tile = capital.location;
		for (int i = 0; i < 4; i++) {
			tile = tile.neighbors[TileDirection.EAST];
			channel.Add(tile);
		}
		Tile site = tile.neighbors[TileDirection.EAST];
		Assert.True(Tile.IsTileValid(site) && !site.HasCity(), "the fixture's capital needs room to its east");
		foreach (Tile t in channel) {
			SetTerrain(t, "coast");
		}
		foreach (Tile t in channel.Append(capital.location).Append(site)) {
			foreach (Tile n in t.neighbors.Values.Where(n => n.IsWater() && !channel.Contains(n))) {
				SetTerrain(n, "grassland");
			}
		}
		if (site.IsWater()) {
			SetTerrain(site, "grassland");
		}
		City town = CityInteractions.BuildCity(site, us, "Port");
		foreach (Tile t in channel) {
			us.tileKnowledge.AddTilesToKnown(t);
		}
		return (town, channel);
	}

	private (City town, List<Tile> channel) Harbors() {
		(City town, List<Tile> channel) = Channel();
		Assert.False(Connected(town));
		capital.AddBuilding(Building("Harbor"), gameData);
		Assert.False(Connected(town));
		town.AddBuilding(Building("Harbor"), gameData);
		return (town, channel);
	}

	private Tech WithFlag(SaveTech.Flag flag) => gameData.techs.First(t => t.flags.Contains(flag));

	[Fact]
	public void HarborsOnTheSameWaterTrade() {
		(City town, List<Tile> channel) = Channel();
		// Wine on a road beside the town.
		Resource wines = gameData.Resources.Find(r => r.Name == "Wines");
		Tile vineyard = town.location.neighbors[TileDirection.EAST];
		Assert.Equal(us, vineyard.OwningPlayer());
		vineyard.Resource = wines;
		vineyard.overlays.Add(gameData.terrainImprovements.Find(i => i.key == "road"));
		Assert.DoesNotContain(wines, capital.GetLuxuries(gameData).Keys);

		capital.AddBuilding(Building("Harbor"), gameData);
		town.AddBuilding(Building("Harbor"), gameData);
		Assert.True(Connected(town));
		Assert.Contains(wines, capital.GetLuxuries(gameData).Keys);
	}

	// Coast at once; sea with Astronomy, ocean with Navigation (or
	// Magnetism); only over water we've seen.
	[Fact]
	public void TheRouteMustBeSailableAndSeen() {
		(City town, List<Tile> channel) = Harbors();

		SetTerrain(channel[2], "sea");
		Assert.False(Connected(town));
		us.AcquireTechs(gameData, [WithFlag(SaveTech.Flag.EnablesTradeOverSea)]);
		Assert.True(Connected(town));

		SetTerrain(channel[2], "ocean");
		Assert.False(Connected(town));
		us.AcquireTechs(gameData, [WithFlag(SaveTech.Flag.EnablesTradeOverOcean)]);
		Assert.True(Connected(town));

		us.tileKnowledge.knownTiles.Remove(channel[1]);
		gameData.InvalidateCachedTradeNetwork();
		Assert.False(Connected(town));
	}

	[Fact]
	public void EnemyWatersAndShipsCutTheRoute() {
		(City town, List<Tile> channel) = Harbors();
		City theirs = CityInteractions.BuildCity(FarSite(), them, "Theirs");
		channel[2].owningCity = theirs;
		us.EnsureRelationshipExists(them);
		gameData.InvalidateCachedTradeNetwork();
		Assert.True(Connected(town));

		PlayerRelationship.DeclareWar(us, them, false, gameData.turn, -1);
		Assert.False(Connected(town));
		PlayerRelationship.SignPeaceAfterWar(us, them, gameData);
		Assert.True(Connected(town));

		// A blockade: their galley next to the town, at war.
		channel[2].owningCity = null;
		gameData.SpawnUnit(them, gameData.unitPrototypes.Find(u => u.name == "Galley"), channel[3]);
		gameData.InvalidateCachedTradeNetwork();
		Assert.True(Connected(town));
		PlayerRelationship.DeclareWar(us, them, false, gameData.turn, -1);
		Assert.False(Connected(town));
	}

	[Fact]
	public void AirportsTradeAnywhere() {
		City town = CityInteractions.BuildCity(FarSite(), us, "Faraway");
		capital.AddBuilding(Building("Airport"), gameData);
		Assert.False(Connected(town));
		town.AddBuilding(Building("Airport"), gameData);
		Assert.True(Connected(town));

		town.RemoveBuilding(town.constructed_buildings.Find(cb => cb.building.name == "Airport"));
		Assert.False(Connected(town));
	}

	[Fact]
	public void RoadsThroughEnemyTerritoryDontCount() {
		List<Tile> between = [];
		Tile tile = capital.location;
		for (int i = 0; i < 3; i++) {
			tile = tile.neighbors[TileDirection.EAST];
			between.Add(tile);
		}
		Tile site = tile.neighbors[TileDirection.EAST];
		Assert.True(between.Append(site).All(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity()), "the fixture's capital needs land to its east");
		City town = CityInteractions.BuildCity(site, us, "Town");
		TerrainImprovement road = gameData.terrainImprovements.Find(i => i.key == "road");
		foreach (Tile t in between) {
			t.overlays.Add(road);
		}
		City theirs = CityInteractions.BuildCity(FarSite(), them, "Theirs");
		between[1].owningCity = theirs;
		us.EnsureRelationshipExists(them);
		gameData.InvalidateCachedTradeNetwork();
		Assert.True(Connected(town));

		PlayerRelationship.DeclareWar(us, them, false, gameData.turn, -1);
		Assert.False(Connected(town));
	}
}
