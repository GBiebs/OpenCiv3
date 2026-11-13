
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Serilog;

namespace C7GameData {

	// A class for interacting with the PediaIcons.txt file used in scenarios.
	public class PediaIcons {
		private static readonly ILogger log = Log.ForContext<PediaIcons>();

		// A mapping from the civilopedia entry name (like PRTO_Spearman or
		// PRTO_Legionary_II) to the name used in the art directory (Spearman
		// and Legionary)
		private readonly Dictionary<string, string> unitArtMapping = new();

		// A mapping from the civilopedia entry name (like TECH_Map_Making) to
		// the small icon used in the science advisor display.
		private readonly Dictionary<string, string> techSmallIconMapping = new();

		// A mapping from the all caps name of a race (BABYLON, GERMANS, 
		// RUSSIAN, etc) to the art file with happy/neutral/mad images of the
		// leader in each era, like `art\advisors\LZ_all.pcx`.
		private readonly Dictionary<string, string> raceToArtMapping = new();

		// A mapping from the building civilopedia name to the row within the
		// building icon art file.
		public readonly Dictionary<string, int> buildingToRowNumberMapping = new();

		private readonly Dictionary<string, string> civilopediaLargeIcons = new();
		private readonly Dictionary<string, string> civilopediaSmallIcons = new();

		private readonly Dictionary<string, int> unitSmallIconsIndex = new();

		private readonly string pediaIconsPath;

		// Every #KEY block of the file, by its key without the #: the lines
		// after the key up to the next key, without blank lines and comments.
		// Looked up ignoring case, as the file isn't consistent about it.
		private readonly Dictionary<string, List<string>> blocks = new(StringComparer.OrdinalIgnoreCase);

		public PediaIcons(string path) {
			pediaIconsPath = path;
			string[] lines = File.ReadAllLines(path);
			ReadBlocks(lines);

			string animNamePrefix = "#ANIMNAME_";
			for (int i = 0; i < lines.Length - 1; ++i) {
				if (lines[i].StartsWith(animNamePrefix)) {
					string civilopediaName = lines[i].Substring(animNamePrefix.Length);

					string artName = lines[i + 1];
					unitArtMapping.TryAdd(civilopediaName, artName);

					continue;
				}

				if (lines[i].StartsWith("#TECH") && !lines[i].EndsWith("_LARGE")) {
					// Drop the # from the line to get the civilopedia name
					// and then the next line is the icon path.
					techSmallIconMapping[lines[i].Substring(1)] = lines[i + 1];
				}

				if (lines[i].StartsWith("#RACE") && i + 2 < lines.Length) {
					// +2 because the line at +1 is the leaderheads neutral
					// victory image.
					raceToArtMapping[lines[i]] = lines[i + 2];
				}

				if (lines[i].StartsWith("#ICON_BLDG") && i + 2 < lines.Length) {
					// +2 because +1 specifies if the building has different
					// columns for culture groups or by era. We don't try to
					// support that yet.
					buildingToRowNumberMapping[lines[i].Substring(6)] = Int32.Parse(lines[i + 2]);
				}

				if (lines[i].StartsWith("#UNITICON_PRTO") && i + 1 < lines.Length) {
					unitSmallIconsIndex[lines[i].Substring(10)] = Int32.Parse(lines[i + 1]);
				}

				if (lines[i].StartsWith("#ICON_PRTO") && i + 2 < lines.Length) {
					civilopediaLargeIcons[lines[i].Substring(6)] = lines[i + 1];
					civilopediaSmallIcons[lines[i].Substring(6)] = lines[i + 2];
				}
			}
		}

		private void ReadBlocks(string[] lines) {
			List<string> block = null;
			foreach (string rawLine in lines) {
				string line = rawLine.Trim();
				if (line.StartsWith('#')) {
					string key = line.Substring(1).Trim();
					block = new List<string>();
					// The first block with a key wins, as with the mappings
					// below that use TryAdd.
					blocks.TryAdd(key, block);
					continue;
				}
				if (block != null && line.Length > 0 && !line.StartsWith(';')) {
					block.Add(line.Replace('\\', '/'));
				}
			}
		}

		// The lines after #key, or null if the file has no such key.
		public IReadOnlyList<string> GetBlock(string key) {
			return key != null && blocks.TryGetValue(key.Trim(), out List<string> block) ? block : null;
		}

		// The Civilopedia's large (128x128) or small (32x32) icon for an
		// entry, as a path relative to the Civ3 folder, or null if the file
		// doesn't have one. Era (0 to 3) picks between the icons of buildings
		// that change with the era, and culture group (0 to 4) between the
		// icons of buildings that look different for each culture.
		public string GetCivilopediaIcon(string civilopediaEntry, bool large, int era = 0, int cultureGroup = 0) {
			if (string.IsNullOrWhiteSpace(civilopediaEntry)) {
				return null;
			}
			string key = civilopediaEntry.Trim();

			if (key.StartsWith("TECH_", StringComparison.OrdinalIgnoreCase)) {
				IReadOnlyList<string> tech = GetBlock(large ? key + "_LARGE" : key);
				return tech is { Count: > 0 } ? tech[0] : null;
			}

			IReadOnlyList<string> icons = GetBlock("ICON_" + key);
			if (icons == null || icons.Count == 0) {
				return null;
			}

			if (key.StartsWith("BLDG_", StringComparison.OrdinalIgnoreCase)) {
				// A type (SINGLE, ERA or CULTURE), the row of the building's
				// icon in the city screen's art, then the large icons
				// followed by as many small ones.
				if (icons.Count < 3) {
					return null;
				}
				List<string> files = new();
				for (int i = 2; i < icons.Count; i++) {
					files.Add(icons[i]);
				}
				int perSize = Math.Max(1, files.Count / 2);
				int index = icons[0].ToUpperInvariant() switch {
					"ERA" => era,
					"CULTURE" => cultureGroup,
					_ => 0,
				};
				index = Math.Clamp(index, 0, perSize - 1);
				int at = large ? index : perSize + index;
				return at < files.Count ? files[at] : files[0];
			}

			if (large) {
				return icons[0];
			}
			return icons.Count > 1 ? icons[1] : icons[0];
		}

		// A scenario's own tech with no icon of its own in the PediaIcons
		// file (a BIQ that comes without its mod folder) has none.
		public string GetTechIconPath(string civilopediaEntry) {
			if (!techSmallIconMapping.TryGetValue(civilopediaEntry, out string path)) {
				log.Warning($"Could not find the icon of tech {civilopediaEntry} in PediaIcons file '{pediaIconsPath}'");
				return "";
			}
			return path;
		}

		public MainArt GetUnitMainArt(string civilopediaEntry) {
			if (!unitArtMapping.TryGetValue(civilopediaEntry, out string artName)) {
				log.Warning($"Could not find #ANIMNAME_{civilopediaEntry} in PediaIcons file '{pediaIconsPath}");
				KeyValuePair<string, string> similar = unitArtMapping.FirstOrDefault(e => e.Key.StartsWith(civilopediaEntry));
				// A unit with no art named at all (a mod's PediaIcons file that
				// leaves one out) gets the name its entry gives, and the game
				// draws it with a stock unit's art if no art has that name
				// (see AnimationManager.FallbackArtFor).
				artName = similar.Value?.Replace($"{civilopediaEntry}_", "") ?? civilopediaEntry.Replace("PRTO_", "");
			}

			var variations = unitArtMapping
				.Where(e => e.Key != civilopediaEntry && e.Key.StartsWith(civilopediaEntry))
				.ToDictionary(e => e.Key.Replace($"{civilopediaEntry}_", ""), e => e.Value);

			return new MainArt() {
				defaultName = artName,
				variations = variations.Count > 0 ? variations : null,
			};
		}

		public ThumbNailArt GetUnitThumbnailArt(string civilopediaEntry, int index) {
			var variations = unitSmallIconsIndex
				.Where(e => e.Key != civilopediaEntry && e.Key.StartsWith(civilopediaEntry))
				.ToDictionary(e => e.Key.Replace($"{civilopediaEntry}_", ""), e => e.Value);

			return new ThumbNailArt() {
				defaultIndex = index,
				variations = variations.Count > 0 ? variations : null,
			};
		}

		// A unit with no icons in the PediaIcons file (a mod's file that leaves
		// one out, as QC 1.70's Minuteman) shows a Warrior's, or another stock
		// unit's, as its art falls back to a stock unit's (see GetUnitMainArt).
		public PediaArt GetUnitCivilopediaArt(string civilopediaEntry) {
			bool hasIcons(string entry) => civilopediaLargeIcons.ContainsKey(entry) && civilopediaSmallIcons.ContainsKey(entry);
			string entry = hasIcons(civilopediaEntry) ? civilopediaEntry
				: hasIcons("PRTO_Warrior") ? "PRTO_Warrior"
				: civilopediaLargeIcons.Keys.FirstOrDefault(k => k.StartsWith("PRTO_") && hasIcons(k));
			if (entry != civilopediaEntry) {
				log.Warning($"Could not find the icons of {civilopediaEntry} in PediaIcons file '{pediaIconsPath}'; using {entry ?? "none"}");
			}
			if (entry == null) {
				return new PediaArt();
			}
			return new PediaArt() {
				large = civilopediaLargeIcons[entry],
				small = civilopediaSmallIcons[entry],
			};
		}

		public string GetLeaderArtName(string civilopediaEntry) {
			string key = "#" + civilopediaEntry.ToUpper();
			if (!raceToArtMapping.ContainsKey(key)) {
				return null;
			}
			return raceToArtMapping[key];
		}
	}

}
