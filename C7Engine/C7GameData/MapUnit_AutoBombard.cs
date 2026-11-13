using System.Linq;
using System.Threading.Tasks;
using C7Engine;

namespace C7GameData {
	// Civ3's automated bombarding: "Automate: Bombard (Shift-B)" for
	// artillery and ships, and for aircraft "Automate: Bombing (Shift-B)"
	// and "Automate: Precision Bombing (Shift-P)" (labels.txt,
	// #UNIT_ACTIONS). The player picks a target, and the unit goes on
	// bombarding it every turn: "You press Shift-B then select your target.
	// What it does is cause your bombard units to keep firing on that same
	// target, turn after turn, until there is nothing left to bombard"
	// (https://forums.civfanatics.com/threads/stack-bombardment.87621);
	// "simply press the auto-bombard button then indicate the square you
	// wish to bombard by clicking on it" and "Auto-bombard just tells 1
	// Artillery to bombard the same square every turn" (#2 and #5,
	// https://forums.civfanatics.com/threads/how-exactly-do-i-use-auto-bombard.276907/);
	// "you set your unit to bombard a certain square every turn and it does
	// so until either the square can't be bombarded or you stop the unit"
	// (meriadoc, https://codehappy.net/apolyton/threads/71619-1.htm).
	// "ICBM's can no longer auto-bombard" (README-PTW).
	//
	// Per the project owner, only civs we're at war with (and the
	// barbarians) are bombarded automatically, so it never starts a war: a
	// target where it would hit anyone else ends it, as one with nothing
	// left to hit does. UNVERIFIED (no Civ3 source found): it fires as soon
	// as it is ordered, and then once at the start of each turn; and it ends
	// when the unit is selected (as fortifying does), or it is given another
	// order.
	public partial class MapUnit {
		public enum AutoBombardMode { None, Bombard, Bombing, PrecisionBombing }

		public AutoBombardMode autoBombard = AutoBombardMode.None;
		public Tile autoBombardTarget;
		// The turn it last fired automatically, so it fires once a turn.
		public int autoBombardTurn = -1;

		public bool IsAutoBombarding() => autoBombard != AutoBombardMode.None;

		// The unit action whose target an automated bombardment of the mode
		// picks (see GetAvailableActions).
		public static UnitAction ActionFor(AutoBombardMode mode) => mode switch {
			AutoBombardMode.Bombing => UnitAction.AutoBombing,
			AutoBombardMode.PrecisionBombing => UnitAction.AutoPrecisionBombing,
			_ => UnitAction.AutoBombard,
		};

		public static AutoBombardMode? ModeFor(UnitAction action) => action switch {
			UnitAction.AutoBombard => AutoBombardMode.Bombard,
			UnitAction.AutoBombing => AutoBombardMode.Bombing,
			UnitAction.AutoPrecisionBombing => AutoBombardMode.PrecisionBombing,
			_ => null,
		};

		// Whether the unit can be set bombarding automatically in the mode.
		public bool CanAutomateBombard(AutoBombardMode mode) {
			if (IsLockedInArmy()) {
				return false;
			}
			return mode switch {
				AutoBombardMode.Bombard => !IsAirUnit() && HasBombardAbility() && !IsNuclearWeapon(),
				AutoBombardMode.Bombing => HasAirMission(UnitAction.Bombing) && IsAtAirBase(),
				AutoBombardMode.PrecisionBombing => HasAirMission(UnitAction.PrecisionBombing) && IsAtAirBase(),
				_ => false,
			};
		}

		// Whether the unit could bombard the tile in the mode now, hitting
		// only civs it is at war with.
		public bool CanAutoBombardTile(AutoBombardMode mode, Tile tile) {
			if (!Tile.IsTileValid(tile) || !CanAutomateBombard(mode)) {
				return false;
			}
			BombardTarget target;
			bool can = mode == AutoBombardMode.Bombard ? CanBombardTile(tile, out target)
				: mode == AutoBombardMode.Bombing ? CanAirBombTile(tile, out target)
				: CanPrecisionBombTile(tile, out target);
			return can && HitsOnlyEnemies(tile, target);
		}

		// Whether bombarding the tile hits no one we aren't at war with: not
		// the city's owner, any foreign unit there, nor for its improvements
		// the civ whose territory it is.
		private bool HitsOnlyEnemies(Tile tile, BombardTarget target) {
			bool Enemy(Player p) => p == null || p == owner || !owner.IsAtPeaceWith(p);
			if (tile.HasCity() && !Enemy(tile.cityAtTile.owner)) {
				return false;
			}
			if (tile.unitsOnTile.Any(u => u.owner != owner && !Enemy(u.owner))) {
				return false;
			}
			return target != BombardTarget.Improvement || Enemy(tile.OwningPlayer());
		}

		// Sets the unit bombarding the tile automatically, and fires at once.
		public async Task StartAutoBombard(AutoBombardMode mode, Tile tile) {
			if (mode == AutoBombardMode.None || !CanAutoBombardTile(mode, tile)) {
				return;
			}
			Wake();
			path = null;
			autoBombard = mode;
			autoBombardTarget = tile;
			autoBombardTurn = -1;
			await PerformAutoBombard();
		}

		public void StopAutoBombard() {
			autoBombard = AutoBombardMode.None;
			autoBombardTarget = null;
		}

		// Fires at the target, once a turn; with nothing left there to hit
		// (or only civs we're not at war with), it stops, and the unit waits
		// for orders.
		public async Task PerformAutoBombard() {
			int turn = EngineStorage.gameData?.turn ?? 0;
			if (!IsAutoBombarding() || autoBombardTurn == turn) {
				return;
			}
			autoBombardTurn = turn;
			AutoBombardMode mode = autoBombard;
			Tile target = autoBombardTarget;
			if (!CanAutoBombardTile(mode, target)) {
				StopAutoBombard();
				if (owner.isHuman) {
					new MsgUnitMoved(this).send();
				}
				return;
			}
			switch (mode) {
				case AutoBombardMode.Bombard:
					await Bombard(target);
					break;
				case AutoBombardMode.Bombing:
					await AirBomb(target);
					break;
				case AutoBombardMode.PrecisionBombing:
					await PrecisionBomb(target);
					break;
			}
			// A mission ends by waking the aircraft (see EndMission); one
			// that survived it carries on next turn.
			if (hitPointsRemaining > 0 && autoBombard == AutoBombardMode.None) {
				autoBombard = mode;
				autoBombardTarget = target;
			}
		}
	}
}
