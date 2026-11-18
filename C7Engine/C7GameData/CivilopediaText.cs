using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Serilog;

namespace C7GameData {

	// One article of Civ3's Civilopedia.txt: the text shown on an entry's
	// page, and the history shown when the Description button is pressed.
	public class CivilopediaArticle {
		// The key without its #, like PRTO_Warrior or GCON_Combat.
		public string Key;

		// Only game concepts have their name in the file, on the line after
		// the key. Everything else is named by the game's rules.
		public string Name;

		// The lines of the text. Each ^ in the file starts a line, and lines
		// in the file without a ^ continue the one before. An empty string is
		// a blank line. The markup ([bold], {bold}, $LINK<text=key>, and tabs
		// between columns) is kept; see CivilopediaMarkup.
		public List<string> Lines = new();

		// The lines under #DESC_<key>, or empty if there are none.
		public List<string> DescriptionLines = new();

		public bool HasDescription => DescriptionLines.Exists(l => l.Trim().Length > 0);
	}

	// The articles of a Civilopedia.txt file, by key.
	public class CivilopediaText {
		private static readonly ILogger log = Log.ForContext<CivilopediaText>();

		// Keys are looked up ignoring case and spaces around them: the file
		// has keys like "GCON_Enslavement " and RACE_ROMANS where the rules
		// say RACE_Romans.
		private readonly Dictionary<string, CivilopediaArticle> articles = new(StringComparer.OrdinalIgnoreCase);

		// The game concepts in the order #GAME_CONCEPTS_KEYS lists them.
		public readonly List<string> GameConceptKeys = new();

		public IEnumerable<CivilopediaArticle> Articles => articles.Values;

		public int Count => articles.Count;

		public CivilopediaArticle Get(string key) {
			if (key == null) {
				return null;
			}
			return articles.GetValueOrDefault(key.Trim());
		}

		public bool Has(string key) => Get(key) != null;

		// This text with a scenario's own articles laid over it (an article
		// the scenario gives replaces the game's of that key; its history
		// only if it gives one). This text is left as it is.
		public CivilopediaText With(IEnumerable<CivilopediaEntryRule> entries) {
			CivilopediaText result = new();
			foreach (CivilopediaArticle a in articles.Values) {
				result.articles[a.Key] = a;
			}
			result.GameConceptKeys.AddRange(GameConceptKeys);
			foreach (CivilopediaEntryRule entry in entries ?? []) {
				if (string.IsNullOrWhiteSpace(entry?.Key)) {
					continue;
				}
				string key = entry.Key.Trim();
				CivilopediaArticle old = Get(key);
				result.articles[key] = new CivilopediaArticle {
					Key = key,
					Name = old?.Name,
					Lines = entry.Text?.Count > 0 ? [.. entry.Text] : old?.Lines ?? [],
					DescriptionLines = entry.History?.Count > 0 ? [.. entry.History] : old?.DescriptionLines ?? [],
				};
			}
			return result;
		}

		// Reads a Civilopedia.txt, which is Windows-1252 text. A missing or
		// unreadable file gives an empty Civilopedia rather than an error, so
		// the game still runs without Civ3's text (e.g. in standalone mode).
		public static CivilopediaText Load(string path) {
			if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
				log.Warning("No Civilopedia text at {path}", path);
				return new CivilopediaText();
			}
			try {
				return Parse(File.ReadAllLines(path, QueryCiv3.Util.Civ3Encoding));
			} catch (Exception e) {
				log.Error(e, "Could not read the Civilopedia text at {path}", path);
				return new CivilopediaText();
			}
		}

		public static CivilopediaText Parse(string text) {
			return Parse(text.Replace("\r\n", "\n").Split('\n'));
		}

		public static CivilopediaText Parse(IEnumerable<string> fileLines) {
			CivilopediaText result = new();
			CivilopediaArticle article = null;
			List<string> lines = null;
			bool inConceptKeys = false;
			bool expectName = false;

			foreach (string rawLine in fileLines) {
				string line = rawLine.TrimEnd('\r', '\n');

				if (line.StartsWith(';')) {
					continue;
				}

				if (line.StartsWith('#')) {
					string key = line.Substring(1).Trim();
					article = null;
					lines = null;
					inConceptKeys = key == "GAME_CONCEPTS_KEYS";
					expectName = false;
					if (inConceptKeys || key == "GAME_CONCEPTS" || key.StartsWith("EOF") || key.Length == 0) {
						continue;
					}

					if (key.StartsWith("DESC_", StringComparison.OrdinalIgnoreCase)) {
						string of = key.Substring("DESC_".Length);
						article = result.GetOrAdd(of);
						lines = article.DescriptionLines;
					} else {
						article = result.GetOrAdd(key);
						lines = article.Lines;
						expectName = key.StartsWith("GCON_", StringComparison.OrdinalIgnoreCase) && article.Name == null;
					}
					continue;
				}

				if (inConceptKeys) {
					if (line.Trim().Length > 0) {
						result.GameConceptKeys.Add(line.Trim());
					}
					continue;
				}

				if (lines == null) {
					continue;
				}

				if (expectName) {
					expectName = false;
					if (!line.StartsWith('^')) {
						article.Name = line.Trim();
						continue;
					}
				}

				if (line.StartsWith('^')) {
					lines.Add(line.Substring(1).TrimEnd());
				} else if (line.Trim().Length == 0) {
					// Blank lines in the file separate articles; they aren't
					// part of the text.
					continue;
				} else if (lines.Count == 0) {
					lines.Add(line.TrimEnd());
				} else {
					string previous = lines[^1];
					lines[^1] = previous.Length == 0 ? line.Trim() : previous.TrimEnd() + " " + line.Trim();
				}
			}

			foreach (CivilopediaArticle a in result.articles.Values) {
				TrimBlankLines(a.Lines);
				TrimBlankLines(a.DescriptionLines);
			}
			return result;
		}

		private CivilopediaArticle GetOrAdd(string key) {
			if (!articles.TryGetValue(key, out CivilopediaArticle article)) {
				article = new CivilopediaArticle { Key = key };
				articles[key] = article;
			}
			return article;
		}

		// Drops the blank lines at the start and end of a text; the screens
		// leave their own space above the text.
		private static void TrimBlankLines(List<string> lines) {
			while (lines.Count > 0 && lines[0].Trim().Length == 0) {
				lines.RemoveAt(0);
			}
			while (lines.Count > 0 && lines[^1].Trim().Length == 0) {
				lines.RemoveAt(lines.Count - 1);
			}
		}
	}

	// The markup of a line of Civilopedia text: [text] and {text} are in bold,
	// and $LINK<text=key> links to another article.
	public static class CivilopediaMarkup {
		public readonly record struct Span(string Text, bool Bold, string LinkKey) {
			public bool IsLink => LinkKey != null;
		}

		public static List<Span> Tokenize(string line) {
			List<Span> spans = new();
			StringBuilder text = new();
			bool bold = false;

			void Flush() {
				if (text.Length > 0) {
					spans.Add(new Span(text.ToString(), bold, null));
					text.Clear();
				}
			}

			for (int i = 0; i < line.Length; i++) {
				char c = line[i];
				if (c == '$' && string.CompareOrdinal(line, i, "$LINK<", 0, 6) == 0) {
					int close = line.IndexOf('>', i + 6);
					int equals = close < 0 ? -1 : line.LastIndexOf('=', close, close - (i + 6));
					if (close >= 0 && equals >= 0) {
						Flush();
						string linkText = StripBrackets(line.Substring(i + 6, equals - (i + 6)));
						string key = line.Substring(equals + 1, close - equals - 1).Trim();
						spans.Add(new Span(linkText, bold, key));
						i = close;
						continue;
					}
				}
				if (c == '[' || c == '{') {
					Flush();
					bold = true;
					continue;
				}
				if (c == ']' || c == '}') {
					Flush();
					bold = false;
					continue;
				}
				text.Append(c);
			}
			Flush();
			return spans;
		}

		// The text of a line without its markup.
		public static string PlainText(string line) {
			StringBuilder result = new();
			foreach (Span span in Tokenize(line)) {
				result.Append(span.Text);
			}
			return result.ToString();
		}

		// The keys of the articles a text links to, in order and without
		// repeats.
		public static List<string> LinkedKeys(IEnumerable<string> lines) {
			List<string> keys = new();
			HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
			foreach (string line in lines) {
				foreach (Span span in Tokenize(line)) {
					if (span.IsLink && seen.Add(span.LinkKey)) {
						keys.Add(span.LinkKey);
					}
				}
			}
			return keys;
		}

		private static string StripBrackets(string s) {
			return s.Replace("[", "").Replace("]", "").Replace("{", "").Replace("}", "");
		}
	}
}
