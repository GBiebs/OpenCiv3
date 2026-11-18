using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace C7GameData {
	public class Government {
		public ID id;
		public string name;
		public string civilopediaEntry;

		// If non-null, the tech required to use this government.
		public ID prerequisiteTech;

		// If true, this is the starting government.
		public bool defaultType;

		// If true, this is the government used while switching governments.
		public bool transitionType;

		// Whether buildings cost upkeep under this government (the BIQ's
		// RequiresMaintenance). Civ3's Anarchy has "No maintenance costs or
		// unit upkeep needed" (https://civfanatics.com/civ3/civilopedia/governments/).
		// Null in saves from before it was imported, where the transition
		// government (Anarchy) is the one without.
		public bool? requiresMaintenance;

		public bool RequiresMaintenance() {
			return requiresMaintenance ?? !transitionType;
		}

		// The "despotism penalty" applies for this goverment; reduces all 
		// commerce, production and food output done by citizen laborers by -1 
		// when they produce more than 2.
		public bool hasTilePenalty {
			get => _hasTilePenalty;
			set {
				_hasTilePenalty = value;
				RebuildTileModifier();
			}
		}
		private bool _hasTilePenalty;

		// +1 commerce any tile with at least 1 commerce.
		public bool hasTradeBonus {
			get => _hasTradeBonus;
			set {
				_hasTradeBonus = value;
				RebuildTileModifier();
			}
		}
		private bool _hasTradeBonus;

		// Built from the flags above by their setters; don't add to it
		// directly, as setting a flag replaces it.
		[JsonIgnore]
		public Action<Tile.Yield> tileModifier;

		// Rebuilt from scratch, so setting a flag again (even to the same
		// value) never applies its modifier twice. The penalty goes first,
		// as it always has: it looks at the yield including bonuses.
		private void RebuildTileModifier() {
			Action<Tile.Yield> modifier = null;
			if (_hasTilePenalty) {
				modifier += TilePenalty;
			}
			if (_hasTradeBonus) {
				modifier += TradeBonus;
			}
			tileModifier = modifier;
		}

		// See https://codehappy.net/apolyton/threads/46801-1.htm and
		// https://forums.civfanatics.com/threads/everything-about-corruption-c3c-edition.76619/.
		public enum CorruptionType {
			Minimal,
			Nuisance,
			Problematic,
			Rampant,
			Catastrophic,
			Communal,
			Off
		};
		public CorruptionType corruptionType;

		public enum HurryProductionType {
			CannotHurry,
			ForcedLabor,
			PaidLabor,
		};
		public HurryProductionType hurryingType;

		// The highest value, in tenths, that any one of the tax, science and
		// luxury sliders can be set to under this government.
		public int rateCap = 10;

		// How much the people tire of war: 0 not at all, 1 low, 2 high.
		public int warWeariness;

		public int draftLimit;
		public int militaryPoliceLimit;
		public int workerRate;

		public bool allUnitsFree;

		// The BIQ's government flags (C3C editor help, Flags): Xenophobic,
		// "no culture is gained in cities where less than half of the
		// population of the city is from the same race as the controlling
		// civilization" (see City.GetCulturePerTurn); and Forced
		// Resettlement, "When switching to the selected government, each
		// town in the nation loses 1 population, each city loses 2
		// population, and each metropolis loses 3 population" (see
		// Player.AdoptGovernment). Fascism has both in conquests.biq.
		public bool xenophobic;
		public bool forcedResettlement;

		// Kept for scenarios; the game doesn't use them yet (its espionage
		// and culture follow players' studies): the BIQ GOVT's assimilation
		// chance (%), the experience level of its diplomats and spies (by
		// key), what it's immune to (the BIQ's value), and how it stands
		// against each government (by name): whether its cities can be
		// bribed, and the bribery and resistance modifiers.
		public int assimilationChance;
		public string diplomatLevel;
		public string spyLevel;
		public int immuneTo;
		public List<GovernmentRelation> relations = new();
		public int freeUnitsPerTown;
		public int freeUnitsPerCity;
		public int freeUnitsPerMetropolis;
		public int unitCost;

		// The ruler titles a leader is addressed by under this government,
		// male and female, in the BIQ's pairs (only the RulerTitlePairsUsed
		// of them). Empty in saves from before they were imported.
		public List<string> maleRulerTitles = new();
		public List<string> femaleRulerTitles = new();

		// The title for a leader of the gender, from the pair at the index
		// (which Civ3 picks at random among those used: FUN_00544d70). A
		// pair without a female title uses its male one, as in Civ3. Null if
		// there are no titles.
		public string RulerTitle(Gender gender, int pairIndex) {
			int pairs = maleRulerTitles?.Count ?? 0;
			if (pairs == 0) {
				return null;
			}
			int pair = ((pairIndex % pairs) + pairs) % pairs;
			if (gender == Gender.Female && femaleRulerTitles != null && pair < femaleRulerTitles.Count
					&& !string.IsNullOrWhiteSpace(femaleRulerTitles[pair])) {
				return femaleRulerTitles[pair];
			}
			return string.IsNullOrWhiteSpace(maleRulerTitles[pair]) ? null : maleRulerTitles[pair];
		}

		private static void TradeBonus(Tile.Yield yield) {
			if (yield.type == Tile.YieldType.Commerce && yield.baseYield > 0) {
				yield.bonus += 1;
			}
		}

		private static void TilePenalty(Tile.Yield yield) {
			yield.penalty += yield.yield > 2 ? 1 : 0;
		}
	}

	// See Government.relations.
	public class GovernmentRelation {
		public string government;
		public bool canBribe;
		public int briberyModifier;
		public int resistanceModifier;
	}
}
