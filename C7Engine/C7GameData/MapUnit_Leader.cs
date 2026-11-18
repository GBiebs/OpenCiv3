using System.Collections.Generic;
using System.Linq;
using C7Engine;

namespace C7GameData;

// Military great leaders. When an elite land unit wins a battle against
// another civ, there's a chance a leader appears on its tile. A leader in one
// of its owner's cities can form an army, or finish the city's current
// improvement.
public partial class MapUnit {
	// The chance of an elite victory producing a leader: 1 in 16, or 1 in 12
	// with the Heroic Epic, half that for a victory on defense. Read from the
	// C3C 1.22 executable: "Ratio = 4 * (Small_Wonder_Count <= 0) + 12" /
	// "If the combat is defence, Ratio doubles" (Antal1987,
	// https://forums.civfanatics.com/threads/c3c-mechanics-birth-of-military-leader.526511, #1),
	// as players found: "The chances of getting a leader from a successful
	// defense are 1/2 the chances while attacking"
	// (https://civfanatics.com/civ3/strategy/military/creating-and-using-leaders/).
	// The same routine skips the roll for a unit that "is naval or air", so
	// only land units make leaders here.
	internal const double LeaderChance = 1.0 / 16.0;
	internal const double IncreasedLeaderChance = 1.0 / 12.0;

	public static double LeaderChanceFor(Player player, bool defending = false) {
		double chance = player.HasIncreasedLeaderChance() ? IncreasedLeaderChance : LeaderChance;
		return defending ? chance / 2 : chance;
	}

	public bool IsLeader() {
		return unitType.isLeader;
	}

	public bool IsElite() {
		return experienceLevel != null && EngineStorage.gameData.GetExperienceLevelAfter(experienceLevel) == null;
	}

	// Whether a victory by this unit over the opponent could produce a
	// leader, before the roll. Only elite land units fighting for themselves
	// (not as members of an army) qualify, victories over barbarians don't
	// count, each unit produces at most one leader, and a civ has at most one
	// military leader at a time (a scientific leader doesn't count).
	internal bool CouldProduceLeaderBeating(MapUnit opponent, bool wasElite) {
		if (!wasElite || hasProducedLeader || IsInArmy() || IsArmy() || !IsLandUnit())
			return false;
		if (owner == null || owner.IsIndependent || opponent?.owner == null || opponent.owner.IsIndependent)
			return false;
		if (LeaderPrototype() == null)
			return false;
		return !owner.units.Any(u => u.IsLeader() && !u.isScientificLeader);
	}

	private static UnitPrototype LeaderPrototype() {
		GameData gD = EngineStorage.gameData;
		string name = gD?.rules?.BattleCreatedUnit;
		if (string.IsNullOrEmpty(name))
			return null;
		return gD.unitPrototypes.Find(p => p.name == name);
	}

	// Rolls for a leader after this unit won a fight. wasElite is whether
	// the unit was elite going into the fight: a unit promoted to elite by
	// this victory doesn't also get the chance. defending is whether it won
	// on defense. Returns the new leader, or null.
	internal MapUnit RollForLeader(MapUnit opponent, bool wasElite, bool defending = false) {
		if (!CouldProduceLeaderBeating(opponent, wasElite))
			return null;
		if (GameData.rng.NextDouble() >= LeaderChanceFor(owner, defending))
			return null;
		return ProduceLeader();
	}

	// Records a new leader for the replay, by its name or, without one, its
	// unit type's (Civ3 FUN_0056d9e0 and FUN_005cdb10 record military and
	// scientific leaders alike).
	internal static void RecordGreatLeader(GameData gameData, MapUnit leader) {
		if (gameData?.gameHistory == null || leader == null) {
			return;
		}
		string name = string.IsNullOrEmpty(leader.name) ? leader.unitType?.name : leader.name;
		gameData.gameHistory.Record(gameData.turn, ReplayEventType.GreatLeader, leader.owner, leader.location, name);
	}

	// The name of the civilization's next leader from its list (Civ3's
	// editor: "A list of names used when the selected civilization produces
	// a Great Leader"; scientific leaders have their own), in order, again
	// from the first once all were used; null without a list. Counted from
	// the replay's leaders of that player named from the list.
	internal static string NextLeaderName(GameData gameData, Player player, List<string> names) {
		if (names == null || names.Count == 0) {
			return null;
		}
		string key = GameHistory.KeyOf(player);
		int used = gameData?.gameHistory?.Events.Count(e => e.Type == ReplayEventType.GreatLeader && e.Civ == key && names.Contains(e.Text)) ?? 0;
		return names[used % names.Count];
	}

	internal MapUnit ProduceLeader() {
		UnitPrototype proto = LeaderPrototype();
		if (proto == null || !Tile.IsTileValid(location))
			return null;
		hasProducedLeader = true;
		MapUnit leader = EngineStorage.gameData.SpawnUnit(owner, proto, location);
		leader.name = NextLeaderName(EngineStorage.gameData, owner, owner.civilization?.greatLeaderNames) ?? leader.name;
		RecordGreatLeader(EngineStorage.gameData, leader);
		log.Information("{Unit}'s victory has produced a great leader, {Leader}", this, leader);
		if (owner.IsToldNews) {
			new MsgShowMilitaryAdvisorPopup(owner, $"Our {name}'s great victory has inspired a new leader!\nA leader can form an army or hurry production in one of our cities.", happy: true).send();
		}
		return leader;
	}

	// A military leader in one of its owner's cities can form an army there
	// (a scientific one can't; see MapUnit_ScientificLeader.cs). Like armies
	// built by the Military Academy, it needs the cities to support one more
	// army: "you must have at least four cities for every army you create"
	// (Conquests Civilopedia, GCON_Armies), the four being the rules' "Cities
	// Needed to Support an Army" (see Player.CanSupportAnotherArmy).
	public bool CanFormArmy() {
		if (!IsLeader() || isScientificLeader || !movementPoints.canMove || !InOwnCity())
			return false;
		if (!owner.CanSupportAnotherArmy())
			return false;
		return ArmyPrototype() != null;
	}

	private static UnitPrototype ArmyPrototype() {
		GameData gD = EngineStorage.gameData;
		string name = gD?.rules?.BuildArmyUnit;
		if (string.IsNullOrEmpty(name))
			return null;
		return gD.unitPrototypes.Find(p => p.name == name);
	}

	private bool InOwnCity() {
		return Tile.IsTileValid(location) && location.HasCity() && location.cityAtTile.owner == owner;
	}

	// The leader becomes an empty army in its city. Returns the army, or
	// null if the leader can't form one.
	public MapUnit FormArmy() {
		if (!CanFormArmy())
			return null;
		GameData gD = EngineStorage.gameData;
		Tile tile = location;
		Player player = owner;
		gD.RemoveUnit(this);
		MapUnit army = gD.SpawnUnit(player, ArmyPrototype(), tile);
		log.Information("{Leader} formed {Army}", this, army);
		return army;
	}

	// A leader in one of its owner's cities can finish the city's current
	// improvement or small wonder. In Conquests military leaders can't hurry
	// great wonders (that's for scientific leaders) or units (an assumption).
	// A scientific leader can finish anything, great wonders and units
	// included (owner's decision, 2026-10-12; see
	// MapUnit_ScientificLeader.cs).
	public bool CanHurryProduction() {
		return CanOfferHurryProduction() && HurryProductionBlocker() == null;
	}

	// Whether to show the hurry action at all: the leader is in one of its
	// owner's cities with moves left. The city's current item may still rule
	// it out, and HurryProductionBlocker says why.
	public bool CanOfferHurryProduction() {
		return IsLeader() && movementPoints.canMove && InOwnCity();
	}

	// Why this leader can't hurry its city's current item, or null if it can.
	public string HurryProductionBlocker() {
		if (!CanOfferHurryProduction())
			return "A leader must be in one of our cities, with moves left, to hurry production.";
		City city = location.cityAtTile;
		if (isScientificLeader) {
			return city.itemBeingProduced switch {
				null => $"{city.name} isn't building anything.",
				Inflow => $"A leader can't hurry {city.name}'s {city.itemBeingProduced.name}.",
				IProducible p when city.shieldsStored >= owner.ShieldCost(p) => $"{city.name} will already finish its {p.name} this turn.",
				_ => null,
			};
		}
		return city.itemBeingProduced switch {
			null => $"{city.name} isn't building anything.",
			UnitPrototype => $"A leader can't hurry units. Switch {city.name}'s production to an improvement or small wonder first.",
			Building b when b.IsGreatWonder() => $"A military leader can't hurry great wonders like the {b.name}. Switch {city.name}'s production to an improvement or small wonder first.",
			Building b when city.shieldsStored >= owner.ShieldCost(b) => $"{city.name} will already finish its {b.name} this turn.",
			Building => null,
			_ => $"A leader can't hurry {city.name}'s current production.",
		};
	}

	// Fills the city's production box, so that the item is finished at the
	// end of the turn, and uses up the leader.
	public bool HurryProductionAsLeader() {
		if (!CanHurryProduction())
			return false;
		City city = location.cityAtTile;
		city.FillProductionBox();
		log.Information("{Leader} hurried {Item} in {City}", this, city.itemBeingProduced, city);
		EngineStorage.gameData.RemoveUnit(this);
		return true;
	}
}
