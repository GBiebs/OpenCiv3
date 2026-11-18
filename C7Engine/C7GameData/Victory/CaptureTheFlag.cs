using System.Collections.Generic;
using System.Linq;
using C7GameData;
using Serilog;

namespace C7Engine;

// Civ3's Capture the Flag game mode ("Capture the Unit" in the editor's
// Scenario tab; Conquests' Capture the Princess). Civ3's help: "each
// player-civilization retains a flag unit (the Princess is the default).
// This flag unit may be captured by other players / civilizations in order
// to win the game or score victory points. Depending upon the scenario,
// the person who captures the flag (princess) or scores the most victory
// points by capturing the flag (princess) multiple times will win the
// game."
//
// Our reading, where the help is silent:
// - Each civ's flag (Rules.FlagUnitType, else a unit type with the Flag
//   Unit ability) is placed in its capital when the game starts, if the
//   scenario auto-places capture units; its nationality is its civ's.
// - A unit moving onto an enemy flag with no defender takes it and carries
//   it (it is loaded on that unit). A civ can't take its own flag unless
//   the scenario lets anyone capture any flag.
// - Brought into the captor's capital, the flag scores the GAME section's
//   points for capturing it (1000) and its gold for capture, and is gone; if
//   the scenario respawns flag units, a new one appears in its civ's
//   capital (Civ3's "original starting point"; the game keeps none).
// - Under Victory Point Scoring the points decide; without it, the first to
//   bring in a flag wins (CaptureTheFlagVictory).
// - The computer players don't go after flags.
public static class CaptureTheFlag {
	private static readonly ILogger log = Log.ForContext(typeof(CaptureTheFlag));

	public static bool IsOn(GameData gameData) => gameData?.victoryConditions?.CaptureTheFlag == true;

	// Whether the unit is a flag: its type has the Flag Unit ability, or is
	// the rules' flag unit.
	public static bool IsFlag(GameData gameData, MapUnit unit) {
		return unit.unitType.isFlagUnit || (gameData.rules?.FlagUnitType is string name && unit.unitType.name == name);
	}

	public static UnitPrototype FlagType(GameData gameData) {
		string name = gameData.rules?.FlagUnitType;
		return gameData.unitPrototypes.Find(p => name != null && p.name == name) ?? gameData.unitPrototypes.Find(p => p.isFlagUnit);
	}

	// Each civ with a capital (else a unit) and no flag gets its flag there,
	// at the start of a game with auto-placed capture units.
	public static void PlaceFlags(GameData gameData) {
		if (!IsOn(gameData) || gameData.victoryConditions?.AutoPlaceCaptureUnits != true || gameData.turn > 0) {
			return;
		}
		UnitPrototype flag = FlagType(gameData);
		if (flag == null) {
			return;
		}
		foreach (Player player in gameData.players.Where(p => !p.IsIndependent && !p.defeated && p.civilization != null).ToList()) {
			if (player.units.Any(u => IsFlag(gameData, u) && u.nationality == player.civilization)) {
				continue;
			}
			Tile home = Home(player) ?? player.units.FirstOrDefault()?.location;
			if (home != null && Tile.IsTileValid(home)) {
				gameData.SpawnUnit(player, flag, home).nationality = player.civilization;
			}
		}
	}

	private static Tile Home(Player player) => (player.cities.Find(c => c.IsCapital()) ?? player.cities.FirstOrDefault())?.location;

	// Whether the captor may take the flag.
	public static bool MayCapture(GameData gameData, Player captor, MapUnit flag) {
		return IsOn(gameData) && IsFlag(gameData, flag) && !captor.IsIndependent
			&& (gameData.victoryConditions.AnyoneCapturesAnyFlag || flag.nationality != captor.civilization);
	}

	// The captor's unit takes the flag and carries it.
	public static void Capture(GameData gameData, MapUnit flag, MapUnit carrier) {
		Civilization home = flag.nationality ?? flag.owner.civilization;
		gameData.CaptureUnit(flag, carrier.owner);
		flag.nationality = home;
		flag.loadedOnUnitId = carrier.id;
		log.Information("{Captor} took the {Home} flag", carrier.owner, home?.name);
	}

	// A unit entering its civ's capital brings in the flags it carries.
	public static void OnEnterTile(GameData gameData, MapUnit carrier, Tile tile) {
		if (!IsOn(gameData) || tile.cityAtTile is not City city || city.owner != carrier.owner || !city.IsCapital()) {
			return;
		}
		foreach (MapUnit flag in carrier.Passengers().Where(p => IsFlag(gameData, p)
				&& (p.nationality != carrier.owner.civilization || gameData.victoryConditions.AnyoneCapturesAnyFlag)).ToList()) {
			Deliver(gameData, carrier.owner, flag);
		}
	}

	private static void Deliver(GameData gameData, Player captor, MapUnit flag) {
		VictoryConditions conditions = gameData.victoryConditions;
		Civilization home = flag.nationality;
		captor.victoryPoints = (int)System.Math.Min(int.MaxValue, (long)captor.victoryPoints + conditions.CaptureFlagVictoryPoints);
		captor.gold += conditions.GoldForCapture;
		++captor.flagsCaptured;
		log.Information("{Captor} brought the {Home} flag home", captor, home?.name);
		if (captor.IsToldNews) {
			string gold = conditions.GoldForCapture > 0 ? $" and {conditions.GoldForCapture} gold" : "";
			new MsgShowMilitaryAdvisorPopup(captor,
				$"We have brought the {home?.adjective ?? home?.name} {flag.unitType.name} to our capital: {conditions.CaptureFlagVictoryPoints} victory points{gold}!",
				happy: true).send();
		}
		gameData.RemoveUnit(flag);
		if (conditions.RespawnFlagUnits) {
			Player owner = gameData.players.Find(p => p.civilization == home && !p.defeated);
			if (owner != null && Home(owner) is Tile tile) {
				gameData.SpawnUnit(owner, flag.unitType, tile).nationality = home;
			}
		}
	}
}

// Capture the Flag's win without Victory Point Scoring: the first civ to
// bring a flag to its capital.
public class CaptureTheFlagVictory : IVictory {
	public string Header() => "Capture the Flag";

	public VictoryStatus Evaluate(Player player, GameData gameData) {
		return new VictoryStatus { Player = player, CurrentTurn = gameData.turn };
	}

	public bool HasVictory(VictoryStatus status) => status.Player?.flagsCaptured > 0;

	public IEnumerable<string[]> GenerateStatusRows(VictoryStatus status, List<VictoryStatus> rivalStatuses) {
		yield return ["Flags captured:", $"{status.Player?.flagsCaptured ?? 0}", "", "", "", ""];
	}
}
