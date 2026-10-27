using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// The civilization traits' rules (Civilopedia "Civilization Strengths"), and
// what a city's own tile makes (https://civ-wiki.de/wiki/Stadtfeldertrag_(Civ3)).
// The city tile numbers were checked against the commerce, food and shields
// Civ3 stored for every city in the user's saves with stock rules: after these
// rules, 463 of 468 cities match in commerce and 454 in food.
public class TraitsTest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly City capital;
	private readonly City city;

	public TraitsTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		us = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		capital = CityInteractions.BuildCity(us.units[0].location, us, "Capital");
		Tile site = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity()
			&& gameData.cities.All(c => c.location.DistanceTo(t) > 4));
		city = CityInteractions.BuildCity(site, us, "Town");
		Assert.True(capital.IsCapital() && !city.IsCapital());
		// No despotism penalty, no republic bonus.
		us.government = gameData.governments.Find(g => g.name == "Monarchy");
		Traits();
		foreach (City c in new[] { capital, city }) {
			Plain(c.location, "grassland");
			foreach (Tile n in c.location.neighbors.Values) {
				Plain(n, "grassland");
			}
		}
	}

	private void Traits(params Civilization.Trait[] traits) {
		us.civilization.traits.Clear();
		us.civilization.traits.UnionWith(traits);
	}

	// Land of this type, no river, resource or bonus shield.
	private void Plain(Tile t, string key) {
		TerrainType type = gameData.terrainTypes.Find(tt => tt.Key == key);
		t.baseTerrainType = type;
		t.overlayTerrainType = type;
		t.isFreshWater = false;
		t.isBonusShield = false;
		t.Resource = Resource.NONE;
		t.riverNorth = t.riverNortheast = t.riverEast = t.riverSoutheast = false;
		t.riverSouth = t.riverSouthwest = t.riverWest = t.riverNorthwest = false;
	}

	private void Water(Tile t, bool fresh) {
		Plain(t, "coast");
		t.isFreshWater = fresh;
	}

	private void Size(City c, int size) {
		CitizenType laborer = gameData.citizenTypes.Find(ct => ct.IsDefaultCitizen);
		while (c.residents.Count < size) {
			c.AddCitizen(new CityResident { city = c, nationality = us.civilization, citizenType = laborer });
		}
	}

	private static int Commerce(City c) => c.location.CommerceYield(c).yield;
	private static int Food(City c) => c.location.FoodYield(c).yield;
	private static int Shields(City c) => c.location.ProductionYield(c).yield;

	// The city tile's commerce comes from its own formula: the road every
	// city has adds nothing (it used to add 1).
	[Fact]
	public void TheCityTilesRoadAddsNoCommerce() {
		Assert.True(city.location.HasRoad());
		Assert.Equal(1, Commerce(city));
		Size(city, 7);
		Assert.Equal(2, Commerce(city));
		Size(city, 13);
		Assert.Equal(3, Commerce(city));
		Assert.Equal(4, Commerce(capital));
	}

	// "The center city squares of all cities and metros produce extra
	// commerce": 2 at 7-12, 3 from 13. A capital takes the larger of the
	// ordinary sum and its flat 4.
	[Fact]
	public void CommercialCitiesMakeMoreCommerceOnTheirTile() {
		Traits(Civilization.Trait.Commercial);
		Assert.Equal(1, Commerce(city));
		Size(city, 7);
		Assert.Equal(4, Commerce(city));
		Size(city, 13);
		Assert.Equal(6, Commerce(city));

		Size(capital, 7);
		Assert.Equal(4, Commerce(capital));
		capital.location.riverNorth = true;
		Assert.Equal(5, Commerce(capital));

		// The rules' bonuses.
		gameData.rules.traitBonuses.commercialMetropolisCommerce = 10;
		Assert.Equal(13, Commerce(city));
		gameData.rules.traitBonuses.commercialCityCommerce = 5;
		Assert.Equal(8, Commerce(capital));
	}

	// "Each city built along the coast receives a commerce bonus in the
	// center city square": one, capitals too; a lake isn't the coast.
	[Fact]
	public void SeafaringCoastalCitiesMakeMoreCommerce() {
		Tile shore = city.location.neighbors[TileDirection.NORTH];
		Water(shore, fresh: false);
		Assert.Equal(1, Commerce(city));
		Traits(Civilization.Trait.Seafaring);
		Assert.Equal(2, Commerce(city));
		shore.isFreshWater = true;
		Assert.Equal(1, Commerce(city));

		Water(capital.location.neighbors[TileDirection.NORTH], fresh: false);
		Assert.Equal(5, Commerce(capital));
		gameData.rules.traitBonuses.seafaringCoastalCityCommerce = 2;
		Assert.Equal(6, Commerce(capital));
	}

	// "Ships move faster": one move more.
	[Fact]
	public void SeafaringShipsMoveFaster() {
		MapUnit galley = gameData.SpawnUnit(us, gameData.unitPrototypes.Find(u => u.name == "Galley"), city.location);
		Assert.Equal(galley.unitType.movement, galley.MovementPerTurn());
		Traits(Civilization.Trait.Seafaring);
		Assert.Equal(galley.unitType.movement + 1, galley.MovementPerTurn());
		// A new ship starts with all its moves.
		MapUnit another = gameData.SpawnUnit(us, galley.unitType, city.location);
		Assert.Equal(galley.unitType.movement + 1, another.movementPoints.remaining);
		gameData.rules.traitBonuses.seafaringShipMoves = 3;
		Assert.Equal(galley.unitType.movement + 3, galley.MovementPerTurn());
	}

	// The city tile makes its terrain's shields, at least one in a town, one
	// more in a city and two more in a metropolis (three for Industrious).
	[Fact]
	public void TheCityTileMakesItsTerrainsShieldsAndASizeBonus() {
		Assert.Equal(1, Shields(city));
		Size(city, 7);
		Assert.Equal(1, Shields(city));
		Size(city, 13);
		Assert.Equal(2, Shields(city));
		Traits(Civilization.Trait.Industrious);
		Assert.Equal(3, Shields(city));

		Traits();
		Plain(capital.location, "plains");
		Assert.Equal(1, Shields(capital));
		Size(capital, 7);
		Assert.Equal(2, Shields(capital));

		// A town on richer land (no stock city site is, but a mod's may be)
		// keeps its terrain's shields.
		City village = CityInteractions.BuildCity(gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand()
			&& !t.HasCity() && gameData.cities.All(c => c.location.DistanceTo(t) > 4)), us, "Village");
		Plain(village.location, "forest");
		Assert.Equal(2, Shields(village));
	}

	// "The center city square and irrigated deserts produce one more food";
	// under Despotism the city tile keeps it only with fresh water or once
	// the town is a city.
	[Fact]
	public void AgriculturalCitiesAndIrrigatedDesertsMakeMoreFood() {
		Tile desert = city.location.neighbors[TileDirection.EAST];
		Plain(desert, "desert");
		desert.overlays.Add(gameData.terrainImprovements.Find(i => i.key == "irrigation"));
		int irrigatedDesert = desert.FoodYield(city).yield;
		Assert.Equal(2, Food(city));

		Traits(Civilization.Trait.Agricultural);
		Assert.Equal(3, Food(city));
		Assert.Equal(irrigatedDesert + 1, desert.FoodYield(city).yield);

		us.government = gameData.governments.Find(g => g.name == "Despotism");
		Assert.Equal(2, Food(city));
		Assert.Equal(irrigatedDesert + 1, desert.FoodYield(city).yield);
		city.location.riverNorth = true;
		Assert.Equal(3, Food(city));
		city.location.riverNorth = false;
		Water(city.location.neighbors[TileDirection.NORTH], fresh: true);
		Assert.Equal(3, Food(city));
		Water(city.location.neighbors[TileDirection.NORTH], fresh: false);
		Assert.Equal(2, Food(city));
		Size(city, 7);
		Assert.Equal(3, Food(city));
	}

	// "Receives a free Civilization Advance at the start of every era".
	[Fact]
	public void ScientificCivsGetAnAdvanceWithEachNewEra() {
		Player other = gameData.players.First(p => p != us && !p.isBarbarians);
		foreach (Player p in new[] { us, other }) {
			p.isHuman = true;
		}
		// Nobody gets a first-to-discover bonus here.
		Player third = gameData.players.First(p => p != us && p != other && !p.isBarbarians);
		third.knownTechs.UnionWith(gameData.techs.Where(t => t.BonusTechToFirstCivThatResearches).Select(t => t.id));

		Traits(Civilization.Trait.Scientific);
		string era = us.eraCivilopediaName;
		List<Tech> firstEra = gameData.techs.Where(t => t.EraCivilopediaName == era).ToList();
		us.AcquireTechs(gameData, firstEra);
		Assert.NotEqual(era, us.eraCivilopediaName);
		Assert.Equal(1, us.freeTechsRemaining);

		other.civilization.traits.Remove(Civilization.Trait.Scientific);
		other.AcquireTechs(gameData, firstEra);
		Assert.NotEqual(era, other.eraCivilopediaName);
		Assert.Equal(0, other.freeTechsRemaining);
	}
}
