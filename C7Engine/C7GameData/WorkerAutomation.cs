using System;
using System.Linq;

namespace C7GameData {
	// Civ3's kinds of worker automation besides plain A (Conquests'
	// labels.txt, CivFanatics' hotkey list): improve only the nearest city's
	// tiles (Shift-I), never replace an improvement (Shift-A), both
	// (Ctrl-Shift-I); clear only forests, wetlands or pollution (Shift-F,
	// Shift-W, Shift-D); road or railroad to a tile (Ctrl-R, Ctrl-Shift-R),
	// road to a tile and found a colony there (Ctrl-B); connect our cities
	// and resources by road (Ctrl-N); irrigate toward the nearest city
	// (Ctrl-I). WorkerAI plans by it; the goal kinds end when their work is
	// done and the worker comes back to the player.
	public class WorkerAutomation {
		public enum Kind { ThisCityOnly, NoAltering, ThisCityNoAltering, ClearOnly, RoadTo, RailTo, ColonyTo, TradeNetwork, IrrigateToCity }

		public Kind kind;
		// The goal's tile (road, rail or colony to), or the city's (this city
		// only, irrigate to the nearest city: chosen when it starts).
		public Tile target;
		// Clear only: the job's order (its Terraform.UIAction).
		public string job;

		public bool CityOnly => kind is Kind.ThisCityOnly or Kind.ThisCityNoAltering;
		public bool NoAlterations => kind is Kind.NoAltering or Kind.ThisCityNoAltering;
		public bool NeedsCity => CityOnly || kind == Kind.IrrigateToCity;

		// As saved: "Kind", then ":job" and "@x,y" when it has them.
		public string Save() {
			string saved = kind.ToString();
			if (job != null) {
				saved += ":" + job;
			}
			if (target != null && target != Tile.NONE) {
				saved += $"@{target.XCoordinate},{target.YCoordinate}";
			}
			return saved;
		}

		public static WorkerAutomation Load(string saved, GameMap map) {
			if (string.IsNullOrEmpty(saved)) {
				return null;
			}
			string[] at = saved.Split('@');
			string[] parts = at[0].Split(':');
			if (!Enum.TryParse(parts[0], out Kind kind)) {
				return null;
			}
			WorkerAutomation result = new() { kind = kind, job = parts.Length > 1 ? parts[1] : null };
			if (at.Length > 1 && at[1].Split(',') is [string x, string y] && int.TryParse(x, out int tx) && int.TryParse(y, out int ty)) {
				result.target = map.tileAt(tx, ty);
			}
			return result;
		}

		// The city this automation is about: the target's, else our nearest.
		public static City NearestCity(MapUnit unit) {
			return unit.owner.cities.OrderBy(c => c.location.DistanceTo(unit.location)).FirstOrDefault();
		}
	}
}
