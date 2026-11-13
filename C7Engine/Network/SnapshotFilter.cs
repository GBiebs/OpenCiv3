using System;
using System.Collections.Generic;
using System.Linq;
using C7GameData;
using C7GameData.Save;

namespace C7Engine.Network;

// What a LAN host sends a guest's machine when it hides what the guest's
// players can't see (LanHost.HideUnseen), and a spectator watching as some
// of the civilizations: the snapshot, less what none of the players at that
// machine may know. They share a screen, so the machine sees what any of
// them knows. The host, and a spectator watching the whole game, have it
// all.
//
// The guest runs its UI, and parts of the engine, over what it's sent, and
// works much out again from it: what each player sees from their units,
// citizens' moods, trade networks and so on. What it can't work out from
// what it's sent, the host works out and sends with it (see HostFacts):
// whose territory each tile it knows is in, since the cities whose borders
// reach those tiles may not be sent; each player's totals (cities,
// population, culture, domination territory and turn score) for the victory
// screens and advisors; the tiles in the world counting towards domination;
// where each great wonder is, which everyone hears; and who has the United
// Nations. The scoreboard's scores are the game's history, which the host
// keeps and sends whole.
//
// - Other players' units on tiles none of the players can see now are left
//   out, as are invisible units (Submarines) none of them detects, and
//   those they can see lose their paths.
// - Tiles none of the players has ever seen lose their resource, barbarian
//   camp, goody hut and improvements. Those next to a tile they know keep
//   their terrain and rivers, since the map draws the edges of the known
//   tiles from them. The rest are sent as placeholder terrain, grassland for
//   land and coast for water, without rivers or anything else: that keeps
//   only whether they're land or water, and their continent, so that the
//   guest's units can still be sent into the unknown. The map's starting
//   locations are left out but for those they know.
// - Other civilizations' cities on tiles none of the players has ever seen
//   are left out. Those on tiles they know keep their name, size, owner,
//   buildings, culture and whether they're the capital, celebrating or in
//   disorder, which the map and the city's borders need. Unless one of the
//   players has an embassy with their civilization, which shows their
//   cities' insides, they lose their citizens (the guest places as many on
//   its own), stored food and shields, production queue and what they're
//   building, unless it's a great wonder (which the Wonders view shows) or a
//   city improvement like wealth that counts towards their borders.
// - Other players' research is left out unless one of the players has an
//   embassy with them, and their gold, techs and spaceship unless they've
//   met, which the diplomacy screens and the space race need. What they
//   know of the map is cut to their own territory that the players know.
// - What other players think of each other is left out unless the players
//   have met both, as are their reports on each other's capitals, and the
//   votes for the United Nations the other humans have cast.
public static class SnapshotFilter {
	// What the host works out from the game for every view of one snapshot,
	// on the main thread, for Filter to use on another thread.
	public sealed class Facts {
		// The ID of the player whose territory each tile is in, by its
		// place in the map's tiles, or null.
		internal readonly string[] owners;
		// The places of the tiles each player knows within their own borders.
		internal readonly Dictionary<ID, int[]> ownTerritory = new();
		internal readonly Dictionary<string, PlayerFacts> players = new();
		internal readonly int dominationTiles;
		internal readonly List<WonderFacts> wonders = new();
		internal readonly string unitedNationsOwner;
		// Just beyond the largest coordinates of the map's tiles.
		internal readonly int mapWidth, mapHeight;

		internal Facts(GameData gameData) {
			List<Tile> tiles = gameData.map.tiles;
			owners = new string[tiles.Count];
			// Each player's ID, written once, and the places of the tiles
			// they know within their borders, found in one pass over the
			// map, which gives them in order.
			Dictionary<Player, (string id, List<int> places)> territories = new();
			foreach (Player player in gameData.players) {
				territories[player] = (player.id.ToString(), new List<int>());
			}
			for (int i = 0; i < tiles.Count; ++i) {
				Tile tile = tiles[i];
				mapWidth = Math.Max(mapWidth, tile.XCoordinate + 1);
				mapHeight = Math.Max(mapHeight, tile.YCoordinate + 1);
				Player owner = tile.OwningPlayer();
				if (owner != null) {
					if (territories.TryGetValue(owner, out (string id, List<int> places) territory)) {
						owners[i] = territory.id;
						if (owner.tileKnowledge.knownTiles.Contains(tile)) {
							territory.places.Add(i);
						}
					} else {
						owners[i] = owner.id.ToString();
					}
				}
				if (tile.IsCountedForDomination()) {
					++dominationTiles;
				}
			}
			List<Player> civs = gameData.players.Where(p => !p.IsIndependent).ToList();
			Dictionary<Player, DemographicsReport.Territory> demographicsTerritory = DemographicsReport.TerritoriesOf(gameData, civs);
			foreach (Player player in gameData.players) {
				ownTerritory[player.id] = territories[player].places.ToArray();
				if (player.IsIndependent) {
					continue;
				}
				players[territories[player].id] = new PlayerFacts {
					cities = player.cities.Count,
					population = player.cities.Sum(c => c.residents.Count),
					culture = player.cities.Sum(c => c.GetCulture()),
					topCityCulture = CulturalVictory.TopCityCulture(player),
					dominationTiles = player.tileKnowledge.DominationTiles().Count,
					turnScore = ScoreVictory.ComputeTurnScore(player, gameData),
					demographics = DemographicsReport.Values(gameData, player, demographicsTerritory[player]),
				};
			}
			foreach (City city in gameData.cities) {
				foreach (CityBuilding built in city.constructed_buildings) {
					if (built.building.IsGreatWonder()) {
						wonders.Add(new WonderFacts(built.building.name, city.owner?.id.ToString(), city.name));
					}
				}
			}
			unitedNationsOwner = UnitedNations.Owner(gameData)?.id.ToString();
		}
	}

	// What the players at one machine know, worked out on the main thread
	// from the game itself, for Filter to use on another thread.
	public sealed class View {
		internal readonly HashSet<ID> players;
		internal readonly TileSet known;
		// The tiles they don't know next to those they do.
		internal readonly TileSet edge;
		internal readonly TileSet visible;
		// The players they've met, and those they have an embassy with.
		internal readonly HashSet<ID> met = new();
		internal readonly HashSet<ID> embassies = new();
		// Other players' invisible units on tiles they see, which none of
		// them detects (see MapUnit.IsSeenBy).
		internal readonly HashSet<ID> unseenUnits = new();
		internal readonly Facts facts;

		internal View(HashSet<ID> players, Facts facts) {
			this.players = players;
			this.facts = facts;
			known = new TileSet(facts.mapWidth, facts.mapHeight);
			edge = new TileSet(facts.mapWidth, facts.mapHeight);
			visible = new TileSet(facts.mapWidth, facts.mapHeight);
		}
	}

	// A set of places on the map, by their coordinates, as a flag for each,
	// which is far quicker to fill and look up than a hash set for the tens
	// of thousands of tiles players may know. It has room for the
	// coordinates of all the map's tiles, which are all a snapshot has;
	// others are left out, since no tile in a snapshot could be at them.
	internal sealed class TileSet {
		private readonly int width, height;
		private readonly bool[] contains;

		internal TileSet(int width, int height) {
			this.width = width;
			this.height = height;
			contains = new bool[width * height];
		}

		internal void Add(Tile tile) {
			int x = tile.XCoordinate, y = tile.YCoordinate;
			if (IsInside(x, y)) {
				contains[y * width + x] = true;
			}
		}

		internal bool Contains(Tile tile) => Contains(tile.XCoordinate, tile.YCoordinate);

		internal bool Contains(int x, int y) => IsInside(x, y) && contains[y * width + x];

		private bool IsInside(int x, int y) => x >= 0 && y >= 0 && x < width && y < height;
	}

	// What the host works out for every view of the game as it stands.
	public static Facts FactsOf(GameData gameData) => new(gameData);

	// What the players know. Their active tiles are brought up to date
	// first, as the engine would when it next needs them. The facts can be
	// shared by the views of one snapshot; they're worked out if not given.
	public static View ViewOf(GameData gameData, IEnumerable<ID> playerIDs, Facts facts = null) {
		View view = new(playerIDs.ToHashSet(), facts ?? FactsOf(gameData));
		List<TileKnowledge> knowledges = [];
		foreach (ID id in view.players) {
			Player player = gameData.GetPlayer(id);
			if (player == null) {
				continue;
			}
			TileKnowledge knowledge = player.tileKnowledge;
			knowledges.Add(knowledge);
			knowledge.RecomputeActiveTiles();
			foreach (Tile tile in knowledge.knownTiles) {
				view.known.Add(tile);
			}
			foreach (Tile tile in knowledge.VisibleTiles()) {
				if (tile != null && tile != Tile.NONE) {
					view.visible.Add(tile);
				}
			}
			foreach ((ID other, PlayerRelationship relationship) in player.playerRelationships) {
				view.met.Add(other);
				if (relationship.hasEmbassy) {
					view.embassies.Add(other);
				}
			}
		}
		List<Player> viewers = view.players.Select(gameData.GetPlayer).Where(p => p != null).ToList();
		foreach (MapUnit unit in gameData.mapUnits) {
			if (unit.owner != null && !view.players.Contains(unit.owner.id) && Tile.IsTileValid(unit.location)
					&& view.visible.Contains(unit.location) && !viewers.Any(unit.IsSeenBy)) {
				view.unseenUnits.Add(unit.id);
			}
		}
		foreach (TileKnowledge knowledge in knowledges) {
			foreach (Tile tile in knowledge.knownTiles) {
				foreach (Tile neighbor in tile.neighbors.Values) {
					if (neighbor != null && neighbor != Tile.NONE && !view.known.Contains(neighbor)) {
						view.edge.Add(neighbor);
					}
				}
			}
		}
		return view;
	}

	// The snapshot as the view's players may see it. The snapshot isn't
	// changed, and what's returned shares what it doesn't change with it, so
	// several views can be filtered from one snapshot at once, on any
	// thread, as long as nothing changes the snapshot meanwhile.
	public static SaveGame Filter(SaveGame snapshot, View view) {
		SaveGame filtered = snapshot.ShallowCopy();
		bool[] known = snapshot.Map.tiles.Select(t => view.known.Contains(t.X, t.Y)).ToArray();
		filtered.Map = FilterMap(snapshot, view);
		filtered.Units = snapshot.Units
			.Where(u => view.players.Contains(u.owner)
				|| (view.visible.Contains(u.currentLocation.X, u.currentLocation.Y) && !view.unseenUnits.Contains(u.id)))
			.Select(u => view.players.Contains(u.owner) || u.path == null ? u : WithoutPath(u))
			.ToList();
		filtered.Cities = FilterCities(snapshot, view);
		filtered.Players = snapshot.Players.ConvertAll(p => FilterPlayer(p, view, known, snapshot.GameOver));
		filtered.UnitedNations = FilterVotes(snapshot.UnitedNations, view);
		filtered.HostFacts = FactsFor(view, known);
		return filtered;
	}

	private static HostFacts FactsFor(View view, bool[] known) {
		Facts facts = view.facts;
		HostFacts result = new() {
			players = facts.players,
			dominationTiles = facts.dominationTiles,
			wonders = facts.wonders,
			unitedNationsOwner = facts.unitedNationsOwner,
		};
		Dictionary<string, List<int>> territory = new();
		for (int i = 0; i < known.Length && i < facts.owners.Length; ++i) {
			if (known[i] && facts.owners[i] is string owner) {
				if (!territory.TryGetValue(owner, out List<int> tiles)) {
					territory[owner] = tiles = [];
				}
				tiles.Add(i);
			}
		}
		foreach ((string owner, List<int> tiles) in territory) {
			result.territory[owner] = SavePlayer.EncodeIndices(tiles);
		}
		return result;
	}

	private static SaveMap FilterMap(SaveGame snapshot, View view) {
		SaveMap map = snapshot.Map;
		SaveMap filtered = map.ShallowCopy();
		string land = snapshot.TerrainTypes.Find(t => t.IsGrassland)?.Key;
		string water = snapshot.TerrainTypes.Find(t => t.IsCoast)?.Key;
		HashSet<string> waterKeys = snapshot.TerrainTypes.Where(t => t.IsWater).Select(t => t.Key).ToHashSet();
		filtered.tiles = map.tiles.ConvertAll(tile => {
			if (view.known.Contains(tile.X, tile.Y)) {
				return tile;
			}
			if (!view.edge.Contains(tile.X, tile.Y)) {
				string placeholder = waterKeys.Contains(tile.baseTerrain) ? water : land;
				if (placeholder != null) {
					return new SaveTile {
						id = tile.id,
						X = tile.X,
						Y = tile.Y,
						continent = tile.continent,
						baseTerrain = placeholder,
						overlayTerrain = placeholder,
					};
				}
			}
			if (tile.resource == null && tile.overlays.Count == 0 && !tile.features.Contains("barbarianCamp") && !tile.features.Contains("goodyHut")) {
				return tile;
			}
			SaveTile unseen = tile.ShallowCopy();
			unseen.resource = null;
			unseen.overlays = [];
			unseen.features = tile.features.Where(f => f != "barbarianCamp" && f != "goodyHut").ToList();
			return unseen;
		});
		filtered.startingLocations = map.startingLocations.Where(t => view.known.Contains(t.X, t.Y)).ToList();
		return filtered;
	}

	private static SaveUnit WithoutPath(SaveUnit unit) {
		SaveUnit copy = unit.ShallowCopy();
		copy.path = null;
		return copy;
	}

	private static List<SaveCity> FilterCities(SaveGame snapshot, View view) {
		HashSet<string> greatWonders = snapshot.Buildings.Where(b => b.greatWonderProperties != null).Select(b => b.name).ToHashSet();
		// What a city whose production is hidden is said to build: any
		// unit, which only an embassy's city screen would show, and it
		// shows the real thing.
		string placeholder = snapshot.UnitPrototypes.FirstOrDefault()?.name;
		return snapshot.Cities
			.Where(city => view.players.Contains(city.owner) || view.known.Contains(city.location.X, city.location.Y))
			.Select(city => {
				if (view.players.Contains(city.owner) || view.embassies.Contains(city.owner)) {
					return city;
				}
				SaveCity hidden = city.ShallowCopy();
				hidden.size = city.residents.Count;
				hidden.residents = [];
				hidden.foodStored = 0;
				hidden.shieldsStored = 0;
				hidden.turnsOfUnhappinessDueToPopRushing = 0;
				hidden.turnsOfUnhappinessDueToDrafting = 0;
				hidden.unitsDraftedThisTurn = 0;
				hidden.soldBuildingThisTurn = false;
				hidden.propagandaConfusedCitizens = 0;
				hidden.hurriedThisTurn = false;
				hidden.productionQueue = [];
				bool shown = city.producibleType == ProducibleType.INFLOW
					|| (city.producibleType == ProducibleType.BUILDING && greatWonders.Contains(city.producible));
				if (!shown && placeholder != null) {
					hidden.producible = placeholder;
					hidden.producibleType = ProducibleType.UNIT;
				}
				return hidden;
			})
			.ToList();
	}

	private static SavePlayer FilterPlayer(SavePlayer player, View view, bool[] known, bool gameOver) {
		if (view.players.Contains(player.id)) {
			return player;
		}
		SavePlayer filtered = player.ShallowCopy();

		int[] territory = view.facts.ownTerritory.GetValueOrDefault(player.id) ?? [];
		filtered.knownTileIndices = SavePlayer.EncodeIndices(territory.Where(i => i < known.Length && known[i]));
		filtered.tileKnowledge = [];
		filtered.outdatedTiles = [];
		// The production queues a player saved are their own.
		filtered.savedProductionQueues = [];
		// So are their statistics until the game is over, when the
		// Statistics screen compares every civ (see StatisticsView).
		if (!gameOver) {
			filtered.stats = null;
		}

		if (!view.embassies.Contains(player.id)) {
			filtered.currentlyResearchedTech = null;
			filtered.researchQueue = [];
			filtered.beakers = 0;
			filtered.turnsResearched = 0;
			filtered.freeTechsRemaining = 0;
		}
		filtered.turnsUntilPriorityReevaluation = 0;
		// The AI's plans are its own.
		filtered.ai = null;
		if (!view.met.Contains(player.id)) {
			filtered.gold = 0;
			// Loading gives them their civilization's starting techs.
			filtered.knownTechs = [];
			filtered.spaceshipParts = [];
		}

		Dictionary<string, PlayerRelationship> relationships = new();
		foreach ((string otherID, PlayerRelationship relationship) in player.playerRelationships) {
			ID other = ID.FromString(otherID);
			if (view.players.Contains(other)) {
				relationships[otherID] = relationship;
			} else if (view.met.Contains(player.id) && view.met.Contains(other)) {
				// Their spies' reports are theirs alone.
				PlayerRelationship copy = relationship.ShallowCopy();
				copy.embassyReport = null;
				copy.closedEmbassyReport = null;
				relationships[otherID] = copy;
			}
		}
		filtered.playerRelationships = relationships;
		return filtered;
	}

	private static UnitedNationsState FilterVotes(UnitedNationsState state, View view) {
		if (state?.humanVotes == null || state.humanVotes.Keys.All(voter => view.players.Contains(ID.FromString(voter)))) {
			return state;
		}
		return new UnitedNationsState {
			votingTurn = state.votingTurn,
			offerTurn = state.offerTurn,
			humanVotes = state.humanVotes.Where(v => view.players.Contains(ID.FromString(v.Key)))
				.ToDictionary(v => v.Key, v => v.Value),
			candidateA = state.candidateA,
			candidateB = state.candidateB,
			candidateC = state.candidateC,
			secretaryGeneral = state.secretaryGeneral,
		};
	}
}
