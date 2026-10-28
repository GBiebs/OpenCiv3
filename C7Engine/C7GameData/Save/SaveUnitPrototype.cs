using System;
using System.Collections.Generic;
using System.Linq;

namespace C7GameData.Save {
	public class SaveUnitPrototype {
		public enum Flag {
			RotateBeforeAttack,
			CanCarryFootUnitsOnly,
			CanCarryAircraft,
			CanCarryTacticalMissiles,
			LethalLandBombardment,
			LethalSeaBombardment,
			Radar,
			Wheeled,
			CanCapture,
			ZoneOfControl,
			Draftable,
			// A great leader (Civ3's "leader" ability).
			Leader,
			// An army: a unit that other units load into to move and fight together.
			Army,
			// Can attack more than once a turn.
			Blitz,
			// Its victories over another civ start its own civ's golden age
			// (Civ3's unique units, PRTO "starts golden age").
			StartsGoldenAge,
			// A king: in a regicide game, losing it (or all of them) loses the
			// game (see Regicide).
			King,
			// Doesn't move by itself: the Princess (carried by its captor, see
			// Princess) and Civ3's aircraft and missiles.
			Immobile,
			// May sink ending its turn in sea, or in ocean (see Naval).
			SinksInSea,
			SinksInOcean,
			// Attacks straight from a ship (Marines; see Naval).
			Amphibious,
			// Shows no flag: attacks, and is attacked, without war (the
			// Privateer).
			HiddenNationality,
			// Unseen by enemies but those that can see submarines.
			Submarine,
			CanSeeSubmarines,
			// Picks its target in a stack (from its stealth targets).
			StealthAttack,
			// Spent by its attack (the Cruise Missile).
			CruiseMissile,
			// Hard to intercept (Stealth Fighter and Bomber).
			Stealth,
		}

		public string name { get; set; }
		public string civilopediaEntry { get; set; }
		public Art art { get; set; }
		public int shieldCost { get; set; }
		public int populationCost { get; set; }
		public ID requiredTech { get; set; }
		public int attack { get; set; }
		public int defense { get; set; }
		public int bombard { get; set; }
		public int bombardRange { get; set; }
		public int rateOfFire { get; set; }
		public int movement { get; set; }
		public int capacity { get; set; }
		public int hpBonus { get; set; }
		public int operationalRange { get; set; }
		public int airDefense { get; set; }

		public HashSet<string> producibleBy = [];

		public List<string> upgradesTo;
		// The unit type that a unit this one defeats may become (Civ3's
		// "enslave results in"), or null if this unit doesn't enslave.
		public string enslaveResult;
		public bool unproducible;

		// Assorted boolean flags for the unit prototype. They're stored in
		// this set rather than as booleans to avoid bloating the json file.
		public HashSet<Flag> flags = [];

		public HashSet<string> categories = new HashSet<string>();

		public HashSet<UnitAction> actions = [];

		public HashSet<string> attributes = new HashSet<string>();

		public HashSet<string> requiredResources = [];

		public HashSet<ID> terraformActions = [];

		// The unit types a stealth attack may pick (Civ3's PRTO stealth
		// targets), if any.
		public List<string> stealthTargets;

		public SaveUnitPrototype() { }

		public SaveUnitPrototype(UnitPrototype proto) {
			(name, art, shieldCost, populationCost, unproducible,
			attack, defense, bombard, bombardRange, rateOfFire, movement, capacity, hpBonus) =
			(proto.name, proto.art, proto.shieldCost, proto.populationCost, proto.unproducible,
			 proto.attack, proto.defense, proto.bombard, proto.bombardRange, proto.rateOfFire, proto.movement,
			 proto.capacity, proto.hpBonus);

			civilopediaEntry = proto.civilopediaEntry;
			(operationalRange, airDefense) = (proto.operationalRange, proto.airDefense);

			if (proto.requiredTech != null)
				requiredTech = proto.requiredTech.id;

			if (proto.upgradesTo != null)
				upgradesTo = proto.upgradesTo?.Select(x => x.name).OrderBy(x => x).ToList() ?? [];

			enslaveResult = proto.enslaveResult?.name;

			categories = new HashSet<string>(proto.categories);
			actions = proto.actions;
			attributes = new HashSet<string>(proto.attributes);
			flags = new HashSet<Flag>(proto.flags);
			stealthTargets = proto.stealthTargets.Count > 0 ? proto.stealthTargets.OrderBy(x => x, StringComparer.Ordinal).ToList() : null;

			requiredResources = proto.requiredResources.Select(r => r.Key).ToHashSet();
			terraformActions = proto.terraformActions.Select(r => r.Id).ToHashSet();
			producibleBy = proto.producibleBy.Select(r => r.name).ToHashSet();
		}
	}
}
