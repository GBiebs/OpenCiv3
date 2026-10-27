using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// The rules in ruleset.json and the defaults in Rules must agree: games
// from Civ3's files (BIQ and SAV) and older saves get the defaults for
// every rule Civ3 doesn't have, so a rule changed in one place only would
// play differently by where the game came from.
public class RulesetTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;

	public RulesetTest(SaveGameFixture fixture) {
		this.fixture = fixture;
	}

	private static JsonElement Ruleset() {
		using JsonDocument ruleset = JsonDocument.Parse(File.ReadAllText(Path.Combine(PathUtils.GameModesDir, "civ3", "ruleset.json")));
		return ruleset.RootElement.GetProperty("rules").Clone();
	}

	private static JsonElement Defaults() {
		JsonSerializerOptions options = new() {
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			IncludeFields = true,
			Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
		};
		return JsonSerializer.SerializeToElement(new Rules(), options);
	}

	// A rule Civ3's BIQ sets has no default here (0, false or nothing).
	private static bool HasNoDefault(JsonElement value) {
		return value.ValueKind is JsonValueKind.Null or JsonValueKind.False
			|| (value.ValueKind == JsonValueKind.Number && value.GetDouble() == 0)
			|| (value.ValueKind == JsonValueKind.String && value.GetString().Length == 0);
	}

	private static void AssertSame(JsonElement expected, JsonElement actual, string path, List<string> differences) {
		if (expected.ValueKind != actual.ValueKind) {
			differences.Add($"{path}: {expected} in Rules, {actual} in ruleset.json");
			return;
		}
		switch (expected.ValueKind) {
			case JsonValueKind.Object:
				foreach (JsonProperty p in expected.EnumerateObject()) {
					if (!actual.TryGetProperty(p.Name, out JsonElement a)) {
						differences.Add($"{path}.{p.Name}: missing from ruleset.json");
					} else {
						AssertSame(p.Value, a, $"{path}.{p.Name}", differences);
					}
				}
				foreach (JsonProperty p in actual.EnumerateObject().Where(p => !expected.TryGetProperty(p.Name, out _))) {
					differences.Add($"{path}.{p.Name}: no such rule");
				}
				break;
			case JsonValueKind.Array:
				if (expected.GetArrayLength() != actual.GetArrayLength()) {
					differences.Add($"{path}: {expected.GetArrayLength()} entries in Rules, {actual.GetArrayLength()} in ruleset.json");
					return;
				}
				for (int i = 0; i < expected.GetArrayLength(); ++i) {
					AssertSame(expected[i], actual[i], $"{path}[{i}]", differences);
				}
				break;
			case JsonValueKind.Number:
				if (Math.Abs(expected.GetDouble() - actual.GetDouble()) > 1e-6) {
					differences.Add($"{path}: {expected} in Rules, {actual} in ruleset.json");
				}
				break;
			default:
				if (expected.ToString() != actual.ToString()) {
					differences.Add($"{path}: {expected} in Rules, {actual} in ruleset.json");
				}
				break;
		}
	}

	[Fact]
	public void TheRulesetAndTheDefaultsAgree() {
		JsonElement ruleset = Ruleset();
		JsonElement defaults = Defaults();
		List<string> differences = [];
		foreach (JsonProperty rule in defaults.EnumerateObject()) {
			if (!ruleset.TryGetProperty(rule.Name, out JsonElement value)) {
				differences.Add($"{rule.Name}: missing from ruleset.json");
			} else if (!HasNoDefault(rule.Value)) {
				AssertSame(rule.Value, value, rule.Name, differences);
			}
		}
		foreach (JsonProperty rule in ruleset.EnumerateObject().Where(r => !defaults.TryGetProperty(r.Name, out _))) {
			differences.Add($"{rule.Name}: no such rule");
		}
		Assert.Empty(differences);
	}

	// The test itself: it catches a rule changed in one place only.
	[Fact]
	public void ItNoticesADifference() {
		JsonElement defaults = Defaults();
		using JsonDocument changed = JsonDocument.Parse(Ruleset().GetRawText().Replace("\"divisor\": 2000", "\"divisor\": 1000"));
		List<string> differences = [];
		AssertSame(defaults.GetProperty("cultureFlip"), changed.RootElement.GetProperty("cultureFlip"), "cultureFlip", differences);
		Assert.Equal(["cultureFlip.divisor: 2000 in Rules, 1000 in ruleset.json"], differences);
	}

	// The rules reach the game: a game made from the ruleset plays by them.
	[Fact]
	public void AGameUsesTheRulesetsValues() {
		C7GameData.GameData gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		JsonElement ruleset = Ruleset();
		Assert.Equal(ruleset.GetProperty("warWeariness").GetProperty("cityLost").GetInt32(), gameData.rules.warWeariness.cityLost);
		Assert.Equal(ruleset.GetProperty("goodyHutOdds")[7].GetProperty("barbarians").GetDouble(), gameData.rules.GoodyHutOdds[7].barbarians);
		Assert.Same(gameData.rules, gameData.players[0].rules);
	}
}
