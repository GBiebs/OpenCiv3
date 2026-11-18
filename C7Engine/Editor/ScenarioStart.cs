using System;
using System.Collections.Generic;
using System.Linq;
using C7GameData;
using C7GameData.Save;

namespace C7Engine.Editor;

// What a scenario made in the editor asks of the game when it starts, beyond
// what the save itself sets up: called by Load Scenario as it starts a
// scenario with players (ScenarioSetup), after the human's civilization is
// settled (AssignCivilizations).
public static class ScenarioStart {
	public static void Apply(SaveGame save) {
		SwitchesToRules(save);
		ApplyPlayerSlots(save);
		ComputerDifficulties(save);
		CivSpecificKings(save);
		NameAutoNamedCities(save);
		GiveStartUnits(save);
		StartWithEmbassies(save);
		LockAlliances(save);
		RevealTiles(save);
		RevealMap(save);
	}

	// The Scenario tab's switches the game reads from its rules (the
	// editor writes both; a scenario saved before it did has them only in
	// its ScenarioInfo).
	private static void SwitchesToRules(SaveGame save) {
		ScenarioInfo info = save.Scenario;
		if (info == null || save.Rules == null) {
			return;
		}
		save.Rules.CivSpecificAbilities = info.civSpecificAbilities;
		save.Rules.CulturallyLinkedStart = info.culturallyLinkedStart;
		save.Rules.RespawnAiPlayers = info.respawnAiPlayers;
		save.Rules.PreserveRandomSeed = info.preserveRandomSeed;
		save.Rules.RetainCultureOnCapture = info.retainCulture;
		save.Rules.TimeLimitMinutes = info.timeLimitMinutes;
	}

	// A civilization the human may play, and the player it makes them.
	public sealed record HumanChoice(string Civilization, string Player);

	// The civilizations Load Scenario offers the human (Civ3's Player
	// Properties): each player a human may play (Human Player; none ticked:
	// every player) whose civilization is fixed; for an "Any" player, every
	// civilization no fixed player has; for a "Random" player, one drawn
	// now. Only the scenario's playable civilizations.
	public static List<HumanChoice> HumanChoices(SaveGame save, Random random) {
		List<SavePlayer> players = CivPlayers(save);
		List<SavePlayer> pickable = players.Where(p => p.canBePicked).ToList();
		if (pickable.Count == 0) {
			pickable = players;
		}
		List<HumanChoice> choices = pickable.Where(p => ChoiceOf(save, p) == CivChoice.Fixed && ScenarioRules.IsPlayable(save, p.civilization))
			.Select(p => new HumanChoice(p.civilization, p.id.ToString())).ToList();
		List<string> free = FreeCivilizations(save).Where(c => ScenarioRules.IsPlayable(save, c)).ToList();
		foreach (SavePlayer random1 in pickable.Where(p => ChoiceOf(save, p) == CivChoice.Random)) {
			if (free.Count > 0) {
				string civ = free[random.Next(free.Count)];
				free.Remove(civ);
				choices.Add(new HumanChoice(civ, random1.id.ToString()));
			}
		}
		if (pickable.FirstOrDefault(p => ChoiceOf(save, p) == CivChoice.Any) is SavePlayer any) {
			choices.AddRange(free.Select(c => new HumanChoice(c, any.id.ToString())));
		}
		return choices;
	}

	// Units marked "Use Civ-Specific King Unit" (Civ3's Unit Properties)
	// become their owner's civilization's king (Civ3's Civilizations page),
	// once the civilizations are settled. A civilization without one keeps
	// the unit as placed.
	private static void CivSpecificKings(SaveGame save) {
		foreach (SaveUnit unit in save.Units.Where(u => u.useCivSpecificKing)) {
			string civ = save.Players.Find(p => p.id == unit.owner)?.civilization;
			string king = save.Civilizations.Find(c => c.name == civ)?.kingUnit;
			if (king != null && save.UnitPrototypes.Any(p => p.name == king)) {
				unit.prototype = king;
			}
		}
	}

	// Each computer player the scenario gives a difficulty plays at it
	// (its AI bonuses; GameData.AiBonusDifficulty).
	private static void ComputerDifficulties(SaveGame save) {
		foreach (SavePlayer player in save.Players.Where(p => !p.human)) {
			player.difficulty = DifficultyFor(save, player.id.ToString());
		}
	}

	// The difficulty the scenario sets for the player (Civ3's Player
	// Properties), or null for the player's choice.
	public static string DifficultyFor(SaveGame save, string player) {
		return save.Scenario?.players.Find(s => s.player == player)?.difficulty;
	}

	// Makes the chosen player the human with the chosen civilization, and
	// gives the other "Any" and "Random" players civilizations no one has.
	public static void AssignCivilizations(SaveGame save, HumanChoice choice, Random random) {
		List<SavePlayer> players = CivPlayers(save);
		SavePlayer human = players.Find(p => p.id.ToString() == choice.Player)
			?? players.Find(p => p.civilization == choice.Civilization);
		if (human != null && human.civilization != choice.Civilization) {
			SetCivilization(save, human, choice.Civilization);
		}
		foreach (SavePlayer player in save.Players) {
			player.human = player == human;
		}
		foreach (SavePlayer player in players.Where(p => p != human && ChoiceOf(save, p) != CivChoice.Fixed)) {
			List<string> free = FreeCivilizations(save, human);
			if (free.Count > 0) {
				SetCivilization(save, player, free[random.Next(free.Count)]);
			}
			// Settled: Any and Random no more.
			save.Scenario.players.Find(s => s.player == player.id.ToString()).civ = CivChoice.Fixed;
		}
		if (human != null && save.Scenario?.players.Find(s => s.player == human.id.ToString()) is PlayerSlot slot) {
			slot.civ = CivChoice.Fixed;
		}
	}

	private static CivChoice ChoiceOf(SaveGame save, SavePlayer player) {
		return save.Scenario?.players.Find(s => s.player == player.id?.ToString())?.civ ?? CivChoice.Fixed;
	}

	// The players that are civilizations (not the barbarians or the city
	// states), in the game.
	private static List<SavePlayer> CivPlayers(SaveGame save) {
		return save.Players.Where(p => p.isIncludedInGame && save.Civilizations.Find(c => c.name == p.civilization) is Civilization civ
			&& !civ.isBarbarian && !civ.IsIndependent).ToList();
	}

	// The civilizations no fixed player has (nor the one given), in the
	// rules' order.
	private static List<string> FreeCivilizations(SaveGame save, SavePlayer settled = null) {
		HashSet<string> taken = CivPlayers(save).Where(p => p == settled || ChoiceOf(save, p) == CivChoice.Fixed)
			.Select(p => p.civilization).ToHashSet();
		return save.Civilizations.Where(c => !c.isBarbarian && !c.IsIndependent && !taken.Contains(c.name)).Select(c => c.name).ToList();
	}

	// The player becomes the civilization: its units and citizens of its
	// old one too. A player kept for the civilization outside the game
	// takes the old one in exchange.
	private static void SetCivilization(SaveGame save, SavePlayer player, string civilization) {
		string old = player.civilization;
		if (save.Players.Find(p => p != player && p.civilization == civilization) is SavePlayer other) {
			other.civilization = old;
		}
		player.civilization = civilization;
		foreach (SaveUnit unit in save.Units.Where(u => Equals(u.owner, player.id) && u.nationality == old)) {
			unit.nationality = civilization;
		}
		foreach (SaveCity city in save.Cities.Where(c => Equals(c.owner, player.id))) {
			foreach (SaveCityResident resident in city.residents.Where(r => r.nationality == old)) {
				resident.nationality = civilization;
			}
		}
		// A player in its old civilization's colour (another player may now
		// be that civilization) takes the new one's; a colour of its own
		// stays.
		Civilization was = save.Civilizations.Find(c => c.name == old);
		Civilization civ = save.Civilizations.Find(c => c.name == civilization);
		if (was != null && civ != null && player.primaryColorIndex == was.primaryColorIndex) {
			player.primaryColorIndex = civ.primaryColorIndex;
			player.secondaryColorIndex = civ.secondaryColorIndex;
		}
	}

	// Civilization Defaults: the civilization's colour and free advances;
	// otherwise the player's own leader name and gender.
	private static void ApplyPlayerSlots(SaveGame save) {
		foreach (PlayerSlot slot in save.Scenario?.players ?? []) {
			SavePlayer player = save.Players.Find(p => p.id?.ToString() == slot.player);
			Civilization civ = save.Civilizations.Find(c => c.name == player?.civilization);
			if (civ == null) {
				continue;
			}
			if (slot.civDefaults) {
				player.primaryColorIndex = civ.primaryColorIndex;
				player.secondaryColorIndex = civ.secondaryColorIndex;
				player.knownTechs = [.. civ.startingTechs];
				continue;
			}
			if (!string.IsNullOrWhiteSpace(slot.leaderName)) {
				civ.leader = slot.leaderName;
			}
			if (slot.leaderGender is Gender gender) {
				civ.leaderGender = gender;
			}
		}
	}

	// Civ3's Reveal Entire Map: every player knows every tile.
	private static void RevealMap(SaveGame save) {
		if (save.Scenario?.revealMap != true) {
			return;
		}
		List<TileLocation> all = save.Map.tiles.Select(t => new TileLocation(t.X, t.Y)).ToList();
		foreach (SavePlayer player in save.Players.Where(p => !p.isBarbarian)) {
			player.tileKnowledge = [.. all];
		}
	}

	// A player's own starting location gets its starting units (Player
	// Properties), else the rules' start units, as a Civ3 scenario's
	// player starts are given theirs (ImportCiv3), at the default
	// experience level.
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
			List<StartUnit> own = save.Scenario.players.Find(s => s.player == start.player)?.startUnits;
			IEnumerable<string> types = own?.Count > 0
				? own.SelectMany(u => Enumerable.Repeat(u.type, Math.Max(0, u.count)))
				: [save.Rules.StartUnitType1, save.Rules.StartUnitType2];
			foreach (string type in types) {
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

	// Civ3's Start with Embassies: each such player has an embassy with
	// every other one, so they've met (at peace, unless an alliance war
	// says otherwise).
	private static void StartWithEmbassies(SaveGame save) {
		List<SavePlayer> players = (save.Scenario?.players ?? []).Where(s => s.startWithEmbassies)
			.Select(s => save.Players.Find(p => p.id?.ToString() == s.player)).Where(p => p != null).ToList();
		foreach (SavePlayer player in players) {
			foreach (SavePlayer other in players.Where(o => o != player)) {
				PlayerRelationship relationship = Met(player, other);
				relationship.hasEmbassy = true;
			}
		}
	}

	// Civ3's Locked Alliances: allies have met and are at peace with a
	// mutual protection pact; alliances at war have met at war. The game
	// keeps both for good (GameData.AreInLockedPeace, AreInLockedWar).
	private static void LockAlliances(SaveGame save) {
		List<SavePlayer> members = save.Players.Where(p => p.alliance != null).ToList();
		HashSet<(string, string)> wars = save.AllAllianceWars().SelectMany(w => new[] { w, (w.Item2, w.Item1) }).ToHashSet();
		foreach (SavePlayer player in members) {
			foreach (SavePlayer other in members.Where(o => o != player)) {
				if (player.alliance == other.alliance) {
					PlayerRelationship relationship = Met(player, other);
					relationship.multiTurnDeals = [MultiTurnDeal.DEFAULT_PEACE, MultiTurnDeal.DEFAULT_MUTUAL_PROTECTION_PACT];
				} else if (wars.Contains((player.alliance, other.alliance))) {
					Met(player, other).multiTurnDeals = [];
				}
			}
		}
	}

	// The player's relationship with the other, made (met at the start, at
	// peace) if they hadn't one.
	private static PlayerRelationship Met(SavePlayer player, SavePlayer other) {
		string key = other.id.ToString();
		if (!player.playerRelationships.TryGetValue(key, out PlayerRelationship relationship)) {
			relationship = new PlayerRelationship { firstContactTurn = 0, multiTurnDeals = [MultiTurnDeal.DEFAULT_PEACE] };
			player.playerRelationships[key] = relationship;
		}
		return relationship;
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
