using System.Collections.Generic;
using System.Linq;
using C7Engine.AI;
using Serilog;

namespace C7Engine {
	using System;
	using C7GameData;

	public class CityInteractions {
		private static ILogger log = Log.ForContext<CityInteractions>();

		public static City BuildCity(Tile tileWithNewCity, Player owner, string name) {
			GameData gameData = EngineStorage.gameData;
			City newCity = new City(tileWithNewCity, owner, name, gameData.ids.CreateID("city"));
			if (owner.cities.Count == 0) {
				newCity.capital = true;
				newCity.AddBuilding(gameData.Buildings.Find(x => x.isCenterOfEmpire));
			}
			gameData.cities.Add(newCity);
			owner.cities.Add(newCity);
			tileWithNewCity.cityAtTile = newCity;

			CityResident firstResident = new CityResident();
			firstResident.nationality = owner.civilization;
			firstResident.city = newCity;
			firstResident.citizenType = gameData.citizenTypes.Find(x => x.IsDefaultCitizen);
			newCity.AddCitizen(firstResident);

			// Update owners before we assign the citizen so the tile owners are
			// accurate. We do this after adding the resident though, because
			// cities with zero residents are considered destroyed.
			gameData.UpdateTileOwners();

			// Now that the city exists and its borders have been established,
			// invalidate the trade network so it can be recomputed with this
			// new information.
			gameData.InvalidateCachedTradeNetwork();

			// Assigning citizens to tiles requires knowing luxuries, so this
			// has to happen after invalidating the trade network.
			CityTileAssignmentAI.AssignNewCitizenToTile(gameData, firstResident);

			newCity.SetItemBeingProduced(ChooseProducible.Choose(newCity, owner));

			// Redo corruption calculations after a city is created, since it
			// may change rank corruption values.
			owner.DoCorruptionCalculations(gameData);

			return newCity;
		}

		public static void DestroyCity(City city) {
			DestroyCity(city.location);
		}
		public static void DestroyCity(Tile tile) {
			DestroyCity(tile.XCoordinate, tile.YCoordinate);
		}

		public static void DestroyCity(int X, int Y) {
			GameData gameData = EngineStorage.gameData;
			Tile tile = gameData.map.tileAt(X, Y);
			Player owner = tile.cityAtTile.owner;

			// Land units stay on the tile; ships and aircraft can't.
			tile.RemoveUnitsStrandedOnLand();

			tile.cityAtTile.RemoveAllCitizens();
			tile.cityAtTile.owner.cities.Remove(tile.cityAtTile);

			gameData.cities.Remove(tile.cityAtTile);
			gameData.UpdateTileOwnersOnCityDestruction(tile.cityAtTile);

			new MsgCityDestroyed(tile.cityAtTile).send();

			gameData.CheckForCivDestructionAndNotifyUi(owner);

			tile.cityAtTile = null;

			// Now that the city has been destroyed and tile owners updated,
			// invalidate the trade network in case removing this city cut off
			// resource access.
			gameData.InvalidateCachedTradeNetwork();

			// If that was the capital, the palace moves to another city.
			RelocateCapitalIfLost(owner, gameData.Buildings.Find(x => x.isCenterOfEmpire));

			// Redo corruption calculations after a city is destroyed, since it
			// may change rank corruption values.
			owner.DoCorruptionCalculations(gameData);
		}

		// What became of a city an enemy took.
		public enum CaptureOutcome {
			Captured,
			// A size 1 city with no culture: Civ3 destroys it instead.
			Destroyed,
			// The AI conqueror razed it.
			Razed,
		}

		// `conqueror` has moved into `city`, which nothing defends any more, and
		// takes it, as in Civ3:
		// - the conqueror "liberates" the city's share of the loser's gold;
		// - a citizen of the loser's nationality dies, or, in a size 1 city with
		//   no culture, the city is destroyed;
		// - the palace, small wonders and buildings that make culture are lost,
		//   other buildings may be, and great wonders change hands;
		// - the city's culture was its old owner's, so its borders shrink;
		// - if it was the loser's capital, the palace moves.
		// A human conqueror is then asked whether to keep or raze it
		// (MsgCityCaptured, answered with MsgRazeCity); the AI decides here.
		public static CaptureOutcome CaptureCity(City city, MapUnit conqueror) {
			GameData gameData = EngineStorage.gameData;
			Player loser = city.owner;
			Player winner = conqueror.owner;

			WarWeariness.OnCityLost(loser, winner, city.residents.Count);

			int gold = CaptureGold(city, winner);
			loser.gold -= gold;
			winner.gold += gold;

			if (city.residents.Count == 1) {
				// Unless its last citizen isn't one of the loser's own.
				if (city.GetCultureFor(loser) == 0 && city.residents[0].nationality == loser.civilization) {
					log.Information($"{winner} takes {city.name}, a size 1 city with no culture, and it's destroyed ({gold} gold taken)");
					DestroyCity(city);
					new MsgCityRazed(city, winner.id, loser.id, gold, [], 0, destroyedOnCapture: true).send();
					return CaptureOutcome.Destroyed;
				}
			} else {
				CityResident victim = city.residents.FirstOrDefault(r => r.nationality == loser.civilization);
				if (victim != null) {
					city.RemoveCitizen(victim);
				}
			}

			List<string> wonders = new();
			List<string> lost = new();
			foreach (CityBuilding cb in city.constructed_buildings.ToList()) {
				if (cb.building.IsGreatWonder()) {
					wonders.Add(cb.building.name);
				} else if (GameData.rng.Next(100) >= BuildingCaptureSurvivalChance(city, cb.building)) {
					lost.Add(cb.building.name);
					city.RemoveBuilding(cb);
				}
			}

			List<Tile> bordersBefore = city.GetTilesWithinBorders();
			loser.cities.Remove(city);
			winner.cities.Add(city);
			city.owner = winner;
			city.capital = false;
			city.perPlayerCulture.TryAdd(winner, 0);
			city.isInCivilDisorder = false;
			city.SetStoredShields(0);
			city.capturedOnTurn = gameData.turn;
			city.capturedFrom = loser;

			// Its citizens worked the loser's land; now they work the winner's,
			// unless they resist.
			CitizenType laborer = gameData.citizenTypes.Find(c => c.IsDefaultCitizen);
			foreach (CityResident resident in city.residents) {
				resident.tileWorked.personWorkingTile = null;
				resident.tileWorked = Tile.NONE;
				resident.citizenType = laborer;
			}
			gameData.UpdateTileOwnersOnCityCapture(city, bordersBefore);
			gameData.InvalidateCachedTradeNetwork();
			int resisters = Resistance.StartResisting(gameData, city);
			foreach (CityResident resident in city.residents.Where(r => !r.resisting)) {
				CityTileAssignmentAI.AssignNewCitizenToTile(gameData, resident);
			}
			city.SetItemBeingProduced(ChooseProducible.Choose(city, winner));

			RelocateCapitalIfLost(loser, gameData.Buildings.Find(x => x.isCenterOfEmpire));
			loser.DoCorruptionCalculations(gameData);
			winner.DoCorruptionCalculations(gameData);
			log.Information($"{winner} captures {city.name} from {loser}: {gold} gold taken, {resisters} resisters, buildings lost: {string.Join(", ", lost)}; wonders taken: {string.Join(", ", wonders)}");

			gameData.CheckForCivDestructionAndNotifyUi(loser);

			if (!winner.isHuman && AiRazesCapturedCity(city, winner)) {
				RazeCity(city, winner, gold);
				return CaptureOutcome.Razed;
			}
			new MsgCityCaptured(city, winner.id, loser.id, gold, wonders, resisters).send();
			return CaptureOutcome.Captured;
		}

		// Whether the AI razes a city it has just captured rather than keeping
		// it. For now it keeps them all.
		private static bool AiRazesCapturedCity(City city, Player ai) {
			return false;
		}

		// Razes a city its owner captured this turn, as Civ3 offers right after
		// a capture: the city is destroyed, with any great wonders in it, and its
		// owner gets slaves, one of the rules' slave units (workers) for every two
		// citizens left, with those citizens' nationality, foreigners first.
		// `goldTaken` is only for the message to the city's former owner.
		// Returns false, doing nothing, if the city can't be razed.
		public static bool RazeCity(City city, Player razer, int goldTaken = 0) {
			GameData gameData = EngineStorage.gameData;
			if (city.owner != razer || city.capturedOnTurn != gameData.turn) {
				log.Warning($"{razer} can't raze {city.name}: only a city captured this turn can be razed");
				return false;
			}

			Tile tile = city.location;
			List<Civilization> slaveNationalities = city.residents
				.Select(r => r.nationality ?? razer.civilization)
				.OrderBy(n => n == razer.civilization ? 1 : 0)
				.Take(city.residents.Count / 2)
				.ToList();
			List<string> wonders = city.constructed_buildings
				.Where(cb => cb.building.IsGreatWonder())
				.Select(cb => cb.building.name)
				.ToList();

			DestroyCity(city);

			UnitPrototype slave = gameData.unitPrototypes.Find(u => u.name == gameData.rules.SlaveUnitType);
			if (slave != null) {
				foreach (Civilization nationality in slaveNationalities) {
					MapUnit worker = gameData.SpawnUnit(razer, slave, tile, nationality);
					worker.movementPoints.onConsumeAll();
				}
			}
			log.Information($"{razer} razes {city.name}, taking {slaveNationalities.Count} slaves; wonders destroyed: {string.Join(", ", wonders)}");
			new MsgCityRazed(city, razer.id, city.capturedFrom?.id, goldTaken, wonders, slaveNationalities.Count).send();
			return true;
		}

		// The gold a conqueror takes from a city's owner, from the Lua behavior
		// gameplay.cities.capture_gold, kept between 0 and the owner's gold.
		private static int CaptureGold(City city, Player conqueror) {
			Player owner = city.owner;
			int ownerCitizens = owner.cities.Sum(c => c.residents.Count);
			Func<City, Player, int, int> captureGold = EngineStorage.gameData.luaBehaviorEngine.ImportFunc<Func<City, Player, int, int>>("gameplay.cities.capture_gold");
			return Math.Clamp(captureGold.Invoke(city, conqueror, ownerCitizens), 0, Math.Max(owner.gold, 0));
		}

		// The percent chance a building (not a great wonder) survives its city's
		// capture, from the Lua behavior gameplay.cities.building_capture_survival_chance.
		private static int BuildingCaptureSurvivalChance(City city, Building building) {
			Func<City, Building, int> chance = EngineStorage.gameData.luaBehaviorEngine.ImportFunc<Func<City, Building, int>>("gameplay.cities.building_capture_survival_chance");
			return Math.Clamp(chance.Invoke(city, building), 0, 100);
		}

		// How strongly a city claims to be the new capital when the old one is
		// lost, using Civ3's formula from
		// https://civfanatics.com/civ3/strategy/game-mechanics/free-palace-jump/:
		// 3 points per citizen of the owner's nationality, 1 per foreign citizen,
		// 1 per military unit in the city, and 1/2/3 points per neighboring
		// town/city/metropolis of the owner's within the rules'
		// CapitalNeighborDistance.
		public static int CapitalScore(Player owner, City city) {
			int score = 0;
			foreach (CityResident resident in city.residents) {
				score += resident.nationality == owner.civilization ? 3 : 1;
			}
			score += city.location.unitsOnTile.Count(u => u.owner == owner && u.IsCombatUnit());
			foreach (City neighbor in owner.cities) {
				if (neighbor != city && city.location.DistanceTo(neighbor.location) <= owner.rules.CapitalNeighborDistance) {
					score += neighbor.SizeLevel();
				}
			}
			return score;
		}

		// The city that becomes the capital when the old one is lost: the one
		// with the highest CapitalScore. Ties go to the city that comes first in
		// the owner's list of cities, which is founding order (as in Civ3, where
		// ties go to the first city in the database, usually the oldest). Null if
		// no cities are left.
		public static City ChooseNewCapital(Player owner) {
			City best = null;
			int bestScore = int.MinValue;
			foreach (City city in owner.cities) {
				int score = CapitalScore(owner, city);
				if (score > bestScore) {
					best = city;
					bestScore = score;
				}
			}
			return best;
		}

		// Moves the capital, and the palace, to the best remaining city when the
		// owner no longer has one (e.g. it was destroyed or abandoned). Any
		// Forbidden Palace in the new capital stays: its effects stack with the
		// capital's. Returns the new capital, or null if there was nothing to do.
		public static City RelocateCapitalIfLost(Player owner, Building palace) {
			if (owner.cities.Count == 0 || owner.cities.Any(c => c.IsCapital())) {
				return null;
			}
			City newCapital = ChooseNewCapital(owner);
			newCapital.capital = true;
			if (palace != null && !newCapital.constructed_buildings.Any(b => b.building == palace)) {
				newCapital.AddBuilding(palace);
			}
			log.Information($"{owner.civilization?.name} lost its capital; the palace moves to {newCapital.name}");
			return newCapital;
		}
	}
}
