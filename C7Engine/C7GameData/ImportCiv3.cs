using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Serilog;
using QueryCiv3;
using QueryCiv3.Biq;
using C7GameData.Save;
using System.Reflection;
using C7Engine;
using static C7GameData.Tile.TileOverlays;
using GAME = QueryCiv3.Sav.GAME;
using Util = QueryCiv3.Util;

/*
  This will read a Civ3 sav into C7 native format for immediate use or saving to native JSON save
*/

namespace C7GameData {
	public class Civ3ExtraInfo {
		public int BaseTerrainFileID;
		public int BaseTerrainImageID;
	}

	public enum TerraformKey {
		BuildRoad,
		BuildRailroad,
		BuildMine,
		BuildFortress,
		ClearDamage,
		BuildAirfield,
		BuildRadarTower,
		BuildOutpost,
		BuildBarricade,
		Irrigate,
		ClearWetlands,
		ClearForest,
		PlantForest
	}

	public class ImportCiv3 {
		private SaveGame save;
		private BiqData biq;
		// The default BIQ (conquests.biq) fills in any sections a scenario's
		// BIQ leaves out. It is only loaded if one of those is needed.
		private BiqData defaultBiq {
			get {
				if (_defaultBiq == null && defaultBiqPath != null) {
					_defaultBiq = BiqData.LoadFile(defaultBiqPath);
				}
				return _defaultBiq;
			}
		}
		private BiqData _defaultBiq;
		private string defaultBiqPath;
		// Buildings that need a government, by the government's BIQ index.
		// Governments are imported after buildings, so their IDs are filled
		// in once they exist.
		private readonly List<(SaveBuilding building, int government)> buildingRequiredGovernments = new();
		private SavData savData;
		private PediaIcons pediaIcons;
		private readonly ID.Factory ids;
		private Dictionary<TerraformKey, ID> terraformIdByCiv3Key = [];

		private static ILogger log = Log.ForContext<ImportCiv3>();

		// The PRTO AI strategies the import keeps (see SaveUnitPrototype.Flag).
		private static readonly SaveUnitPrototype.Flag[] AIStrategyFlags = [
			SaveUnitPrototype.Flag.AIOffense, SaveUnitPrototype.Flag.AIDefense, SaveUnitPrototype.Flag.AINavalTransport,
			SaveUnitPrototype.Flag.AIArtillery, SaveUnitPrototype.Flag.AIExplore, SaveUnitPrototype.Flag.AIArmy,
			SaveUnitPrototype.Flag.AICruiseMissile, SaveUnitPrototype.Flag.AIAirBombard, SaveUnitPrototype.Flag.AIAirDefense,
			SaveUnitPrototype.Flag.AINavalPower, SaveUnitPrototype.Flag.AIAirTransport, SaveUnitPrototype.Flag.AINavalCarrier,
			SaveUnitPrototype.Flag.AITerraform, SaveUnitPrototype.Flag.AISettle, SaveUnitPrototype.Flag.AITacticalNuke,
			SaveUnitPrototype.Flag.AIICBM, SaveUnitPrototype.Flag.AINavalMissileTransport,
		];

		// The BIQ the civilizations (RACE) come from.
		private BiqData RaceBiq => biq.Race is null ? defaultBiq : biq;

		// The BIQ the leaders (LEAD, the scenario's player data) come from,
		// or null if there are none (as in a BIQ with only a map). A LEAD
		// refers to its civ by index, so the default BIQ's leaders are only
		// used along with its civs.
		private BiqData LeadBiq => biq.Lead is not null ? biq
			: biq.Race is null && defaultBiq?.Lead is not null ? defaultBiq
			: null;

		// The BIQ the scenario's game settings (GAME) come from.
		private BiqData GameBiq => biq.Game is not null ? biq : defaultBiq;

		private ImportCiv3() {
			save = new SaveGame();
			ids = new ID.Factory();
		}

		/// <summary>
		/// Items loaded from the BIQ and used the same way in both the SAV and BIQ should generally go here.
		/// This excludes items that can change mid-game, such as tiles (which may be chopped, roaded, etc.).
		/// </summary>
		/// <param name="theBiq">Source BIQ</param>
		/// <param name="c7Save">Destination C7 in-memory structure</param>
		private void ImportSharedBiqData() {
			RULE improvementRule = (biq.Rule ?? defaultBiq.Rule)[0];
			save.TerrainImprovements = SaveTerrainImprovement.Civ3Improvements(
				fortressDefenseBonus: improvementRule.FortressDefensiveBonus / 100.0,
				roadMovementCost: improvementRule.MovementAlongRoads > 0 ? 1.0f / improvementRule.MovementAlongRoads : 1.0f / 3).ToList();

			ImportTimeScale();
			ImportRaces();
			ImportCultureGroups();
			ImportTechs();
			ImportCiv3Resources();
			ImportTerraforms();
			ImportUnitPrototypes();
			ImportUnitUpgrades();
			ImportBuildings();
			ImportCiv3TerrainTypes();
			ImportWorldSizes();
			ImportCiv3ExperienceLevels();
			ImportCiv3DefensiveBonuses();
			save.HealRates["friendly_field"] = 1;
			save.HealRates["neutral_field"] = 1;
			save.HealRates["hostile_field"] = 0;
			save.HealRates["city"] = 2;
			save.ScenarioSearchPath = GameBiq?.Game?[0].ScenarioSearchFolders;
			// The GAME section's "Debug Mode" flag, which Civ3 reads when the
			// game starts (Civ3 FUN_004ff550, DAT_009e68f4).
			save.DebugMode = (GameBiq?.Game?[0].DebugMode ?? 0) != 0;
			ImportBarbarianInfo();
			ImportCitizenTypes();
			ImportGovernments();
			foreach ((SaveBuilding building, int government) in buildingRequiredGovernments) {
				if (government < save.Governments.Count) {
					building.requiredGovernment = save.Governments[government].id;
				}
			}
			ImportDifficulties();
			ImportRules();
			ImportFlavors();
		}

		public static SaveGame ImportSav(string savePath, string defaultBicPath, Func<string, string> getPediaIconsPath) {
			ImportCiv3 importer = new ImportCiv3();
			return importer.importSav(savePath, defaultBicPath, getPediaIconsPath);
		}

		private SaveGame importSav(string savePath, string defaultBicPath, Func<string, string> getPediaIconsPath) {
			// Get save data reader
			byte[] defaultBicBytes = Util.ReadFile(defaultBicPath);
			savData = new SavData(Util.ReadFile(savePath), defaultBicBytes);
			biq = savData.Bic;
			this.defaultBiqPath = defaultBicPath;
			pediaIcons = new(getPediaIconsPath(GameBiq?.Game?[0].ScenarioSearchFolders ?? ""));
			save.TurnNumber = savData.Game.TurnNumber;
			save.Seed = savData.Wrld.WorldSeed;

			ImportSharedBiqData();
			ImportSavLeaders();
			ImportSavUnits();
			ImportSavCities();
			if (savData.Game.DifficultyID >= 0 && savData.Game.DifficultyID < save.Difficulties.Count) {
				save.GameDifficulty = save.Difficulties[savData.Game.DifficultyID];
			}

			ImportSavHistory();
			ImportSavReplay();
			ImportSavVictory();
			save.Rules.AcceleratedProduction = savData.Game.AcceleratedProduction;
			save.Rules.AllowCultureFlips = savData.Game.AllowCulturalConversions;
			save.Rules.ScientificLeaders = savData.Game.AllowScientificLeaders;

			SetMapDimensions(savData, save);
			SetWorldWrap(savData, save);

			SetBarbarianActivity(savData, save);

			// Import tiles.  This is similar to, but different from the BIQ version as tile contents may have changed in-game.
			int i = 0;
			MapHoldings holdings = SavHoldings();
			foreach (QueryCiv3.Sav.TILE civ3Tile in savData.Tile) {
				// TODO: Civ3ExtraInfo can be removed when migrating to rendering via tilemap
				Civ3ExtraInfo extra = new Civ3ExtraInfo
				{
					BaseTerrainFileID = civ3Tile.TextureFile,
					BaseTerrainImageID = civ3Tile.TextureLocation,
				};

				(int X, int Y) = GetMapCoordinates(i, savData.Wrld.Width);
				SaveTile tile = new SaveTile{
					id = ids.CreateID("tile"),
					extraInfo = extra,
					X = X,
					Y = Y,
					continent = civ3Tile.Continent,
					baseTerrain = save.TerrainTypes[civ3Tile.BaseTerrain].Key,
					overlayTerrain = save.TerrainTypes[civ3Tile.OverlayTerrain].Key,
				};
				if (civ3Tile.BarbarianCamp >= 0) {
					tile.features.Add(BARBARIAN_CAMP);
				}
				if (civ3Tile.GoodyHut) {
					tile.features.Add(GOODY_HUT);
				}
				if (civ3Tile.HasRuins) {
					tile.overlays.Add(RUINS);
				}
				if (civ3Tile.Pollution) {
					tile.overlays.Add(POLLUTION);
				}
				if (civ3Tile.Craters) {
					tile.overlays.Add(CRATERS);
				}
				ImportHoldings(tile, civ3Tile.Fortress, civ3Tile.Barricade, civ3Tile.Airfield, civ3Tile.RadarTower, civ3Tile.Outpost, holdings);
				if (civ3Tile.BonusShield) {
					tile.features.Add("bonusShield");
				}
				if (civ3Tile.SnowCapped) {
					tile.features.Add("snowCapped");
				}
				if (civ3Tile.PineForest) {
					tile.features.Add("pineForest");
				}
				if (civ3Tile.RiverNorth) {
					tile.features.Add("riverNorth");
				}
				if (civ3Tile.RiverNortheast) {
					tile.features.Add("riverNortheast");
				}
				if (civ3Tile.RiverEast) {
					tile.features.Add("riverEast");
				}
				if (civ3Tile.RiverSoutheast) {
					tile.features.Add("riverSoutheast");
				}
				if (civ3Tile.RiverSouth) {
					tile.features.Add("riverSouth");
				}
				if (civ3Tile.RiverSouthwest) {
					tile.features.Add("riverSouthwest");
				}
				if (civ3Tile.RiverWest) {
					tile.features.Add("riverWest");
				}
				if (civ3Tile.RiverNorthwest) {
					tile.features.Add("riverNorthwest");
				}
				if (civ3Tile.Road) {
					tile.overlays.Add(ROAD);
				}
				if (civ3Tile.Railroad) {
					tile.overlays.Add(RAILROAD);
				}
				if (civ3Tile.Mine) {
					tile.overlays.Add(MINE);
				}
				if (civ3Tile.Irrigation) {
					tile.overlays.Add(IRRIGATION);
				}
				if (civ3Tile.ResourceID != -1) {
					tile.resource = save.Resources[civ3Tile.ResourceID].Key;
				}
				save.Map.tiles.Add(tile);
				for (int lead = 0; lead < savLeadPlayers.Length; lead++) {
					if (savLeadPlayers[lead] != null && civ3Tile.ExploredBy[lead]) {
						savLeadPlayers[lead].tileKnowledge.Add(new TileLocation(X, Y));
					}
				}
				i++;
			}
			MarkLakes();
			LogUnsupportedHoldings();

			// make barbarians unpickable
			save.Players.Where(p => p.isBarbarian).ToList().ForEach(p => p.canBePicked = false);

			return save;
		}

		// The tile improvements left out because their tile already had a
		// holding (see ImportHoldings), with how many tiles had each, logged
		// once the map is imported.
		private readonly Dictionary<string, int> unsupportedHoldings = new();

		// Where a Civ3 map's colonies, outposts, airfields and radar towers
		// are, by their (X, Y), with the ID of each one's owner (null if it
		// has none we know of).
		private sealed class MapHoldings {
			public readonly Dictionary<(int, int), ID> colonies = new();
			public readonly Dictionary<(int, int), ID> outposts = new();
			public readonly Dictionary<(int, int), ID> airfields = new();
			public readonly Dictionary<(int, int), ID> radarTowers = new();
		}

		// Imports the holdings layer (fortresses and the like) of a tile. A
		// barricade is an upgraded fortress. A colony is wherever the map's
		// list of colonies has one (see SavHoldings and BiqHoldings); an
		// outpost, airfield or radar tower is marked on the tile, and its
		// owner comes from the map's lists. A colony can share its tile with
		// a fortress, but C7 has a tile hold only one civ's holding, and one
		// of a fortress, outpost, airfield or radar tower; anything more is
		// left out.
		private void ImportHoldings(SaveTile tile, bool fortress, bool barricade, bool airfield, bool radarTower, bool outpost, MapHoldings holdings) {
			bool colony = holdings.colonies.TryGetValue((tile.X, tile.Y), out ID colonyOwner);
			string holding = barricade ? BARRICADE : fortress ? FORTRESS
				: colony ? null
				: airfield ? AIRFIELD : radarTower ? RADAR_TOWER : outpost ? OUTPOST : null;
			if (holding != null) {
				tile.overlays.Add(holding);
			}
			if (colony) {
				tile.overlays.Add(COLONY);
			}
			string owned = colony ? COLONY : holding is OUTPOST or AIRFIELD or RADAR_TOWER ? holding : null;
			if (owned != null) {
				tile.holdingOwner = owned switch {
					COLONY => colonyOwner,
					AIRFIELD => holdings.airfields.GetValueOrDefault((tile.X, tile.Y)),
					RADAR_TOWER => holdings.radarTowers.GetValueOrDefault((tile.X, tile.Y)),
					_ => holdings.outposts.GetValueOrDefault((tile.X, tile.Y)),
				};
				if (tile.holdingOwner == null) {
					log.Information("The {Holding} at ({X}, {Y}) belongs to no one", owned, tile.X, tile.Y);
				}
			}
			foreach ((bool has, string name) in new[] {
				(airfield && holding != AIRFIELD, "airfield"), (radarTower && holding != RADAR_TOWER, "radar tower"),
				(outpost && holding != OUTPOST, "outpost") }) {
				if (has) {
					unsupportedHoldings[name] = unsupportedHoldings.GetValueOrDefault(name) + 1;
				}
			}
		}

		// A SAV's colonies, outposts, airfields and radar towers. The CLNY,
		// OUTP, AIBS and RADT sections give each one's place and its player's
		// index into LEAD. (A tile's ColonyID is set for all of them, so it
		// doesn't tell them apart: in the SAVs looked at, the tiles with an
		// outpost had the ColonyID of an OUTP entry, those with a colony a
		// CLNY one.)
		private MapHoldings SavHoldings() {
			MapHoldings holdings = new();
			ID Owner(int lead) {
				return lead >= 0 && lead < savLeadPlayers.Length ? savLeadPlayers[lead]?.id : null;
			}
			foreach (QueryCiv3.Sav.CLNY colony in savData.Clny ?? []) {
				holdings.colonies.TryAdd((colony.XCoordinate, colony.YCoordinate), Owner(colony.PlayerID));
			}
			foreach (QueryCiv3.Sav.OUTP outpost in savData.Outp ?? []) {
				holdings.outposts.TryAdd((outpost.XCoordinate, outpost.YCoordinate), Owner(outpost.PlayerID));
			}
			foreach (QueryCiv3.Sav.AIBS airfield in savData.Aibs ?? []) {
				holdings.airfields.TryAdd((airfield.XCoordinate, airfield.YCoordinate), Owner(airfield.PlayerID));
			}
			foreach (QueryCiv3.Sav.RADT radarTower in savData.Radt ?? []) {
				holdings.radarTowers.TryAdd((radarTower.XCoordinate, radarTower.YCoordinate), Owner(radarTower.PlayerID));
			}
			return holdings;
		}

		// A BIQ map's colonies and the owners of its outposts, airfields and
		// radar towers, from its CLNY section, whose entries have an
		// improvement type. The scenarios
		// that come with Conquests show 0 is a colony (Fall of Rome's one
		// entry is on a tile without an airfield, radar tower or outpost),
		// and 1 and 2 airfields and radar towers (WWII in the Pacific has two
		// airfields, with two entries of type 1, and seven radar towers, with
		// seven of type 2). A tile's Colony field isn't an index into the
		// CLNY: scenarios without any CLNY entries have it set.
		// UNVERIFIED: that an outpost placed in the editor has an entry (of
		// type 3) for its owner; none of the scenarios has an outpost. One
		// without an entry belongs to no one.
		private MapHoldings BiqHoldings() {
			MapHoldings holdings = new();
			foreach (QueryCiv3.Biq.CLNY entry in biq.Clny ?? []) {
				(string name, Dictionary<(int, int), ID> list) = entry.ImprovementType switch {
					0 => ("colony", holdings.colonies),
					1 => ("airfield", holdings.airfields),
					2 => ("radar tower", holdings.radarTowers),
					_ => ("outpost", holdings.outposts),
				};
				ID owner = BiqOwner(entry.OwnerType, entry.Owner, $"{name} at ({entry.X}, {entry.Y})")?.id;
				list.TryAdd((entry.X, entry.Y), owner);
			}
			return holdings;
		}

		private void LogUnsupportedHoldings() {
			foreach (KeyValuePair<string, int> holding in unsupportedHoldings) {
				log.Warning("The map has {Count} tiles with a {Improvement} on a tile that already has a holding; they are left out", holding.Value, holding.Key);
			}
		}

		// Marks the tiles of small bodies of water as fresh water lakes, as
		// GameMap.recomputeContinents does for generated maps. Civ3 gives each
		// body of water its own continent number.
		private void MarkLakes() {
			HashSet<string> waterTerrains = save.TerrainTypes.Where(t => t.IsWater).Select(t => t.Key).ToHashSet();
			foreach (IGrouping<int, SaveTile> body in save.Map.tiles.Where(t => waterTerrains.Contains(t.baseTerrain)).GroupBy(t => t.continent)) {
				if (body.Count() <= GameMap.MaxFreshWaterLakeTiles) {
					foreach (SaveTile t in body) {
						t.isFreshWater = true;
					}
				}
			}
		}

		private void ImportSavHistory() {
			foreach (SavePlayer player in savLeadPlayers.Where(p => p != null && !p.isBarbarian)) {
				save.History[player.id.ToString()] = new List<HistTurnRecord>();
			}

			foreach (var turn in savData.HistTurn) {
				var civs = savData.TurnCiv[turn.TurnNumber];
				var powers = savData.TurnPower[turn.TurnNumber];
				var scores = savData.TurnScore[turn.TurnNumber];
				var cultures = savData.TurnCulture[turn.TurnNumber];
				var vps = savData.TurnVP[turn.TurnNumber];
				foreach ((int civ, int idx) in civs.Select((civ, idx) => (civ, idx))) {
					// The history lists civs by LEAD, including ones that
					// joined the game after the first turn.
					SavePlayer player = SavPlayer(civ);
					if (player == null || !save.History.TryGetValue(player.id.ToString(), out List<HistTurnRecord> history)) {
						continue;
					}
					history.Add(new HistTurnRecord {
						Date = turn.Date,
						Power = powers[idx],
						Score = scores[idx],
						Culture = cultures[idx],
						VP = vps == null ? 0 : vps[idx]
					});
				}
			}
		}

		// The SAV's replay (its RPLS section) as the game's replay events (see
		// GameHistory). Civs are LEAD indexes; a territory change to or from
		// LEAD 0 is to or from no one (Civ3 FUN_00598f60 clears the tile's
		// owner for civ 0).
		private void ImportSavReplay() {
			if (savData.Rplt == null) {
				return;
			}
			for (int i = 0; i < savData.Rplt.Length; ++i) {
				int turn = savData.Rplt[i].TurnNumber;
				for (int j = 0; j < savData.RpltRple[i].Length; ++j) {
					QueryCiv3.Sav.RPLE e = savData.RpltRple[i][j];
					ReplayEventType type = (ReplayEventType)e.EventType;
					if (!Enum.IsDefined(type)) {
						continue;
					}
					bool territory = type == ReplayEventType.TerritoryChanged;
					string text = savData.RpltRpleDescription[i][j];
					save.Replay.Add(new ReplayEvent {
						Turn = turn,
						Type = type,
						Civ = ReplayCiv(e.CivID, territory),
						X = e.XLocation,
						Y = e.YLocation,
						Text = string.IsNullOrEmpty(text) ? null : text,
						PreviousCiv = territory ? ReplayCiv(e.PreviousOwner, true) : null,
					});
				}
			}
		}

		private string ReplayCiv(int lead, bool territory) {
			if (territory && lead == 0) {
				return null;
			}
			return SavPlayer(lead)?.id.ToString();
		}

		/**
		 * defaultBiqPath is used in case some sections (map, rules, player data) are not
		 * present.
		 */
		public static SaveGame ImportBiq(string biqPath, string defaultBiqPath, Func<string, string> getPediaIconsPath) {
			ImportCiv3 importer = new ImportCiv3();
			return importer.importBiq(biqPath, defaultBiqPath, getPediaIconsPath);
		}

		private SaveGame importBiq(string biqPath, string defaultBiqPath, Func<string, string> getPediaIconsPath) {
			biq = BiqData.LoadFile(biqPath);
			this.defaultBiqPath = defaultBiqPath;
			pediaIcons = new(getPediaIconsPath(GameBiq?.Game?[0].ScenarioSearchFolders ?? ""));

			// A BIQ may have no map, only rules. Then the map and the players
			// are left for GameSetup.Populate to make.
			bool hasMap = biq.Wmap?.Length > 0 && biq.Tile?.Length > 0;

			// The game's random numbers start from the map's seed. Civ3's
			// "Preserve random seed" doesn't choose this seed: it "means that
			// the randomly generated number for something such as a battle or
			// anarchy calculation will be stored in the game so that you
			// cannot save your game and reload continuously to get a favored
			// outcome" (https://civfanatics.com/civ3/faq/). C7 always saves
			// its random state (GameRandom), so it always behaves that way.
			// Without a map, GameSetup.Populate sets the seed of the map it
			// generates.
			save.Seed = hasMap ? biq.Wmap[0].MapSeed : Random.Shared.Next(int.MaxValue);

			ImportSharedBiqData();
			ImportBiqVictory();
			if (hasMap) {
				ImportBicLeaders();
				ImportBicUnits();
				ImportBicCities();
				ImportEmbassies();
				ImportAlliances();
			} else {
				log.Information("{Biq} has no map, so the map and players will be generated", biqPath);
			}

			SetMapDimensions(biq, save);
			SetWorldWrap(biq, save);

			// Import tiles
			int i = 0;
			MapHoldings holdings = hasMap ? BiqHoldings() : new();
			bool mapVisible = GameBiq?.Game?[0].MapVisible == 1;
			foreach (QueryCiv3.Biq.TILE civ3Tile in hasMap ? biq.Tile : Array.Empty<QueryCiv3.Biq.TILE>()) {
				(int X, int Y) = GetMapCoordinates(i, biq.Wmap[0].Width);
				Civ3ExtraInfo extra = new Civ3ExtraInfo
				{
					BaseTerrainFileID = civ3Tile.TextureFile,
					BaseTerrainImageID = civ3Tile.TextureLocation,
				};
				SaveTile tile = new SaveTile{
					id = ids.CreateID("tile"),
					extraInfo = extra,
					X = X,
					Y = Y,
					continent = civ3Tile.Continent,
					baseTerrain = save.TerrainTypes[civ3Tile.BaseTerrain].Key,
					overlayTerrain = save.TerrainTypes[civ3Tile.OverlayTerrain].Key,
				};
				if (civ3Tile.BarbarianCamp) {
					tile.features.Add(BARBARIAN_CAMP);
				}
				if (civ3Tile.GoodyHut) {
					tile.features.Add(GOODY_HUT);
				}
				// "0: VPL, -1: Not a VPL"
				if (civ3Tile.VictoryPointLocation == 0) {
					tile.features.Add(SaveTile.VictoryLocationFeature);
				}
				if (civ3Tile.Ruin > 0) {
					tile.overlays.Add(RUINS);
				}
				if (civ3Tile.Pollution) {
					tile.overlays.Add(POLLUTION);
				}
				if (civ3Tile.Craters) {
					tile.overlays.Add(CRATERS);
				}
				ImportHoldings(tile, civ3Tile.Fortress, civ3Tile.Barricade, civ3Tile.Airfield, civ3Tile.RadarTower, civ3Tile.Outpost, holdings);
				if (civ3Tile.BonusGrassland) {
					tile.features.Add("bonusShield");
				}
				if (civ3Tile.SnowCappedMountain) {
					tile.features.Add("snowCapped");
				}
				if (civ3Tile.PineForest) {
					tile.features.Add("pineForest");
				}
				if (civ3Tile.RiverNorth) {
					tile.features.Add("riverNorth");
				}
				if (civ3Tile.RiverConnectionNortheast) {
					tile.features.Add("riverNortheast");
				}
				if (civ3Tile.RiverEast) {
					tile.features.Add("riverEast");
				}
				if (civ3Tile.RiverConnectionSoutheast) {
					tile.features.Add("riverSoutheast");
				}
				if (civ3Tile.RiverSouth) {
					tile.features.Add("riverSouth");
				}
				if (civ3Tile.RiverConnectionSouthwest) {
					tile.features.Add("riverSouthwest");
				}
				if (civ3Tile.RiverWest) {
					tile.features.Add("riverWest");
				}
				if (civ3Tile.RiverConnectionNorthwest) {
					tile.features.Add("riverNorthwest");
				}
				if (civ3Tile.Road) {
					tile.overlays.Add(ROAD);
				}
				if (civ3Tile.Railroad) {
					tile.overlays.Add(RAILROAD);
				}
				if (civ3Tile.Mine) {
					tile.overlays.Add(MINE);
				}
				if (civ3Tile.Irrigation) {
					tile.overlays.Add(IRRIGATION);
				}
				if (civ3Tile.Resource != -1) {
					tile.resource = save.Resources[civ3Tile.Resource].Key;
				}

				// Some tiles are known ahead of time, like all of europe in age of
				// discovery. Other scenarios set the entire map to be visible.
				//
				// Add those tiles ahead of time.
				if (civ3Tile.FogOfWar != 0 || mapVisible) {
					for (int playerIndex = 0; playerIndex < save.Players.Count; playerIndex++) {
						SavePlayer player = save.Players[playerIndex];
						player.tileKnowledge.Add(new TileLocation(X, Y));
					}
				}

				save.Map.tiles.Add(tile);
				i++;
			}
			MarkLakes();
			LogUnsupportedHoldings();

			// The rest of the fog of war is done unit by unit; each unit can see their
			// own tile and the neighbor tiles.
			//
			// This will eventually need to handle hills and other tiles that can see
			// further.
			Dictionary<ID, SavePlayer> playerLookup = new();
			foreach (SavePlayer player in save.Players) {
				playerLookup[player.id] = player;
			}

			foreach (SaveUnit unit in save.Units) {
				SavePlayer player = playerLookup[unit.owner];
				player.tileKnowledge.Add(unit.currentLocation);
				foreach (TileDirection direction in TileDirectionExtensions.All) {
					player.tileKnowledge.Add(Tile.NeighborCoordinate(unit.currentLocation, direction));
				}
			}
			foreach (SaveCity city in save.Cities) {
				SavePlayer player = playerLookup[city.owner];
				player.tileKnowledge.Add(city.location);
				foreach (TileDirection direction in TileDirectionExtensions.All) {
					player.tileKnowledge.Add(Tile.NeighborCoordinate(city.location, direction));
				}
			}

			// Remove any unplayable Players, except Barbarians,
			// ex. Mongols in `4 Middle Ages.biq` scenario.
			// We only need to do this in the .biq files, not the .sav,
			// because .sav files already contain just the playable Players + Barbarians.
			// It's easier to do it like this, otherwise we need to manipulate the biq arrays,
			// to check for playable players, offset array indexes by the difference, etc,
			// as we are parsing the file, which is 10 times the hassle compared to this approach
			save.Players = save.Players.Where(p => p.isBarbarian || p.isIncludedInGame).ToList();
			HashSet<ID> remaining = save.Players.Select(p => p.id).ToHashSet();
			int unitsLeftOut = save.Units.RemoveAll(u => !remaining.Contains(u.owner));
			int citiesLeftOut = save.Cities.RemoveAll(c => !remaining.Contains(c.owner));
			if (unitsLeftOut > 0 || citiesLeftOut > 0) {
				log.Warning("Left out {Units} units and {Cities} cities of civs that aren't in the game", unitsLeftOut, citiesLeftOut);
			}

			// make barbarians unpickable
			save.Players.Where(p => p.isBarbarian).ToList().ForEach(p => p.canBePicked = false);

			return save;
		}

		static (int, int) GetMapCoordinates(int tileIndex, int mapWidth) {
			int Y = tileIndex / (mapWidth / 2);
			int X = tileIndex % (mapWidth / 2) * 2 + (Y % 2);
			return (X, Y);
		}

		private void ImportTimeScale() {
			var games = GameBiq?.Game;
			if (games == null || games.Length == 0) {
				log.Warning("No GAME section to take the time scale from; using the defaults");
				return;
			}
			save.TimeOptions = new TimeOptions() {
				baseUnit = (TimeUnit)games[0].BaseTimeUnit,
				startYear = games[0].StartYear,
				startMonth = games[0].StartMonth,
				startWeek = games[0].StartWeek,
				turnLimit = TurnLimit(games[0]),
				negativeLabel = "BC",
				positiveLabel = "AD",
			};

			save.TimeOptions.timeScale = new int[2, 8];

			for (int i = 0; i < 7; ++i) {
				var turns = games[0].TimescaleNumberOfTurns[i];
				var units = games[0].TurnNumberOfTimeUnits[i];
				save.TimeOptions.timeScale[0, i] = turns;
				save.TimeOptions.timeScale[1, i] = units;
			}

			// Add some extra padding at the end to allow continuing playing 1 turn at a time.
			// CivIII allows dates from -10000 to 10000, so 50000 is enough to cover for that,
			// and for our custom scenarios from now on, this value is easily editable in the json
			save.TimeOptions.timeScale[0, 7] = 50000;
			save.TimeOptions.timeScale[1, 7] = 1;

		}

		// The turn the game ends on: the GAME section's turn limit, as before
		// eb8aad18. A limit of 0 means none (see TimeLimitVictory).
		//
		// Civ3's "standard game length is 540 turns"
		// (https://civfanatics.com/civ3/faq/), which conquests.biq stores
		// as a turn limit of 540, with its "use time limit" flag clear. The
		// time scale can't be summed instead: the BIQ lists only 7 of its
		// segments, 440 turns in a standard game, and "Whatever turns isn't
		// listed...is assigned to 1 unit (year, month, week) per turn"
		// (https://forums.civfanatics.com/threads/time-options-time-scale.456771/).
		// UNVERIFIED (no Civ3 source found): what the "use time limit" flag
		// does. Every BIQ that ships with Conquests that leaves it clear has
		// a turn limit of 540.
		private static int TurnLimit(QueryCiv3.Biq.GAME game) {
			return Math.Max(0, game.TurnTimeLimit);
		}

		// The victory conditions of a scenario. A scenario either uses
		// Civ3's default ones or sets its own.
		private void ImportBiqVictory() {
			QueryCiv3.Biq.GAME[] games = GameBiq?.Game;
			if (games == null || games.Length == 0 || games[0].DefaultVictoryConditions != 0) {
				save.VictoryConditions = VictoryConditions.NewGameDefaults();
				// A scenario that turns on its time limit keeps it.
				save.VictoryConditions.UseTurnLimit = games != null && games.Length > 0 && games[0].UseTimeLimit != 0;
				if (games != null && games.Length > 0) {
					ImportInstantElimination(save.VictoryConditions, games[0]);
				}
				return;
			}
			QueryCiv3.Biq.GAME game = games[0];
			save.Rules ??= new Rules();
			save.Rules.CulturallyLinkedStart = game.CulturallyLinkedStart;
			save.Rules.RespawnAiPlayers = game.RespawnAiPlayers;
			save.Rules.PreserveRandomSeed = game.PreserveRandomSeed;
			save.Rules.RetainCultureOnCapture = game.RetainCulture != 0;
			save.VictoryConditions = new VictoryConditions {
				// A scenario that turns on its time limit keeps it.
				UseTurnLimit = game.UseTimeLimit != 0,
				AllowDominationVictory = game.DominationVictory,
				AllowSpaceRaceVictory = game.SpaceRaceVictory,
				AllowDiplomaticVictory = game.DiplomaticVictory,
				AllowConquestVictory = game.ConquestVictory,
				AllowCulturalVictory = game.CulturalVictory,

				AllowWonderVictory = game.WonderVictory,

				CityElimination = game.CityElimination,
				Regicide = game.Regicide,
				MassRegicide = game.MassRegicide,
				VictoryLocations = game.VictoryLocations,
				CaptureTheFlag = game.CaptureTheFlag,
				ReverseCaptureTheFlag = game.ReverseCaptureTheFlag,
			};
			// Keep the defaults if the game doesn't set the thresholds.
			if (game.DominationTerrain > 0) {
				save.VictoryConditions.DominationTerritoryPercent = game.DominationTerrain;
			}
			if (game.DominationPopulation > 0) {
				save.VictoryConditions.DominationPopulationPercent = game.DominationPopulation;
			}
			ImportCultureWin(save.VictoryConditions, game.OneCityCultureWin, game.AllCitiesCultureWin);
			ImportInstantElimination(save.VictoryConditions, game);
		}

		// The GAME section's "autoplace kings" (Regicide) and "city
		// elimination count" (Elimination). Keep the default count if the
		// game doesn't set one.
		private static void ImportInstantElimination(VictoryConditions conditions, QueryCiv3.Biq.GAME game) {
			conditions.AutoPlaceKings = game.AutoPlaceKings != 0;
			conditions.AutoPlaceVictoryLocations = game.AutoPlaceVictoryLocations != 0;
			conditions.AutoPlaceCaptureUnits = game.PlaceCaptureUnits != 0;
			conditions.AnyoneCapturesAnyFlag = game.CaptureAnyFlag != 0;
			conditions.RespawnFlagUnits = game.RespawnFlagUnits != 0;
			conditions.CaptureFlagVictoryPoints = System.Math.Max(0, game.CapturingSpecialUnit);
			conditions.GoldForCapture = System.Math.Max(0, game.GoldForCapture);
			conditions.VictoryLocationPoints = Math.Max(0, game.VictoryPointScoring);
			if (game.CityEliminationCount > 0) {
				conditions.CityEliminationCount = game.CityEliminationCount;
			}
			ImportVictoryPoints(conditions, game.VictoryPointLimit, game.WonderCost, game.DefeatingOpposingUnitCost,
				game.AdvancementCost, game.CityConquestPopulation);
		}

		// The GAME section's victory point limit and multiples (see
		// VictoryPoints). A multiple of 0 scores nothing, as in the
		// Napoleonic Europe conquest, which has none for wonders.
		private static void ImportVictoryPoints(VictoryConditions conditions, int limit, int wonder, int defeat, int advance, int cityConquest) {
			if (limit > 0) {
				conditions.VictoryPointLimit = limit;
			}
			conditions.WonderVictoryPoints = Math.Max(0, wonder);
			conditions.DefeatVictoryPoints = Math.Max(0, defeat);
			conditions.AdvanceVictoryPoints = Math.Max(0, advance);
			conditions.CityConquestVictoryPoints = Math.Max(0, cityConquest);
		}

		// The culture a cultural victory needs (see CulturalVictory), from
		// the GAME section's "one city culture win" and "all cities culture
		// win". Keep the defaults if the game doesn't set them.
		private static void ImportCultureWin(VictoryConditions conditions, int oneCity, int allCities) {
			if (oneCity > 0) {
				conditions.CultureOneCityWin = oneCity;
			}
			if (allCities > 0) {
				conditions.CultureAllCitiesWin = allCities;
			}
		}

		private void ImportSavVictory() {
			var game = savData.Game;

			save.VictoryConditions = new VictoryConditions {
				AllowDominationVictory = game.DominationVictory,
				AllowSpaceRaceVictory = game.SpaceRaceVictory,
				AllowDiplomaticVictory = game.DiplomaticVictory,
				AllowConquestVictory = game.ConquestVictory,
				AllowCulturalVictory = game.CulturalVictory,

				AllowWonderVictory = game.WonderVictory,

				CityElimination = game.CityElimination,
				Regicide = game.Regicide,
				MassRegicide = game.MassRegicide,
				VictoryLocations = game.VictoryLocations,
				CaptureTheFlag = game.CaptureTheFlag, // 'Capture the Unit', 'Capture the Princess'
				ReverseCaptureTheFlag = game.ReverseCaptureTheFlag,

				// A game under way already has its kings and victory
				// locations.
				AutoPlaceKings = false,
				AutoPlaceVictoryLocations = false,
				VictoryLocationPoints = Math.Max(0, game.VictoryPointScoring),
			};
			if (game.CityEliminationCount > 0) {
				save.VictoryConditions.CityEliminationCount = game.CityEliminationCount;
			}
			ImportVictoryPoints(save.VictoryConditions, game.VPLimit, game.WonderCost, game.DefeatingOpposingUnitCost,
				game.AdvancementCost, game.CityConquestPopulation);
			// Keep the defaults if the game doesn't set the thresholds.
			if (game.DominationTerrain > 0) {
				save.VictoryConditions.DominationTerritoryPercent = game.DominationTerrain;
			}
			if (game.DominationPopulation > 0) {
				save.VictoryConditions.DominationPopulationPercent = game.DominationPopulation;
			}
			ImportCultureWin(save.VictoryConditions, game.OneCityCultureWin, game.AllCitiesCultureWin);

			if (game.Winner > -1) {
				// TODO: translate victory type
				save.Winner = SavPlayer(game.Winner);
				save.GameOver = true;
				log.Warning("This game is over: {Winner} won, by an unknown kind of victory ({Type})", save.Winner?.civilization ?? "an unknown civ", game.VictoryType);
			}

		}

		private void ImportCiv3Resources() {
			GOOD[] Good = biq?.Good ?? defaultBiq.Good;
			// A scenario may have two resources by one name (MPTournamentMDJ
			// has two Whales, two Wheat, ...); the second's key gets a
			// number, as everything refers to a resource by its key.
			HashSet<string> keys = new();
			foreach (GOOD good in Good) {
				string key = good.Name;
				for (int n = 2; !keys.Add(key); ++n) {
					key = $"{good.Name} {n}";
				}
				Resource resource = new Resource {
					Key = key,
					Name = good.Name,
					Icon = good.Icon,
					FoodBonus = good.FoodBonus,
					ShieldsBonus = good.ShieldsBonus,
					CommerceBonus = good.CommerceBonus,
					AppearanceRatio = good.AppearanceRatio,
					DisappearanceRatio = good.DisappearanceProbability,
					CivilopediaEntry = good.CivilopediaEntry,
					Category = good.Type switch {
						0 => ResourceCategory.BONUS,
						1 => ResourceCategory.LUXURY,
						2 => ResourceCategory.STRATEGIC,
						_ => ResourceCategory.NONE,
					}
				};
				if (resource.Category == ResourceCategory.NONE) {
					log.Warning("WARNING!  Unknown resource category for " + good);
				}
				if (good.Prerequisite > -1) {
					resource.Prerequisite = save.Techs[good.Prerequisite].id;
				}

				save.Resources.Add(resource);
			}
		}

		private void ImportCultureGroups() {
			BiqData theBiq = biq.Race is null ? defaultBiq : biq;
			HashSet<CultureGroup> cultureGroups = new HashSet<CultureGroup>();
			HashSet<int> cultureGroupsIndexes = new HashSet<int>();
			foreach (RACE race in theBiq.Race) {
				if (cultureGroupsIndexes.Add(race.CultureGroup)) {
					var cg = new CultureGroup() {
						index = race.CultureGroup,
						name = GetCultureGroupIdentifier(race.CultureGroup),
					};
					cultureGroups.Add(cg);
				}
			}
			save.CultureGroups = cultureGroups.OrderBy(c => c.index).ToHashSet();
		}

		private void ImportRaces() {
			BiqData theBiq = biq.Race is null ? defaultBiq : biq;
			int i = 0;
			foreach (RACE race in theBiq.Race) {
				Civilization civ = new Civilization{
					name = race.Name,
					noun = race.Noun,
					adjective = race.Adjective,
					leader = race.LeaderName,
					leaderTitle = race.LeaderTitle,
					leaderGender = race.LeaderGender == 0 ? Gender.Male : Gender.Female,
					primaryColorIndex = race.DefaultColor,
					secondaryColorIndex = race.UniqueColor,
					isBarbarian = i == 0 ? true : false,
				};
				foreach (RACE_City city in theBiq.RaceCityName[i]) {
					civ.cityNames.Add(city.Name);
				}
				civ.traits = LoadCivTraits(race).ToHashSet();
				civ.cultureGroupKey = GetCultureGroupIdentifier(race.CultureGroup);
				civ.aggressionLevel = race.AggressionLevel;
				civ.favoriteGovernment = GovernmentName(race.FavoriteGovernment);
				civ.shunnedGovernment = GovernmentName(race.ShunnedGovernment);
				civ.civilopediaEntry = race.CivilopediaEntry?.Trim();
				civ.kingUnit = KingUnitName(race.UnitTypeForKing);
				LoadBuildFlags(race, civ);
				civ.greatLeaderNames = theBiq.RaceGreatLeaderName?[i]?.Select(n => n.Name).ToList() ?? [];
				civ.scientificLeaderNames = theBiq.RaceScientificLeaderName?[i]?.Select(n => n.Name).ToList() ?? [];
				civ.pluralName = race.Plurality != 0;
				civ.nameGender = race.CivilizationGender == 0 ? Gender.Male : Gender.Female;
				civ.diplomacyTextIndex = race.DiplomacyTextIndex;
				civ.governorManagesCitizens = race.ManageCitizens;
				civ.governorEmphasizesFood = race.EmphasizeFood;
				civ.governorEmphasizesShields = race.EmphasizeShields;
				civ.governorEmphasizesTrade = race.EmphasizeTrade;
				civ.governorManagesProduction = race.ManageProduction;
				civ.governorNoWonders = race.NoWonders;
				civ.governorNoSmallWonders = race.NoSmallWonders;

				// Look up the image for non-barbarian civs.
				string artName = pediaIcons.GetLeaderArtName(race.CivilopediaEntry);
				if (artName != null) {
					civ.leaderArtFile = artName;
				}

				save.Civilizations.Add(civ);
				i++;
			}
		}

		// The civ's build-often and build-never flags (RACE Flags[8..9] and
		// [12..13]).
		private static void LoadBuildFlags(RACE race, Civilization civ) {
			(PlanFocus? focus, bool never, bool often)[] flags = [
				(PlanFocus.Offense, race.OffensiveLandUnitsNever, race.OffensiveLandUnitsOften),
				(PlanFocus.Defense, race.DefensiveLandUnitsNever, race.DefensiveLandUnitsOften),
				(PlanFocus.Artillery, race.ArtilleryLandUnitsNever, race.ArtilleryLandUnitsOften),
				(PlanFocus.Settlers, race.SettlersNever, race.SettlersOften),
				(PlanFocus.Workers, race.WorkersNever, race.WorkersOften),
				(PlanFocus.Naval, race.NavalUnitsNever, race.NavalUnitsOften),
				(PlanFocus.Air, race.AirUnitsNever, race.AirUnitsOften),
				(PlanFocus.Growth, race.GrowthNever, race.GrowthOften),
				(PlanFocus.Production, race.ProductionNever, race.ProductionOften),
				(PlanFocus.Happiness, race.HappinessNever, race.HappinessOften),
				(PlanFocus.Science, race.ScienceNever, race.ScienceOften),
				(PlanFocus.Wealth, race.WealthNever, race.WealthOften),
				(PlanFocus.Trade, race.TradeNever, race.TradeOften),
				(PlanFocus.Culture, race.CultureNever, race.CultureOften),
				(null, race.ExploreNever, race.ExploreOften),
			];
			foreach ((PlanFocus? focus, bool never, bool often) in flags) {
				if (never) {
					++civ.buildFlagCount;
				}
				if (often) {
					++civ.buildFlagCount;
				}
				if (focus is PlanFocus f) {
					if (never) {
						civ.buildNever.Add(f);
					}
					if (often) {
						civ.buildOften.Add(f);
					}
				}
			}
		}

		// The name of the BIQ's unit with this index, which a civ's King is
		// (the RACE section's "unit type for king"), or null for none.
		private string KingUnitName(int index) {
			PRTO[] prtos = biq.Prto ?? defaultBiq.Prto;
			return index >= 0 && index < prtos.Length ? prtos[index].Name : null;
		}

		// The name of the BIQ's government with this index, or null for none.
		private string GovernmentName(int index) {
			BiqData theBiq = biq.Govt is null ? defaultBiq : biq;
			return index >= 0 && index < theBiq.Govt.Length ? theBiq.Govt[index].Name : null;
		}

		private static string GetCultureGroupIdentifier(int cultureGroupIndex) {
			if (cultureGroupIndex == -1) return "None"; // Barbarians
			if (cultureGroupIndex == 0) return "American";
			if (cultureGroupIndex == 1) return "European";
			if (cultureGroupIndex == 2) return "Mediterranean";
			if (cultureGroupIndex == 3) return "Mid East";
			if (cultureGroupIndex == 4) return "Asian";
			log.Error("The culture group index {CultureGroupIndex} is invalid. Defaulting to `American`.", cultureGroupIndex);
			return "American";
		}

		private static IEnumerable<Civilization.Trait> LoadCivTraits(RACE race) {
			return new[] {
				(race.Militaristic, Civilization.Trait.Militaristic),
				(race.Commercial, Civilization.Trait.Commercial),
				(race.Expansionist, Civilization.Trait.Expansionist),
				(race.Scientific, Civilization.Trait.Scientific),
				(race.Religious, Civilization.Trait.Religious),
				(race.Industrious, Civilization.Trait.Industrious),
				(race.Agricultural, Civilization.Trait.Agricultural),
				(race.Seafaring, Civilization.Trait.Seafaring),
			}
			.Where(t => t.Item1)
			.Select(t => t.Item2);
		}

		private void ImportBicLeaders() {
			// The playable civs are part of the scenario's game settings. An
			// empty list means every civ may play, as in Civ3.
			int[] playableCivs = GameBiq?.GameCiv?[0];
			if (playableCivs is { Length: 0 }) {
				playableCivs = null;
			}

			Government defaultGovernment = save.Governments.Find(g => g.defaultType) ?? save.Governments[0];

			// Make a player for each civ. The barbarians are always civ 0.
			for (int i = 0; i < save.Civilizations.Count; ++i) {
				Civilization civ = save.Civilizations[i];

				// GameCiv[0] does not contain the barbarians,
				// but we want to include them in the gameplay
				bool isIncluded = playableCivs == null || playableCivs.Contains(i) || civ.isBarbarian;

				save.Players.Add(MakeSavePlayerFromCiv(civ,
									   isHuman: false,
									   era: "",
									   isIncluded));

				// Set a government for players not associated with LEAD.
				// Usually, this applies only to barbarians, but in some scenarios
				// it may also include civilizations that exist in the game files
				// but are not actually part of the gameplay.
				save.Players.Last().governmentId = defaultGovernment.id;
			}

			// Now fill in the rest of the data using the leader struct. A BIQ
			// with only a map has none.
			BiqData leadBiq = LeadBiq;
			if (leadBiq == null) {
				return;
			}
			ERAS[] eras = biq.Eras ?? defaultBiq.Eras;
			bool foundHuman = false;
			int leadIndex = 0;
			foreach (LEAD lead in leadBiq.Lead) {
				if (lead.Civ < 0 || lead.Civ >= save.Players.Count) {
					log.Warning("LEAD {Lead} is of unknown civ {Civ}, and is left out", leadIndex, lead.Civ);
					++leadIndex;
					continue;
				}
				SavePlayer player = save.Players[lead.Civ];

				player.canBePicked = lead.HumanPlayer == 1;

				// Put the player in the correct starting era.
				player.eraCivilopediaName = eras[lead.InitialEra].CivilopediaEntry;

				// Give the correct amount of starting gold.
				player.gold = lead.StartCash;

				// The game starts out with 50% on the science slider and 0% on
				// the luxury slider.
				player.scienceRate = 5;
				player.taxRate = 5;
				player.luxuryRate = 0;

				player.governmentId = save.Governments[lead.Government].id;

				player.skipFirstTurn = lead.SkipFirstTurn == 1;

				// Add the starting techs for scenarios.
				if (leadBiq.LeadTech != null) {
					for (int j = 0; j < leadBiq.LeadTech[leadIndex].Length; ++j) {
						player.knownTechs.Add(save.Techs[leadBiq.LeadTech[leadIndex][j]].id);
					}
				}

				// Mark the first human playable civ as the human player.
				if (lead.HumanPlayer == 1 && !foundHuman) {
					player.human = true;
					foundHuman = true;
				}

				// Team color in scenarios
				player.primaryColorIndex = lead.Color;

				++leadIndex;
			}
		}

		// The player for each of the SAV's LEADs, by LEAD index, or null for
		// a LEAD slot no civ uses. Everything in a SAV refers to players by
		// LEAD index, but the unused slots aren't made players, so the
		// indices into save.Players can differ.
		private SavePlayer[] savLeadPlayers = [];

		// The player for the SAV's LEAD at the given index, or null.
		private SavePlayer SavPlayer(int lead) {
			return lead >= 0 && lead < savLeadPlayers.Length ? savLeadPlayers[lead] : null;
		}

		private void ImportSavLeaders() {
			BiqData theBiq = biq.Eras is null ? defaultBiq : biq;
			int currentTurn = save.TurnNumber;
			savLeadPlayers = new SavePlayer[savData.Lead.Length];
			for (int i = 0; i < savData.Lead.Length; ++i) {
				QueryCiv3.Sav.LEAD leader = savData.Lead[i];
				if (leader.RaceID < 0) {
					continue;
				}
				if (leader.RaceID >= save.Civilizations.Count) {
					log.Warning("LEAD {Lead} is of unknown civ {Race}, and is left out", i, leader.RaceID);
					continue;
				}
				Civilization civ = save.Civilizations[leader.RaceID];
				// The GAME section's HumanPlayers and RemainingPlayers are
				// bitmaps by LEAD index. No CivFanatics source documents them,
				// but they were checked against 60 Conquests saves: the human
				// slots matched the games' players, including the hotseat
				// ones, and only civs with no cities or units were missing
				// from RemainingPlayers.
				SavePlayer player = MakeSavePlayerFromCiv(civ,
										  isHuman: !civ.isBarbarian && savData.Game.HumanPlayers[i],
										  era: theBiq.Eras[leader.Era].CivilopediaEntry,
                                          // by default if the player is in the .sav file, well, it's included in the game
                                          // in contrast to a .biq file where it can have a player/civ that is not included in the final gameplay
                                          true);

				// Record what the player is currently researching.
				if (leader.Researching > -1) {
					player.currentlyResearchedTech = save.Techs[leader.Researching].id;
				}
				player.futureTechs = leader.FutureTechsKnown;

				// Record any techs that this player knows.
				for (int k = 0; k < savData.KnownTechFlags.Length; ++k) {
					if (savData.KnownTechFlags[k][i]) {
						player.knownTechs.Add(save.Techs[k].id);
					}
				}

				// Populate player's research queue
				for (int t = 0; t < savData.LeadTechQueue[i].Length; ++t) {
					int techItem = savData.LeadTechQueue[i][t];
					player.researchQueue.Add(save.Techs[techItem].id);
				}

				player.gold = leader.Gold;
				player.beakers = leader.Beakers;
				player.turnsResearched = leader.TurnsResearched;
				player.scienceRate = leader.ScienceRate;
				player.luxuryRate = leader.LuxuryRate;
				player.taxRate = leader.TaxRate;
				player.governmentId = save.Governments[leader.Government].id;
				player.inAnarchyUntilTurn = save.TurnNumber + leader.AnarchyTurnsLeft;
				player.primaryColorIndex = leader.Color;
				player.hadGoldenAge = leader.GoldenAgeEndTurn >= 0;
				// The golden age still runs on its end turn.
				player.goldenAgeTurnsRemaining = Math.Max(0, leader.GoldenAgeEndTurn - save.TurnNumber + 1);

				player.defeated = !player.isBarbarian && !savData.Game.RemainingPlayers[i];
				player.hasVictoriousArmy = leader.HasVictoriousArmy;
				short[] spaceshipParts = savData.LeadSpaceshipParts?[i];
				if (spaceshipParts != null && spaceshipParts.Any(n => n > 0)) {
					player.spaceshipParts = spaceshipParts.Select(n => (int)n).ToList();
				}

				save.Players.Add(player);
				savLeadPlayers[i] = player;
			}

			// A game always has a human player. Should the save not mark one,
			// fall back to the first civ, the one a single-player game's
			// human plays.
			if (!save.Players.Any(p => p.human)) {
				SavePlayer first = save.Players.FirstOrDefault(p => !p.isBarbarian);
				if (first != null) {
					log.Warning("The save marks no player as human; making {Player} human", first.civilization);
					first.human = true;
				}
			}

			// Now that we know all the players, fill in details about their
			// relationship to each other.
			for (int i = 0; i < savData.Lead.Length; ++i) {
				QueryCiv3.Sav.LEAD leader = savData.Lead[i];
				SavePlayer player = SavPlayer(i);
				if (player == null) {
					continue;
				}
				List<int> contacts = leader.GetContact();
				List<int> refuseContactForTurns = leader.GetRefuseContactForTurns();
				for (int j = 0; j < contacts.Count; ++j) {
					SavePlayer other = SavPlayer(j);
					if (contacts[j] > 0 && other != null && other != player
						&& i < savData.ReputationRelationship.Length && j < savData.ReputationRelationship[i].Length) {
						QueryCiv3.Sav.LEAD_LEAD relationship = savData.ReputationRelationship[i][j];
						player.playerRelationships.TryAdd(other.id.ToString(), new PlayerRelationship() {
							warDeclarationCount = relationship.WarDeclarationCount,
							// I don't think there is a way to figure this out for .sav or .biq files
							// so by default we set this to 0 for these games
							warDeclarationWithRoPActiveCount = 0,
							wasSneakAttacked = relationship.WasSneakAttacked == 1,
							refuseContactUntilTurn =
								refuseContactForTurns[j] > 0 ?
									save.TurnNumber + refuseContactForTurns[j] : -1,
							hasEmbassy = leader.HasEmbassyWith(j),
							hasSpy = leader.HasSpyIn(j),
						});
					}
				}
			}

			// Multi-Turn deals

			#region Multi-turn-deal-binary-documentation
			// Each player's diplomatic relationship with another player is represented as two arrays in a row.
			// Well, most of the time, more details on this are further down.
			// If the diplomacy array is empty, that either means the players are at war, or one/both the players are not in the game.
			// The data we get from a .SAV file, have all the (potential) diplomatic relationships between all 32 players.
			// So Player 1 has an array of relationships for players 0-31,
			// but also Player 31 has an array 0-31, even if they are not in the game.

			// As stated before, each relationship is represented by 2 arrays in a row.
			// So for example if Player1 is at peace with Player2, there will be 2 arrays inside the diplomacy array of both players
			// and because peace is the basis for any diplomatic agreement, these arrays are always 0 and 1.

			// Here are some more visual representations of the arrays, so it's easier to understand what's happening
			// Example Peace between Player1 and Player2
			//
			// Player1 LEAD_LEAD_Diplomacy[2] <- size of the diplomacy array is 2,
			// so it's safe to say only 1 deal is currently active,
			// and from what we know, it should always be peace

			// This should be the same for Player2's diplomacy array
			//
			// [0] =
			//   Data1 = 1          <-- "Gives" peace to Player2
			//   Data2 = 0          <-- This deal will end at turn 0 (still active, never at war)
			//   EntryType = -1
			// [1] =
			//   Data1 = 0          <-- Peace (DealSubtype)
			//   Data2 = 0
			//   EntryType = 0      <-- Diplomatic Agreement (DealType)

			// Notice that there is no reference to the other player, we know it's Player1 & Player2
			// because we are specifically looking at Player1's relationship with Player2.
			// Stating the obvious here, just making sure it's clear what kind of data is (and is not) present in these arrays.

			// Array #0
			// Array #0 holds information like, who gives (1) and who receives (0) (Data1),
			// number of remaining turns (Data2); All players in a normal game start at peace, so that value is 0.
			// If at some point in the future they go at war, and then make peace, this value will hold the turn the peace deal ends.
			// A value of future turn means a deal is active and will end at that turn in the future.
			// A value of current/past turn means the deal is still active, even beyond the usual 20 turns.
			// This is true for all other deals, but usually a Gold per Turn deal doesn't go beyond the 20 turns, it automatically resets,
			// but a right of passage deal for example sometimes doesn't.

			// Array #1
			// Array #1 holds the type of deal (DealType enum), so EntryType 0 means a diplomatic agreement, 1 is a military alliance, etc...
			// Data1 holds what I named, subtype (DealSubType enum). So if it's a diplomatic agreement and Data1 is 0, it means Peace, 1 is a Mutual Protection Pact, and so on.
			// In a resource per turn deal Data1 will hold the index of the resource being traded (e.x. 0 is Horses, 12 is Spices, etc)
			// In a military alliance deal Data1 will hold the index of the 3rd player, the player the alliance is against (same for trade embargo)
			// Data2 is used when for example there is a resource per turn deal, it holds the index of the resource being traded (e.x. 0 is Horses, 12 is Spices, etc)
			// In the case of a gold per turn deal, Data2 holds the gpt amount.
			// But there is a caveat here. In some cases, and I am not entirely sure when, but it seems to happen when there are multiple deals that happened on the same turn,
			// the second array (of the nth deal, not the 1st) is not present. So the information for when the deal ends for example, is taken from the previous deal in the diplomacy array.
			// That is why in the implementation below the dealStart is outside the main diplomacy for-loop check, it keeps a state for that type of situation.
			// Another instance of a deal missing parts, is for example, when Player1 gives Player2 5 gpt.
			// In the Player1's diplomacy info (the one giving), there will be two arrays as described above.
			// But in the Player2's info (the one receiving), the second array that holds what type of deal and how much gold is being traded can be missing.

			// Example Right of passage (current turn 25)
			// This should be the same for Player2's diplomacy array
			// Player1 LEAD_LEAD_Diplomacy[4]
			// Peace as said is mandatory for any diplomatic agreement
			// ---------------------- First Deal -------------------------------
			// [0] =
			//   Data1 = 1       <- "Gives" peace to Player2
			//   Data2 = 0       <- This deal will end at turn 0 (still active, never at war)
			//   EntryType = -1
			// [1] =
			//   Data1 = 0       <- Peace (DealSubtype)
			//   Data2 = 0
			//   EntryType = 0   <- Diplomatic Agreement (DealType)
			// ---------------------- Second Deal -------------------------------
			// [2] =
			//   Data1 = 1       <- "Gives" ROP to Player2
			//   Data2 = 40       <- This deal will end at turn 40 (it's been active for 5 turns)
			//   EntryType = -1
			// [3] =
			//   Data1 = 2       <- Right-of-passage (DealSubtype)
			//   Data2 = 0
			//   EntryType = 0   <- Diplomatic Agreement (DealType)


			// Example Trade luxury (current turn 25)
			// Player1 LEAD_LEAD_Diplomacy[4]
			// Peace as said is mandatory for any diplomatic agreement
			// ---------------------- First Deal -------------------------------
			// Peace placeholder, so I am not cluttering the screen, it's the same as above
			// ---------------------- Second Deal -------------------------------
			// [2] =
			//   Data1 = 1       <- "Gives" Spices to Player2
			//   Data2 = 40       <- This deal will end at turn 40 (it's been active for 5 turns)
			//   EntryType = -1
			// [3] =
			//   Data1 = 12      <- Luxury per turn (Spices)
			//   Data2 = 0
			//   EntryType = 6   <- Luxury (DealType)


			// Example Gold per turn with missing array for receiving player (current turn 25)
			// Player1 LEAD_LEAD_Diplomacy[4]
			// ---------------------- First Deal -------------------------------
			// Peace
			// ---------------------- Second Deal -------------------------------
			// [2] =
			//   Data1 = 1       <- "Gives" 3 gpt to Player2
			//   Data2 = 40       <- This deal will end at turn 40 (it's been active for 5 turns)
			//   EntryType = -1
			// [3] =
			//   Data1 = 0
			//   Data2 = 3       <- Gold per turn amount
			//   EntryType = 7   <- Gold (DealType)

			// in contrast, Player2's diplomacy array might look like this

			// Player2 LEAD_LEAD_Diplomacy[3] <-- 3 instead of 4!
			// ---------------------- First Deal -------------------------------
			// Peace
			// ---------------------- Second Deal -------------------------------
			// [2] =
			//   Data1 = 0       <- "Receives" 3 gpt from Player1
			//   Data2 = 40       <- This deal will end at turn 40 (it's been active for 5 turns)
			//   EntryType = -1
			// ---------------------- End ------------------------------
			//
			// Notice how Player2 is basically unaware that they receive 3 gpt from Player1,
			// they only know that they are receiving something up to turn 40
			#endregion

			for (int i = 0; i < savData.Lead.Length; ++i) {
				QueryCiv3.Sav.LEAD leader = savData.Lead[i];
				int turnsRemaining = 0;
				int dealStart = 0;
				int defaultDealDuration = save.Rules.DefaultDealDuration;
				List<int> contacts = leader.GetContact();
				SavePlayer player = SavPlayer(i);
				for (int j = 0; j < contacts.Count; ++j) {
					if (contacts[j] > 0) {
						// skip barbarians, they can't have any diplomatic relationships, they are at war against everyone, at all times
						if (i <= 0 || j <= 0) continue;

						// skip LEADs that aren't players
						SavePlayer other = SavPlayer(j);
						if (player == null || other == null) continue;

						QueryCiv3.Sav.LEAD_LEAD_Diplomacy[] diplomacy = savData.LeadLeadDiplomacy[i, j];
						// a civ doesn't have any diplomatic relationships with itself, or with another civ they are at war with
						if (i == j || diplomacy.Length == 0) continue;

						for (int d = 0; d < diplomacy.Length; ++d) {
							DealType deadType = DealType.None;
							DealSubType dealSubType = DealSubType.None;
							DealDetails dealDetails = DealDetails.None;
							int goldPerTurn = 0;
							int resourceIndex = -1;
							string resourcePerTurn = null;
							ID againstPlayer = null;
							if (diplomacy[d].EntryType == -1) {
								turnsRemaining = diplomacy[d].Data2 - currentTurn > 0 ? diplomacy[d].Data2 - currentTurn : 0;
								dealStart = diplomacy[d].Data2 > 0 ? currentTurn - (defaultDealDuration - turnsRemaining) : 0;
								// I am pretty sure that is what these values represent, take it with a grain of salt.
								// We assign these manually anyway in each deal
								// but still could come handy at some point to know what they are.
								if (diplomacy[d].Data1 == 0) {
									dealDetails = DealDetails.Inbound;
								} else if (diplomacy[d].Data1 == 1) {
									dealDetails = DealDetails.Outbound;
								}
							}

							if (d + 1 <= diplomacy.Length - 1) {
								deadType = (DealType)diplomacy[d + 1].EntryType;
								if (deadType == DealType.DiplomaticAgreement) {
									if (diplomacy[d + 1].Data1 == 0) {
										dealSubType = DealSubType.Peace;
									} else if (diplomacy[d + 1].Data1 == 1) {
										dealSubType = DealSubType.MutualProtectionPact;
									} else if (diplomacy[d + 1].Data1 == 2) {
										dealSubType = DealSubType.RightOfPassage;
									}

									dealDetails = DealDetails.Exchange;
								} else if (deadType == DealType.Alliance) {
									dealSubType = DealSubType.MilitaryAlliance;
									againstPlayer = SavPlayer(diplomacy[d + 1].Data1)?.id;
									dealDetails = DealDetails.Exchange;
									if (againstPlayer == null) continue;
								} else if (deadType == DealType.Embargo) {
									dealSubType = DealSubType.TradeEmbargo;
									againstPlayer = SavPlayer(diplomacy[d + 1].Data1)?.id;
									dealDetails = DealDetails.Exchange;
									if (againstPlayer == null) continue;
								}
								  // These types of deals are only present in full on the civ that gives the gold/resources/luxuries
								  else if (deadType == DealType.Gold) {
									dealSubType = DealSubType.GoldPerTurn;
									goldPerTurn = diplomacy[d + 1].Data2;
									dealDetails = DealDetails.Outbound;
								} else if (deadType == DealType.Luxury) {
									dealSubType = DealSubType.LuxuryPerTurn;
									resourceIndex = diplomacy[d + 1].Data1;
									resourcePerTurn = save.Resources[resourceIndex].Key;
									dealDetails = DealDetails.Outbound;
								} else if (deadType == DealType.Resource) {
									dealSubType = DealSubType.ResourcePerTurn;
									resourceIndex = diplomacy[d + 1].Data1;
									resourcePerTurn = save.Resources[resourceIndex].Key;
									dealDetails = DealDetails.Outbound;
								} else {
									continue;
								}

								if (dealSubType != DealSubType.None
									&& player.playerRelationships.TryGetValue(other.id.ToString(), out PlayerRelationship pr)) {
									MultiTurnDeal mtd = new MultiTurnDeal(deadType, dealSubType, dealDetails, goldPerTurn, resourcePerTurn, defaultDealDuration, dealStart, againstPlayer);
									pr.multiTurnDeals.Add(mtd);
								}

								// Add inbound info to other player because that info is not exactly present in the binary
								if (dealDetails == DealDetails.Outbound
									&& dealSubType != DealSubType.None
									&& other.playerRelationships.TryGetValue(player.id.ToString(), out PlayerRelationship prOther)) {
									MultiTurnDeal mtd = new MultiTurnDeal(deadType, dealSubType, DealDetails.Inbound, goldPerTurn, resourcePerTurn, defaultDealDuration, dealStart, againstPlayer);
									prOther.multiTurnDeals.Add(mtd);
								}
							}
						}
					}
				}
			}

			foreach (SavePlayer savePlayer in save.Players) {
				log.Information("- - - - - - - - - - - - - - - - - - {Civilization} - - - - - - - - - - - - - - - - - - ", savePlayer.civilization);
				foreach (KeyValuePair<string, PlayerRelationship> pr in savePlayer.playerRelationships) {
					if (pr.Value.multiTurnDeals.Count == 0) {
						log.Information("{Player} is at war with {Other}", savePlayer, save.Players.First(c => c.id.ToString() == pr.Key));
						continue;
					}
					foreach (MultiTurnDeal mtd in pr.Value.multiTurnDeals) {
						string against = mtd.dealSubType == DealSubType.MilitaryAlliance || mtd.dealSubType == DealSubType.TradeEmbargo ? $"(against: {save.Players.First(p => p.id == mtd.againstPlayer)})" : "";
						string gpt = mtd.dealSubType == DealSubType.GoldPerTurn? $"({mtd.goldPerTurn} gold per turn)" : "";
						string rpt = mtd.dealSubType == DealSubType.LuxuryPerTurn || mtd.dealSubType == DealSubType.ResourcePerTurn ? $"({mtd.resourcePerTurn} per turn)" : "";
						log.Information($"{savePlayer} " +
										$"has {mtd.dealSubType} " +
										$"with {save.Players.First(c => c.id.ToString() == pr.Key)} " +
										$"for another {mtd.TurnsRemaining(save.TurnNumber)} turns {mtd.turnStartDeal}-{mtd.turnEndDeal}" +
										$" {against} {gpt} {rpt} {mtd.dealDetails}");
					}
				}
			}
		}

		private SavePlayer MakeSavePlayerFromCiv(Civilization civ, bool isHuman, string era, bool isIncludedInGame = true) {
			return new SavePlayer {
				id = ids.CreateID("player"),
				primaryColorIndex = civ.primaryColorIndex,
				secondaryColorIndex = civ.secondaryColorIndex,
				human = isHuman,
				civilization = civ.name,
				isIncludedInGame = isIncludedInGame,
				// Never let barbarians play before a real player.
				hasPlayedCurrentTurn = civ.isBarbarian,
				isBarbarian = civ.isBarbarian,
				eraCivilopediaName = era,
			};
		}

		private void ImportEmbassies() {
			BiqData theBiq = LeadBiq;
			if (theBiq == null) {
				return;
			}

			List<SavePlayer> playerWithEmbassies = new List<SavePlayer>();

			for (int i = 0; i < theBiq.Lead.Length; ++i) {
				var lead = theBiq.Lead[i];
				if (lead.StartEmbassies == 1 && lead.Civ >= 0 && lead.Civ < save.Players.Count) {
					var player = save.Players[lead.Civ];
					if (!player.isBarbarian)
						playerWithEmbassies.Add(player);
				}
			}

			// In scenarios where there isn't any actual information about player relationships,
			// the entry point of these relationships seems to be the embassies.
			// Players that start with embassies, are aware of each other.
			// Information about players at war, is given in alliances data.
			foreach (var playerWithEmbassy in playerWithEmbassies) {
				foreach (var other in playerWithEmbassies.Except(new List<SavePlayer> { playerWithEmbassy })) {
					var pr = new PlayerRelationship() {
						warDeclarationCount = 0,
						warDeclarationWithRoPActiveCount = 0,
						wasSneakAttacked = false,
						refuseContactUntilTurn = -1,
						hasEmbassy = true,
					};
					playerWithEmbassy.playerRelationships.Add(other.id.ToString(), pr);

					// simple peace deal, that could be removed if in a locked war (check ImportAlliances())
					pr.multiTurnDeals.Add(MultiTurnDeal.DEFAULT_PEACE);
				}
			}
		}

		private void ImportAlliances() {
			BiqData theBiq = GameBiq;
			if (theBiq?.Game == null) {
				return;
			}

			// import alliances names and indexes
			HashSet<Alliance> alliances = new HashSet<Alliance>() {
                // new (0, theBiq.Game[0].AllianceNames[0]), // no alliance, not sure if it's necessary
                new (1, theBiq.Game[0].AllianceNames[1]),
				new (2, theBiq.Game[0].AllianceNames[2]),
				new (3, theBiq.Game[0].AllianceNames[3]),
				new (4, theBiq.Game[0].AllianceNames[4]),
			};

			save.Alliances = new HashSet<Alliance>(alliances.OrderBy(a => a.index).ToHashSet());

			// import player alliances
			var filteredPlayers = save.Players.Where(p => p.isIncludedInGame || p.isBarbarian).ToList();
			int[] gameAlliances = theBiq.GameAlliance?[0] ?? [];
			for (int i = 0; i < gameAlliances.Length && i + 1 < filteredPlayers.Count; i++) {
				filteredPlayers[i + 1].alliance = alliances.FirstOrDefault(a => a.index == gameAlliances[i])?.name;
			}

			foreach (var saveAlliance in save.Alliances) {
				foreach (var savePlayer in save.Players.Where(p => p.alliance == saveAlliance.name)) {
					foreach (var otherSavePlayer in save.Players.Where(p => p.alliance == saveAlliance.name && p.id != savePlayer.id)) {
						var relationship = savePlayer.playerRelationships.TryGetValue(otherSavePlayer.id.ToString(), out var otherPlayerRelationship);
						if (relationship) {
							log.Information($"Players {savePlayer.civilization} and {otherSavePlayer.civilization}" +
											$" are in the same alliance: {saveAlliance.name}, and have a Mutual Protection Pact");
							otherPlayerRelationship.multiTurnDeals.Add(MultiTurnDeal.DEFAULT_MUTUAL_PROTECTION_PACT);
						}
					}
				}
			}

			// import alliances war info

			// alliance no
			// skip first alliance as it means "no alliance"
			for (int a = 1; a < 5; a++) {
				// entry in each alliance
				for (int e = 0; e < 5; e++) {
					var wwa = theBiq.Game[0].WarWithAlliance[a, e];

					if (wwa != 0) {
						string one = alliances.First(al => al.name == theBiq.Game[0].AllianceNames[a]).name;
						string other = alliances.First(al => al.name == theBiq.Game[0].AllianceNames[e]).name;
						save.AllianceWars.TryAdd(one, other);
						if (!save.AllianceWarPairs.Any(p => (p[0] == one && p[1] == other) || (p[0] == other && p[1] == one))) {
							save.AllianceWarPairs.Add([one, other]);
						}
					}
				}
			}

			// "Declare" war for rival alliances
			foreach ((string allianceA, string allianceB) in save.AllAllianceWars()) {
				var playerAllianceA = save.Players.Where(p => p.alliance == allianceA).ToList();
				var playerAllianceB = save.Players.Where(p => p.alliance == allianceB).ToList();

				foreach (var playerInAllianceA in playerAllianceA) {
					foreach (var playerInAllianceB in playerAllianceB) {
						playerInAllianceA.playerRelationships[playerInAllianceB.id.ToString()].multiTurnDeals = new List<MultiTurnDeal>();
						playerInAllianceB.playerRelationships[playerInAllianceA.id.ToString()].multiTurnDeals = new List<MultiTurnDeal>();
					}
				}
			}
		}

		private void ImportSavUnits() {
			var shadowIdMap = new Dictionary<int, ID>();
			var loadMap = new Dictionary<ID, int>();

			foreach (QueryCiv3.Sav.UNIT unit in savData.Unit) {
				SavePlayer player = SavPlayer(unit.OwnerID);
				if (player == null) {
					continue;
				}
				PRTO prototype = savData.Bic.Prto[unit.UnitType];
				string prototypeName = prototype.Name;
				string unitName = unit.Name;
				ExperienceLevel experience = save.ExperienceLevels[unit.ExperienceLevel];
				SaveUnit saveUnit = new SaveUnit{
					id = ids.CreateID(prototypeName),
					name = String.IsNullOrEmpty(unitName) ? prototypeName : unitName,
					nationality = save.Civilizations[unit.Nationality].name,
					owner = player.id,
					prototype = prototypeName,
					currentLocation = new TileLocation(unit.X, unit.Y),
					previousLocation = new TileLocation(unit.PreviousX, unit.PreviousY),
					experience = experience.key,
					hitPointsRemaining = experience.baseHitPoints,
					movePointsRemaining = prototype.Movement,
					WorkerProgressTowardsJob = unit.WorkerProgressTowardsJob,
					WorkerJob = (unit.WorkerJob==-1) ? null: save.TerraForms[unit.WorkerJob].Id,
					isAutomated = unit.IsAutomated,
				};
				// since this is a .sav unit, we need to adjust things like the hp, remaining moves, etc
				// TODO: there are surely more things to add here, e.x. has this unit used its defensive bombardment this round?
				saveUnit.hitPointsRemaining += prototype.HPBonus - unit.Damage;
				saveUnit.movePointsRemaining -= (unit.MovementUsed / 3f);
				if (unit.Fortified) {
					saveUnit.action = "fortified";
				}

				shadowIdMap[unit.ID] = saveUnit.id;
				// Unit IDs start at 0; -1 means the unit isn't loaded.
				if (unit.LoadedOnUnitId >= 0)
					loadMap[saveUnit.id] = unit.LoadedOnUnitId;

				save.Units.Add(saveUnit);
			}

			// Civ3 saves have their own ID scheme.
			// Here we translate one "loaded on" reference to another using information
			// gathered above. Note that we can't be sure to be able to resolve identifiers
			// until we have scanned all units. Hence a second pass for these mappings.
			foreach (var saveUnit in save.Units) {
				if (loadMap.TryGetValue(saveUnit.id, out var loadedOnUnitId)) {
					if (shadowIdMap.TryGetValue(loadedOnUnitId, out ID carrier)) {
						saveUnit.loadedOnUnitId = carrier;
					} else {
						log.Warning("{Unit} is loaded on unit {Carrier}, which isn't in the game; unloading it", saveUnit.name, loadedOnUnitId);
					}
				}
			}
		}

		private void ImportBicUnits() {
			// Units and starting locations belong to the BIQ's map; the unit
			// types and rules may come from the default BIQ.
			PRTO[] prtos = biq.Prto ?? defaultBiq.Prto;

			var createUnitAtLocation = (SavePlayer player, string unitName, int unitType, string experienceLevel, int hitPoints, int x, int y) => {
				PRTO prototype = prtos[unitType];
				string prototypeName = prototype.Name;
				SaveUnit saveUnit = new SaveUnit{
					id = ids.CreateID(prototypeName),
					name = String.IsNullOrEmpty(unitName) ? prototypeName : unitName,
                    /* TODO: scenarios seem to not support slave units by default,
                       meaning there is no Nationality field in the Biq UNIT data structure,
                       we might want to do something different in the future (somehow) */
                    nationality = player.civilization,
					owner = player.id,
					prototype = prototypeName,
					currentLocation = new TileLocation(x, y),
					previousLocation = new TileLocation(x, y),
					experience = experienceLevel,
					hitPointsRemaining = hitPoints + prototype.HPBonus,
					movePointsRemaining = (float)prototype.Movement,
				};
				return saveUnit;
			};

			foreach (UNIT unit in biq.Unit ?? []) {
				// Barbarian units' owner is their tribe, which C7 doesn't
				// have yet, so they all belong to the barbarians.
				// TODO: implement tribes for barbarians
				SavePlayer player = BiqOwner(unit.OwnerType, unit.Owner, $"unit {unit.Name}");
				if (player == null) {
					continue;
				}
				if (unit.ExperienceLevel < 0 || unit.ExperienceLevel >= save.ExperienceLevels.Count || unit.UnitType < 0 || unit.UnitType >= prtos.Length) {
					log.Warning("Unit {Unit} at ({X}, {Y}) has unknown type {Type} or experience level {Level}, and is left out", unit.Name, unit.X, unit.Y, unit.UnitType, unit.ExperienceLevel);
					continue;
				}
				ExperienceLevel experience = save.ExperienceLevels[unit.ExperienceLevel];
				SaveUnit placed = createUnitAtLocation(player, unit.Name, unit.UnitType, experience.key, experience.baseHitPoints, unit.X, unit.Y);
				placed.useCivSpecificKing = unit.UseCivilizationKing != 0;
				save.Units.Add(placed);
			}

			// Each civ's starting location gets the starting units, at the
			// default experience level (the second EXPR, Regular in Civ3's
			// rules, which these units were hardcoded to before eb8aad18).
			// UNVERIFIED (no Civ3 source found): the experience Civ3 gives
			// them. Units placed on the map take their level from the BIQ.
			RULE rule = biq.Rule?[0] ?? defaultBiq.Rule[0];
			ExperienceLevel startLevel = save.ExperienceLevels.Find(e => e.key == save.DefaultExperienceLevel) ?? save.ExperienceLevels[0];
			foreach (SLOC starting_location in biq.Sloc ?? []) {
				// Skip unowned and barbarian starting locations
				if (starting_location.OwnerType <= 1) {
					continue;
				}

				SavePlayer player = BiqOwner(starting_location.OwnerType, starting_location.Owner, "starting location");
				if (player == null) {
					continue;
				}
				foreach (int unitType in new[] { rule.StartUnitType1, rule.StartUnitType2 }) {
					if (unitType >= 0 && unitType < prtos.Length) {
						save.Units.Add(createUnitAtLocation(player, prtos[unitType].Name, unitType, startLevel.key, startLevel.baseHitPoints, starting_location.X, starting_location.Y));
					}
				}
			}
		}

		// The player that owns something in a BIQ, given its owner type
		// (0: none, 1: barbarians, 2: a civ, 3: a player, that is a LEAD)
		// and owner index, or null if there is none.
		private SavePlayer BiqOwner(int ownerType, int owner, string what) {
			// Until the players are filtered at the end of importBiq, there
			// is a player for each civ, in the same order.
			int civ;
			switch (ownerType) {
				case 1:
					return save.Players.FirstOrDefault(p => p.isBarbarian);
				case 2:
					civ = owner;
					break;
				case 3:
					LEAD[] leads = LeadBiq?.Lead;
					if (leads == null || owner < 0 || owner >= leads.Length) {
						log.Warning("The {What} belongs to unknown player {Owner}, and is left out", what, owner);
						return null;
					}
					civ = leads[owner].Civ;
					break;
				default:
					log.Information("The {What} belongs to no one, and is left out", what);
					return null;
			}
			if (civ < 0 || civ >= save.Players.Count) {
				log.Warning("The {What} belongs to civ {Owner}, but there are only {PlayerCount} civs; it is left out", what, civ, save.Players.Count);
				return null;
			}
			return save.Players[civ];
		}

		private void ImportSavCities() {
			for (int i = 0; i < savData.City.Length; ++i) {
				QueryCiv3.Sav.CITY city = savData.City[i];
				SavePlayer owner = SavPlayer(city.Owner);
				if (owner == null) {
					log.Warning("City {City} belongs to LEAD {Owner}, which isn't a player; it is left out", city.Name, city.Owner);
					continue;
				}

				var (producible, producibleType) = CityToProducible(city);

				SaveCity saveCity = new SaveCity{
					id = ids.CreateID("city"),
					owner = owner.id,
					location = new TileLocation(city.X, city.Y),
					capital = i == savData.Lead[city.Owner].CapitalCity,
					producible = producible,
					producibleType = producibleType,
					name = city.Name,
					size = city.Popd.CitizenCount,
					shieldsStored = city.ShieldsCollected,
					foodStored = city.TotalFood,
					buildings = ImportCityBuildingsFromSav(i),
					turnsOfUnhappinessDueToPopRushing = city.TurnsOfUnhappinessDueToPopRushing,
					turnsOfUnhappinessDueToDrafting = city.TurnsOfUnhappinessDueToDrafting,
					unitsDraftedThisTurn = city.NumUnitsDraftedThisTurn,
				};

				List<int> culturePerLeader = city.GetCulturePerLeader();
				for (int j = 0; j < culturePerLeader.Count; ++j) {
					SavePlayer player = SavPlayer(j);
					if (player != null && (culturePerLeader[j] > 0 || j == city.Owner)) {
						saveCity.perPlayerCulture.TryAdd(player.id.ToString(), culturePerLeader[j]);
					}
				}

				ID defaultCitizenType = save.CitizenTypes.Find(x => x.IsDefaultCitizen).Id;
				foreach (QueryCiv3.Sav.CTZN ctzn in savData.CityCtzn[i]) {
					SaveCityResident scr = new() {
						city = saveCity.id,
						nationality = ctzn.Nationality >= 0 && ctzn.Nationality < save.Civilizations.Count
							? save.Civilizations[ctzn.Nationality].name : owner.civilization,
						// Not working a tile, unless set below.
						tileWorked = new TileLocation(),
						citizenType = defaultCitizenType,
					};
					CitizenType specialist = ctzn.Type == 4 ? save.CitizenTypes.Find(x => x.SpecialistIndex == ctzn.SpecialistType) : null;
					if (specialist != null) {
						scr.citizenType = specialist.Id;
					} else if (ctzn.TileWorked != 0) {
						scr.tileWorked = GetTileFromSpiral(saveCity.location, ctzn.TileWorked);
					}
					// Otherwise the citizen works no tile (as resistors and
					// citizens of a city in disorder may not), and is given one
					// when the game starts (see SaveGame.OnGameCreation).
					// TODO: handle resistors
					saveCity.residents.Add(scr);
				}
				save.Cities.Add(saveCity);
			}
		}

		List<SaveCityBuilding> ImportCityBuildingsFromSav(int cityIndex) {
			List<SaveCityBuilding> res = [];

			var city = savData.City[cityIndex];
			QueryCiv3.Sav.CITY_Building[] cityBuildings = savData.CityBuilding[cityIndex];

			BiqData theBiq = biq.Bldg is null ? defaultBiq : biq;

			for (int buildingIndex = 0; buildingIndex < cityBuildings.Length; ++buildingIndex) {
				var building = cityBuildings[buildingIndex];

				if (building.BuiltByPlayer != -1 && city.Bitm.IsBuildingUsable(buildingIndex)) {
					BLDG bldg = theBiq.Bldg[buildingIndex];
					string buildingName = BuildingName(theBiq.Bldg, buildingIndex);
					res.Add(new SaveCityBuilding {
						building = buildingName,
						// Buildings the save doesn't say who built are
						// credited to the city's owner.
						builtByPlayer = (SavPlayer(building.BuiltByPlayer) ?? SavPlayer(city.Owner)).id,
						year = building.Year,
						totalCulture = building.Culture,
					});

					if (bldg.Wonder) {
						save.GreatWondersBuilt.Add(buildingName);
					}
				}
			}

			return res;
		}

		List<SaveCityBuilding> ImportCityBuildingsFromBiq(int cityIndex, ID player) {
			List<SaveCityBuilding> res = [];
			int[] cityBuildings = biq.CityBuilding[cityIndex];
			BLDG[] bldgs = biq.Bldg ?? defaultBiq.Bldg;

			for (int buildingIndex = 0; buildingIndex < cityBuildings.Length; ++buildingIndex) {
				BLDG building = bldgs[cityBuildings[buildingIndex]];
				string buildingName = BuildingName(bldgs, cityBuildings[buildingIndex]);

				res.Add(new SaveCityBuilding {
					building = buildingName,
					builtByPlayer = player,
					year = 0,
					totalCulture = 0,
				});

				if (building.Wonder) {
					save.GreatWondersBuilt.Add(buildingName);
				}
			}

			return res;
		}

		private (string, ProducibleType) CityToProducible(QueryCiv3.Sav.CITY city) {
			PRTO[] unitPrototypes = biq.Prto ?? defaultBiq.Prto;
			BiqData theBiq = biq.Bldg is null ? defaultBiq : biq;

			// In .sav files Wealth (the building with the capitalization
			// flag) is a building (ConstructingType 1), but in C7 it is an
			// inflow.
			if (city.ConstructingType == 1 && city.Constructing >= 0 && city.Constructing < theBiq.Bldg.Length
				&& theBiq.Bldg[city.Constructing].Capitalization)
				city.ConstructingType = 0;

			return city.ConstructingType switch {
				0 => (theBiq.Bldg[city.Constructing].Name, ProducibleType.INFLOW),
				1 => (BuildingName(theBiq.Bldg, city.Constructing), ProducibleType.BUILDING),
				2 => (unitPrototypes[city.Constructing].Name, ProducibleType.UNIT),
				_ => throw new NotImplementedException()
			};
		}

		private void ImportBicCities() {
			// Cities belong to the BIQ's map.
			CITY[] cities = biq.City ?? [];

			for (int cityIndex = 0; cityIndex < cities.Length; ++cityIndex) {
				CITY city = cities[cityIndex];

				SavePlayer player = BiqOwner(city.OwnerType, city.Owner, $"city {city.Name}");
				if (player == null) {
					continue;
				}
				(string producible, ProducibleType producibleType) = StartingProduction(player);

				SaveCity saveCity = new SaveCity{
					id = ids.CreateID("city"),
					owner = player.id,
					location = new TileLocation(city.X, city.Y),
					capital = city.HasPalace != 0,
					producible = producible,
					producibleType = producibleType,
					name = city.Name,
					size = city.Size,
					buildings = ImportCityBuildingsFromBiq(cityIndex, player.id),
					shieldsStored = 0,
					foodStored = 0,
				};
				saveCity.perPlayerCulture.Add(player.id.ToString(), city.Culture);

				save.Cities.Add(saveCity);
			}
		}

		// What a scenario's city starts out producing, since the BIQ doesn't
		// say. The player can change it.
		//
		// UNVERIFIED (no Civ3 source found): what Civ3 starts a scenario's
		// cities on. As before eb8aad18, it is a Worker, if the owner can
		// build one. Otherwise it is the cheapest land unit the owner can
		// build with the techs it starts with, or else the first inflow
		// (Wealth), a C7 fallback.
		private (string, ProducibleType) StartingProduction(SavePlayer player) {
			Civilization civ = save.Civilizations.Find(c => c.name == player.civilization);
			HashSet<ID> techs = new(player.knownTechs);
			if (civ != null) {
				techs.UnionWith(civ.startingTechs);
			}
			List<SaveUnitPrototype> buildable = save.UnitPrototypes
				.Where(p => !p.unproducible && p.categories.Contains("Land") && p.producibleBy.Contains(player.civilization)
					&& (p.requiredTech == null || techs.Contains(p.requiredTech)) && p.requiredResources.Count == 0)
				.ToList();
			SaveUnitPrototype unit = buildable.Find(p => p.name == "Worker")
				?? buildable.OrderBy(p => p.shieldCost).FirstOrDefault();
			if (unit != null) {
				return (unit.name, ProducibleType.UNIT);
			}
			if (save.Inflows.Count > 0) {
				return (save.Inflows[0].name, ProducibleType.INFLOW);
			}
			return (save.UnitPrototypes.First().name, ProducibleType.UNIT);
		}

		private static IEnumerable<UnitAction> GetUnitActions(PRTO prto) {
			if (prto.BuildCity) yield return UnitAction.BuildCity;
			if (prto.Bombard) yield return UnitAction.Bombard;
			if (prto.SkipTurn) yield return UnitAction.Hold;
			if (prto.Wait) yield return UnitAction.Wait;
			if (prto.Fortify) yield return UnitAction.Fortify;
			if (prto.Disband) yield return UnitAction.Disband;
			if (prto.GoTo) yield return UnitAction.Goto;
			if (prto.Explore) yield return UnitAction.Explore;
			if (prto.Automate) yield return UnitAction.Automate;
			if (prto.Load) yield return UnitAction.Load;
			if (prto.Unload) yield return UnitAction.Unload;
			if (prto.UpgradeUnit) yield return UnitAction.Upgrade;
			if (prto.Pillage) yield return UnitAction.Pillage;
			if (prto.Sentry) yield return UnitAction.Sentry;
			if (prto.Airdrop) yield return UnitAction.Airdrop;
			if (prto.Bombing) yield return UnitAction.Bombing;
			if (prto.Recon) yield return UnitAction.Recon;
			if (prto.Intercept) yield return UnitAction.Intercept;
			if (prto.Rebase) yield return UnitAction.Rebase;
			if (prto.PrecisionBombing) yield return UnitAction.PrecisionBombing;
			if (prto.BuildColony) yield return UnitAction.BuildColony;
			if (prto.Airlift) yield return UnitAction.Airlift;
			if (prto.JoinCity) yield return UnitAction.JoinCity;
			if (prto.Sacrifice) yield return UnitAction.Sacrifice;
		}

		private static IEnumerable<TerraformKey> GetUnitTerraforms(PRTO prto) {
			if (prto.BuildRoad) yield return TerraformKey.BuildRoad;
			if (prto.BuildRailroad) yield return TerraformKey.BuildRailroad;
			if (prto.BuildMine) yield return TerraformKey.BuildMine;
			if (prto.Irrigate) yield return TerraformKey.Irrigate;
			if (prto.ClearJungle) yield return TerraformKey.ClearWetlands;
			if (prto.ClearForest) yield return TerraformKey.ClearForest;
			if (prto.PlantForest) yield return TerraformKey.PlantForest;
			if (prto.ClearPollution) yield return TerraformKey.ClearDamage;
			if (prto.BuildBarricade) yield return TerraformKey.BuildBarricade;
			if (prto.BuildFortress) yield return TerraformKey.BuildFortress;
			if (prto.BuildOutpost) yield return TerraformKey.BuildOutpost;
			if (prto.BuildAirfield) yield return TerraformKey.BuildAirfield;
			if (prto.BuildRadarTower) yield return TerraformKey.BuildRadarTower;
		}

		private static bool IsUnproducible(PRTO prto) {
			int[] availableTo = prto.AvailableTo.GetAvailableCivIndexes().ToArray();

			// Armies are only built through a building that allows building
			// armies (the Military Academy), which UnitPrototype.CanProduce
			// special-cases, or from a leader (not implemented yet).
			return availableTo.Length == 0 || prto.ShieldCost < 1 || prto.Army;
		}

		private HashSet<string> ImportUnitAvailability(PRTO prto) {
			HashSet<string> availableToCivs = [];
			int[] availableTo = prto.AvailableTo.GetAvailableCivIndexes().ToArray();
			// The indexes are into the civs, which may come from the default BIQ.
			RACE[] races = RaceBiq.Race;
			for (int i = 0; i < races.Length; ++i) {
				if (availableTo.Contains(i))
					availableToCivs.Add(races[i].Name);
			}

			return availableToCivs;
		}

		private void ImportUnitPrototypes() {
			PRTO[] Prto = biq.Prto ?? defaultBiq.Prto;
			int[][] stealthTargets = biq.Prto != null ? biq.PrtoPrto : defaultBiq.PrtoPrto;
			for (int prtoIndex = 0; prtoIndex < Prto.Length; prtoIndex++) {
				PRTO prto = Prto[prtoIndex];
				SaveUnitPrototype prototype = new();
				if (prto.Type == PRTO.TYPE_SEA) {
					prototype.categories.Add("Sea");
				} else if (prto.Type == PRTO.TYPE_LAND) {
					prototype.categories.Add("Land");
				} else if (prto.Type == PRTO.TYPE_AIR) {
					prototype.categories.Add("Air");
				}

				prototype.name = prto.Name;
				prototype.civilopediaEntry = prto.CivilopediaEntry?.Trim();

				Art unitArt = new Art();
				unitArt.mainArt = pediaIcons.GetUnitMainArt(prto.CivilopediaEntry);
				unitArt.thumbnailArt = pediaIcons.GetUnitThumbnailArt(prto.CivilopediaEntry, prto.IconIndex);
				unitArt.pediaArt = pediaIcons.GetUnitCivilopediaArt(prto.CivilopediaEntry);
				prototype.art = unitArt;

				prototype.attack = prto.Attack;
				prototype.defense = prto.Defense;
				prototype.movement = prto.Movement;
				prototype.capacity = prto.Capacity;
				prototype.hpBonus = prto.HPBonus;
				prototype.shieldCost = prto.ShieldCost;
				prototype.populationCost = prto.PopulationCost;
				prototype.bombard = prto.BombardStrength;
				prototype.bombardRange = prto.BombardRange;
				prototype.rateOfFire = prto.RateOfFire;
				prototype.operationalRange = prto.OperationalRange;
				prototype.airDefense = prto.AirDefense;
				// How much a worker does each turn, relative to a Worker.
				prototype.workerStrength = prto.WorkerStrength > 0 ? prto.WorkerStrength : 1;

				if (prto.TurnToAttack) prototype.flags.Add(SaveUnitPrototype.Flag.RotateBeforeAttack);

				if (prto.CanCarryFootUnitsOnly) prototype.flags.Add(SaveUnitPrototype.Flag.CanCarryFootUnitsOnly);
				if (prto.CanCarryAircraft) prototype.flags.Add(SaveUnitPrototype.Flag.CanCarryAircraft);
				if (prto.CanCarryTacticalMissiles) prototype.flags.Add(SaveUnitPrototype.Flag.CanCarryTacticalMissiles);
				if (prto.LethalLandBombardment) prototype.flags.Add(SaveUnitPrototype.Flag.LethalLandBombardment);
				if (prto.LethalSeaBombardment) prototype.flags.Add(SaveUnitPrototype.Flag.LethalSeaBombardment);
				if (prto.Radar) prototype.flags.Add(SaveUnitPrototype.Flag.Radar);
				if (prto.Army) prototype.flags.Add(SaveUnitPrototype.Flag.Army);
				if (prto.Blitz) prototype.flags.Add(SaveUnitPrototype.Flag.Blitz);
				if (prto.Amphibious) prototype.flags.Add(SaveUnitPrototype.Flag.Amphibious);
				if (prto.ZoneOfControl != 0) prototype.flags.Add(SaveUnitPrototype.Flag.ZoneOfControl);
				if (prto.StartsGoldenAge) prototype.flags.Add(SaveUnitPrototype.Flag.StartsGoldenAge);
				if (prto.NuclearWeapon) prototype.flags.Add(SaveUnitPrototype.Flag.NuclearWeapon);
				if (prto.ICBM) prototype.flags.Add(SaveUnitPrototype.Flag.ICBM);
				if (prto.TacticalMissile) prototype.flags.Add(SaveUnitPrototype.Flag.TacticalMissile);
				if (prto.SinkInSea) prototype.flags.Add(SaveUnitPrototype.Flag.SinksInSea);
				if (prto.SinkInOcean) prototype.flags.Add(SaveUnitPrototype.Flag.SinksInOcean);
				if (prto.Leader) prototype.flags.Add(SaveUnitPrototype.Flag.Leader);
				if (prto.Wheeled) prototype.flags.Add(SaveUnitPrototype.Flag.Wheeled);
				if (prto.FootSoldier) prototype.flags.Add(SaveUnitPrototype.Flag.FootSoldier);
				if (prto.AllTerrainAsRoads) prototype.flags.Add(SaveUnitPrototype.Flag.AllTerrainAsRoads);
				if (prto.Immobile) prototype.flags.Add(SaveUnitPrototype.Flag.Immobile);
				if (prto.Stealth) prototype.flags.Add(SaveUnitPrototype.Flag.Stealth);
				if (prto.Draft) prototype.flags.Add(SaveUnitPrototype.Flag.Draftable);
				if (prto.Submarine) prototype.flags.Add(SaveUnitPrototype.Flag.Invisible);
				if (prto.CanSeeSubmarines) prototype.flags.Add(SaveUnitPrototype.Flag.DetectInvisible);
				if (prto.HiddenNationality) prototype.flags.Add(SaveUnitPrototype.Flag.HiddenNationality);
				if (prto.StealthAttack) prototype.flags.Add(SaveUnitPrototype.Flag.StealthAttack);
				if (prto.CruiseMissile) prototype.flags.Add(SaveUnitPrototype.Flag.CruiseMissile);
				if (prto.King) prototype.flags.Add(SaveUnitPrototype.Flag.King);
				if (prto.FlagUnit) prototype.flags.Add(SaveUnitPrototype.Flag.FlagUnit);
				if (prto.AIOffense) prototype.flags.Add(SaveUnitPrototype.Flag.AIOffense);
				if (prto.AIDefense) prototype.flags.Add(SaveUnitPrototype.Flag.AIDefense);
				if (prto.AINavalTransport) prototype.flags.Add(SaveUnitPrototype.Flag.AINavalTransport);
				if (prto.AIArtillery) prototype.flags.Add(SaveUnitPrototype.Flag.AIArtillery);
				if (prto.AIExplore) prototype.flags.Add(SaveUnitPrototype.Flag.AIExplore);
				if (prto.AIArmy) prototype.flags.Add(SaveUnitPrototype.Flag.AIArmy);
				if (prto.AICruiseMissile) prototype.flags.Add(SaveUnitPrototype.Flag.AICruiseMissile);
				if (prto.AIAirBombard) prototype.flags.Add(SaveUnitPrototype.Flag.AIAirBombard);
				if (prto.AIAirDefense) prototype.flags.Add(SaveUnitPrototype.Flag.AIAirDefense);
				if (prto.AINavalPower) prototype.flags.Add(SaveUnitPrototype.Flag.AINavalPower);
				if (prto.AIAirTransport) prototype.flags.Add(SaveUnitPrototype.Flag.AIAirTransport);
				if (prto.AINavalCarrier) prototype.flags.Add(SaveUnitPrototype.Flag.AINavalCarrier);
				if (prto.AITerraform) prototype.flags.Add(SaveUnitPrototype.Flag.AITerraform);
				if (prto.AISettle) prototype.flags.Add(SaveUnitPrototype.Flag.AISettle);
				if (prto.AITacticalNuke) prototype.flags.Add(SaveUnitPrototype.Flag.AITacticalNuke);
				if (prto.AIICBM) prototype.flags.Add(SaveUnitPrototype.Flag.AIICBM);
				if (prto.AINavalMissileTransport) prototype.flags.Add(SaveUnitPrototype.Flag.AINavalMissileTransport);
				if (prto.RequireSupport == 0) prototype.flags.Add(SaveUnitPrototype.Flag.NoSupportRequired);
				int[] targets = stealthTargets != null && prtoIndex < stealthTargets.Length ? stealthTargets[prtoIndex] : null;
				if (targets != null && targets.Length > 0) {
					prototype.stealthTargets = targets.Where(t => t >= 0 && t < Prto.Length).Select(t => Prto[t].Name).Distinct().ToList();
				}

				prototype.actions.UnionWith(GetUnitActions(prto));
				// A job the BIQ has no TFRM for (such as an outpost in a BIQ
				// from before Conquests) is left out.
				prototype.terraformActions.UnionWith(GetUnitTerraforms(prto).Where(terraformIdByCiv3Key.ContainsKey).Select(tfKey => terraformIdByCiv3Key[tfKey]));

				prototype.unproducible = IsUnproducible(prto);

				if (prto.EnslaveResults >= 0 && prto.EnslaveResults < Prto.Length) {
					prototype.enslavesTo = Prto[prto.EnslaveResults].Name;
				}

				if (prto.Required != -1) {
					prototype.requiredTech = save.Techs[prto.Required].id;
				}

				// Land terrains only: the BIQ marks Sea for nearly every unit,
				// which changes nothing.
				for (int t = 0; t < MapGenerator.Civ3TerrainKeys.Length; ++t) {
					string key = MapGenerator.Civ3TerrainKeys[t];
					if (prto.IgnoreMovementCost[t] && key is not ("coast" or "sea" or "ocean")) {
						prototype.ignoreMovementCost.Add(key);
					}
				}

				if (prto.RequiredResource1 != -1) {
					prototype.requiredResources.Add(save.Resources[prto.RequiredResource1].Key);
				}

				if (prto.RequiredResource2 != -1) {
					prototype.requiredResources.Add(save.Resources[prto.RequiredResource2].Key);
				}

				prototype.producibleBy = ImportUnitAvailability(prto);

				// Units are known by name, so only the first unit of each name
				// is kept. Civ3 repeats a unit for each extra AI strategy it
				// has, which is expected; two different units of the same
				// name are not, and are logged.
				// Temporary check until #330 is finished
				SaveUnitPrototype existing = save.UnitPrototypes.Find(p => p.name == prototype.name);
				if (existing == null) {
					save.UnitPrototypes.Add(prototype);
					continue;
				}
				// A repeat for another AI strategy adds its strategies.
				foreach (SaveUnitPrototype.Flag strategy in AIStrategyFlags) {
					if (prototype.flags.Contains(strategy)) {
						existing.flags.Add(strategy);
					}
				}
				if (existing.attack != prototype.attack || existing.defense != prototype.defense
						|| existing.movement != prototype.movement || existing.shieldCost != prototype.shieldCost
						|| existing.hpBonus != prototype.hpBonus || existing.requiredTech != prototype.requiredTech
						|| !existing.producibleBy.SetEquals(prototype.producibleBy)) {
					log.Warning("There are two different units named {Unit}; only the first is kept", prototype.name);
				}
			}
		}

		private class UnitNode {
			private readonly int _civCount;
			private SaveUnitPrototype proto { get; }
			public string name => proto.name;
			public int shieldCost => proto.shieldCost;
			public bool producibleBy(string civ) => proto.producibleBy.Contains(civ);

			public HashSet<UnitNode> prev { get; set; } = [];
			public UnitNode next { get; set; } = null;

			public UnitNode(SaveUnitPrototype proto, int civCount) {
				_civCount = civCount;
				this.proto = proto;
			}

			private bool IsCommon() => proto.producibleBy.Count > (_civCount / 2);

			public override string ToString() {
				var tail = next == null ? " -| " : $" -> {next}";
				return IsCommon() ? $"{name}{tail}" : $"({name}){tail}";
			}
		}

		private void ImportUnitUpgrades() {
			List<Tuple<SaveUnitPrototype, SaveUnitPrototype>> upgradePairs = ResolveUpgradePairs();
			var civs = save.Civilizations.ToList();

			// Build upgrade chains as linked lists, indexed by name
			Dictionary<string, UnitNode> idx = BuildUnitUpgradeChains(upgradePairs, civs);

			// Resolve upgrade mappings for each unit, per civilization
			// By evaluating the unit upgrade chains in the context of every civilization,
			// we capture the full picture of how any particular unit can upgrade by any civ.
			// This full "closure" is the record we store in the save file and base ruleset.

			var upgradeBook = new Dictionary<string, HashSet<string>>();

			foreach (KeyValuePair<string, UnitNode> kv in idx) {
				var name = kv.Key;

				if (!upgradeBook.ContainsKey(name))
					upgradeBook[name] = new HashSet<string>();

				foreach (Civilization civ in civs) {
					var node = kv.Value;

					if (!node.producibleBy(civ.name))
						continue;

					var upgrade = node.next;
					while (upgrade != null && !upgrade.producibleBy(civ.name)) {
						upgrade = upgrade.next;
					}

					if (upgrade != null)
						upgradeBook[name].Add(upgrade.name);
				}
			}

			// Apply
			foreach (SaveUnitPrototype proto in save.UnitPrototypes) {
				if (upgradeBook.TryGetValue(proto.name, out HashSet<string> target)) {
					proto.upgradesTo = target.OrderBy(x => x).ToList();
				} else {
					throw new DataException($"Missing upgrade for {proto.name}");
				}
			}
		}

		/// This function takes pairs of units in the save game format (complex upgrade chain relations)
		/// and creates a name->Node mapping for further processing. The big idea here
		/// is to reuse the same object, effectively gathering all references relating to
		/// a particular unit in one object. And then building a nice index to these nodes.
		///
		/// See https://forums.civfanatics.com/threads/how-to-upgrade-regular-units-to-uus.108396/
		private static Dictionary<string, UnitNode> BuildUnitUpgradeChains(
			List<Tuple<SaveUnitPrototype, SaveUnitPrototype>> upgradePairs,
			List<Civilization> civilizations) {
			var idx = new Dictionary<string, UnitNode>();
			var civCount = civilizations.Count;

			foreach (Tuple<SaveUnitPrototype, SaveUnitPrototype> pair in upgradePairs) {
				var a = idx.TryGetValue(pair.Item1.name, out var nodeA) ? nodeA : new UnitNode(pair.Item1, civCount);
				var b = pair.Item2 == null ? null :
					idx.TryGetValue(pair.Item2.name, out var nodeB) ? nodeB : new UnitNode(pair.Item2, civCount);

				a.next = b;
				idx[a.name] = a;
				if (b != null) {
					b.prev.Add(a);
					idx[b.name] = b;
				}
			}

			return idx;
		}

		/// This method builds a Dictionary of unit upgrades based on Civ3 data.
		/// The dictionary represents the raw upgrade relationships as they are defined in a save game file.
		private List<Tuple<SaveUnitPrototype, SaveUnitPrototype>> ResolveUpgradePairs() {
			PRTO[] Prto = biq.Prto ?? defaultBiq.Prto;
			var unitPrototypeDict = save.UnitPrototypes.ToDictionary(b => b.name);

			List<Tuple<SaveUnitPrototype, SaveUnitPrototype>> upgradePairs = [];

			foreach (PRTO prto in Prto) {
				SaveUnitPrototype upgradeFrom = unitPrototypeDict[prto.Name];
				if (prto.UpgradeTo != -1) {
					SaveUnitPrototype upgradeTo = unitPrototypeDict[Prto[prto.UpgradeTo].Name];
					upgradePairs.Add(new Tuple<SaveUnitPrototype, SaveUnitPrototype>(upgradeFrom, upgradeTo));
				} else {
					upgradePairs.Add(new Tuple<SaveUnitPrototype, SaveUnitPrototype>(upgradeFrom, null));
				}
			}

			// Note: duplicate pairs are due to "OtherStrategy", alternate AI strategy for unit

			return upgradePairs;
		}

		// Civ3 tells buildings apart by their place in its list, so a mod may
		// give several the same name (Modern has three Barracks and three
		// Airports); C7 tells them apart by name. So the first with a name
		// keeps it, and the others get " (2)", " (3)" and so on, in the
		// list's order, everywhere a building is named.
		private readonly Dictionary<BLDG[], string[]> buildingNames = new(ReferenceEqualityComparer.Instance);

		private string BuildingName(BLDG[] buildings, int index) {
			if (!buildingNames.TryGetValue(buildings, out string[] names)) {
				names = UniqueNames(buildings.Select(b => b.Name).ToArray());
				buildingNames[buildings] = names;
			}
			return names[index];
		}

		// The names, with each name that came before given a number.
		internal static string[] UniqueNames(string[] names) {
			// A numbered name never takes one another building has as its own.
			HashSet<string> own = new(names);
			HashSet<string> used = new();
			string[] unique = new string[names.Length];
			for (int i = 0; i < names.Length; ++i) {
				string name = names[i];
				for (int n = 2; used.Contains(name) || (n > 2 && own.Contains(name)); ++n) {
					name = $"{names[i]} ({n})";
				}
				used.Add(name);
				unique[i] = name;
			}
			return unique;
		}

		private void ImportBuildings() {
			BLDG[] Bldg = biq.Bldg ?? defaultBiq.Bldg;

			for (int index = 0; index < Bldg.Length; ++index) {
				BLDG bldg = Bldg[index];
				// Civ3's Wealth is a building with the capitalization flag,
				// but in C7 it is an inflow.
				if (bldg.Capitalization) {
					SaveInflow inflow = new () {
						name = bldg.Name,
						iconRowIndex = pediaIcons.buildingToRowNumberMapping[bldg.CivilopediaEntry],
						localYield = [
							new SaveLocalYield(InflowYield.commerce, "inflows.result.wealth.commerce"),
						],
					};
					save.Inflows.Add(inflow);
					continue; // We don't consider Wealth as a building
				}

				SaveBuilding building = new() {
					name=BuildingName(Bldg, index),
					civilopediaEntry=bldg.CivilopediaEntry?.Trim(),
					shieldCost=bldg.Cost * 10, // In Civ3 files, building costs are stored at 1/10th of their actual value
					populationCost=0, // In Civ3, a building cannot have a population cost
					isSmallWonder=bldg.SmallWonder,
					greatWonderProperties=bldg.Wonder ? new SaveBuilding.GreatWonderProperties() : null,
					culturePerTurn=bldg.Culture,
					contentFacesInCity=bldg.ContentFaces - bldg.UnhappyFaces,
					iconRowIndex=pediaIcons.buildingToRowNumberMapping[bldg.CivilopediaEntry],
					combatDefenseBonus=bldg.DefenseBonus / 100.0,
					navalDefenseBonus=bldg.NavalDefenseBonus / 100.0,
					maintenanceCost=bldg.MaintenanceCost,
					numberOfArmiesRequired=bldg.NumberOfArmiesRequired,
					// Production is stored in 25% steps.
					productionBonusPercent=bldg.Production * 25,
				};

				if (bldg.RequiredAdvance != -1) {
					building.requiredTech = save.Techs[bldg.RequiredAdvance].id;
				}

				if (bldg.RequiredBuilding != -1) {
					building.requiredBuilding = BuildingName(Bldg, bldg.RequiredBuilding);
					building.requiredBuildingCount = bldg.NumberOfRequiredBuildings;
				}

				if (bldg.RequiredGovernment >= 0) {
					buildingRequiredGovernments.Add((building, bldg.RequiredGovernment));
				}

				building.contentFacesAllCities = bldg.ContentFacesAllCities - bldg.UnhappyFacesAllCities;
				building.pollution = bldg.Pollution;
				building.bombardDefense = bldg.BombardDefense;
				building.airPower = bldg.AirPower;
				building.navalPower = bldg.NavalPower;
				if (bldg.SpaceshipPart >= 0) {
					building.spaceshipPart = bldg.SpaceshipPart;
				}
				if (bldg.DoublesHappiness >= 0) {
					building.doublesHappinessOf = BuildingName(Bldg, bldg.DoublesHappiness);
				}
				if (bldg.CanBuildUnits && bldg.UnitProduced >= 0) {
					PRTO[] prto = biq.Prto ?? defaultBiq.Prto;
					building.unitProduced = prto[bldg.UnitProduced].Name;
					building.unitFrequency = bldg.UnitFrequency;
				}

				if (bldg.RequiredResource1 != -1) {
					building.requiredResources.Add(save.Resources[bldg.RequiredResource1].Key);
				}

				if (bldg.RequiredResource2 != -1) {
					building.requiredResources.Add(save.Resources[bldg.RequiredResource2].Key);
				}

				if (bldg.RenderedObsoleteBy != -1) {
					building.renderedObsoleteBy = save.Techs[bldg.RenderedObsoleteBy].id;
				}

				if (bldg.GainInEveryCity >= 0) {
					building.greatWonderProperties.buildingGainedInEveryCity = BuildingName(Bldg, bldg.GainInEveryCity);
				}
				if (bldg.GainInEveryCityOnContinent >= 0) {
					building.greatWonderProperties.buildingGainedInEveryCityOnContinent = BuildingName(Bldg, bldg.GainInEveryCityOnContinent);
				}

				building.flags = LoadBuildingFlags(bldg).ToHashSet();
				building.traits = LoadBuildingTraits(bldg).ToHashSet();

				// Civ3 has no walls flag. Its City Walls are the building
				// that both raises the city's defense against land attacks
				// and protects it from bombardment; a Coastal Fortress only
				// guards against ships, and a wonder like the Great Wall that
				// acts as walls does so through its own flags. City Walls
				// provide "a land bombardment defense of 8, and +50% to
				// defensive bonus against land attacks", and a Coastal
				// Fortress "a naval bombardment defense of 8, +50% against
				// naval attacks" (https://civfanatics.com/civ3/civilopedia/improvements/),
				// as in conquests.biq.
				if (bldg.DefenseBonus > 0 && bldg.BombardDefense > 0 && !bldg.Wonder && !bldg.SmallWonder) {
					building.flags.Add(SaveBuilding.Flag.ProvidesWalls);
					building.flags.Add(SaveBuilding.Flag.CanOnlyBeBuiltInTowns);
				}

				MapFlagsToLuaFunctions(building);

				save.Buildings.Add(building);
			}
		}

		private static IEnumerable<SaveBuilding.Flag> LoadBuildingFlags(BLDG bldg) {
			return new[] {
				(bldg.CenterOfEmpire, SaveBuilding.Flag.IsCenterOfEmpire),
				(bldg.CoastalInstallation, SaveBuilding.Flag.MustBeCoastal),
				(bldg.MustBeNearRiver, SaveBuilding.Flag.MustBeNearRiver),
				(bldg.MustBeNearWater, SaveBuilding.Flag.MustBeNearWater),
				(bldg.CanMeltdown, SaveBuilding.Flag.CanMeltdown),
				(bldg.VeteranGroundUnits, SaveBuilding.Flag.VeteranGroundUnits),
				(bldg.VeteranSeaUnits, SaveBuilding.Flag.VeteranSeaUnits),
				(bldg.VeteranAirUnits, SaveBuilding.Flag.VeteranAirUnits),
				(bldg.IncreasesLuxuryTrade, SaveBuilding.Flag.IncreasesLuxuryTrade),
				(bldg.ReducesCorruption, SaveBuilding.Flag.ReducesCorruption),
				(bldg.ForbiddenPalace, SaveBuilding.Flag.ForbiddenPalace),
				(bldg.IncreasesShieldsInWater, SaveBuilding.Flag.IncreasesShieldsInWater),
				(bldg.IncreasesFoodInWater, SaveBuilding.Flag.IncreasesFoodInWater),
				(bldg.IncreasesTradeInWater, SaveBuilding.Flag.IncreasesTradeInWater),
				(bldg.IncreasedTrade, SaveBuilding.Flag.IncreasedTrade),
				(bldg.AllowsCitySize2, SaveBuilding.Flag.AllowsCitySize2),
				(bldg.AllowsCitySize3, SaveBuilding.Flag.AllowsCitySize3),
				(bldg.DoublesCityGrowthRate, SaveBuilding.Flag.DoublesCityGrowthRate),
				(bldg.ReplacesOtherBuildings, SaveBuilding.Flag.ReplacesOtherBuildings),
				(bldg.TreasuryEarnsInterest, SaveBuilding.Flag.TreasuryEarnsInterest),
				(bldg.AllowsBuildArmy, SaveBuilding.Flag.AllowsBuildArmy),
				(bldg.AllowsLargerArmies, SaveBuilding.Flag.AllowsLargerArmies),
				(bldg.RequiresVictoriousArmy, SaveBuilding.Flag.RequiresVictoriousArmy),
				(bldg.Plus50PercentResearch, SaveBuilding.Flag.Plus50PercentResearch),
				(bldg.DoublesResearchOutput, SaveBuilding.Flag.DoublesResearchOutput),
				(bldg.Plus50PercentLuxury, SaveBuilding.Flag.Plus50PercentLuxury),
				(bldg.Plus50PercentCommerce, SaveBuilding.Flag.Plus50PercentCommerce),
				(bldg.PaysTradeMaintenance, SaveBuilding.Flag.PaysTradeMaintenance),
				(bldg.AllowsEnemyTerritoryHealing, SaveBuilding.Flag.AllowsEnemyTerritoryHealing),
				(bldg.ReducesWarWeariness, SaveBuilding.Flag.ReducesWarWeariness),
				(bldg.RemovesPopulationPollution, SaveBuilding.Flag.RemovesPopulationPollution),
				(bldg.ReducesBuildingPollution, SaveBuilding.Flag.ReducesBuildingPollution),
				(bldg.ReducedWarWeariness, SaveBuilding.Flag.ReducesWarWearinessEverywhere),
				(bldg.ContinentalMoodEffects, SaveBuilding.Flag.ContinentalMoodEffects),
				(bldg.SafeSeaTravel, SaveBuilding.Flag.SafeSeaTravel),
				(bldg.GainAnyTechKnownByTwoCivs, SaveBuilding.Flag.GainAnyTechKnownByTwoCivs),
				(bldg.DoubleCombatVsBarbarians, SaveBuilding.Flag.DoubleCombatVsBarbarians),
				(bldg.IncreasedShipMovement, SaveBuilding.Flag.IncreasedShipMovement),
				(bldg.PlusTwoShipMovement, SaveBuilding.Flag.PlusTwoShipMovement),
				(bldg.CheaperUpgrades, SaveBuilding.Flag.CheaperUpgrades),
				(bldg.TwoFreeAdvances, SaveBuilding.Flag.TwoFreeAdvances),
				(bldg.AllowDiplomaticVictory, SaveBuilding.Flag.AllowDiplomaticVictory),
				(bldg.AllowsNuclearWeapons, SaveBuilding.Flag.AllowsNuclearWeapons),
				(bldg.DoubleCityGrowth, SaveBuilding.Flag.DoublesCityGrowthEverywhere),
				(bldg.TouristAttraction, SaveBuilding.Flag.TouristAttraction),
				(bldg.IncreasesLeaderChance, SaveBuilding.Flag.IncreasesLeaderChance),
				(bldg.IncreasedArmyValue, SaveBuilding.Flag.IncreasedArmyValue),
				(bldg.DecreasesMissileSuccess, SaveBuilding.Flag.DecreasesMissileSuccess),
				(bldg.AllowsSpyMissions, SaveBuilding.Flag.AllowsSpyMissions),
				(bldg.BuildSpaceshipParts, SaveBuilding.Flag.BuildSpaceshipParts),
				(bldg.AllowsWaterTrade, SaveBuilding.Flag.AllowsWaterTrade),
				(bldg.AllowsAirTrade, SaveBuilding.Flag.AllowsAirTrade),
				(bldg.DoublesSacrifice, SaveBuilding.Flag.DoublesSacrifice),
				(bldg.ResistantToBribery, SaveBuilding.Flag.ResistantToBribery),
				(bldg.DoubleCityDefenses, SaveBuilding.Flag.DoubleCityDefenses),
				// The BIQ has the flag twice, among the improvement flags and
				// among the small wonder ones; conquests.biq sets the
				// improvement one on Iron Works ("The city that builds it must
				// have Iron and Coal in its city radius", Civilopedia).
				(bldg.GoodsMustBeInCityRadius || bldg.GoodsMustBeInCityRadiusImprovement, SaveBuilding.Flag.GoodsMustBeInCityRadius),
			}
			.Where(t => t.Item1)
			.Select(t => t.Item2);
		}

		private static IEnumerable<Civilization.Trait> LoadBuildingTraits(BLDG bldg) {
			return new[] {
				(bldg.Militaristic, Civilization.Trait.Militaristic),
				(bldg.Commercial, Civilization.Trait.Commercial),
				(bldg.Expansionist, Civilization.Trait.Expansionist),
				(bldg.Scientific, Civilization.Trait.Scientific),
				(bldg.Religious, Civilization.Trait.Religious),
				(bldg.Industrious, Civilization.Trait.Industrious),
				(bldg.Agricultural, Civilization.Trait.Agricultural),
				(bldg.Seafaring, Civilization.Trait.Seafaring),
			}
			.Where(t => t.Item1)
			.Select(t => t.Item2);
		}

		private static void MapFlagsToLuaFunctions(SaveBuilding building) {
			var flagToProductionRule = new Dictionary<SaveBuilding.Flag, string>
			{
				{ SaveBuilding.Flag.MustBeCoastal, "must_be_coastal" },
				{ SaveBuilding.Flag.MustBeNearRiver, "must_be_near_river" },
				{ SaveBuilding.Flag.MustBeNearWater, "must_be_near_water" },
				{ SaveBuilding.Flag.AllowsCitySize2, "allows_city_size_2" },
				{ SaveBuilding.Flag.AllowsCitySize3, "allows_city_size_3" },
				{ SaveBuilding.Flag.CanOnlyBeBuiltInTowns, "can_only_be_built_in_towns"}
			};

			var flagToUnitProductionEffect = new Dictionary<SaveBuilding.Flag, string>
			{
				{ SaveBuilding.Flag.VeteranGroundUnits, "veteran_ground_units" },
				{ SaveBuilding.Flag.VeteranSeaUnits, "veteran_sea_units" },
				// The Airport "Produces veteran air units"
				// (https://civfanatics.com/civ3/civilopedia/improvements/).
				{ SaveBuilding.Flag.VeteranAirUnits, "veteran_air_units" },
			};

			var flagToTileModifier = new Dictionary<SaveBuilding.Flag, string>
			{
				{ SaveBuilding.Flag.IncreasesFoodInWater, "increases_food_in_water" },
				{ SaveBuilding.Flag.IncreasesShieldsInWater, "increases_shields_in_water" },
				{ SaveBuilding.Flag.IncreasesTradeInWater, "increases_trade_in_water" },
				{ SaveBuilding.Flag.IncreasedTrade, "increased_trade" },
			};

			foreach (var flag in building.flags) {
				if (flagToProductionRule.TryGetValue(flag, out var productionRule)) {
					building.productionPrerequisites.Add($"buildings.production_rules.{productionRule}");
				}

				if (flagToUnitProductionEffect.TryGetValue(flag, out var unitEffect)) {
					building.onFinishedUnitProduction.Add($"buildings.unit_production_effects.{unitEffect}");
				}

				if (flagToTileModifier.TryGetValue(flag, out var tileModifier)) {
					building.tileModifiers.Add($"buildings.tile_modifiers.{tileModifier}");
				}
			}
		}

		private void ImportCiv3TerrainTypes() {
			TERR[] Terr = biq.Terr ?? defaultBiq.Terr;
			bool[,] TerrGood = biq.TerrGood ?? defaultBiq.TerrGood;
			int civ3Index = 0;
			foreach (TERR terrain in Terr) {
				TerrainType c7TerrainType = TerrainType.ImportFromCiv3(civ3Index, terrain);
				for (int i = 0; i < TerrGood.GetLength(1); ++i) {
					if (TerrGood[civ3Index, i]) {
						c7TerrainType.allowedResources.Add(save.Resources[i].Key);
					}
				}
				AddYieldBonusesForTerrainImprovements(c7TerrainType.Key, terrain);
				c7TerrainType.landmark = new TerrainLandmark {
					enabled = terrain.LandmarkEnabled != 0,
					name = terrain.LandmarkName ?? "",
					civilopediaEntry = terrain.LandmarkCivilopediaEntry ?? "",
					food = terrain.LandmarkFood,
					shields = terrain.LandmarkShields,
					commerce = terrain.LandmarkCommerce,
					irrigationBonus = terrain.LandmarkIrrigationBonus,
					miningBonus = terrain.LandmarkMiningBonus,
					roadBonus = terrain.LandmarkRoadBonus,
					movementCost = terrain.LandmarkMovementBonus,
					defenseBonus = terrain.LandmarkDefensiveBonus,
				};
				save.TerrainTypes.Add(c7TerrainType);
				civ3Index++;
			}
		}

		private void ImportCiv3ExperienceLevels() {
			EXPR[] Expr = biq.Expr ?? defaultBiq.Expr;
			if (Expr.Length != 4) {
				throw new Exception("BIQ data must include four experience levels.");
			}

			Dictionary<string, ExperienceLevel> levelsByKey = new Dictionary<string, ExperienceLevel>();

			foreach (EXPR expr in Expr) {
				// Generate a unique key for this level based on its name. If multiple levels have the same name, append apostrophes
				// to the end until the key is unique.
				string key = expr.Name;
				while (levelsByKey.ContainsKey(key)) {
					key += "'";
				}

				ExperienceLevel level = ExperienceLevel.ImportFromCiv3(key, expr, levelsByKey.Count);
				save.ExperienceLevels.Add(level);
				levelsByKey.Add(key, level);

				if (levelsByKey.Count == 2) {
					save.DefaultExperienceLevel = key;
				}
			}
		}

		private void ImportCiv3DefensiveBonuses() {
			RULE Rule = biq?.Rule?[0] ?? defaultBiq.Rule[0];
			save.StrengthBonuses.Add(new StrengthBonus {
				description = "Fortified",
				amount = Rule.FortificationsDefensiveBonus / 100.0
			});

			save.StrengthBonuses.Add(new StrengthBonus {
				description = "Behind river",
				amount = Rule.RiverDefensiveBonus / 100.0
			});

			save.StrengthBonuses.Add(new StrengthBonus {
				description = "Town",
				amount = Rule.TownDefenseBonus / 100.0
			});

			save.StrengthBonuses.Add(new StrengthBonus {
				description = "City",
				amount = Rule.CityDefenseBonus / 100.0
			});

			save.StrengthBonuses.Add(new StrengthBonus {
				description = "Metropolis",
				amount = Rule.MetropolisDefenseBonus / 100.0
			});
		}

		private SaveUnitPrototype MatchCiv3IndexToImportedUnit(int civ3Index) {
			PRTO[] Prto = biq.Prto ?? defaultBiq.Prto;
			if (civ3Index == -1) return null;
			string name = Prto[civ3Index].Name;
			return save.UnitPrototypes.Where(up => up.name == name).First();
		}

		private void ImportBarbarianInfo() {
			RULE Rule = biq?.Rule?[0] ?? defaultBiq.Rule[0];
			PRTO[] Prto = biq?.Prto ?? defaultBiq.Prto;
			BarbarianInfo barbInfo = save.BarbarianInfo;

			barbInfo.basicBarbarianUnit = MatchCiv3IndexToImportedUnit(Rule.BasicBarbarianUnitType)?.name;
			barbInfo.advancedBarbarianUnit = MatchCiv3IndexToImportedUnit(Rule.AdvancedBarbarianUnitType)?.name;
			barbInfo.barbarianSeaUnit = MatchCiv3IndexToImportedUnit(Rule.BarbarianSeaUnitType)?.name;
		}

		private void ImportTechs() {
			BiqData theBiq = biq.Tech is null ? defaultBiq : biq;

			// Pass one: create the techs without prereqs.
			for (int i = 0; i < theBiq.Tech.Length; ++i) {
				TECH t = theBiq.Tech[i];

				SaveTech st = new() {
					id = ids.CreateID("tech"),
					Name = t.Name,
					CivilopediaEntry = t.CivilopediaEntry,
					Cost = t.Cost,
					RequiredForEraAdvancement = !t.NotRequiredForEraAdvancement,
					EraCivilopediaName = t.Era == -1 ? EraUtils.HIDDEN_CVLPD : theBiq.Eras[t.Era].CivilopediaEntry,
					SmallIconPath = t.Era == -1 ? "" : pediaIcons.GetTechIconPath(t.CivilopediaEntry),
					X = t.X,
					Y = t.Y,
					flags = LoadTechFlags(t).ToHashSet(),
				};
				save.Techs.Add(st);
			}

			// Pass two: set up the prereqs now that we can index into the list
			// of techs.
			for (int i = 0; i < theBiq.Tech.Length; ++i) {
				TECH t = theBiq.Tech[i];
				SaveTech st = save.Techs[i];

				if (t.Prerequisite1 > -1) {
					st.Prerequisites.Add(save.Techs[t.Prerequisite1].id);
				}
				if (t.Prerequisite2 > -1) {
					st.Prerequisites.Add(save.Techs[t.Prerequisite2].id);
				}
				if (t.Prerequisite3 > -1) {
					st.Prerequisites.Add(save.Techs[t.Prerequisite3].id);
				}
				if (t.Prerequisite4 > -1) {
					st.Prerequisites.Add(save.Techs[t.Prerequisite4].id);
				}
			}

			// Now that we have ids for all the techs, distribute the free techs
			for (int i = 0; i < save.Civilizations.Count; ++i) {
				Civilization sc = save.Civilizations[i];
				// The civs may come from a different BIQ than the techs.
				RACE race = RaceBiq.Race[i];

				if (race.FreeTech1 > -1) {
					sc.startingTechs.Add(save.Techs[race.FreeTech1].id);
				}
				if (race.FreeTech2 > -1) {
					sc.startingTechs.Add(save.Techs[race.FreeTech2].id);
				}
				if (race.FreeTech3 > -1) {
					sc.startingTechs.Add(save.Techs[race.FreeTech3].id);
				}
				if (race.FreeTech4 > -1) {
					sc.startingTechs.Add(save.Techs[race.FreeTech4].id);
				}

				// Remove any invalid starting techs. Some scenarios like
				// Fall of Rome give starting techs without giving all of the
				// prereqs, so they should be ignored.
				sc.startingTechs.RemoveWhere(t => {
					SaveTech st = save.Techs.Find(x => x.id == t);
					foreach (ID prereqId in st.Prerequisites) {
						if (!sc.startingTechs.Contains(prereqId)) {
							return true;
						}
					}
					return false;
				});
			}
		}

		private static IEnumerable<SaveTech.Flag> LoadTechFlags(TECH t) {
			return new[] {
				(t.BonusTechToFirstCivThatResearches, SaveTech.Flag.BonusTechToFirstCivThatResearches),
				(t.EnablesBridges, SaveTech.Flag.EnablesBridges),
				(t.DoublesWealthProduction, SaveTech.Flag.DoublesWealthProduction),
				(t.EnablesIrrigationEverywhere, SaveTech.Flag.EnablesIrrigationEverywhere),
				(t.DoublesWorkerRate, SaveTech.Flag.DoublesWorkerRate),
				(t.EnablesRightOfPassage, SaveTech.Flag.EnablesRightOfPassage),
				(t.EnablesDiplomats, SaveTech.Flag.EnablesDiplomats),
				(t.DisablesDiseases, SaveTech.Flag.DisablesDiseases),
				(t.EnablesConscription, SaveTech.Flag.EnablesConscription),
				(t.EnablesMobilization, SaveTech.Flag.EnablesMobilization),
				(t.EnablesRecycling, SaveTech.Flag.EnablesRecycling),
				(t.EnablesPrecisionBombing, SaveTech.Flag.EnablesPrecisionBombing),
				(t.EnablesMutualProtection, SaveTech.Flag.EnablesMutualProtection),
				(t.EnablesMilitaryAlliances, SaveTech.Flag.EnablesMilitaryAlliances),
				(t.EnablesTradeEmbargoes, SaveTech.Flag.EnablesTradeEmbargoes),
				(t.EnablesTradeOverSea, SaveTech.Flag.EnablesTradeOverSea),
				(t.EnablesTradeOverOcean, SaveTech.Flag.EnablesTradeOverOcean),
				(t.EnablesMapTrading, SaveTech.Flag.EnablesMapTrading),
				(t.EnablesCommunicationTrading, SaveTech.Flag.EnablesCommunicationTrading),
				(t.CannotBeTraded, SaveTech.Flag.CannotBeTraded),
				(t.PermitsSacrifice, SaveTech.Flag.PermitsSacrifice),
				(t.RevealsWorldMap, SaveTech.Flag.RevealsWorldMap),
			}
			.Where(t => t.Item1)
			.Select(t => t.Item2);
		}

		private void ImportCitizenTypes() {
			BiqData theBiq = biq.Ctzn is null ? defaultBiq : biq;

			for (int i = 0; i < theBiq.Ctzn.Length; ++i) {
				CTZN c = theBiq.Ctzn[i];

				CitizenType ct = new() {
					Id = ids.CreateID("CitizenType"),
					IsDefaultCitizen = c.DefaultCitizen == 1,
					SingularName = c.SingularName,
					CivilopediaEntry = c.CivilopediaEntry,
					PluralName = c.PluralName,
					Luxuries = c.Luxuries,
					Research = c.Research,
					Taxes = c.Taxes,
					Corruption = c.Corruption,
					Construction = c.Construction
				};
				if (!ct.IsDefaultCitizen) {
					ct.SpecialistIndex = i;
				}
				if (c.Prerequisite > -1) {
					ct.PrerequisiteTech = save.Techs[c.Prerequisite].id;
				}

				save.CitizenTypes.Add(ct);
			}
		}

		private void ImportTerraforms() {
			BiqData theBiq = biq.Tfrm is null ? defaultBiq : biq;

			for (int i = 0; i < theBiq.Tfrm.Length; ++i) {
				TFRM t = theBiq.Tfrm[i];

				TerraformKey tfKey = ConvertCiv3Order(t.Order, i);

				SaveTerraform tf = new() {
					Id = ids.CreateID("Terraform"),
					Name = t.Name,
					CivilopediaEntry = t.CivilopediaEntry,
					TurnsToComplete = t.TurnsToComplete,
				};

				tf.SetUpByTerraformKey(tfKey);

				if (t.Required > -1) {
					tf.RequiredTech = save.Techs[t.Required].id;
				}
				if (t.RequiredResource1 > -1) {
					tf.RequiredResources.Add(save.Resources[t.RequiredResource1].Key);
				}
				if (t.RequiredResource2 > -1) {
					tf.RequiredResources.Add(save.Resources[t.RequiredResource2].Key);
				}

				save.TerraForms.Add(tf);
				terraformIdByCiv3Key[tfKey] = tf.Id;
			}
		}

		private void SetBonus(string terrainKey, string improvementKey, Tile.YieldType type, int bonus) {
			SaveTerrainImprovement improvement = save.TerrainImprovements.Find(ti => ti.key == improvementKey);

			if (!improvement.bonusYields.TryGetValue(terrainKey, out var yieldDict)) {
				yieldDict = new Dictionary<Tile.YieldType, int>();
				improvement.bonusYields[terrainKey] = yieldDict;
			}

			yieldDict[type] = bonus;
		}

		private void AddYieldBonusesForTerrainImprovements(string terrainKey, TERR terrainType) {
			if (terrainType.MiningBonus > 0) {
				SetBonus(terrainKey, MINE, Tile.YieldType.Production, terrainType.MiningBonus);
			}
			if (terrainType.IrrigationBonus > 0) {
				SetBonus(terrainKey, IRRIGATION, Tile.YieldType.Food, terrainType.IrrigationBonus);
			}
			if (terrainType.RoadBonus > 0) {
				SetBonus(terrainKey, ROAD, Tile.YieldType.Commerce, terrainType.RoadBonus);
				SetBonus(terrainKey, RAILROAD, Tile.YieldType.Commerce, terrainType.RoadBonus);
			}
		}

		// Civ3's worker jobs, in the order its rules list them. A scenario
		// may rename a job and its order ("Determines the verbiage used to
		// explain the orders of a worker", Civ3's editor help) but has the
		// same jobs in the same places: Space Opera's "Build Station" is the
		// fortress.
		private static readonly TerraformKey[] Civ3JobOrder = [
			TerraformKey.BuildMine, TerraformKey.Irrigate, TerraformKey.BuildFortress, TerraformKey.BuildRoad,
			TerraformKey.BuildRailroad, TerraformKey.PlantForest, TerraformKey.ClearForest, TerraformKey.ClearWetlands,
			TerraformKey.ClearDamage, TerraformKey.BuildAirfield, TerraformKey.BuildRadarTower, TerraformKey.BuildOutpost,
			TerraformKey.BuildBarricade,
		];

		private static TerraformKey ConvertCiv3Order(string order, int index) {
			return order switch {
				"Build Mine" => TerraformKey.BuildMine,
				"Irrigate" => TerraformKey.Irrigate,
				"Build Fortress" => TerraformKey.BuildFortress,
				"Build Road" => TerraformKey.BuildRoad,
				"Build Railroad" => TerraformKey.BuildRailroad,
				"Plant Forest" => TerraformKey.PlantForest,
				"Clear Forest" => TerraformKey.ClearForest,
				"Clear Wetlands" => TerraformKey.ClearWetlands,
				"Clear Damage" => TerraformKey.ClearDamage,
				"Build Airfield" => TerraformKey.BuildAirfield,
				"Build Radar Tower" => TerraformKey.BuildRadarTower,
				"Build Outpost" => TerraformKey.BuildOutpost,
				"Build Barricade" or "Build Barricades" => TerraformKey.BuildBarricade,
				_ when index >= 0 && index < Civ3JobOrder.Length => Civ3JobOrder[index],
				_ => throw new NotSupportedException($"Unknown order: {order}"),
			};
		}

		private void ImportGovernments() {
			BiqData theBiq = biq.Govt is null ? defaultBiq : biq;

			foreach (QueryCiv3.Biq.GOVT govt in theBiq.Govt) {
				Government g = new();
				g.id = ids.CreateID("Government");
				g.name = govt.Name;
				g.civilopediaEntry = govt.CivilopediaEntry;
				if (govt.PrerequisiteTechnology != -1) {
					g.prerequisiteTech = save.Techs[govt.PrerequisiteTechnology].id;
				}
				g.defaultType = govt.DefaultType == 1;
				g.transitionType = govt.TransitionType == 1;
				g.requiresMaintenance = govt.RequiresMaintenance == 1;
				g.hasTilePenalty = govt.TilePenalty == 1;
				g.hasTradeBonus = govt.TradeBonus == 1;
				g.corruptionType = (Government.CorruptionType)govt.Corruption;
				g.hurryingType = (Government.HurryProductionType)govt.Hurrying;
				g.rateCap = govt.ScienceRateCap;
				g.warWeariness = govt.WarWeariness;
				g.draftLimit = govt.DraftLimit;
				g.militaryPoliceLimit = govt.MilitaryPoliceLimit;
				g.workerRate = govt.WorkerRate;
				g.allUnitsFree = govt.FreeUnits == -1;
				g.xenophobic = govt.Xenophobic != 0;
				g.forcedResettlement = govt.ForceResettle != 0;
				g.freeUnitsPerTown = govt.FreeUnitsPerTown;
				g.freeUnitsPerCity = govt.FreeUnitsPerCity;
				g.freeUnitsPerMetropolis = govt.FreeUnitsPerMetropolis;
				g.unitCost = govt.UnitCost;
				g.assimilationChance = govt.AssimilationChance;
				g.immuneTo = govt.ImmuneTo;
				g.diplomatLevel = ExperienceKey(govt.DiplomatsAre);
				g.spyLevel = ExperienceKey(govt.SpiesAre);
				string[] maleTitles = [govt.MaleRulerTitle1, govt.MaleRulerTitle2, govt.MaleRulerTitle3, govt.MaleRulerTitle4];
				string[] femaleTitles = [govt.FemaleRulerTitle1, govt.FemaleRulerTitle2, govt.FemaleRulerTitle3, govt.FemaleRulerTitle4];
				for (int i = 0; i < Math.Clamp(govt.RulerTitlePairsUsed, 1, maleTitles.Length); i++) {
					g.maleRulerTitles.Add(maleTitles[i]?.Trim() ?? "");
					g.femaleRulerTitles.Add(femaleTitles[i]?.Trim() ?? "");
				}

				save.Governments.Add(g);
			}
			// How each government stands against each other one.
			for (int a = 0; a < save.Governments.Count && theBiq.GovtGovt != null && a < theBiq.GovtGovt.GetLength(0); ++a) {
				for (int b = 0; b < save.Governments.Count && b < theBiq.GovtGovt.GetLength(1); ++b) {
					QueryCiv3.Biq.GOVT_GOVT r = theBiq.GovtGovt[a, b];
					save.Governments[a].relations.Add(new GovernmentRelation {
						government = save.Governments[b].name,
						canBribe = r.CanBribe != 0,
						briberyModifier = r.BriberyModifier,
						resistanceModifier = r.ResistanceModifier,
					});
				}
			}
		}

		// The key of the experience level at the index, or null.
		private string ExperienceKey(int index) {
			return index >= 0 && index < save.ExperienceLevels.Count ? save.ExperienceLevels[index].key : null;
		}

		// Civ3's flavors, and which advances, improvements and civilizations
		// have each (a bit per flavor in their Flavors field).
		private void ImportFlavors() {
			FLAV[] flavors = biq.Flav ?? defaultBiq?.Flav;
			if (flavors == null) {
				return;
			}
			save.Rules.Flavors = [];
			for (int i = 0; i < flavors.Length; ++i) {
				FLAV f = flavors[i];
				save.Rules.Flavors.Add(new FlavorRule {
					Name = f.Name,
					Relationships = Enumerable.Range(0, Math.Clamp(f.NumberOfFlavors, 0, 7)).Select(j => f.FlavorRelationship[j]).ToList(),
				});
			}
			List<FlavorRule> list = save.Rules.Flavors;
			void Mark(int bits, string name, Func<FlavorRule, List<string>> into) {
				for (int i = 0; i < list.Count && i < 31; ++i) {
					if ((bits & (1 << i)) != 0) {
						into(list[i]).Add(name);
					}
				}
			}
			TECH[] techs = biq.Tech ?? defaultBiq?.Tech;
			for (int i = 0; techs != null && i < techs.Length && i < save.Techs.Count; ++i) {
				Mark(techs[i].Flavors, save.Techs[i].Name, f => f.Techs);
			}
			BLDG[] buildings = biq.Bldg ?? defaultBiq?.Bldg;
			for (int i = 0; buildings != null && i < buildings.Length; ++i) {
				Mark(buildings[i].Flavors, BuildingName(buildings, i), f => f.Buildings);
			}
			RACE[] races = biq.Race ?? defaultBiq?.Race;
			for (int i = 0; races != null && i < races.Length && i < save.Civilizations.Count; ++i) {
				Mark(races[i].Flavors, save.Civilizations[i].name, f => f.Civilizations);
			}
		}

		private void ImportDifficulties() {
			BiqData theBiq = biq.Diff is null ? defaultBiq : biq;

			foreach (QueryCiv3.Biq.DIFF diff in theBiq.Diff) {
				Difficulty d = new();
				d.id = ids.CreateID("Difficulty");
				d.Name = diff.Name;
				d.NumberOfCitizensBornContent = diff.NumberOfCitizensBornContent;
				d.MaxAiGovernmentTransitionTime = diff.MaxGovernmentTransitionTime;
				d.NumberOfAIDefensiveStartingUnits = diff.NumberOfAIDefensiveStartingUnits;
				d.NumberOfAIOffensiveStartingUnits = diff.NumberOfAIOffensiveStartingUnits;
				d.ExtraStartUnit1 = diff.ExtraStartUnit1;
				d.ExtraStartUnit2 = diff.ExtraStartUnit2;
				d.AdditionalFreeUnitSupport = diff.AdditionalFreeSupport;
				d.UnitSupportBonusForEachSettlement = diff.UnitSupportBonusForEachSettlement;
				d.AttackBonusAgainstBarbarians = diff.AttackBonusAgainstBarbarians;
				d.AiCostFactor = diff.CostFactor;
				d.PercentageOfOptimalCities = diff.PercentageOfOptimalCities;
				d.AIToAITradeRate = diff.AIToAITradeRate;
				d.CorruptionPercentage = diff.CorruptionPercentage;
				d.MilitaryLaw = diff.MilitaryLaw;

				save.Difficulties.Add(d);
			}
		}

		private void ImportRules() {
			BiqData theBiq = biq.Rule is null ? defaultBiq : biq;
			RULE rule = theBiq.Rule[0];

			save.Rules.MaximumResearchTime = rule.MaximumResearchTime;
			save.Rules.AcceleratedProduction = GameBiq?.Game?[0].AcceleratedProduction ?? false;
			// Only a scenario with its own rules turns culture flips off;
			// with Civ3's default rules they are on (see Rules.AllowCultureFlips).
			QueryCiv3.Biq.GAME[] games = GameBiq?.Game;
			save.Rules.AllowCultureFlips = games == null || games.Length == 0 || games[0].DefaultGameRules != 0
				|| games[0].AllowCulturalConversions;
			// Scientific leaders likewise (see Rules.ScientificLeaders).
			save.Rules.ScientificLeaders = games == null || games.Length == 0 || games[0].DefaultGameRules != 0
				|| games[0].AllowScientificLeaders;
			save.Rules.UpgradeCostPerShield = rule.UpgradeCost;
			save.Rules.MinimumPopulationForWeLoveTheKing = rule.MinimumPopulationForWeLoveTheKing;
			save.Rules.GoldenAgeDuration = rule.GoldenAgeDuration;
			save.Rules.MinimumResearchTime = rule.MinimumResearchTime;
			save.Rules.MaximumLevel1CitySize = rule.MaximumLevel1CitySize;
			save.Rules.MaximumLevel2CitySize = rule.MaximumLevel2CitySize;
			save.Rules.ShieldValueInGold = rule.ShieldValueInGold;
			save.Rules.ForestValueInShields = rule.ForestValueInShields;
			save.Rules.CitizenValueInShields = rule.CitizenValueInShields;
			save.Rules.TurnPenaltyForEachHurrySacrifice = rule.TurnPenaltyForEachHurrySacrifice;
			if (rule.DefaultDifficultyLevel >= 0 && rule.DefaultDifficultyLevel < save.Difficulties.Count) {
				save.GameDifficulty = save.Difficulties[rule.DefaultDifficultyLevel];
			}
			save.Rules.StartingTreasury = rule.StartingTreasury;
			if (games != null && games.Length > 0) {
				save.Rules.VolcanoEruptionPeriod = games[0].EruptionPeriod;
				QueryCiv3.Biq.GAME game = games[0];
				save.Rules.PermitPlagues = game.PermitPlagues != 0;
				if (!string.IsNullOrWhiteSpace(game.PlagueName)) {
					save.Rules.PlagueName = game.PlagueName;
				}
				save.Rules.PlagueEarliestStart = game.PlagueEarliestStart;
				save.Rules.PlagueVariance = game.PlagueVartiation;
				save.Rules.PlagueDuration = game.PlagueDuration;
				save.Rules.PlagueStrength = game.PlagueStrength;
				save.Rules.PlagueGracePeriod = game.PlagueGracePeriod;
				save.Rules.PlagueMaxOccurrences = game.PlagueMaxOccurance;
			}
			ERAS[] eras = biq.Eras ?? defaultBiq.Eras;
			if (eras?.Length > 0) {
				save.Rules.FirstEraCivilopediaName = eras[0].CivilopediaEntry;
				foreach (ERAS era in eras) {
					int used = Math.Clamp(era.NumberOfUsedResearcherNames, 0, 5);
					save.Rules.EraResearcherNames[era.CivilopediaEntry] =
						new[] { era.Researcher1, era.Researcher2, era.Researcher3, era.Researcher4, era.Researcher5 }
							.Take(used).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
				}
			}
			save.Rules.FoodConsumptionPerCitizen = rule.FoodConsumptionPerCitizen;
			save.Rules.CityLevelNames = [rule.CitySizeLevel1Name, rule.CitySizeLevel2Name, rule.CitySizeLevel3Name];
			if (rule.DefaultDifficultyLevel >= 0 && rule.DefaultDifficultyLevel < save.Difficulties.Count) {
				save.Rules.DefaultDifficulty = save.Difficulties[rule.DefaultDifficultyLevel].Name;
			}
			GOOD[] goods = biq.Good ?? defaultBiq?.Good;
			if (goods != null && rule.DefaultMoneyResource >= 0 && rule.DefaultMoneyResource < goods.Length) {
				save.Rules.DefaultMoneyResource = goods[rule.DefaultMoneyResource].Name;
			}
			// The missions by their place in the BIQ (Civ3's order); their
			// names stay the game's own (as the era names do), so an imported
			// game reads as before.
			ESPN[] missions = biq.Espn ?? defaultBiq?.Espn;
			if (missions?.Length > 0) {
				for (int i = 0; i < missions.Length && i < save.Rules.EspionageMissions.Count; ++i) {
					EspionageMissionRule m = save.Rules.EspionageMissions[i];
					m.CivilopediaEntry = missions[i].CivilopediaEntry;
					m.Description = missions[i].Description;
					m.Diplomat = missions[i].Diplomat;
					m.Spy = missions[i].Spy;
					m.BaseCost = missions[i].BaseCost;
				}
			}
			save.Rules.MovementAlongRoads = rule.MovementAlongRoads;
			save.Rules.FortressDefensiveBonus = rule.FortressDefensiveBonus;
			save.Rules.ChanceToInterceptAirMissions = rule.ChanceToInterceptEnemyAirMissions;
			save.Rules.ChanceToInterceptStealthMissions = rule.ChanceToInterceptEnemyStealthMissions;
			save.Rules.BorderExpansionMultiplier = rule.BorderExpansionMultiplier;
			save.Rules.BorderFactor = rule.BorderFactor;
			save.Rules.FutureTechCost = rule.FutureTechCost;
			save.Rules.ChanceOfRioting = rule.ChanceOfRioting;
			save.Rules.CitizensAffectedByEachHappyFace = rule.CitizensAffectedByEachHappyFace;
			save.Rules.BuildingDefensiveBonus = rule.BuildingDefensiveBonus;
			save.Rules.CitizenDefensiveBonus = rule.CitizenDefensiveBonus;
			save.Rules.TurnPenaltyForEachDraftedCitizen = rule.TurnPenaltyForEachDraftedCitizen;
			if (rule.Slave >= 0 && rule.Slave < theBiq.Prto.Length) {
				save.Rules.SlaveUnitType = theBiq.Prto[rule.Slave].Name;
			}
			if (rule.FlagUnitType >= 0 && rule.FlagUnitType < theBiq.Prto.Length) {
				save.Rules.FlagUnitType = theBiq.Prto[rule.FlagUnitType].Name;
			}
			if (rule.StartUnitType1 >= 0) {
				save.Rules.StartUnitType1 = theBiq.Prto[rule.StartUnitType1].Name;
			}
			if (rule.StartUnitType2 >= 0) {
				save.Rules.StartUnitType2 = theBiq.Prto[rule.StartUnitType2].Name;
			}
			if (rule.Scout >= 0) {
				save.Rules.ScoutUnitType = theBiq.Prto[rule.Scout].Name;
			}
			save.Rules.MaxRankOfWorkableTiles = 2;
			save.Rules.MaxRankOfBarbarianCampTiles = 2;
			save.Rules.DefaultDealDuration = 20;
			save.Rules.ShieldCostPerGold = rule.ShieldsCostPerGold;
			// Not in the BIQ. Civ3 gives a city a quarter of the shields of a
			// unit disbanded in it, rounded down (https://civfanatics.com/civ3/faq/).
			save.Rules.ShieldRateForDisbanding = 0.25f;
			save.Rules.AllowLesserUnitProduction = false;
			save.Rules.RadarTileVisibility = 2;
			save.Rules.CitiesNeededToSupportAnArmy = rule.CitiesNeededToSupportAnArmy;
			if (theBiq.RuleSpaceship?.Length > 0 && theBiq.RuleSpaceship[0] != null) {
				save.Rules.SpaceshipPartsRequired = theBiq.RuleSpaceship[0].ToList();
			}
			if (rule.BuildArmyUnit >= 0) {
				save.Rules.BuildArmyUnit = theBiq.Prto[rule.BuildArmyUnit].Name;
			}
			if (rule.BattleCreatedUnit >= 0 && rule.BattleCreatedUnit < theBiq.Prto.Length) {
				save.Rules.BattleCreatedUnit = theBiq.Prto[rule.BattleCreatedUnit].Name;
			}
			if (theBiq.RuleCult?.Length > 0 && theBiq.RuleCult[0]?.Length > 0) {
				save.Rules.CultureLevelNames = theBiq.RuleCult[0].Select(c => c.Name).ToList();
			}
			if (theBiq.Cult?.Length > 0) {
				save.Rules.CultureOpinions = theBiq.Cult
					.Where(c => c.CultureRatioDenominator > 0)
					.Select(c => new CultureOpinion {
						Name = c.Name,
						Numerator = c.CultureRatioNumerator,
						Denominator = c.CultureRatioDenominator,
						PropagandaChance = c.ChanceOfSuccessfulPropaganda,
						InitialResistanceChance = c.InitialResistanceChance,
						ContinuedResistanceChance = c.ContinuedResistanceChance,
					})
					.OrderByDescending(o => (double)o.Numerator / o.Denominator)
					.ToList();
			}
		}

		private static void SetWorldWrap(SavData civ3Save, SaveGame save) {
			if (civ3Save is not null && civ3Save.Wrld.Height > 0 && civ3Save.Wrld.Width > 0) {
				save.Map.wrapHorizontally = civ3Save.Wrld.XWrapping;
				save.Map.wrapVertically = civ3Save.Wrld.YWrapping;
				save.Map.polarIceCaps = civ3Save.Wrld.PolarIceCaps;
			}
		}

		private static void SetBarbarianActivity(SavData civ3Save, SaveGame save) {
			if (civ3Save?.Wrld is not null) {
				save.BarbarianInfo.barbarianActivity = (BarbarianActivity)civ3Save.Wrld.ActualBarbarians;
			}
		}

		// A scenario's own world sizes (its WSIZ section), as MDJ's player
		// counts: 1v1 to 8 Player. The middle one is chosen at first, as
		// Civ3's world size screen chooses Standard. Without its own, the
		// sizes are left for the rules to give (Civ3's are kept in the
		// bundled ruleset, see RegenerateRuleset).
		private void ImportWorldSizes() {
			if (biq?.Wsiz is not WSIZ[] sizes || sizes.Length == 0) {
				return;
			}
			save.WorldSizes = sizes.Select((w, i) => new WorldSize {
				name = w.Name,
				width = w.Width,
				height = w.Height,
				optimalNumberOfCities = w.OptimalNumberOfCities,
				techRate = w.TechRate,
				distanceBetweenCivs = w.DistanceBetweenCivs,
				numberOfCivs = w.NumberOfCivs,
				isDefault = i == sizes.Length / 2,
			}).ToList();
		}

		private static void SetWorldWrap(BiqData biq, SaveGame save) {
			if (biq is not null && biq.Wmap is not null && biq.Wmap.Length > 0) {
				save.Map.wrapHorizontally = biq.Wmap[0].XWrapping;
				save.Map.wrapVertically = biq.Wmap[0].YWrapping;
				save.Map.polarIceCaps = biq.Wmap[0].PolarIceCaps;
			}
		}

		private static void SetMapDimensions(SavData civ3Save, SaveGame save) {
			if (civ3Save is not null && civ3Save.Wrld.Height > 0 && civ3Save.Wrld.Width > 0) {
				save.Map.tilesTall = civ3Save.Wrld.Height;
				save.Map.tilesWide = civ3Save.Wrld.Width;
				save.Map.techRate = civ3Save.Bic.Wsiz[civ3Save.Wrld.WsizID].TechRate;
				save.Map.optimalNumberOfCities = civ3Save.Bic.Wsiz[civ3Save.Wrld.WsizID].OptimalNumberOfCities;
			}
		}

		private void SetMapDimensions(BiqData biq, SaveGame save) {
			if (biq is not null && biq.Wmap is not null && biq.Wmap.Length > 0) {
				save.Map.tilesTall = biq.Wmap[0].Height;
				save.Map.tilesWide = biq.Wmap[0].Width;
				// The world sizes may come from the default BIQ.
				WSIZ[] sizes = biq.Wsiz ?? defaultBiq?.Wsiz;
				int size = biq.Wchr?.Length > 0 ? biq.Wchr[0].WorldSize : -1;
				if (sizes != null && size >= 0 && size < sizes.Length) {
					save.Map.techRate = sizes[size].TechRate;
					save.Map.optimalNumberOfCities = sizes[size].OptimalNumberOfCities;
				} else {
					log.Warning("The map has no world size, so its tech rate and optimal number of cities are unknown");
				}
			}
		}

		// The position of citizens is encoded in a single byte as positions in
		// a spiral around the city, starting with the NE tile and going
		// clockwise. After the inner spiral, it continues with the top of the
		// NE outer spiral. Here's a crude ASCII diagram of this.
		//
		//                      <   8  >
		//                  <   7  ><   1  >
		//              <   6  >< City ><   2  >
		//                  <   5  ><   3  >
		//                      <   4  >
		//
		//                  <  20  ><  9  >
		//              <  19  >        <  10  >
		//          <  18  >                <  11 >
		//                      < City >
		//          <  17  >                <  12  >
		//              <  16  >        <  13  >
		//                  <  15  ><  14  >
		internal static TileLocation GetTileFromSpiral(TileLocation start, int spiral) {
			return spiral switch {
				// Inner circle.
				1 => Tile.NeighborCoordinate(start, TileDirection.NORTHEAST),
				2 => Tile.NeighborCoordinate(start, TileDirection.EAST),
				3 => Tile.NeighborCoordinate(start, TileDirection.SOUTHEAST),
				4 => Tile.NeighborCoordinate(start, TileDirection.SOUTH),
				5 => Tile.NeighborCoordinate(start, TileDirection.SOUTHWEST),
				6 => Tile.NeighborCoordinate(start, TileDirection.WEST),
				7 => Tile.NeighborCoordinate(start, TileDirection.NORTHWEST),
				8 => Tile.NeighborCoordinate(start, TileDirection.NORTH),

				// Outer circle, to the NE
				9 => Tile.NeighborCoordinate(Tile.NeighborCoordinate(start, TileDirection.NORTHEAST), TileDirection.NORTH),
				10 => Tile.NeighborCoordinate(Tile.NeighborCoordinate(start, TileDirection.NORTHEAST), TileDirection.NORTHEAST),
				11 => Tile.NeighborCoordinate(Tile.NeighborCoordinate(start, TileDirection.NORTHEAST), TileDirection.EAST),

				// Outer circle, to the SE
				12 => Tile.NeighborCoordinate(Tile.NeighborCoordinate(start, TileDirection.SOUTHEAST), TileDirection.EAST),
				13 => Tile.NeighborCoordinate(Tile.NeighborCoordinate(start, TileDirection.SOUTHEAST), TileDirection.SOUTHEAST),
				14 => Tile.NeighborCoordinate(Tile.NeighborCoordinate(start, TileDirection.SOUTHEAST), TileDirection.SOUTH),

				// Outer circle, to the SW
				15 => Tile.NeighborCoordinate(Tile.NeighborCoordinate(start, TileDirection.SOUTHWEST), TileDirection.SOUTH),
				16 => Tile.NeighborCoordinate(Tile.NeighborCoordinate(start, TileDirection.SOUTHWEST), TileDirection.SOUTHWEST),
				17 => Tile.NeighborCoordinate(Tile.NeighborCoordinate(start, TileDirection.SOUTHWEST), TileDirection.WEST),

				// Outer circle, to the NW
				18 => Tile.NeighborCoordinate(Tile.NeighborCoordinate(start, TileDirection.NORTHWEST), TileDirection.WEST),
				19 => Tile.NeighborCoordinate(Tile.NeighborCoordinate(start, TileDirection.NORTHWEST), TileDirection.NORTHWEST),
				20 => Tile.NeighborCoordinate(Tile.NeighborCoordinate(start, TileDirection.NORTHWEST), TileDirection.NORTH),

				_ => throw new ArgumentOutOfRangeException("Invalid spiral value" + spiral),
			};
		}

		// A handy utility for trying to reverse engineer various structs when
		// comparing SAV files.
		private void DumpObject(string label, object o) {
			Console.WriteLine(label);
			foreach (System.Reflection.PropertyInfo propertyInfo in o.GetType().GetProperties(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)) {
				Console.WriteLine($"\t{propertyInfo.Name}={propertyInfo.GetValue(o)}");
			}
			foreach (System.Reflection.MethodInfo method in o.GetType().GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)) {
				// Print the common case of methods returning lists with no argments
				if (typeof(List<int>).IsAssignableFrom(method.ReturnType) && method.ReturnType != typeof(string)) {
					List<int> result = (List<int>)method.Invoke(o, null);
					Console.WriteLine($"\t{method.Name}=" + string.Join(", ", result));
				}
			}
			foreach (System.Reflection.FieldInfo fieldInfo in o.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)) {
				Console.WriteLine($"\t{fieldInfo.Name}={fieldInfo.GetValue(o)}");
			}
		}
	}
}
