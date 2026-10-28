using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7GameData.Save;
using Serilog;

namespace C7GameData {
	// Air units (Civilopedia "Air Missions"): "Air units are not moved around
	// the map like other units. Instead, they are stationed in a city, and
	// assigned missions which they execute immediately against targets within
	// their operational range": bombing, recon, precision strikes, air
	// superiority (intercepting enemy missions "within the inner half of its
	// operational range") and re-basing (to a city or a carrier "up to Six
	// times the plane's operational range"). A mission takes the unit's
	// turn. On its way, an enemy on air superiority may intercept it (RULE:
	// 50%, 5% for stealth) and fight it; over the target, the strongest air
	// defense there may shoot it down. Paratroopers airdrop within their
	// range from a city, and units airlift between two cities with airports
	// (one a turn from each).
	public static class Air {
		private static ILogger log = Log.ForContext(typeof(Air));

		public enum Mission {
			Bombing,
			Recon,
			PrecisionStrike,
			Rebase,
			AirSuperiority,
		}

		public static UnitAction ActionFor(Mission mission) => mission switch {
			Mission.Bombing => UnitAction.Bombing,
			Mission.Recon => UnitAction.Recon,
			Mission.PrecisionStrike => UnitAction.PrecisionStrike,
			Mission.Rebase => UnitAction.Rebase,
			_ => UnitAction.AirSuperiority,
		};

		// The carrier on `tile` that can take `plane`, or null.
		public static MapUnit CarrierFor(MapUnit plane, Tile tile) {
			return tile.unitsOnTile.FirstOrDefault(c => c.owner == plane.owner && c != plane
				&& c.unitType.flags.Contains(SaveUnitPrototype.Flag.CanCarryAircraft)
				&& (plane.IsLoadedIn(c) || c.FreeCapacity() >= 1));
		}

		// Whether `plane` can be based at `tile`: one of its owner's cities,
		// or a carrier of its owner's with room.
		public static bool IsBase(MapUnit plane, Tile tile) {
			return (tile.HasCity() && tile.cityAtTile.owner == plane.owner) || CarrierFor(plane, tile) != null;
		}

		public static bool CanFly(MapUnit plane, Mission mission) {
			return plane.IsAirUnit() && plane.unitType.actions.Contains(ActionFor(mission))
				&& plane.movementPoints.canMove && plane.hitPointsRemaining > 0;
		}

		// Whether `plane` can fly `mission` to `target` now.
		public static bool CanFly(MapUnit plane, Mission mission, Tile target) {
			if (!CanFly(plane, mission) || !Tile.IsTileValid(target)) {
				return false;
			}
			int distance = plane.location.DistanceTo(target);
			int range = plane.unitType.operationalRange;
			switch (mission) {
				case Mission.Bombing:
					// At war: bombing someone at peace would need a declaration first.
					// (CanBombardTile checks the range.)
					return plane.CanBombardTile(target) && Targets(plane, target).All(p => !plane.owner.IsAtPeaceWith(p));
				case Mission.Recon:
					return distance <= range;
				case Mission.PrecisionStrike:
					return distance <= range && target.cityAtTile is City city && city.owner != plane.owner && !plane.owner.IsAtPeaceWith(city.owner);
				case Mission.Rebase:
					return target != plane.location && distance <= range * (plane.owner.rules?.air.rebaseRangeFactor ?? 6) && IsBase(plane, target);
				default:
					return true;
			}
		}

		// Whose things a mission over `target` hits.
		private static IEnumerable<Player> Targets(MapUnit plane, Tile target) {
			IEnumerable<Player> owners = target.unitsOnTile.Where(u => u.owner != plane.owner).Select(u => u.owner);
			if (target.cityAtTile is City city && city.owner != plane.owner) {
				owners = owners.Append(city.owner);
			} else if (target.OwningPlayer() is Player landOwner && landOwner != plane.owner && target.HasImprovements) {
				owners = owners.Append(landOwner);
			}
			return owners.Distinct();
		}

		// The enemy on air superiority duty that takes on a mission by
		// `plane` to `target`, or null: those within half their range of the
		// target try in turn, each with the rules' chance.
		public static MapUnit Interceptor(GameData gameData, MapUnit plane, Tile target) {
			AirRules rules = plane.owner.rules?.air ?? gameData.rules.air;
			int percent = plane.unitType.stealth ? rules.stealthInterceptPercent : rules.interceptPercent;
			foreach (MapUnit fighter in gameData.mapUnits.Where(u => u.airSuperiority && u.owner != plane.owner && u.hitPointsRemaining > 0
				&& !u.owner.IsAtPeaceWith(plane.owner) && u.location.DistanceTo(target) <= u.unitType.operationalRange / 2).ToList()) {
				if (GameData.rng.Next(100) < percent) {
					return fighter;
				}
			}
			return null;
		}

		// The strongest anti-aircraft fire at `target`: an enemy unit's air
		// defense there, or an enemy city's air power (the SAM Missile
		// Battery).
		public static int AntiAir(MapUnit plane, Tile target) {
			int units = target.unitsOnTile.Where(u => u.owner != plane.owner && !u.owner.IsAtPeaceWith(plane.owner))
				.Select(u => u.unitType.airDefense).DefaultIfEmpty(0).Max();
			int city = target.cityAtTile is City c && c.owner != plane.owner && !c.owner.IsAtPeaceWith(plane.owner)
				? c.constructed_buildings.Select(b => b.building.airPower).DefaultIfEmpty(0).Max() : 0;
			return Math.Max(units, city);
		}

		public static int ShootDownPercent(GameData gameData, MapUnit plane, Tile target) {
			AirRules rules = plane.owner.rules?.air ?? gameData.rules.air;
			int percent = AntiAir(plane, target) * rules.antiAirPercentPerPoint;
			// Stealth aircraft are as much harder to hit as to intercept.
			if (plane.unitType.stealth && rules.interceptPercent > 0) {
				percent = percent * rules.stealthInterceptPercent / rules.interceptPercent;
			}
			return Math.Clamp(percent, 0, 100);
		}

		public enum Outcome {
			Done,
			Intercepted,
			ShotDown,
			Impossible,
		}

		// Flies `mission` to `target`: the plane may be intercepted or shot
		// down on the way; otherwise it bombs, scouts or strikes the target,
		// or moves there. It takes the plane's turn.
		public static async Task<Outcome> Fly(GameData gameData, MapUnit plane, Mission mission, Tile target = null) {
			if (mission == Mission.AirSuperiority) {
				if (!CanFly(plane, mission)) {
					return Outcome.Impossible;
				}
				plane.airSuperiority = true;
				plane.movementPoints.onConsumeAll();
				log.Information($"{plane} flies air superiority over {plane.location}");
				return Outcome.Done;
			}
			if (!CanFly(plane, mission, target)) {
				return Outcome.Impossible;
			}
			plane.Wake();
			if (mission == Mission.Rebase) {
				Rebase(gameData, plane, target);
				return Outcome.Done;
			}

			Outcome outcome = await Penetrate(gameData, plane, target);
			if (outcome == Outcome.Done) {
				switch (mission) {
					case Mission.Bombing:
						await plane.Bombard(target);
						break;
					case Mission.Recon:
						Recon(gameData, plane, target);
						break;
					case Mission.PrecisionStrike:
						PrecisionStrike(gameData, plane, target);
						break;
				}
			}
			if (plane.hitPointsRemaining > 0) {
				plane.movementPoints.onConsumeAll();
			}
			new MsgAirMission(plane.owner.id, plane.unitType.name, mission, target, outcome).send();
			return outcome;
		}

		// The flight to `target`: interception, then anti-aircraft fire.
		private static async Task<Outcome> Penetrate(GameData gameData, MapUnit plane, Tile target) {
			MapUnit fighter = Interceptor(gameData, plane, target);
			if (fighter != null) {
				log.Information($"{fighter} intercepts {plane} on its way to {target}");
				await fighter.Fight(plane);
				// Survivor or not, the mission is over.
				return Outcome.Intercepted;
			}
			if (GameData.rng.Next(100) < ShootDownPercent(gameData, plane, target)) {
				log.Information($"Anti-aircraft fire at {target} shoots down {plane}");
				Player shooter = target.cityAtTile?.owner ?? target.unitsOnTile.FirstOrDefault(u => u.owner != plane.owner)?.owner;
				if (shooter != null) {
					WarRecord.OnUnitLost(plane, shooter);
					VictoryPoints.OnUnitKilled(gameData, plane, shooter);
				}
				plane.RemoveFromPlay();
				return Outcome.ShotDown;
			}
			return Outcome.Done;
		}

		private static void Recon(GameData gameData, MapUnit plane, Tile target) {
			int radius = plane.owner.rules?.air.reconRadius ?? gameData.rules.air.reconRadius;
			plane.owner.tileKnowledge.Learn(target.GetTilesWithinTileSquare(radius).Where(Tile.IsTileValid).Append(target).ToList());
			log.Information($"{plane} scouts around {target}");
		}

		// Civilopedia: "precision strikes destroy city improvements only,
		// unless there are none in which case population is affected": one
		// strike with the plane's bombard strength against a building's
		// defense (as bombarding a city).
		private static void PrecisionStrike(GameData gameData, MapUnit plane, Tile target) {
			City city = target.cityAtTile;
			// Its own buildings (not the ones a wonder gives it).
			List<CityBuilding> buildings = city.constructed_buildings.Where(b => !b.building.isCenterOfEmpire && !b.building.IsWonder()).ToList();
			double strength = plane.StrengthVersus(null, CombatRole.Bombard, plane.facingDirection);
			if (GameData.rng.NextDouble() >= strength / (strength + MapUnit.BombardBuildingDefense)) {
				return;
			}
			if (buildings.Count > 0) {
				CityBuilding hit = buildings[GameData.rng.Next(buildings.Count)];
				city.RemoveBuilding(hit);
				log.Information($"{plane}'s precision strike destroys {city.name}'s {hit.building.name}");
			} else if (city.residents.Count > 1) {
				city.RemoveRandomCitizen();
				log.Information($"{plane}'s precision strike kills a citizen of {city.name}");
			}
		}

		private static void Rebase(GameData gameData, MapUnit plane, Tile target) {
			MapUnit carrier = target.HasCity() ? null : CarrierFor(plane, target);
			gameData.TeleportUnit(plane, target);
			plane.loadedOnUnitId = carrier?.id;
			plane.movementPoints.onConsumeAll();
			log.Information($"{plane} re-bases to {target}{(carrier == null ? "" : $" aboard {carrier}")}");
		}

		// ---------------------------------------------------------------- airdrops and airlifts

		// Paratroopers drop from one of their owner's cities onto land
		// within their operational range that holds no foreign units or
		// city, using their turn.
		public static bool CanAirdrop(MapUnit unit, Tile target) {
			return unit.unitType.actions.Contains(UnitAction.Airdrop) && unit.IsLandUnit() && unit.movementPoints.canMove
				&& unit.location.cityAtTile?.owner == unit.owner
				&& Tile.IsTileValid(target) && target.IsLand() && target != unit.location
				&& unit.location.DistanceTo(target) <= unit.unitType.operationalRange
				&& !target.IsImpassableTo(unit.unitType)
				&& !(target.HasCity() && target.cityAtTile.owner != unit.owner)
				&& target.unitsOnTile.All(u => u.owner == unit.owner);
		}

		public static bool Airdrop(GameData gameData, MapUnit unit, Tile target) {
			if (!CanAirdrop(unit, target)) {
				return false;
			}
			unit.Wake();
			gameData.TeleportUnit(unit, target);
			unit.movementPoints.onConsumeAll();
			unit.owner.tileKnowledge.AddTilesToKnown(target);
			log.Information($"{unit} airdrops to {target}");
			return true;
		}

		public static bool HasAirport(City city) {
			return city != null && city.GetBuildings().Any(b => b.building.allowsAirTrade);
		}

		// A unit airlifts from a city of its owner's with an airport to
		// another with one; each airport sends one unit a turn.
		public static bool CanAirlift(GameData gameData, MapUnit unit, Tile target) {
			City from = unit.location.cityAtTile;
			City to = target.cityAtTile;
			return unit.unitType.actions.Contains(UnitAction.Airlift) && unit.movementPoints.canMove
				&& from?.owner == unit.owner && to?.owner == unit.owner && from != to
				&& HasAirport(from) && HasAirport(to) && from.lastAirliftTurn != gameData.turn;
		}

		public static bool Airlift(GameData gameData, MapUnit unit, Tile target) {
			if (!CanAirlift(gameData, unit, target)) {
				return false;
			}
			unit.location.cityAtTile.lastAirliftTurn = gameData.turn;
			unit.Wake();
			gameData.TeleportUnit(unit, target);
			unit.movementPoints.onConsumeAll();
			log.Information($"{unit} airlifts to {target.cityAtTile.name}");
			return true;
		}
	}
}
