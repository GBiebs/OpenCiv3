using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7Engine.AI;
using Serilog;

namespace C7GameData {
	// Civ3's city governor ("Contact Governor"), for a human's cities. It
	// places each new citizen (a city grows, a resister gives in) on the tile
	// it values most, and can also:
	// - manage moods: keep the city out of civil disorder with as few
	//   entertainers as it can (players: "the governor will create an
	//   entertainer to keep the city out of disorder");
	// - emphasize food, shields or commerce when it places a citizen;
	// - manage production: choose what the city builds next, great and small
	//   wonders only if allowed (players' advice: "set the governor to NOT
	//   build Great Wonders and NOT build Small Wonders").
	// How it values a tile is TimBentley's measurement (CivFanatics,
	// "Explanation of the governor"): while the city's food surplus is under
	// twice a citizen's food (4), a food is worth 4 shields and a shield 4
	// gold, and food beyond that surplus only a shield's worth unless food
	// is emphasized; otherwise a shield is worth 4 food and a food 4 gold.
	// Emphasis multiplies what's emphasized by 4 (all three cancel out). The
	// 4s are rules (Rules.GovernorValueRatio, GovernorEmphasisFactor).
	public class CityGovernor {
		private static ILogger log = Log.ForContext<CityGovernor>();

		public bool manageMoods;
		public bool emphasizeFood;
		public bool emphasizeShields;
		public bool emphasizeCommerce;
		public bool manageProduction;
		public bool buildGreatWonders = true;
		public bool buildSmallWonders = true;

		public CityGovernor Copy() {
			return (CityGovernor)MemberwiseClone();
		}

		// Whether these are the settings a city starts with (saves leave them out).
		public bool IsDefault() {
			CityGovernor none = new();
			return manageMoods == none.manageMoods && emphasizeFood == none.emphasizeFood && emphasizeShields == none.emphasizeShields
				&& emphasizeCommerce == none.emphasizeCommerce && manageProduction == none.manageProduction
				&& buildGreatWonders == none.buildGreatWonders && buildSmallWonders == none.buildSmallWonders;
		}

		// What a new citizen of `city` (counted among its residents, not yet
		// working) would be worth working `tile`, in commerce.
		public double TileValue(City city, Tile tile, Rules rules) {
			Player owner = city.owner;
			int food = tile.FoodYield(owner).yield;
			int shields = tile.ProductionYield(owner).yield;
			int commerce = tile.CommerceYield(owner).yield;
			int ratio = rules.GovernorValueRatio;
			int emphasis = rules.GovernorEmphasisFactor;

			// The surplus before the new citizen eats.
			int foodWanted = 2 * City.FoodPerCitizen;
			int surplus = city.FoodGrowthPerTurn() + City.FoodPerCitizen;
			bool hungry = surplus < foodWanted;
			double foodValue = hungry ? ratio * ratio : ratio;
			double shieldValue = hungry ? ratio : ratio * ratio;
			double commerceValue = 1;
			bool all = emphasizeFood && emphasizeShields && emphasizeCommerce;
			if (!all) {
				foodValue *= emphasizeFood ? emphasis : 1;
				shieldValue *= emphasizeShields ? emphasis : 1;
				commerceValue *= emphasizeCommerce ? emphasis : 1;
			}

			// While hungry, food past the wanted surplus is only worth a
			// shield, unless food is emphasized.
			double foodPart = food * foodValue;
			if (hungry && !emphasizeFood) {
				int needed = System.Math.Clamp(foodWanted - surplus, 0, food);
				foodPart = needed * foodValue + (food - needed) * shieldValue;
			}
			return foodPart + shields * shieldValue + commerce * commerceValue;
		}

		// The free tile a new citizen of `city` would work, or Tile.NONE.
		public Tile BestTile(City city, GameData gameData) {
			Tile best = Tile.NONE;
			double bestValue = 0;
			foreach (Tile t in city.GetWorkableTiles().Where(t => t.personWorkingTile == null)) {
				double value = TileValue(city, t, gameData.rules);
				if (value > bestValue) {
					bestValue = value;
					best = t;
				}
			}
			return best;
		}

		// Keeps `city` out of disorder with as few entertainers as it can:
		// entertainers go back to work while that keeps the peace, then the
		// workers on the poorest tiles become entertainers while it would riot.
		public void ManageMoods(City city, GameData gameData) {
			CitizenType laborer = gameData.citizenTypes.Find(c => c.IsDefaultCitizen);
			List<CitizenType> specialists = city.owner.GetKnownSpecialists(gameData);
			CitizenType entertainer = specialists.Count == 0 ? null : specialists.MaxBy(s => s.Luxuries);
			if (entertainer == null || entertainer.Luxuries <= 0) {
				return;
			}

			foreach (CityResident resident in city.residents.Where(r => !r.resisting && r.citizenType == entertainer).ToList()) {
				Tile tile = BestTile(city, gameData);
				if (tile == Tile.NONE) {
					break;
				}
				resident.citizenType = laborer;
				resident.tileWorked = tile;
				tile.personWorkingTile = resident;
				if (city.RecalculateCitizenMoods(gameData) == City.Mood.Unhappy) {
					tile.personWorkingTile = null;
					resident.tileWorked = Tile.NONE;
					resident.citizenType = entertainer;
					break;
				}
			}

			while (city.RecalculateCitizenMoods(gameData) == City.Mood.Unhappy) {
				CityResident poorest = city.residents
					.Where(r => !r.resisting && r.tileWorked != null && r.tileWorked != Tile.NONE)
					.MinBy(r => TileValue(city, r.tileWorked, gameData.rules));
				if (poorest == null) {
					break;
				}
				poorest.tileWorked.personWorkingTile = null;
				poorest.tileWorked = Tile.NONE;
				poorest.citizenType = entertainer;
			}
			city.RecalculateCitizenMoods(gameData);
		}

		// What the city builds next, if the governor manages production.
		public IProducible ChooseProduction(City city) {
			bool Allowed(IProducible p) => p is not Building b
				|| (buildGreatWonders || !b.IsGreatWonder()) && (buildSmallWonders || !b.isSmallWonder);
			IProducible choice = ChooseProducible.Choose(city, city.owner, Allowed);
			log.Information($"The governor of {city.name} chooses {choice}");
			return choice;
		}
	}
}
