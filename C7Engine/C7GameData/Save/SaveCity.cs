using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;

namespace C7GameData.Save {
	public class SaveCityResident {
		public ID citizenType;
		public string nationality;
		public ID city;
		public TileLocation tileWorked;
	}

	public class SaveCityBuilding {
		public string building;
		public ID builtByPlayer;
		public int year;
		public int totalCulture;
		public int turnsTowardFreeUnit;

		public SaveCityBuilding() { }

		public SaveCityBuilding(CityBuilding cityBuilding) {
			building = cityBuilding.building.name;
			builtByPlayer = cityBuilding.builtByPlayer.id;
			year = cityBuilding.year;
			totalCulture = cityBuilding.totalCulture;
			turnsTowardFreeUnit = cityBuilding.turnsTowardFreeUnit;
		}

		public CityBuilding ToCityBuilding(List<Building> buildings, List<Player> players) {
			return new CityBuilding {
				building = buildings.Find(buildingType => buildingType.name == building),
				builtByPlayer = players.Find(player => player.id == builtByPlayer),
				year = year,
				totalCulture = totalCulture,
				turnsTowardFreeUnit = turnsTowardFreeUnit,
			};
		}

		internal CityBuilding ToCityBuilding(SaveCity.Lookups lookups) {
			return new CityBuilding {
				building = SaveCity.Lookups.Find(lookups.buildingsByName, building),
				builtByPlayer = SaveCity.Lookups.Find(lookups.playersById, builtByPlayer),
				year = year,
				totalCulture = totalCulture,
				turnsTowardFreeUnit = turnsTowardFreeUnit,
			};
		}

	}

	public enum ProducibleType { INFLOW, BUILDING, UNIT };

	// An item in a city's production queue.
	public class SaveQueuedProducible {
		public string name;
		public ProducibleType type;
	}

	public class SaveCity : IHasID {
		public ID id { get; set; }
		public ID owner;
		public bool capital;
		public TileLocation location;
		public string producible;
		public ProducibleType producibleType;
		public string name;
		public int size;
		public Dictionary<string, int> perPlayerCulture = new();
		public int shieldsStored;
		public int foodStored;
		public int turnsOfUnhappinessDueToPopRushing;
		// Drafting: the turns of unhappiness left and the citizens drafted
		// this turn (see City.Draft). Older saves have 0.
		public int turnsOfUnhappinessDueToDrafting;
		public int unitsDraftedThisTurn;
		// A building was sold here this turn (see City.SellBuilding).
		public bool soldBuildingThisTurn;
		// Citizens confused by enemy propaganda (City.propagandaConfusedCitizens).
		public int propagandaConfusedCitizens;
		public bool celebrating;
		public bool isInCivilDisorder;
		// Resistance in a conquered city: the citizens resisting and the
		// player they're loyal to.
		public int resisters;
		public ID resistanceFrom;
		// The turn the city last changed hands; absent if it never has, or
		// in saves made before this was saved.
		public int? ownerChangedTurn;

		// The city's culture-flip cooldown (City.flipCooldown); absent if 0
		// and in older saves.
		public int? flipCooldown;
		// See City.wasCityState.
		public bool wasCityState;
		// The civilization the city was taken from by force (City.capturedFrom),
		// by name; absent if it wasn't, or in saves made before this was saved.
		public string capturedFrom;
		// A culture flip offered to a human who hasn't answered yet
		// (City.cultureFlipOfferedTo and cultureFlipOfferTurn); absent if
		// none.
		public ID cultureFlipOfferedTo;
		public int? cultureFlipOfferTurn;
		// Production was hurried this turn and can't be changed until it ends.
		public bool hurriedThisTurn;
		// The city was given a cleared forest's shields since it last
		// completed something, so it can't switch to a wonder. Older saves
		// lack it, and those cities may.
		public bool receivedForestShields;
		// The turn disease last struck the city (City.lastDiseaseTurn);
		// missing in older saves.
		public int? lastDiseaseTurn;
		// The city is ill and loses a citizen every turn until it recovers
		// (City.ill); false in older saves.
		public bool ill;
		// Older saves only: disease struck the city and was to take a second
		// citizen the next turn. Such a city loads as ill. Not written.
		public bool diseaseStrikesNextTurn;
		// The city's governor (see CityGovernor); all off in older saves.
		public bool governorManagesMoods;
		public bool governorEmphasizesFood;
		public bool governorEmphasizesProduction;
		public bool governorEmphasizesCommerce;
		// Its production options; off, and every kind "Sometimes", in older
		// saves.
		public bool governorManagesProduction;
		public bool governorNeverBuildsWonders;
		public bool governorNeverBuildsSmallWonders;
		public int governorProduceNever;
		public int governorProduceOften;
		// Where the city sends the units it builds (City.rallyPoint); none in
		// older saves.
		public TileLocation? rallyPoint;
		public List<SaveCityResident> residents = new List<SaveCityResident>();
		public List<SaveCityBuilding> buildings = [];
		public List<SaveQueuedProducible> productionQueue = [];

		public SaveCity() { }

		// A copy sharing everything with this one, for a LAN host to change
		// a little for one guest (see C7Engine.Network.SnapshotFilter).
		internal SaveCity ShallowCopy() {
			return (SaveCity)MemberwiseClone();
		}

		public SaveCity(City city) {
			id = city.id;
			owner = city.owner.id;
			capital = city.capital;
			location = new TileLocation(city.location);
			name = city.name;
			producible = city.itemBeingProduced.name;
			producibleType = TypeOf(city.itemBeingProduced);
			productionQueue = city.productionQueue.ConvertAll(p => new SaveQueuedProducible { name = p.name, type = TypeOf(p) });
			shieldsStored = city.shieldsStored;
			foodStored = city.foodStored;
			turnsOfUnhappinessDueToPopRushing = city.turnsOfUnhappinessDueToPopRushing;
			turnsOfUnhappinessDueToDrafting = city.turnsOfUnhappinessDueToDrafting;
			unitsDraftedThisTurn = city.unitsDraftedThisTurn;
			soldBuildingThisTurn = city.soldBuildingThisTurn;
			propagandaConfusedCitizens = city.propagandaConfusedCitizens;
			celebrating = city.celebrating;
			isInCivilDisorder = city.isInCivilDisorder;
			resisters = city.resisters;
			resistanceFrom = city.resistanceFrom?.id;
			ownerChangedTurn = city.ownerChangedTurn >= 0 ? city.ownerChangedTurn : null;
			flipCooldown = city.flipCooldown > 0 ? city.flipCooldown : null;
			wasCityState = city.wasCityState;
			capturedFrom = city.capturedFrom?.name;
			if (city.cultureFlipOfferedTo != null && city.cultureFlipOfferTurn >= 0) {
				cultureFlipOfferedTo = city.cultureFlipOfferedTo.id;
				cultureFlipOfferTurn = city.cultureFlipOfferTurn;
			}
			hurriedThisTurn = city.hurriedThisTurn;
			receivedForestShields = city.receivedForestShields;
			lastDiseaseTurn = city.lastDiseaseTurn;
			ill = city.ill;
			rallyPoint = city.rallyPoint == null ? null : new TileLocation(city.rallyPoint);
			governorManagesMoods = city.governor.manageMoods;
			governorEmphasizesFood = city.governor.emphasizeFood;
			governorEmphasizesProduction = city.governor.emphasizeProduction;
			governorEmphasizesCommerce = city.governor.emphasizeCommerce;
			governorManagesProduction = city.governor.manageProduction;
			governorNeverBuildsWonders = city.governor.neverBuildWonders;
			governorNeverBuildsSmallWonders = city.governor.neverBuildSmallWonders;
			governorProduceNever = (int)city.governor.produceNever;
			governorProduceOften = (int)city.governor.produceOften;
			residents = city.residents.ConvertAll(resident => {
				return new SaveCityResident {
					nationality = resident.nationality?.name,
					city = resident.city.id,
					tileWorked = new TileLocation(resident.tileWorked),
					citizenType = resident.citizenType.Id,
				};
			});
			buildings = city.constructed_buildings.ConvertAll(building => new SaveCityBuilding(building));

			foreach (KeyValuePair<Player, int> keyValuePair in city.perPlayerCulture) {
				perPlayerCulture.Add(keyValuePair.Key.id.ToString(), keyValuePair.Value);
			}
		}

		// Lookup tables for converting many cities, built once rather than
		// searching the lists for every city. Like List.Find, the first match
		// wins.
		internal class Lookups {
			internal readonly Dictionary<ID, Player> playersById = new();
			internal readonly Dictionary<string, Player> playersByIdString = new();
			internal readonly Dictionary<string, UnitPrototype> unitPrototypesByName = new();
			internal readonly Dictionary<string, Civilization> civilizationsByName = new();
			internal readonly Dictionary<string, Building> buildingsByName = new();
			internal readonly Dictionary<ID, CitizenType> citizenTypesById = new();
			internal readonly Dictionary<string, Inflow> inflowsByName = new();
			internal readonly CitizenType defaultCitizenType;

			internal Lookups(List<Player> players,
							List<UnitPrototype> unitPrototypes,
							List<Civilization> civilizations,
							List<Building> buildings,
							List<CitizenType> citizenTypes,
							List<Inflow> inflows) {
				foreach (Player p in players) {
					if (p.id is not null) {
						playersById.TryAdd(p.id, p);
						playersByIdString.TryAdd(p.id.ToString(), p);
					}
				}
				foreach (UnitPrototype up in unitPrototypes) {
					if (up.name != null) unitPrototypesByName.TryAdd(up.name, up);
				}
				foreach (Civilization civ in civilizations) {
					if (civ.name != null) civilizationsByName.TryAdd(civ.name, civ);
				}
				foreach (Building b in buildings) {
					if (b.name != null) buildingsByName.TryAdd(b.name, b);
				}
				foreach (CitizenType ct in citizenTypes) {
					if (ct.Id is not null) citizenTypesById.TryAdd(ct.Id, ct);
				}
				foreach (Inflow inflow in inflows) {
					if (inflow.name != null) inflowsByName.TryAdd(inflow.name, inflow);
				}
				defaultCitizenType = citizenTypes.Find(x => x.IsDefaultCitizen);
				fallbackProducible = inflows.Count > 0 ? inflows[0]
					: unitPrototypes.Count > 0 ? unitPrototypes[0]
					: null;
			}

			// What a city producing something unknown produces instead.
			internal readonly IProducible fallbackProducible;

			internal static T Find<K, T>(Dictionary<K, T> dict, K key) where T : class {
				return key is not null && dict.TryGetValue(key, out T value) ? value : null;
			}
		}

		internal static ProducibleType TypeOf(IProducible producible) {
			return producible switch {
				Inflow => ProducibleType.INFLOW,
				UnitPrototype => ProducibleType.UNIT,
				Building => ProducibleType.BUILDING,
				_ => throw new ArgumentException($"Unknown producible type {producible?.GetType()}", nameof(producible)),
			};
		}

		private static IProducible FindProducible(Lookups lookups, ProducibleType type, string name) {
			return type switch {
				ProducibleType.INFLOW => Lookups.Find(lookups.inflowsByName, name),
				ProducibleType.UNIT => Lookups.Find(lookups.unitPrototypesByName, name),
				ProducibleType.BUILDING => Lookups.Find(lookups.buildingsByName, name),
				_ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
			};
		}

		public City ToCity(GameMap gameMap,
							List<Player> players,
							List<UnitPrototype> unitPrototypes,
							List<Civilization> civilizations,
							List<Building> buildings,
							List<CitizenType> citizenTypes,
							List<Inflow> inflows) {
			return ToCity(gameMap, new Lookups(players, unitPrototypes, civilizations, buildings, citizenTypes, inflows));
		}

		internal City ToCity(GameMap gameMap, Lookups lookups) {
			Player cityOwner = Lookups.Find(lookups.playersById, owner)
				?? throw new KeyNotFoundException($"City {name} ({id}) is owned by unknown player {owner}");
			IProducible itemBeingProduced = FindProducible(lookups, producibleType, producible);
			if (itemBeingProduced == null) {
				itemBeingProduced = lookups.fallbackProducible
					?? throw new KeyNotFoundException($"City {name} ({id}) is producing unknown {producibleType} {producible}");
				Log.Warning("City {City} is producing unknown {Type} {Producible}; producing {Fallback} instead", name, producibleType, producible, itemBeingProduced.name);
			}
			City city = new City{
				id = id,
				location = gameMap.tileAt(location.X, location.Y),
				owner = cityOwner,
				name = name,
				itemBeingProduced = itemBeingProduced,
				foodStored = foodStored,
				turnsOfUnhappinessDueToPopRushing = turnsOfUnhappinessDueToPopRushing,
				turnsOfUnhappinessDueToDrafting = turnsOfUnhappinessDueToDrafting,
				unitsDraftedThisTurn = unitsDraftedThisTurn,
				soldBuildingThisTurn = soldBuildingThisTurn,
				propagandaConfusedCitizens = propagandaConfusedCitizens,
				celebrating = celebrating,
				isInCivilDisorder = isInCivilDisorder,
				resisters = resisters,
				resistanceFrom = Lookups.Find(lookups.playersById, resistanceFrom),
				ownerChangedTurn = ownerChangedTurn ?? -1,
				flipCooldown = flipCooldown ?? 0,
				wasCityState = wasCityState,
				// Saves made before this was saved still know a city-state a
				// civ took: it was taken from the city-states.
				capturedFrom = Lookups.Find(lookups.civilizationsByName, capturedFrom)
					?? (wasCityState ? lookups.playersById.Values.FirstOrDefault(p => p.isCityStates)?.civilization : null),
				cultureFlipOfferedTo = Lookups.Find(lookups.playersById, cultureFlipOfferedTo),
				cultureFlipOfferTurn = cultureFlipOfferTurn ?? -1,
				hurriedThisTurn = hurriedThisTurn,
				receivedForestShields = receivedForestShields,
				lastDiseaseTurn = lastDiseaseTurn ?? -1,
				ill = ill || diseaseStrikesNextTurn,
				governor = new CityGovernor {
					manageMoods = governorManagesMoods,
					emphasizeFood = governorEmphasizesFood,
					emphasizeProduction = governorEmphasizesProduction,
					emphasizeCommerce = governorEmphasizesCommerce,
					manageProduction = governorManagesProduction,
					neverBuildWonders = governorNeverBuildsWonders,
					neverBuildSmallWonders = governorNeverBuildsSmallWonders,
					produceNever = (ProductionKind)governorProduceNever,
					produceOften = (ProductionKind)governorProduceOften,
				},
				capital = capital,
				rallyPoint = rallyPoint is TileLocation rally ? gameMap.tileAt(rally.X, rally.Y) : null,
				constructed_buildings = [],
			};
			foreach (SaveCityBuilding building in this.buildings) {
				CityBuilding cityBuilding = building.ToCityBuilding(lookups);
				if (cityBuilding.building == null) {
					Log.Warning("City {City} has unknown building {Building}, which is left out", name, building.building);
					continue;
				}
				if (cityBuilding.builtByPlayer == null) {
					Log.Warning("The {Building} in {City} was built by unknown player {Player}; crediting the city's owner", building.building, name, building.builtByPlayer);
					cityBuilding.builtByPlayer = cityOwner;
				}
				city.constructed_buildings.Add(cityBuilding);
			}

			city.SetStoredShields(shieldsStored);
			foreach (SaveQueuedProducible queued in productionQueue ?? []) {
				IProducible item = FindProducible(lookups, queued.type, queued.name);
				if (item != null) {
					city.productionQueue.Add(item);
				} else {
					Log.Warning("City {City} has unknown {Type} {Producible} queued, which is left out", name, queued.type, queued.name);
				}
			}

			foreach (KeyValuePair<string, int> keyValuePair in perPlayerCulture) {
				Player player = Lookups.Find(lookups.playersByIdString, keyValuePair.Key);
				if (player == null) {
					Log.Warning("City {City} has culture from unknown player {Player}, which is left out", name, keyValuePair.Key);
					continue;
				}
				city.perPlayerCulture[player] = keyValuePair.Value;
			}

			city.residents = residents.ConvertAll(resident => {
				CitizenType citizenType = Lookups.Find(lookups.citizenTypesById, resident.citizenType);
				if (citizenType == null) {
					Log.Warning("A citizen of {City} is of unknown type {Type}; making them a worker", name, resident.citizenType);
					citizenType = lookups.defaultCitizenType;
				}
				// Older saves can lack a nationality, which is left unknown.
				Civilization nationality = Lookups.Find(lookups.civilizationsByName, resident.nationality);
				if (nationality == null && resident.nationality != null) {
					Log.Warning("A citizen of {City} has unknown nationality {Nationality}; giving them the owner's", name, resident.nationality);
					nationality = cityOwner.civilization;
				}
				return new CityResident {
					citizenType = citizenType,
					nationality = nationality,
					tileWorked = gameMap.tileAt(resident.tileWorked.X, resident.tileWorked.Y),
					city = city,
				};
			});

			// Fill in the back pointers. A citizen who isn't working a tile
			// (a specialist, or one not yet given a tile) has Tile.NONE, which
			// all such citizens share, so it gets none.
			foreach (CityResident cr in city.residents) {
				if (cr.tileWorked != Tile.NONE) {
					cr.tileWorked.personWorkingTile = cr;
				}
			}

			// Scenarios don't specify the citizens of each city, only the city
			// size. So we need to add the appropriate residents here.
			//
			// We wait to assign them to tiles until after tile ownership has
			// been established in SaveGame.cs.
			if (city.residents.Count == 0) {
				CitizenType ct = lookups.defaultCitizenType;
				for (int i = 0; i < size; ++i) {
					CityResident newResident = new() {
						citizenType = ct,
						nationality = city.owner.civilization,
						city = city
					};
					city.residents.Add(newResident);
				}
			}

			return city;
		}
	}
}
