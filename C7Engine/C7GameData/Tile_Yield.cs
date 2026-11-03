using System;
using System.Linq;
using C7Engine;

namespace C7GameData;

public partial class Tile {
	public enum YieldType {
		Commerce,
		Food,
		Production
	}

	public class Yield {
		public readonly Tile tile;
		public readonly YieldType type;
		public int penalty = 0;
		public int bonus = 0;
		public readonly int baseYield = 0;
		public int yield { get => baseYield + bonus - penalty; }

		public static Yield CalculateForCity(Tile tile, int yield, YieldType type, City city) {
			return new Yield(tile, yield, type)
				.ApplyTerrainImprovementModifiers(tile)
				.ApplyCityModifiers(city)
				.ApplyPlayerModifiers(city.owner);
		}

		public static Yield CalculateForPlayer(Tile tile, int yield, YieldType type, Player player) {
			return new Yield(tile, yield, type)
				.ApplyTerrainImprovementModifiers(tile)
				.ApplyPlayerModifiers(player);
		}

		public Yield(Tile tile, int baseYield, YieldType type) {
			this.tile = tile;
			this.baseYield = baseYield;
			this.type = type;
		}

		private Yield ApplyPlayerModifiers(Player player) {
			// A golden age adds a shield or a commerce to a tile already making
			// one, before the despotism penalty (so a 2-shield tile gains
			// nothing under Despotism, as Civ3 players note).
			if (player.InGoldenAge() && type != YieldType.Food && yield >= 1) {
				bonus += EngineStorage.gameData.rules.GoldenAgeTileBonus;
			}

			// An Agricultural civ's city tile makes a food more, and so does its
			// irrigated desert (Civilopedia "Agricultural"). The despotism
			// penalty takes the city tile's back unless the city has fresh water
			// or is past town size (CivWiki "Stadtfeldertrag"; this matches the
			// food Civ3 stored in the user's saves).
			bool agriculturalCity = false;
			// (The map generator rates sites for a player without a civilization.)
			if (type == YieldType.Food && player.civilization?.traits.Contains(Civilization.Trait.Agricultural) == true) {
				if (tile.HasCity()) {
					agriculturalCity = true;
				} else if (tile.overlayTerrainType.Key == "desert" && tile.HasIrrigation()) {
					bonus += EngineStorage.gameData.rules.traitBonuses.agriculturalIrrigatedDesertFood;
				}
			}
			bool keepsCityFood = agriculturalCity && (tile.BordersRiver() || tile.NeighborsFreshWater()
				|| tile.cityAtTile.residents.Count > EngineStorage.gameData.rules.MaximumLevel1CitySize);
			if (agriculturalCity && !keepsCityFood) {
				bonus += EngineStorage.gameData.rules.traitBonuses.agriculturalCityFood;
			}
			player.government.tileModifier?.Invoke(this);
			if (keepsCityFood) {
				bonus += EngineStorage.gameData.rules.traitBonuses.agriculturalCityFood;
			}
			return this;
		}

		private Yield ApplyCityModifiers(City city) {
			// An obsolete building (the Colossus, from Flight) no longer helps.
			city.GetBuildings().Where(b => !b.building.IsObsolete(city.owner)).ToList().ForEach(b => b.building.tileModifier?.Invoke(this));
			return this;
		}

		private Yield ApplyTerrainImprovementModifiers(Tile tile) {
			tile.overlays.GetImprovements().ToList().ForEach(ti => ti.tileModifier?.Invoke(this));
			return this;
		}
	}

	//Convenience method for printing the yield
	public string YieldString(Player player) {
		return $"{this.FoodYield(player).yield}/{this.ProductionYield(player).yield}/{this.CommerceYield(player).yield}";
	}

	// Food yield
	private int BaseFoodYield(Player player) {
		if (this.HasPollution()) return 0;
		int yield = overlayTerrainType.baseFoodProduction;
		if (this.Resource != Resource.NONE && player.KnowsAboutResource(Resource)) {
			yield += this.Resource.FoodBonus;
		}

		if (this.HasCity()) {
			// All city centers have a food yield of 2, regardless of bonus
			// food. See https://wiki.civforum.de/wiki/Stadtfeldertrag_(Civ3).
			// (An Agricultural civ's extra food: Yield.ApplyPlayerModifiers.)
			yield = 2;
		}

		yield += this.overlays.GetBaseYieldBonus(YieldType.Food);

		if (this.HasCraters())
			yield--;

		return yield;
	}
	public Yield FoodYield(Player player) {
		int yield = BaseFoodYield(player);
		return Yield.CalculateForPlayer(this, yield, YieldType.Food, player);
	}
	public Yield FoodYield(City city) {
		int yield = BaseFoodYield(city.owner);
		return Yield.CalculateForCity(this, yield, YieldType.Food, city);
	}

	// Production yield
	private int BaseProductionYield(Player player) {
		if (this.HasPollution()) return 0;
		int yield = overlayTerrainType.baseShieldProduction;
		if (overlayTerrainType.Key == "grassland" && this.isBonusShield) {
			yield++;
		}

		if (HasCity()) {
			// A city tile makes its terrain's shields, at least one in a town,
			// one more in a city (size 7-12) and two more in a metropolis; an
			// Industrious civ's metropolis one more again. See
			// https://civ-wiki.de/wiki/Stadtfeldertrag_(Civ3); it matches the
			// shields Civ3 stored in the user's saves.
			if (cityAtTile.residents.Count <= EngineStorage.gameData.rules.MaximumLevel1CitySize) {
				yield = Math.Max(yield, 1);
			} else if (cityAtTile.residents.Count <= EngineStorage.gameData.rules.MaximumLevel2CitySize) {
				yield += 1;
			} else {
				yield += 2;
				if (cityAtTile.owner.civilization.traits.Contains(Civilization.Trait.Industrious)) {
					yield += EngineStorage.gameData.rules.traitBonuses.industriousMetropolisShields;
				}
			}
		}

		// Bonus resources provide a boost in yield regardless of whether
		// there is a city.
		if (Resource != Resource.NONE && player.KnowsAboutResource(Resource)) {
			yield += this.Resource.ShieldsBonus;
		}

		yield += this.overlays.GetBaseYieldBonus(YieldType.Production);

		return yield;
	}
	public Yield ProductionYield(Player player) {
		int yield = BaseProductionYield(player);
		return Yield.CalculateForPlayer(this, yield, YieldType.Production, player);
	}
	public Yield ProductionYield(City city) {
		int yield = BaseProductionYield(city.owner);
		return Yield.CalculateForCity(this, yield, YieldType.Production, city);
	}

	// Commerce yield
	private int BaseCommerceYield(Player player) {
		if (this.HasPollution()) return 0;
		int yield = overlayTerrainType.baseCommerceProduction;
		if (this.Resource != Resource.NONE && player.KnowsAboutResource(Resource)) {
			yield += this.Resource.CommerceBonus;
		}

		bool borderRiver = this.BordersRiver();

		if (borderRiver) {
			yield += 1;
		}

		// See https://wiki.civforum.de/wiki/Stadtfeldertrag_(Civ3)
		// TODO: Consider making these values moddable via Lua
		if (HasCity()) {
			// A Commercial civ's cities and metropolises make two and three
			// more (CivWiki; the cities' +2 matches Civ3's saves). A capital
			// takes this ordinary sum when it beats its flat 4.
			bool commercial = cityAtTile.owner.civilization.traits.Contains(Civilization.Trait.Commercial);
			TraitBonusRules traitBonuses = EngineStorage.gameData.rules.traitBonuses;
			int regularCityYield;
			bool isCommercial = player.civilization?.traits.Contains(Civilization.Trait.Commercial) == true;
			if (cityAtTile.residents.Count <= EngineStorage.gameData.rules.MaximumLevel1CitySize) {
				regularCityYield = 1;
			} else if (cityAtTile.residents.Count <= EngineStorage.gameData.rules.MaximumLevel2CitySize) {
				regularCityYield = 2 + (commercial ? traitBonuses.commercialCityCommerce : 0);
			} else {
				regularCityYield = 3 + (commercial ? traitBonuses.commercialMetropolisCommerce : 0);
			}
			if (borderRiver) {
				regularCityYield += 1;
			}
			if (this.Resource != Resource.NONE && player.KnowsAboutResource(Resource)) {
				regularCityYield += this.Resource.CommerceBonus;
			}

			int capitalCityYield = 0;
			if (cityAtTile.IsCapital()) {
				capitalCityYield = 4;
			}

			// A Seafaring civ's coastal cities, capitals too, make a commerce
			// more on their tile (Civilopedia "Seafaring"; matches Civ3's saves).
			if (NeighborsOcean() && cityAtTile.owner.civilization.traits.Contains(Civilization.Trait.Seafaring)) {
				regularCityYield += traitBonuses.seafaringCoastalCityCommerce;
				if (capitalCityYield > 0) {
					capitalCityYield += traitBonuses.seafaringCoastalCityCommerce;
				}
			}

			yield = Math.Max(regularCityYield, capitalCityYield);
		} else {
			// The city tile's own formula above replaces the road's commerce.
			yield += this.overlays.GetBaseYieldBonus(YieldType.Commerce);
		}

		return yield;
	}
	public Yield CommerceYield(Player player) {
		int yield = BaseCommerceYield(player);

		return Yield.CalculateForPlayer(this, yield, YieldType.Commerce, player);
	}
	public Yield CommerceYield(City city) {
		int yield = BaseCommerceYield(city.owner);

		return Yield.CalculateForCity(this, yield, YieldType.Commerce, city);
	}
}
