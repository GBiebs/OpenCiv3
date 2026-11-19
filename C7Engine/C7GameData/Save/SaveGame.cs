using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using C7Engine;
using C7Engine.Lua;
using Serilog;

namespace C7GameData.Save {

	public static class TypeInfoResolver {
		public static void IgnoreDefaultValues(JsonTypeInfo jsonTypeInfo) {
			foreach (JsonPropertyInfo pi in jsonTypeInfo.Properties) {
				if (pi.PropertyType == typeof(string)) {
					pi.ShouldSerialize = (_, value) => ((string)value)?.Length > 0;
				} else if (typeof(ICollection).IsAssignableFrom(pi.PropertyType)) {
					pi.ShouldSerialize = (_, value) => ((ICollection)value)?.Count > 0;
				} else if (typeof(IEnumerable).IsAssignableFrom(pi.PropertyType)) {
					pi.ShouldSerialize = (_, value) => value is not null && ((IEnumerable)value).GetEnumerator().MoveNext();
				} else {
					pi.ShouldSerialize = (_, value) => value is not null;
				}
			}
		}
	}

	public class SaveGame {
		private static readonly ILogger log = Log.ForContext<SaveGame>();

		private static JsonSerializerOptions CreateJsonOptions(bool writeIndented) {
			return new JsonSerializerOptions {
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
				// Pretty print during development; may change this for production
				WriteIndented = writeIndented,
				// By default it only serializes getters, this makes it serialize fields, too
				IncludeFields = true,
				Converters = {
					new Json2DArrayConverter(),
					new IDJsonConverter(),
					new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
				},
				TypeInfoResolver = new DefaultJsonTypeInfoResolver {
					Modifiers = { TypeInfoResolver.IgnoreDefaultValues },
				},
			};
		}

		// System.Text.Json caches the metadata it builds for each type in the
		// options instance, so the options are created once and reused.
		private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions(writeIndented: true);
		private static readonly JsonSerializerOptions CompactJsonOptions = CreateJsonOptions(writeIndented: false);

		public SaveGame() { }

		// A copy sharing everything with this one, for a LAN host to change
		// a little for one guest (see C7Engine.Network.SnapshotFilter).
		internal SaveGame ShallowCopy() {
			return (SaveGame)MemberwiseClone();
		}

		public static SaveGame FromGameData(GameData data) => FromGameData(data, null);

		// With the map of a save made before, the tiles that are the same are
		// shared with it (see SaveMap).
		internal static SaveGame FromGameData(GameData data, SaveMap mapBefore) {
			SaveGame save = new SaveGame {
				FormatVersion = CurrentFormatVersion,
				Seed = data.seed,
				RngState = (data.random as GameRandom)?.State,
				TurnNumber = data.turn,
				PlayedSeconds = data.playedSeconds,
				FallenCivilizations = data.fallenCivilizations.Select(c => c.name).ToList(),
				Civilizations = data.civilizations,
				CultureGroups = data.cultureGroups,
				Alliances = data.alliances,
				Map = new SaveMap(data.map, mapBefore),
				TerrainTypes = data.terrainTypes,
				Resources = data.Resources,
				Buildings = data.Buildings.ConvertAll(building => building.ToSaveBuilding()),
				Inflows = data.Inflows.ConvertAll(inflow => new SaveInflow(inflow)),
				GreatWondersBuilt = data.GreatWondersBuilt,
				BarbarianInfo = data.barbarianInfo,
				Units = data.mapUnits.ConvertAll(unit => new SaveUnit(unit)),
				UnitPrototypes = data.unitPrototypes.ConvertAll(proto => new SaveUnitPrototype(proto)),
				Players = data.players.ConvertAll(player => new SavePlayer(player, data.map)),
				Cities = data.cities.ConvertAll(city => new SaveCity(city)),
				ExperienceLevels = data.experienceLevels,
				ScenarioSearchPath = data.scenarioSearchPath,
				ScenarioName = data.scenarioName,
				DefaultExperienceLevel = data.defaultExperienceLevelKey,
				Techs = data.techs.ConvertAll(t => t.ToSaveTech()),
				CitizenTypes = data.citizenTypes,
				TerraForms = data.Terraforms.ConvertAll(t => t.ToSaveTerraform()),
				Governments = data.governments,
				WorldSizes = data.worldSizes,
				Difficulties = data.difficulties,
				GameDifficulty = data.gameDifficulty,
				GameDifficultyIndex = data.difficulties.IndexOf(data.gameDifficulty) is int index && index >= 0 ? index : null,
				Rules = data.rules,
				TimeOptions = data.timeOptions,
				History = data.history,
				Replay = data.gameHistory.Events,
				VictoryConditions = data.victoryConditions,
				GameOver = data.gameOver,
				SimultaneousTurns = data.simultaneousTurns,
				PlayByEmail = data.playByEmail,
				DebugMode = data.debugMode,
				UnitedNations = data.unitedNations,
				Plague = data.plague,
				// Left out of games without Staging.
				Staging = data.rules.StagingUnits ? data.staging : null,
				// Left out of games without Rising Tide.
				RisingTide = data.rules.RisingTide ? data.risingTide : null,
				// Left out of games that never had the tutorial on.
				Tutorial = data.tutorial.enabled || data.tutorial.shown.Count > 0 ? data.tutorial : null,
				// Left out of games that aren't recorded in a play log.
				PlayLog = data.playLog?.Copy(),
				Winner = data.winner != null ? new SavePlayer(data.winner, data.map) : null,
				VictoryBonus = data.victoryBonus,
				NuclearEvents = data.nuclearEvents,
				TerrainImprovements = data.terrainImprovements.ConvertAll(ti => ti.ToSaveTerrainImprovement()),
				GameModeConfig = data.gameModeConfig,
			};
			foreach (var saveCivilization in save.Civilizations) {
				saveCivilization.cultureGroupKey = save.CultureGroups.First(c => c.name == saveCivilization.cultureGroup.name).name;
			}
			save.StrengthBonuses.Add(data.fortificationBonus);
			save.StrengthBonuses.Add(data.riverCrossingBonus);
			save.StrengthBonuses.Add(data.cityLevel1DefenseBonus);
			save.StrengthBonuses.Add(data.cityLevel2DefenseBonus);
			save.StrengthBonuses.Add(data.cityLevel3DefenseBonus);
			save.HealRates["friendly_field"] = data.healRateInFriendlyField;
			save.HealRates["neutral_field"] = data.healRateInNeutralField;
			save.HealRates["hostile_field"] = data.healRateInHostileField;
			save.HealRates["city"] = data.healRateInCity;

			foreach (KeyValuePair<Alliance, Alliance> kvp in data.allianceWars) {
				save.AllianceWars.TryAdd(kvp.Key.name, kvp.Value.name);
				save.AllianceWarPairs.Add([kvp.Key.name, kvp.Value.name]);
			}

			return save;
		}

		public GameData ToGameData(BehaviorEngine behaviors) {
			GiveCityStatesTheirOwnPlayer();
			GameData data = InitializeGameData();

			data.luaBehaviorEngine = behaviors;
			data.gameModeConfig = GameModeConfig;

			ConvertTerrainImprovements(data);
			ConvertTerraforms(data);
			// convert technologies earlier than the player data,
			// because we need to fill in current research and research queues
			ConvertTechnologies(data);
			ConvertInflow(data);
			ConvertStrengthBonuses(data);
			ConvertHealRates(data);

			EngineStorage.gameData = data;

			ConvertMapAndPlayers(data);
			ConvertBuildings(data);
			ConvertUnits(data);
			ConvertCities(data);
			ConvertAiState(data);
			SpaceRace.RecordApolloFromCities(data);
			ConvertBarbarianInfo(data);
			ConvertCultureGroups(data);
			ConvertAlliances(data);
			ConvertAllianceWars(data);

			ConvertVictoryConditions(data);

			// TODO: Redo victory state recording
			data.winner = data.players?.FirstOrDefault(p => p.civilization?.name == Winner?.civilization);
			data.gameOver = GameOver;
			data.victoryBonus = VictoryBonus;
			data.nuclearEvents = NuclearEvents;
			data.simultaneousTurns = SimultaneousTurns;
			data.playByEmail = PlayByEmail;
			data.debugMode = DebugMode;

			BeginHistory(data);

			data.defaultExperienceLevelKey = DefaultExperienceLevel;
			data.defaultExperienceLevel = data.experienceLevels.Find(el => el.key == DefaultExperienceLevel);

			data.RemoveRelationshipsWithDefeatedCivs();

			// Before the tile owners are worked out, which records what has
			// changed since the events last had them.
			data.gameHistory.Begin(data);
			data.UpdateTileOwners();
			HostFacts?.ApplyTo(data);
			data.InvalidateCachedTradeNetwork();

			data.onGameCreation += OnGameCreation;

			return data;
		}

		private void BeginHistory(GameData data) {
			foreach (var player in data.players) {
				if (!player.IsIndependent) {
					if (!data.history.ContainsKey(player.id.ToString()))
						data.history[player.id.ToString()] = new List<HistTurnRecord>();
				}
			}
		}

		private void ConvertVictoryConditions(GameData data) {
			VictoryConditions conditions = data.victoryConditions;

			// NOTE: Order matters

			// There is no "score victory", but we can treat score as if it were, so
			// we can render the current score alongside the state of other conditions
			data.victories.Add(new ScoreVictory());

			// Under Rising Tide, outlasting the rest takes the place of
			// conquering them.
			if (data.rules?.RisingTide == true) {
				data.victories.Add(new RisingTideVictory());
			} else if (conditions?.AllowConquestVictory == true) {
				data.victories.Add(new ConquestVictory());
			}
			if (conditions?.AllowEmpireVictory == true) {
				data.victories.Add(new EmpireVictory());
			}
			if (conditions?.AllowDominationVictory == true) {
				data.victories.Add(new DominationVictory(conditions.DominationTerritoryPercent, conditions.DominationPopulationPercent));
			}
			// Without victory conditions the space race is allowed (see
			// SpaceRace.SpaceRaceAllowed), so a completed ship must be able to win.
			if (conditions == null || conditions.AllowSpaceRaceVictory) {
				data.victories.Add(new SpaceRaceVictory());
			}
			if (conditions?.AllowDiplomaticVictory == true) {
				data.victories.Add(new DiplomaticVictory());
			}
			if (conditions?.AllowCulturalVictory == true) {
				data.victories.Add(new CulturalVictory(conditions.CultureOneCityWin, conditions.CultureAllCitiesWin));
			}

			if (conditions?.AllowWonderVictory == true) {
				data.victories.Add(new WonderVictory());
			}
			// Victory Point Scoring (the BIQ's VictoryLocations flag).
			if (conditions?.VictoryLocations == true && conditions.VictoryPointLimit > 0) {
				data.victories.Add(new VictoryPointVictory(conditions.VictoryPointLimit));
			}

			// Regicide and Elimination win nothing themselves, but show how
			// near each civ is to being eliminated.
			if (conditions != null && (conditions.Regicide || conditions.MassRegicide)) {
				data.victories.Add(new RegicideCondition(all: conditions.MassRegicide && !conditions.Regicide));
			}
			if (conditions?.CityElimination == true) {
				data.victories.Add(new EliminationCondition(conditions.CityEliminationCount));
			}
			// Capture the Flag wins by bringing in a flag, unless victory
			// points decide (see CaptureTheFlag).
			if (conditions?.CaptureTheFlag == true && !conditions.VictoryLocations) {
				data.victories.Add(new CaptureTheFlagVictory());
			}

			// The game ends at the turn limit only if that was chosen;
			// otherwise it goes on until someone wins. A time limit in
			// minutes (Rules.TimeLimitMinutes) ends it too.
			if (conditions?.UseTurnLimit == true || data.rules?.TimeLimitMinutes > 0) {
				data.victories.Add(new TimeLimitVictory(conditions?.UseTurnLimit == true ? data.timeOptions.turnLimit : 0));
			}
		}

		private void OnGameCreation() {
			// Each civ's flag, at the start of a Capture the Flag game.
			C7Engine.CaptureTheFlag.PlaceFlags(EngineStorage.gameData);
			// Civ3's Civ-Specific Abilities off: no civilization has its
			// traits in this game (Rules.CivSpecificAbilities).
			if (EngineStorage.gameData.rules?.CivSpecificAbilities == false) {
				foreach (Civilization civ in EngineStorage.gameData.civilizations) {
					civ.traits.Clear();
				}
			}
			foreach (Player p in EngineStorage.gameData.players) {
				// Backfill citizen assignments for scenarios that don't specify
				// them.
				foreach (City c in p.cities) {
					foreach (CityResident cr in c.residents) {
						if (cr.citizenType.IsDefaultCitizen && cr.tileWorked == Tile.NONE) {
							C7Engine.AI.CityTileAssignmentAI.AssignNewCitizenToTile(EngineStorage.gameData, cr);
						}
					}
				}

				// Backfill visibility. The active tiles only need to be
				// recomputed once, after all the units have been seen to.
				foreach (MapUnit u in p.units) {
					p.tileKnowledge.AddTilesToKnown(u.location, false);
				}
				if (p.units.Count > 0) {
					p.tileKnowledge.RecomputeActiveTiles();
				}

				// TODO: this may require more than one loop, because if all the
				// citizens are happy it reduces wasted shields via we love the
				// king day.
				p.DoCorruptionCalculations(EngineStorage.gameData);
				p.RecalculateCitizenMoods(EngineStorage.gameData);
			}
		}

		// The difficulty the game is played at, as one of Difficulties, since
		// game rules (such as the score) tell the difficulty by its place in
		// that list. GameDifficulty is saved as a copy, and may be set to one
		// from another copy of the rules (as by GameSetup), so it is matched
		// by id or name; saves made before the index was saved have only
		// those.
		internal Difficulty ResolveGameDifficulty() {
			if (GameDifficulty != null) {
				Difficulty match = Difficulties.Find(d => ReferenceEquals(d, GameDifficulty))
					?? (GameDifficulty.id is null ? null : Difficulties.Find(d => d.id == GameDifficulty.id));
				if (match == null && GameDifficulty.Name != null) {
					List<Difficulty> named = Difficulties.FindAll(d => d.Name == GameDifficulty.Name);
					if (named.Count == 1) {
						match = named[0];
					}
				}
				if (match != null) {
					return match;
				}
			}
			if (GameDifficultyIndex is int index && index >= 0 && index < Difficulties.Count) {
				return Difficulties[index];
			}
			if (Difficulties.Count > 0) {
				log.Warning("The game difficulty {Difficulty} is not one of the game's difficulties", GameDifficulty?.Name);
			}
			return GameDifficulty ?? new Difficulty();
		}

		// The game's random number generator, carrying on from where it was
		// when the game was saved. Saves made before its state was saved
		// start it afresh each turn from the seed.
		//
		// Unless the rules don't preserve the random seed (Civ3's Preserve
		// Random Seed, Rules.PreserveRandomSeed): then a game loaded part way
		// through gets new random numbers, so playing a turn again may turn
		// out differently.
		private Random CreateRandom(int seed) {
			if (RngState is ulong state) {
				if (Rules?.PreserveRandomSeed == false) {
					return new GameRandom(GameRandom.InitialState(Random.Shared.Next(), TurnNumber));
				}
				return new GameRandom(state);
			}
			return new GameRandom(GameRandom.InitialState(seed, TurnNumber));
		}

		// Kings & Nomads saves from before the city-states had a player of
		// their own (2026-10-25) have them held by the barbarians: they go,
		// with the units in them, to a City-States player made for them.
		private void GiveCityStatesTheirOwnPlayer() {
			if (Rules?.SettlersOnlyInConqueredCities != true || !Rules.FallenCitiesGoIndependent
					|| Civilizations.Any(c => c.isCityStates)) {
				return;
			}
			Civilization barbarianCiv = Civilizations.Find(c => c.isBarbarian);
			SavePlayer barbarians = barbarianCiv == null ? null : Players.Find(p => p.civilization == barbarianCiv.name);
			if (barbarians == null) {
				return;
			}
			Civilization civ = C7Engine.GameSetup.CityStatesCivilization(barbarianCiv);
			Civilizations.Add(civ);
			SavePlayer cityStates = new() {
				id = ID.FromString("CityStates-1"),
				isCityStates = true,
				civilization = civ.name,
				primaryColorIndex = civ.primaryColorIndex,
				secondaryColorIndex = civ.secondaryColorIndex,
				canBePicked = false,
				eraCivilopediaName = barbarians.eraCivilopediaName,
				governmentId = barbarians.governmentId,
				knownTechs = new HashSet<ID>(barbarians.knownTechs),
			};
			Players.Add(cityStates);
			HashSet<(int, int)> held = new();
			foreach (SaveCity city in Cities.Where(c => c.owner == barbarians.id)) {
				city.owner = cityStates.id;
				held.Add((city.location.X, city.location.Y));
				if (city.perPlayerCulture.Remove(barbarians.id.ToString(), out int culture)) {
					city.perPlayerCulture[cityStates.id.ToString()] = culture;
				}
			}
			foreach (SaveUnit unit in Units.Where(u => u.owner == barbarians.id && held.Contains((u.currentLocation.X, u.currentLocation.Y)))) {
				unit.owner = cityStates.id;
			}
		}

		private GameData InitializeGameData() {
			// copy data without references
			var data = new GameData(Seed) {
				turn = TurnNumber,
				playedSeconds = PlayedSeconds,
				terrainTypes = TerrainTypes,
				Resources = Resources,
				scenarioSearchPath = ScenarioSearchPath,
				scenarioName = ScenarioName,
				civilizations = Civilizations,
				fallenCivilizations = Civilizations.Where(c => FallenCivilizations.Contains(c.name)).ToList(),
				cultureGroups = CultureGroups,
				alliances = Alliances,
				citizenTypes = CitizenTypes,
				governments = Governments,
				worldSizes = WorldSizes,
				difficulties = Difficulties,
				gameDifficulty = ResolveGameDifficulty(),
				ids = new ID.Factory(this),
				experienceLevels = ExperienceLevels,
				rules = Rules,
				timeOptions = TimeOptions,
				victoryConditions = VictoryConditions,
				history = History,
				gameHistory = new GameHistory { Events = Replay ?? new() },
				GreatWondersBuilt = GreatWondersBuilt,
				unitedNations = UnitedNations ?? new UnitedNationsState(),
				plague = Plague ?? new PlagueState(),
				staging = Staging ?? new StagingState(),
				risingTide = RisingTide ?? new RisingTideState(),
				tutorial = Tutorial ?? new TutorialState(),
				playLog = PlayLog?.Copy(),
			};
			// A save without a seed has been given a random one.
			data.random = CreateRandom(data.seed);

			return data;
		}

		private void ConvertTerraforms(GameData data) {
			data.Terraforms = TerraForms.ConvertAll(st => new Terraform(st, data));
		}

		private void ConvertMapAndPlayers(GameData data) {
			// units and cities are empty
			data.map = Map.ToGameMap(data);

			// players need game map to populate tile knowledge
			data.players = Players.ConvertAll(player => player.ToPlayer(data.map, Civilizations, data.governments, data.techs, data.rules, data.alliances, data.terrainImprovements));

			// The owners of outposts and colonies, now that there are players.
			Dictionary<ID, Player> playersById = null;
			for (int i = 0; i < Map.tiles.Count && i < data.map.tiles.Count; ++i) {
				ID ownerId = Map.tiles[i].holdingOwner;
				if (ownerId == null) {
					continue;
				}
				playersById ??= data.players.Where(p => p.id != null).GroupBy(p => p.id).ToDictionary(g => g.Key, g => g.First());
				if (playersById.TryGetValue(ownerId, out Player owner)) {
					data.map.tiles[i].holdingOwner = owner;
				} else {
					Serilog.Log.Warning("The outpost or colony at {Tile} belongs to unknown player {Owner}, so to no one", data.map.tiles[i], ownerId);
				}
			}
		}

		private void ConvertTerrainImprovements(GameData gameData) {
			var saveByKey = TerrainImprovements.ToDictionary(s => s.key);
			var terrainTypeByKey = TerrainTypes.ToDictionary(tt => tt.Key);

			var created = new Dictionary<string, TerrainImprovement>();

			TerrainType ResolveTerrainType(string key) {
				return terrainTypeByKey[key];
			}

			TerrainImprovement Create(string key) {
				if (created.TryGetValue(key, out var existing)) {
					return existing;
				}

				var save = saveByKey[key];
				TerrainImprovement upgradesFrom = null;

				if (!string.IsNullOrEmpty(save.upgradesFrom)) {
					upgradesFrom = Create(save.upgradesFrom);
				}

				var improvement = new TerrainImprovement(save, gameData.luaBehaviorEngine, ResolveTerrainType, upgradesFrom);
				created[key] = improvement;
				return improvement;
			}

			foreach (var key in saveByKey.Keys) {
				Create(key);
			}

			gameData.terrainImprovements = created.Values.ToList();
		}

		private void ConvertTechnologies(GameData data) {
			// Fill in the list of techs and then backfill the prereqs.
			//
			// This is an N^2 approach, but doing a topological sort of the
			// prereqs feels like overkill given how many techs are in a game.

			foreach (SaveTech st in this.Techs) {
				data.techs.Add(st.ToTechWithoutPrereqs());
			}

			foreach (Tech t in data.techs) {
				t.FillInPrereqs(this.Techs, data.techs);
			}
		}

		private void ConvertInflow(GameData data) {
			data.Inflows = Inflows.ConvertAll(saveInflow => new Inflow(saveInflow, data.luaBehaviorEngine) {
				requiredTech = saveInflow.requiredTech == null ? null : data.techs.Find(t => t.id == saveInflow.requiredTech),
			});
		}

		private void ConvertBuildings(GameData data) {
			data.Buildings = Buildings.ConvertAll(building => new Building(building, data));

			var buildingDict = data.Buildings.ToDictionary(b => b.name);
			var techDict = data.techs.ToDictionary(t => t.id);
			var resDict = data.Resources.ToDictionary(r => r.Key);

			foreach (SaveBuilding saveBuilding in Buildings) {
				Building building = buildingDict[saveBuilding.name];

				if (saveBuilding.requiredBuilding != null) {
					building.requiredBuilding = buildingDict[saveBuilding.requiredBuilding];
				}

				if (saveBuilding.requiredTech != null) {
					building.requiredTech = techDict[saveBuilding.requiredTech];
				}
				if (saveBuilding.doublesHappinessOf != null) {
					building.doublesHappinessOf = buildingDict[saveBuilding.doublesHappinessOf];
				}
				if (saveBuilding.renderedObsoleteBy != null) {
					building.renderedObsoleteBy = techDict[saveBuilding.renderedObsoleteBy];
				}
				if (saveBuilding.requiredGovernment != null) {
					building.requiredGovernment = data.governments.Find(g => g.id == saveBuilding.requiredGovernment);
				}
				if (saveBuilding.greatWonderProperties?.buildingGainedInEveryCity?.Length > 0) {
					building.greatWonderProperties.buildingGainedInEveryCity = buildingDict[saveBuilding.greatWonderProperties.buildingGainedInEveryCity];
				}
				if (saveBuilding.greatWonderProperties?.buildingGainedInEveryCityOnContinent?.Length > 0) {
					building.greatWonderProperties.buildingGainedInEveryCityOnContinent = buildingDict[saveBuilding.greatWonderProperties.buildingGainedInEveryCityOnContinent];
				}

				building.requiredResources = saveBuilding.requiredResources.Select(a => resDict[a]).ToHashSet();
			}
		}

		private void ConvertUnits(GameData data) {
			data.unitPrototypes = UnitPrototypes.ConvertAll(prototype => new UnitPrototype(prototype, data.Terraforms));

			var techDict = data.techs.ToDictionary(t => t.id);
			var unitPrototypeDict = data.unitPrototypes.ToDictionary(b => b.name);
			var civDict = data.civilizations.ToDictionary(c => c.name);
			var resDict = data.Resources.ToDictionary(r => r.Key);

			foreach (SaveUnitPrototype saveProto in UnitPrototypes) {
				UnitPrototype proto = unitPrototypeDict[saveProto.name];

				if ((saveProto.upgradesTo ?? []).Any()) {
					proto.upgradesTo = saveProto.upgradesTo.Select(x => unitPrototypeDict[x]).ToList();
				}

				if (saveProto.requiredTech != null) {
					proto.requiredTech = techDict[saveProto.requiredTech];
				}

				proto.producibleBy = saveProto.producibleBy.Select(c => civDict[c]).ToHashSet();

				proto.requiredResources = saveProto.requiredResources.Select(a => resDict[a]).ToHashSet();
			}

			// map units need game map and players to populate location and owner.
			// Each unit is added to its tile's list of units as it is created.
			SaveUnit.Lookups lookups = new(data.unitPrototypes, ExperienceLevels, data.players, data.Terraforms, DefaultExperienceLevel);
			data.mapUnits = Units.ConvertAll(unit => unit.ToMapUnit(lookups, data.map));

			// A unit that isn't on the map can't take part in the game (it
			// would have no tile to stand on), so it is left out. ToMapUnit
			// has already logged it.
			data.mapUnits.RemoveAll(unit => unit.location == Tile.NONE);

			// A unit can only be loaded on a unit that exists and shares its
			// tile. Saves made before cargo was cleaned up with its transport
			// or army can point at one that's gone, so let such units go free.
			Dictionary<ID, MapUnit> unitsById = new();
			foreach (MapUnit unit in data.mapUnits)
				unitsById.TryAdd(unit.id, unit);
			foreach (MapUnit unit in data.mapUnits.Where(u => u.loadedOnUnitId != null)) {
				if (!unitsById.TryGetValue(unit.loadedOnUnitId, out MapUnit carrier) || carrier.location != unit.location) {
					Serilog.Log.Warning("Unloading {Unit}, which was loaded on missing unit {Carrier}", unit, unit.loadedOnUnitId);
					unit.loadedOnUnitId = null;
				}
			}


			// once unit owners are known, players can reference units
			Dictionary<ID, List<MapUnit>> unitsByOwner = new();
			foreach (MapUnit unit in data.mapUnits) {
				ID ownerId = unit.owner.id;
				if (!unitsByOwner.TryGetValue(ownerId, out List<MapUnit> owned)) {
					owned = new List<MapUnit>();
					unitsByOwner[ownerId] = owned;
				}
				owned.Add(unit);
			}
			data.players.ForEach(player => {
				player.units = unitsByOwner.TryGetValue(player.id, out List<MapUnit> owned) ? new List<MapUnit>(owned) : new List<MapUnit>();
			});
		}

		private void ConvertCities(GameData data) {
			// cities require game map for location and players for city owner
			SaveCity.Lookups lookups = new(data.players, data.unitPrototypes, Civilizations, data.Buildings, CitizenTypes, data.Inflows);
			data.cities = Cities.ConvertAll(city => city.ToCity(data.map, lookups));

			// Once cities are known, players can reference cities.
			Dictionary<ID, List<City>> citiesByOwner = new();
			foreach (City city in data.cities) {
				ID ownerId = city.owner.id;
				if (!citiesByOwner.TryGetValue(ownerId, out List<City> owned)) {
					owned = new List<City>();
					citiesByOwner[ownerId] = owned;
				}
				owned.Add(city);
			}
			data.players.ForEach(player => {
				player.cities = citiesByOwner.TryGetValue(player.id, out List<City> owned) ? new List<City>(owned) : new List<City>();
				// A civ with units but no cities or settlers, that has never
				// founded a city, is one that started that way, as some
				// scenario civs do. (Any other civ in that state would have
				// been destroyed when it lost its last city or settler.)
				if (!player.IsIndependent && player.citiesFounded == 0 && player.cities.Count == 0
					&& player.units.Count > 0 && !player.units.Any(u => u.unitType.isSettler)) {
					player.neverHadCityOrSettler = true;
				}
			});
			// Saves from before receivedStartUnits was saved: the start units
			// came at the start of turn 1, to an AI with a city then, so one
			// with cities from turn 1 on is taken to have had them (it may
			// have founded its first city later and missed them, but handing
			// them out mid-game would be worse).
			for (int i = 0; i < Players.Count && i < data.players.Count; ++i) {
				if (Players[i].receivedStartUnits == null && data.turn >= 1 && data.players[i].cities.Count > 0) {
					data.players[i].receivedStartUnits = true;
				}
			}

			// Add references to map tiles. The units were placed on their tiles
			// as they were created.
			foreach (City city in data.cities) {
				data.map.tileAt(city.location.XCoordinate, city.location.YCoordinate).cityAtTile = city;
			}
		}

		// The AI's memory refers to units and cities, so it comes after them.
		private void ConvertAiState(GameData data) {
			for (int i = 0; i < Players.Count && i < data.players.Count; ++i) {
				C7Engine.AI.AiSaveState.Restore(data.players[i], data, Players[i].ai);
			}
		}

		private void ConvertBarbarianInfo(GameData data) {
			data.barbarianInfo = BarbarianInfo;

			if (BarbarianInfo.basicBarbarianUnit != null) {
				data.barbarianInfo.basicBarbarian = data.unitPrototypes.Where(up => up.name == data.barbarianInfo.basicBarbarianUnit).First();
			}
			if (BarbarianInfo.advancedBarbarianUnit != null) {
				data.barbarianInfo.advancedBarbarian = data.unitPrototypes.Where(up => up.name == data.barbarianInfo.advancedBarbarianUnit).First();
			}
			if (BarbarianInfo.barbarianSeaUnit != null) {
				data.barbarianInfo.barbarianSeaUnitProto = data.unitPrototypes.Where(up => up.name == data.barbarianInfo.barbarianSeaUnit).First();
			}
		}

		private void ConvertStrengthBonuses(GameData data) {
			foreach (StrengthBonus sb in StrengthBonuses) {
				switch (sb.description) {
					case "Fortified":
						data.fortificationBonus = sb;
						break;
					case "Behind river":
						data.riverCrossingBonus = sb;
						break;
					case "Town":
						data.cityLevel1DefenseBonus = sb;
						break;
					case "City":
						data.cityLevel2DefenseBonus = sb;
						break;
					case "Metropolis":
						data.cityLevel3DefenseBonus = sb;
						break;
				}
			}
		}

		private void ConvertHealRates(GameData data) {
			data.healRateInFriendlyField = HealRates["friendly_field"];
			data.healRateInNeutralField = HealRates["neutral_field"];
			data.healRateInHostileField = HealRates["hostile_field"];
			data.healRateInCity = HealRates["city"];
		}

		private void ConvertCultureGroups(GameData data) {
			foreach (var civilization in data.civilizations) {
				// because this might be initialized from the tests
				if (civilization.cultureGroup != null) continue;
				var cg = data.cultureGroups.FirstOrDefault(c => c.name == civilization.cultureGroupKey);
				if (cg == null) {
					throw new Exception(string.Format($"Culture group name `{civilization.cultureGroupKey}` for civilization {civilization.name} was not found in game data."));
				}
				civilization.SetCultureGroup(cg.index, cg.name);
			}
		}

		private void ConvertAlliances(GameData data) {
			foreach (var dataPlayer in data.players) {
				if (dataPlayer.alliance is null) continue; // null for barbarians
				Alliance alliance = data.alliances.FirstOrDefault(a => a.name == dataPlayer.alliance.name);
				dataPlayer.alliance = alliance;
			}
		}

		private void ConvertAllianceWars(GameData data) {
			foreach ((string one, string other) in AllAllianceWars()) {
				Alliance a = data.alliances?.FirstOrDefault(x => x.name == one);
				Alliance b = data.alliances?.FirstOrDefault(x => x.name == other);
				if (a != null && b != null && !data.allianceWars.Any(w => (w.Key == a && w.Value == b) || (w.Key == b && w.Value == a))) {
					data.allianceWars.Add(new(a, b));
				}
			}
		}

		// The alliance wars: AllianceWarPairs, or in older saves
		// AllianceWars (which holds one war per alliance).
		public List<(string, string)> AllAllianceWars() {
			if (AllianceWarPairs.Count > 0) {
				return AllianceWarPairs.Where(p => p?.Length == 2).Select(p => (p[0], p[1])).ToList();
			}
			return AllianceWars.Select(kvp => (kvp.Key, kvp.Value)).ToList();
		}

		// The version of the save format this build writes. Bump it when the
		// format changes in a way loading has to know about, and add a step
		// to Migrations to bring older saves up to date.
		public const int CurrentFormatVersion = 1;

		// The steps that bring a save from each format version to the next.
		// Format 0 is every save made before the format was versioned. Fields
		// added since (RngState, GameDifficultyIndex) are optional, and
		// loading copes without them, so there is nothing to rewrite.
		private static readonly Dictionary<int, Action<SaveGame>> Migrations = new() {
			[0] = save => { },
		};

		// Not used; kept so that older builds can still read the field.
		public string Version = "0.0.0";
		// The format version the save was written with, or null for one made
		// before saves were versioned.
		public int? FormatVersion;
		public int Seed = -1;
		// The state of the game's random number generator (see GameRandom),
		// or null in saves made before it was saved.
		public ulong? RngState;
		public int TurnNumber = 0;
		// Seconds of real time played (GameData.playedSeconds).
		public double PlayedSeconds;
		// GameData.fallenCivilizations, by name.
		public List<string> FallenCivilizations = [];
		public SaveMap Map = new SaveMap();
		public List<TerrainType> TerrainTypes = new List<TerrainType>();
		public List<SaveTerrainImprovement> TerrainImprovements = [];
		public List<Resource> Resources = new List<Resource>();
		public List<SaveUnit> Units = new List<SaveUnit>();
		public List<SaveUnitPrototype> UnitPrototypes = [];
		public List<SaveBuilding> Buildings = [];
		public List<SaveInflow> Inflows = [];
		public HashSet<string> GreatWondersBuilt = new();
		public List<SavePlayer> Players = new List<SavePlayer>();
		public List<SaveCity> Cities = new List<SaveCity>();
		public BarbarianInfo BarbarianInfo = new BarbarianInfo();
		public List<ExperienceLevel> ExperienceLevels = new List<ExperienceLevel>();
		public string DefaultExperienceLevel; // key
		public List<Civilization> Civilizations = new List<Civilization>();
		public HashSet<CultureGroup> CultureGroups = new HashSet<CultureGroup>();
		public HashSet<Alliance> Alliances = new HashSet<Alliance>();
		public Dictionary<string, string> AllianceWars = new Dictionary<string, string>();
		// Every pair of alliances locked at war, by name. AllianceWars keeps
		// one war per alliance, for older versions; older saves have only it.
		public List<string[]> AllianceWarPairs = new();
		public List<StrengthBonus> StrengthBonuses = new List<StrengthBonus>();
		public Dictionary<string, int> HealRates = new Dictionary<string, int>();
		public Rules Rules = new();
		public TimeOptions TimeOptions = new();
		public VictoryConditions VictoryConditions = new();
		public bool GameOver { get; set; }
		public SavePlayer Winner { get; set; }
		// See GameData.victoryBonus. Older saves, without it, have none.
		public int VictoryBonus { get; set; }
		// See GameData.nuclearEvents. Older saves, without it, have none.
		public int NuclearEvents { get; set; }
		// Older saves, without it, have the humans take turns one by one.
		public bool SimultaneousTurns { get; set; }
		// See GameData.playByEmail. Older saves, without it, aren't.
		public bool PlayByEmail { get; set; }
		// See GameData.debugMode. Older saves, without it, aren't in debug
		// mode.
		public bool DebugMode { get; set; }
		public UnitedNationsState UnitedNations = new();
		// A scenario's plague; saves from before it have none under way.
		public PlagueState Plague = new();
		// The turn's Staging (see C7Engine.Staging); null in games without it.
		public StagingState Staging;
		// The tide (see C7Engine.RisingTide); null in games without it.
		public RisingTideState RisingTide;
		// Civ3's tutorial (see Tutorial); null in games that never had it on,
		// and in saves made before it was saved.
		public TutorialState Tutorial;
		// The game's name and folder for its play log (see PlayLogInfo); null
		// in games that aren't recorded, and in saves made before it. Left out
		// of LAN snapshots (LanProtocol.StripHostSecrets).
		public PlayLogInfo PlayLog;
		// What a LAN host worked out for a guest that isn't sent all of the
		// game; null otherwise.
		public HostFacts HostFacts;
		public List<SaveTech> Techs = new();
		public List<CitizenType> CitizenTypes = new();
		public List<SaveTerraform> TerraForms = new();
		public List<Government> Governments = new();
		public List<WorldSize> WorldSizes = new();
		public Dictionary<string, List<HistTurnRecord>> History = new();
		// The events of the game for its replay (see GameHistory). Older
		// saves, without them, record their cities and borders as they stand
		// when loaded.
		public List<ReplayEvent> Replay = new();

		// The relative directory that can be used to find scenario-specific
		// assets.
		public string ScenarioSearchPath;

		// The scenario file the game began from, without its extension, as
		// "1 Mesopotamia"; none for a game that didn't. A win at one of
		// Conquests' campaigns goes into the Campaign Record by it.
		public string ScenarioName;

		// Civ3's Playable Civilizations, from a Civ3 scenario file (its GAME
		// section), by name; null when every civilization may play. The
		// scenario editor keeps them in Scenario.playableCivs instead (see
		// C7Engine.Editor.ScenarioRules.IsPlayable).
		public List<string> PlayableCivilizations;

		public List<Difficulty> Difficulties = new();
		public Difficulty GameDifficulty = new();
		// GameDifficulty's place in Difficulties, or null in saves made
		// before it was saved (see ResolveGameDifficulty).
		public int? GameDifficultyIndex;

		public GameMode.Config GameModeConfig = new("civ3");

		// What a scenario made in the scenario editor says about itself
		// (see C7Engine.Editor.ScenarioFiles); null in other saves.
		public C7Engine.Editor.ScenarioInfo Scenario;

		public void Save(string path) {
			FormatVersion ??= CurrentFormatVersion;
			byte[] json = JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);
			File.WriteAllBytes(path, json);
		}

		// Serializes without indentation, for sending over the network.
		public byte[] ToCompactJSON() {
			FormatVersion ??= CurrentFormatVersion;
			return JsonSerializer.SerializeToUtf8Bytes(this, CompactJsonOptions);
		}

		public static SaveGame FromJSON(byte[] json) {
			return Migrate(JsonSerializer.Deserialize<SaveGame>(json, JsonOptions));
		}

		// Brings a save just read up to the current format, or throws if it
		// was written by a newer build than this one.
		internal static SaveGame Migrate(SaveGame save) {
			if (save == null) {
				return null;
			}
			int version = save.FormatVersion ?? 0;
			if (version > CurrentFormatVersion) {
				throw new NotSupportedException($"The save is in format version {version}, but this build only reads up to version {CurrentFormatVersion}. It was made by a newer version of the game.");
			}
			for (; version < CurrentFormatVersion; ++version) {
				if (Migrations.TryGetValue(version, out Action<SaveGame> step)) {
					log.Information("Migrating the save from format version {From} to {To}", version, version + 1);
					step(save);
				}
			}
			save.FormatVersion = CurrentFormatVersion;
			return save;
		}

		// A deep copy of one of a save's items (a unit type, a building...),
		// as the save would write and read it.
		public static T DeepCopy<T>(T item) {
			byte[] json = JsonSerializer.SerializeToUtf8Bytes(item, JsonOptions);
			return JsonSerializer.Deserialize<T>(json, JsonOptions);
		}

		// Makes a deep copy of a SaveGame instance via JSON serialization
		public SaveGame Clone() {
			byte[] json = JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);
			return JsonSerializer.Deserialize<SaveGame>(json, JsonOptions);
		}

		// A save carries its own copy of the rules (buildings, units, terrain
		// improvements, governments and so on), and loading uses that copy
		// rather than the current ruleset. So a save made before some rule
		// data was imported, such as building flags, units' requiredGovernment
		// or BattleCreatedUnit, or the fallout terrain improvement, goes on
		// without it. Nothing fills it in from the current ruleset.
		public static SaveGame Load(string path, Func<string, string> getPediaIconsPath) {
			SaveGame result = LoadFromUtf8(File.ReadAllBytes(path));

			// This lambda has side effects in the Game.cs code.
			if (result.ScenarioSearchPath?.Count() > 0) {
				getPediaIconsPath(result.ScenarioSearchPath);
			}
			return result;
		}

		// Reads a save file's bytes as they are, rather than as text that
		// the JSON reader would have to turn back into UTF-8. A file that
		// isn't plain UTF-8 (as one saved by a text editor might not be) is
		// read as text, as before.
		private static SaveGame LoadFromUtf8(byte[] file) {
			ReadOnlySpan<byte> json = file;
			if (json.StartsWith(Utf8Bom)) {
				json = json[Utf8Bom.Length..];
			} else if (json.StartsWith(Utf16LittleEndianBom) || json.StartsWith(Utf16BigEndianBom)) {
				return LoadFromText(file);
			}
			try {
				return Migrate(JsonSerializer.Deserialize<SaveGame>(json, JsonOptions));
			} catch (Exception) when (!System.Text.Unicode.Utf8.IsValid(file)) {
				// Read as text, broken characters are replaced rather than
				// failing.
				return LoadFromText(file);
			}
		}

		private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];
		private static ReadOnlySpan<byte> Utf16LittleEndianBom => [0xFF, 0xFE];
		private static ReadOnlySpan<byte> Utf16BigEndianBom => [0xFE, 0xFF];

		// As File.ReadAllText reads it.
		private static SaveGame LoadFromText(byte[] file) {
			using StreamReader reader = new(new MemoryStream(file), System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
			return LoadFromJSON(reader.ReadToEnd());
		}

		internal static SaveGame LoadFromJSON(string json) {
			return Migrate(JsonSerializer.Deserialize<SaveGame>(json, JsonOptions));
		}
	}
}
