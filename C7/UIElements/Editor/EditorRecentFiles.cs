using System.Collections.Generic;
using System.IO;
using System.Linq;
using C7Engine;

// Civ3's File menu's last four files, kept in C7.ini ([ScenarioEditor]
// RecentFiles, separated by |), newest first. Files no longer there are
// left out.
public static class EditorRecentFiles {
	private const string Section = "ScenarioEditor", Key = "RecentFiles";
	private const int Count = 4;

	public static List<string> List() {
		string value = C7Settings.GetSettingValue(Section, Key) ?? "";
		return value.Split('|').Where(p => p.Length > 0 && File.Exists(p)).Take(Count).ToList();
	}

	public static void Add(string path) {
		string full = Path.GetFullPath(path);
		List<string> files = [full, .. List().Where(p => !string.Equals(Path.GetFullPath(p), full, System.StringComparison.OrdinalIgnoreCase))];
		C7Settings.SetValue(Section, Key, string.Join("|", files.Take(Count)));
		C7Settings.SaveSettings();
	}
}
