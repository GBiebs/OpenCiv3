using System;
using System.Collections.Generic;
using C7Engine;

namespace C7GameData {
	// A log of tiles whose state may have changed in a way that affects which
	// tiles are "active" for some player: units arriving, leaving, dying or
	// changing hands, tile ownership changing, or terrain changing. Each
	// player's TileKnowledge reads the entries recorded since it last brought
	// its active tiles up to date, and re-examines only those tiles instead of
	// every tile it knows about.
	//
	// The log is shared by every game in the process. Recording a tile that a
	// player doesn't know about, or that belongs to another game, is harmless:
	// re-examining a tile always gives the right answer. Missing a change is
	// not, so every change has to be recorded. A reader that falls further
	// behind than the log holds simply recomputes everything.
	//
	// The log holds on to the tiles it records, and through them to their
	// game, so it is reset whenever a game is created, loaded or replaced
	// (see Reset), letting the previous game be collected.
	internal static class TileChangeJournal {
		internal const int Capacity = 1 << 17;
		private const int Mask = Capacity - 1;

		private static readonly Tile[] entries = new Tile[Capacity];

		// The number of entries ever recorded.
		internal static long head { get; private set; }

		// Bumped to make every player recompute their active tiles from scratch.
		internal static long epoch { get; private set; }

		internal static Tile EntryAt(long position) => entries[position & Mask];

		internal static void Record(Tile tile) {
			if (tile == null || tile == Tile.NONE) {
				return;
			}
			entries[head & Mask] = tile;
			++head;
		}

		// Records a change to a tile's terrain. What a unit can see depends on
		// the terrain up to two tiles away, so the tiles around it are recorded
		// too, each once.
		internal static void RecordTerrainChange(Tile tile) {
			if (tile == null || tile == Tile.NONE) {
				return;
			}
			HashSet<Tile> recorded = new(25) { tile };
			Record(tile);
			foreach (Tile n in tile.neighbors.Values) {
				if (n == Tile.NONE) {
					continue;
				}
				if (recorded.Add(n)) {
					Record(n);
				}
				foreach (Tile nn in n.neighbors.Values) {
					if (nn != Tile.NONE && recorded.Add(nn)) {
						Record(nn);
					}
				}
			}
		}

		// Makes every player recompute their active tiles from scratch the
		// next time they are brought up to date.
		internal static void InvalidateAll() {
			++epoch;
		}

		// Forgets every recorded tile, so that the log no longer keeps a
		// previous game alive, and makes every reader recompute from scratch
		// (the entries they hadn't read yet are gone). Called whenever a game
		// is created, loaded or replaced.
		internal static void Reset() {
			Array.Clear(entries);
			InvalidateAll();
		}
	}

	// Read-only access to the TileChangeJournal for the UI, which uses it to
	// redraw the map only where it may have changed while the engine changes
	// the game without telling it, as while the AI plays.
	public static class TileChanges {
		// The number of changes ever recorded.
		public static long Head => TileChangeJournal.head;

		// Changes whenever the recorded changes can't be relied on, so
		// everything should be looked at again.
		public static long Epoch => TileChangeJournal.epoch;

		// How many of the latest changes are kept.
		public const int Capacity = TileChangeJournal.Capacity;

		// The tile of a change, if it is among the latest Capacity ones;
		// null if it has been forgotten.
		public static Tile At(long position) => TileChangeJournal.EntryAt(position);
	}

	public class TileKnowledge {
		private readonly Player _player;

		public TileKnowledge(Player player) {
			_player = player;
		}

		public HashSet<Tile> knownTiles { get; private set; } = new();
		public HashSet<Tile> borderTiles { get; private set; } = new();

		// Has this player explored all known ocean tiles?
		// TODO: this should be split out for coast/ocean
		public bool fullyExploredOceans = false;

		// The set of tiles this player currently has explorers headed towards.
		public HashSet<Tile> aiExplorationTargets = new();

		// The active tiles are the union of the tiles contributed by each
		// "source": a known tile with one of our units on top (contributing
		// what the unit can see), or a known tile within our borders with no
		// units on it (contributing itself and its neighbors), or a known tile
		// with our outpost, colony or radar tower (contributing what it can
		// see, and what our unit there can see if one is on top).
		//
		// We keep the contribution of each source and a count, per tile, of the
		// sources contributing it, so that RecomputeActiveTiles only has to
		// re-examine the tiles that may have changed since it last ran. The
		// result is always the same as recomputing from scratch.
		private enum SourceKind : byte { None, City, Unit, Outpost }

		private sealed class Source {
			public SourceKind kind;
			public bool radar;
			public Tile[] tiles;
		}

		private readonly Dictionary<Tile, Source> sources = new();
		private readonly Dictionary<Tile, int> activeTileCounts = new();

		// Tiles added to knownTiles since the active tiles were last updated.
		private readonly List<Tile> newlyKnownTiles = new();
		private int knownTileCountAtLastUpdate;
		private long journalPosition;
		private long journalEpoch;
		private bool needsFullRecompute = true;

		// Tiles whose entire two-tile neighborhood is known. Revealing the
		// tiles around such a tile (without radar) can't change anything.
		// This relies on our bookkeeping of knownTiles and borderTiles, so it
		// is switched off if knownTiles is ever changed behind our back.
		private readonly HashSet<Tile> saturatedTiles = new();
		private bool saturationTrusted = true;

		private readonly List<Tile> visibleScratch = new(32);

		// What the player last saw of each known tile's terrain improvements
		// (roads, mines, irrigation and so on), so that changes made where
		// they can't see only show up once they look again. Brought up to
		// date whenever the player sees a tile, and when a tile they were
		// watching goes out of view. The map shows these for the tiles that
		// aren't active.
		private readonly Dictionary<Tile, TerrainImprovement[]> rememberedImprovements = new();
		private static readonly TerrainImprovement[] NoImprovements = [];

		public void AddTilesToKnown(Tile unitLocation, bool recomputeActiveTiles = true) {
			CheckForOutsideChanges();

			bool outpost = HasOwnOutpost(unitLocation);
			if (saturationTrusted && !outpost && !HasRadarUnits(unitLocation) && IsSaturated(unitLocation)) {
				// Everything the tile can see is already known, so the loop
				// below would not change anything.
				if (recomputeActiveTiles) {
					RecomputeActiveTiles();
				}
				return;
			}

			MarkKnown(unitLocation);
			borderTiles.Remove(unitLocation);

			// Crude benchmarking tool for GetTilesVisibleToUnit, which can be
			// a hot function when profiling.
			// {
			// 	Stopwatch stopwatch = new Stopwatch();
			// 	stopwatch.Start();

			// 	for (int i = 0; i < 10000; ++i) {
			// 		foreach (Tile t in GetTilesVisibleToUnit(unitLocation)) {
			// 			knownTiles.Add(t);
			// 		}
			// 	}

			// 	System.Console.WriteLine($"10k runs took: {stopwatch.ElapsedMilliseconds} milliseconds");
			// }

			visibleScratch.Clear();
			CollectTilesVisibleToUnit(unitLocation, visibleScratch);
			if (outpost) {
				CollectTilesSeenFromOutpost(unitLocation, visibleScratch);
			}
			foreach (Tile t in visibleScratch) {
				MarkKnown(t);
				borderTiles.Remove(t);

				foreach (Tile border in t.neighbors.Values) {
					if (border == Tile.NONE) {
						continue;
					}
					if (!knownTiles.Contains(border)) {
						borderTiles.Add(border);
					}
				}
			}

			if (recomputeActiveTiles) {
				RecomputeActiveTiles();
			}
		}

		// Called for every tile the player sees.
		private void MarkKnown(Tile t) {
			if (knownTiles.Add(t)) {
				newlyKnownTiles.Add(t);
			}
			RememberImprovements(t);
		}

		// Remembers the tile's terrain improvements as they are now.
		internal void RememberImprovements(Tile t) {
			Dictionary<TerrainImprovement.Layer, TerrainImprovement> current = t.overlays.terrainImprovementByLayer;
			if (rememberedImprovements.TryGetValue(t, out TerrainImprovement[] remembered) && SameImprovements(remembered, current)) {
				return;
			}
			if (current.Count == 0) {
				rememberedImprovements[t] = NoImprovements;
				return;
			}
			TerrainImprovement[] improvements = new TerrainImprovement[current.Count];
			current.Values.CopyTo(improvements, 0);
			rememberedImprovements[t] = improvements;
		}

		// Remembers the tile as having the given improvements, e.g. when
		// loading what the player last saw from a save.
		internal void RememberImprovements(Tile t, TerrainImprovement[] improvements) {
			rememberedImprovements[t] = improvements.Length == 0 ? NoImprovements : improvements;
		}

		internal static bool SameImprovements(TerrainImprovement[] remembered, Dictionary<TerrainImprovement.Layer, TerrainImprovement> current) {
			if (remembered.Length != current.Count) {
				return false;
			}
			foreach (TerrainImprovement ti in remembered) {
				if (!current.TryGetValue(ti.layer, out TerrainImprovement c) || c != ti) {
					return false;
				}
			}
			return true;
		}

		// The terrain improvements the player last saw on a tile they can't
		// see now. False if they can see it, in which case what's there now
		// is what they see, or if they don't remember the tile.
		public bool TryGetRememberedImprovements(Tile t, out TerrainImprovement[] improvements) {
			if (isActiveTile(t)) {
				improvements = null;
				return false;
			}
			if (rememberedImprovements.TryGetValue(t, out improvements)) {
				return true;
			}
			foreach (TileKnowledge other in alsoShown ?? []) {
				if (other.rememberedImprovements.TryGetValue(t, out improvements)) {
					return true;
				}
			}
			return false;
		}

		// The tiles out of view whose remembered improvements differ from
		// what's there now, with what the player remembers, for saving. What
		// is remembered of the tiles in view doesn't matter, since they're
		// remembered afresh when they go out of view.
		//
		// This only reads: saves can be made from the UI while the engine is
		// changing the game, so it uses the active tiles as they were last
		// brought up to date rather than recomputing them. If that is behind,
		// a tile that has just gone out of view is left out and is remembered
		// as it is now when the save is loaded, which is what recomputing
		// would have remembered too.
		internal IEnumerable<KeyValuePair<Tile, TerrainImprovement[]>> OutdatedMemories() {
			foreach (KeyValuePair<Tile, TerrainImprovement[]> memory in rememberedImprovements) {
				if (!isActiveTile(memory.Key) && !SameImprovements(memory.Value, memory.Key.overlays.terrainImprovementByLayer)) {
					yield return memory;
				}
			}
		}

		public List<Tile> GetTilesVisibleToUnit(Tile unitLocation) {
			// Space for current tile, 8 inner ring tiles, 16 outer ring tiles
			List<Tile> result = new(25);
			CollectTilesVisibleToUnit(unitLocation, result);
			return result;
		}

		// Whether the tile has this player's outpost, colony or radar tower,
		// which see around them.
		private bool HasOwnOutpost(Tile tile) {
			return tile.HasSightedHoldingOf(_player);
		}

		// The tiles an outpost, colony or radar tower sees: all those within
		// its sight range (Tile.HoldingSightRange), whatever the terrain
		// between, as a radar unit sees.
		// UNVERIFIED: the Civilopedia gives only the outpost's range; whether
		// hills and mountains in between block its view isn't stated, so
		// they don't.
		private static void CollectTilesSeenFromOutpost(Tile outpost, List<Tile> result) {
			foreach (Tile t in outpost.GetTilesWithinTileSquare(outpost.HoldingSightRange())) {
				if (Tile.IsTileValid(t)) {
					result.Add(t);
				}
			}
		}

		private static bool HasRadarUnits(Tile tile) {
			List<MapUnit> units = tile.unitsOnTile;
			for (int i = 0; i < units.Count; ++i) {
				if (units[i].unitType.hasRadar) {
					return true;
				}
			}
			return false;
		}

		private static void CollectTilesVisibleToUnit(Tile unitLocation, List<Tile> result) {
			// TODO: Make visibility configurable in game rules
			result.Add(unitLocation);
			int unitHeight = unitLocation.overlayTerrainType.height;

			// Units with radar can see X (2 in OG game, configurable here) tiles away, regardless of terrain.
			// They also don't need to be the "top" unit on the tile, if they exist on the tile, they boost visibility.
			if (HasRadarUnits(unitLocation)) {
				var rules = EngineStorage.gameData.rules;
				foreach (Tile t in unitLocation.GetTilesWithinTileSquare(rules.RadarTileVisibility)) {
					if (Tile.IsTileValid(t)) {
						result.Add(t);
					}
				}
				return;
			}

			foreach (var (innerTileDirection, innerRingNeighbor) in unitLocation.neighbors) {
				if (innerRingNeighbor == Tile.NONE) {
					continue;
				}
				result.Add(innerRingNeighbor);
				int innerHeight = innerRingNeighbor.overlayTerrainType.height;

				foreach (var (outerTileDirection, outerRingNeighbor) in innerRingNeighbor.neighbors) {
					// Say we have the following. We are standing on the XX tile
					// and need to figure out which tiles we can see. We can see
					// the hill directly to our SW, because we're next to it.
					// We can see the hill that is S+SW of us, because it's
					// diagonal to us and we can see tiles of height 2 from 2
					// tiles away. We can't see the hill that is SW+SW of us
					// because we are blocked. To implement this we need to
					// prevent 90 degree "turns", which we handle by ensuring
					// the innerTileDirection+outerTileDirection combo is never
					// a combination of two N/S/E/W dirs (except for when they are the same).
					//
					//                      <  ..  >
					//                  <  ..  ><  XX  >
					//              <  ..  >< Hill ><  gg  >
					//                  < Hill ><  gg  >
					//                      < Hill >

					// N->N, S->S, W->W, E->E, etc
					var hasDirectLineOfSight = ((int)innerTileDirection == (int)outerTileDirection);

					if (!hasDirectLineOfSight && (((int)innerTileDirection) % 2 == 0 && ((int)outerTileDirection) % 2 == 0)) {
						continue;
					}

					if (outerRingNeighbor == Tile.NONE) {
						continue;
					}
					int outerHeight = outerRingNeighbor.overlayTerrainType.height;

					// Water tiles provide us with a boost in visibility,
					// so we can see water & land tiles that are further away from a water tile,
					// but land tiles "block" any tiles that are in their line of sight

					if ((outerRingNeighbor.IsWater() && !innerRingNeighbor.IsLand())) {
						result.Add(outerRingNeighbor);
						continue;
					}

					if ((outerRingNeighbor.IsLand() && innerRingNeighbor.IsWater())) {
						result.Add(outerRingNeighbor);
						continue;
					}

					// Tiles with a height of at least 2 are visible from 2 tiles
					// away as long as the tile in between is 2 less than the
					// outer tile (so you can see mountains over hills, but
					// not hills over forest).
					if (outerHeight >= 2 && innerHeight + 2 <= outerHeight) {
						result.Add(outerRingNeighbor);
						continue;
					}

					// You can also see tiles whose height is 2 lower than where
					// you are standing, as long as the tile in the middle has
					// height at least 2 lower than you.
					if (outerHeight + 1 <= unitHeight && innerHeight + 2 <= unitHeight) {
						result.Add(outerRingNeighbor);
						continue;
					}
				}
			}
		}

		// True if the tile and every tile within two steps of it are known, so
		// that nothing a (non-radar) unit there can see is unknown.
		private bool IsSaturated(Tile t) {
			if (saturatedTiles.Contains(t)) {
				return true;
			}
			if (!knownTiles.Contains(t)) {
				return false;
			}
			foreach (Tile n in t.neighbors.Values) {
				if (n == Tile.NONE) {
					continue;
				}
				if (!knownTiles.Contains(n)) {
					return false;
				}
				foreach (Tile nn in n.neighbors.Values) {
					if (nn != Tile.NONE && !knownTiles.Contains(nn)) {
						return false;
					}
				}
			}
			saturatedTiles.Add(t);
			return true;
		}

		// knownTiles is public, so it can be changed without going through
		// this class (tests do this). If that happened we can no longer trust
		// our incremental bookkeeping.
		private void CheckForOutsideChanges() {
			if (knownTiles.Count != knownTileCountAtLastUpdate + newlyKnownTiles.Count) {
				saturationTrusted = false;
				saturatedTiles.Clear();
				needsFullRecompute = true;
				newlyKnownTiles.Clear();
				knownTileCountAtLastUpdate = knownTiles.Count;
			}
		}

		// neighboring tiles should not be added when loading tile knowledge
		// from a .sav file
		internal bool AddTileToKnown(Tile unitLocation) {
			CheckForOutsideChanges();
			bool added = knownTiles.Add(unitLocation);
			if (added) {
				newlyKnownTiles.Add(unitLocation);
			}
			if (!rememberedImprovements.ContainsKey(unitLocation)) {
				RememberImprovements(unitLocation);
			}
			borderTiles.Remove(unitLocation);

			foreach (Tile border in unitLocation.neighbors.Values) {
				if (border == Tile.NONE) {
					continue;
				}
				if (!knownTiles.Contains(border)) {
					borderTiles.Add(border);
				}
			}

			return added;
		}

		public bool isTileKnown(Tile t) {
			return knownTiles.Contains(t);
		}

		public bool isActiveTile(Tile t) {
			if (t == Tile.NONE || t == null) {
				return false;
			}
			return activeTileCounts.ContainsKey(t) || (peekedTiles.Count > 0 && peekedTiles.Contains(t))
				|| (alsoShown != null && alsoShown.Exists(other => other.isActiveTile(t)));
		}

		// Other players' knowledge shown along with this player's, for a
		// LAN spectator watching the game as several civilizations at once:
		// what any of them knows is known, and what any of them sees is in
		// view. Only a spectator's machine, which shows the host's game
		// rather than running it, does this; the game itself never does.
		private List<TileKnowledge> alsoShown;

		public void ShowAlso(IEnumerable<TileKnowledge> others) {
			alsoShown = [];
			foreach (TileKnowledge other in others) {
				if (other == this || other == null) {
					continue;
				}
				other.RecomputeActiveTiles();
				alsoShown.Add(other);
				knownTiles.UnionWith(other.knownTiles);
				borderTiles.UnionWith(other.borderTiles);
			}
			borderTiles.ExceptWith(knownTiles);
		}

		// Tiles the player is shown as they are now for a while, though none
		// of their units or cities can see them, as when an embassy first
		// shows them the land around a capital. They aren't saved.
		private readonly HashSet<Tile> peekedTiles = new();

		// Shows the known tiles as in view until EndPeek.
		public void Peek(IEnumerable<Tile> tiles) {
			foreach (Tile t in tiles) {
				if (knownTiles.Contains(t)) {
					peekedTiles.Add(t);
				}
			}
		}

		// The peeked tiles go back to being remembered as they are now.
		public void EndPeek() {
			foreach (Tile t in peekedTiles) {
				if (!activeTileCounts.ContainsKey(t)) {
					RememberImprovements(t);
				}
			}
			peekedTiles.Clear();
		}

		/**
		 * Returns a copy of the list of known tiles.
		 * This prevents external modifications.
		 **/
		public List<Tile> AllKnownTiles() {
			return new List<Tile>(knownTiles);
		}

		// Brings the active tiles up to date with the current state of the
		// game. The result is identical to recomputing them from scratch.
		public void RecomputeActiveTiles() {
			CheckForOutsideChanges();

			long head = TileChangeJournal.head;
			if (needsFullRecompute
				|| journalEpoch != TileChangeJournal.epoch
				|| head - journalPosition > TileChangeJournal.Capacity) {
				FullRecompute();
				return;
			}

			// A tile is often in the journal many times (each unit moving
			// through it records it). Looking at it again in the same pass
			// would change nothing, since nothing a tile's contribution
			// depends on changes during the pass, so look at it only once.
			long pass = System.Threading.Interlocked.Increment(ref lastPass);
			foreach (Tile t in newlyKnownTiles) {
				ReevaluateOnce(t, pass);
			}
			for (long i = journalPosition; i < head; ++i) {
				ReevaluateOnce(TileChangeJournal.EntryAt(i), pass);
			}

			newlyKnownTiles.Clear();
			knownTileCountAtLastUpdate = knownTiles.Count;
			journalPosition = head;
		}

		private static long lastPass;

		private void ReevaluateOnce(Tile t, long pass) {
			if (t == null || t.activeTilesPass == pass) {
				return;
			}
			t.activeTilesPass = pass;
			Reevaluate(t);
		}

		// The number of times the active tiles were recomputed from scratch.
		internal int fullRecomputeCount { get; private set; }

		private void FullRecompute() {
			++fullRecomputeCount;
			List<Tile> wereActive = new(activeTileCounts.Keys);
			sources.Clear();
			activeTileCounts.Clear();
			foreach (Tile t in knownTiles) {
				Reevaluate(t);
			}
			// The tiles that went out of view are remembered as they were.
			foreach (Tile t in wereActive) {
				if (!activeTileCounts.ContainsKey(t) && knownTiles.Contains(t)) {
					RememberImprovements(t);
				}
			}
			newlyKnownTiles.Clear();
			knownTileCountAtLastUpdate = knownTiles.Count;
			journalPosition = TileChangeJournal.head;
			journalEpoch = TileChangeJournal.epoch;
			needsFullRecompute = false;
		}

		// For tests: the active tiles computed from scratch, without touching
		// the incremental bookkeeping.
		internal HashSet<Tile> ComputeActiveTilesFromScratch() {
			HashSet<Tile> result = new();
			foreach (Tile t in knownTiles) {
				// A tile within a city's borders and all of its neighbors are active.
				// A unit's visibility might be going further than that of a city,
				// so we don't need to calculate this here.
				if (t.unitsOnTile.Count < 1 && t.owningCity != null && t.owningCity.owner == _player) {
					result.Add(t);

					foreach (Tile neighbor in t.neighbors.Values) {
						result.Add(neighbor);
					}
				}

				// A tile with a unit on it and all of its neighbors are active.
				if (t.unitsOnTile.Count > 0 && t.unitsOnTile[0].owner == _player) {
					foreach (Tile x in GetTilesVisibleToUnit(t)) {
						result.Add(x);
					}
				}

				// An outpost, colony or radar tower of ours sees around it.
				if (HasOwnOutpost(t)) {
					List<Tile> seen = new();
					CollectTilesSeenFromOutpost(t, seen);
					result.UnionWith(seen);
				}
			}
			result.Remove(Tile.NONE);
			return result;
		}

		// The tiles the player sees now: the active tiles as they were last
		// brought up to date, and those peeked at.
		internal IEnumerable<Tile> VisibleTiles() {
			foreach (Tile t in activeTileCounts.Keys) {
				yield return t;
			}
			foreach (Tile t in peekedTiles) {
				yield return t;
			}
		}

		// For tests: the current set of active tiles.
		internal HashSet<Tile> ActiveTiles() {
			HashSet<Tile> result = new(activeTileCounts.Keys);
			result.Remove(Tile.NONE);
			return result;
		}

		private SourceKind KindOf(Tile t) {
			if (t == null || !knownTiles.Contains(t)) {
				return SourceKind.None;
			}
			if (HasOwnOutpost(t)) {
				return SourceKind.Outpost;
			}
			List<MapUnit> units = t.unitsOnTile;
			if (units.Count > 0) {
				return units[0].owner == _player ? SourceKind.Unit : SourceKind.None;
			}
			City city = t.owningCity;
			return city != null && city.owner == _player ? SourceKind.City : SourceKind.None;
		}

		// Recomputes the contribution of a single tile.
		private void Reevaluate(Tile t) {
			if (t == null) {
				return;
			}
			SourceKind kind = KindOf(t);
			sources.TryGetValue(t, out Source old);

			switch (kind) {
				case SourceKind.None:
					if (old != null) {
						Release(old.tiles);
						sources.Remove(t);
					}
					return;

				case SourceKind.City:
					if (old != null && old.kind == SourceKind.City) {
						return;
					}
					if (old != null) {
						Release(old.tiles);
					}
					Tile[] cityTiles = new Tile[t.neighbors.Count + 1];
					cityTiles[0] = t;
					int i = 1;
					foreach (Tile neighbor in t.neighbors.Values) {
						cityTiles[i++] = neighbor;
					}
					Acquire(cityTiles);
					sources[t] = new Source { kind = SourceKind.City, tiles = cityTiles };
					return;

				case SourceKind.Unit:
					visibleScratch.Clear();
					CollectTilesVisibleToUnit(t, visibleScratch);
					bool radar = HasRadarUnits(t);
					if (old != null && old.kind == SourceKind.Unit && SameTiles(old.tiles, visibleScratch)) {
						old.radar = radar;
						return;
					}
					if (old != null) {
						Release(old.tiles);
					}
					Tile[] unitTiles = visibleScratch.ToArray();
					Acquire(unitTiles);
					sources[t] = new Source { kind = SourceKind.Unit, radar = radar, tiles = unitTiles };
					return;

				case SourceKind.Outpost:
					visibleScratch.Clear();
					CollectTilesSeenFromOutpost(t, visibleScratch);
					if (t.unitsOnTile.Count > 0 && t.unitsOnTile[0].owner == _player) {
						CollectTilesVisibleToUnit(t, visibleScratch);
					}
					if (old != null && old.kind == SourceKind.Outpost && SameTiles(old.tiles, visibleScratch)) {
						return;
					}
					if (old != null) {
						Release(old.tiles);
					}
					Tile[] outpostTiles = visibleScratch.ToArray();
					Acquire(outpostTiles);
					sources[t] = new Source { kind = SourceKind.Outpost, tiles = outpostTiles };
					return;
			}
		}

		private static bool SameTiles(Tile[] a, List<Tile> b) {
			if (a.Length != b.Count) {
				return false;
			}
			for (int i = 0; i < a.Length; ++i) {
				if (a[i] != b[i]) {
					return false;
				}
			}
			return true;
		}

		private void Acquire(Tile[] tiles) {
			foreach (Tile t in tiles) {
				if (t == null) {
					continue;
				}
				activeTileCounts.TryGetValue(t, out int count);
				activeTileCounts[t] = count + 1;
			}
		}

		private void Release(Tile[] tiles) {
			foreach (Tile t in tiles) {
				if (t == null) {
					continue;
				}
				int count = activeTileCounts[t] - 1;
				if (count == 0) {
					activeTileCounts.Remove(t);
					// Going out of view: remember it as it is now.
					if (knownTiles.Contains(t)) {
						RememberImprovements(t);
					}
				} else {
					activeTileCounts[t] = count;
				}
			}
		}

		public List<Tile> OwnedTiles() {
			List<Tile> result = new();
			foreach (Tile t in knownTiles) {
				if (t.OwningPlayer() == _player) {
					result.Add(t);
				}
			}
			return result;
		}

		public List<Tile> DominationTiles() {
			List<Tile> result = new();
			foreach (Tile t in knownTiles) {
				if (t.OwningPlayer() == _player && t.IsCountedForDomination()) {
					result.Add(t);
				}
			}
			return result;
		}

		public List<Tile> ScoreTiles() {
			List<Tile> result = new();
			foreach (Tile t in knownTiles) {
				if (t.OwningPlayer() == _player && t.IsCountedForScore()) {
					result.Add(t);
				}
			}
			return result;
		}

		// DominationTiles().Count and ScoreTiles().Count, without building
		// the lists: victory checks and the score want only the number,
		// every turn for every player.
		public int CountDominationTiles() {
			int count = 0;
			foreach (Tile t in knownTiles) {
				if (t.OwningPlayer() == _player && t.IsCountedForDomination()) {
					++count;
				}
			}
			return count;
		}

		public int CountScoreTiles() {
			int count = 0;
			foreach (Tile t in knownTiles) {
				if (t.OwningPlayer() == _player && t.IsCountedForScore()) {
					++count;
				}
			}
			return count;
		}
	}
}
