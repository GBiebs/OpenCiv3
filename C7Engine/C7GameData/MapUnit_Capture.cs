using System.Collections.Generic;
using System.Linq;
using C7Engine;

namespace C7GameData;

public partial class MapUnit {
	// Whether this unit can fight back when attacked. Units that can't
	// (workers, settlers, artillery, explorers and so on) don't fight: an enemy
	// moving onto them captures or destroys them instead.
	public bool CanDefendItself() {
		return unitType.defense > 0;
	}

	// Whether an enemy that can capture units takes this unit when it moves
	// onto it unguarded, rather than destroying it: workers and settlers (who
	// become workers), and artillery, as in Civ3.
	public bool IsCapturable() {
		return IsLandUnit() && !CanDefendItself() && (unitType.isWorker || unitType.isSettler || unitType.bombard > 0);
	}

	// Whether this unit can take an enemy city by moving in (Civ3: "only
	// combat units can capture cities"). Barbarians ransack instead.
	public bool CanTakeCities() {
		return IsLandUnit() && (unitType.attack > 0 || unitType.canCapture);
	}

	// Called when this unit has moved onto `tile`. Any enemy units left there
	// can't defend themselves, so they're captured or destroyed, and their
	// owners are told.
	internal void CaptureUnitsOn(Tile tile) {
		List<MapUnit> enemies = tile.unitsOnTile.Where(u => u.owner != owner && !owner.IsAtPeaceWith(u.owner)).ToList();
		GameData gameData = EngineStorage.gameData;
		foreach (IGrouping<Player, MapUnit> byOwner in enemies.GroupBy(u => u.owner)) {
			List<string> captured = new();
			List<string> destroyed = new();
			foreach (MapUnit enemy in byOwner) {
				// Barbarians have no use for captives.
				if (enemy.IsCapturable() && unitType.canCapture && !owner.isBarbarians) {
					captured.Add(enemy.name);
					gameData.CaptureUnit(enemy, owner);
				} else {
					destroyed.Add(enemy.name);
					enemy.RemoveFromPlay();
				}
			}
			log.Information($"{this} at {tile} captured {string.Join(", ", captured)} and destroyed {string.Join(", ", destroyed)} of {byOwner.Key}");
			new MsgUnitsCaptured(owner.id, byOwner.Key.id, tile, captured, destroyed).send();
		}
	}
}
