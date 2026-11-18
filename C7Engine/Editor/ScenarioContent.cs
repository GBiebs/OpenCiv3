using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using C7GameData;
using C7GameData.Save;

namespace C7Engine.Editor;

// A scenario's own files beside it, as Civ3 lays a mod out: a scenario
// saved as Scenarios/Name.json keeps its art and text in Scenarios/Name/
// (Art/Units/..., Art/Civilopedia/Icons/..., Text/...), its search folder
// "Name" (Civ3's Scenario tab's Search Folders, SaveGame.ScenarioSearchPath).
// The game looks there first, with or without Civ3 (Util.ScenarioRoots).
// Files are copied in by the editor (ImportFile, ImportUnitArt), and the
// scenario and its folder go to a friend as one .zip (Package, Install).
public static class ScenarioContent {
	// The scenario's own folder, by its search folder: the first of its
	// search folders that isn't a path into Civ3's folders, else one named
	// after the scenario file. Relative to the scenario file's folder.
	public static string SearchFolder(string searchPath, string scenarioPath) {
		foreach (string folder in (searchPath ?? "").Split(';').Select(f => f.Trim())) {
			if (folder.Length > 0 && !folder.Contains("..") && !Path.IsPathRooted(folder)) {
				return folder;
			}
		}
		return Path.GetFileNameWithoutExtension(scenarioPath);
	}

	// The search path with the scenario's own folder in it (first, if added).
	public static string WithSearchFolder(string searchPath, string folder) {
		List<string> folders = (searchPath ?? "").Split(';').Select(f => f.Trim()).Where(f => f.Length > 0).ToList();
		if (!folders.Contains(folder, StringComparer.OrdinalIgnoreCase)) {
			folders.Insert(0, folder);
		}
		return string.Join(";", folders);
	}

	// The scenario's own folder on disk.
	public static string FolderOnDisk(string scenarioPath, string searchFolder) {
		return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(scenarioPath)), searchFolder);
	}

	// Copies a file into the scenario's folder, under mediaFolder (like
	// "Art/Civilopedia/Icons/Units"), and returns its media path (like
	// "Art/Civilopedia/Icons/Units/knight_lg.pcx"). A file already there is
	// replaced.
	public static string ImportFile(string scenarioPath, string searchFolder, string source, string mediaFolder) {
		string folder = Path.Combine(FolderOnDisk(scenarioPath, searchFolder), mediaFolder);
		Directory.CreateDirectory(folder);
		string target = Path.Combine(folder, Path.GetFileName(source));
		if (!Path.GetFullPath(source).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) {
			File.Copy(source, target, overwrite: true);
		}
		return (mediaFolder.TrimEnd('/', '\\') + "/" + Path.GetFileName(source)).Replace('\\', '/');
	}

	// Copies a unit's animation, Civ3's Art/Units/<Name> folder whose
	// <Name>.INI is given, into the scenario's Art/Units, and returns the
	// art's name (what a unit's art names, its INI's folder and file).
	public static string ImportUnitArt(string scenarioPath, string searchFolder, string iniPath) {
		string sourceFolder = Path.GetDirectoryName(Path.GetFullPath(iniPath));
		string name = Path.GetFileNameWithoutExtension(iniPath);
		string target = Path.Combine(FolderOnDisk(scenarioPath, searchFolder), "Art", "Units", name);
		Directory.CreateDirectory(target);
		foreach (string file in Directory.GetFiles(sourceFolder)) {
			string to = Path.Combine(target, Path.GetFileName(file));
			if (!Path.GetFullPath(file).Equals(Path.GetFullPath(to), StringComparison.OrdinalIgnoreCase)) {
				File.Copy(file, to, overwrite: true);
			}
		}
		return name;
	}

	// The scenario and its folder as one .zip: Name.json and Name/... at
	// its top, as they go into a Scenarios folder.
	public static void Package(string scenarioPath, string searchFolder, string zipPath) {
		if (File.Exists(zipPath)) {
			File.Delete(zipPath);
		}
		using ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
		zip.CreateEntryFromFile(scenarioPath, Path.GetFileName(scenarioPath));
		string folder = FolderOnDisk(scenarioPath, searchFolder);
		if (Directory.Exists(folder)) {
			foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories)) {
				string entry = Path.Combine(searchFolder, Path.GetRelativePath(folder, file)).Replace('\\', '/');
				zip.CreateEntryFromFile(file, entry);
			}
		}
	}

	// Puts a packaged scenario into the Scenarios folder, and returns its
	// scenario file (the .json at the zip's top). Refuses entries that would
	// land outside the folder.
	public static string Install(string zipPath, string scenariosFolder) {
		string root = Path.GetFullPath(scenariosFolder);
		string scenario = null;
		using ZipArchive zip = ZipFile.OpenRead(zipPath);
		foreach (ZipArchiveEntry entry in zip.Entries) {
			string target = Path.GetFullPath(Path.Combine(root, entry.FullName));
			if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) {
				throw new InvalidDataException($"The package's {entry.FullName} would go outside the Scenarios folder.");
			}
			if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) {
				Directory.CreateDirectory(target);
				continue;
			}
			Directory.CreateDirectory(Path.GetDirectoryName(target));
			entry.ExtractToFile(target, overwrite: true);
			if (!entry.FullName.Contains('/') && !entry.FullName.Contains('\\')
					&& Path.GetExtension(entry.FullName).Equals(".json", StringComparison.OrdinalIgnoreCase)) {
				scenario = target;
			}
		}
		return scenario ?? throw new InvalidDataException("The package has no scenario (.json) at its top.");
	}

	// Civ3's limits and what the game needs, as a list of problems (empty if
	// none): Civ3's editor checks "at least 2 civs; at most 32 civs and 31
	// players; 512 cities, 256 improvements, one palace; default units with
	// at most one AI strategy; unit art present". hasUnitArt says whether
	// the game can draw a unit art name (its own art, or the scenario's or
	// Civ3's INI), hasPicture whether it finds a Civilopedia picture.
	public static List<string> Check(SaveGame save, Func<string, bool> hasUnitArt = null, Func<string, bool> hasPicture = null) {
		List<string> problems = [];
		int civs = save.Civilizations.Count(c => !c.isBarbarian);
		if (civs < 2) {
			problems.Add($"The rules have {civs} civilization{(civs == 1 ? "" : "s")}; a game needs at least 2.");
		}
		if (civs > 32) {
			problems.Add($"The rules have {civs} civilizations; Civ3 allows 32.");
		}
		int players = save.Players.Count(p => !p.isBarbarian);
		if (players > 31) {
			problems.Add($"The scenario has {players} players; Civ3 allows 31.");
		}
		if (save.Cities.Count > 512) {
			problems.Add($"The map has {save.Cities.Count} cities; Civ3 allows 512.");
		}
		if (save.Buildings.Count > 256) {
			problems.Add($"The rules have {save.Buildings.Count} improvements; Civ3 allows 256.");
		}
		int palaces = save.Buildings.Count(b => b.flags.Contains(SaveBuilding.Flag.IsCenterOfEmpire));
		if (palaces != 1) {
			problems.Add(palaces == 0 ? "No improvement is the palace (Center of Empire)." : $"{palaces} improvements are the palace (Center of Empire); there must be one.");
		}
		foreach (string unit in DefaultUnits(save.Rules)) {
			SaveUnitPrototype type = save.UnitPrototypes.Find(u => u.name == unit);
			if (type == null) {
				problems.Add($"The default unit {unit} isn't one of the rules' units.");
			} else if (type.flags.Count(f => f.ToString().StartsWith("AI")) > 1) {
				problems.Add($"The default unit {unit} has more than one AI strategy.");
			}
		}
		foreach (SaveUnitPrototype unit in save.UnitPrototypes) {
			string art = unit.art.mainArt.defaultName;
			if (hasUnitArt != null) {
				if (string.IsNullOrEmpty(art)) {
					problems.Add($"{unit.name} has no animation art.");
				} else if (!hasUnitArt(art)) {
					problems.Add($"{unit.name}'s animation ({art}: Art/Units/{art}/{art}.INI) isn't found.");
				}
			}
			if (hasPicture != null) {
				foreach (string picture in new[] { unit.art.pediaArt.small, unit.art.pediaArt.large }) {
					if (!string.IsNullOrEmpty(picture) && !hasPicture(picture)) {
						problems.Add($"{unit.name}'s Civilopedia picture {picture} isn't found.");
					}
				}
			}
		}
		return problems;
	}

	// The rules' default units (General Settings).
	private static IEnumerable<string> DefaultUnits(Rules rules) {
		return new[] {
			rules.StartUnitType1, rules.StartUnitType2, rules.ScoutUnitType, rules.SlaveUnitType,
			rules.BattleCreatedUnit, rules.BuildArmyUnit, rules.FlagUnitType,
		}.Where(u => !string.IsNullOrEmpty(u)).Distinct();
	}
}
