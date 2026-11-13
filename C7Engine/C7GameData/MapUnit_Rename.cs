using System.Linq;

namespace C7GameData;

public partial class MapUnit {
	// The longest name a unit can be given. UNVERIFIED: Civ3's own limit for
	// a unit's name; this is the one we give a city's (City.MaxNameLength).
	public const int MaxNameLength = City.MaxNameLength;

	// Gives the unit a new name, as Civ3's Rename Unit (Shift-N; labels.txt
	// "Re(n)ame unit (Shift-N)", script.txt RENAME_UNIT "Rename $UNIT0?").
	// The name is trimmed, without control characters, and cut to
	// MaxNameLength; a blank name gives the unit back its type's name.
	// The name is saved with the unit and kept when it upgrades (see
	// ChangeTypeForUpgrade). Returns whether the name changed.
	public bool Rename(string newName) {
		string clean = new string((newName ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
		if (clean.Length > MaxNameLength) {
			clean = clean.Substring(0, MaxNameLength).TrimEnd();
		}
		if (clean.Length == 0) {
			clean = unitType.name;
		}
		if (clean == name) {
			return false;
		}
		name = clean;
		return true;
	}
}
