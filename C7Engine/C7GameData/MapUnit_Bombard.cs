using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;

namespace C7GameData {
	public partial class MapUnit {
		// Shared by all units: it is only ever read.
		private static readonly AnimatedEffect[] hitList = [AnimatedEffect.Hit, AnimatedEffect.Hit2, AnimatedEffect.Hit3, AnimatedEffect.Hit5];

		public enum BombardTarget {
			None,
			City,
			Unit,
			Improvement
		}

		public bool HasBombardAbility() {
			return this.unitType.actions.Contains(UnitAction.Bombard);
		}

		// Whether this unit's bombardment can kill the target, rather than
		// only bring it down to its last hit point. Per the project owner,
		// the unit type's lethal land bombardment kills land units and its
		// lethal sea bombardment kills ships: "Lethal bombardment can
		// bombard units that are red-lined and therefore kill them"
		// (https://forums.civfanatics.com/threads/what-is-lethal-bombardment.129927/).
		// Aircraft at their base are killed by any bombardment: "Air units
		// can be destroyed by bombardment." / "There is no such attribute
		// as "lethal air bombardment"." (justanick, C3C 1.22,
		// https://forums.civfanatics.com/threads/bombardment-of-settlements-in-c3c-1-22.647207/, #1).
		public bool IsBombardmentLethalAgainst(MapUnit target) {
			if (target.IsAirUnit()) {
				return true;
			}
			return target.IsWaterUnit() ? unitType.isSeaBombardmentLethal : unitType.isLandBombardmentLethal;
		}

		public bool CanBombardTile(Tile tile) {
			return CanBombardTile(tile, out _);
		}

		/// <summary>
		/// Whether this unit can bombard a tile, regardless of status, except locked alliance, because nothing can break it.<br/>
		/// Locked alliances are more protective of tiles/units etc, than even our own;<br/>
		/// While we can pillage our own tiles, we can't pillage a tile belonging to a locked ally.
		/// </summary>
		/// <param name="tile"></param>
		/// <param name="bombardTarget"></param>
		/// <returns></returns>
		public bool CanBombardTile(Tile tile, out BombardTarget bombardTarget) {
			bombardTarget = BombardTarget.None;

			// Nuclear weapons are fired with the bombard order.
			if (IsNuclearWeapon())
				return CanNukeTile(tile, out bombardTarget);

			return CanBombardTileWithin(tile, this.unitType.bombardRange, out bombardTarget);
		}

		// CanBombardTile for a bombardment that reaches `range` tiles: the
		// unit's bombard range, or an aircraft's operational range for a
		// bombing mission (see MapUnit_Air.cs).
		private bool CanBombardTileWithin(Tile tile, int range, out BombardTarget bombardTarget) {
			bombardTarget = BombardTarget.None;

			if (this.location.DistanceTo(tile) > range)
				return false;

			if (this.unitType.bombard == 0)
				return false;

			if (!CanAttackAgainThisTurn())
				return false;

			if (tile.HasCity() && tile.cityAtTile.owner == this.owner)
				return false;

			if (tile.HasCity() && EngineStorage.gameData.AreInLockedPeace(this.owner, tile.cityAtTile.owner))
				return false;

			if (tile.HasCity())
				bombardTarget = BombardTarget.City;

			MapUnit target = tile.FindTopDefenderForBombard(this);

			// TODO: Consider colony on neutral tile (allies && potential enemies)

			if (tile.overlays.HasBeenImproved()) {
				if (target == NONE) {
					if (tile.OwningPlayer() != null && EngineStorage.gameData.AreInLockedPeace(this.owner, tile.OwningPlayer()))
						return false;
				}

				if (target != NONE) {
					bombardTarget = BombardTarget.Unit;
					if (EngineStorage.gameData.AreInLockedPeace(this.owner, target.owner))
						return false;

					if (target.IsCombatUnit() && target.owner != this.owner) {
						return true;
					}
				}

				if (tile.OwningPlayer() != null && EngineStorage.gameData.AreInLockedPeace(this.owner, tile.OwningPlayer()))
					return false;

				bombardTarget = BombardTarget.Improvement;
			} else {
				if (target != NONE) {
					if (!target.IsCombatUnit())
						return false;
					if (target.IsCombatUnit() && target.owner == this.owner)
						return false;
					if (EngineStorage.gameData.AreInLockedPeace(this.owner, target.owner))
						return false;

					bombardTarget = BombardTarget.Unit;
				}
			}

			if (bombardTarget == BombardTarget.None)
				return false;

			return true;
		}

		public async Task Bombard(Tile tile) {
			// Could check canBombardTile(..) again, but no need really

			if (IsNuclearWeapon()) {
				await NuclearStrike(tile);
				return;
			}

			MapUnit target = tile.FindTopDefenderForBombard(this);

			var hasTargetUnit = target != NONE && target.owner != owner;
			var hasForeignCity = tile.HasCity() && tile.cityAtTile.owner != owner;
			var hasTileImprovements = tile.HasImprovements;

			if (!(hasTargetUnit || hasTileImprovements || hasForeignCity))
				return; // Nothing to bombard

			facingDirection = location.DirectionTo(tile);
			hasAttackedThisTurn = true;
			bool barricaded = tile.overlays.HasImprovementWithKey(Tile.TileOverlays.BARRICADE, TerrainImprovement.Layer.Holdings);

			// A city is hit in a fixed order that depends on the bombarder
			// (see BombardCityInOrder). Elsewhere the units are hit before the
			// tile's improvements. Only lethal bombardment kills a unit;
			// otherwise the units are hit until all are down to their last
			// hit point (see Tile.FindTopDefenderForBombard).
			if (hasForeignCity)
				await BombardCityInOrder(tile);
			else if (hasTargetUnit)
				await BombardUnits(tile, target);
			else
				await BombardTileImprovements(tile);

			// A cruise missile is used up: "[Missile attacks] are like
			// bombardment, but the unit never returns to its base and the
			// attacks can destroy enemy units" (Conquests Civilopedia,
			// GCON_Combat). Which units are missiles is the BIQ's PRTO "Cruise
			// Missile" flag (the Cruise Missile in conquests.biq).
			if (unitType.isCruiseMissile && hitPointsRemaining > 0) {
				RemoveFromPlay();
				return;
			}

			// Bombarding a barricade ends the unit's turn, where another
			// bombardment costs one move: C3X, whose author reads the 1.22
			// code, added an "Option not to end a unit's turn after it bombards
			// a barricade" (https://github.com/maxpetul/C3X/blob/master/changelog.txt,
			// Release 17), so the base game does.
			if (barricaded) {
				movementPoints.onConsumeAll();
			}
		}

		// Whether this unit's bombardment can damage the unit at all. Land
		// artillery can't hurt aircraft anywhere, nor ships in a city:
		// "land artillery can't damage aircraft" (Flintlock, reading the
		// 1.22 code, https://forums.civfanatics.com/threads/c3x-exe-mod-including-bug-fixes-stack-bombard-and-much-more.666881/post-16021251)
		// and "Ships and air units cannot be hit with land based artillery"
		// (justanick, C3C 1.22 tests of bombarding settlements,
		// https://forums.civfanatics.com/threads/bombardment-of-settlements-in-c3c-1-22.647207/, #1).
		// UNVERIFIED: whether land artillery can hit a ship out at sea;
		// justanick's statement is about settlements, so here it still can.
		public bool CanBombardmentDamage(MapUnit target) {
			if (!IsLandUnit() || unitType.isCruiseMissile) {
				return true;
			}
			if (target.IsAirUnit()) {
				return false;
			}
			return !(target.IsWaterUnit() && target.location != null && target.location.HasCity());
		}

		// What a bombardment of a city may hit once no defence or unit is
		// left in its way (see BombardCity).
		[Flags]
		private enum CityHit {
			Buildings = 1,
			Citizens = 2,
			Either = Buildings | Citizens,
		}

		// The order in which a bombardment hits a city follows Conquests
		// 1.22, per the project owner ("Follow Civ3", owner's decision,
		// 2026-10-20). This replaces the owner's earlier ruling that ships
		// and planes hit a city like land artillery, its units first, with
		// walls an ordinary building for them. justanick's 1.22 tests
		// (https://forums.civfanatics.com/threads/bombardment-of-settlements-in-c3c-1-22.647207/, #1)
		// give a fixed priority by the kind of bombarder:
		// - Land artillery: "City walls are hit first." / "If there is are no
		//   city walls or the settlement has already reached city level, then
		//   land units are hit." / "If neither city walls nor land units are
		//   left to be hit, then citizens or buildings are hit," /
		//   "apparently with same probability." / "Ships and air units cannot
		//   be hit with land based artillery."
		// - Ships: "A coastal fortress is hit first.", then ships, then air
		//   units, then land units, then citizens or buildings.
		// - Air units: "If air units are left in the settlement, then air
		//   units or citizens are hit." / "If no air units are left in the
		//   settlement, then ships, citizens or buildings are hit.", then
		//   land units, citizens or buildings.
		// - Cruise missiles: "Ships are hits first.", then air units, land
		//   units, citizens or buildings.
		// Flintlock, reading the 1.22 code, agrees that "the normal bombard
		// method prioritizes damaging units" and that it "rolls first to
		// determine a target then rolls for damage" (C3X thread,
		// https://forums.civfanatics.com/threads/c3x-exe-mod-including-bug-fixes-stack-bombard-and-much-more.666881/post-16393188
		// and post-16311320); Predator145 there: C3C artillery "only targets
		// units until all of them have been redlined", and cruise missiles
		// "will always target sea units first" (post-16262382). A unit is
		// "left to be hit" while the bombardment can still take a hit point
		// from it (see Tile.FindTopDefenderForBombard).
		//
		// Disputed: Lanzelot (same thread, page 2) suggests the order may be
		// a strong bias rather than a rule ("city walls are hit with 95%
		// probability"), and the C3X mod's notes say Conquests artillery
		// "always goes after units first" without mentioning walls; this
		// follows justanick's tests, walls first in a town. Predator145 says
		// air bombardment targets aircraft first, where justanick has
		// aircraft or citizens; this follows justanick, as does Civinator
		// (post-16273890: "Air bombardment at first destroys planes and
		// citizens of a city").
		// UNVERIFIED: that the choices justanick lists together for air
		// units (e.g. "ships, citizens or buildings") are equally likely; he
		// says so only of citizens and buildings. The target is chosen once
		// per bombardment and all its shots go at it (see BombardUnits);
		// against citizens and buildings each shot chooses again (see
		// BombardCity).
		private async Task BombardCityInOrder(Tile tile) {
			City city = tile.cityAtTile;
			MapUnit Top(Func<MapUnit, bool> kind) {
				MapUnit u = tile.FindTopDefenderForBombard(this, kind);
				return u != NONE && u.owner != owner ? u : NONE;
			}
			MapUnit ship = Top(u => u.IsWaterUnit());
			MapUnit plane = Top(u => u.IsAirUnit());
			MapUnit landUnit = Top(u => u.IsLandUnit());

			if (unitType.isCruiseMissile) {
				await BombardFirstOf(tile, ship, plane, landUnit);
			} else if (IsAirUnit()) {
				if (plane != NONE) {
					await BombardUnitOrCity(tile, plane, CityHit.Citizens);
				} else if (ship != NONE) {
					await BombardUnitOrCity(tile, ship, CityHit.Either);
				} else if (landUnit != NONE) {
					await BombardUnitOrCity(tile, landUnit, CityHit.Either);
				} else {
					await BombardCity(tile);
				}
			} else if (IsWaterUnit()) {
				CityBuilding fortress = FindDestructibleCoastalFortress(city);
				if (fortress != null) {
					await BombardCityDefence(tile, fortress);
				} else {
					await BombardFirstOf(tile, ship, plane, landUnit);
				}
			} else {
				CityBuilding walls = FindDestructibleWalls(city);
				Building wonderWalls = FindWonderWalls(city);
				if (walls != null) {
					await BombardCityDefence(tile, walls);
				} else if (wonderWalls != null) {
					await BombardWonderWalls(tile, wonderWalls);
				} else {
					// Land artillery can't hit the city's ships or aircraft
					// (see CanBombardmentDamage), so only its land units.
					await BombardFirstOf(tile, landUnit);
				}
			}
		}

		// Bombards the first of the units there is, or else the city.
		private async Task BombardFirstOf(Tile tile, params MapUnit[] targets) {
			foreach (MapUnit target in targets) {
				if (target != NONE) {
					await BombardUnits(tile, target);
					return;
				}
			}
			await BombardCity(tile);
		}

		// Bombards the unit, or what of the city's buildings and citizens
		// `cityTargets` allows and there is to hit, each with the same
		// chance (see BombardCityInOrder).
		private async Task BombardUnitOrCity(Tile tile, MapUnit unit, CityHit cityTargets) {
			City city = tile.cityAtTile;
			bool citizens = cityTargets.HasFlag(CityHit.Citizens) && CanBombardCitizens(city);
			bool buildings = cityTargets.HasFlag(CityHit.Buildings) && BombardableBuildings(city).Count > 0;
			int choices = 1 + (citizens ? 1 : 0) + (buildings ? 1 : 0);
			if (GameData.rng.Next(0, choices) == 0) {
				await BombardUnits(tile, unit);
			} else {
				await BombardCity(tile, allowed: cityTargets);
			}
		}

		// Per the project owner, bombardment never kills a city's last
		// citizen.
		private static bool CanBombardCitizens(City city) {
			return city.residents.Count > 1;
		}

		// The buildings of the city bombardment can destroy (see
		// IsBombardableBuilding).
		private static List<CityBuilding> BombardableBuildings(City city) {
			return city.constructed_buildings.Where(cb => IsBombardableBuilding(city, cb)).ToList();
		}

		// The coastal fortress a ship's bombardment has to break first, or
		// null: "A coastal fortress is hit first." (justanick, C3C 1.22, see
		// BombardCityInOrder). He gives it no size limit, unlike walls. A
		// coastal fortress is a building with the BIQ's naval power or naval
		// defense bonus (see IsWallsOrCoastalFortress).
		private static CityBuilding FindDestructibleCoastalFortress(City city) {
			foreach (CityBuilding cb in city.constructed_buildings) {
				Building b = cb.building;
				if (!b.providesWalls && (b.navalPower > 0 || b.navalDefenseBonus != null) && IsBombardableBuilding(city, cb)) {
					return cb;
				}
			}
			return null;
		}

		// The walls a land bombardment has to break first, or null. Only a
		// town's walls shield it: "If the walls win, then all improvements,
		// citizens, and units in the city are safe." / "This happens only if
		// the city is size 6 or smaller." (alexman,
		// https://codehappy.net/apolyton/threads/84569-1.htm, #1); in C3C 1.22,
		// "If there is are no city walls or the settlement has already reached
		// city level, then land units are hit." (justanick,
		// https://forums.civfanatics.com/threads/bombardment-of-settlements-in-c3c-1-22.647207/, #1).
		// In a bigger city the walls are an ordinary building (see
		// BombardCity). Only walls built in the city can be knocked down:
		// per the project owner, walls a wonder provides (e.g. the Great
		// Wall) can't be bombarded (see IsBombardableBuilding); a town with
		// those is immune to land bombardment (see FindWonderWalls).
		private static CityBuilding FindDestructibleWalls(City city) {
			if (city.residents.Count > EngineStorage.gameData.rules.MaximumLevel1CitySize) {
				return null;
			}
			foreach (CityBuilding cb in city.constructed_buildings) {
				if (cb.building.providesWalls && IsBombardableBuilding(city, cb)) {
					return cb;
				}
			}
			return null;
		}

		// The walls a wonder (the Great Wall) gives a town, or null, when a
		// land bombardment can't get past them. The project owner chose to
		// keep a C3C bug here (owner's decision, 2026-10-21): "In a town with
		// "Great Wall - induced" walls, all buildings and units" / "are
		// immune to bombardment." / "(Because the wall is always hit first,
		// and if it gets destroyed, the Great Wall immediately "resurrects"
		// it...)" (Lanzelot, C3C 1.22,
		// https://forums.civfanatics.com/threads/bombardment-of-settlements-in-c3c-1-22.647207/, #13);
		// "It seems the walls are immediately restored." (jarred!, same
		// thread, page 2). Walls shield only a town (see
		// FindDestructibleWalls), and only against land bombardment, the
		// one kind that hits walls first (compendium 01, "Great Wall walls
		// make a town immune to land bombard"); ships, aircraft and cruise
		// missiles hit a city in their own order (see BombardCityInOrder).
		// The compendium also records C3X-thread reports of the bug hitting
		// another building instead; the owner chose Lanzelot's form.
		private static Building FindWonderWalls(City city) {
			if (city.residents.Count > EngineStorage.gameData.rules.MaximumLevel1CitySize) {
				return null;
			}
			foreach (CityBuilding cb in city.EffectiveBuildings()) {
				if (cb.building.providesWalls && city.IsProvidedByWonders(cb.building)) {
					return cb.building;
				}
			}
			return null;
		}

		// Whether bombardment can destroy the building: anything built in the
		// city except wonders and the palace. Per the project owner, a
		// building a wonder provides can't be bombarded either: those granted
		// by the wonder aren't stored in the city, and one also built there
		// is left alone, so the wonder's effect stays.
		//
		// Great wonders can never be bombarded away (owner's decision,
		// 2026-10-21). The sources disagree: justanick's C3C 1.22 tests say
		// they can be until the owner reaches the Industrial Age ("If the
		// bombardement occurs against a settlement of a civilization that has
		// reached the industrial age," / "then any world wonder in this
		// settlement is immune against any bombardment.",
		// https://forums.civfanatics.com/threads/bombardment-of-settlements-in-c3c-1-22.647207/, #1);
		// Flintlock, reading the code, says "only in the ancient era"
		// (https://forums.civfanatics.com/threads/c3x-exe-mod-including-bug-fixes-stack-bombard-and-much-more.666881/post-16725029);
		// Dominus Romae says never, "At least in PTW 1.27f and C3C 1.22"
		// (https://forums.civfanatics.com/threads/128868/, #9). The owner
		// chose never.
		private static bool IsBombardableBuilding(City city, CityBuilding cb) {
			if (city.IsProvidedByWonders(cb.building)) {
				return false;
			}
			return IsBombardableBuilding(cb);
		}

		// Whether the building is a Civil Defense: one that adds to its
		// units' defense at any size, without being walls. When bombardment
		// hits an ordinary building, it hits the Civil Defense instead: "If a
		// regular building(meaning neither a coastal fortress nor city walls)
		// is hit and a civil defence is in the settlement," / "then it is the
		// civil defence that is hit." (justanick, C3C 1.22,
		// https://forums.civfanatics.com/threads/bombardment-of-settlements-in-c3c-1-22.647207/, #1).
		private static bool IsCivilDefense(Building b) {
			return b.combatDefenseBonus != null && !b.providesWalls && !b.onlyUsefulInTowns
				&& b.bombardDefense == 0 && !b.isSmallWonder && !b.IsGreatWonder() && !b.isCenterOfEmpire;
		}

		// Whether the building is walls or a coastal fortress, the two that
		// bombardment hits for themselves rather than through a Civil
		// Defense (see IsCivilDefense).
		private static bool IsWallsOrCoastalFortress(Building b) {
			return b.providesWalls || b.bombardDefense > 0 || b.navalPower > 0 || b.navalDefenseBonus != null;
		}

		// The building a bombardment hit on the chosen building destroys: the
		// city's Civil Defense, if it has one that can be hit and the chosen
		// one is an ordinary building, else the chosen one.
		private static CityBuilding BuildingHitBy(CityBuilding chosen, List<CityBuilding> hittable) {
			if (IsWallsOrCoastalFortress(chosen.building)) {
				return chosen;
			}
			foreach (CityBuilding cb in hittable) {
				if (IsCivilDefense(cb.building)) {
					return cb;
				}
			}
			return chosen;
		}

		// Whether the building can be destroyed at all, by bombardment or a
		// nuclear strike: anything but wonders and the palace.
		private static bool IsBombardableBuilding(CityBuilding cb) {
			Building b = cb.building;
			return !b.isCenterOfEmpire && !b.isSmallWonder && !b.IsGreatWonder();
		}

		// Bombards the walls or coastal fortress that shields the city (see
		// BombardCityInOrder). It defends with the BIQ's bombard defense (8
		// for both in the standard rules, see Building.bombardDefense): "When
		// you use a ground unit to bombard a city with walls, you have to
		// first destroy the walls (by damaging them as if they were a unit
		// with a defensive strength of 8) before you can harm anything else"
		// (alexman, https://codehappy.net/apolyton/threads/84569-1.htm, #25);
		// naval bombardment works the same way with a coastal fortress (#1).
		private async Task BombardCityDefence(Tile tile, CityBuilding walls) {
			int wallDefence = walls.building.bombardDefense > 0 ? walls.building.bombardDefense : Building.DefaultWallBombardDefense;

			var hitCount = 0;

			double bombardStrength  = StrengthVersus(null, CombatRole.Bombard, facingDirection);
			double defenderStrength = wallDefence;
			double attackerOdds = bombardStrength / (bombardStrength + defenderStrength);
			if (Double.IsNaN(attackerOdds))
				return;

			// Each shot of the unit's rate of fire is rolled on its own (see
			// RateOfFire). Once the defence is down there is nothing left for
			// a shot at it to hit, so the bombardment ends there.
			await BeginBombard();
			for (int shot = 0; shot < RateOfFire() && tile.cityAtTile.constructed_buildings.Contains(walls); ++shot) {
				if (await FireShot(tile, attackerOdds)) {
					hitCount += 1;
					tile.cityAtTile.RemoveBuilding(walls);
				}
			}

			TriggerPopUp(hitCount, tile, $"The {walls.building.name} of {tile.cityAtTile.name} has been destroyed!");
		}

		// A land bombardment of a town shielded by a wonder's walls (see
		// FindWonderWalls): its shots go at the walls, which are back at
		// once, so nothing in the town is harmed.
		private async Task BombardWonderWalls(Tile tile, Building walls) {
			int wallDefence = walls.bombardDefense > 0 ? walls.bombardDefense : Building.DefaultWallBombardDefense;
			double bombardStrength = StrengthVersus(null, CombatRole.Bombard, facingDirection);
			double attackerOdds = bombardStrength / (bombardStrength + wallDefence);
			if (Double.IsNaN(attackerOdds))
				return;

			await BeginBombard();
			for (int shot = 0; shot < RateOfFire(); ++shot) {
				await FireShot(tile, attackerOdds);
			}
			TriggerPopUp(0, tile, "");
		}

		private async Task BombardUnits(Tile tile, MapUnit target) {
			// TODO: Make configurable

			movementPoints.onUnitMove(1);
			await animateAsync(AnimatedAction.ATTACK1);

			// TODO: Figure out the bombard defense that walls grant.
			double bombardStrength  = StrengthVersus(target, CombatRole.Bombard, facingDirection);
			double defenderStrength = target.StrengthVersus(this, CombatRole.BombardDefense, facingDirection);
			double attackerOdds = bombardStrength / (bombardStrength + defenderStrength);
			if (Double.IsNaN(attackerOdds))
				return;

			var tries = 0;
			var hitCount = 0;

			// Per the project owner, bombardment that isn't lethal against
			// the target can only bring it down to its last hit point;
			// lethal bombardment stops once the target is dead.
			bool lethal = IsBombardmentLethalAgainst(target);
			while (tries < unitType.rateOfFire) {
				tries++;
				if (target.CompositeHitPoints() - hitCount <= (lethal ? 0 : 1))
					break;

				var r = GameData.rng.NextDouble();
				if (r < attackerOdds) {
					hitCount++;
				}
			}

			bool targetDestroyed = false;
			bool wasAboveOneHitPoint = target.CompositeHitPoints() > 1;
			if (hitCount > 0) {
				for (int i = 0; i < hitCount && !targetDestroyed; ++i) {
					targetDestroyed = target.AbsorbBombardHit(lethal);
					await tile.AnimateAsync(hitList[GameData.rng.Next(0, hitList.Length)]);
				}

			} else
				await tile.AnimateAsync(tile.IsWater() ? AnimatedEffect.WaterMiss : AnimatedEffect.Miss);

			if (targetDestroyed) {
				RollToPromote(target);
				// A kill by bombardment can enslave, as one in combat does
				// (see RollToEnslave).
				RollToEnslave(target);
				await target.animateAsync(AnimatedAction.DEATH, AnimationEnding.Pause);
				target.RemoveFromPlay();
				// Target destroyed, skip remaining fire -- TODO: Re-target?
			} else if (wasAboveOneHitPoint && target.CompositeHitPoints() == 1) {
				target.owner.AddWarWeariness(owner, Player.WarWearinessForUnitBombardedToOneHitPoint);
			}

			TriggerPopUp(hitCount, tile, "Artillery bombardment successful! Enemy units injured.",
				"Bombing run successful! Enemy units injured.");
		}

		// Once a city has nothing left to hit in its way (see
		// BombardCityInOrder), its buildings and citizens are hit with the
		// same probability: "then citizens or buildings are hit," /
		// "apparently with same probability." (justanick, C3C 1.22,
		// https://forums.civfanatics.com/threads/bombardment-of-settlements-in-c3c-1-22.647207/, #1).
		// They defend with the rules' building and citizen defensive bonuses
		// (see Rules.BuildingDefensiveBonus).
		private const float BuildingOrPopulationOdds = 0.5f;

		// A precision strike (see MapUnit_Air.cs) hits only the city's
		// buildings, or its citizens if it has none to hit. Otherwise the
		// shots hit what `allowed` lets them (see BombardUnitOrCity).
		private async Task BombardCity(Tile tile, bool precision = false, CityHit allowed = CityHit.Either) {
			City city = tile.cityAtTile;
			Rules rules = EngineStorage.gameData.rules;
			double bombardStrength = StrengthVersus(null, CombatRole.Bombard, facingDirection);
			var hitCount = 0;
			List<string> destroyMsgs = new();

			// Each shot of the unit's rate of fire (see RateOfFire) picks its
			// own target, as a single shot would: the 50/50 roll between
			// buildings and citizens is made again, among what is still left
			// to hit. A precision strike stays a single shot, as before rate
			// of fire applied here: UNVERIFIED (no Civ3 source found) whether
			// it fires the aircraft's rate of fire, and if so whether later
			// shots go on to the citizens once the buildings are gone.
			int shots = precision ? 1 : RateOfFire();
			await BeginBombard();
			for (int shot = 0; shot < shots; ++shot) {
				// Only buildings actually built in the city can be destroyed,
				// and not small wonders, the palace or what a wonder provides
				// (see IsBombardableBuilding). Walls are among them when a ship
				// or plane bombards, or the city is past town size.
				// TODO: probably not canon to exclude palace
				List<CityBuilding> eligibleBuildingsForBombardment = BombardableBuildings(city);

				// What can't be hit isn't rolled for, so a size 1 city only
				// loses buildings (see CanBombardCitizens), and a city with
				// neither buildings to lose nor citizens to spare takes the
				// shot for nothing. Later shots with nothing left to hit aren't
				// fired.
				bool canHitBuildings = eligibleBuildingsForBombardment.Count > 0 && (precision || allowed.HasFlag(CityHit.Buildings));
				bool canHitCitizens = CanBombardCitizens(city) && (precision || allowed.HasFlag(CityHit.Citizens));
				if (!canHitBuildings && !canHitCitizens) {
					if (shot == 0) {
						await FireShot(tile, 0);
					}
					break;
				}
				bool targetBuildings = canHitBuildings && canHitCitizens && !precision
					? GameData.rng.NextDouble() < BuildingOrPopulationOdds
					: canHitBuildings;

				double defenderStrength = targetBuildings ? rules.BuildingDefensiveBonus : rules.CitizenDefensiveBonus;
				double attackerOdds = bombardStrength / (bombardStrength + defenderStrength);
				if (Double.IsNaN(attackerOdds))
					break;

				if (!await FireShot(tile, attackerOdds))
					continue;
				hitCount += 1;
				if (targetBuildings) {
					var building = BuildingHitBy(eligibleBuildingsForBombardment
						.OrderBy(x => GameData.rng.Next()).First(), eligibleBuildingsForBombardment);
					city.RemoveBuilding(building);
					destroyMsgs.Add($"The {building.building.name} of {city.name} has been destroyed!");
				} else {
					city.RemoveRandomCitizen();
					string msg = $"Some of {city.name}'s citizens have been killed!";
					if (!destroyMsgs.Contains(msg))
						destroyMsgs.Add(msg);
				}
			}

			TriggerPopUp(hitCount, tile, string.Join(" ", destroyMsgs));
		}

		// Tile improvements defend against bombardment as Conquests 1.22
		// does, per the project owner ("Follow Civ3", owner's decision,
		// 2026-10-20, replacing the earlier ruling of a fixed defence of 3).
		// "In the base game, tile defense is hardcoded at 16." (the C3X mod's
		// notes, https://github.com/maxpetul/C3X/blob/master/default.c3x_config.ini).
		// Flintlock, reading the 1.22 code: "the chance to do damage on each
		// roll equals 1 - X/(S + X)", with S the bombard strength and X =
		// "floor((100 + D) * 16 / 100)", D the tile's defence in per cent,
		// and "The chance to do damage depends only on the tile's terrain,
		// whether it has a fortress or barricade" (C3X thread,
		// https://forums.civfanatics.com/threads/c3x-exe-mod-including-bug-fixes-stack-bombard-and-much-more.666881/post-16584231
		// and post-16583488). C3X's re-implementation keeps "the game's
		// integer rounding and 1..1023 threshold": the defender's share is
		// clamp(1, 1023, 1024 * X / (X + S)) out of 1024
		// (https://github.com/maxpetul/C3X/blob/master/injected_code.c,
		// get_tile_bombard_defense_threshold).
		internal const int TileBombardDefence = 16;

		// The chance that one roll of a bombardment damages the tile.
		internal static double TileBombardHitChance(double bombardStrength, int defencePercent) {
			int x = (int)Math.Floor((100 + defencePercent) * TileBombardDefence / 100.0);
			if (bombardStrength + x <= 0) {
				return 0;
			}
			int threshold = Math.Clamp((int)(1024 * x / (x + bombardStrength)), 1, 1023);
			return (1024 - threshold) / 1024.0;
		}

		// The tile's defence against bombardment, in per cent: its terrain's
		// and its fortress's or barricade's (see TileBombardDefence).
		internal static int TileBombardDefencePercent(Tile tile) {
			double bonus = tile.overlayTerrainType?.defenseBonus.amount ?? 0;
			foreach (StrengthBonus sb in tile.overlays.GetDefenseBonuses()) {
				bonus += sb.amount;
			}
			return (int)Math.Round(bonus * 100);
		}

		// The bombarder rolls up to its rate of fire times, and the first
		// roll that succeeds damages the tile, once: (paraphrase of
		// Flintlock, post-16584231) "The attacker gets to roll multiple
		// times up to its rate of fire and if it succeeds on any roll then
		// the tile is damaged", as in C3X's patch_Fighter_roll_for_bombard,
		// which answers the whole bombardment with one success or failure.
		// The improvement hit is one of those on the tile; a railroad goes
		// back to a road.
		private async Task BombardTileImprovements(Tile tile) {
			var hitCount = 0;
			List<string> destroyed = new();

			double bombardStrength = StrengthVersus(null, CombatRole.Bombard, facingDirection);
			double attackerOdds = TileBombardHitChance(bombardStrength, TileBombardDefencePercent(tile));
			if (Double.IsNaN(attackerOdds))
				return;

			await BeginBombard();
			for (int roll = 0; roll < RateOfFire(); ++roll) {
				if (!await FireShot(tile, attackerOdds))
					continue;
				hitCount += 1;
				var improvement = tile.overlays.GetManMadeImprovements()
					.OrderBy(x => GameData.rng.Next()).FirstOrDefault();
				if (improvement != null) {
					// Remove top improvement
					tile.overlays.Remove(improvement);
					// "Replace" with downgraded improvement if it exists
					tile.overlays.Add(improvement.upgradesFrom);
					tile.OwningPlayer()?.AddWarWeariness(owner, Player.WarWearinessForPillagedOrBombedImprovement);
					destroyed.Add(improvement.key);
				}
				break;
			}

			TriggerPopUp(hitCount, tile, $"Artillery bombardment successful! Destroyed {string.Join(", ", destroyed)}.",
				"Bombing run successful--improvements destroyed.");
		}

		// How many shots the unit fires in one bombardment. The BIQ's rate of
		// fire is "how many times a bombarding unit will attack per turn"
		// (Civ3 editor help, Rate of Fire), and each shot is rolled on its
		// own: "You fire twice. Each shot is tallied up individually"
		// (warpstorm, https://forums.civfanatics.com/threads/bombard-question.19648).
		// At least one, so a unit without a rate of fire still bombards once.
		private int RateOfFire() {
			return Math.Max(1, unitType.rateOfFire);
		}

		// The start of a bombardment: the unit's attack animation, and the
		// move it costs, once however many shots it fires.
		private async Task BeginBombard() {
			await animateAsync(AnimatedAction.ATTACK1);
			movementPoints.onUnitMove(1);
		}

		// One shot, a hit with the given odds. Returns whether it hit.
		private async Task<bool> FireShot(Tile tile, double attackerOdds) {
			if (GameData.rng.NextDouble() < attackerOdds) {
				await tile.AnimateAsync(hitList[GameData.rng.Next(0, hitList.Length)]);
				return true;
			}
			await tile.AnimateAsync(tile.IsWater() ? AnimatedEffect.WaterMiss : AnimatedEffect.Miss);
			return false;
		}

		// Tells the player how the bombardment went. An aircraft's bombing
		// run has Civ3's own wording (script.txt): "Our bombing run failed."
		// (BOMBFAILED), "Bombing run successful! Enemy units injured."
		// (UNITBOMBSUCEEDED) and "Bombing run successful--improvements
		// destroyed." (BOMBSUCCEEDED), given as airSuccessMessage; what a
		// run does to a city is told as for any bombardment.
		private void TriggerPopUp(int hitCount, Tile tile, string successMessage, string airSuccessMessage = null) {
			if (owner.isHuman) {
				bool air = IsAirUnit();
				if (hitCount > 0)
					new MsgShowTemporaryPopup(air ? airSuccessMessage ?? successMessage : successMessage, tile).send();
				else
					new MsgShowTemporaryPopup(air ? "Our bombing run failed." : "Artillery bombardment failed.", tile).send();
			}
		}

	}
}
