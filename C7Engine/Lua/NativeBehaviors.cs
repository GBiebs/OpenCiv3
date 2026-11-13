using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using C7GameData;
using MoonSharp.Interpreter;

namespace C7Engine.Lua;

// C# versions of stock Lua behaviors that run so often (tile yields are
// computed for every tile of every city, many times a turn) that calling
// into Lua is a large part of the turn's time.
//
// The stock scripts mark the functions these mirror by wrapping them in
// NATIVE_DEFAULT("name", function ... end). When the BehaviorEngine imports
// a function that is still the one so marked, it hands out the C# version
// instead. A function that an addon put in its place isn't marked, so the
// Lua runs as written; and whoever changes a marked function in the stock
// scripts must drop the marker (or change the C# to match), which
// NativeBehaviorsTest checks.
//
// Each C# version must give exactly what its Lua does, for every input.
internal static class NativeBehaviors {
	// The marked Lua functions, with the name each was marked with.
	private static readonly ConditionalWeakTable<Closure, string> markedFunctions = new();

	private static readonly Dictionary<string, Delegate> natives = new() {
		["terrain_improvements.railroad.tile_modifier"] = (Action<Tile.Yield>)RailroadTileModifier,
		["buildings.tile_modifiers.increases_food_in_water"] = (Action<Tile.Yield>)IncreasesFoodInWater,
		["buildings.tile_modifiers.increases_shields_in_water"] = (Action<Tile.Yield>)IncreasesShieldsInWater,
		["buildings.tile_modifiers.increases_trade_in_water"] = (Action<Tile.Yield>)IncreasesTradeInWater,
		["buildings.tile_modifiers.increased_trade"] = (Action<Tile.Yield>)IncreasedTrade,
	};

	// The NATIVE_DEFAULT Lua global: remembers that the function is the
	// stock one with the given name, and returns it unchanged.
	internal static DynValue MarkDefault(ScriptExecutionContext context, CallbackArguments args) {
		string name = args.AsType(0, "NATIVE_DEFAULT", DataType.String).String;
		DynValue function = args.AsType(1, "NATIVE_DEFAULT", DataType.Function);
		if (!natives.ContainsKey(name)) {
			throw new ScriptRuntimeException($"NATIVE_DEFAULT: no C# version of '{name}'");
		}
		markedFunctions.AddOrUpdate(function.Function, name);
		return function;
	}

	// The C# version of the Lua function, if it is a marked stock function
	// with a C# version of the delegate type wanted.
	internal static T Find<T>(Closure function) where T : Delegate {
		if (markedFunctions.TryGetValue(function, out string name) && natives.TryGetValue(name, out Delegate native)) {
			return native as T;
		}
		return null;
	}

	// For tests: the C# version registered under the name.
	internal static T Get<T>(string name) where T : Delegate {
		return natives.TryGetValue(name, out Delegate native) ? native as T : null;
	}

	internal static IEnumerable<string> Names => natives.Keys;

	// terrain_improvements.lua: railroad.tile_modifier
	private static void RailroadTileModifier(Tile.Yield yield) {
		TerrainImprovement improvement = yield.tile.overlays.ImprovementAtLayer(TerrainImprovement.Layer.ResourceDevelopment);
		if (improvement == null) {
			return;
		}
		if (improvement.key == "mine" && yield.type == Tile.YieldType.Production) {
			yield.bonus += 1;
		}
		if (improvement.key == "irrigation" && yield.type == Tile.YieldType.Food) {
			yield.bonus += 1;
		}
	}

	// buildings.lua: tile_modifiers.increases_food_in_water
	private static void IncreasesFoodInWater(Tile.Yield yield) {
		if (yield.type == Tile.YieldType.Food && yield.tile.IsInLargeBodyOfWater() && !yield.tile.HasFallout()) {
			yield.bonus += 1;
		}
	}

	// buildings.lua: tile_modifiers.increases_shields_in_water
	private static void IncreasesShieldsInWater(Tile.Yield yield) {
		if (yield.type == Tile.YieldType.Production && yield.tile.IsInLargeBodyOfWater()) {
			yield.bonus += 1;
		}
	}

	// buildings.lua: tile_modifiers.increases_trade_in_water
	private static void IncreasesTradeInWater(Tile.Yield yield) {
		if (yield.type == Tile.YieldType.Commerce && yield.tile.IsInLargeBodyOfWater() && yield.baseYield > 0) {
			yield.bonus += 1;
		}
	}

	// buildings.lua: tile_modifiers.increased_trade
	private static void IncreasedTrade(Tile.Yield yield) {
		if (yield.type == Tile.YieldType.Commerce && yield.baseYield > 0) {
			yield.bonus += 1;
			Player owner = yield.tile.OwningPlayer();
			if (owner != null && owner.government.hasTilePenalty && yield.baseYield == 2) {
				yield.penalty += 1;
			}
		}
	}
}
