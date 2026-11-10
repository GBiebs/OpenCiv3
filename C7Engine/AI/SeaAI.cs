using System.Collections.Generic;
using System.Linq;
using C7Engine.AI;
using C7Engine.AI.UnitAI;
using C7Engine.Pathing;
using C7GameData;
using C7GameData.AIData;
using C7GameData.Save;
using Serilog;

namespace C7Engine {
	// The AI at sea: ferries carry settlers to land overseas and attackers to
	// an enemy's coast (FerryAI with OverseasAI passengers), warships go
	// after enemy ships they're likely to beat, and both wait in port when
	// there's nothing to do. Choices here are AI tuning, not Civ3 rules.
	public static class SeaAI {
		private static readonly ILogger log = Log.ForContext(typeof(SeaAI));

		// How far around its target a ferry looks for free land to set its
		// passengers on.
		internal const int LandingRadius = 5;
		// A ferry with room left waits in port up to this many turns for
		// passengers still on their way.
		internal const int FerryWaitTurns = 3;
		// A unit waits this many turns in port for a ferry before it gives
		// up its trip.
		internal const int PassengerWaitTurns = 20;
		// The least chance of winning a warship wants before it attacks.
		internal const double WarshipMinWinChance = 0.6;
		// How far a warship looks for enemy ships.
		internal const int WarshipRange = 6;
		// How far away an overseas settler site may be.
		internal const int OverseasSiteRange = 25;
		// What a war overseas counts as in distance, on top of the miles.
		internal const int OverseasWarDistance = 10;

		// Landings so far (for the soak's report).
		internal static int landings;

		// The sites settlers were shipped to, for their settler AI to take up
		// once ashore (SettlerAI.MakeAiData).
		private static readonly Dictionary<ID, Tile> shippedTo = new();

		internal static void Landed(MapUnit unit, Tile destination) {
			++landings;
			if (unit.unitType.isSettler) {
				shippedTo[unit.id] = destination;
			}
		}

		// The site a settler was shipped to, once (null if none).
		public static Tile TakeShippedSite(MapUnit settler) {
			return shippedTo.Remove(settler.id, out Tile site) ? site : null;
		}

		// A ship that carries land units (not a carrier's aircraft or a
		// missile sub's missiles).
		public static bool IsFerryType(UnitPrototype type) {
			return type.categories.Contains("Sea") && type.capacity > 0 && type.actions.Contains(UnitAction.Unload)
				&& !type.flags.Contains(SaveUnitPrototype.Flag.CanCarryAircraft)
				&& !type.flags.Contains(SaveUnitPrototype.Flag.CanCarryTacticalMissiles)
				&& !type.flags.Contains(SaveUnitPrototype.Flag.CanCarryFootUnitsOnly);
		}

		public static bool IsFerry(MapUnit unit) => IsFerryType(unit.unitType) && !unit.IsInArmy();

		// A ship that fights and carries nothing.
		public static bool IsWarshipType(UnitPrototype type) {
			return type.categories.Contains("Sea") && type.attack > 0 && type.capacity == 0;
		}

		internal static bool AtWar(Player us, Player them) {
			return them.isBarbarians || (us.playerRelationships.TryGetValue(them.id, out PlayerRelationship r) && r.AtWar());
		}

		// Our cities next to the sea (not a lake) on that continent: where
		// ships sail from.
		public static List<City> Ports(Player player, int continent) {
			return player.cities.Where(c => c.location.continent == continent && c.location.NeighborsOcean()).ToList();
		}

		public static City NearestPort(Player player, Tile from) {
			return Ports(player, from.continent).OrderBy(c => c.location.DistanceTo(from)).FirstOrDefault();
		}

		// Where units on this continent bound for the destination gather for
		// their ferry: our port there nearest the destination, so all of them
		// wait in one place and the crossing is short.
		public static City MusterPort(Player player, Tile from, Tile destination) {
			return Ports(player, from.continent).OrderBy(c => c.location.DistanceTo(destination)).ThenBy(c => c.location.DistanceTo(from)).FirstOrDefault();
		}

		// Whether the player knows how to build a ferry (unique units and
		// resources aside: this is only a guide for plans).
		public static bool KnowsFerries(Player player) {
			return EngineStorage.gameData.unitPrototypes.Any(t => IsFerryType(t) && (t.requiredTech == null || player.knownTechs.Contains(t.requiredTech.id)));
		}

		// Whether units on this continent can be shipped: a port there, and a
		// ferry or the means to build one.
		public static bool CanFerry(Player player, int continent) {
			return Ports(player, continent).Count > 0 && (player.units.Any(IsFerry) || KnowsFerries(player));
		}

		// Land a unit can be set on: no city but ours, no one else's units,
		// and not the land of a civ we're at peace with (its border guards
		// would send the unit home).
		public static bool IsFreeLand(Tile tile, Player us) {
			return tile != Tile.NONE && tile.IsLand() && (tile.cityAtTile == null || tile.cityAtTile.owner == us)
				&& tile.unitsOnTile.All(u => u.owner == us)
				&& (tile.OwningPlayer() is not Player owner || owner == us || AtWar(us, owner));
		}

		// Whether a trip is still worth making: an enemy city still ours to
		// attack, a site still free to settle (a war may end, or another
		// civ settle there, while the unit is on its way).
		public static bool StillWorthIt(Player player, OverseasAIData trip) {
			Tile to = trip.destination;
			if (to == null) {
				return false;
			}
			if (trip.attack) {
				return to.cityAtTile is City city && city.owner != player && !city.owner.isBarbarians && AtWar(player, city.owner);
			}
			return !to.HasCity() && (to.OwningPlayer() is not Player owner || owner == player)
				&& !to.HasCityCloserThan(System.Math.Max(3, player.rules?.MinimumCityDistance ?? 0));
		}

		// Where a ferry sets passengers bound for the target ashore: a water
		// tile it can reach next to free land of the target's continent, as
		// near the target as can be. Null if there's none.
		public static (Tile water, Tile land)? LandingSpot(MapUnit ferry, Tile target) {
			PathingAlgorithm algorithm = PathingAlgorithmChooser.GetAlgorithm(ferry);
			IEnumerable<(Tile water, Tile land)> candidates = target.GetTilesWithinRankDistance(LandingRadius)
				.Where(l => l != Tile.NONE && l.continent == target.continent && IsFreeLand(l, ferry.owner))
				.SelectMany(l => l.neighbors.Values.Where(w => w != Tile.NONE && !w.IsLand()).Select(w => (w, l)))
				.OrderBy(p => p.l.DistanceTo(target)).ThenBy(p => p.w.DistanceTo(ferry.location));
			foreach ((Tile water, Tile land) in candidates.Take(12)) {
				if (water == ferry.location) {
					return (water, land);
				}
				TilePath path = algorithm.PathFrom(ferry.location, water, ferry);
				if (path != null && path.PathLength() > 0) {
					return (water, land);
				}
			}
			return null;
		}

		// The units waiting for a ferry, or on their way to wait for one.
		public static IEnumerable<(MapUnit unit, OverseasAI plan)> Travellers(Player player) {
			foreach (MapUnit unit in player.units) {
				if (unit.currentAI is OverseasAI plan && plan.data.port != null && !unit.IsLoaded()) {
					yield return (unit, plan);
				}
			}
		}

		// Whether this continent's travellers need another ferry: more of
		// them than room on our ferries, and none being built.
		public static bool FerryNeeded(Player player, int continent) {
			int waiting = Travellers(player).Count(t => t.plan.data.port.continent == continent);
			if (waiting == 0) {
				return false;
			}
			List<MapUnit> ferries = player.units.Where(IsFerry).ToList();
			int room = ferries.Sum(f => f.FreeCapacity());
			bool building = player.cities.Any(c => c.itemBeingProduced is UnitPrototype t && IsFerryType(t));
			int most = 2 + player.cities.Count(c => c.location.NeighborsOcean()) / 4;
			return waiting > room && !building && ferries.Count < most;
		}

		// Whether we'd like a warship: at war with a civ that has ships or
		// lives across the water, with fewer warships than one for every
		// three ports, and none being built.
		public static bool WarshipsWanted(Player player) {
			GameData gameData = EngineStorage.gameData;
			bool navalWar = gameData.players.Any(p => p != player && !p.isBarbarians && !p.defeated && AtWar(player, p)
				&& (p.units.Any(u => u.IsWaterUnit()) || p.cities.Any(c => player.cities.All(o => o.location.continent != c.location.continent))));
			if (!navalWar) {
				return false;
			}
			int ports = player.cities.Count(c => c.location.NeighborsOcean());
			int warships = player.units.Count(u => IsWarshipType(u.unitType));
			bool building = player.cities.Any(c => c.itemBeingProduced is UnitPrototype t && IsWarshipType(t));
			return warships < System.Math.Max(1, ports / 3) && !building;
		}

		// The best place for a settler that has none on its own continent:
		// the best site we know on another one, that no other traveller is
		// headed for, if we can ferry it there.
		public static Tile OverseasSettlerSite(MapUnit settler, Player player) {
			if (!CanFerry(player, settler.location.continent)) {
				return Tile.NONE;
			}
			List<Tile> taken = player.units.Where(u => u != settler && u.currentAI is OverseasAI o && o.data.destination != null)
				.Select(u => ((OverseasAI)u.currentAI).data.destination).ToList();
			Dictionary<Tile, float> scores = SettlerLocationAI.GetScoredSettlerCandidates(settler.location, player, overseasWithin: OverseasSiteRange);
			foreach ((Tile site, float _) in scores.OrderByDescending(s => s.Value)) {
				if (taken.All(t => t.DistanceTo(site) > 2)) {
					return site;
				}
			}
			return Tile.NONE;
		}

		// An enemy city across the water an attacker could be shipped to: the
		// nearest one we know. Tile.NONE if none, or if it's no attacker or
		// can't be shipped.
		public static Tile OverseasAttackTarget(MapUnit unit, Player player) {
			if (!unit.IsLandUnit() || unit.unitType.attack <= 0 || unit.unitType.attack < unit.unitType.defense
				|| !CanFerry(player, unit.location.continent)) {
				return Tile.NONE;
			}
			City target = EngineStorage.gameData.cities
				.Where(c => c.location.continent != unit.location.continent && !c.owner.isBarbarians && AtWar(player, c.owner)
					&& player.tileKnowledge.isTileKnown(c.location))
				.OrderBy(c => c.location.DistanceTo(unit.location))
				.FirstOrDefault();
			return target?.location ?? Tile.NONE;
		}

		// An enemy ship near enough that a warship is likely to sink it.
		public static CombatAIData WarshipAttack(MapUnit ship, Player player) {
			if (!IsWarshipType(ship.unitType)) {
				return null;
			}
			PathingAlgorithm algorithm = PathingAlgorithmChooser.GetAlgorithm(ship);
			foreach (Tile t in ship.location.GetTilesWithinRankDistance(WarshipRange).Where(t => t != Tile.NONE && !t.IsLand())
				.OrderBy(t => t.DistanceTo(ship.location))) {
				if (!t.unitsOnTile.Any(u => AtWar(player, u.owner) && Naval.IsVisibleTo(u, player))) {
					continue;
				}
				MapUnit defender = t.FindTopDefender(ship);
				if (defender == MapUnit.NONE || CombatOdds.AttackerWinChance(ship, defender) < WarshipMinWinChance) {
					continue;
				}
				TilePath path = algorithm.PathFrom(ship.location, t, ship);
				if (path == null || path.PathLength() <= 0) {
					continue;
				}
				log.Information($"{ship} goes after the ships at {t}");
				return new CombatAIData { destination = t, path = path };
			}
			return null;
		}

		// A ship's job, if the sea has one for it now: a ferry's when it has
		// passengers or someone waits for one, a warship's attack.
		public static UnitAI JobFor(MapUnit ship, Player player) {
			if (IsFerry(ship) && (ship.Passengers().Count > 0 || Travellers(player).Any())) {
				return new FerryAI();
			}
			if (IsWarshipType(ship.unitType) && WarshipAttack(ship, player) is CombatAIData attack) {
				return new CombatAI(attack);
			}
			return null;
		}

		// A ship with nothing to do waits in port: a ferry as a ferry (it
		// sails when someone needs it), any other ship fortified there.
		public static UnitAI IdleJob(MapUnit ship, Player player) {
			if (IsFerry(ship)) {
				return new FerryAI();
			}
			City port = NearestReachablePort(ship, player);
			if (port != null && port.location != ship.location && DefenderAI.MakeAiDataForDefendCity(ship, port) is DefenderAIData toPort) {
				return new DefenderAI(toPort);
			}
			return new DefenderAI(DefenderAI.MakeAiDataForDefendInPlace(ship, player));
		}

		// Our nearest port (on any continent) the ship can sail into.
		public static City NearestReachablePort(MapUnit ship, Player player) {
			PathingAlgorithm algorithm = PathingAlgorithmChooser.GetAlgorithm(ship);
			foreach (City port in player.cities.Where(c => c.location.NeighborsOcean()).OrderBy(c => c.location.DistanceTo(ship.location)).Take(4)) {
				if (port.location == ship.location) {
					return port;
				}
				TilePath path = algorithm.PathFrom(ship.location, port.location, ship);
				if (path != null && path.PathLength() > 0) {
					return port;
				}
			}
			return null;
		}

		// A land unit aboard one of our ferries with no plan of its own (a
		// loaded game): it goes where its shipmates go, else stays aboard
		// until the ferry puts in at a port.
		public static UnitAI AboardJob(MapUnit unit, MapUnit ferry) {
			Tile shared = ferry.Passengers().Select(p => (p.currentAI as OverseasAI)?.data.destination).FirstOrDefault(d => d != null);
			return new OverseasAI(new OverseasAIData { destination = shared });
		}
	}
}
