using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace C7GameData.Save {

	public class SavePlayer {
		public ID id;
		public int primaryColorIndex;
		public int secondaryColorIndex;
		public bool human = false;
		// Which AI plays the civ (see Player.aiType). Null for Standard, so
		// it's left out of the save; older saves have none, and load as
		// Standard.
		public AiType? aiType;
		public bool hasPlayedCurrentTurn = false;
		public bool defeated = false;
		public bool isIncludedInGame = true;
		public bool canBePicked = true;
		public bool skipFirstTurn = false;

		// The name of the person playing, for human players in a hotseat game.
		public string name;

		// Hotseat and play by email: the player's password, hashed, and
		// whether they've chosen it (see Player.passwordHash).
		public string passwordHash = "";
		public bool passwordChosen = false;
		// Play by email: the news kept for the player's turn, and the turn
		// the computer players last had their say with them (see
		// PlayByEmail).
		public List<string> newsWhileAway = new();
		public int heardFromAIsOnTurn = -1;
		// The production queues the player saved (see SavedProductionQueue);
		// none in older saves.
		public List<SavedProductionQueue> savedProductionQueues = new();

		public string civilization;

		// The tiles this player knows about, in the order they learned of
		// them. Older saves (and imported Civ3 games) list them here.
		public List<TileLocation> tileKnowledge = new List<TileLocation>();

		// The tiles this player knows about, in the order they learned of
		// them, in a compact form: the (URL-safe) base64 encoding of each tile's index in
		// the map's list of tiles, as the difference from the previous index
		// (zigzag encoded) written as a variable-length integer. Used instead
		// of tileKnowledge when every known tile is on the map.
		public string knownTileIndices;

		// The known tiles whose terrain improvements have changed since this
		// player last saw them, with the improvements (by key) the player
		// remembers. The player remembers every other known tile as it is.
		public List<RememberedTile> outdatedTiles = new();

		public class RememberedTile {
			public TileLocation tile;
			public List<string> improvements = new();
		}

		// A map from player id to the relationship this player has with the other player.
		public Dictionary<string, PlayerRelationship> playerRelationships = new();

		// The list of techs known by this player.
		public HashSet<ID> knownTechs = new();

		// The tech the player is currently researching.
		public ID currentlyResearchedTech;

		// The tech queue the player is currently researching.
		public List<ID> researchQueue = new();

		// The civilopedia name of the era this player is in.
		//
		// The civilopedia name is what is used for art lookups, not the actual
		// name.
		public string eraCivilopediaName;

		public int turnsUntilPriorityReevaluation = 0;

		// The values of the science/happiness/tax sliders (tax is implicit)
		// A value of 1 => 10%, a value of 10 => 100%.
		//
		// INVARIANT: LuxuryRate + ScienceRate + TaxRate = 10
		public int luxuryRate = 0;
		public int scienceRate = 5;
		public int taxRate = 5;

		// The amount of gold this player has.
		public int gold = 0;

		public int warWeariness;
		public bool hadGoldenAge;
		public int goldenAgeTurnsRemaining;
		// See Player.palace. Left out while the palace has earned nothing,
		// and absent from older saves.
		public PalaceUpgrades palace;
		// See Player.mobilized. False in older saves.
		public bool mobilized;
		public bool mayEndMobilization;
		public int scienceAgeTurnsRemaining;
		public int citiesFounded;
		public int cityStatesCaptured;
		// Saves from before this was saved have it false, and loading works
		// it out again (see SaveGame.ConvertCities).
		public bool neverHadCityOrSettler;
		// Whether the AI has had the difficulty's extra start units. Null in
		// saves from before this was saved, and loading works it out (see
		// SaveGame.ConvertCities).
		public bool? receivedStartUnits;

		// For Regicide and Elimination: the King units the civ has lost,
		// and the cities enemies have taken from it.
		public int kingsLost;
		// Under PersistentResources, the keys of the resources the civ keeps.
		public List<string> obtainedResources;
		public int citiesLost;
		// The civ's victory points.
		public int victoryPoints;
		// For the Rising Tide score: enemy units defeated, cities taken
		// and turns survived.
		public int unitsKilled;
		public int citiesCaptured;
		public int turnsSurvived;

		// The number of "beakers" (gold) spent on the currently researched
		// tech.
		public int beakers = 0;

		// The number of turns the player has been researching the current tech.
		public int turnsResearched = 0;

		// Free techs (from Philosophy or Theory of Evolution) not yet used,
		// kept until the player chooses something to research.
		public int freeTechsRemaining = 0;
		// How many of them are Philosophy's bonus techs (see
		// Player.bonusFreeTechsRemaining); 0 in older saves.
		public int bonusFreeTechsRemaining = 0;

		// Future techs learned, once every advance was known; 0 in older saves.
		public int futureTechs;

		// The statistics screen's records (see PlayerStats); empty in older saves.
		public PlayerStats stats;

		// If the government is anarchy (or a govt with the transition bool set
		// to true), the turn number at which switching governments is allowed.
		public int inAnarchyUntilTurn = 0;

		// The current government of the player.
		public ID governmentId;

		public string alliance;

		// Whether one of this player's armies has won a battle.
		public bool hasVictoriousArmy = false;

		// How many of each spaceship part (by index) the player has built.
		public List<int> spaceshipParts = new();

		// The turn of airliftOrigins, the airports and airfields the player
		// has airlifted a unit from that turn. Null when there are none.
		public int airliftTurn = -1;
		public List<TileLocation> airliftOrigins;

		// What the AI remembers from turn to turn (see SaveAiState); null
		// in older saves. Loaded once the units and cities are (see
		// SaveGame.ConvertAiState).
		public SaveAiState ai;

		// Used when importing from .biq, to make it easier to distinguish barbarians from other players.
		// It's not meant to be saved in the json.
		[JsonIgnore]
		public bool isBarbarian { get; init; }
		// The same for the city-states (see Civilization.isCityStates).
		[JsonIgnore]
		public bool isCityStates { get; init; }
		[JsonIgnore]
		public bool IsIndependent => isBarbarian || isCityStates;

		public Player ToPlayer(GameMap map, List<Civilization> civilizations, List<Government> governments, List<Tech> techs, Rules rules, HashSet<Alliance> alliances,
				List<TerrainImprovement> terrainImprovements) {
			Player player = new Player{
				id = id,
				isHuman = human,
				aiType = aiType ?? AiType.Standard,
				isIncludedInGame = isIncludedInGame,
				canBePicked = canBePicked,
				alliance = alliance is not null ? alliances.First(a => a.name == alliance) : null,
				hasPlayedThisTurn = hasPlayedCurrentTurn,
				skipFirstTurn = skipFirstTurn,
				name = name,
				passwordHash = passwordHash ?? "",
				passwordChosen = passwordChosen,
				newsWhileAway = new(newsWhileAway ?? new()),
				heardFromAIsOnTurn = heardFromAIsOnTurn,
				// Copied, so that the game doesn't change the save.
				savedProductionQueues = (savedProductionQueues ?? []).ConvertAll(q => q.Copy()),
				defeated = defeated,
				primaryColorIndex = primaryColorIndex,
				secondaryColorIndex = secondaryColorIndex,
				civilization = civilization is not null ? civilizations.Find(civ => civ.name == civilization) : null,
				// Copied, so that the game doesn't change the save (or other
				// games made from it) as the player learns techs.
				knownTechs = knownTechs == null ? new HashSet<ID>() : new HashSet<ID>(knownTechs),
				eraCivilopediaName = eraCivilopediaName,
				luxuryRate = luxuryRate,
				scienceRate = scienceRate,
				taxRate = taxRate,
				gold = gold,
				warWeariness = warWeariness,
				hadGoldenAge = hadGoldenAge,
				goldenAgeTurnsRemaining = goldenAgeTurnsRemaining,
				// Copied, so that the game doesn't change the save.
				palace = palace?.Copy() ?? new(),
				mobilized = mobilized,
				mayEndMobilization = mayEndMobilization,
				scienceAgeTurnsRemaining = scienceAgeTurnsRemaining,
				citiesFounded = citiesFounded,
				cityStatesCaptured = cityStatesCaptured,
				freeTechsRemaining = freeTechsRemaining,
				bonusFreeTechsRemaining = bonusFreeTechsRemaining,
				futureTechs = futureTechs,
				stats = stats ?? new(),
				neverHadCityOrSettler = neverHadCityOrSettler,
				receivedStartUnits = receivedStartUnits ?? false,
				kingsLost = kingsLost,
				obtainedResources = obtainedResources == null ? new() : new HashSet<string>(obtainedResources),
				citiesLost = citiesLost,
				victoryPoints = victoryPoints,
				unitsKilled = unitsKilled,
				citiesCaptured = citiesCaptured,
				turnsSurvived = turnsSurvived,
				// The AI's strategic priorities aren't saved; PlayerAI works
				// them out again quietly on the AI's first turn after a load.
				turnsUntilPriorityReevaluation = turnsUntilPriorityReevaluation,
				inAnarchyUntilTurn = inAnarchyUntilTurn,
				government = governments.Find(x => x.id == governmentId),
				rules = rules,
				hasVictoriousArmy = hasVictoriousArmy,
				spaceshipParts = spaceshipParts == null ? new() : new List<int>(spaceshipParts),
			};
			player.airliftTurn = airliftTurn;
			foreach (TileLocation origin in airliftOrigins ?? []) {
				Tile tile = map.tileAt(origin.X, origin.Y);
				if (Tile.IsTileValid(tile)) {
					player.airliftOrigins.Add(tile);
				}
			}
			if (!string.IsNullOrEmpty(knownTileIndices)) {
				foreach (int index in DecodeTileIndices(knownTileIndices)) {
					if (index < 0 || index >= map.tiles.Count) {
						throw new FormatException($"Known tile index {index} is outside the map");
					}
					player.tileKnowledge.AddTileToKnown(map.tiles[index]);
				}
			}
			foreach (TileLocation tile in tileKnowledge) {
				player.tileKnowledge.AddTileToKnown(map.tileAt(tile.X, tile.Y));
			}
			if (outdatedTiles != null && outdatedTiles.Count > 0) {
				Dictionary<string, TerrainImprovement> improvementsByKey = new();
				foreach (TerrainImprovement ti in terrainImprovements) {
					if (ti.key != null) {
						improvementsByKey.TryAdd(ti.key, ti);
					}
				}
				foreach (RememberedTile remembered in outdatedTiles) {
					Tile tile = map.tileAt(remembered.tile.X, remembered.tile.Y);
					if (tile == null || tile == Tile.NONE) {
						continue;
					}
					TerrainImprovement[] improvements = remembered.improvements
						.Select(key => improvementsByKey.GetValueOrDefault(key))
						.Where(ti => ti != null)
						.ToArray();
					player.tileKnowledge.RememberImprovements(tile, improvements);
				}
			}
			foreach (ID techId in player.civilization.startingTechs) {
				if (!player.knownTechs.Contains(techId)) {
					player.knownTechs.Add(techId);
				}
			}
			foreach (KeyValuePair<string, PlayerRelationship> keyValuePair in this.playerRelationships) {
				player.playerRelationships.Add(ID.FromString(keyValuePair.Key), keyValuePair.Value);
			}

			// Because of the custom setter we need to set the researched tech
			// and then set the beakers and turns researched - otherwise they'd
			// be reset by the setter.
			player.SetCurrentlyResearchedTech(currentlyResearchedTech);
			player.beakers = beakers;
			player.turnsResearched = turnsResearched;

			foreach (ID techId in researchQueue) {
				Tech tech = techs.Find(x => x.id == techId);
				player.AddTechItemToResearchQueue(tech);
			}

			if (player.civilization.IsIndependent) {
				player.canBePicked = false;
			}

			return player;
		}

		public SavePlayer() { }

		// A copy sharing everything with this one, for a LAN host to change
		// a little for one guest (see C7Engine.Network.SnapshotFilter).
		internal SavePlayer ShallowCopy() {
			return (SavePlayer)MemberwiseClone();
		}

		public SavePlayer(Player player) : this(player, null) { }

		// With a map, the known tiles are saved in the compact form.
		public SavePlayer(Player player, GameMap map) {
			id = player.id;
			isIncludedInGame = player.isIncludedInGame;
			canBePicked = player.canBePicked;
			alliance = (player.IsIndependent || player.alliance == null) ? null : player.alliance.name;
			primaryColorIndex = player.primaryColorIndex;
			secondaryColorIndex = player.secondaryColorIndex;
			human = player.isHuman;
			aiType = player.aiType == AiType.Standard ? null : player.aiType;
			hasPlayedCurrentTurn = player.hasPlayedThisTurn;
			skipFirstTurn = player.skipFirstTurn;
			name = player.name;
			passwordHash = player.passwordHash;
			passwordChosen = player.passwordChosen;
			newsWhileAway = new(player.newsWhileAway);
			heardFromAIsOnTurn = player.heardFromAIsOnTurn;
			savedProductionQueues = player.savedProductionQueues.ConvertAll(q => q.Copy());
			defeated = player.defeated;
			civilization = player.civilization?.name;
			// TODO: this should be computed by looking at cities defined in the save
			// so that adding cities in the save structure doesn't require updating this value
			string encoded = map == null ? null : EncodeTileIndices(player.tileKnowledge.knownTiles, map);
			if (encoded != null) {
				knownTileIndices = encoded;
			} else {
				tileKnowledge = player.tileKnowledge.AllKnownTiles().ConvertAll(tile => new TileLocation(tile));
			}
			foreach (KeyValuePair<Tile, TerrainImprovement[]> memory in player.tileKnowledge.OutdatedMemories()) {
				outdatedTiles.Add(new RememberedTile {
					tile = new TileLocation(memory.Key),
					improvements = memory.Value.Select(ti => ti.key).ToList(),
				});
			}
			turnsUntilPriorityReevaluation = player.turnsUntilPriorityReevaluation;
			knownTechs = new HashSet<ID>(player.knownTechs);
			currentlyResearchedTech = player.currentlyResearchedTech;
			researchQueue = new List<ID>(player.ResearchQueue.Select(t => t.id));
			eraCivilopediaName = player.eraCivilopediaName;
			luxuryRate = player.luxuryRate;
			scienceRate = player.scienceRate;
			taxRate = player.taxRate;
			gold = player.gold;
			warWeariness = player.warWeariness;
			hadGoldenAge = player.hadGoldenAge;
			goldenAgeTurnsRemaining = player.goldenAgeTurnsRemaining;
			palace = player.palace.IsEmpty ? null : player.palace.Copy();
			mobilized = player.mobilized;
			mayEndMobilization = player.mayEndMobilization;
			scienceAgeTurnsRemaining = player.scienceAgeTurnsRemaining;
			citiesFounded = player.citiesFounded;
			cityStatesCaptured = player.cityStatesCaptured;
			freeTechsRemaining = player.freeTechsRemaining;
			bonusFreeTechsRemaining = player.bonusFreeTechsRemaining;
			futureTechs = player.futureTechs;
			stats = player.stats.IsEmpty ? null : player.stats;
			neverHadCityOrSettler = player.neverHadCityOrSettler;
			receivedStartUnits = player.receivedStartUnits;
			kingsLost = player.kingsLost;
			obtainedResources = player.obtainedResources.Count == 0 ? null : player.obtainedResources.Order().ToList();
			citiesLost = player.citiesLost;
			victoryPoints = player.victoryPoints;
			unitsKilled = player.unitsKilled;
			citiesCaptured = player.citiesCaptured;
			turnsSurvived = player.turnsSurvived;
			beakers = player.beakers;
			turnsResearched = player.turnsResearched;
			inAnarchyUntilTurn = player.inAnarchyUntilTurn;
			governmentId = player.government.id;
			hasVictoriousArmy = player.hasVictoriousArmy;
			spaceshipParts = new List<int>(player.spaceshipParts);
			if (player.airliftOrigins.Count > 0) {
				airliftTurn = player.airliftTurn;
				airliftOrigins = player.airliftOrigins.Select(t => new TileLocation(t)).ToList();
			}
			ai = C7Engine.AI.AiSaveState.Save(player);

			foreach (KeyValuePair<ID, PlayerRelationship> keyValuePair in player.playerRelationships) {
				playerRelationships.Add(keyValuePair.Key.ToString(), keyValuePair.Value);
			}
		}

		// Returns null if some tile isn't on the map, in which case the tiles
		// have to be saved by location.
		internal static string EncodeTileIndices(IEnumerable<Tile> tiles, GameMap map) {
			IndexEncoder encoder = new();
			Dictionary<Tile, int> indexByTile = null;
			foreach (Tile tile in tiles) {
				int index = IndexOf(tile, map, ref indexByTile);
				if (index < 0) {
					return null;
				}
				encoder.Add(index);
			}
			return encoder.Finish();
		}

		// The tiles at these places in the map's tiles, encoded as
		// knownTileIndices; null for none.
		internal static string EncodeIndices(IEnumerable<int> indices) {
			IndexEncoder encoder = new();
			foreach (int index in indices) {
				encoder.Add(index);
			}
			return encoder.Finish();
		}

		// Encodes indices one by one as knownTileIndices. Every player's known
		// tiles are encoded for each snapshot a LAN host sends, so this
		// writes straight to bytes, then to the string.
		private struct IndexEncoder {
			private byte[] bytes;
			private int length;
			private int previous;

			public void Add(int index) {
				bytes ??= new byte[256];
				if (bytes.Length - length < 5) {
					Array.Resize(ref bytes, bytes.Length * 2);
				}
				int delta = index - previous;
				previous = index;
				uint zigzag = (uint)((delta << 1) ^ (delta >> 31));
				while (zigzag >= 0x80) {
					bytes[length++] = (byte)(zigzag | 0x80);
					zigzag >>= 7;
				}
				bytes[length++] = (byte)zigzag;
			}

			public readonly string Finish() {
				if (length == 0) {
					return null;
				}
				return string.Create((length + 2) / 3 * 4, (bytes, length), static (chars, encoded) => {
					Convert.TryToBase64Chars(encoded.bytes.AsSpan(0, encoded.length), chars, out _);
					// Use the URL-safe alphabet, since JSON escapes '+'.
					for (int i = 0; i < chars.Length; ++i) {
						if (chars[i] == '+') {
							chars[i] = '-';
						} else if (chars[i] == '/') {
							chars[i] = '_';
						}
					}
				});
			}
		}

		private static int IndexOf(Tile tile, GameMap map, ref Dictionary<Tile, int> indexByTile) {
			if (tile == null || tile == Tile.NONE) {
				return -1;
			}
			int x = tile.XCoordinate, y = tile.YCoordinate;
			if (x >= 0 && y >= 0 && x < map.numTilesWide && y < map.numTilesTall) {
				int index = map.tileCoordsToIndex(x, y);
				if (index >= 0 && index < map.tiles.Count && map.tiles[index] == tile) {
					return index;
				}
			}
			// The tiles aren't laid out as expected, so look the tile up.
			if (indexByTile == null) {
				indexByTile = new Dictionary<Tile, int>(map.tiles.Count);
				for (int i = 0; i < map.tiles.Count; ++i) {
					indexByTile.TryAdd(map.tiles[i], i);
				}
			}
			return indexByTile.TryGetValue(tile, out int found) ? found : -1;
		}

		internal static List<int> DecodeTileIndices(string encoded) {
			byte[] bytes = Convert.FromBase64String(encoded.Replace('-', '+').Replace('_', '/'));
			List<int> result = new();
			int previous = 0;
			int i = 0;
			while (i < bytes.Length) {
				uint zigzag = 0;
				int shift = 0;
				while (true) {
					if (i >= bytes.Length || shift > 28) {
						throw new FormatException("Malformed known tile indices");
					}
					byte b = bytes[i++];
					// The fifth byte holds the top 4 bits of a 32-bit value,
					// and must be the last.
					if (shift == 28 && (b & 0xf0) != 0) {
						throw new FormatException("Malformed known tile indices");
					}
					zigzag |= (uint)(b & 0x7f) << shift;
					if ((b & 0x80) == 0) {
						break;
					}
					shift += 7;
				}
				int delta = (int)(zigzag >> 1) ^ -(int)(zigzag & 1);
				previous += delta;
				result.Add(previous);
			}
			return result;
		}

		public override string ToString() {
			if (civilization != null)
				return $"{civilization} [{this.id}]";
			return "";
		}
	}
}
