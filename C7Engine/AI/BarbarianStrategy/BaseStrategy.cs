using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using C7Engine.AI.UnitAI;
using C7GameData;
using C7GameData.AIData;

namespace C7Engine;

internal abstract class BaseStrategy : IBarbarianStrategy {
	// TODO: Determine how barbarian AI is implemented in Civ3
	// TODO: What are the key parameters influencing barbarian activity levels in Civ3?

	/// <summary>
	/// Observe - Orient - Decide - Act.
	/// 
	/// Note: This approach may or may not have anything to do with how Civ3 implements barbarian AI.
	/// </summary>
	public async Task PlayUnitTurn(Player player, MapUnit unit) {
		// "Observe: Collect data and information from the environment through senses and feedback."

		// Wake up the unit if there's a reason to do so
		if (ShouldWake(player, unit))
			unit.Wake();

		// Skip units that didn't wake up
		if (unit.isFortified)
			return;

		if (await KeepCamp(player, unit))
			return;

		var orientation = await Orient(player, unit);
		var plan = await Decide(player, unit, orientation);
		var result = await Act(player, unit, plan);

		// TODO: store result
	}

	/// <summary>
	/// Wake the unit if a foreign unit or the borders of a civ are in sight. 
	/// </summary>
	private static bool ShouldWake(Player player, MapUnit unit) {
		var tiles = player.tileKnowledge.GetTilesVisibleToUnit(unit.location);
		foreach (Tile t in tiles) {
			if (t.unitsOnTile.Count > 0 && t.unitsOnTile[0].owner != player)
				return true;

			if (t.OwningPlayer() != null)
				return true;
		}

		return false;
	}

	/// <summary>
	/// Civ3's camps keep their last unit (Conquests players' reports): it
	/// leaves only to fall on a defenseless unit next to the camp (a worker,
	/// a settler), and a barbarian next to an empty camp goes back into it.
	/// True if the unit's turn was played here.
	/// </summary>
	private async Task<bool> KeepCamp(Player player, MapUnit unit) {
		if (!unit.IsLandUnit() || !unit.IsCombatUnit()) {
			return false;
		}
		Tile here = unit.location;
		if (IsLastUnitInCamp(player, unit)) {
			if (RaidsFromCamp) {
				foreach (KeyValuePair<TileDirection, Tile> next in here.neighbors) {
					if (IsDefenseless(next.Value, player) && unit.CanEnter(next.Value)) {
						await unit.Move(next.Key);
						return true;
					}
				}
			}
			return false; // (Decide keeps it in the camp.)
		}
		foreach (KeyValuePair<TileDirection, Tile> next in here.neighbors) {
			if (next.Value.hasBarbarianCamp && next.Value.unitsOnTile.Count == 0 && unit.CanEnter(next.Value)) {
				await unit.Move(next.Key);
				return true;
			}
		}
		return false;
	}

	// The only barbarian in its camp (its guard).
	internal static bool IsLastUnitInCamp(Player player, MapUnit unit) {
		return unit.location.hasBarbarianCamp && unit.location.unitsOnTile.Count(u => u.owner == player) == 1;
	}

	// Units another civ left on open ground with no defense at all.
	internal static bool IsDefenseless(Tile tile, Player barbarians) {
		return tile.cityAtTile == null && tile.unitsOnTile.Count > 0
			&& tile.unitsOnTile.All(u => u.owner != barbarians && u.IsLandUnit() && u.unitType.defense == 0);
	}

	// Whether a camp's guard may step out for a defenseless unit next to it
	// (not when barbarians keep to their camps).
	protected virtual bool RaidsFromCamp => true;

	/// <summary>
	/// "Orient:Analyze and synthesize data to form a mental perspective, considering experience,
	/// culture, and new information. This is considered the most important phase of the OODA loop."
	/// </summary>
	protected Task<Orientation> Orient(Player player, MapUnit unit) {
		return Task.FromResult(new Orientation {
			IsLastUnitInCamp = IsLastUnitInCamp(player, unit),
			CombatIntel = CombatAI.MakeAiData(unit, player)
		});
	}

	/// <summary>
	/// "Decide: Formulate a plan or course of action based on the orientation."
	/// </summary>
	protected async Task<UnitAI> Decide(Player player, MapUnit unit, Orientation orientation) {
		// Barbarians defend their camp if it is unguarded.
		if (orientation.IsLastUnitInCamp)
			return new DefenderAI(DefenderAI.MakeAiDataForDefendInPlace(unit, player));

		// Decide whether to engage enemy units
		if (orientation.CanEngage() && DecideToEngage(player, unit, orientation))
			return new CombatAI(orientation.CombatIntel);

		// Decide whether to explore
		if (DecideToExplore(player, unit, orientation)) {
			var maybeAiData = ExplorerAI.MaybeMakeAiData(unit, player);
			if (maybeAiData != null)
				return new ExplorerAI(maybeAiData);
		}

		// Defend otherwise
		return new DefenderAI(DefenderAI.MakeAiDataForDefendInPlace(unit, player));
	}

	/// <summary>
	/// "Act: Implement the decision, which creates new data and feeds back into the observation phase."
	/// </summary>
	protected async Task<UnitAI.Result> Act(Player player, MapUnit unit, UnitAI plan) {
		return await plan.PlayTurn(player, unit);
	}

	internal class Orientation {
		public bool IsLastUnitInCamp { get; set; }
		public CombatAIData CombatIntel { get; set; }

		public bool CanEngage() => CombatIntel != null;
	}

	protected abstract bool DecideToEngage(Player player, MapUnit unit, Orientation orientation);

	protected abstract bool DecideToExplore(Player player, MapUnit unit, Orientation orientation);
}
