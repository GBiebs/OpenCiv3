using System.Collections.Generic;
using System.Linq;
using C7GameData.Save;

namespace C7GameData {
	// A production queue the player saved under a name (Shift-Q on the city
	// screen), to load into any of their cities (Q). Civ3 has the queue
	// (labels.txt, #CS_QUEUE: "QUEUE (hold 'Shift' to add)") but no saved
	// queues, so these are the player's, kept in the game's save like the
	// rest of their empire, rather than the user's preferences: they travel
	// with a hotseat, play-by-email or LAN game, and each player has their
	// own.
	//
	// Adapted from GBiebs/OpenCiv3.
	public class SavedProductionQueue {
		public const int MaxNameLength = 32;

		public string name;
		// The queued items, by kind and name, so that a queue saved before
		// an item could be built still names it.
		public List<SaveQueuedProducible> items = new();

		public SavedProductionQueue Copy() {
			return new SavedProductionQueue {
				name = name,
				items = items.ConvertAll(i => new SaveQueuedProducible { name = i.name, type = i.type }),
			};
		}

		// The name as saved: trimmed and kept short. Empty if there's none.
		public static string CleanName(string name) {
			string clean = (name ?? "").Trim();
			return clean.Length > MaxNameLength ? clean[..MaxNameLength].TrimEnd() : clean;
		}

		// Saves the city's queue under the name, replacing any saved queue of
		// that name. Returns false, saving nothing, when the name is blank or
		// the queue empty.
		public static bool Save(Player player, string name, List<IProducible> queue) {
			string clean = CleanName(name);
			if (clean == "" || queue.Count == 0) {
				return false;
			}
			SavedProductionQueue saved = new() {
				name = clean,
				items = queue.ConvertAll(p => new SaveQueuedProducible { name = p.name, type = SaveCity.TypeOf(p) }),
			};
			player.savedProductionQueues.RemoveAll(q => string.Equals(q.name, clean, System.StringComparison.OrdinalIgnoreCase));
			player.savedProductionQueues.Add(saved);
			player.savedProductionQueues.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase));
			return true;
		}

		public static SavedProductionQueue Find(Player player, string name) {
			string clean = CleanName(name);
			return player.savedProductionQueues.Find(q => string.Equals(q.name, clean, System.StringComparison.OrdinalIgnoreCase));
		}

		// Makes the saved queue the city's, in place of the one it had. Items
		// the city can't build now are left out, as Shift+click couldn't add
		// them either; the city's current build is kept.
		public void LoadInto(GameData gameData, City city) {
			List<IProducible> options = city.ListProductionOptions(gameData).ToList();
			city.ClearProductionQueue();
			foreach (SaveQueuedProducible item in items) {
				IProducible option = options.Find(o => o.name == item.name && SaveCity.TypeOf(o) == item.type);
				if (option != null) {
					city.EnqueueProduction(option);
				}
			}
		}
	}
}
