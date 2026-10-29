using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData.Save;
using Serilog;

namespace C7GameData {
	// Nuclear weapons (Civilopedia): no civ can build them until someone has
	// built the Manhattan Project ("Allows the construction of nuclear
	// weapons by all civilizations"). A Tactical Nuke strikes up to 6 tiles
	// away (its bombard range) and an ICBM "can strike any target on the
	// map", both against a civ we're at war with. A nuke aimed at a city of a
	// civ with the Strategic Missile Defense is shot down 75% of the time
	// (players: "your nuclear force is effectively 3/4ths smaller"; it
	// protects cities only). Otherwise it destroys every unit on its tile and
	// the eight around it, kills half the target city's people and some of
	// its buildings, and leaves fallout (pollution, which feeds global
	// warming) on the land around. Using one has "a tremendous negative
	// effect upon world opinion". Civ3 documents none of the numbers: all
	// are rules.
	public static class Nuclear {
		private static ILogger log = Log.ForContext(typeof(Nuclear));

		public static bool IsNuke(UnitPrototype type) => type.flags.Contains(SaveUnitPrototype.Flag.NuclearWeapon);

		// Whether anyone has built a wonder allowing nuclear weapons.
		public static bool Allowed(GameData gameData) {
			return gameData.GreatWondersBuilt.Any(name => gameData.Buildings.Find(b => b.name == name)?.HasWonderFlag(SaveBuilding.WonderFlag.AllowsNuclearWeapons) == true);
		}

		// Whether `nuke` can be aimed at `target`: any tile in range (an ICBM
		// anywhere), seen or not, with anyone or no one there (user, 2026-10-29).
		// Striking a civ at peace with us needs a declaration of war first
		// (see AtPeace).
		public static bool CanStrike(MapUnit nuke, Tile target) {
			if (!IsNuke(nuke.unitType) || !nuke.movementPoints.canMove || !Tile.IsTileValid(target) || target == nuke.location) {
				return false;
			}
			bool icbm = nuke.unitType.flags.Contains(SaveUnitPrototype.Flag.ICBM);
			return icbm || nuke.location.DistanceTo(target) <= nuke.unitType.bombardRange;
		}

		// The civs at peace with the attacker whose units or city are on `target`.
		public static List<Player> AtPeace(Player attacker, Tile target) {
			return Victims(attacker, target).Where(attacker.IsAtPeaceWith).ToList();
		}

		// Whose units or city are on `target`.
		internal static List<Player> Victims(Player attacker, Tile target) {
			IEnumerable<Player> owners = target.unitsOnTile.Select(u => u.owner);
			if (target.cityAtTile != null) {
				owners = owners.Append(target.cityAtTile.owner);
			}
			return owners.Where(p => p != attacker && !p.isBarbarians).Distinct().ToList();
		}

		// Whether a missile defense covers a strike at `target`: one guarding
		// a city the blast would reach (else a nuke aimed beside the city
		// would kill its defenders unopposed), not the attacker's own.
		public static bool Defended(Player attacker, Tile target) {
			return Blast(target).Any(t => t.cityAtTile is City city && city.owner != attacker
				&& city.owner.OwnsBuilding(b => b.HasWonderFlag(SaveBuilding.WonderFlag.DecreasesMissileSuccess)));
		}

		private static List<Tile> Blast(Tile target) => target.neighbors.Values.Where(Tile.IsTileValid).Append(target).ToList();

		public enum Outcome {
			Impossible,
			ShotDown,
			Detonated,
		}

		public static Outcome Strike(GameData gameData, MapUnit nuke, Tile target) {
			if (!CanStrike(nuke, target) || AtPeace(nuke.owner, target).Count > 0) {
				return Outcome.Impossible;
			}
			NuclearRules rules = nuke.owner.rules?.nuclear ?? gameData.rules.nuclear;
			Player attacker = nuke.owner;
			List<Player> victims = Victims(attacker, target);
			nuke.RemoveFromPlay();
			Attitude.OnNuclearAttack(gameData, attacker, victims);
			gameData.nukesUsed++;

			if (Defended(attacker, target) && GameData.rng.Next(100) < rules.missileDefensePercent) {
				log.Information($"{attacker}'s nuke at {target} is shot down by a missile defense");
				new MsgNuclearAttack(attacker, target, Outcome.ShotDown, 0, 0).send();
				return Outcome.ShotDown;
			}

			List<Tile> blast = Blast(target);
			int unitsKilled = 0;
			foreach (MapUnit unit in blast.SelectMany(t => t.unitsOnTile).ToList()) {
				if (unit.hitPointsRemaining <= 0) {
					continue;
				}
				if (unit.owner != attacker) {
					WarRecord.OnUnitLost(unit, attacker);
				}
				gameData.RemoveUnit(unit);
				unitsKilled++;
			}

			int citizensKilled = 0;
			if (target.cityAtTile is City city) {
				int dead = city.residents.Count * rules.populationKilledPercent / 100;
				for (int i = 0; i < dead && city.residents.Count > 1; i++) {
					city.RemoveRandomCitizen();
					citizensKilled++;
				}
				foreach (CityBuilding b in city.constructed_buildings.Where(b => !b.building.IsWonder() && !b.building.isCenterOfEmpire).ToList()) {
					if (GameData.rng.Next(100) < rules.buildingDestroyedPercent) {
						city.RemoveBuilding(b);
					}
				}
			}

			// Fallout on the land around.
			TerrainImprovement pollution = gameData.terrainImprovements.Find(i => i.key == Tile.TileOverlays.POLLUTION);
			foreach (Tile t in blast.Where(t => t.IsLand() && !t.HasCity())) {
				if (pollution != null && t.overlays.CanAdd(t, pollution) && GameData.rng.Next(100) < rules.falloutPercent) {
					t.overlays.Add(pollution);
				}
			}

			log.Information($"{attacker} nukes {target}: {unitsKilled} units and {citizensKilled} citizens killed");
			new MsgNuclearAttack(attacker, target, Outcome.Detonated, unitsKilled, citizensKilled).send();
			return Outcome.Detonated;
		}
	}
}
