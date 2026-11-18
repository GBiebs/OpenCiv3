using System;
using System.Collections.Generic;
using System.Linq;
using C7GameData.Save;

namespace C7Engine.Editor;

// What the rules editor does to a scenario's rules (RulePages): a field's
// value set within its range, the rules checked, and Civ3's Rules menu:
// Restore Defaults (every page back to the game's standard rules), Clear
// Rules (the page shown), and the Scenario menu's Custom Rules switch (off:
// the scenario plays the standard rules).
public static class RuleEditing {
	// Sets a field, or says why the value can't be.
	public static string SetValue(SaveGame save, RuleField field, object item, object value) {
		if (field.ReadOnly) {
			return $"{field.Label.TrimEnd(':')} can't be changed.";
		}
		if (field.Refuse?.Invoke(save, item) is string why) {
			return why;
		}
		switch (field.Kind) {
			case RuleFieldKind.Number when value is not int n || n < field.Min || n > field.Max:
				return $"{field.Label.TrimEnd(':')} is {field.Min} to {field.Max}.";
			case RuleFieldKind.Numbers when value is not List<int> numbers || numbers.Any(n => n < field.Min || n > field.Max):
				return $"{field.Label.TrimEnd(':')} are {field.Min} to {field.Max} each.";
			case RuleFieldKind.Choice when value is string name && !field.Choices(save).Contains(name):
			case RuleFieldKind.Choice when value == null && !field.AllowNone:
				return $"{value ?? "None"} isn't one of the choices for {field.Label.TrimEnd(':')}.";
			case RuleFieldKind.Choices when value is not List<string> names || names.Except(field.Choices(save)).Any():
				return $"{field.Label.TrimEnd(':')} has a name that isn't one of its choices.";
			case RuleFieldKind.Choices when field.MaxChoices > 0 && ((List<string>)value).Count > field.MaxChoices:
				return $"{field.Label.TrimEnd(':')} takes at most {field.MaxChoices}.";
			case RuleFieldKind.Lines when value is List<string> lines && lines.Any(string.IsNullOrWhiteSpace):
				return $"{field.Label.TrimEnd(':')} can't have an empty line.";
		}
		field.Set(save, item, value);
		return null;
	}

	// Why the rules can't be the scenario's (the first page's reason), or
	// null.
	public static string Problem(SaveGame save) {
		foreach (RulePage page in RulePages.All) {
			if (page.Problem?.Invoke(save) is string why) {
				return $"{page.Name}: {why}";
			}
		}
		return null;
	}

	// Civ3's Restore Defaults: every page's rules as the defaults have them
	// (what in the scenario named something gone is cleared).
	public static void RestoreDefaults(SaveGame save, SaveGame defaults) {
		SaveGame copy = defaults.Clone();
		foreach (RulePage page in RulePages.All) {
			page.Restore(save, copy);
		}
	}

	// Civ3's Clear Rules, as we read it: the page shown back to the
	// defaults.
	public static void ClearPage(SaveGame save, SaveGame defaults, RulePage page) {
		page.Restore(save, defaults.Clone());
	}

	// A scenario without Custom Rules plays the standard rules: Load
	// Scenario puts them in as it starts (the scenario may be older than
	// the game's rules).
	public static void UseStandardRulesIfNotCustom(SaveGame save, SaveGame defaults) {
		if (save.Scenario?.customRules == false && defaults != null) {
			RestoreDefaults(save, defaults);
		}
	}
}
