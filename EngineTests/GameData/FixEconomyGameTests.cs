using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Regression tests for fixes to the economy and city code that need a game.
public class FixEconomyGameTests : IClassFixture<SaveGameFixture>, System.IDisposable {
	private readonly C7GameData.GameData gameData;
	private readonly Player attacker;
	private readonly Player defender;

	public FixEconomyGameTests(SaveGameFixture fixture) {
		gameData = fixture.saveGame.ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);

		Player[] civs = gameData.players.Where(p => !p.isBarbarians && p.units.Any(u => u.unitType.isSettler)).ToArray();
		attacker = civs[0];
		defender = civs[1];
	}

	// Captures send messages to the UI; don't leave them for other tests.
	public void Dispose() {
		while (EngineStorage.TryDequeueNextMessageToUI(out _)) { }
	}

	private City FoundCity(Player player, int size) {
		Tile tile = player.units.First(u => u.unitType.isSettler).location;
		City city = CityInteractions.BuildCity(tile, player, player.GetNextCityName());
		while (city.residents.Count < size) {
			city.AddCitizen(new CityResident() {
				city = city,
				citizenType = city.residents[0].citizenType,
				tileWorked = city.residents[0].tileWorked,
			});
		}
		return city;
	}

	private Building MakeWonder(string name, Building granted = null) {
		SaveBuilding sb = new() { name = name, greatWonderProperties = new() };
		Building wonder = new(sb, gameData);
		wonder.greatWonderProperties.buildingGainedInEveryCity = granted;
		return wonder;
	}

	// Every tech's queue, from the starting techs and from nothing, has each
	// unknown prerequisite of every queued tech before it, holds every
	// unknown tech leading to the target once, and ends with the target.
	[Fact]
	public void ResearchQueuesOfTheRealTechTreePutPrerequisitesFirst() {
		Player player = attacker;
		Assert.NotEmpty(gameData.techs);

		void CheckAll() {
			foreach (Tech target in gameData.techs) {
				player.CalculateFreshTechQueueAndAssignNewCurrent(target);
				List<Tech> queue = player.ResearchQueue.ToList();
				FixEconomyTests.AssertPrerequisitesFirst(player);
				Assert.Equal(target, queue[^1]);
				Assert.Equal(queue.Count, queue.Distinct().Count());
				Assert.Equal(UnknownAncestors(player, target).Count + 1, queue.Count);
			}
		}

		// With the starting techs, and with nothing known at all.
		CheckAll();
		player.knownTechs = new();
		CheckAll();
	}

	private static HashSet<Tech> UnknownAncestors(Player player, Tech tech) {
		HashSet<Tech> result = new();
		Stack<Tech> todo = new(tech.Prerequisites);
		while (todo.Count > 0) {
			Tech t = todo.Pop();
			if (t != tech && !player.knownTechs.Contains(t.id) && result.Add(t)) {
				foreach (Tech p in t.Prerequisites) {
					todo.Push(p);
				}
			}
		}
		return result;
	}

	[Fact]
	public void WonderSnapshotsFollowCaptureFoundingAndDestruction() {
		Building granary = gameData.Buildings.First(b => !b.IsGreatWonder() && !b.isSmallWonder && !b.isCenterOfEmpire);
		Building wonder = MakeWonder("Test Wonder", granary);

		FoundCity(attacker, 1);
		City wonderCity = FoundCity(defender, 3);
		wonderCity.AddBuilding(wonder);
		Assert.Single(defender.GetActiveWonders());
		Assert.Empty(attacker.GetActiveWonders());

		// Captured: the great wonder survives and changes hands.
		CityInteractions.CaptureCity(wonderCity, attacker);
		Assert.Empty(defender.GetActiveWonders());
		Assert.Single(attacker.GetActiveWonders());
		Assert.All(attacker.cities, c => Assert.True(c.HasEffectiveBuilding(granary)));

		// A city founded later is granted the building too.
		Tile site = gameData.map.tiles.First(t => t.cityAtTile == null && !t.baseTerrainType.IsWater
			&& attacker.cities.All(c => t.RankDistanceTo(c.location) > 4)
			&& defender.cities.All(c => t.RankDistanceTo(c.location) > 4));
		City founded = CityInteractions.BuildCity(site, attacker, attacker.GetNextCityName());
		Assert.True(founded.HasEffectiveBuilding(granary));

		// Destroyed: the wonder and what it granted are gone.
		CityInteractions.DestroyCity(wonderCity);
		Assert.Empty(attacker.GetActiveWonders());
		Assert.False(founded.HasEffectiveBuilding(granary));
	}

	[Fact]
	public void BordersAtTheMapsEdgeSkipOffMapTilesAndFarOcean() {
		// A land tile on the top rows of the map, with borders reaching well
		// past the edge. Generated maps have water (and often ice-free sea)
		// along the poles, so if no land is there, make some.
		Tile edge = gameData.map.tiles.Where(t => t.YCoordinate <= 1)
			.OrderBy(t => t.baseTerrainType.IsWater ? 1 : 0).ThenBy(t => t.YCoordinate).First();
		if (edge.baseTerrainType.IsWater) {
			TerrainType land = gameData.map.tiles.First(t => !t.baseTerrainType.IsWater).baseTerrainType;
			edge.baseTerrainType = land;
			edge.overlayTerrainType = land;
		}
		City city = new(edge, attacker, "Edgeville", ID.None("city"));
		city.perPlayerCulture[attacker] = 100000;

		List<Tile> tiles = city.GetTilesWithinBorders();

		Assert.NotEmpty(tiles);
		Assert.DoesNotContain(Tile.NONE, tiles);
		Assert.DoesNotContain(tiles, t => t.baseTerrainType.IsOcean && t.RankDistanceTo(edge) > 2);
	}
}
