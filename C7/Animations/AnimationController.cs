using C7.UIElements;
using C7Engine;
using C7GameData;
using Godot;

/// This class mediates between the Engine and the game's animation
/// system. It receives animation messages from the Engine, checks
/// whether they are relevant for the player, and starts and updates
/// animations using the AnimationTracker and the AnimationManager.
[GlobalClass]
public partial class AnimationController : Node {
	public AnimationManager civ3AnimData;
	public AnimationTracker animTracker;

	[Export]
	Game game;

	public override void _Ready() {
		civ3AnimData = new AnimationManager();
		animTracker = new AnimationTracker(civ3AnimData);
	}

	private bool Animated(MapUnit unit, MapUnit.AnimatedAction action) {
		if (action is MapUnit.AnimatedAction.ATTACK1 or MapUnit.AnimatedAction.ATTACK2 or MapUnit.AnimatedAction.ATTACK3
			or MapUnit.AnimatedAction.DEFEND or MapUnit.AnimatedAction.DEATH) {
			return Civ3Preferences.Get(Civ3Preferences.AnimateBattles);
		}
		return Civ3Preferences.Get(MoveKind(unit, game.controller, show: false));
	}

	// Which of Civ3's kinds a unit's move is: ours by hand, ours on a
	// go-to or automation, a civ at peace with us, an enemy (barbarians too).
	public static string MoveKind(MapUnit unit, Player us, bool show) {
		if (unit.owner == us) {
			bool automatic = unit.isAutomated || (unit.path?.PathLength() ?? 0) > 0;
			return automatic ? (show ? Civ3Preferences.ShowOurAutomaticMoves : Civ3Preferences.AnimateOurAutomaticMoves)
				: (show ? Civ3Preferences.ShowOurManualMoves : Civ3Preferences.AnimateOurManualMoves);
		}
		bool enemy = unit.owner.isBarbarians || us == null || !us.IsAtPeaceWith(unit.owner);
		return enemy ? (show ? Civ3Preferences.ShowEnemyMoves : Civ3Preferences.AnimateEnemyMoves)
			: (show ? Civ3Preferences.ShowFriendMoves : Civ3Preferences.AnimateFriendMoves);
	}

	public void HandleEngineMessage(AnimationMessage msg) {
		GameData gameData = EngineStorage.gameData;

		switch (msg) {
			case MsgStartUnitAnimation mSUA:
				MapUnit unit = gameData.GetUnit(mSUA.unitID);
				// Civ3's preferences: battles, and our manual moves, our
				// automatic ones, other civs' at peace with us, enemies'.
				if (unit != null && !Animated(unit, mSUA.action)) {
					mSUA.markCompleted();
					break;
				}
				if (unit != null && mSUA.action is MapUnit.AnimatedAction.RUN or MapUnit.AnimatedAction.WALK
					&& game.controller.tileKnowledge.isActiveTile(unit.location) && Civ3Preferences.Get(MoveKind(unit, game.controller, show: true))) {
					game.ensureLocationIsInView(unit.location);
				}
				// A plane on a mission is seen from its base or its target.
				Tile flightTarget = unit != null && mSUA.flying ? FlightTarget(gameData, unit, mSUA) : null;
				if (flightTarget != null && game.controller.tileKnowledge.isActiveTile(flightTarget)) {
					game.ensureLocationIsInView(flightTarget);
					animTracker.startAnimation(unit, mSUA.action, mSUA.markCompleted, mSUA.ending, mSUA.flying, mSUA.flightFrom, mSUA.flightTo);
				} else if (unit != null && (game.controller.tileKnowledge.isActiveTile(unit.location) || game.controller.tileKnowledge.isActiveTile(unit.previousLocation))) {
					// TODO: This needs to be extended so that the player is shown when AIs found cities, when they move units
					// (optionally, depending on preferences) and generalized so that modders can specify whether custom
					// animations should be shown to the player.
					if (mSUA.action == MapUnit.AnimatedAction.ATTACK1)
						game.ensureLocationIsInView(unit.location);

					animTracker.startAnimation(unit, mSUA.action, mSUA.markCompleted, mSUA.ending, mSUA.flying, mSUA.flightFrom, mSUA.flightTo);
				} else {
					mSUA.markCompleted();
				}
				break;
			case MsgStartEffectAnimation mSEA:
				int X, Y;
				gameData.map.tileIndexToCoords(mSEA.tileIndex, out X, out Y);
				Tile tile = gameData.map.tileAt(X, Y);
				if (tile != Tile.NONE && game.controller.tileKnowledge.isActiveTile(tile)) {
					animTracker.startAnimation(tile, mSEA.effect, mSEA.markCompleted, mSEA.ending);
				} else {
					mSEA.markCompleted();
				}
				break;
		}
	}

	private static Tile FlightTarget(GameData gameData, MapUnit unit, MsgStartUnitAnimation msg) {
		(float x, float y) far = Mathf.Abs(msg.flightTo.x) + Mathf.Abs(msg.flightTo.y) >= Mathf.Abs(msg.flightFrom.x) + Mathf.Abs(msg.flightFrom.y) ? msg.flightTo : msg.flightFrom;
		return gameData.map.tileAt(unit.location.XCoordinate + (int)far.x, unit.location.YCoordinate + (int)far.y);
	}

	// Instead of Game calling animTracker.update periodically (this used to happen in _Process), this method gets called as necessary to bring
	// the animations up to date. Right now it's called from UnitLayer right before it draws the units on the map. This method also processes all
	// waiting messages b/c some of them might pertain to animations.
	public void updateAnimations() {
		while (EngineStorage.TryDequeueNextAnimationMessage(out AnimationMessage msg))
			HandleEngineMessage(msg);

		animTracker.update();
	}

	public void ToggleAnimationsEnabled() {
		new MsgToggleAnimationsEnabled().send();
		animTracker.endAllImmediately = !animTracker.endAllImmediately;
	}

	public void SetAnimationsEnabled(bool enabled) {
		new MsgSetAnimationsEnabled(enabled).send();
		animTracker.endAllImmediately = !enabled;
	}
}
