using System;
using System.Collections.Generic;
using System.Diagnostics;
using C7GameData;
using Serilog;

public partial class AnimationTracker {
	private static readonly ILogger log = LogManager.ForContext<AnimationTracker>();
	private AnimationManager civ3AnimData;
	public bool endAllImmediately = true; // If true, update() ends all running animations regardless of time remaining.

	public AnimationTracker(AnimationManager civ3AnimData) {
		this.civ3AnimData = civ3AnimData;
	}

	public struct ActiveAnimation {
		public long startTimeMS, endTimeMS;
		public Action completionEvent;
		public AnimationEnding ending;
		public C7Animation anim;
		// The tile a unit's animation shows it on.
		public int tileX, tileY;
	}

	// Unit animations are keyed by unit ID and effect animations by tile ID.
	// They're kept apart so that drawing a tile can skip looking for an effect
	// when none are playing, which is most of the time.
	private Dictionary<ID, ActiveAnimation> activeAnims = new Dictionary<ID, ActiveAnimation>();
	private Dictionary<ID, ActiveAnimation> activeTileEffects = new Dictionary<ID, ActiveAnimation>();

	// Reused by update() for the animations it has to finish.
	private readonly List<ID> finishedIds = new List<ID>();

	private static readonly long stopwatchTicksPerMS = Math.Max(1, Stopwatch.Frequency / 1000);

	// The time in milliseconds from a monotonic clock, which unlike the wall
	// clock can't jump (for example when daylight saving time ends). Only
	// differences between these times are meaningful.
	public long getCurrentTimeMS() {
		return Stopwatch.GetTimestamp() / stopwatchTicksPerMS;
	}

	// How much longer than its duration an animation the engine waits for may
	// stay pending before the watchdog completes it anyway.
	private const long watchdogGraceMS = 10000;

	// A completion event the engine waits for, with the time by which it
	// should have been triggered. The engine stops processing anything else
	// until every animation it started is completed, so one that's never
	// completed (say because its unit's art is broken, or it was dropped by
	// mistake) would freeze the game.
	private class WatchedCompletion {
		public bool done;
		public long deadlineMS;
		public string description;
	}

	private readonly List<(WatchedCompletion, Action)> watchedCompletions = new();

	// Wraps the completion event so it's only triggered once and the watchdog
	// can tell whether it has been.
	private Action watch(Action completionEvent, long deadlineMS, string description) {
		if (completionEvent == null)
			return null;
		var watched = new WatchedCompletion { deadlineMS = deadlineMS, description = description };
		Action once = () => {
			if (watched.done)
				return;
			watched.done = true;
			completionEvent();
		};
		watchedCompletions.Add((watched, once));
		return once;
	}

	// Triggers the completion events that should have been triggered long ago.
	private void checkWatchdog(long currentTimeMS) {
		if (watchedCompletions.Count == 0)
			return;
		for (int i = watchedCompletions.Count - 1; i >= 0; i--) {
			(WatchedCompletion watched, Action complete) = watchedCompletions[i];
			if (watched.done) {
				watchedCompletions.RemoveAt(i);
			} else if (watched.deadlineMS <= currentTimeMS) {
				log.Warning("Animation {Animation} was still pending {GraceMS} ms after it should have ended; completing it", watched.description, watchdogGraceMS);
				watchedCompletions.RemoveAt(i);
				complete();
			}
		}
	}

	// Starts the animation, or if it can't be started, triggers its completion
	// event right away so the engine doesn't wait for it forever. Returns
	// whether it was started.
	private bool startAnimation(Dictionary<ID, ActiveAnimation> anims, long currentTimeMS, ID id, Func<C7Animation> getAnim, Action completionEvent, AnimationEnding ending, string description,
			long maxDurationMS = long.MaxValue) {
		C7Animation anim;
		long animDurationMS;
		try {
			anim = getAnim();
			animDurationMS = Math.Min((long)(anim.getDuration()), maxDurationMS);
		} catch (Exception e) {
			log.Error(e, "Couldn't start animation {Animation}", description);
			completeSafely(completionEvent, description);
			return false;
		}

		ActiveAnimation aa;
		if (anims.TryGetValue(id, out aa)) {
			// If there's already an animation playing for this unit, end it first before replacing it
			// TODO: Consider instead queueing up the new animation until after the first one is completed
			if (aa.completionEvent != null)
				completeSafely(aa.completionEvent, description);
		}
		aa = new ActiveAnimation {
			startTimeMS = currentTimeMS,
			endTimeMS = currentTimeMS + animDurationMS,
			completionEvent = watch(completionEvent, currentTimeMS + Math.Max(0, animDurationMS) + watchdogGraceMS, description),
			ending = ending,
			anim = anim
		};

		anims[id] = aa;

		try {
			anim.playSound();
		} catch (Exception e) {
			log.Warning(e, "Couldn't play the sound of animation {Animation}", description);
		}
		return true;
	}

	private static void completeSafely(Action completionEvent, string description) {
		try {
			completionEvent?.Invoke();
		} catch (Exception e) {
			log.Error(e, "Completing animation {Animation} failed", description);
		}
	}

	// The longest a step from one tile to the next takes: a Warrior's run
	// animation (WarriorRun.flc, 10 frames a direction at 66 ms). A step
	// plays the unit's whole run animation, and ships' are much longer
	// (FrigateRun.flc 14 at 125 ms, 1.75 s; CarrierRun.flc 15 at 142 ms,
	// 2.1 s), which made them crawl, though Civ3 moves every unit at one
	// pace: its unit INIs give ships and soldiers alike "Normal Speed=225".
	// A longer run animation is played faster to fit. Adapted from
	// GBiebs/OpenCiv3.
	public const long MaxStepDurationMS = 660;

	public void startAnimation(MapUnit unit, MapUnit.AnimatedAction action, Action completionEvent, AnimationEnding ending) {
		string description = $"{action} of {unit}";
		if (!startAnimation(activeAnims, getCurrentTimeMS(), unit.id, () => civ3AnimData.forUnit(unit, action), completionEvent, ending, description,
				action == MapUnit.AnimatedAction.RUN ? MaxStepDurationMS : long.MaxValue))
			return;
		ActiveAnimation aa = activeAnims[unit.id];
		aa.tileX = unit.location.XCoordinate;
		aa.tileY = unit.location.YCoordinate;
		activeAnims[unit.id] = aa;
	}

	public void startAnimation(Tile tile, AnimatedEffect effect, Action completionEvent, AnimationEnding ending) {
		if (!startAnimation(activeTileEffects, getCurrentTimeMS(), tile.Id, () => civ3AnimData.forEffect(effect), completionEvent, ending, $"{effect} at {tile}"))
			return;
		ActiveAnimation aa = activeTileEffects[tile.Id];
		aa.tileX = tile.XCoordinate;
		aa.tileY = tile.YCoordinate;
		activeTileEffects[tile.Id] = aa;
	}

	// Adds the coordinates of the tiles where unit animations and effects are playing, so the units can be drawn there even without
	// looking at every tile.
	public void CollectAnimatedTiles(List<(int x, int y)> tiles) {
		foreach (ActiveAnimation aa in activeAnims.Values)
			tiles.Add((aa.tileX, aa.tileY));
		foreach (ActiveAnimation aa in activeTileEffects.Values)
			tiles.Add((aa.tileX, aa.tileY));
	}

	public void endAnimation(MapUnit unit) {
		ActiveAnimation aa;
		if (activeAnims.TryGetValue(unit.id, out aa)) {
			if (aa.completionEvent != null)
				aa.completionEvent();
			activeAnims.Remove(unit.id);
		}
	}

	public bool hasCurrentAction(MapUnit unit) {
		return activeAnims.ContainsKey(unit.id);
	}

	// Whether the unit is playing an animation the player would want to see,
	// like a move or an attack, rather than nothing, a repeating animation
	// like a worker at work, or an animation that has ended.
	public bool hasAnimationDeservingAttention(MapUnit unit) {
		return activeAnims.Count > 0 && activeAnims.ContainsKey(unit.id) && getUnitAppearance(unit).DeservesPlayerAttention();
	}

	// Reused by forgetRemovedUnits for the animations to drop.
	private readonly List<ID> removedIds = new List<ID>();

	// Drops the animations of units that are no longer in the game, like
	// units that died (their death animation pauses on its last frame) or,
	// on a LAN client, units that aren't in the host's latest snapshot, and
	// of units the snapshot has on another tile than their animation, like
	// a step the client guessed wrong. Animations that repeat or pause are otherwise kept until they're
	// replaced, which may be never. Any completion event that hasn't been
	// triggered yet is triggered, so nothing waits for the animation forever.
	public void forgetRemovedUnits(GameData gameData) {
		// Also forget the work loops of units that may be gone; they're worked out again as needed.
		workLoops.Clear();
		if (activeAnims.Count == 0) {
			return;
		}

		removedIds.Clear();
		foreach ((ID id, ActiveAnimation aa) in activeAnims) {
			MapUnit unit = gameData.GetUnit(id);
			if (unit == null || unit.location.XCoordinate != aa.tileX || unit.location.YCoordinate != aa.tileY) {
				removedIds.Add(id);
			}
		}

		foreach (ID id in removedIds) {
			if (activeAnims.Remove(id, out ActiveAnimation aa)) {
				aa.completionEvent?.Invoke();
			}
		}
		removedIds.Clear();
	}

	// Whether any effect animations (like a hit or a miss) are playing on tiles.
	public bool hasTileEffects => activeTileEffects.Count > 0;

	// Whether an effect, or a unit animation that moves on, like a step or an attack, is playing, rather than one that loops or has stopped
	// on its last frame.
	public bool hasPlayingAnimations() {
		if (activeTileEffects.Count > 0) {
			return true;
		}
		if (activeAnims.Count == 0) {
			return false;
		}
		long now = getCurrentTimeMS();
		foreach (ActiveAnimation aa in activeAnims.Values) {
			if (aa.ending != AnimationEnding.Repeat && aa.endTimeMS > now) {
				return true;
			}
		}
		return false;
	}

	private (MapUnit.AnimatedAction, float, AnimationEnding) getActionAndProgress(in ActiveAnimation aa) {
		var durationMS = (double)(aa.endTimeMS - aa.startTimeMS);
		if (durationMS <= 0.0)
			durationMS = 1.0;

		var progress = (double)(getCurrentTimeMS() - aa.startTimeMS) / durationMS;
		if (aa.ending == AnimationEnding.Repeat)
			progress = progress - Math.Floor(progress);
		else if (progress > 1.0)
			progress = 1.0;

		return (aa.anim.action, (float)progress, aa.ending);
	}

	public (MapUnit.AnimatedAction, float, AnimationEnding) getCurrentActionAndProgress(ID id) {
		if (!activeAnims.TryGetValue(id, out ActiveAnimation aa) && !activeTileEffects.TryGetValue(id, out aa))
			throw new KeyNotFoundException($"No animation is playing for {id}");
		return getActionAndProgress(aa);
	}

	public (MapUnit.AnimatedAction, float, AnimationEnding) getCurrentActionAndProgress(MapUnit unit) {
		return getActionAndProgress(activeAnims[unit.id]);
	}

	public (MapUnit.AnimatedAction, float, AnimationEnding) getCurrentActionAndProgress(Tile tile) {
		return getActionAndProgress(activeTileEffects[tile.Id]);
	}

	public void update() {
		long currentTimeMS = (! endAllImmediately) ? getCurrentTimeMS() : long.MaxValue;
		update(activeAnims, currentTimeMS);
		update(activeTileEffects, currentTimeMS);
		checkWatchdog(getCurrentTimeMS());
	}

	// Triggers the completion events of the animations that have ended, once
	// each, and removes the ones that stop when they end.
	private void update(Dictionary<ID, ActiveAnimation> anims, long currentTimeMS) {
		if (anims.Count == 0)
			return;

		finishedIds.Clear();
		foreach (var (id, aa) in anims) {
			if (aa.endTimeMS <= currentTimeMS && (aa.completionEvent != null || aa.ending == AnimationEnding.Stop))
				finishedIds.Add(id);
		}

		foreach (ID id in finishedIds) {
			if (!anims.TryGetValue(id, out ActiveAnimation aa))
				continue;
			Action completionEvent = aa.completionEvent;

			// Update the stored animation before triggering the event, in case
			// the event starts a new animation for the same unit or tile.
			if (aa.ending == AnimationEnding.Stop) {
				anims.Remove(id);
			} else {
				aa.completionEvent = null; // So event is only triggered once
				anims[id] = aa;
			}

			completeSafely(completionEvent, id.ToString());
		}
		finishedIds.Clear();
	}

	// The durations of the work animations by art and action, so a worker's
	// loop doesn't need its flic header looked up every frame.
	private readonly Dictionary<(string, MapUnit.AnimatedAction), long> workDurationsMS = new();

	// The duration of each worker's work animation and what it was worked
	// out from, so that a worker drawn every frame doesn't need its art and
	// duration looked up by name every frame. Not kept for armies, whose art
	// changes with their members (see AnimationManager.ArtMayChange).
	private readonly struct WorkLoop {
		public readonly Terraform job;
		public readonly UnitPrototype unitType;
		public readonly Player owner;
		public readonly string era;
		public readonly Civilization nationality;
		public readonly long durationMS;

		public WorkLoop(MapUnit unit, long durationMS) {
			job = unit.WorkerJob;
			unitType = unit.unitType;
			owner = unit.owner;
			era = unit.owner?.eraCivilopediaName;
			nationality = unit.nationality;
			this.durationMS = durationMS;
		}

		// Whether the unit's art and job are still what the duration was worked out for: the art is chosen by the unit's type, its
		// owner's era and whether it's a captive.
		public bool Matches(MapUnit unit) {
			return ReferenceEquals(job, unit.WorkerJob) && ReferenceEquals(unitType, unit.unitType) && ReferenceEquals(owner, unit.owner)
				&& ReferenceEquals(era, unit.owner?.eraCivilopediaName) && ReferenceEquals(nationality, unit.nationality);
		}
	}
	private readonly Dictionary<MapUnit, WorkLoop> workLoops = new(ReferenceEqualityComparer.Instance);
	private const int MaxWorkLoops = 4096;

	// The animation of a worker at work, which is worked out from its job
	// rather than from the animation started when it was given the job. That
	// one is lost when a game is loaded, isn't sent to LAN clients and isn't
	// started at all while animations are off or the worker is out of sight.
	private bool tryGetWorkAppearance(MapUnit unit, out MapUnit.Appearance appearance) {
		appearance = default;
		if (unit.WorkerJob?.Animation is not MapUnit.AnimatedAction action) {
			return false;
		}

		long durationMS;
		bool cacheable = !AnimationManager.ArtMayChange(unit);
		if (cacheable && workLoops.TryGetValue(unit, out WorkLoop loop) && loop.Matches(unit)) {
			durationMS = loop.durationMS;
		} else {
			// Art that's missing or broken shows the worker in its usual pose,
			// which is remembered as a duration of zero so it's only tried once.
			string artName;
			try {
				artName = AnimationManager.ArtNameFor(unit);
			} catch (Exception e) {
				log.Warning(e, "Couldn't find the art of {Unit}", unit);
				return false;
			}
			if (!workDurationsMS.TryGetValue((artName, action), out durationMS)) {
				try {
					durationMS = Math.Max(1, (long)civ3AnimData.forUnit(unit, action).getDuration());
				} catch (Exception e) {
					log.Warning(e, "Couldn't load the {Action} animation of {Art}", action, artName);
					durationMS = 0;
				}
				workDurationsMS[(artName, action)] = durationMS;
			}
			if (cacheable) {
				if (workLoops.Count >= MaxWorkLoops) {
					workLoops.Clear();
				}
				workLoops[unit] = new WorkLoop(unit, durationMS);
			}
		}
		if (durationMS == 0) {
			return false;
		}

		// Each worker starts its loop at its own point, so a crew working
		// together doesn't swing in lockstep.
		long phaseMS = (uint)unit.id.GetHashCode() % durationMS;
		appearance = new MapUnit.Appearance {
			action = action,
			direction = unit.facingDirection,
			progress = (float)((getCurrentTimeMS() + phaseMS) % durationMS) / durationMS,
			ending = AnimationEnding.Repeat,
		};
		return true;
	}

	public MapUnit.Appearance getUnitAppearance(MapUnit unit) {
		// Repeating animations are the poses of units at rest, which follow
		// what the unit is doing now, so a worker's job takes their place.
		bool animating = activeAnims.TryGetValue(unit.id, out ActiveAnimation aa);
		if ((!animating || aa.ending == AnimationEnding.Repeat) && tryGetWorkAppearance(unit, out MapUnit.Appearance working)) {
			return working;
		}

		if (animating) {
			var (action, progress, ending) = getActionAndProgress(aa);

			float offsetX = 0, offsetY = 0;
			if (action == MapUnit.AnimatedAction.RUN) {
				(int dX, int dY) = unit.facingDirection.ToCoordDiff();
				offsetX = -1 * dX * (1f - progress);
				offsetY = -1 * dY * (1f - progress);
			}

			return new MapUnit.Appearance {
				action = action,
				direction = unit.facingDirection,
				progress = progress,
				offsetX = offsetX,
				offsetY = offsetY,
				ending = ending,
			};
		} else {
			return new MapUnit.Appearance {
				action = unit.isFortified ? MapUnit.AnimatedAction.FORTIFY : MapUnit.AnimatedAction.DEFAULT,
				direction = unit.facingDirection,
				progress = 1f,
				offsetX = 0f,
				offsetY = 0f,
			};
		}
	}

	public C7Animation getTileEffect(Tile tile) {
		if (activeTileEffects.Count == 0)
			return null;
		return activeTileEffects.TryGetValue(tile.Id, out ActiveAnimation aa) ? aa.anim : null;
	}
}
