using System;
using System.Collections.Generic;
using System.IO;
using Serilog;

namespace C7Engine;

// The end of game's words, read from the Civ3 install as Civ3 reads them:
// labels.txt's #LABELS list, by line number, and script.txt's popups, by
// key. Without the install (standalone mode), OwnWording stands in for
// them with OpenCiv3's own words (see OwnEndGameWording).
public sealed class Civ3EndGameText {
	private static readonly ILogger log = Log.ForContext<Civ3EndGameText>();

	// A script.txt popup: its #caption, its text, and the choices listed
	// after the blank line that ends the text (as in GAMEOVERMAN).
	public sealed record ScriptPopup(string Caption, string Text, List<string> Choices);

	private readonly List<string> labels;
	private readonly Dictionary<string, ScriptPopup> popups;

	private Civ3EndGameText(List<string> labels, Dictionary<string, ScriptPopup> popups) {
		this.labels = labels;
		this.popups = popups;
	}

	// The text files, or null if either can't be found or read.
	public static Civ3EndGameText Load(string labelsPath, string scriptPath) {
		try {
			if (!File.Exists(labelsPath) || !File.Exists(scriptPath)) {
				return null;
			}
			return FromLines(File.ReadAllLines(labelsPath, QueryCiv3.Util.Civ3Encoding),
				File.ReadAllLines(scriptPath, QueryCiv3.Util.Civ3Encoding));
		} catch (Exception e) {
			log.Warning(e, "Couldn't read Civ3's labels.txt and script.txt for the end of the game");
			return null;
		}
	}

	// The text from the files' lines, or null if labels.txt has no #LABELS.
	public static Civ3EndGameText FromLines(string[] labelsLines, string[] scriptLines) {
		List<string> labels = ReadLabels(labelsLines);
		return labels.Count > 0 ? new Civ3EndGameText(labels, ReadPopups(scriptLines)) : null;
	}

	// OpenCiv3's own words for the end of the game, by the same indexes and
	// keys as Civ3's, for when the Civ3 text isn't there.
	public static Civ3EndGameText OwnWording() {
		List<string> labels = [];
		foreach ((int index, string label) in OwnEndGameWording.Labels) {
			while (labels.Count <= index) {
				labels.Add(null);
			}
			labels[index] = label;
		}
		Dictionary<string, ScriptPopup> popups = new(StringComparer.Ordinal);
		foreach ((string key, (string caption, string text)) in OwnEndGameWording.Popups) {
			popups[key] = new ScriptPopup(caption, text, []);
		}
		return new Civ3EndGameText(labels, popups);
	}

	// The line of #LABELS with that index, or null.
	public string Label(int index) => index >= 0 && index < labels.Count ? labels[index] : null;

	// The script.txt popup with that key, or null.
	public ScriptPopup Popup(string key) => key != null && popups.TryGetValue(key, out ScriptPopup popup) ? popup : null;

	// The #LABELS lines, counted as Civ3 counts them: every line after
	// #LABELS up to the next section.
	private static List<string> ReadLabels(string[] lines) {
		List<string> result = [];
		int start = Array.FindIndex(lines, l => l.Trim() == "#LABELS");
		if (start < 0) {
			return result;
		}
		for (int i = start + 1; i < lines.Length && !lines[i].StartsWith('#'); i++) {
			result.Add(lines[i].TrimEnd());
		}
		return result;
	}

	// The popups of script.txt. A popup starts at a line of #KEY (in
	// capitals); lines like #caption and #xs set it up, and its other lines
	// are its text, where a leading ^ starts a paragraph.
	private static Dictionary<string, ScriptPopup> ReadPopups(string[] lines) {
		Dictionary<string, ScriptPopup> result = new(StringComparer.Ordinal);
		string key = null;
		string caption = "";
		List<string> body = [];

		void Finish() {
			if (key == null) {
				return;
			}
			while (body.Count > 0 && body[^1].Length == 0) {
				body.RemoveAt(body.Count - 1);
			}
			int blank = body.IndexOf("");
			List<string> text = blank < 0 ? body : body.GetRange(0, blank);
			List<string> choices = blank < 0 ? [] : body.GetRange(blank + 1, body.Count - blank - 1).FindAll(c => c.Length > 0);
			result.TryAdd(key, new ScriptPopup(caption, string.Join("\n", text), choices));
		}

		foreach (string raw in lines) {
			string line = raw.TrimEnd();
			if (line.StartsWith('#') && line.Length > 1 && char.IsUpper(line[1])) {
				Finish();
				key = line.Substring(1).Trim();
				caption = "";
				body = [];
				continue;
			}
			if (key == null || line.StartsWith(';')) {
				continue;
			}
			if (line.StartsWith("#caption")) {
				caption = line.Substring("#caption".Length).Trim();
			} else if (!line.StartsWith('#')) {
				// A lone ^ is an empty paragraph, not the blank line that
				// ends the text.
				body.Add(line.StartsWith('^') ? (line.Length > 1 ? line.Substring(1) : " ") : line);
			}
		}
		Finish();
		return result;
	}
}
