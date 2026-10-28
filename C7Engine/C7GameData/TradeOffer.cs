
using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;

namespace C7GameData {
	// A class representing one side of a diplomatic agreement between two civs.
	public class TradeOffer {
		// True if two players involved in the agreement were at war prior to
		// the agreement.
		public bool partOfPeaceTreaty = false;

		public int? gold = null;
		public List<Tech> techs = new();
		// Agreements both sign (they're on both sides' offers). See
		// Agreements.
		public List<Agreement> agreements = new();

		// Civ3's other goods: a world map (every tile the giver knows) or a
		// territory map (the giver's land), introductions to civs the giver
		// has met, resources (strategic or luxury, by key) and gold each turn
		// for the deal's duration, and cities (not the capital) and workers.
		public bool worldMap = false;
		public bool territoryMap = false;
		public List<ID> contacts = new();
		public List<string> resources = new();
		public int? goldPerTurn = null;
		public List<ID> cities = new();
		public List<ID> workers = new();

		// How much this offer, given by `giver`, is worth to `p` (who is either
		// the giver or the one receiving it), in gold. Per player because tech
		// costs vary with how many civs a player knows that have the tech, and
		// because a resource is worth more to a civ without it.
		public int GoldEquivalentFor(GameData gameData, Player p, Player giver) {
			int result = 0;
			if (gold.HasValue) {
				result += gold.Value;
			}
			foreach (Tech t in techs) {
				result += gameData.TechCostFor(t, p);
			}

			DealValueRules values = gameData.rules.dealValues;
			bool giving = p == giver;
			// Gold to come is worth nothing from a civ that has broken deals.
			if (goldPerTurn is int perTurn && (giving || !Reputation.Tarnished(gameData, giver))) {
				result += perTurn * gameData.rules.DefaultDealDuration;
			}
			if (!giving && values.mapTilesPerGold > 0) {
				if (worldMap) {
					result += giver.tileKnowledge.knownTiles.Count(t => !p.tileKnowledge.isTileKnown(t)) / values.mapTilesPerGold;
				}
				if (territoryMap) {
					result += giver.tileKnowledge.OwnedTiles().Count(t => !p.tileKnowledge.isTileKnown(t)) / values.mapTilesPerGold;
				}
			}
			if (!giving) {
				result += contacts.Count * values.contactGold;
			}
			foreach (string key in resources) {
				Resource resource = gameData.Resources.Find(r => r.Key == key);
				if (resource == null) {
					continue;
				}
				int worth = resource.Category == ResourceCategory.LUXURY ? values.luxuryResourceGold : values.strategicResourceGold;
				int had = Trading.ResourceCount(gameData, p, resource);
				if (giving) {
					// One to spare costs little.
					result += had > 1 ? worth * values.spareResourcePercent / 100 : worth;
				} else if (had == 0) {
					result += worth;
				}
			}
			foreach (ID cityId in cities) {
				City city = gameData.cities.Find(c => c.id == cityId);
				if (city != null) {
					int worth = WarRecord.CityValue(gameData, city) * gameData.rules.ShieldValueInGold;
					result += giving ? worth * values.cityGiveFactor : worth;
				}
			}
			foreach (ID unitId in workers) {
				MapUnit unit = gameData.mapUnits.Find(u => u.id == unitId);
				if (unit != null) {
					result += unit.unitType.shieldCost * gameData.rules.ShieldValueInGold;
				}
			}
			return result;
		}

		// Nothing given.
		public bool IsEmpty() {
			return !partOfPeaceTreaty && (gold ?? 0) == 0 && techs.Count == 0 && agreements.Count == 0
				&& !worldMap && !territoryMap && contacts.Count == 0 && resources.Count == 0 && (goldPerTurn ?? 0) == 0
				&& cities.Count == 0 && workers.Count == 0;
		}

		public void Clear() {
			partOfPeaceTreaty = false;
			gold = null;
			techs.Clear();
			agreements.Clear();
			worldMap = false;
			territoryMap = false;
			contacts.Clear();
			resources.Clear();
			goldPerTurn = null;
			cities.Clear();
			workers.Clear();
		}

		public string ToString() {
			List<string> pieces = new();
			if (partOfPeaceTreaty) {
				pieces.Add("peace treaty");
			}
			if (gold != null) {
				pieces.Add($"{gold.Value} gold");
			}
			foreach (Tech t in techs) {
				pieces.Add(t.Name);
			}
			foreach (Agreement a in agreements) {
				pieces.Add(a.against == null ? $"{a.type}" : $"{a.type} vs {a.against}");
			}
			if (worldMap) {
				pieces.Add("world map");
			}
			if (territoryMap) {
				pieces.Add("territory map");
			}
			pieces.AddRange(contacts.Select(c => $"contact with {c}"));
			pieces.AddRange(resources);
			if (goldPerTurn != null) {
				pieces.Add($"{goldPerTurn.Value} gold per turn");
			}
			pieces.AddRange(cities.Select(c => $"city {c}"));
			pieces.AddRange(workers.Select(w => $"worker {w}"));
			return string.Join(",", pieces);
		}
	}
}
