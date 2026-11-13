using System;
using System.Collections.Generic;

namespace C7Engine {

	// A flow on screen that has the screen to itself until it's done: the
	// end of the game from its announcement to the question of whether to
	// play on; the Science Advisor opened by "What's the big picture?"; a
	// city screen or the palace opened from a popup. While it lasts, the
	// popups waiting stay back (but its own), and the game's own prompts
	// (production, held news) wait too. A claim may let popups asked for
	// while it lasts come up over its screen, as the city screen's own
	// questions must, holding back only those already waiting and the
	// game's prompts. A claim that holds the game also
	// keeps the engine's news to the UI in its queue and the engine from
	// going on, so nothing new happens behind it (owner's report,
	// 2026-11-12: "Nothing should happen in the game screens until 'Maybe a
	// few more rounds' is selected").
	public sealed class ScreenClaim {
		public string Name { get; }

		// Whether the game waits for the flow too (see ScreenClaims.HoldsGame).
		public bool HoldsGame { get; }

		// Whether popups asked for while it lasts come up (see OwnerOf).
		public bool AdmitsNewPopups { get; }

		internal readonly Func<bool> lastsWhile;
		internal readonly Action afterwards;
		internal ScreenClaims owner;

		internal ScreenClaim(string name, bool holdsGame, bool admitsNewPopups, Func<bool> lastsWhile, Action afterwards) {
			Name = name;
			HoldsGame = holdsGame;
			AdmitsNewPopups = admitsNewPopups;
			this.lastsWhile = lastsWhile;
			this.afterwards = afterwards;
		}

		public bool Active => owner != null && owner.IsActive(this);

		// Ends the claim: its afterwards runs, then what was waiting may
		// come up. Releasing it again does nothing.
		public void Release() => owner?.Release(this);

		public override string ToString() => Name;
	}

	// The flows that have claimed the screen (see ScreenClaim). Claims nest:
	// the latest is in front, and only its popups come up until it's done.
	public sealed class ScreenClaims {
		private readonly List<ScreenClaim> active = [];

		// Raised after a claim ends (and its afterwards has run), so that
		// what waited may come up.
		public event Action Released;

		public bool Any => active.Count > 0;

		// Whether a claim holds the game: the engine's news to the UI waits
		// in its queue, and the engine doesn't go on, until it ends.
		public bool HoldsGame => active.Exists(c => c.HoldsGame);

		// The claim in front, or null.
		public ScreenClaim Top => active.Count == 0 ? null : active[^1];

		internal bool IsActive(ScreenClaim claim) => active.Contains(claim);

		// Claims the screen until the claim is released, or, with lastsWhile,
		// until that stops holding (see Poll). Afterwards runs as it ends,
		// before anything waiting comes up.
		public ScreenClaim Claim(string name, bool holdsGame = false, bool admitsNewPopups = false, Func<bool> lastsWhile = null,
				Action afterwards = null) {
			ScreenClaim claim = new(name, holdsGame, admitsNewPopups, lastsWhile, afterwards) { owner = this };
			active.Add(claim);
			return claim;
		}

		// Claims the screen while something the player is sent to is open:
		// until isOpen has held and stopped holding again. It may open a
		// moment later (as when the engine answers with it), so if it hasn't
		// opened within openWithin seconds of now (by the clock), the claim
		// ends without it.
		public ScreenClaim ClaimWhileOpen(string name, Func<bool> isOpen, Func<double> clock, bool admitsNewPopups = false,
				double openWithin = 3, Action afterwards = null) {
			bool seen = false;
			double since = clock();
			return Claim(name, admitsNewPopups: admitsNewPopups, lastsWhile: () => {
				if (isOpen()) {
					seen = true;
					return true;
				}
				return !seen && clock() - since < openWithin;
			}, afterwards: afterwards);
		}

		// The claim a popup asked for now belongs to: the one it was asked for
		// by, if any, or the front claim if that lets new popups come up.
		public ScreenClaim OwnerOf(ScreenClaim askedBy) => askedBy ?? (Top is { AdmitsNewPopups: true } top ? top : null);

		// Whether a popup belonging to the claim (null for none) may come up
		// now: any may with no claim; otherwise only the front claim's.
		public bool Admits(ScreenClaim claim) => active.Count == 0 || (claim != null && claim == Top);

		// Which of the popups waiting, in order, given the claim each belongs
		// to (null for none), comes up next: the first, with no claim; the
		// front claim's first otherwise. -1 if none may.
		public int NextToShow(IReadOnlyList<ScreenClaim> waiting) {
			for (int i = 0; i < waiting.Count; ++i) {
				if (Admits(waiting[i])) {
					return i;
				}
			}
			return -1;
		}

		// Ends the claims whose condition has stopped holding. Called each
		// frame.
		public void Poll() {
			foreach (ScreenClaim claim in active.ToArray()) {
				if (claim.lastsWhile != null && IsActive(claim) && !claim.lastsWhile()) {
					Release(claim);
				}
			}
		}

		internal void Release(ScreenClaim claim) {
			if (!active.Remove(claim)) {
				return;
			}
			claim.afterwards?.Invoke();
			Released?.Invoke();
		}

		// Forgets every claim without running their afterwards, as when the
		// game is left.
		public void Clear() {
			active.Clear();
		}
	}
}
