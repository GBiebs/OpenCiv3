using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using C7GameData;
using Serilog;

namespace C7Engine.Online {
	// Online play: an order (one of the engine's MessageToEngine types) as
	// JSON, to go to the host and from it to every game. Only the engine's
	// own order types are read, by name, each with its fields; the game's
	// things go by their IDs (or a tile by its place) and come back as the
	// same things in each game's copy, or the order isn't read. Orders that
	// concern one computer only (animations, the screens) stay there (Local).
	public static class OrderCodec {
		private static readonly HashSet<Type> local = [
			typeof(MsgShutdownEngine), typeof(MsgSetAnimationsEnabled), typeof(MsgToggleAnimationsEnabled), typeof(MsgUiDisengaged),
		];

		private static readonly Dictionary<string, Type> orders = typeof(MessageToEngine).Assembly.GetTypes()
			.Where(t => t.IsSubclassOf(typeof(MessageToEngine)) && !t.IsAbstract && !local.Contains(t))
			.ToDictionary(t => t.Name);

		private static readonly JsonSerializerOptions plain = new() {
			IncludeFields = true,
			MaxDepth = 16,
			Converters = { new IDJsonConverter() },
		};

		public const int MaxPathLength = 10_000;

		public static IEnumerable<Type> OrderTypes => orders.Values;

		// Whether the order stays on this computer (not one of the engine's
		// orders that change the game).
		public static bool IsLocal(MessageToEngine message) => !orders.ContainsKey(message.GetType().Name) || orders[message.GetType().Name] != message.GetType();

		public static string Kind(MessageToEngine order) => order.GetType().Name;

		private static IEnumerable<FieldInfo> Fields(Type type) {
			for (Type t = type; t != null && t != typeof(MessageToEngine); t = t.BaseType) {
				foreach (FieldInfo field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)) {
					if (field.FieldType != typeof(ILogger) && field.FieldType != typeof(GameData)) {
						yield return field;
					}
				}
			}
		}

		public static string Write(MessageToEngine order) {
			JsonObject json = new();
			foreach (FieldInfo field in Fields(order.GetType())) {
				json[field.Name] = WriteValue(field.FieldType, field.GetValue(order));
			}
			return json.ToJsonString();
		}

		private static JsonNode WriteValue(Type type, object value) {
			if (value == null) {
				return null;
			}
			Type plainType = Nullable.GetUnderlyingType(type) ?? type;
			return value switch {
				bool b => JsonValue.Create(b),
				int i when !plainType.IsEnum => JsonValue.Create(i),
				long l => JsonValue.Create(l),
				float f => JsonValue.Create(f),
				double d => JsonValue.Create(d),
				string s => JsonValue.Create(s),
				Enum e => JsonValue.Create(Convert.ToInt32(e)),
				ID id => JsonValue.Create(id.ToString()),
				MapUnit unit => JsonValue.Create(unit.id.ToString()),
				City city => JsonValue.Create(city.id.ToString()),
				Player player => JsonValue.Create(player.id.ToString()),
				Tech tech => JsonValue.Create(tech.id.ToString()),
				Building building => JsonValue.Create(building.name),
				Terraform terraform => JsonValue.Create(terraform.Id.ToString()),
				Government government => JsonValue.Create(government.id.ToString()),
				Tile tile => new JsonArray(tile.XCoordinate, tile.YCoordinate),
				TilePath path => new JsonObject {
					["destination"] = path.destination == null ? null : new JsonArray(path.destination.XCoordinate, path.destination.YCoordinate),
					["path"] = new JsonArray(path.path.Select(t => (JsonNode)new JsonArray(t.XCoordinate, t.YCoordinate)).ToArray()),
				},
				TradeOffer offer => JsonSerializer.SerializeToNode(DealTerms.Of(offer), plain),
				CityGovernor or List<Administration.Change> => JsonSerializer.SerializeToNode(value, type, plain),
				_ => throw new NotSupportedException($"An order's field of type {type.Name} can't be sent"),
			};
		}

		// The order of `kind` in `json`, with this game's things; null if it
		// isn't one (an unknown kind, a field missing or of the wrong sort, a
		// thing this game doesn't have).
		public static MessageToEngine Read(string kind, string json, GameData gameData) {
			if (kind == null || !orders.TryGetValue(kind, out Type type) || json == null) {
				return null;
			}
			try {
				if (JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { MaxDepth = 16 }) is not JsonObject fields) {
					return null;
				}
				MessageToEngine order = (MessageToEngine)RuntimeHelpers.GetUninitializedObject(type);
				for (Type t = type; t != null && t != typeof(MessageToEngine); t = t.BaseType) {
					foreach (FieldInfo field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)) {
						if (field.FieldType == typeof(ILogger)) {
							field.SetValue(order, Log.ForContext(type));
						} else if (field.FieldType == typeof(GameData)) {
							field.SetValue(order, gameData);
						}
					}
				}
				foreach (FieldInfo field in Fields(type)) {
					if (!fields.TryGetPropertyValue(field.Name, out JsonNode node)) {
						return null;
					}
					if (!TryReadValue(field.FieldType, node, gameData, out object value)) {
						return null;
					}
					field.SetValue(order, value);
				}
				return order;
			} catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException or OverflowException or ArgumentException) {
				return null;
			}
		}

		private static bool TryReadValue(Type type, JsonNode node, GameData gameData, out object value) {
			value = null;
			if (node == null) {
				// (Only what can be nothing, as it was sent.)
				return !type.IsValueType || Nullable.GetUnderlyingType(type) != null;
			}
			Type plainType = Nullable.GetUnderlyingType(type) ?? type;
			if (plainType == typeof(bool)) {
				value = node.GetValue<bool>();
			} else if (plainType.IsEnum) {
				// (In the enum's own kind of number: some are bytes.)
				object number = Convert.ChangeType(node.GetValue<int>(), Enum.GetUnderlyingType(plainType));
				if (!Enum.IsDefined(plainType, number)) {
					return false;
				}
				value = Enum.ToObject(plainType, number);
			} else if (plainType == typeof(int)) {
				value = node.GetValue<int>();
			} else if (plainType == typeof(long)) {
				value = node.GetValue<long>();
			} else if (plainType == typeof(float)) {
				value = node.GetValue<float>();
			} else if (plainType == typeof(double)) {
				value = node.GetValue<double>();
			} else if (plainType == typeof(string)) {
				string text = node.GetValue<string>();
				if (text.Length > 1000) {
					return false;
				}
				value = text;
			} else if (plainType == typeof(ID)) {
				value = ID.FromString(node.GetValue<string>());
			} else if (plainType == typeof(MapUnit)) {
				value = gameData.GetUnit(ID.FromString(node.GetValue<string>()));
			} else if (plainType == typeof(City)) {
				ID id = ID.FromString(node.GetValue<string>());
				value = gameData.cities.Find(c => c.id == id);
			} else if (plainType == typeof(Player)) {
				value = gameData.GetPlayer(ID.FromString(node.GetValue<string>()));
			} else if (plainType == typeof(Tech)) {
				ID id = ID.FromString(node.GetValue<string>());
				value = gameData.techs.Find(t => t.id == id);
			} else if (plainType == typeof(Building)) {
				string name = node.GetValue<string>();
				value = gameData.Buildings.Find(b => b.name == name);
			} else if (plainType == typeof(Terraform)) {
				ID id = ID.FromString(node.GetValue<string>());
				value = gameData.Terraforms.Find(t => t.Id == id);
			} else if (plainType == typeof(Government)) {
				ID id = ID.FromString(node.GetValue<string>());
				value = gameData.governments.Find(g => g.id == id);
			} else if (plainType == typeof(Tile)) {
				value = ReadTile(node, gameData);
			} else if (plainType == typeof(TilePath)) {
				JsonObject path = node.AsObject();
				JsonArray steps = path["path"].AsArray();
				if (steps.Count > MaxPathLength) {
					return false;
				}
				Tile destination = path["destination"] == null ? null : ReadTile(path["destination"], gameData);
				List<Tile> tiles = steps.Select(s => ReadTile(s, gameData)).ToList();
				if (tiles.Any(t => t == null) || (path["destination"] != null && destination == null)) {
					return false;
				}
				value = destination == null ? TilePath.NONE : new TilePath(destination, new Queue<Tile>(tiles));
			} else if (plainType == typeof(TradeOffer)) {
				value = node.Deserialize<DealTerms>(plain)?.ToOffer(gameData);
			} else if (plainType == typeof(CityGovernor) || plainType == typeof(List<Administration.Change>)) {
				value = node.Deserialize(plainType, plain);
			} else {
				return false;
			}
			// A thing this game doesn't have.
			return value != null;
		}

		private static Tile ReadTile(JsonNode node, GameData gameData) {
			JsonArray place = node.AsArray();
			if (place.Count != 2) {
				return null;
			}
			int x = place[0].GetValue<int>();
			int y = place[1].GetValue<int>();
			if (x < 0 || y < 0 || x >= gameData.map.numTilesWide || y >= gameData.map.numTilesTall) {
				return null;
			}
			Tile tile = gameData.map.tileAt(x, y);
			return tile == Tile.NONE ? null : tile;
		}
	}
}
