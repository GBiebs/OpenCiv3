using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using C7Engine;
using C7Engine.Online;
using C7GameData;
using EngineTests.Utils;
using Serilog;
using Xunit;

namespace EngineTests.Online;

// Orders as they go over the network: every one of the engine's orders,
// with this game's things by their IDs.
public class OrderCodecTest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;
	private readonly Player player;
	private readonly City city;

	public OrderCodecTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		player = gameData.players.First(p => p.isHuman);
		city = CityInteractions.BuildCity(player.units[0].location, player, "Uruk");
	}

	private static IEnumerable<FieldInfo> Fields(Type type) {
		for (Type t = type; t != typeof(MessageToEngine); t = t.BaseType) {
			foreach (FieldInfo field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)) {
				if (field.FieldType != typeof(ILogger) && field.FieldType != typeof(C7GameData.GameData)) {
					yield return field;
				}
			}
		}
	}

	// A value of each kind an order may hold, from this game.
	private object Sample(Type type) {
		Type plain = Nullable.GetUnderlyingType(type) ?? type;
		Tile tile = city.location.neighbors.Values.First(t => t != Tile.NONE);
		return plain switch {
			_ when plain == typeof(bool) => true,
			_ when plain == typeof(int) => 7,
			_ when plain == typeof(long) => 7L,
			_ when plain == typeof(float) => 0.5f,
			_ when plain == typeof(double) => 0.25,
			_ when plain == typeof(string) => "Ur",
			_ when plain.IsEnum => Enum.GetValues(plain).GetValue(Enum.GetValues(plain).Length - 1),
			_ when plain == typeof(ID) => player.id,
			_ when plain == typeof(MapUnit) => player.units[0],
			_ when plain == typeof(City) => city,
			_ when plain == typeof(Player) => player,
			_ when plain == typeof(Tech) => gameData.techs[3],
			_ when plain == typeof(Building) => gameData.Buildings[2],
			_ when plain == typeof(Terraform) => gameData.Terraforms[0],
			_ when plain == typeof(Government) => gameData.governments[1],
			_ when plain == typeof(Tile) => tile,
			_ when plain == typeof(TilePath) => new TilePath(tile, new Queue<Tile>([tile, city.location])),
			_ when plain == typeof(TradeOffer) => new TradeOffer { gold = 30, techs = [gameData.techs[1]] },
			_ when plain == typeof(CityGovernor) => new CityGovernor { manageMoods = true, emphasizeFood = true },
			_ when plain == typeof(List<Administration.Change>) => new List<Administration.Change> { new() { playerId = player.id, human = true, clearPassword = true } },
			_ => throw new NotSupportedException(plain.Name),
		};
	}

	private static string Same(object value) => value switch {
		null => "null",
		TilePath path => $"{path.destination?.XCoordinate},{path.destination?.YCoordinate}:{string.Join(";", path.path.Select(t => $"{t.XCoordinate},{t.YCoordinate}"))}",
		TradeOffer offer => System.Text.Json.JsonSerializer.Serialize(DealTerms.Of(offer), new System.Text.Json.JsonSerializerOptions { IncludeFields = true, Converters = { new IDJsonConverter() } }),
		CityGovernor or List<Administration.Change> => System.Text.Json.JsonSerializer.Serialize(value, value.GetType(), new System.Text.Json.JsonSerializerOptions { IncludeFields = true, Converters = { new IDJsonConverter() } }),
		_ => value.ToString(),
	};

	// Each order type, filled with this game's things, comes back the same.
	[Fact]
	public void EveryOrderComesBackTheSame() {
		Assert.True(OrderCodec.OrderTypes.Count() > 40);
		foreach (Type type in OrderCodec.OrderTypes) {
			MessageToEngine order = (MessageToEngine)RuntimeHelpers.GetUninitializedObject(type);
			foreach (FieldInfo field in Fields(type)) {
				field.SetValue(order, Sample(field.FieldType));
			}
			string json = OrderCodec.Write(order);
			MessageToEngine read = OrderCodec.Read(OrderCodec.Kind(order), json, gameData);
			Assert.True(read != null, $"{type.Name} wasn't read back: {json}");
			Assert.IsType(type, read);
			foreach (FieldInfo field in Fields(type)) {
				Assert.True(Same(field.GetValue(order)) == Same(field.GetValue(read)), $"{type.Name}.{field.Name}");
			}
		}
	}

	[Fact]
	public void TheScreensOrdersStayHere() {
		Assert.True(OrderCodec.IsLocal(new MsgSetAnimationsEnabled(false)));
		Assert.True(OrderCodec.IsLocal(new MsgToggleAnimationsEnabled()));
		Assert.True(OrderCodec.IsLocal(new MsgUiDisengaged()));
		Assert.False(OrderCodec.IsLocal(new MsgEndTurn()));
		Assert.False(OrderCodec.IsLocal(new MsgMoveUnit(player.units[0].id, TileDirection.NORTH)));
	}

	// What isn't one of the engine's orders, or names what this game hasn't.
	[Theory]
	[InlineData("MsgShutdownEngine", "{\"log\":null}")]
	[InlineData("Nonsense", "{}")]
	[InlineData("MsgMoveUnit", "{}")]
	[InlineData("MsgPerformUnitAction", "{\"unit\":\"unit-999999\"}")]
	[InlineData("MsgMoveUnit", "{\"unitID\":\"not an id\",\"dir\":0}")]
	[InlineData("MsgMoveUnit", "[1, 2]")]
	[InlineData("MsgMoveUnit", "not json")]
	[InlineData("MsgBombard", "{\"unitID\":\"unit-1\",\"tileX\":\"far\",\"tileY\":3}")]
	[InlineData("MsgChooseResearch", "{\"tech\":\"tech-99999\",\"advisorState\":0,\"selectionMode\":0}")]
	[InlineData("MsgChooseResearch", "{\"tech\":\"tech-1\",\"advisorState\":77,\"selectionMode\":0}")]
	[InlineData("MsgSetUnitPath", "{\"unitID\":\"unit-1\",\"path\":{\"destination\":[99999,5],\"path\":[]}}")]
	public void WhatIsntAnOrderOfThisGameIsntRead(string kind, string json) {
		Assert.Null(OrderCodec.Read(kind, json, gameData));
	}

	[Fact]
	public void AnOrderNamesThingsByTheirIds() {
		MapUnit unit = player.units[0];
		string json = OrderCodec.Write(new MsgMoveUnit(unit.id, TileDirection.EAST));
		Assert.Contains(unit.id.ToString(), json);
		MessageToEngine read = OrderCodec.Read("MsgMoveUnit", json, gameData);
		Assert.NotNull(read);
	}
}
