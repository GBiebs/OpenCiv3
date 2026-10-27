using System.Collections.Generic;

namespace C7GameData {
	// A diplomatic or spy mission (Civ3's ESPN section, the editor's "Diplomats
	// and Spies" page). Civ3 knows them by their place in the list; `key` names
	// that place. Their gold cost starts from `baseCost` (see Embassies and
	// Espionage).
	public class EspionageMission {
		public const string Embassy = "embassy";
		public const string InvestigateCity = "investigateCity";
		public const string StealTechnology = "stealTechnology";
		public const string StealWorldMap = "stealWorldMap";
		public const string PlantSpy = "plantSpy";
		public const string StealPlans = "stealPlans";
		public const string Propaganda = "propaganda";
		public const string SabotageProduction = "sabotageProduction";
		public const string ExposeEnemySpy = "exposeEnemySpy";

		// The keys in Civ3's order.
		public static readonly List<string> Keys = [
			Embassy, InvestigateCity, StealTechnology, StealWorldMap, PlantSpy,
			StealPlans, Propaganda, SabotageProduction, ExposeEnemySpy,
		];

		public string key;
		public string name;
		public int baseCost;
		// Whether it can be done through an embassy (a diplomat), and by a
		// planted spy.
		public bool diplomat;
		public bool spy;

		// Conquests' missions.
		public static List<EspionageMission> Conquests() {
			return [
				new() { key = Embassy, name = "Build an Embassy", baseCost = 20, diplomat = true },
				new() { key = InvestigateCity, name = "Investigate City", baseCost = 10, diplomat = true, spy = true },
				new() { key = StealTechnology, name = "Steal Technology", baseCost = 10, diplomat = true, spy = true },
				new() { key = StealWorldMap, name = "Steal World Map", baseCost = 1, spy = true },
				new() { key = PlantSpy, name = "Plant Spy", baseCost = 60, spy = true },
				new() { key = StealPlans, name = "Steal Plans", baseCost = 10, spy = true },
				new() { key = Propaganda, name = "Initiate Propaganda", baseCost = 100, spy = true },
				new() { key = SabotageProduction, name = "Sabotage Production", baseCost = 10, spy = true },
				new() { key = ExposeEnemySpy, name = "Expose Enemy Spy", baseCost = 80, spy = true },
			];
		}
	}
}
