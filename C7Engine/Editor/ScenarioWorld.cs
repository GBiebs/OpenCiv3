using System;
using System.Collections.Generic;
using System.Linq;
using C7GameData;

namespace C7Engine.Editor;

// One of a scenario's world settings for a new game: a value (an enum's
// name, or a world size's name), null for the player's choice, and whether
// it's the only one allowed or only where the setup screen starts.
public sealed class WorldChoice {
	public string value;
	public bool only;

	public WorldChoice Copy() => new() { value = value, only = only };
}

// The world a scenario without its own map is played on (the user's
// request, 2026-11-18: "only allow specific settings ... i.e. 5 billion
// age, or 60% water, or pangea only ... or at least set some by default").
// Ours: Civ3's editor leaves a rules mod's world to the setup screen. The
// world setup screen starts on these and greys out the rest of an "only"
// one (WorldSetup).
public sealed class ScenarioWorldSetup {
	public WorldChoice worldSize = new(), landform = new(), water = new(), climate = new(), temperature = new(), age = new(), barbarians = new();

	public ScenarioWorldSetup Copy() => new() {
		worldSize = worldSize.Copy(),
		landform = landform.Copy(),
		water = water.Copy(),
		climate = climate.Copy(),
		temperature = temperature.Copy(),
		age = age.Copy(),
		barbarians = barbarians.Copy(),
	};
}

public static class ScenarioWorld {
	public enum Setting { WorldSize, Landform, Water, Climate, Temperature, Age, Barbarians }

	public sealed record Option(string Value, string Label);

	public static readonly Setting[] All = Enum.GetValues<Setting>();

	public static string Label(Setting setting) => setting switch {
		Setting.WorldSize => "World Size",
		Setting.Landform => "Land Mass",
		Setting.Water => "Water Coverage",
		Setting.Climate => "Climate",
		Setting.Temperature => "Temperature",
		Setting.Age => "Age",
		_ => "Barbarians",
	};

	// The values a setting may take, with the setup screen's words; the
	// world sizes are the rules' own.
	public static List<Option> Options(Setting setting, IEnumerable<string> worldSizes) => setting switch {
		Setting.WorldSize => worldSizes.Select(w => new Option(w, w)).ToList(),
		Setting.Landform => [.. Enum.GetValues<WorldCharacteristics.Landform>().Reverse().Select(l => new Option(l.ToString(), l.ToString()))],
		Setting.Water => [.. Enum.GetValues<WorldCharacteristics.OceanCoverage>().Select(o => new Option(o.ToString(), $"{(int)o}% water"))],
		Setting.Climate => [.. Enum.GetValues<WorldCharacteristics.Climate>().Select(c => new Option(c.ToString(), c.ToString()))],
		Setting.Temperature => [.. Enum.GetValues<WorldCharacteristics.Temperature>().Reverse().Select(t => new Option(t.ToString(), t.ToString()))],
		Setting.Age => [.. Enum.GetValues<WorldCharacteristics.Age>().Select(a => new Option(a.ToString(), a.ToString().Replace("Billion_", "") + " billion years"))],
		_ => [.. Enum.GetValues<BarbarianActivity>().OrderBy(b => b).Select(b => new Option(b.ToString(), b == BarbarianActivity.None ? "No Barbarians" : b.ToString()))],
	};

	public static WorldChoice Of(ScenarioWorldSetup world, Setting setting) => setting switch {
		Setting.WorldSize => world.worldSize,
		Setting.Landform => world.landform,
		Setting.Water => world.water,
		Setting.Climate => world.climate,
		Setting.Temperature => world.temperature,
		Setting.Age => world.age,
		_ => world.barbarians,
	};

	// The choice's value as the enum, or null for the player's choice (or a
	// value the game doesn't know).
	public static T? Value<T>(WorldChoice choice) where T : struct, Enum {
		return choice?.value != null && Enum.TryParse(choice.value, out T value) && Enum.IsDefined(value) ? value : null;
	}

	// The world size the scenario names, if the rules have it.
	public static WorldSize Size(WorldChoice choice, List<WorldSize> sizes) {
		return choice?.value == null ? null : sizes.Find(s => s.name == choice.value);
	}

	// Why the setup screen greys the other values out.
	public static string OnlyNote(Setting setting, WorldChoice choice, IEnumerable<string> worldSizes) {
		string label = Options(setting, worldSizes).Find(o => o.Value == choice?.value)?.Label ?? choice?.value;
		return $"This scenario allows only {label} ({Label(setting)}).";
	}

	// Whether a value may be chosen at setup: any, unless the scenario
	// allows only its own.
	public static bool Allows(WorldChoice choice, string value) {
		return choice?.value == null || !choice.only || choice.value == value;
	}
}
