using System.Collections.Generic;
using Serilog;

namespace C7GameData {
	using System;
	using System.Linq;
	using System.Threading;
	using C7Engine;
	using Save;

	public enum UnitAction {
		BuildCity,
		Bombard,
		Hold,
		Wait,
		Fortify,
		Disband,
		Goto,
		Explore,
		Automate,
		Load,
		Unload,
		Upgrade,
		Pillage,
		Sentry,
		// Not a BIQ ability; available to any unit that can sentry.
		SentryEnemyOnly,
		// A great leader forms an army in a city.
		BuildArmy,
		// A great leader finishes a city's improvement.
		HurryBuilding,
		// A scientific leader starts an Age of Science.
		ScienceAge,
		// Air missions (the BIQ's PRTO air missions; see MapUnit_Air.cs).
		Bombing,
		Recon,
		Intercept,
		Rebase,
		PrecisionBombing,
		// Paratroopers jump from a city onto a tile within range.
		Airdrop,
		// A worker founds a colony on a resource outside every civ's borders
		// (see Tile_Holdings).
		BuildColony,
		// A land unit flies from one of its owner's airports or airfields to
		// another (see MapUnit_Airlift.cs).
		Airlift,
		// A unit adds its population cost to the city it stands in (see
		// MapUnit_JoinCity.cs).
		JoinCity,
		// A captive is sacrificed in one of its owner's cities for culture
		// (see MapUnit_Sacrifice.cs).
		Sacrifice,
		// Civ3's automated bombarding (see MapUnit_AutoBombard.cs): not BIQ
		// abilities, but orders for units that bombard or fly bombing runs.
		AutoBombard,
		AutoBombing,
		AutoPrecisionBombing,
	}

	public struct ItemContext(UnitPrototype proto, Player player) {
		public UnitPrototype proto = proto;
		public Player player = player;
	}

	// A container for all the art for this unit
	public struct Art {
		public MainArt mainArt;
		public ThumbNailArt thumbnailArt;
		public PediaArt pediaArt;
	}

	// the main art contains the Animation art folder path for the unit
	public struct MainArt {
		public string defaultName;
		public Dictionary<string, string> variations;
	}
	// the thumbnail art contains the index to the small unit icons used in city screen production, etc
	public struct ThumbNailArt {
		public int defaultIndex;
		public Dictionary<string, int> variations;
	}
	// the icons being used by the civilopedia and also other places like the Science Advisor
	public struct PediaArt {
		public string small;
		public string large;
	}

	/**
	 * The prototype for a unit, which defines the characteristics of a unit.
	 * For example, a Spearman might have 1 attack, 2 defense, and 1 movement.
	 **/
	public class UnitPrototype : IProducible {
		public string name { get; set; }
		// The unit's key in the Civilopedia, like PRTO_Warrior, or null if
		// the rules don't say.
		public string civilopediaEntry { get; set; }
		public Art art { get; set; }
		public int shieldCost { get; set; }
		public int populationCost { get; set; }
		public Tech requiredTech { get; set; }
		public int attack { get; set; }
		public int defense { get; set; }
		public int bombard { get; set; }
		public int bombardRange { get; set; }
		public int rateOfFire { get; set; }
		public int movement { get; set; }
		public int capacity { get; set; }
		public int hpBonus { get; set; }
		// How far, in tiles, an aircraft flies on its missions from its base
		// (the BIQ's PRTO operational range); see MapUnit_Air.cs.
		public int operationalRange { get; set; }
		// The anti-aircraft strength with which the unit shoots at aircraft
		// bombing its tile (the BIQ's PRTO air defense); see MapUnit_Air.cs.
		public int airDefense { get; set; }

		// How much faster than a plain worker this unit does terrain jobs
		// (the BIQ's worker strength: 1 for Workers, 2 for Engineers).
		public float workerStrength { get; set; } = 1;
		// producibleBy and upgradesTo are what the cached upgrade relations
		// (UpgradeGraph) are worked out from. They are public and mutable, so
		// any access from outside this class, which might be followed by a
		// change to the set or list, bumps UpgradeDataVersion. That lets the
		// cache trust its last validation, in O(1), until then. Code in this
		// class reads the backing fields, which doesn't bump the version.
		// Whether the civ may build this unit, read without bumping
		// UpgradeDataVersion as producibleBy would.
		public bool IsProducibleBy(Civilization civ) {
			return producibleBySet.Contains(civ);
		}

		// Whether the captor takes this unit, when it's caught undefended,
		// rather than destroying it. Civ3 captures an undefended zero-defence
		// unit, like a catapult or artillery, only if the captor could build
		// it: the unit must be available to the captor's civ and the captor
		// must know its required tech. Resources don't matter.
		// https://forums.civfanatics.com/threads/capturing-units-with-zero-defense.631539/
		// https://civfanatics.com/civ3/infocenter/
		// UNVERIFIED: whether an obsolete unit can still be captured; it can
		// here, since only tech and civ availability are named.
		public bool CanBeCapturedBy(Player captor) {
			return !unproducible && IsProducibleBy(captor.civilization) && captor.HasRequiredTechnology(this);
		}

		public HashSet<Civilization> producibleBy {
			get {
				OnUpgradeDataAccessed();
				return producibleBySet;
			}
			set {
				producibleBySet = value;
				OnUpgradeDataAccessed();
			}
		}
		public List<UnitPrototype> upgradesTo {
			get {
				OnUpgradeDataAccessed();
				return upgradesToList;
			}
			set {
				upgradesToList = value;
				OnUpgradeDataAccessed();
			}
		}
		private HashSet<Civilization> producibleBySet = [];
		private List<UnitPrototype> upgradesToList = [];

		private static long upgradeDataVersion = 0;
		internal static long UpgradeDataVersion => Interlocked.Read(ref upgradeDataVersion);

		private static void OnUpgradeDataAccessed() {
			Interlocked.Increment(ref upgradeDataVersion);
		}
		public bool unproducible;
		// The name of the unit this one may make of a foe it defeats, or
		// null (see MapUnit.RollToEnslave).
		public string enslavesTo;
		// The units a stealth attack by this one may single out of a stack,
		// by name, in the BIQ's order (see MapUnit.StealthAttackTargets).
		public List<string> stealthTargets = [];
		public HashSet<SaveUnitPrototype.Flag> flags = [];

		// Civ3's movement flags (https://forums.civfanatics.com/threads/is-there-a-guide-on-unit-abilities.57822/):
		// "Wheeled: The unit may not enter any terrain with 'Impassable by
		// Wheeled Units' checked" (mountains, jungle, marsh and volcanoes in
		// conquests.biq), unless "following a road" (see CanMoveBetween);
		// "Foot Unit: ... can be transported by any transport", including
		// those that "can only transport units w/ Foot unit Flag"; "All
		// Terrain As Roads: whether improved w/ road or not, will lose only
		// as much movement as improved terrain"; "Immobile: Can't move".
		public bool isWheeled => flags.Contains(SaveUnitPrototype.Flag.Wheeled);
		public bool isFootSoldier => flags.Contains(SaveUnitPrototype.Flag.FootSoldier);
		public bool allTerrainAsRoads => flags.Contains(SaveUnitPrototype.Flag.AllTerrainAsRoads);

		// The terrains (by key) whose movement cost the unit ignores, as the
		// BIQ's "Ignore Movement Cost" says: the Chasqui Scout "ignores
		// movement costs for both hills and mountains" (PRTO_Chasquis_Scout),
		// paying 1 to enter them, as on flat land.
		public HashSet<string> ignoreMovementCost { get; set; } = [];

		public bool IgnoresMovementCostOf(Tile tile) {
			return ignoreMovementCost.Count > 0 && tile.overlayTerrainType != null && ignoreMovementCost.Contains(tile.overlayTerrainType.Key);
		}
		public bool isImmobile => flags.Contains(SaveUnitPrototype.Flag.Immobile);
		public bool canCarryFootUnitsOnly => flags.Contains(SaveUnitPrototype.Flag.CanCarryFootUnitsOnly);
		public bool isFlagUnit => flags.Contains(SaveUnitPrototype.Flag.FlagUnit);

		// Whether the unit's type lets it step from one tile onto a
		// neighbouring one. Wheeled units, like catapults and chariots,
		// "cannot cross mountains or jungle tiles without a road"
		// (https://sullla.com/Civ3/CQ/CQ_units.html): they can't enter
		// terrain impassable to wheeled units unless the move follows a
		// road, so both tiles need one. In Civ3 "if you are on an unroaded
		// tile, you can't go on a mountain tile even if it's roaded"
		// (stavrosfa, https://github.com/C7-Game/OpenCiv3/pull/1029). A city
		// counts as a road. UNVERIFIED: whether a river crossing without
		// bridges breaks the road here; it doesn't.
		//
		// Terrain impassable to everyone (only some mods have it) works the
		// same way for any land unit, and ships can't slip between two land
		// tiles that touch only at a corner. Antal1987's reading of the 1.22
		// class_Unit::Can_Pass_Tile
		// (https://forums.civfanatics.com/threads/c3c-new-movement-possibilities.529671/,
		// #1): for a land unit, impassable terrain (or terrain impassable to
		// wheeled units, for those) needs a road on both the origin and the
		// target tile; for a naval move between water tiles in one of the
		// corner directions, "If the direction is even, it checks neighbour
		// tiles (clockwise and counter clockwise) to be water", and the move
		// is refused only when both are land. A move into one of the unit's
		// own cities always passes; that one ends on land, so the corner
		// check, which is between water tiles, doesn't touch it.
		public bool CanMoveBetween(Tile from, Tile to) {
			if (IsSeaUnit() && from.IsWater() && to.IsWater() && GameMap.IsLandStrip(from, to)) {
				return false;
			}
			return CanEnterTerrainOf(to) && (!NeedsRoadToEnter(to) || IsRoaded(from));
		}

		// The part of CanMoveBetween that depends only on the tile entered.
		public bool CanEnterTerrainOf(Tile to) {
			return !NeedsRoadToEnter(to) || IsRoaded(to);
		}

		// Whether this land unit may only move onto the tile along a road
		// (see CanMoveBetween). A barbarian camp's tile stays open, so that
		// its garrison can move out and back in.
		private bool NeedsRoadToEnter(Tile to) {
			if (!IsLandUnit()) {
				return false;
			}
			TerrainType terrain = to.overlayTerrainType;
			return (terrain.impassable && !to.hasBarbarianCamp) || (isWheeled && terrain.impassableByWheeled);
		}

		private static bool IsRoaded(Tile tile) {
			return tile.HasRoad() || tile.HasCity();
		}
		public bool rotateBeforeAttack {
			get => flags.Contains(SaveUnitPrototype.Flag.RotateBeforeAttack);
			set {
				if (value) {
					flags.Add(SaveUnitPrototype.Flag.RotateBeforeAttack);
				} else {
					flags.Remove(SaveUnitPrototype.Flag.RotateBeforeAttack);
				}
			}
		}

		public bool isLandBombardmentLethal {
			get => flags.Contains(SaveUnitPrototype.Flag.LethalLandBombardment);
			set {
				if (value) {
					flags.Add(SaveUnitPrototype.Flag.LethalLandBombardment);
				} else {
					flags.Remove(SaveUnitPrototype.Flag.LethalLandBombardment);
				}
			}
		}
		public bool isSeaBombardmentLethal {
			get => flags.Contains(SaveUnitPrototype.Flag.LethalSeaBombardment);
			set {
				if (value) {
					flags.Add(SaveUnitPrototype.Flag.LethalSeaBombardment);
				} else {
					flags.Remove(SaveUnitPrototype.Flag.LethalSeaBombardment);
				}
			}
		}
		public bool hasBlitz {
			get => flags.Contains(SaveUnitPrototype.Flag.Blitz);
			set {
				if (value) {
					flags.Add(SaveUnitPrototype.Flag.Blitz);
				} else {
					flags.Remove(SaveUnitPrototype.Flag.Blitz);
				}
			}
		}
		public bool isAmphibious {
			get => flags.Contains(SaveUnitPrototype.Flag.Amphibious);
			set {
				if (value) {
					flags.Add(SaveUnitPrototype.Flag.Amphibious);
				} else {
					flags.Remove(SaveUnitPrototype.Flag.Amphibious);
				}
			}
		}
		public bool hasZoneOfControl {
			get => flags.Contains(SaveUnitPrototype.Flag.ZoneOfControl);
			set {
				if (value) {
					flags.Add(SaveUnitPrototype.Flag.ZoneOfControl);
				} else {
					flags.Remove(SaveUnitPrototype.Flag.ZoneOfControl);
				}
			}
		}
		public bool startsGoldenAge {
			get => flags.Contains(SaveUnitPrototype.Flag.StartsGoldenAge);
			set {
				if (value) {
					flags.Add(SaveUnitPrototype.Flag.StartsGoldenAge);
				} else {
					flags.Remove(SaveUnitPrototype.Flag.StartsGoldenAge);
				}
			}
		}
		public bool hasRadar {
			get => flags.Contains(SaveUnitPrototype.Flag.Radar);
			set {
				if (value) {
					flags.Add(SaveUnitPrototype.Flag.Radar);
				} else {
					flags.Remove(SaveUnitPrototype.Flag.Radar);
				}
			}
		}

		public bool isNuclearWeapon {
			get => flags.Contains(SaveUnitPrototype.Flag.NuclearWeapon);
			set {
				if (value) {
					flags.Add(SaveUnitPrototype.Flag.NuclearWeapon);
				} else {
					flags.Remove(SaveUnitPrototype.Flag.NuclearWeapon);
				}
			}
		}
		public bool isICBM {
			get => flags.Contains(SaveUnitPrototype.Flag.ICBM);
			set {
				if (value) {
					flags.Add(SaveUnitPrototype.Flag.ICBM);
				} else {
					flags.Remove(SaveUnitPrototype.Flag.ICBM);
				}
			}
		}
		public bool isTacticalMissile {
			get => flags.Contains(SaveUnitPrototype.Flag.TacticalMissile);
			set {
				if (value) {
					flags.Add(SaveUnitPrototype.Flag.TacticalMissile);
				} else {
					flags.Remove(SaveUnitPrototype.Flag.TacticalMissile);
				}
			}
		}
		public bool canCarryTacticalMissiles => flags.Contains(SaveUnitPrototype.Flag.CanCarryTacticalMissiles);
		public bool canCarryAircraft => flags.Contains(SaveUnitPrototype.Flag.CanCarryAircraft);
		public bool isStealth => flags.Contains(SaveUnitPrototype.Flag.Stealth);
		// See MapUnit.Bombard.
		public bool isCruiseMissile => flags.Contains(SaveUnitPrototype.Flag.CruiseMissile);
		// Whether the unit costs upkeep (the BIQ's "Requires Support"; see
		// Player.TotalUnitsAllowedUnitsAndSupportCostRaw).
		public bool requiresSupport {
			get => !flags.Contains(SaveUnitPrototype.Flag.NoSupportRequired);
			set {
				if (value) {
					flags.Remove(SaveUnitPrototype.Flag.NoSupportRequired);
				} else {
					flags.Add(SaveUnitPrototype.Flag.NoSupportRequired);
				}
			}
		}

		// "[Submarines] can operate invisibly to enemy units ... However,
		// other submarines and AEGIS Cruisers and Destroyers can see them
		// plainly" (Civilopedia, PRTO_Submarine).
		public bool isInvisible {
			get => flags.Contains(SaveUnitPrototype.Flag.Invisible);
			set {
				if (value) {
					flags.Add(SaveUnitPrototype.Flag.Invisible);
				} else {
					flags.Remove(SaveUnitPrototype.Flag.Invisible);
				}
			}
		}

		public bool detectsInvisible {
			get => flags.Contains(SaveUnitPrototype.Flag.DetectInvisible);
			set {
				if (value) {
					flags.Add(SaveUnitPrototype.Flag.DetectInvisible);
				} else {
					flags.Remove(SaveUnitPrototype.Flag.DetectInvisible);
				}
			}
		}

		// "Privateers are small frigates that carry no nationality
		// markings ... Thus, you can attack another civilization's shipping
		// without precipitating war" (Civilopedia, PRTO_Privateer).
		public bool hasHiddenNationality {
			get => flags.Contains(SaveUnitPrototype.Flag.HiddenNationality);
			set {
				if (value) {
					flags.Add(SaveUnitPrototype.Flag.HiddenNationality);
				} else {
					flags.Remove(SaveUnitPrototype.Flag.HiddenNationality);
				}
			}
		}

		// "Stealth attack allows a unit to specify which enemy unit -- in a
		// stack -- it will attack" (Civilopedia, GCON_Stealth_Attack).
		public bool hasStealthAttack {
			get => flags.Contains(SaveUnitPrototype.Flag.StealthAttack);
			set {
				if (value) {
					flags.Add(SaveUnitPrototype.Flag.StealthAttack);
				} else {
					flags.Remove(SaveUnitPrototype.Flag.StealthAttack);
				}
			}
		}

		// An army is a container that carries other units into battle as a
		// single stack, rather than fighting with strength of its own.
		public bool isArmy {
			get => flags.Contains(SaveUnitPrototype.Flag.Army);
			set {
				if (value) {
					flags.Add(SaveUnitPrototype.Flag.Army);
				} else {
					flags.Remove(SaveUnitPrototype.Flag.Army);
				}
			}
		}

		// Ships that can't brave the Sea, like the Galley, or the Ocean, like
		// the Caravel. Learning to build a ship without the flag makes that
		// water safe for all of a civ's ships. See Player.CanSailSafelyOn.
		public bool sinksInSea {
			get => flags.Contains(SaveUnitPrototype.Flag.SinksInSea);
			set {
				if (value) {
					flags.Add(SaveUnitPrototype.Flag.SinksInSea);
				} else {
					flags.Remove(SaveUnitPrototype.Flag.SinksInSea);
				}
			}
		}
		public bool sinksInOcean {
			get => flags.Contains(SaveUnitPrototype.Flag.SinksInOcean);
			set {
				if (value) {
					flags.Add(SaveUnitPrototype.Flag.SinksInOcean);
				} else {
					flags.Remove(SaveUnitPrototype.Flag.SinksInOcean);
				}
			}
		}

		// A military great leader, which can form an army or hurry a city's
		// production. See MapUnit_Leader.cs.
		public bool isLeader {
			get => flags.Contains(SaveUnitPrototype.Flag.Leader);
			set {
				if (value) {
					flags.Add(SaveUnitPrototype.Flag.Leader);
				} else {
					flags.Remove(SaveUnitPrototype.Flag.Leader);
				}
			}
		}

		// A King, whose death can eliminate its civ under Regicide and Mass
		// Regicide. See InstantElimination.
		public bool isKing {
			get => flags.Contains(SaveUnitPrototype.Flag.King);
			set {
				if (value) {
					flags.Add(SaveUnitPrototype.Flag.King);
				} else {
					flags.Remove(SaveUnitPrototype.Flag.King);
				}
			}
		}

		// A city may draft the unit (the BIQ's PRTO "Draft" flag). See
		// City.Draft.
		public bool isDraftable {
			get => flags.Contains(SaveUnitPrototype.Flag.Draftable);
			set {
				if (value) {
					flags.Add(SaveUnitPrototype.Flag.Draftable);
				} else {
					flags.Remove(SaveUnitPrototype.Flag.Draftable);
				}
			}
		}

		public HashSet<string> categories = new HashSet<string>();

		public HashSet<UnitAction> actions = [];

		public HashSet<string> attributes = new HashSet<string>();

		public HashSet<Resource> requiredResources { get; set; } = [];

		public HashSet<Terraform> terraformActions = [];

		// terraformActions.Count > 0 is not enough, as for example the Crusader unit can build a Fortress
		public bool isWorker => terraformActions.Count > 0 && actions.Contains(UnitAction.Automate);
		public bool isSettler => actions.Contains(UnitAction.BuildCity);


		public UnitPrototype() { }

		public UnitPrototype(SaveUnitPrototype proto, IEnumerable<Terraform> terraforms) {
			(name, art, shieldCost, populationCost)
				= (proto.name, proto.art, proto.shieldCost, proto.populationCost);

			(attack, defense, bombard, bombardRange, rateOfFire)
				= (proto.attack, proto.defense, proto.bombard, proto.bombardRange, proto.rateOfFire);

			(movement, capacity, hpBonus, unproducible) =
				(proto.movement, proto.capacity, proto.hpBonus, proto.unproducible);
			// Rulesets without the value leave every worker at plain speed.
			workerStrength = proto.workerStrength > 0 ? proto.workerStrength : 1;
			operationalRange = proto.operationalRange;
			airDefense = proto.airDefense;
			civilopediaEntry = proto.civilopediaEntry;
			enslavesTo = proto.enslavesTo;
			stealthTargets = proto.stealthTargets?.ToList() ?? [];

			categories = new HashSet<string>(proto.categories);
			actions = new HashSet<UnitAction>(proto.actions);
			attributes = new HashSet<string>(proto.attributes);
			flags = new HashSet<SaveUnitPrototype.Flag>(proto.flags);
			ignoreMovementCost = new HashSet<string>(proto.ignoreMovementCost ?? []);

			terraformActions = proto.terraformActions.Select(id => terraforms.First(t => t.Id == id)).ToHashSet();
		}

		// "Unit cost = base * PRTO.cost / 10, minimum 1": never less than a
		// shield, whatever the factors. Civ3 binary
		// (doc/perez-findings/Civ3_Findings.md, Round 2b, Costs).
		public int ShieldCost(HashSet<Civilization.Trait> civTraits, float costFactor) {
			return Math.Max(1, (int)(shieldCost * costFactor));
		}

		public bool IsLandUnit() {
			return GetCategoryFlags().isLand;
		}

		public bool IsSeaUnit() {
			return GetCategoryFlags().isSea;
		}

		public bool IsAirUnit() {
			return GetCategoryFlags().isAir;
		}

		// Whether the type has the Load action, i.e. can board transports.
		public bool HasLoadAction() {
			return GetActionFlags().canLoad;
		}

		// Whether the type has the Unload action.
		public bool HasUnloadAction() {
			return GetActionFlags().canUnload;
		}

		// The category and action checks above are made for every tile the
		// pathfinder looks at, so they are cached instead of hashing strings
		// each time. categories and actions are public sets that get filled
		// after construction (by importers, loaders and tests) or replaced
		// outright, so the cached flags remember which set, and how big it
		// was, they were computed from, and are recomputed when either
		// differs. (Sets are only ever added to, never edited in a way that
		// keeps their size.) Each snapshot is immutable and published with a
		// single reference write, so concurrent readers always see a
		// consistent one.
		private sealed class CategoryFlags {
			public readonly HashSet<string> source;
			public readonly int count;
			public readonly bool isLand, isSea, isAir;

			public CategoryFlags(HashSet<string> source) {
				this.source = source;
				count = source.Count;
				isLand = source.Contains("Land");
				isSea = source.Contains("Sea");
				isAir = source.Contains("Air");
			}
		}

		private sealed class ActionFlags {
			public readonly HashSet<UnitAction> source;
			public readonly int count;
			public readonly bool canLoad, canUnload;

			public ActionFlags(HashSet<UnitAction> source) {
				this.source = source;
				count = source.Count;
				canLoad = source.Contains(UnitAction.Load);
				canUnload = source.Contains(UnitAction.Unload);
			}
		}

		private CategoryFlags categoryFlags;
		private ActionFlags actionFlags;

		private CategoryFlags GetCategoryFlags() {
			HashSet<string> current = categories;
			CategoryFlags flags = categoryFlags;
			if (flags == null || !ReferenceEquals(flags.source, current) || flags.count != current.Count) {
				flags = new CategoryFlags(current);
				categoryFlags = flags;
			}
			return flags;
		}

		private ActionFlags GetActionFlags() {
			HashSet<UnitAction> current = actions;
			ActionFlags flags = actionFlags;
			if (flags == null || !ReferenceEquals(flags.source, current) || flags.count != current.Count) {
				flags = new ActionFlags(current);
				actionFlags = flags;
			}
			return flags;
		}

		// The art variation for captured workers (the first variation whose
		// name ends with "SLAVE"), or null. Cached per variations dictionary,
		// which is only replaced, never edited, once a prototype is set up.
		private sealed class SlaveArt {
			public readonly Dictionary<string, string> source;
			public readonly int count;
			public readonly string name;

			public SlaveArt(Dictionary<string, string> source) {
				this.source = source;
				count = source.Count;
				foreach (KeyValuePair<string, string> variation in source) {
					if (variation.Key.EndsWith("SLAVE", StringComparison.Ordinal)) {
						name = variation.Value;
						break;
					}
				}
			}
		}

		private SlaveArt slaveArt;

		public string GetSlaveArtName() {
			Dictionary<string, string> variations = art.mainArt.variations;
			if (variations == null) {
				return null;
			}
			SlaveArt cached = slaveArt;
			if (cached == null || !ReferenceEquals(cached.source, variations) || cached.count != variations.Count) {
				cached = new SlaveArt(variations);
				slaveArt = cached;
			}
			return cached.name;
		}

		// Whether units of this type can be loaded into an army. Only land
		// units that can attack qualify, so settlers, workers and bombard-only
		// units like catapults are left out, as are transports and other
		// armies.
		public bool CanJoinArmy() {
			return IsLandUnit() && attack > 0 && capacity == 0 && !isArmy;
		}

		public override string ToString() {
			return $"{name} ({attack}/{defense}/{movement})";
		}

		public double BaseStrength(CombatRole role) {
			switch (role) {
				case CombatRole.Attack: return attack;
				case CombatRole.Defense: return defense;
				case CombatRole.Bombard: return bombard;
				case CombatRole.BombardDefense: return defense;
				case CombatRole.DefensiveBombard: return bombard;
				case CombatRole.DefensiveBombardDefense: return defense;
				default: throw new ArgumentOutOfRangeException("Invalid CombatRole");
			}
		}

		// Without hitPoints the unit starts with the full hit points of the
		// game's default experience level (or Civ3's regular 3 if there's no
		// game), plus the prototype's bonus: "3 hit points is a regular, 4
		// hit points is a veteran" (https://civfanatics.com/civ3/faq/).
		public MapUnit GetInstance(ID id, UnitPrototype proto, Player owner, Civilization nationality = null, Tile location = null, TileDirection facingDirection = TileDirection.SOUTHWEST, int? hitPoints = null) {
			MapUnit instance = new MapUnit(id);
			instance.unitType = proto;
			instance.name = proto.name;
			instance.hitPointsRemaining = hitPoints ?? (EngineStorage.gameData?.defaultExperienceLevel?.baseHitPoints ?? 3) + proto.hpBonus;
			instance.owner = owner;
			if (nationality != null)
				instance.nationality = nationality;
			else
				instance.nationality = owner.civilization;
			instance.location = location;

			// Includes any bonus from the owner's wonders, like the Great
			// Lighthouse's for ships.
			instance.movementPoints.reset(instance.MaxMovementPoints());
			return instance;
		}

		/// Whether a given city can produce this unit, given available resources.
		public bool CanProduce(City city, HashSet<Resource> accessibleResources) {
			return this.IsAvailableTo(city)
				   && this.MeetsProductionRequirements(city, accessibleResources)
				   && !this.IsUnitObsolete(city, accessibleResources)
				   && !this.IsTooCostlyInPopulationFor(city);
		}

		// Whether the city is too small to pay this unit's population cost
		// and can't grow to: a city no bigger than the cost with no food
		// surplus, or at its size cap (no aqueduct or hospital to grow past
		// it). A size-2 city that can't grow isn't offered the Settler, so
		// neither the player nor the AI can pick it there (owner's decision,
		// 2026-10-21). Civ3 offered such a city the choice to abandon itself
		// for the Settler (Pentium and TimBentley,
		// https://forums.civfanatics.com/threads/raise-abandon.113940;
		// compendium 02, section 11); the owner's rule leaves it out of the
		// list instead. A city that can grow keeps it, and the unit waits
		// for the city to grow (see City.ComputeTurnProduction).
		private bool IsTooCostlyInPopulationFor(City city) {
			if (this.populationCost <= 0 || city.residents.Count > this.populationCost) {
				return false;
			}
			return !city.CanGrow();
		}

		/// Whether the city's Civ could build this unit, if it had the necessary resources.
		private bool IsAvailableTo(City city) {
			if (this.isSettler && city.capturedFrom == null && EngineStorage.gameData?.rules?.SettlersOnlyInConqueredCities == true) {
				return false;
			}
			// Rising Tide's sea sinks every ship (see RisingTide.SinksAt).
			if (this.IsSeaUnit() && EngineStorage.gameData?.rules?.RisingTide == true) {
				return false;
			}
			return (!this.unproducible || this.CanBeBuiltAsArmy(city)) && this.producibleBySet.Contains(city.owner.civilization);
		}

		/// Armies aren't normally producible, but a city with a building that allows
		/// building armies (the Military Academy) can build the rules' army unit.
		private bool CanBeBuiltAsArmy(City city) {
			string armyUnit = EngineStorage.gameData?.rules?.BuildArmyUnit;
			bool isArmyUnit = armyUnit != null ? armyUnit == this.name : this.isArmy;
			return isArmyUnit && city.constructed_buildings.Exists(cb => cb.building.allowsBuildArmy);
		}

		/// Whether this unit can be built in this city (by the owner), given this particular set of resources.
		private bool MeetsProductionRequirements(City city, HashSet<Resource> accessibleResources) {
			if (!city.owner.HasRequiredTechnology(this)) {
				return false;
			}

			if (this.IsSeaUnit() && !city.location.NeighborsWater()) {
				return false;
			}

			// Civ3: once any civ completes the Manhattan Project, every civ
			// with the required tech may build nuclear weapons.
			if (this.isNuclearWeapon && !NuclearWeaponsAllowed(EngineStorage.gameData)) {
				return false;
			}

			if (!this.requiredResources.All(accessibleResources.Contains)) {
				return false;
			}

			return true;
		}

		/// Whether some civ has built a great wonder that allows nuclear
		/// weapons (the Manhattan Project), which lets every civ build them.
		public static bool NuclearWeaponsAllowed(GameData gameData) {
			if (gameData == null || gameData.GreatWondersBuilt.Count == 0) {
				return false;
			}
			foreach (Building building in gameData.Buildings) {
				if (building.allowsNuclearWeapons && gameData.GreatWondersBuilt.Contains(building.name)) {
					return true;
				}
			}
			return false;
		}

		/// A unit is obsolete if a unit in its upgrade chain can be produced (in this city
		/// with the given resources).
		///
		/// "AllowLesserUnitProduction" removes unit obsolescence.
		private bool IsUnitObsolete(City city, HashSet<Resource> accessibleResources) {
			// TODO: Consider golden ages when determining whether a unit is obsolete.
			// If a golden age has not yet been triggered and a unit can trigger one,
			// it shouldn't be marked as obsolete, even if its upgrade is available.

			if (EngineStorage.gameData.rules.AllowLesserUnitProduction) return false;

			if (this.GetProducibleUpgrade(city, accessibleResources) == null) return false;

			return true;
		}

		/// The "true" upgrade for any given in-game unit. The unique unit prototype available to
		/// this unit for this civ, in this city, with the given resources.
		public UnitPrototype GetProducibleUpgrade(City city, HashSet<Resource> accessibleResources) {
			var civ = city.owner.civilization;

			// The units we might upgrade to that the civ can build, which
			// only depends on the prototypes, so it is cached.
			UpgradeGraph graph = UpgradeGraph.For(this);
			UnitPrototype[] candidates = graph.Candidates(this, civ);
			if (candidates.Length == 0)
				return null;

			// Filter down to units we can produce here. There are only ever a
			// few candidates, so their indices are kept on the stack rather
			// than in a new list per call.
			Span<int> producible = candidates.Length <= MaxStackUnits ? stackalloc int[candidates.Length] : new int[candidates.Length];
			int producibleCount = 0;
			for (int i = 0; i < candidates.Length; i++) {
				if (candidates[i].MeetsProductionRequirements(city, accessibleResources))
					producible[producibleCount++] = i;
			}

			// Select the best unit we can upgrade to. Say we are upgrading a Warrior: if Medieval Infantry
			// is available, we don't want to upgrade to a mere Swordsman.
			return LastInUpgradeOrder(candidates, producible.Slice(0, producibleCount), graph);
		}

		// Above this many units, the sorting scratch space goes on the heap.
		private const int MaxStackUnits = 32;

		/// The same unit as SortInUpgradeOrder(the units at indices).LastOrDefault(),
		/// without allocating for small inputs.
		private static UnitPrototype LastInUpgradeOrder(UnitPrototype[] all, ReadOnlySpan<int> indices, UpgradeGraph graph) {
			int n = indices.Length;
			if (n == 0)
				return null;
			if (n == 1)
				return all[indices[0]];

			Span<bool> before = n <= MaxStackUnits ? stackalloc bool[n * n] : new bool[n * n];
			Span<bool> placed = n <= MaxStackUnits ? stackalloc bool[n] : new bool[n];
			for (int i = 0; i < n; i++) {
				UnitPrototype ui = all[indices[i]];
				HashSet<UnitPrototype> reachable = graph.Reachable(ui);
				for (int j = 0; j < n; j++) {
					UnitPrototype uj = all[indices[j]];
					before[i * n + j] = i != j && ui != uj && reachable.Contains(uj);
				}
			}
			placed.Clear();
			int last = PlaceInUpgradeOrder(n, before, placed, null);
			return all[indices[last]];
		}

		/// Sorts by the upgrade relation: if a eventually upgrades to b, a
		/// comes before b. Units that don't upgrade to one another keep their
		/// relative order where the relation allows it. (This is a stable
		/// topological sort; the comparer this replaces wasn't a total order,
		/// so List.Sort could return the units in an inconsistent order.) The
		/// last unit is always one that doesn't upgrade to any of the others.
		internal static List<UnitPrototype> SortInUpgradeOrder(List<UnitPrototype> units) {
			return SortInUpgradeOrder(units, UpgradeGraph.Uncached(units));
		}

		private static List<UnitPrototype> SortInUpgradeOrder(List<UnitPrototype> units, UpgradeGraph graph) {
			int n = units.Count;
			List<UnitPrototype> sorted = new(n);
			if (n == 0)
				return sorted;

			// before[i * n + j]: unit i upgrades, eventually, to unit j.
			bool[] before = new bool[n * n];
			for (int i = 0; i < n; i++) {
				HashSet<UnitPrototype> reachable = graph.Reachable(units[i]);
				for (int j = 0; j < n; j++) {
					before[i * n + j] = i != j && units[i] != units[j] && reachable.Contains(units[j]);
				}
			}

			bool[] placed = new bool[n];
			List<int> order = new(n);
			PlaceInUpgradeOrder(n, before, placed, order);
			foreach (int i in order)
				sorted.Add(units[i]);
			return sorted;
		}

		// The stable topological sort behind SortInUpgradeOrder, over n units
		// whose relation is given by before (row-major, n * n) and with
		// placed all false. Adds the order to order if given, and returns the
		// index of the unit placed last.
		private static int PlaceInUpgradeOrder(int n, ReadOnlySpan<bool> before, Span<bool> placed, List<int> order) {
			int next = -1;
			for (int placedCount = 0; placedCount < n; placedCount++) {
				// Place the first unit nothing remaining has to come before.
				// If there is none, the upgrades loop (bad data), so place the
				// first remaining unit to break the loop.
				int firstRemaining = -1;
				next = -1;
				for (int j = 0; j < n && next < 0; j++) {
					if (placed[j])
						continue;
					if (firstRemaining < 0)
						firstRemaining = j;
					bool free = true;
					for (int i = 0; i < n && free; i++) {
						if (!placed[i] && before[i * n + j])
							free = false;
					}
					if (free)
						next = j;
				}
				if (next < 0)
					next = firstRemaining;
				placed[next] = true;
				order?.Add(next);
			}
			return next;
		}

		/// Whether target can be reached from this unit by following upgrades.
		internal bool UpgradesEventuallyTo(UnitPrototype target) {
			return UpgradeGraph.For(this).Reachable(this).Contains(target);
		}

		// Upgrade relations between prototypes, worked out once and cached,
		// since working them out means walking the upgrade graph and scanning
		// every prototype, for every unit in every city's production list.
		//
		// The results depend only on the prototypes' upgradesTo and
		// producibleBy (and on the list of the game's prototypes), which are
		// set up when a game is created or loaded, but are public and can be
		// filled in afterwards (e.g. by tests). So the graph remembers, for
		// the game's prototype list and every prototype reachable from it,
		// which upgradesTo list and producibleBy set it saw and their sizes,
		// and is rebuilt when any of these differ from the live data. This
		// check is linear in the number of prototypes, with no allocation;
		// it replaces work that was quadratic and allocating. It is skipped
		// altogether (O(1)) while UnitPrototype.UpgradeDataVersion, bumped by
		// any outside access to upgradesTo or producibleBy, and the game's
		// prototype list (reference and count) are as last validated. Prototypes
		// outside the graph (not reachable from the game's list) are
		// computed on a fresh, uncached graph each time.
		internal sealed class UpgradeGraph {
			private static UpgradeGraph current;

			// The UnitPrototype.UpgradeDataVersion as of the last time this
			// graph was found up to date, or -1. While the version and the
			// game's prototype list (reference and count) are unchanged,
			// nothing the graph depends on can have been changed through
			// UnitPrototype's public members, so For skips revalidating.
			private long validatedVersion = -1;

			private readonly List<UnitPrototype> source;
			private readonly UnitPrototype[] sourceItems;
			// Every prototype the results can depend on, with what was seen
			// of each.
			private readonly UnitPrototype[] members;
			private readonly List<UnitPrototype>[] memberUpgradesTo;
			private readonly int[] memberUpgradesToCount;
			private readonly HashSet<Civilization>[] memberProducibleBy;
			private readonly int[] memberProducibleByCount;
			private readonly HashSet<UnitPrototype> memberSet;

			private readonly object cacheLock = new();
			private readonly Dictionary<UnitPrototype, HashSet<UnitPrototype>> reachable = new();
			private readonly Dictionary<(UnitPrototype, Civilization), UnitPrototype[]> candidates = new();

			private UpgradeGraph(List<UnitPrototype> source) {
				this.source = source;
				sourceItems = source.ToArray();

				List<UnitPrototype> all = new();
				memberSet = new HashSet<UnitPrototype>();
				Stack<UnitPrototype> pending = new();
				foreach (UnitPrototype p in sourceItems) {
					pending.Push(p);
					while (pending.Count > 0) {
						UnitPrototype unit = pending.Pop();
						if (unit == null || !memberSet.Add(unit))
							continue;
						all.Add(unit);
						foreach (UnitPrototype next in unit.upgradesToList ?? [])
							pending.Push(next);
					}
				}

				members = all.ToArray();
				memberUpgradesTo = new List<UnitPrototype>[members.Length];
				memberUpgradesToCount = new int[members.Length];
				memberProducibleBy = new HashSet<Civilization>[members.Length];
				memberProducibleByCount = new int[members.Length];
				for (int i = 0; i < members.Length; i++) {
					memberUpgradesTo[i] = members[i].upgradesToList;
					memberUpgradesToCount[i] = members[i].upgradesToList?.Count ?? -1;
					memberProducibleBy[i] = members[i].producibleBySet;
					memberProducibleByCount[i] = members[i].producibleBySet?.Count ?? -1;
				}
			}

			// The graph to use for the given prototype.
			public static UpgradeGraph For(UnitPrototype proto) {
				List<UnitPrototype> source = EngineStorage.gameData?.unitPrototypes ?? [];
				UpgradeGraph graph = current;
				// Read before validating, so that a change made while
				// validating is caught by the next call.
				long version = UpgradeDataVersion;
				bool fresh = graph != null
					&& Interlocked.Read(ref graph.validatedVersion) == version
					&& ReferenceEquals(graph.source, source)
					&& source.Count == graph.sourceItems.Length;
				if (!fresh) {
					if (graph == null || !graph.IsUpToDate(source)) {
						graph = new UpgradeGraph(source);
						current = graph;
					}
					Interlocked.Exchange(ref graph.validatedVersion, version);
				}
				if (graph.memberSet.Contains(proto))
					return graph;
				// Not cacheable: nothing would tell us when this prototype or
				// those it upgrades to change. The graph is built over the
				// game's prototypes plus this one, since it's what the
				// results are computed from.
				return Uncached(new List<UnitPrototype>(source) { proto });
			}

			// A graph over the given prototypes, for one-off use.
			public static UpgradeGraph Uncached(List<UnitPrototype> source) {
				return new UpgradeGraph(source);
			}

			private bool IsUpToDate(List<UnitPrototype> liveSource) {
				if (!ReferenceEquals(source, liveSource) || liveSource.Count != sourceItems.Length)
					return false;
				for (int i = 0; i < sourceItems.Length; i++) {
					if (!ReferenceEquals(liveSource[i], sourceItems[i]))
						return false;
				}
				for (int i = 0; i < members.Length; i++) {
					UnitPrototype p = members[i];
					if (!ReferenceEquals(p.upgradesToList, memberUpgradesTo[i]) || (p.upgradesToList?.Count ?? -1) != memberUpgradesToCount[i])
						return false;
					if (!ReferenceEquals(p.producibleBySet, memberProducibleBy[i]) || (p.producibleBySet?.Count ?? -1) != memberProducibleByCount[i])
						return false;
				}
				return true;
			}

			// The prototypes reachable from proto by following one or more
			// upgrades.
			public HashSet<UnitPrototype> Reachable(UnitPrototype proto) {
				lock (cacheLock) {
					return ReachableLocked(proto);
				}
			}

			private HashSet<UnitPrototype> ReachableLocked(UnitPrototype proto) {
				if (reachable.TryGetValue(proto, out HashSet<UnitPrototype> result))
					return result;
				result = new HashSet<UnitPrototype>();
				var pending = new Stack<UnitPrototype>(proto.upgradesToList ?? []);
				while (pending.Count > 0) {
					var unit = pending.Pop();
					if (unit == null || !result.Add(unit)) continue;
					foreach (var next in unit.upgradesToList ?? []) pending.Push(next);
				}
				reachable[proto] = result;
				return result;
			}

			/// The units proto might upgrade to for the civ: its upgrade chain
			/// for that civ, in order. Empty if proto has no upgrade for the civ.
			///
			/// Unique units are already part of the chain (upgradesTo holds each
			/// civ's next unit, so a Cannon lists the Korean Hwach'a), so units
			/// from other chains that merely share a later upgrade are left out:
			/// a Warrior must not become a Longbowman just because Persia's
			/// Immortals and the Longbowman both upgrade to the Guerilla.
			public UnitPrototype[] Candidates(UnitPrototype proto, Civilization civ) {
				lock (cacheLock) {
					if (candidates.TryGetValue((proto, civ), out UnitPrototype[] result))
						return result;

					result = GetUpgradeChain(proto, civ).ToArray();
					candidates[(proto, civ)] = result;
					return result;
				}
			}

			/// Immediate upgrade target, given a civilization.
			///
			/// upgradesTo is the union of every civ's next upgrade, so a civ can match
			/// several targets: Spearman lists Pikeman and Musketman because some civ
			/// can't build Pikeman. The immediate target is the one the others are
			/// further along the chain from.
			private UnitPrototype GetUnitUpgrade(UnitPrototype proto, Civilization civ) {
				var match = (proto.upgradesToList ?? []).Where(x => x != null && (x.producibleBySet?.Contains(civ) ?? false)).ToList();
				if (match.Count > 1) {
					match = match.Where(x => !match.Any(y => y != x && ReachableLocked(y).Contains(x))).ToList();
					if (match.Count > 1)
						Log.Warning($"Unexpected upgrade chain: more than one valid target for upgrading {proto.name} with {civ.name}.");
				}
				return match.FirstOrDefault();
			}

			/// The upgrade chain: a unit upgrade series as an ordered collection of unit prototypes,
			/// for a particular civilization, starting from this unit.
			///
			/// Note: must be unique and stable: in-game every unit has at most one direct upgrade target.
			private List<UnitPrototype> GetUpgradeChain(UnitPrototype proto, Civilization civ) {
				var chain = new List<UnitPrototype>();
				// Bad data can make the upgrades loop; stop at the first unit
				// seen before (or the unit itself) instead of looping forever.
				var visited = new HashSet<UnitPrototype> { proto };
				var current = GetUnitUpgrade(proto, civ);
				while (current != null && visited.Add(current)) {
					chain.Add(current);
					current = GetUnitUpgrade(current, civ);
				}
				return chain;
			}
		}
	}
}
