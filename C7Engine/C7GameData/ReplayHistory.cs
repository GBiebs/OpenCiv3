using System.Collections.Generic;
using System.Linq;
using System.Text;
using C7Engine;

namespace C7GameData {
	// The game's history for Civ3's replay, shown when the game ends or the
	// player retires: who held each tile at the end of every turn, where the
	// cities stood, and the main events. Recording begins with the first
	// turn played after it was added (an older save replays from there).
	//
	// Owners are kept as the player's place in GameData.players plus one (0
	// for nobody): the first frame whole, then each turn's changes.
	public class ReplayHistory {
		public enum Kind { City, CityGone, News }

		public class Event {
			public int turn;
			public Kind kind;
			// The city's id and name, for its events.
			public string city;
			public string name;
			public int player = -1;
			public int x = -1;
			public int y = -1;
			// What the replay says (empty for the cities already there when
			// recording began).
			public string text = "";
		}

		private class CitySeen {
			public int owner;
			public string name;
		}

		public int startTurn = -1;
		public List<int> turns = new();
		// Run-length "owner*count," for the first frame, "tile:owner," changes after.
		public List<string> frames = new();
		public List<Event> events = new();
		// Where the last frame left things, to compare the next one with.
		public string lastOwners;
		public Dictionary<string, int> lastCityOwners = new();

		// At the end of each turn.
		public void Record(GameData gameData) {
			if (turns.Count > 0 && turns[^1] == gameData.turn) {
				return;
			}
			int[] owners = Owners(gameData);
			if (startTurn < 0 || lastOwners == null) {
				startTurn = gameData.turn;
				frames.Add(Encode(owners));
			} else {
				int[] before = Decode(lastOwners, owners.Length);
				StringBuilder changes = new();
				for (int i = 0; i < owners.Length; i++) {
					if (owners[i] != before[i]) {
						changes.Append(i).Append(':').Append(owners[i]).Append(',');
					}
				}
				frames.Add(changes.ToString());
			}
			turns.Add(gameData.turn);
			lastOwners = Encode(owners);
			RecordCities(gameData, first: turns.Count == 1);
		}

		private void RecordCities(GameData gameData, bool first) {
			Dictionary<string, int> now = new();
			foreach (City city in gameData.cities) {
				string id = city.id.ToString();
				int owner = gameData.players.IndexOf(city.owner);
				now[id] = owner;
				if (!lastCityOwners.TryGetValue(id, out int was)) {
					Add(gameData, Kind.City, city.location, owner, id,
						first ? "" : $"{city.name} is founded by the {city.owner.civilization.noun}.", city.name);
				} else if (was != owner) {
					Add(gameData, Kind.City, city.location, owner, id, $"{city.name} passes to the {city.owner.civilization.noun}.", city.name);
				}
			}
			foreach ((string id, int owner) in lastCityOwners) {
				if (!now.ContainsKey(id)) {
					Event seen = events.LastOrDefault(e => e.city == id);
					string name = seen?.name ?? "A city";
					events.Add(new Event() {
						turn = gameData.turn,
						kind = Kind.CityGone,
						city = id,
						name = name,
						player = owner,
						x = seen?.x ?? -1,
						y = seen?.y ?? -1,
						text = $"{name} is destroyed.",
					});
				}
			}
			lastCityOwners = now;
		}

		// News worth a line in the replay (every message to the UI passes).
		public void Note(GameData gameData, MessageToUI msg) {
			if (startTurn < 0) {
				return;
			}
			switch (msg) {
				case MsgWonderCompleted m:
					Add(gameData, Kind.News, m.city.location, gameData.players.IndexOf(m.builder), null,
						$"The {m.builder.civilization.noun} complete {m.wonder.name} in {m.city.name}.");
					break;
				case MsgWarDeclarationNotification m:
					Add(gameData, Kind.News, null, gameData.players.IndexOf(m.aggressor), null,
						$"The {m.aggressor.civilization.noun} declare war on the {m.opponent.civilization.noun}.");
					break;
				case MsgPeaceTreatySigned m:
					Add(gameData, Kind.News, null, gameData.players.IndexOf(m.a), null,
						$"The {m.a.civilization.noun} and the {m.b.civilization.noun} make peace.");
					break;
				case MsgCivilizationDestroyed m:
					Add(gameData, Kind.News, null, -1, null, $"The {m.civilization.noun} are destroyed.");
					break;
			}
		}

		private void Add(GameData gameData, Kind kind, Tile at, int player, string city, string text, string name = null) {
			events.Add(new Event() {
				turn = gameData.turn,
				kind = kind,
				city = city,
				name = name,
				player = player,
				x = at?.XCoordinate ?? -1,
				y = at?.YCoordinate ?? -1,
				text = text,
			});
		}

		private static int[] Owners(GameData gameData) {
			List<Tile> tiles = gameData.map.tiles;
			int[] owners = new int[tiles.Count];
			for (int i = 0; i < tiles.Count; i++) {
				Player owner = tiles[i].OwningPlayer();
				owners[i] = owner == null ? 0 : gameData.players.IndexOf(owner) + 1;
			}
			return owners;
		}

		private static string Encode(int[] owners) {
			StringBuilder result = new();
			for (int i = 0; i < owners.Length;) {
				int run = 1;
				while (i + run < owners.Length && owners[i + run] == owners[i]) {
					run++;
				}
				result.Append(owners[i]).Append('*').Append(run).Append(',');
				i += run;
			}
			return result.ToString();
		}

		private static int[] Decode(string encoded, int count) {
			int[] owners = new int[count];
			int i = 0;
			foreach (string part in encoded.Split(',', System.StringSplitOptions.RemoveEmptyEntries)) {
				string[] pair = part.Split('*');
				int owner = int.Parse(pair[0]);
				int run = int.Parse(pair[1]);
				for (int k = 0; k < run && i < count; k++) {
					owners[i++] = owner;
				}
			}
			return owners;
		}

		// Every recorded turn's tile owners, in order.
		public IEnumerable<(int turn, int[] owners)> Frames(int tileCount) {
			int[] owners = null;
			for (int f = 0; f < frames.Count; f++) {
				if (f == 0) {
					owners = Decode(frames[0], tileCount);
				} else {
					owners = (int[])owners.Clone();
					foreach (string change in frames[f].Split(',', System.StringSplitOptions.RemoveEmptyEntries)) {
						string[] pair = change.Split(':');
						int tile = int.Parse(pair[0]);
						if (tile < tileCount) {
							owners[tile] = int.Parse(pair[1]);
						}
					}
				}
				yield return (turns[f], owners);
			}
		}
	}
}
