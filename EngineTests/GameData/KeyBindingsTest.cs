using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// The game's input map (C7/project.godot). Godot matches a key's release to
// an action's first event for that key, whatever the modifiers: an action
// listing Shift-P before P never released P, stayed held, and P and Shift-P
// did nothing more for the rest of the game (pillage and airlift, found by
// pressing every key in the game, 2026-11-11).
public class KeyBindingsTest {
	private static readonly string[] Modifiers = ["alt", "shift", "ctrl", "meta"];

	private sealed record KeyEvent(int key, string[] modifiers);

	private static Dictionary<string, List<KeyEvent>> Actions() {
		string project = File.ReadAllText(PathUtils.getBasePath("../C7/project.godot"));
		string input = Regex.Match(project, @"\[input\]\n(.*?)(?=\n\[|\z)", RegexOptions.Singleline).Groups[1].Value;
		Dictionary<string, List<KeyEvent>> actions = [];
		foreach (Match action in Regex.Matches(input, @"^(\w+)=\{\n""deadzone"": [\d.]+,\n""events"": \[(.*?)\]\n\}", RegexOptions.Singleline | RegexOptions.Multiline)) {
			actions[action.Groups[1].Value] = Regex.Matches(action.Groups[2].Value, @"Object\(InputEventKey,(.*?)\)")
				.Select(e => {
					string props = e.Groups[1].Value;
					int key = int.Parse(Regex.Match(props, @"""physical_keycode"":(\d+)").Groups[1].Value);
					if (key == 0) {
						key = int.Parse(Regex.Match(props, @"""keycode"":(\d+)").Groups[1].Value);
					}
					return new KeyEvent(key, Modifiers.Where(m => props.Contains($"\"{m}_pressed\":true")).ToArray());
				}).ToList();
		}
		return actions;
	}

	[Fact]
	public void TheInputMapIsRead() {
		Dictionary<string, List<KeyEvent>> actions = Actions();
		Assert.True(actions.Count > 100, $"{actions.Count} actions");
		Assert.Equal(2, actions["unit_pillage"].Count);
		Assert.Equal(["ctrl", "shift"], actions["clean_up_map"].Single().modifiers.Order());
	}

	// Of an action's events for one key, the first must have no modifiers
	// (or be the only one): a press of the key with any modifiers then
	// matches the same event its release does.
	[Fact]
	public void APlainKeyComesBeforeTheSameKeyWithModifiers() {
		List<string> wrong = [];
		foreach ((string action, List<KeyEvent> events) in Actions()) {
			foreach (IGrouping<int, KeyEvent> key in events.GroupBy(e => e.key).Where(g => g.Count() > 1)) {
				if (key.First().modifiers.Length > 0) {
					wrong.Add($"{action}: key {key.Key} with {string.Join("+", key.First().modifiers)} listed first");
				}
			}
		}
		Assert.Empty(wrong);
	}
}
