using System.Linq;
using C7Engine.AI;

namespace C7Engine {
	using System;
	using System.Collections.Generic;
	using C7GameData;

	public class CityInteractions {
		private static Serilog.ILogger log = Serilog.Log.ForContext<CityInteractions>();

		// Sells one of the city's improvements (see City.SellBuilding),
		// returning the gold it brought, or -1 if it couldn't be sold.
		public static int SellBuilding(City city, Building building) {
			int gold = city.SellBuilding(building);
			if (gold >= 0) {
				log.Information("{Player} sold the {Building} in {City} for {Gold} gold", city.owner, building.name, city, gold);
			}
			return gold;
		}

		public static City BuildCity(Tile tileWithNewCity, Player owner, string name) {
			GameData gameData = EngineStorage.gameData;

			City newCity = new City(tileWithNewCity, owner, name, gameData.ids.CreateID("city"));
			// A civ without a capital gets its palace in the next city it
			// founds, even if it already holds captured cities (which lose
			// their palace on capture).
			if (!owner.cities.Any(c => c.IsCapital())) {
				newCity.capital = true;
				newCity.AddBuilding(gameData.Buildings.Find(x => x.isCenterOfEmpire));
			}
			gameData.cities.Add(newCity);
			owner.cities.Add(newCity);
			owner.citiesFounded++;
			owner.neverHadCityOrSettler = false;
			tileWithNewCity.cityAtTile = newCity;
			// Civ3's City_Init: a new city can't flip for 10 turns, nor can
			// other civs' cities near it.
			CultureFlip.SetFlipCooldown(gameData, newCity, CultureFlip.Cooldown);
			// Building the city may clear the terrain, which changes what units
			// nearby can see.
			TileChangeJournal.RecordTerrainChange(tileWithNewCity);

			CityResident firstResident = new CityResident();
			firstResident.city = newCity;
			firstResident.nationality = owner.civilization;
			firstResident.citizenType = gameData.citizenTypes.Find(x => x.IsDefaultCitizen);
			newCity.AddCitizen(firstResident);

			// Update owners before we assign the citizen so the tile owners are
			// accurate. We do this after adding the resident though, because
			// cities with zero residents are considered destroyed.
			gameData.UpdateTileOwners();
			// After its borders, as Civ3 records them (Civ3 FUN_004b5280).
			gameData.gameHistory.Record(gameData.turn, ReplayEventType.CityFounded, owner, tileWithNewCity, name);

			// Goody huts next to the new city are opened for its owner.
			GoodyHuts.OpenNextToNewCity(gameData, newCity);

			// Barbarian camps inside the new city's borders are dispersed.
			int campsDispersed = 0;
			foreach (Tile camp in gameData.map.barbarianCamps.ToList()) {
				if (camp.owningCity == newCity && BarbarianInteractions.DisperseCamp(gameData, camp, owner)) {
					++campsDispersed;
				}
			}
			if (campsDispersed > 0 && owner.IsToldNews) {
				string camps = campsDispersed == 1 ? "a barbarian encampment" : $"{campsDispersed} barbarian encampments";
				new MsgShowMilitaryAdvisorPopup(owner, $"Our new city {name} dispersed {camps} and earned {campsDispersed * BarbarianInteractions.CampDispersalGold} gold!", happy: true).send();
			}

			// Now that the city exists and its borders have been established,
			// invalidate the trade network so it can be recomputed with this
			// new information.
			gameData.InvalidateCachedTradeNetwork();

			// Assigning citizens to tiles requires knowing luxuries, so this
			// has to happen after invalidating the trade network.
			CityTileAssignmentAI.AssignNewCitizenToTile(gameData, firstResident);

			// A scenario may found cities bigger (Rules.NewCitySize).
			for (int size = 1; size < (gameData.rules?.NewCitySize ?? 1); size++) {
				CityResident resident = new() {
					city = newCity,
					nationality = owner.civilization,
					citizenType = firstResident.citizenType,
				};
				newCity.AddCitizen(resident);
				CityTileAssignmentAI.AssignNewCitizenToTile(gameData, resident);
			}

			newCity.SetItemBeingProduced(ChooseProducible.Choose(newCity, owner));

			// Redo corruption calculations after a city is created, since it
			// may change rank corruption values.
			owner.DoCorruptionCalculations(gameData);

			return newCity;
		}

		// Reassigns a city's citizens after its owner clicks a tile on the city
		// screen: a worked tile's citizen becomes a specialist, an unworked tile
		// takes the citizen on the worst tile, and the city center reassigns
		// everyone.
		public static void ReassignCitizen(GameData gameData, City city, Tile tile) {
			// We only support clicking on workable tiles or the city center.
			if (!city.GetWorkableTiles().Contains(tile) && tile != city.location) {
				return;
			}

			// We can't assign citizens to other cities.
			if (tile.cityAtTile != null && tile.cityAtTile != city) {
				return;
			}

			// We can't assign citizens to tiles worked by other cities.
			if (tile.personWorkingTile != null && tile.personWorkingTile.city != city) {
				return;
			}

			CitizenType defaultCitizen = gameData.citizenTypes.Find(x => x.IsDefaultCitizen);

			if (tile.personWorkingTile != null && tile.personWorkingTile.city == city && !city.IsResisting(tile.personWorkingTile)) {
				// If we're already working a tile and click on it, turn the
				// citizen into a specialist.
				CityResident resident = tile.personWorkingTile;
				tile.personWorkingTile = null;
				resident.tileWorked = Tile.NONE;
				resident.citizenType = city.owner.GetKnownSpecialists(gameData)[0];
			} else if (tile.cityAtTile == null) {
				// We've clicked on an unworked tile, move the "worst" citizen to
				// that tile.
				int worstYield = int.MaxValue;
				CityResident worst = null;

				// Resisters refuse to work, so aren't moved onto it.
				foreach (CityResident cr in city.WorkingResidents()) {
					int tileYield = cr.tileWorked.FoodYield(city.owner).yield +
									cr.tileWorked.ProductionYield(city.owner).yield +
									cr.tileWorked.CommerceYield(city.owner).yield;
					if (tileYield < worstYield) {
						worstYield = tileYield;
						worst = cr;
					}
				}

				if (worst == null) {
					return;
				}
				// Move the worst citizen to our new tile, being sure to update
				// backpointers from the tile.
				worst.tileWorked.personWorkingTile = null;
				worst.tileWorked = tile;
				tile.personWorkingTile = worst;
				worst.citizenType = defaultCitizen;
			} else {
				// We've clicked the city center, so re-assign all the citizens
				// using the basic AI, but specify that we want to manage moods by
				// using entertainers if necessary.
				ReassignAllCitizens(gameData, city);
			}

			city.RecalculateCitizenMoods(gameData);
		}

		// Takes every citizen of the city off its tile or specialty and
		// assigns them afresh, managing moods with entertainers if need be.
		// The citizens keep their nationalities.
		internal static void ReassignAllCitizens(GameData gameData, City city) {
			CitizenType defaultCitizen = gameData.citizenTypes.Find(x => x.IsDefaultCitizen);
			List<Civilization> nationalities = city.residents.Select(r => r.nationality ?? city.owner.civilization).ToList();
			// The resisters are the last citizens, and stay so (see
			// City.StartResistance).
			int resisters = city.resisters;
			Player resistanceFrom = city.resistanceFrom;
			bool foodIsWasted = CityTileAssignmentAI.FoodIsWasted(gameData, city);
			city.RemoveAllCitizens();

			// Nothing the assignments depend on changes while the citizens
			// are reassigned, apart from which tiles are worked, so the tile
			// yields can be shared between them.
			CityTileAssignmentAI.AssignmentContext context = new(gameData, city, foodIsWasted);
			for (int i = 0; i < nationalities.Count; ++i) {
				CityResident newResident = new() {
					citizenType = defaultCitizen,
					nationality = nationalities[i],
					city = city
				};
				city.AddCitizen(newResident);
				// Resisters refuse to work: no tile, and no specialty.
				if (i < nationalities.Count - resisters) {
					CityTileAssignmentAI.AssignNewCitizenToTile(gameData, newResident, manageMoods: true, context);
				}
			}
			city.resisters = resisters;
			city.resistanceFrom = resistanceFrom;
		}

		// The cities of the player a governor setting for the given city
		// applies to.
		public static List<City> CitiesInGovernorScope(Player player, City city, GovernorScope scope) {
			return scope switch {
				GovernorScope.AllCities => player.cities.ToList(),
				GovernorScope.ContinentalCities => player.cities.Where(c => c.location.continent == city.location.continent).ToList(),
				_ => [city],
			};
		}

		// The cities a rally point set for the given city applies to.
		public static List<City> CitiesInRallyScope(Player player, City city, bool continental) {
			return continental
				? player.cities.Where(c => c.location.continent == city.location.continent).ToList()
				: [city];
		}

		// Gives the city's governor these options and has it act on them at
		// once: a new emphasis places the citizens again, and a governor
		// managing moods sees to them now rather than when the turn ends.
		public static void SetGovernor(GameData gameData, City city, CityGovernor governor) {
			CityGovernor old = city.governor;
			city.governor = governor.Copy();
			bool emphasisChanged = old.emphasizeFood != governor.emphasizeFood
				|| old.emphasizeProduction != governor.emphasizeProduction
				|| old.emphasizeCommerce != governor.emphasizeCommerce;
			if (emphasisChanged && city.residents.Count > 0) {
				// This manages moods with entertainers whatever the setting.
				ReassignAllCitizens(gameData, city);
			}
			CityTileAssignmentAI.GovernMoods(gameData, city);
			city.RecalculateCitizenMoods(gameData);
		}

		// Changes a specialist to the next kind of specialist its owner knows.
		public static void CycleSpecialist(GameData gameData, City city, int residentIndex) {
			if (residentIndex < 0 || residentIndex >= city.residents.Count) {
				return;
			}
			CityResident resident = city.residents[residentIndex];
			// Resisters can't be made specialists.
			if (city.IsResisting(resident)) {
				return;
			}
			List<CitizenType> specialistTypes = city.owner.GetKnownSpecialists(gameData);
			int index = specialistTypes.FindIndex(x => x.Id == resident.citizenType.Id);
			if (index < 0) {
				return;
			}
			resident.citizenType = specialistTypes[(index + 1) % specialistTypes.Count];
			city.RecalculateCitizenMoods(gameData);
		}

		// The chance that each building without culture in a captured city
		// is destroyed in the fighting: on capture "all possible buildings
		// with a culture value > 0 are still destroyed, as well as 25% of
		// other buildings and any small wonder (as normal)" (C3C editor help,
		// Retain Culture on Capture).
		private const double BuildingLossChanceOnCapture = 0.25;

		// Whether a city is worth keeping when taken: "Capturing a
		// population 1 city that has no culture will automatically raze it"
		// (Civ3 1.29f readme). Any other city survives.
		public static bool SurvivesCapture(City city) {
			return city.residents.Count > 1 || city.GetCulture() > 0;
		}

		// The gold the captor takes from the old owner's treasury with this
		// city, before it shrinks. Flintlock, reading the 1.22 code
		// (https://forums.civfanatics.com/threads/c3x-exe-mod-including-bug-fixes-stack-bombard-and-much-more.666881/post-16726482):
		// "If it's the last city your opponent has, you get their entire
		// treasury"; otherwise the treasury divided by their city count times
		// a factor, "0.5 for cities of level 1 (towns), 0.75 for level 2, and
		// 1.0 for level 3 (metropolises)".
		public static int CaptureGold(City city, Player oldOwner) {
			if (oldOwner.gold <= 0) {
				return 0;
			}
			int cityCount = oldOwner.cities.Count;
			if (cityCount <= 1) {
				return oldOwner.gold;
			}
			Rules rules = oldOwner.rules;
			int quarters = city.residents.Count <= rules.MaximumLevel1CitySize ? 2
				: city.residents.Count <= rules.MaximumLevel2CitySize ? 3 : 4;
			return oldOwner.gold * quarters / (4 * cityCount);
		}

		// Hands a city over to the player who took it, unless it doesn't
		// survive capture, in which case it is destroyed. The city loses a
		// citizen (never its last), its production, its stored food and
		// some of its buildings, and the captor plunders part of the old
		// owner's treasury. Its borders fall back to the captor's own culture
		// there. A human captor is then asked whether to keep or raze it.
		public static void CaptureCity(City city, Player captor) {
			GameData gameData = EngineStorage.gameData;
			Player oldOwner = city.owner;
			Tile tile = city.location;

			// Losing the capital destroys the spaceship.
			if (city.capital) {
				SpaceRace.DestroySpaceship(oldOwner, captor);
			}
			InstantElimination.OnCityLost(oldOwner, captor);
			VictoryPoints.OnCityConquered(captor, city, gameData.victoryConditions);
			captor.citiesCaptured++;

			if (!SurvivesCapture(city)) {
				// No gold is taken from a city destroyed as it's taken.
				LossNews.TellCityLost(oldOwner, city, captor, gold: 0, razed: true, wonder: LossNews.GreatWonderIn(city));
				DestroyCity(city);
				return;
			}

			tile.DisbandNonDefendingUnits(oldOwner);

			int plunder = CaptureGold(city, oldOwner);
			oldOwner.gold -= plunder;
			captor.gold += plunder;

			if (city.residents.Count > 1) {
				city.RemoveCitizens(1);
			}

			// Kings & Nomads: a city-state taken comes down to
			// CityStates.CapturedSize, needs an aqueduct to grow past the
			// first size limit, and weakens the captor against the rest (see
			// MapUnit.BrethrenPenaltyAgainst).
			if (CityStateTrade.IsCityState(city) && !captor.IsIndependent) {
				if (city.residents.Count > CityStates.CapturedSize) {
					city.RemoveCitizens(city.residents.Count - CityStates.CapturedSize);
				}
				city.wasCityState = true;
				captor.cityStatesCaptured++;
			}

			// Citizens of unknown nationality (e.g. from older saves) are
			// taken to be the old owner's, so that they may resist.
			foreach (CityResident r in city.residents) {
				r.nationality ??= oldOwner.civilization;
			}

			// The palace, small wonders and buildings with culture don't
			// survive a change of owner, great wonders always do, and other
			// buildings may be destroyed (see BuildingLossChanceOnCapture).
			// A scenario may have some always kept
			// (Rules.BuildingsKeptOnCapture).
			foreach (CityBuilding cb in city.constructed_buildings.ToList()) {
				Building b = cb.building;
				if (gameData.rules?.BuildingsKeptOnCapture?.Contains(b.name) == true) {
					continue;
				}
				bool lost = b.isCenterOfEmpire || b.isSmallWonder
					|| (!b.IsGreatWonder() && (b.culturePerTurn > 0 || GameData.rng.NextDouble() < BuildingLossChanceOnCapture));
				if (lost) {
					city.RemoveBuilding(cb);
				}
			}

			city.capturedFrom = oldOwner.civilization;
			city.plunderedOnCapture = plunder;
			bool wasCapital = city.capital;
			city.capital = false;
			oldOwner.cities.Remove(city);
			captor.cities.Add(city);
			captor.neverHadCityOrSettler = false;
			city.owner = captor;
			city.perPlayerCulture.TryAdd(captor, 0);
			gameData.OnCityOwnerChanged(city);
			// Civ3's City_Transfer: a city taken by force may flip again
			// after one turn.
			CultureFlip.SetFlipCooldown(gameData, city, CultureFlip.ConquestCooldown);
			city.isInCivilDisorder = false;
			city.hurriedThisTurn = false;
			city.SetStoredShields(0);
			// With its shields gone, so are any from a cleared forest.
			city.receivedForestShields = false;
			// And its food: growth starts again from an empty food box
			// (owner's ruling, 2026-11-08).
			city.foodStored = 0;

			gameData.UpdateTileOwners();
			BarbarianInteractions.DisperseCampsWithinBorders(gameData);
			gameData.InvalidateCachedTradeNetwork();
			// Recorded for the captor (Civ3 FUN_00570bb0).
			gameData.gameHistory.Record(gameData.turn, ReplayEventType.CityCaptured, captor, tile, city.name);

			// Choosing production needs the trade network to know the new owner.
			city.SetItemBeingProduced(ChooseProducible.Choose(city, captor));
			city.ClearProductionQueue();

			log.Information("{Captor} captured {City} from {OldOwner}, plundering {Plunder} gold", captor, city, oldOwner, plunder);
			// One capture, which each side is also told its own way.
			NewsEvent capture = new();
			new MsgCityCaptured(city, oldOwner) { newsEvent = capture }.send();
			if (captor.IsToldNews) {
				new MsgShowMilitaryAdvisorPopup(captor, $"We have captured {city.name} and plundered {plunder} gold!", happy: true) { newsEvent = capture }.send();
			}
			// A captor can't raze what is now their only city.
			if (captor.isHuman && MayAbandon(captor, city, gameData)) {
				new MsgDisplayRazeCityPopup(captor, city).send();
			}
			// The great wonders survive the capture (see above).
			if (oldOwner.IsToldNews) {
				LossNews.TellCityLost(oldOwner, city, captor, plunder, razed: false, wonder: LossNews.GreatWonderIn(city), capture);
			}

			gameData.CheckForCivDestructionAndNotifyUi(oldOwner, captor);
			if (wasCapital) {
				MovePalaceAfterLosingCapital(oldOwner, tile);
			}

			oldOwner.DoCorruptionCalculations(gameData);
			captor.DoCorruptionCalculations(gameData);
		}

		// Hands a city over to another player peacefully, as when it
		// defects or is traded: unlike a capture it keeps its citizens and
		// buildings, except the palace and small wonders, which belong to the
		// old owner's empire. As in Civ3, the old owner's units in it join the
		// new owner along with the city.
		//
		// viaDeal is for a city given away in a deal (see
		// Player.ExecuteDeal), which per the project owner doesn't resist its
		// new owner. Its old owner keeps their units, which per the project
		// owner move to the old owner's nearest city (see
		// MapUnit.FindNearestOwnCity). With no other city for them, they
		// leave for the nearest free tile, and are lost if there's nowhere
		// for them to go (UNVERIFIED, no Civ3 source found).
		public static void TransferCity(City city, Player newOwner, bool viaDeal = false) {
			GameData gameData = EngineStorage.gameData;
			Player oldOwner = city.owner;
			Tile tile = city.location;

			List<MapUnit> oldOwnersUnits = tile.unitsOnTile.Where(u => u.owner == oldOwner).ToList();
			// Where the units given notice go is found while the city and the
			// land around it are still the old owner's, so their paths out
			// aren't blocked by the new owner's borders. Units loaded on
			// another (in an army or aboard a ship) go with it.
			Dictionary<MapUnit, City> destinations = new();
			if (viaDeal) {
				foreach (MapUnit unit in oldOwnersUnits) {
					if (unit.Carrier() == null) {
						destinations[unit] = unit.FindNearestOwnCity(except: city);
					}
				}
			} else {
				foreach (MapUnit unit in oldOwnersUnits) {
					gameData.CaptureUnit(unit, newOwner);
				}
			}

			foreach (CityBuilding cb in city.constructed_buildings.ToList()) {
				if (cb.building.isCenterOfEmpire || cb.building.isSmallWonder) {
					city.RemoveBuilding(cb);
				}
			}

			city.capturedFrom = null;
			bool wasCapital = city.capital;
			city.capital = false;
			oldOwner.cities.Remove(city);
			newOwner.cities.Add(city);
			newOwner.neverHadCityOrSettler = false;
			city.owner = newOwner;
			city.perPlayerCulture.TryAdd(newOwner, 0);
			gameData.OnCityOwnerChanged(city);
			// Civ3's City_Transfer: a city that flipped, was traded or was
			// given can't flip for 10 turns.
			CultureFlip.SetFlipCooldown(gameData, city, CultureFlip.Cooldown);
			city.isInCivilDisorder = false;
			city.hurriedThisTurn = false;

			gameData.UpdateTileOwners();
			BarbarianInteractions.DisperseCampsWithinBorders(gameData);
			gameData.InvalidateCachedTradeNetwork();

			if (viaDeal) {
				// Those carrying others first, so they take them along.
				foreach (MapUnit unit in oldOwnersUnits.OrderBy(u => destinations.ContainsKey(u) ? 0 : 1)) {
					// Units carried away with another are already gone.
					if (unit.location != tile) {
						continue;
					}
					if (!destinations.TryGetValue(unit, out City destination)) {
						destination = unit.FindNearestOwnCity(except: city);
					}
					if (destination != null && destination.owner == oldOwner) {
						unit.WithdrawToCity(destination);
					} else if (!unit.WithdrawToNearestFreeTile()) {
						gameData.RemoveUnit(unit);
					}
				}
			}

			city.SetItemBeingProduced(ChooseProducible.Choose(city, newOwner));
			city.ClearProductionQueue();

			log.Information("{City} changed hands from {OldOwner} to {NewOwner}{ViaDeal}", city, oldOwner, newOwner, viaDeal ? " in a deal" : "");
			new MsgCityCaptured(city, oldOwner).send();

			gameData.CheckForCivDestructionAndNotifyUi(oldOwner);
			if (wasCapital) {
				MovePalaceAfterLosingCapital(oldOwner, tile);
			}

			oldOwner.DoCorruptionCalculations(gameData);
			newOwner.DoCorruptionCalculations(gameData);
		}

		// Whether the player may abandon (or raze) their city: a city they
		// captured, which they are asked whether to keep, or any city of
		// theirs from its menu, which Civ3 offers: "right-click on
		// the cities you want to remove, and you'll get a pop-up menu that
		// has 'Abandon city' way down at the bottom of the menu"
		// (https://forums.civfanatics.com/threads/getting-rid-of-unwanted-cities.353125/).
		// A player may never abandon their only city, per the project owner.
		public static bool MayAbandon(Player player, City city, GameData gameData) {
			return WhyCannotAbandon(player, city) == null;
		}

		// Why the player may not abandon the city, for the UI to show, or
		// null if they may.
		public static string WhyCannotAbandon(Player player, City city) {
			if (city == null || city.owner != player) {
				return "It is not our city.";
			}
			if (player.cities.Count <= 1) {
				return "We cannot abandon our only city.";
			}
			return null;
		}

		private static void MovePalaceAfterLosingCapital(Player player, Tile oldCapitalLocation) {
			City newCapital = player.RelocatePalace(EngineStorage.gameData, oldCapitalLocation);
			if (newCapital != null && player.IsToldNews) {
				new MsgShowMilitaryAdvisorPopup(player, $"With our capital lost, the palace has been rebuilt in {newCapital.name}.", happy: false).send();
			}
		}

		// Whether the player may raze the city rather than just abandon it:
		// one they took by force this turn, which they are asked whether to
		// keep (see CaptureCity), and may abandon.
		public static bool MayRaze(Player player, City city, GameData gameData) {
			return city != null && city.owner == player && city.capturedFrom != null
				&& city.ownerChangedTurn == gameData.turn && MayAbandon(player, city, gameData);
		}

		// Razes a city just taken by force. Razing gives slave workers: one
		// for each citizen over the first, so a size N city gives N - 1
		// workers, of the nationality of the civ it was taken from, on the
		// city's tile, where the unit that took it stands (owner's decision,
		// 2026-10-21). Players report slaves from razing in C3C without a
		// rate: "A size 5 will get you two workers IIRC" (zerksees,
		// https://forums.civfanatics.com/threads/occupy-vs-raze.313823;
		// compendium 02, section 11). Returns the workers.
		public static List<MapUnit> RazeCity(City city) {
			GameData gameData = EngineStorage.gameData;
			Player razer = city.owner;
			Tile tile = city.location;
			int slaves = Math.Max(0, city.residents.Count - 1);
			Civilization nationality = city.capturedFrom
				?? city.residents.Select(r => r.nationality).Where(n => n != null)
					.GroupBy(n => n).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault()
				?? razer.civilization;
			log.Information("{Razer} razes {City}, taking {Slaves} slave workers", razer, city, slaves);
			// The civ it was taken from hears of it, with the gold taken with
			// it and the wonder lost.
			if (city.capturedFrom != null) {
				Player victim = gameData.players.FirstOrDefault(p => p.civilization == city.capturedFrom && p != razer);
				LossNews.TellCityLost(victim, city, razer, city.plunderedOnCapture, razed: true, wonder: LossNews.GreatWonderIn(city));
			}
			DestroyCity(city);

			List<MapUnit> workers = new();
			UnitPrototype workerType = SlaveWorkerType(gameData);
			if (workerType == null) {
				return workers;
			}
			for (int i = 0; i < slaves; i++) {
				MapUnit slave = gameData.SpawnUnit(razer, workerType, tile);
				slave.nationality = nationality;
				workers.Add(slave);
			}
			if (slaves > 0) {
				razer.tileKnowledge.RecomputeActiveTiles();
			}
			return workers;
		}

		// The unit a slave becomes: the rules' slave unit (the BIQ's
		// "Slave" unit, the Worker in the stock rules), or else a Worker.
		internal static UnitPrototype SlaveWorkerType(GameData gameData) {
			string name = gameData.rules?.SlaveUnitType;
			return gameData.unitPrototypes.FirstOrDefault(p => name != null && p.name == name)
				?? gameData.unitPrototypes.FirstOrDefault(p => p.name == "Worker")
				?? gameData.unitPrototypes.FirstOrDefault(p => p.isWorker);
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
			bool wasCapital = tile.cityAtTile.capital;
			if (wasCapital) {
				SpaceRace.DestroySpaceship(owner, null);
			}

			// TODO: this will get removed eventually, since we will be capturing non-combat units,
			// plus, it doesn't what it says, if the city is abandoned for example, ALL units are removed.
			// I am leaving it as it is for the moment.
			tile.DisbandNonDefendingUnits(owner);

			City city = tile.cityAtTile;
			city.RemoveAllCitizens();
			owner.cities.Remove(city);
			gameData.cities.Remove(city);

			// Clear the tile before updating tile owners, which brings every
			// player's active tiles up to date: units on the tile see less
			// without the city.
			tile.cityAtTile = null;
			TileChangeJournal.RecordTerrainChange(tile);

			gameData.UpdateTileOwnersOnCityDestruction(city);
			BarbarianInteractions.DisperseCampsWithinBorders(gameData);
			// Civ3 FUN_004b5c80 tells apart a city whose owner still holds its
			// tile afterwards.
			ReplayEventType destroyed = tile.owningCity?.owner == owner ? ReplayEventType.CityDestroyedOwnerKeepsTile : ReplayEventType.CityDestroyed;
			gameData.gameHistory.Record(gameData.turn, destroyed, owner, tile, city.name);

			new MsgCityDestroyed(city).send();

			gameData.CheckForCivDestructionAndNotifyUi(owner);

			if (wasCapital) {
				MovePalaceAfterLosingCapital(owner, tile);
			}

			// Now that the city has been destroyed and tile owners updated,
			// invalidate the trade network in case removing this city cut off
			// resource access.
			gameData.InvalidateCachedTradeNetwork();

			// Redo corruption calculations after a city is destroyed, since it
			// may change rank corruption values.
			owner.DoCorruptionCalculations(gameData);
		}
	}
}
