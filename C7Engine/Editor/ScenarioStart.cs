using System.Collections.Generic;
using System.Linq;
using C7GameData;
using C7GameData.Save;

namespace C7Engine.Editor;

// What a scenario made in the editor asks of the game when it starts, beyond
// what the save itself sets up: called by Load Scenario as it starts a
// scenario with players (ScenarioSetup).
public static class ScenarioStart {
	public static void Apply(SaveGame save) {
		NameAutoNamedCities(save);
		GiveStartUnits(save);
		RevealTiles(save);
	}

	// A player's own starting location gets the rules' start units, as a
	// Civ3 scenario's player starts are given theirs (ImportCiv3), at the
	// default experience level.
	private static void GiveStartUnits(SaveGame save) {
		List<PlayerStart> starts = save.Scenario?.playerStarts;
		if (starts == null || starts.Count == 0) {
			return;
		}
		ID.Factory ids = new(save);
		ExperienceLevel level = save.ExperienceLevels.Find(e => e.key == save.DefaultExperienceLevel) ?? save.ExperienceLevels.FirstOrDefault();
		foreach (PlayerStart start in starts) {
			SavePlayer player = save.Players.Find(p => p.id?.ToString() == start.player);
			if (player == null || player.isBarbarian) {
				continue;
			}
			foreach (string type in new[] { save.Rules.StartUnitType1, save.Rules.StartUnitType2 }) {
				if (save.UnitPrototypes.Find(p => p.name == type) is not SaveUnitPrototype proto) {
					continue;
				}
				save.Units.Add(new SaveUnit {
					id = ids.CreateID(type),
					name = type,
					nationality = player.civilization,
					prototype = type,
					owner = player.id,
					previousLocation = new TileLocation(-1, -1),
					currentLocation = new TileLocation(start.x, start.y),
					hitPointsRemaining = (level?.baseHitPoints ?? 3) + proto.hpBonus,
					movePointsRemaining = proto.movement,
					experience = level?.key,
					facingDirection = TileDirection.SOUTHEAST,
				});
			}
		}
	}

	// The tiles painted clear of fog are known to every player.
	private static void RevealTiles(SaveGame save) {
		List<TileLocation> revealed = save.Scenario?.revealedTiles;
		if (revealed == null || revealed.Count == 0) {
			return;
		}
		foreach (SavePlayer player in save.Players.Where(p => !p.isBarbarian)) {
			HashSet<(int, int)> known = player.tileKnowledge.Select(t => (t.X, t.Y)).ToHashSet();
			player.tileKnowledge.AddRange(revealed.Where(t => known.Add((t.X, t.Y))));
		}
	}

	// Civ3's City Properties "Auto": each such city takes the first name of
	// its owner's civilization no city has yet, in the cities' order.
	private static void NameAutoNamedCities(SaveGame save) {
		List<string> auto = save.Scenario?.autoNamedCities;
		if (auto == null || auto.Count == 0) {
			return;
		}
		HashSet<string> autoIds = auto.ToHashSet();
		HashSet<string> taken = save.Cities.Where(c => !autoIds.Contains(c.id.ToString())).Select(c => c.name).ToHashSet();
		foreach (SaveCity city in save.Cities.Where(c => autoIds.Contains(c.id.ToString()))) {
			SavePlayer owner = save.Players.Find(p => Equals(p.id, city.owner));
			Civilization civ = save.Civilizations.Find(c => c.name == owner?.civilization);
			string name = civ?.cityNames.FirstOrDefault(n => !taken.Contains(n));
			if (name != null) {
				city.name = name;
				taken.Add(name);
			}
		}
	}
}
