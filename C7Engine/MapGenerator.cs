namespace C7Engine {
	using System;
	using System.Collections.Generic;
	using System.Runtime.InteropServices;
	using System.Linq;
	using Serilog;
	using System.Diagnostics;
	using C7GameData;
	using C7GameData.Save;

	public class WorldCharacteristics {
		public enum Climate {
			Arid,
			Normal,
			Wet,
		}
		public Climate climate;

		public enum Landform {
			Archipelago,
			Continents,
			Pangaea,
		}
		public Landform landform;

		public enum OceanCoverage {
			Percent_60 = 60,
			Percent_70 = 70,
			Percent_80 = 80,
		}
		public OceanCoverage oceanCoverage;

		public enum Temperature {
			Cool,
			Temperate,
			Warm,
		}
		public Temperature temperature;

		public enum Age {
			Billion_3,
			Billion_4,
			Billion_5,
		}
		public Age age;

		public BarbarianActivity barbarianActivity;

		public int mapSeed = -1;
		public WorldSize worldSize;

		// Whether the map wraps east to west (Civ3's default) and north to
		// south: the scenario editor's World Setup chooses; a new game keeps
		// the defaults.
		public bool wrapX = true, wrapY = false;
		// How many of the starts must be next to the sea: one for each
		// seafaring civ picked for the game (see GameSetup), which must start
		// on the coast. Other civs may start anywhere.
		public int coastalStartsNeeded;
		// Whether every landmass but the largest is sunk, leaving one
		// landmass and no islands. Lakes and peninsulas are kept.
		public bool singleLandmass;
		public bool goodyHuts = true;
		// The cities each civ starts with. With more than one, the map gets
		// that many city sites per civ scattered over the land at random
		// in place of its starting locations (see DetermineScatteredCitySites).
		public int citiesPerCiv = 1;
		public List<TerrainType> terrainTypes;
		public List<Resource> resources = new();
		// The keys of the resources a unit or building needs, which are
		// placed before the strategic resources nothing needs (see
		// AddResources).
		public HashSet<string> requiredResources = new();
		// The food irrigation, the shields a mine and the commerce a road
		// add to each terrain (the BIQ's TERR), by terrain key, which Civ3's
		// map generator weighs. Civ3's own values for terrains missing.
		public Dictionary<string, (int irrigation, int mining, int road)> terrainBonuses = new();
		public Government defaultGovernment = new();

		public int maxRankOfWorkableTiles;

		// The food each citizen eats a turn (the BIQ's RULE).
		public int foodPerCitizen = 2;

		public WorldCharacteristics() { }

		public WorldCharacteristics(SaveGame save) {
			terrainTypes = save.TerrainTypes;
			resources = save.Resources;
			requiredResources = save.UnitPrototypes.SelectMany(u => u.requiredResources)
				.Concat(save.Buildings.SelectMany(b => b.requiredResources ?? [])).ToHashSet();
			defaultGovernment = save.Governments.Find(g => g.defaultType);

			maxRankOfWorkableTiles = save.Rules.MaxRankOfWorkableTiles;
			foodPerCitizen = save.Rules.FoodConsumptionPerCitizen;

			barbarianActivity = save.BarbarianInfo.barbarianActivity;

			int Bonus(string improvement, string terrain, Tile.YieldType yield) {
				SaveTerrainImprovement ti = save.TerrainImprovements.Find(t => t.key == improvement);
				return ti != null && ti.bonusYields.TryGetValue(terrain, out var yields) ? yields.GetValueOrDefault(yield) : 0;
			}
			foreach (TerrainType t in save.TerrainImprovements.Count > 0 ? terrainTypes ?? [] : []) {
				terrainBonuses[t.Key] = (Bonus(Tile.TileOverlays.IRRIGATION, t.Key, Tile.YieldType.Food), Bonus(Tile.TileOverlays.MINE, t.Key, Tile.YieldType.Production),
					Bonus(Tile.TileOverlays.ROAD, t.Key, Tile.YieldType.Commerce));
			}
		}
	}

	public class MapGenerator {
		private static ILogger log = Log.ForContext<MapGenerator>();

		// The entry point to the overall map generation process.
		public static GameMap GenerateMap(WorldCharacteristics wc) {
			return GenerateMap(wc, out _);
		}

		// Generates a map, and says why it can't be played, or null if it
		// can. A map can't be played when it has no room for a starting
		// location for every civ, the world size's distance between civs
		// apart. Such a map is still generated in full, so it can be looked
		// at.
		//
		// The map is Civ3's own (Civ3MapGenerator, a port of Civ3's
		// generator): Civ3's rules and settings with a seed give the same
		// map as in Civ3, tile for tile, with the same starts. OpenCiv3 adds
		// a few things of its own: a single landmass (Rising Tide, Eternal
		// War), scattered city sites for several cities per civ (Kings and
		// Nomads), no goody huts, and the resources a unit needs placed
		// before the other strategic ones (MDJ).
		public static GameMap GenerateMap(WorldCharacteristics wc, out string problem) {
			if (wc.mapSeed == -1) {
				log.Information("Random seed is not specified, generating...");
				wc.mapSeed = new Random().Next(int.MaxValue);
			}
			log.Information("Seed: " + wc.mapSeed);

			SanitizeWorldCharacteristics(wc);

			Civ3MapGenerator generator = new(SettingsOf(wc), RulesOf(wc));
			generator.Generate();
			GameMap gameMap = ToGameMap(wc, generator);
			// What the map was generated as, for the Advanced AI's map profiles
			// (see AI.Advanced.AdvancedProfiles).
			gameMap.generation = new MapGeneration {
				worldSize = string.IsNullOrEmpty(wc.worldSize.name) ? null : wc.worldSize.name,
				landform = wc.landform.ToString(),
				water = (int)wc.oceanCoverage,
			};

			problem = null;
			if (wc.citiesPerCiv > 1) {
				// Too few sites for every civ's cities is no problem: GameSetup
				// gives each civ fewer.
				DetermineScatteredCitySites(wc, gameMap);
				if (gameMap.startingLocations.Count < wc.worldSize.numberOfCivs) {
					problem = $"Only {gameMap.startingLocations.Count} city sites fit on the land for {wc.worldSize.numberOfCivs} civs";
				} else if (gameMap.startingLocations.Count(s => s.NeighborsOcean()) < wc.coastalStartsNeeded) {
					problem = $"Fewer than {wc.coastalStartsNeeded} coastal city sites for the seafaring civs' capitals";
				}
				// Civ3 put its huts away from its own starts; keep them off
				// the scattered sites' doorsteps too.
				foreach (Tile site in gameMap.startingLocations) {
					foreach (Tile n in site.GetTilesWithinRankDistance(2)) {
						n.hasGoodyHut = false;
					}
				}
			} else {
				if (gameMap.startingLocations.Count < wc.worldSize.numberOfCivs) {
					problem = $"Only {gameMap.startingLocations.Count} of {wc.worldSize.numberOfCivs} starting locations fit on the land";
				} else if (gameMap.startingLocations.Count(s => s.NeighborsOcean()) < wc.coastalStartsNeeded) {
					problem = $"Fewer than {wc.coastalStartsNeeded} coastal starting locations for the seafaring civs";
				}
			}

			// No barbarian camps come with the map: Civ3's generator places
			// none (camps appear once the game is under way, see
			// BarbarianInteractions.CampsMayAppear).

			// Last step: Assign the terrain file and image ids to each tile so
			// we know which texture to use when displaying them.
			TerrainTextureFiles.AssignTextureDetails(new Random(wc.mapSeed + 0xebac), wc.terrainTypes, gameMap);

			return gameMap;
		}

		// What the scenario editor's Map > Redistribute places again.
		public enum Redistribution { StartingLocations, Resources, GoodyHuts, BonusGrassland }

		// The scenario editor's Map > Redistribute: one of the generator's
		// placement steps run again, with the seed, on a map made elsewhere
		// (painted, imported, generated before). The map is loaded into the
		// generator as it is, the step's old placements taken off first, and
		// what the step places is returned, leaving the map unchanged: each
		// tile's resource (null for none), whether it has a hut, whether it's
		// bonus grassland, and the starting locations.
		public static (Resource[] resources, bool[] huts, bool[] bonusGrassland, List<Tile> starts) Redistribute(
			GameMap map, WorldCharacteristics wc, Redistribution what, int seed) {
			Civ3MapGenerator.Settings settings = SettingsOf(wc);
			settings.width = map.numTilesWide;
			settings.height = map.numTilesTall;
			settings.wrapX = map.wrapHorizontally;
			settings.wrapY = map.wrapVertically;
			settings.seed = seed;
			// Huts are wanted whatever the barbarians (Civ3 places none
			// without them).
			settings.barbarians = Math.Max(settings.barbarians, 1);
			Civ3MapGenerator g = new(settings, RulesOf(wc));
			int[] tileAt = new int[map.tiles.Count];
			for (int i = 0; i < map.tiles.Count; ++i) {
				Tile t = map.tiles[i];
				int k = g.Index(t.XCoordinate, t.YCoordinate);
				tileAt[i] = k;
				int terrain = Array.IndexOf(Civ3TerrainKeys, t.overlayTerrainType?.Key);
				int baseTerrain = Array.IndexOf(Civ3TerrainKeys, t.baseTerrainType?.Key);
				g.terrain[k] = terrain < 0 ? Civ3MapGenerator.Grassland : terrain;
				g.baseTerrain[k] = baseTerrain < 0 ? g.terrain[k] : baseTerrain;
				g.river[k] = (byte)((t.riverNorth ? 0x01 : 0) | (t.riverNortheast ? 0x02 : 0) | (t.riverEast ? 0x04 : 0) | (t.riverSoutheast ? 0x08 : 0)
					| (t.riverSouth ? 0x10 : 0) | (t.riverSouthwest ? 0x20 : 0) | (t.riverWest ? 0x40 : 0) | (t.riverNorthwest ? 0x80 : 0));
				g.flags[k] = (t.isBonusShield ? Civ3MapGenerator.BonusGrasslandFlag : 0) | (t.isSnowCapped ? Civ3MapGenerator.SnowCappedFlag : 0)
					| (t.isPineForest ? Civ3MapGenerator.ColdFlag : 0) | (map.startingLocations.Contains(t) ? Civ3MapGenerator.StartFlag : 0);
				g.resource[k] = t.Resource == null || t.Resource == Resource.NONE ? -1 : wc.resources.FindIndex(r => r.Key == t.Resource.Key);
				g.hut[k] = t.hasGoodyHut;
			}
			g.FindBodies();
			switch (what) {
				case Redistribution.StartingLocations: g.RedoStartingLocations(); break;
				case Redistribution.Resources: g.RedoResources(); break;
				case Redistribution.GoodyHuts: g.RedoGoodyHuts(); break;
				case Redistribution.BonusGrassland: g.RedoBonusGrassland(); break;
			}
			Resource[] resources = new Resource[map.tiles.Count];
			bool[] huts = new bool[map.tiles.Count], bonus = new bool[map.tiles.Count];
			for (int i = 0; i < map.tiles.Count; ++i) {
				int k = tileAt[i];
				resources[i] = g.resource[k] >= 0 ? wc.resources[g.resource[k]] : null;
				huts[i] = g.hut[k];
				bonus[i] = (g.flags[k] & Civ3MapGenerator.BonusGrasslandFlag) != 0;
			}
			List<Tile> starts = [];
			Dictionary<int, Tile> byIndex = [];
			for (int i = 0; i < map.tiles.Count; ++i) {
				byIndex[tileAt[i]] = map.tiles[i];
			}
			for (int s = 1; s <= g.startCount && s < g.startSlots.Length; ++s) {
				if (g.startSlots[s] >= 0 && byIndex.TryGetValue(g.startSlots[s], out Tile start)) {
					starts.Add(start);
				}
			}
			return (resources, huts, bonus, starts);
		}

		// The keys of Civ3's terrain types, by Civ3's numbers (TERR's order).
		public static readonly string[] Civ3TerrainKeys = {
			"desert", "plains", "grassland", "tundra", "flood plain", "hills", "mountains",
			"forest", "jungle", "marsh", "volcano", "coast", "sea", "ocean",
		};

		private static Civ3MapGenerator.Settings SettingsOf(WorldCharacteristics wc) {
			return new Civ3MapGenerator.Settings {
				width = wc.worldSize.width,
				height = wc.worldSize.height,
				wrapX = wc.wrapX,
				wrapY = wc.wrapY,
				seed = wc.mapSeed,
				landform = (int)wc.landform,
				oceanCoverage = wc.oceanCoverage switch {
					WorldCharacteristics.OceanCoverage.Percent_80 => 0,
					WorldCharacteristics.OceanCoverage.Percent_70 => 1,
					_ => 2,
				},
				climate = (int)wc.climate,
				temperature = (int)wc.temperature,
				age = (int)wc.age,
				// Civ3 places no goody huts without barbarians (Civ3 0x601fd0).
				barbarians = wc.goodyHuts && wc.barbarianActivity != BarbarianActivity.None ? (int)wc.barbarianActivity : -1,
				numberOfCivs = wc.worldSize.numberOfCivs,
				civDistance = wc.worldSize.distanceBetweenCivs,
				coastalStarts = wc.coastalStartsNeeded,
				singleLandmass = wc.singleLandmass,
			};
		}

		// Civ3's own values of the bonuses irrigation, a mine and a road give
		// each terrain (conquests.biq's TERR), for rules that don't say.
		private static readonly int[] Civ3Irrigation = { 1, 1, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
		private static readonly int[] Civ3Mining = { 1, 1, 1, 1, 0, 2, 2, 0, 0, 0, 0, 0, 0, 0 };
		private static readonly int[] Civ3Road = { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0 };

		private static Civ3MapGenerator.Rules RulesOf(WorldCharacteristics wc) {
			Civ3MapGenerator.Rules rules = new();
			TerrainType[] terrains = Civ3TerrainKeys.Select(k => wc.terrainTypes.Find(t => t.Key == k)).ToArray();
			for (int i = 0; i < Civ3TerrainKeys.Length; ++i) {
				TerrainType t = terrains[i];
				if (t == null) {
					continue;
				}
				rules.food[i] = t.baseFoodProduction;
				rules.shields[i] = t.baseShieldProduction;
				rules.commerce[i] = t.baseCommerceProduction;
				rules.allowCities[i] = t.allowCities;
				rules.workerJob[i] = t.allowedFoliageAction != TerrainType.Civ3FoliageAction.None;
				if (wc.terrainBonuses.TryGetValue(t.Key, out var bonus)) {
					(rules.irrigation[i], rules.mining[i], rules.road[i]) = bonus;
				} else {
					(rules.irrigation[i], rules.mining[i], rules.road[i]) = (Civ3Irrigation[i], Civ3Mining[i], Civ3Road[i]);
				}
			}
			foreach (Resource r in wc.resources) {
				Civ3MapGenerator.Good good = new() {
					type = r.Category switch {
						ResourceCategory.LUXURY => 1,
						ResourceCategory.STRATEGIC => 2,
						_ => 0,
					},
					appearanceRatio = r.AppearanceRatio,
					food = r.FoodBonus,
					shields = r.ShieldsBonus,
					commerce = r.CommerceBonus,
					needsAdvance = r.Prerequisite != null,
					placedFirst = wc.requiredResources.Contains(r.Key),
				};
				for (int i = 0; i < terrains.Length; ++i) {
					good.allowedOn[i] = terrains[i] != null && terrains[i].allowedResources.Contains(r.Key);
				}
				rules.goods.Add(good);
			}
			return rules;
		}

		// The GameMap of Civ3's map: its tiles in Civ3's order (row by row,
		// x + y even), terrain by key, rivers, resources, huts, bonus
		// grassland, snow, pines, and the starts in player slot order.
		private static GameMap ToGameMap(WorldCharacteristics wc, Civ3MapGenerator g) {
			GameMap m = new() {
				numTilesTall = g.height,
				numTilesWide = g.width,
				wrapHorizontally = wc.wrapX,
				wrapVertically = wc.wrapY,
				optimalNumberOfCities = wc.worldSize.optimalNumberOfCities,
				techRate = wc.worldSize.techRate,
			};
			TerrainType[] terrains = Civ3TerrainKeys.Select(k => wc.terrainTypes.Find(t => t.Key == k) ?? TerrainType.NONE).ToArray();
			ID.Factory factory = new();
			for (int i = 0; i < g.tileCount; ++i) {
				byte river = g.river[i];
				uint flags = g.flags[i];
				Tile t = new(factory.CreateID("tile")) {
					XCoordinate = g.X(i),
					YCoordinate = g.Y(i),
					baseTerrainType = terrains[g.baseTerrain[i]],
					overlayTerrainType = terrains[g.terrain[i]],
					hasGoodyHut = g.hut[i],
					isBonusShield = (flags & Civ3MapGenerator.BonusGrasslandFlag) != 0,
					// Civ3 keeps these flags on every tile they were set on, the
					// cold one on all cold land, and shows pines only on a
					// forest (as ImportCiv3 reads them).
					isSnowCapped = (flags & Civ3MapGenerator.SnowCappedFlag) != 0,
					isPineForest = (flags & Civ3MapGenerator.ColdFlag) != 0,
					riverNorth = (river & 0x01) != 0,
					riverNortheast = (river & 0x02) != 0,
					riverEast = (river & 0x04) != 0,
					riverSoutheast = (river & 0x08) != 0,
					riverSouth = (river & 0x10) != 0,
					riverSouthwest = (river & 0x20) != 0,
					riverWest = (river & 0x40) != 0,
					riverNorthwest = (river & 0x80) != 0,
				};
				if (g.resource[i] >= 0) {
					t.Resource = wc.resources[g.resource[i]];
					t.ResourceKey = t.Resource.Key;
				}
				m.tiles.Add(t);
			}
			m.computeNeighbors();
			m.recomputeContinents();
			for (int s = 1; s <= g.startCount && s < g.startSlots.Length; ++s) {
				if (g.startSlots[s] >= 0) {
					m.startingLocations.Add(m.tiles[g.startSlots[s]]);
				}
			}
			return m;
		}

		private static void SanitizeWorldCharacteristics(WorldCharacteristics wc) {
			// TODO: Supporting maps of odd dimensions should be doable, but beyond current tiling implementation
			// As a mitigation, we simply shrink the map dimensions a bit

			// The world size is usually one of the game's list of sizes, so
			// change a copy of it.
			if (wc.worldSize.width % 2 == 1 || wc.worldSize.height % 2 == 1) {
				wc.worldSize = wc.worldSize.Clone();
			}

			if (wc.worldSize.width % 2 == 1) {
				log.Warning("Uneven map width. Shrinking by one.");
				wc.worldSize.width -= 1;
			}

			if (wc.worldSize.height % 2 == 1) {
				log.Warning("Uneven map height. Shrinking by one.");
				wc.worldSize.height -= 1;
			}
		}

		// The direction of the tile one step (dx, dy) away, for diagonal steps.
		private static TileDirection EdgeDirection(int dx, int dy) {
			if (dy < 0) {
				return dx > 0 ? TileDirection.NORTHEAST : TileDirection.NORTHWEST;
			}
			return dx > 0 ? TileDirection.SOUTHEAST : TileDirection.SOUTHWEST;
		}

		// Marks the edge between two neighboring corners as a river, on both
		// tiles that share it.
		internal static void setRiverFlags(GameMap m, (int x, int y) from, (int x, int y) to) {
			int dx = to.x - from.x;
			int dy = to.y - from.y;

			// Undo the world wrap.
			if (Math.Abs(dx) > 1) {
				dx = -Math.Sign(dx);
			}

			// The two tiles sharing the edge are off to the side of it, so
			// from one to the other is the step (-dx, dy).
			Tile a = m.tileAt(from.x + dx, from.y);
			Tile b = m.tileAt(from.x, from.y + dy);
			SetRiverFlag(a, EdgeDirection(-dx, dy));
			SetRiverFlag(b, EdgeDirection(dx, -dy));
		}

		internal static void SetRiverFlag(Tile t, TileDirection direction) {
			// Neighbors off the edge of the map are Tile.NONE, which is shared
			// by every map, so flags must never be set on it.
			if (t == Tile.NONE) {
				return;
			}
			switch (direction) {
				case TileDirection.NORTHEAST: t.riverNortheast = true; break;
				case TileDirection.NORTHWEST: t.riverNorthwest = true; break;
				case TileDirection.SOUTHEAST: t.riverSoutheast = true; break;
				case TileDirection.SOUTHWEST: t.riverSouthwest = true; break;
			}
		}

		// The terrain and spacing rules for a barbarian camp that appears
		// during the game (see BarbarianInteractions.MaybeAddCamp).
		public static bool IsCampSiteSuitable(Tile t, Func<int, int> continentSize, int maxRankOfCampTiles) {
			// No barbarian camps on water, volcanos, or mountains.
			if (!t.IsLand() || t == Tile.NONE || t.overlayTerrainType.Key == "volcano" || t.overlayTerrainType.Key == "mountains") {
				return false;
			}

			// No barbarian camps on luxury or strategic resources (we're ok
			// with barb camps on things like cows or gold).
			if (t.Resource != null && t.Resource != Resource.NONE &&
				(t.Resource.Category == ResourceCategory.STRATEGIC || t.Resource.Category == ResourceCategory.LUXURY)) {
				return false;
			}

			// No barbarian camps on super tiny islands.
			if (continentSize(t.continent) < 15) {
				return false;
			}

			// No barbarian camps within the big fat cross of another camp.
			foreach (Tile n in t.GetTilesWithinRankDistance(maxRankOfCampTiles)) {
				if (n.hasBarbarianCamp) {
					return false;
				}
			}

			return true;
		}

		// The fewest moves between two scattered city sites, however
		// crowded the land; closer still for the sites whose radii may be
		// shared.
		private const int MIN_SCATTERED_CITY_SPACING = 4;
		private const int MIN_CROWDED_CITY_SPACING = 3;

		// Scatters numberOfCivs * citiesPerCiv city sites over the land at
		// random, as the map's starting locations, for GameSetup to deal out
		// to the civs. Any tile a capital could start on (see
		// ScorePossibleCityLocation) may be picked. The sites are kept as far
		// apart as there's room for, about the side of a square of land per
		// site, and closer only when that many don't fit. No two sites' city
		// radii (their big fat crosses) share a tile: on a 60% water map
		// never, packing the sites tighter if they don't fit at random; with
		// more water, as many as fit, the rest anywhere a city may stand,
		// the least crowded spacing apart (owner's decision, 2026-10-21).
		// There may still be too few, and GameSetup then gives each civ
		// fewer cities.
		private static void DetermineScatteredCitySites(WorldCharacteristics wc, GameMap m) {
			Random rand = new(wc.mapSeed + 0x5ca7);
			int count = wc.worldSize.numberOfCivs * wc.citiesPerCiv;
			Player scoringPlayer = new() { government = wc.defaultGovernment };
			List<Tile> candidates = m.tiles.Where(t => ScorePossibleCityLocation(wc, t, scoringPlayer) > 0).ToList();
			Dictionary<Tile, List<Tile>> radii = candidates.ToDictionary(t => t, t => t.GetTilesWithinRankDistance(wc.maxRankOfWorkableTiles));
			int land = m.tiles.Count(t => t.IsLand());
			int spacing = Math.Max(MIN_SCATTERED_CITY_SPACING, (int)Math.Sqrt(land / (double)Math.Max(1, count)));

			List<Tile> sites = ScatterSites(candidates, radii, count, spacing, MIN_SCATTERED_CITY_SPACING, rand, [], separateRadii: true);
			if (sites.Count < count && wc.oceanCoverage == WorldCharacteristics.OceanCoverage.Percent_60) {
				List<Tile> packed = PackSites(candidates, radii, count, rand);
				if (packed.Count > sites.Count) {
					sites = packed;
				}
			} else if (sites.Count < count) {
				List<Tile> anywhere = m.tiles.Where(t => t.IsLand() && t.IsAllowCities()).ToList();
				foreach (Tile t in anywhere.Where(t => !radii.ContainsKey(t))) {
					radii[t] = t.GetTilesWithinRankDistance(wc.maxRankOfWorkableTiles);
				}
				sites = ScatterSites(anywhere, radii, count, spacing, MIN_CROWDED_CITY_SPACING, rand, sites, separateRadii: false);
			}
			log.Information($"Scattered {sites.Count} of {count} city sites");
			rand.Shuffle(CollectionsMarshal.AsSpan(sites));
			m.startingLocations = sites;
		}

		// Adds sites to those kept, taking the candidates in a random order,
		// each the spacing from every other site and, with separateRadii,
		// sharing no tile of its radius with theirs. Tries again closer
		// together, down to the least spacing, until there are count sites.
		private static List<Tile> ScatterSites(List<Tile> candidates, Dictionary<Tile, List<Tile>> radii, int count, int spacing, int leastSpacing,
				Random rand, List<Tile> kept, bool separateRadii) {
			List<Tile> sites = new(kept);
			for (; spacing >= leastSpacing && sites.Count < count; --spacing) {
				rand.Shuffle(CollectionsMarshal.AsSpan(candidates));
				sites = new(kept);
				HashSet<Tile> claimed = kept.SelectMany(t => radii[t]).ToHashSet();
				foreach (Tile t in candidates) {
					if (sites.Count == count) {
						break;
					}
					if (sites.All(site => site.DistanceTo(t) >= spacing) && !(separateRadii && radii[t].Any(claimed.Contains))) {
						sites.Add(t);
						claimed.UnionWith(radii[t]);
					}
				}
			}
			return sites;
		}

		// Packs sites whose radii share no tile as tightly as they go, for
		// when scattering them at random leaves too little room: from a
		// random first site, each next one is the candidate nearest the
		// sites so far, so they fill the land from there like cells.
		private static List<Tile> PackSites(List<Tile> candidates, Dictionary<Tile, List<Tile>> radii, int count, Random rand) {
			List<Tile> sites = [];
			HashSet<Tile> claimed = [];
			Dictionary<Tile, int> nearest = candidates.ToDictionary(t => t, _ => int.MaxValue);
			Tile next = candidates.Count > 0 ? candidates[rand.Next(candidates.Count)] : null;
			while (next != null && sites.Count < count) {
				sites.Add(next);
				claimed.UnionWith(radii[next]);
				nearest.Remove(next);
				next = null;
				int best = int.MaxValue;
				foreach (Tile t in nearest.Keys.ToList()) {
					if (radii[t].Any(claimed.Contains)) {
						nearest.Remove(t);
						continue;
					}
					int d = Math.Min(nearest[t], t.DistanceTo(sites[^1]));
					nearest[t] = d;
					if (d < best) {
						best = d;
						next = t;
					}
				}
			}
			return sites;
		}

		// Every civ must start where its capital can grow to at least this
		// size working the best food tiles around it.
		private const int MIN_START_GROWTH_SIZE = 3;

		// A city center always makes this much food (see Tile.BaseFoodYield).
		// Each citizen eats the rules' foodPerCitizen.
		private const int CITY_CENTER_FOOD = 2;

		// Whether a city founded on t could grow to the given size, with each
		// citizen working the best food tile left in reach. To grow past a
		// size the city needs a food surplus at that size.
		private static bool CanGrowToSize(WorldCharacteristics wc, Tile t, Player player, int size) {
			// A new city can only work tiles inside its borders, which start
			// out covering just the tiles next to it, so only count those.
			List<int> food = t.GetTilesWithinRankDistance(1)
				.Where(n => n != t && n != Tile.NONE)
				.Select(n => n.FoodYield(player).yield)
				.OrderByDescending(f => f)
				.ToList();

			int total = CITY_CENTER_FOOD;
			for (int citizens = 1; citizens < size; ++citizens) {
				if (citizens > food.Count) {
					return false;
				}
				total += food[citizens - 1];
				if (total <= citizens * wc.foodPerCitizen) {
					return false;
				}
			}
			return true;
		}

		// TODO: merge this with the ai logic
		// Maps each continent id to the number of tiles in that continent.
		private static Dictionary<int, int> ComputeContinentSizes(GameMap m) {
			Dictionary<int, int> result = new();
			foreach (HashSet<Tile> continent in m.continents) {
				foreach (Tile t in continent) {
					result[t.continent] = continent.Count;
				}
			}
			return result;
		}

		private static int ScorePossibleCityLocation(WorldCharacteristics wc, Tile t, Player player) {
			if (!t.IsAllowCities() || !CanGrowToSize(wc, t, player, MIN_START_GROWTH_SIZE)) {
				return int.MinValue;
			}

			const int CommercePoints = 4;
			const int ShieldPoints = 12;
			const int FoodPoints = 20;
			const int RiverPoints = 35;
			const int CoastPoints = 30;
			const int LandTilePoints = 1;
			// Barbarian camps aren't scored: there are none at the start.

			// Calculate the score for tiles in the immediate area.
			int score = 0;
			foreach (Tile n in t.GetTilesWithinRankDistance(1)) {
				score += CommercePoints * n.CommerceYield(player).yield;
				score += ShieldPoints * n.ProductionYield(player).yield;
				score += FoodPoints * n.FoodYield(player).yield;

				if (!n.IsLand()) {
					score += CoastPoints;
				}
			}

			// Then do it again for the full big fat cross, effectively weighting
			// the immediate neighbors at a 2x rate.
			foreach (Tile n in t.GetTilesWithinRankDistance(wc.maxRankOfWorkableTiles)) {
				score += CommercePoints * n.CommerceYield(player).yield;
				score += ShieldPoints * n.ProductionYield(player).yield;
				score += FoodPoints * n.FoodYield(player).yield;
			}

			// Give an extra bonus for fresh water, a river or a lake (the
			// owner asked for starts with fresh water, 2026-10-12).
			if (t.BordersRiver() || t.NeighborsFreshWater()) {
				score += RiverPoints;
			}

			// Try to get a rough sense of the number of surrounding land tiles
			// on the same continent, to avoid players getting stuck on a
			// peninsula.
			foreach (Tile n in t.GetTilesWithinRankDistance(4)) {
				if (n.continent == t.continent) {
					score += LandTilePoints;
				}
			}

			return score;
		}
	}
}
