using System;
using System.Linq;
using C7Engine.Pathing;
using C7GameData;
using Serilog;

namespace C7Engine.AI.UnitAI {
	/// <summary>
	/// A great leader's plan, after the C3X mod's replacement for Civ3's leader
	/// AI: our first army is worth more than anything; a later one is worth an
	/// army's shield cost divided by the armies we have; hurrying is worth the
	/// shields it saves in a city building an improvement or small wonder,
	/// less what the city would make while the leader travels there. The
	/// leader goes to the city where it does the most good and acts there;
	/// with nothing worth doing it waits in the nearest of our cities.
	/// A scientific leader builds no army, can hurry great wonders too, and
	/// weighs the Age of Science he could start in our capital: the extra
	/// research it brings over its turns, a beaker counted as a shield.
	/// </summary>
	public class LeaderAI : C7GameData.UnitAI {
		private static ILogger log = Log.ForContext<LeaderAI>();

		private City destination;
		private TilePath path;

		public void UpdateOnDeath() { }

		public string SummarizePlan() {
			return $"LeaderAI: heading for {destination?.name ?? "no city"}";
		}

		public C7GameData.UnitAI.MoveResult PlayTurnImpl(Player player, MapUnit unit) {
			int armyValue = ArmyValue(player, unit);
			(City hurryCity, int hurryValue) = BestCityToHurry(player, unit);
			int scienceValue = AgeOfScienceValue(player, unit);
			bool wantsScience = scienceValue > 0 && scienceValue >= hurryValue && scienceValue >= armyValue;
			bool wantsArmy = !wantsScience && armyValue > 0 && armyValue >= hurryValue;

			City here = unit.location.cityAtTile;
			if (wantsScience && unit.CanStartAgeOfScience()) {
				log.Information($"{unit} starts an Age of Science");
				unit.StartAgeOfScience();
				return C7GameData.UnitAI.Result.Done;
			}
			if (here != null && here.owner == player && !wantsScience) {
				if (wantsArmy && unit.CanBuildArmy()) {
					log.Information($"{unit} builds an army in {here.name}");
					unit.BuildArmy();
					return C7GameData.UnitAI.Result.Done;
				}
				if (!wantsArmy && hurryCity == here && unit.CanHurryProduction()) {
					log.Information($"{unit} hurries the {here.itemBeingProduced.name} in {here.name}");
					unit.HurryProduction();
					return C7GameData.UnitAI.Result.Done;
				}
			}

			City goal = wantsScience ? player.cities.Find(c => c.IsCapital())
				: wantsArmy || hurryCity == null ? NearestCity(player, unit) : hurryCity;
			if (goal == null || goal.location == unit.location) {
				// Nothing worth doing now: wait, and look again next turn.
				unit.SkipTurn();
				return C7GameData.UnitAI.Result.InProgress;
			}

			if (goal != destination || path == null) {
				destination = goal;
				path = PathingAlgorithmChooser.GetAlgorithm(unit).PathFrom(unit.location, goal.location, unit);
				if ((path?.PathLength() ?? -1) <= 0) {
					log.Information($"{unit} can't reach {goal.name}");
					destination = null;
					path = null;
					unit.SkipTurn();
					return C7GameData.UnitAI.Result.InProgress;
				}
			}
			return this.TryToMoveAlongPath(unit, ref path);
		}

		// What building an army is worth to us now, or 0 if we can't build
		// one: the first is worth more than anything, a later one the army's
		// shield cost divided by the armies we have.
		internal static int ArmyValue(Player player, MapUnit leader) {
			UnitPrototype armyType = MapUnit.ArmyType();
			if (armyType == null || leader.scientificLeader || !leader.unitType.actions.Contains(UnitAction.BuildArmy)
				|| !player.CanSupportAnotherArmy()) {
				return 0;
			}
			int armies = player.ArmyCount();
			return armies == 0 ? int.MaxValue : armyType.shieldCost / armies;
		}

		// The city of ours where hurrying saves the most shields, and how
		// many: one building an improvement or small wonder, counting the
		// shields still needed less what it makes until the leader gets there.
		internal static (City city, int value) BestCityToHurry(Player player, MapUnit leader) {
			if (!leader.unitType.actions.Contains(UnitAction.HurryProduction)) {
				return (null, 0);
			}
			City best = null;
			int bestValue = 0;
			foreach (City city in player.cities) {
				if (city.itemBeingProduced is not Building building || (building.IsGreatWonder() && !leader.scientificLeader)) {
					continue;
				}
				int perTurn = city.CurrentProductionYield().useful;
				int turnsAway = leader.location.DistanceTo(city.location) / Math.Max(1, leader.MovementPerTurn());
				int value = player.ShieldCost(building) - city.shieldsStored - perTurn * (1 + turnsAway);
				if (value > bestValue) {
					best = city;
					bestValue = value;
				}
			}
			return (best, bestValue);
		}

		// What an Age of Science is worth to us now, in beakers: our research
		// per turn, times its bonus, times its turns; 0 if this leader can't
		// start one (not scientific, no capital, one on already).
		internal static int AgeOfScienceValue(Player player, MapUnit leader) {
			if (!leader.scientificLeader || player.InAgeOfScience() || !player.cities.Any(c => c.IsCapital())) {
				return 0;
			}
			Rules rules = EngineStorage.gameData.rules;
			int beakers = player.cities.Sum(c => c.CurrentCommerceYield().beakers);
			return beakers * rules.AgeOfScienceResearchBonus * rules.AgeOfScienceDuration / 100;
		}

		private static City NearestCity(Player player, MapUnit unit) {
			return player.cities.OrderBy(c => c.location.DistanceTo(unit.location)).FirstOrDefault();
		}
	}
}
