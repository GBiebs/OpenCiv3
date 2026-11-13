using C7Engine;
using System.Linq;

namespace C7GameData {
	// Civ3's kinds of worker automation besides plain Automate (A), which
	// WorkerAI plans by. Their keys and names are labels.txt's
	// (#UNIT_ACTIONS), what they do the Civilopedia's (GCON_Worker_Jobs,
	// GCON_Worker_Jobs_Advanced):
	// - This City Only (Shift-I): "perform automated tasks to improve the
	//   nearest city".
	// - No Altering (Shift-A): "automated tasks that do not change existing
	//   tile improvements" (the hotkey list calls it "Automate Worker (virgin
	//   terrain)").
	// - No Altering/This City Only (Ctrl-Shift-I): both.
	// - Clear Forest (Shift-F), Clear Wetlands (Shift-W), Clear Damage
	//   (Shift-D): "clearing all nearby forests", "all nearby jungle and/or
	//   marsh", "all nearby pollution, including craters and fallout".
	// - Road to (Ctrl-R), Rail to (Ctrl-Shift-R): "Builds a road [railroad]
	//   to a particular destination."
	// - Build Trade (Ctrl-N): "linking all cities to your capital city".
	// - Irrigate to Nearest City (Ctrl-I): "irrigating the nearest city".
	// A kind is saved with the unit, and ends with its automation: when the
	// player takes the unit back, or there's nothing left to do.
	public class WorkerAutomation {
		public enum Kind {
			ThisCityOnly,
			NoAltering,
			ThisCityNoAltering,
			ClearForest,
			ClearWetlands,
			ClearDamage,
			RoadTo,
			RailTo,
			TradeNetwork,
			IrrigateToCity,
		}

		public Kind kind;

		// The city's tile for the kinds about one city (chosen when the
		// order is given: the nearest), the destination for road and rail
		// to; otherwise null.
		public Tile target;

		public WorkerAutomation(Kind kind, Tile target = null) {
			this.kind = kind;
			this.target = target;
		}

		public bool CityOnly => kind is Kind.ThisCityOnly or Kind.ThisCityNoAltering;
		public bool NoAlterations => kind is Kind.NoAltering or Kind.ThisCityNoAltering;
		public bool NeedsCity => CityOnly || kind == Kind.IrrigateToCity;
		public bool NeedsDestination => kind is Kind.RoadTo or Kind.RailTo;

		// The worker job of a clearing kind (its Terraform.UIAction), or null.
		public string ClearingJob => kind switch {
			Kind.ClearForest => C7Action.UnitClearForest,
			Kind.ClearWetlands => C7Action.UnitClearWetlands,
			Kind.ClearDamage => C7Action.UnitClearDamage,
			_ => null,
		};

		// The player's city nearest the unit, the one the city kinds work for.
		public static City NearestCity(MapUnit unit) {
			return unit.owner.cities.OrderBy(c => c.location.DistanceTo(unit.location)).FirstOrDefault();
		}

		public override string ToString() {
			return target == null ? kind.ToString() : $"{kind} {target}";
		}
	}
}
