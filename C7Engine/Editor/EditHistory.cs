using System;
using System.Collections.Generic;
using System.Linq;
using C7GameData;

namespace C7Engine.Editor;

// The map editor's undo and redo, unlimited as Civ3's editor's. An action
// (a brush stroke, one drag of the mouse, or a dialog's OK) is one step: it
// records each tile it touches before changing it, and on ending, how each
// one became.
public sealed class EditHistory {
	public sealed class Step {
		public string Name { get; init; }
		internal readonly Dictionary<Tile, TileState> before = [];
		internal readonly Dictionary<Tile, TileState> after = [];
		// The map's starting locations before and after, when the action
		// changed them: their order is the player slots', which the tiles'
		// own states can't put back.
		internal List<Tile> startsBefore, startsAfter;
		public IReadOnlyCollection<Tile> Tiles => before.Keys;

		// Whether the action turned land into water or water into land
		// somewhere, which is all the continents and lakes depend on.
		public bool ChangesWater => before.Any(b => after.TryGetValue(b.Key, out TileState a) && a.IsWater != b.Value.IsWater);
	}

	private readonly GameMap map;
	private readonly List<Step> undo = [];
	private readonly List<Step> redo = [];
	private Step open;

	public EditHistory(GameMap map) {
		this.map = map;
	}

	public bool CanUndo => undo.Count > 0 && open == null;
	public bool CanRedo => redo.Count > 0 && open == null;
	public string UndoName => undo.Count > 0 ? undo[^1].Name : null;
	public string RedoName => redo.Count > 0 ? redo[^1].Name : null;
	public int UndoCount => undo.Count;
	public bool InAction => open != null;

	// Whether anything changed since the scenario was opened or saved.
	public bool Modified => undo.Count != savedAt;
	private int savedAt;

	public void MarkSaved() {
		savedAt = undo.Count;
	}

	// Says the scenario has changes not yet saved, though there's nothing
	// to undo (it was opened with a new map).
	public void MarkUnsaved() {
		savedAt = -1;
	}

	public void Begin(string name) {
		if (open != null) {
			throw new InvalidOperationException($"\"{open.Name}\" hasn't ended");
		}
		open = new Step { Name = name, startsBefore = [.. map.startingLocations] };
	}

	// Called before a tile changes in the open action (more than once is
	// fine: the first time counts).
	public void Touch(Tile tile) {
		if (open == null) {
			throw new InvalidOperationException("No action is open");
		}
		if (!open.before.ContainsKey(tile)) {
			open.before[tile] = TileState.Of(tile, map);
		}
	}

	// Ends the action. One that changed nothing isn't kept. Returns it, or
	// null.
	public Step End() {
		Step step = open ?? throw new InvalidOperationException("No action is open");
		open = null;
		foreach ((Tile tile, TileState before) in step.before.ToList()) {
			TileState after = TileState.Of(tile, map);
			if (after.SameAs(before)) {
				step.before.Remove(tile);
			} else {
				step.after[tile] = after;
			}
		}
		if (step.startsBefore.SequenceEqual(map.startingLocations)) {
			step.startsBefore = null;
		} else {
			step.startsAfter = [.. map.startingLocations];
		}
		if (step.before.Count == 0) {
			return null;
		}
		// A new action after an undo: what was undone can't come back, as in
		// any editor. Nor can the saved state, if it was among it.
		if (savedAt > undo.Count) {
			savedAt = -1;
		}
		undo.Add(step);
		redo.Clear();
		return step;
	}

	// Takes the last action back. Returns it, or null if there's none.
	public Step Undo() {
		if (!CanUndo) {
			return null;
		}
		Step step = undo[^1];
		undo.RemoveAt(undo.Count - 1);
		foreach ((Tile tile, TileState state) in step.before) {
			state.Restore(tile, map);
		}
		PutStarts(step.startsBefore);
		redo.Add(step);
		return step;
	}

	public Step Redo() {
		if (!CanRedo) {
			return null;
		}
		Step step = redo[^1];
		redo.RemoveAt(redo.Count - 1);
		foreach ((Tile tile, TileState state) in step.after) {
			state.Restore(tile, map);
		}
		PutStarts(step.startsAfter);
		undo.Add(step);
		return step;
	}

	private void PutStarts(List<Tile> starts) {
		if (starts != null) {
			map.startingLocations.Clear();
			map.startingLocations.AddRange(starts);
		}
	}
}
