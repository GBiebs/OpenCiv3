using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using C7GameData.Save;

namespace C7Engine.Editor;

// The rules editor's pages are generated from this schema, as Civ3's Edit
// Rules dialog has them: a page per kind of rule (Citizens, Difficulty
// Levels, General Settings...), each a list of items (or one, for General
// Settings) with Rename, Add and Delete where Civ3 has them, and the item's
// fields. Pages work on a scenario (SaveGame), so a change is plain data:
// the editor edits a copy and opens it when done (RuleEditing).

// What a field holds: a whole number, a line of text, a switch, one of a
// list of names (or none), several of them, lines of text, or numbers.
public enum RuleFieldKind { Number, Text, Check, Choice, Choices, Lines, Numbers }

public sealed class RuleField {
	// The box the field is in on its page, and the page's column for it.
	public string Group = "";
	public int Column;
	public string Label = "";
	public RuleFieldKind Kind;
	// What the field does, in our words (Civ3's help says the same).
	public string Help = "";
	// A Number's range.
	public int Min, Max = 9999;
	// A Choice may be none ("None").
	public bool AllowNone;
	// At most this many Choices (0: any).
	public int MaxChoices;
	// Kept with the scenario; the game doesn't use it yet.
	public bool NotUsed;
	public bool ReadOnly;
	// The value: an int, string, bool, string (Choice, null for none),
	// List<string> (Choices, Lines) or List<int> (Numbers).
	public Func<SaveGame, object, object> Get;
	public Action<SaveGame, object, object> Set;
	// The names a Choice or Choices picks from.
	public Func<SaveGame, List<string>> Choices;
}

public sealed class RulePage {
	public string Name = "";
	// The selector's label ("Citizens:"); null for a page of one item.
	public string ItemLabel;
	public Func<SaveGame, IList> Items;
	public Func<SaveGame, object, string> NameOf;
	// Each returns why it couldn't, or null; a null function means the page
	// hasn't that button (as in Civ3).
	public Func<SaveGame, object, string, string> Rename;
	// Adds an item like the given one (a copy) and returns it.
	public Func<SaveGame, object, object> Add;
	public Func<SaveGame, object, string> Delete;
	// What in the scenario refers to the item: the editor asks before a
	// Delete that clears them.
	public Func<SaveGame, object, List<string>> UsedBy;
	// Puts the page's rules back as the defaults have them.
	public Action<SaveGame, SaveGame> Restore;
	// Why the page's rules can't be the scenario's, or null.
	public Func<SaveGame, string> Problem;
	public List<RuleField> Fields = [];

	public bool IsSingle => ItemLabel == null;

	public string ItemName(SaveGame save, object item) => NameOf?.Invoke(save, item) ?? item?.ToString() ?? "";
}

// Building fields and pages: typed shorthands.
internal static class Rule {
	public static RuleField Number<T>(string group, string label, Func<T, int> get, Action<T, int> set, int min, int max, string help,
		bool notUsed = false, int column = 0) => new() {
			Group = group,
			Column = column,
			Label = label,
			Kind = RuleFieldKind.Number,
			Help = help,
			Min = min,
			Max = max,
			NotUsed = notUsed,
			Get = (_, o) => get((T)o),
			Set = (_, o, v) => set((T)o, (int)v),
		};

	public static RuleField Text<T>(string group, string label, Func<T, string> get, Action<T, string> set, string help,
		bool notUsed = false, int column = 0, bool readOnly = false) => new() {
			Group = group,
			Column = column,
			Label = label,
			Kind = RuleFieldKind.Text,
			Help = help,
			NotUsed = notUsed,
			ReadOnly = readOnly,
			Get = (_, o) => get((T)o) ?? "",
			Set = (_, o, v) => set((T)o, (string)v),
		};

	public static RuleField Check<T>(string group, string label, Func<T, bool> get, Action<T, bool> set, string help,
		bool notUsed = false, int column = 0) => new() {
			Group = group,
			Column = column,
			Label = label,
			Kind = RuleFieldKind.Check,
			Help = help,
			NotUsed = notUsed,
			Get = (_, o) => get((T)o),
			Set = (_, o, v) => set((T)o, (bool)v),
		};

	public static RuleField Choice<T>(string group, string label, Func<SaveGame, T, string> get, Action<SaveGame, T, string> set,
		Func<SaveGame, List<string>> choices, string help, bool allowNone = false, bool notUsed = false, int column = 0) => new() {
			Group = group,
			Column = column,
			Label = label,
			Kind = RuleFieldKind.Choice,
			Help = help,
			AllowNone = allowNone,
			NotUsed = notUsed,
			Choices = choices,
			Get = (s, o) => get(s, (T)o),
			Set = (s, o, v) => set(s, (T)o, (string)v),
		};

	public static RuleField Choices<T>(string group, string label, Func<SaveGame, T, List<string>> get, Action<SaveGame, T, List<string>> set,
		Func<SaveGame, List<string>> choices, string help, int maxChoices = 0, bool notUsed = false, int column = 0) => new() {
			Group = group,
			Column = column,
			Label = label,
			Kind = RuleFieldKind.Choices,
			Help = help,
			MaxChoices = maxChoices,
			NotUsed = notUsed,
			Choices = choices,
			Get = (s, o) => get(s, (T)o),
			Set = (s, o, v) => set(s, (T)o, (List<string>)v),
		};

	public static RuleField Lines<T>(string group, string label, Func<T, List<string>> get, Action<T, List<string>> set, string help,
		bool notUsed = false, int column = 0) => new() {
			Group = group,
			Column = column,
			Label = label,
			Kind = RuleFieldKind.Lines,
			Help = help,
			NotUsed = notUsed,
			Get = (_, o) => get((T)o) ?? [],
			Set = (_, o, v) => set((T)o, (List<string>)v),
		};

	public static RuleField Numbers<T>(string group, string label, Func<T, List<int>> get, Action<T, List<int>> set, int min, int max, string help,
		int column = 0) => new() {
			Group = group,
			Column = column,
			Label = label,
			Kind = RuleFieldKind.Numbers,
			Help = help,
			Min = min,
			Max = max,
			Get = (_, o) => get((T)o) ?? [],
			Set = (_, o, v) => set((T)o, (List<int>)v),
		};

	// A name no other item has: the name, else "name (2)", "(3)"...
	public static string Unique(string name, IEnumerable<string> taken) {
		HashSet<string> names = taken.ToHashSet();
		if (!names.Contains(name)) {
			return name;
		}
		for (int i = 2; ; ++i) {
			if (!names.Contains($"{name} ({i})")) {
				return $"{name} ({i})";
			}
		}
	}

	// A new "Prefix-n" ID string, after the highest one used.
	public static string NextId(string prefix, IEnumerable<string> ids) {
		int highest = ids.Where(i => i != null && i.StartsWith(prefix + "-"))
			.Select(i => int.TryParse(i[(prefix.Length + 1)..], out int n) ? n : 0).DefaultIfEmpty(0).Max();
		return $"{prefix}-{highest + 1}";
	}
}
