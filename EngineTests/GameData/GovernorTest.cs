using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7Engine.AI;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Civ3's city governor for a human's cities: it places new citizens by
// TimBentley's tile values (CivFanatics, "Explanation of the governor"),
// with emphasis on food, shields or commerce; it can keep a city out of
// disorder with entertainers, and choose what it builds.
public class GovernorTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly City city;

	public GovernorTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		us = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		us.isHuman = true;
		city = CityInteractions.BuildCity(us.units[0].location, us, "Seat");
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private CitizenType Laborer => gameData.citizenTypes.Find(c => c.IsDefaultCitizen);

	private CityResident Grow() {
		CityResident resident = new() { city = city, nationality = us.civilization, citizenType = Laborer };
		city.AddCitizen(resident);
		C7Engine.AI.CityTileAssignmentAI.AssignNewCitizenToTile(gameData, resident);
		return resident;
	}

	private (int food, int shields, int commerce) Yields(Tile t) {
		return (t.FoodYield(us).yield, t.ProductionYield(us).yield, t.CommerceYield(us).yield);
	}

	private static void Make(Tile tile, TerrainType terrain, TerrainType overlay = null) {
		tile.baseTerrainType = terrain;
		tile.overlayTerrainType = overlay ?? terrain;
		tile.Resource = Resource.NONE;
		tile.ResourceKey = Resource.NONE.Key;
		tile.overlays.Clear();
	}

	// The article's values: hungry (surplus under 4), a food is 4 shields
	// and a shield 4 gold, food past the surplus wanted only a shield; fed,
	// a shield is 4 food and a food 4 gold; emphasis multiplies by 4.
	[Fact]
	public void TilesAreValuedAsTheArticleMeasured() {
		// A forest, not of the city tile's terrain, whose food the test changes.
		Make(city.location, gameData.terrainTypes.Find(t => t.Key == "grassland"));
		Tile tile = city.GetWorkableTiles().First(t => t.personWorkingTile == null && t.IsLand() && !t.HasCity());
		Make(tile, gameData.terrainTypes.Find(t => t.Key == "plains"), gameData.terrainTypes.Find(t => t.Key == "forest"));
		(int f, int s, int c) = Yields(tile);
		Assert.True(f > 0 && s > 0);
		CityGovernor governor = city.governor;
		// A new citizen, counted but not yet working: the surplus before it
		// eats.
		city.AddCitizen(new CityResident { city = city, nationality = us.civilization, citizenType = Laborer });
		int Surplus() => city.FoodGrowthPerTurn() + 2;
		double Value() => governor.TileValue(city, tile, gameData.rules);

		// Hungry: the city tile makes little food.
		TerrainType cityTerrain = city.location.overlayTerrainType;
		int cityFood = cityTerrain.baseFoodProduction;
		try {
			cityTerrain.baseFoodProduction = 0;
			Assert.True(Surplus() < 4);
			int needed = Math.Clamp(4 - Surplus(), 0, f);
			Assert.Equal(needed * 16 + (f - needed) * 4 + s * 4 + c, Value(), 9);
			governor.emphasizeFood = true;
			Assert.Equal(f * 64 + s * 4 + c, Value(), 9);
			governor.emphasizeFood = false;
			governor.emphasizeCommerce = true;
			Assert.Equal(needed * 16 + (f - needed) * 4 + s * 4 + c * 4, Value(), 9);
			governor.emphasizeCommerce = false;

			// Fed: plenty from the city tile.
			cityTerrain.baseFoodProduction = 12;
			Assert.True(Surplus() >= 4);
			Assert.Equal(f * 4 + s * 16 + c, Value(), 9);
			governor.emphasizeShields = true;
			Assert.Equal(f * 4 + s * 64 + c, Value(), 9);
			// All three cancel out.
			governor.emphasizeFood = governor.emphasizeCommerce = true;
			Assert.Equal(f * 4 + s * 16 + c, Value(), 9);
		} finally {
			cityTerrain.baseFoodProduction = cityFood;
		}
	}

	// Hungry, food past the 4 wanted counts only as a shield.
	[Fact]
	public void FoodPastTheSurplusWantedIsWorthAShield() {
		us.government = gameData.governments.Find(g => g.name == "Monarchy");
		Make(city.location, gameData.terrainTypes.Find(t => t.Key == "grassland"));
		Tile farm = city.GetWorkableTiles().First(t => t.personWorkingTile == null && t.IsLand() && !t.HasCity());
		Make(farm, gameData.terrainTypes.Find(t => t.Key == "plains"));
		farm.overlays.Add(gameData.terrainImprovements.Find(i => i.key == "irrigation"));
		(int f, int s, int c) = Yields(farm);
		Assert.True(f >= 2);
		city.AddCitizen(new CityResident { city = city, nationality = us.civilization, citizenType = Laborer });
		TerrainType grassland = city.location.overlayTerrainType;
		int food = grassland.baseFoodProduction;
		try {
			for (grassland.baseFoodProduction = 0; city.FoodGrowthPerTurn() + 2 < 3 && grassland.baseFoodProduction < 20; ++grassland.baseFoodProduction) {
			}
			Assert.Equal(3, city.FoodGrowthPerTurn() + 2);
			// One food wanted, the rest at a shield's worth.
			Assert.Equal(1 * 16 + (f - 1) * 4 + s * 4 + c, city.governor.TileValue(city, farm, gameData.rules), 9);
			city.governor.emphasizeFood = true;
			Assert.Equal(f * 64 + s * 4 + c, city.governor.TileValue(city, farm, gameData.rules), 9);
		} finally {
			grassland.baseFoodProduction = food;
		}
	}

	[Fact]
	public void ANewCitizenGoesWhereTheGovernorValuesMost() {
		List<Tile> free = city.GetWorkableTiles().Where(t => t.personWorkingTile == null && t.IsLand() && !t.HasCity()).Take(2).ToList();
		Make(free[0], gameData.terrainTypes.Find(t => t.Key == "grassland"));
		Make(free[1], gameData.terrainTypes.Find(t => t.Key == "grassland"), gameData.terrainTypes.Find(t => t.Key == "forest"));
		// Nothing else to work, and the first citizen's tile makes nothing:
		// the city is short of food.
		foreach (Tile other in city.GetWorkableTiles().Where(t => !free.Contains(t))) {
			other.overlays.Add(gameData.terrainImprovements.Find(i => i.key == Tile.TileOverlays.POLLUTION));
		}
		CityResident hungry = Grow();
		Assert.Contains(hungry.tileWorked, free);
		Tile pickedHungry = hungry.tileWorked;
		Assert.Equal(Yields(free[0]).food >= Yields(free[1]).food ? free[0] : free[1], pickedHungry);

		// Emphasizing production takes the hills instead.
		hungry.tileWorked.personWorkingTile = null;
		hungry.tileWorked = Tile.NONE;
		city.RemoveCitizen(hungry);
		city.governor.emphasizeShields = true;
		CityResident worker = Grow();
		Assert.Equal(free[1], worker.tileWorked);
	}

	[Fact]
	public void ManagingMoodsKeepsTheCityOutOfDisorderWithFewEntertainers() {
		// Borders wide enough for every citizen to work a tile.
		city.perPlayerCulture[us] = 1000;
		gameData.UpdateTileOwners();
		us.luxuryRate = 0;
		for (int i = 0; i < 11; i++) {
			Grow();
		}
		Assert.Equal(City.Mood.Unhappy, city.RecalculateCitizenMoods(gameData));
		city.governor.manageMoods = true;
		city.governor.ManageMoods(city, gameData);
		Assert.NotEqual(City.Mood.Unhappy, city.RecalculateCitizenMoods(gameData));
		int entertainers = city.residents.Count(r => r.citizenType != Laborer);
		Assert.True(entertainers > 0);

		// With luxuries, fewer will do.
		us.luxuryRate = 10;
		us.scienceRate = us.taxRate = 0;
		city.governor.ManageMoods(city, gameData);
		Assert.NotEqual(City.Mood.Unhappy, city.RecalculateCitizenMoods(gameData));
		Assert.True(city.residents.Count(r => r.citizenType != Laborer) < entertainers);
	}

	[Fact]
	public async Task AtTheTurnsEndTheGovernorHeadsOffDisorder() {
		Player human = await CreateGame.createGame(fixture.saveGame.Clone(), (_) => fixture.behaviors);
		C7GameData.GameData game = EngineStorage.gameData;
		TurnHandling.OnBeginTurn();
		TurnHandling.InitTurnData();
		await TurnHandling.AdvanceTurn();
		City managed = CityInteractions.BuildCity(human.units.First(u => u.unitType.actions.Contains(UnitAction.BuildCity)).location, human, "Calm");
		managed.perPlayerCulture[human] = 1000;
		game.UpdateTileOwners();
		human.luxuryRate = 0;
		CitizenType laborer = game.citizenTypes.Find(c => c.IsDefaultCitizen);
		while (managed.residents.Count < 12) {
			CityResident r = new() { city = managed, nationality = human.civilization, citizenType = laborer };
			managed.AddCitizen(r);
			C7Engine.AI.CityTileAssignmentAI.AssignNewCitizenToTile(game, r);
		}
		// Set directly, so only the turn's end can calm the city.
		managed.governor.manageMoods = true;
		Assert.Equal(City.Mood.Unhappy, managed.RecalculateCitizenMoods(game));

		TurnHandling.OnEndTurn(human);
		human.hasPlayedThisTurn = true;
		await TurnHandling.AdvanceTurn();
		Assert.False(managed.isInCivilDisorder);
	}

	[Fact]
	public void WithoutTheGovernorAUnitIsBuiltAgainAndWithItWondersCanBeRuledOut() {
		// Three different units, each built again: no chooser picks all three.
		foreach (string name in new[] { "Warrior", "Worker", "Settler" }) {
			UnitPrototype unit = gameData.unitPrototypes.Find(u => u.name == name);
			city.SetItemBeingProduced(unit);
			city.SetStoredShields(us.ShieldCost(unit));
			for (int i = 0; i < 4 && city.residents.Count < 4; i++) {
				city.AddCitizen(new CityResident { city = city, nationality = us.civilization, citizenType = Laborer });
			}
			city.HandleCityProduction(gameData);
			Assert.Equal(unit, city.itemBeingProduced);
		}

		// A city with every improvement built, where wonders are worth it.
		us.knownTechs.UnionWith(gameData.techs.Where(t => t.EraCivilopediaName == gameData.techs[0].EraCivilopediaName).Select(t => t.id));
		foreach (Building b in city.ListProductionOptions(gameData).OfType<Building>().Where(b => !b.IsGreatWonder() && !b.isSmallWonder).ToList()) {
			city.AddBuilding(b, gameData);
		}
		// Nothing left but wonders and Wealth: the chooser picks a wonder.
		foreach (UnitPrototype u in gameData.unitPrototypes) {
			u.unproducible = true;
		}
		IProducible free = ChooseProducible.Choose(city, us);
		Assert.True(free is Building fb && (fb.IsGreatWonder() || fb.isSmallWonder), $"chose {free}");
		city.governor.manageProduction = true;
		city.governor.buildGreatWonders = false;
		city.governor.buildSmallWonders = false;
		for (int i = 0; i < 20; i++) {
			IProducible choice = city.governor.ChooseProduction(city);
			Assert.False(choice is Building b && (b.IsGreatWonder() || b.isSmallWonder), $"chose {choice}");
		}
		city.SetItemBeingProduced(free);
		city.SetStoredShields(us.ShieldCost(free));
		city.HandleCityProduction(gameData);
		Assert.False(city.itemBeingProduced is Building nb && (nb.IsGreatWonder() || nb.isSmallWonder), $"chose {city.itemBeingProduced}");
	}

	// The AI keeps its own way of placing citizens.
	[Fact]
	public void TheAIsCitiesHaveNoGovernor() {
		// The same choice as above: a governor emphasizing production takes
		// the forest; the AI, short of food, the grassland.
		List<Tile> free = city.GetWorkableTiles().Where(t => t.personWorkingTile == null && t.IsLand() && !t.HasCity()).Take(2).ToList();
		Make(free[0], gameData.terrainTypes.Find(t => t.Key == "grassland"));
		Make(free[1], gameData.terrainTypes.Find(t => t.Key == "grassland"), gameData.terrainTypes.Find(t => t.Key == "forest"));
		foreach (Tile other in city.GetWorkableTiles().Where(t => !free.Contains(t))) {
			other.overlays.Add(gameData.terrainImprovements.Find(i => i.key == Tile.TileOverlays.POLLUTION));
		}
		city.governor.emphasizeShields = true;
		us.isHuman = false;
		Assert.Equal(free[0], Grow().tileWorked);
	}

	[Fact]
	public void AllOurCitiesAndTheOnesToComeGetTheSettingsAndTheyAreSaved() {
		City second = CityInteractions.BuildCity(gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsAllowCities() && t.unitsOnTile.Count == 0
			&& gameData.cities.All(c => c.location.DistanceTo(t) > 3)), us, "Second");
		new MsgSetGovernor(city.id, new CityGovernor { emphasizeCommerce = true, buildGreatWonders = false }, allCities: true).process();
		Assert.All(us.cities, c => Assert.True(c.governor.emphasizeCommerce));
		Assert.NotSame(city.governor, second.governor);
		City third = CityInteractions.BuildCity(gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsAllowCities() && t.unitsOnTile.Count == 0
			&& gameData.cities.All(c => c.location.DistanceTo(t) > 3)), us, "Third");
		Assert.True(third.governor.emphasizeCommerce);

		new MsgSetGovernor(second.id, new CityGovernor { manageMoods = true }, allCities: false).process();
		Assert.True(second.governor.manageMoods);
		Assert.False(city.governor.manageMoods);

		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		Assert.True(reloaded.cities.Find(c => c.id == second.id).governor.manageMoods);
		Assert.False(reloaded.cities.Find(c => c.id == city.id).governor.buildGreatWonders);
		Player them = reloaded.players.Find(p => p.id == us.id);
		Assert.True(them.defaultGovernor.emphasizeCommerce);
	}
}
