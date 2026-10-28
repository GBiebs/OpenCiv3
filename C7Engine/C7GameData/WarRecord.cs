using System.Linq;

namespace C7GameData {
	// How a war is going, for each side: what it has destroyed or taken from
	// the other (units killed or captured, cities taken), and lost to it, in
	// shields. Units count their shield cost (an army its members too), a city
	// its citizens at the rules' citizen value plus its buildings' cost. The
	// AI weighs it when it thinks about peace (PeaceAI). Barbarians keep no
	// record.
	public static class WarRecord {
		// A new war: both sides start counting.
		public static void OnWarStarted(Player a, Player b, int turn) {
			foreach ((Player us, Player them) in new[] { (a, b), (b, a) }) {
				if (PlayerRelationship.TryGetRelationship(us, them, out PlayerRelationship relationship)) {
					relationship.warStartTurn = turn;
					relationship.warGains = 0;
					relationship.warLosses = 0;
					relationship.lastPeaceOfferTurn = null;
				}
			}
		}

		public static void OnPeace(Player a, Player b) {
			foreach ((Player us, Player them) in new[] { (a, b), (b, a) }) {
				if (PlayerRelationship.TryGetRelationship(us, them, out PlayerRelationship relationship)) {
					relationship.warStartTurn = null;
					relationship.warGains = 0;
					relationship.warLosses = 0;
					relationship.lastPeaceOfferTurn = null;
				}
			}
		}

		// `winner` destroyed or took `shields`' worth of `loser`'s.
		public static void Record(Player winner, Player loser, int shields) {
			if (PlayerRelationship.TryGetRelationship(winner, loser, out PlayerRelationship ours) && ours.AtWar()) {
				ours.warGains += shields;
			}
			if (PlayerRelationship.TryGetRelationship(loser, winner, out PlayerRelationship theirs) && theirs.AtWar()) {
				theirs.warLosses += shields;
			}
		}

		public static int UnitValue(MapUnit unit) {
			int value = unit.unitType.shieldCost;
			if (unit.IsArmy()) {
				value += unit.Passengers().Sum(member => member.unitType.shieldCost);
			}
			return value;
		}

		public static int CityValue(GameData gameData, City city) {
			return city.residents.Count * gameData.rules.CitizenValueInShields
				+ city.constructed_buildings.Sum(cb => cb.building.shieldCost);
		}

		// `unit` was killed or captured by `by`.
		public static void OnUnitLost(MapUnit unit, Player by) {
			Record(by, unit.owner, UnitValue(unit));
		}

		// `winner` took `city` (before it changes hands).
		public static void OnCityTaken(GameData gameData, City city, Player winner) {
			Record(winner, city.owner, CityValue(gameData, city));
		}
	}
}
