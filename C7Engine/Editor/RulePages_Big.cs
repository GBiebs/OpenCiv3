using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using C7GameData;
using C7GameData.Save;

namespace C7Engine.Editor;

// Phase 6's rules pages: Civilizations, Civilization Advances, Flavors,
// Governments, Improvements and Wonders, Natural Resources, Terrain and
// Units. Renames and deletes go through RuleReferences, so the rest of the
// scenario follows.
public static partial class RulePages {
	private static bool Same(ID a, ID b) => a != null && b != null && a.ToString() == b.ToString();

	internal static List<string> BuildingNames(SaveGame s) => s.Buildings.Select(b => b.name).ToList();
	internal static List<string> CivNames(SaveGame s) => s.Civilizations.Select(c => c.name).ToList();
	internal static List<string> GovernmentNames(SaveGame s) => s.Governments.Select(g => g.name).ToList();
	internal static List<string> FlavorNames(SaveGame s) => (s.Rules.Flavors ?? []).Select(f => f.Name).ToList();
	private static List<string> TerrainNames(SaveGame s) => s.TerrainTypes.Select(t => t.DisplayName).ToList();
	private static string GovernmentName(SaveGame s, ID id) => s.Governments.Find(g => Same(g.id, id))?.name;
	private static ID GovernmentId(SaveGame s, string name) => name == null ? null : s.Governments.Find(g => g.name == name)?.id;

	private static List<string> TechNamesOf(SaveGame s, IEnumerable<ID> ids) => s.Techs.Where(t => ids.Any(i => Same(i, t.id))).Select(t => t.Name).ToList();
	private static IEnumerable<ID> TechIdsOf(SaveGame s, IEnumerable<string> names) => names.Select(n => TechId(s, n)).Where(i => i != null);

	private static List<string> ResourceNamesOf(SaveGame s, IEnumerable<string> keys) => keys.Select(k => s.Resources.Find(r => r.Key == k)?.Name ?? k).ToList();
	private static IEnumerable<string> ResourceKeysOf(SaveGame s, IEnumerable<string> names) => names.Select(n => s.Resources.Find(r => r.Name == n)?.Key ?? n);

	// An item's flavors (Rules.Flavors keeps, for each flavor, who has it).
	private static RuleField Flavors<T>(string group, Func<T, string> nameOf, Func<FlavorRule, List<string>> members, int column = 0) => new() {
		Group = group,
		Column = column,
		Label = "Flavors:",
		Kind = RuleFieldKind.Choices,
		NotUsed = true,
		Help = "The leanings it has, which steer the AI's choices (Civ3's flavors).",
		Choices = FlavorNames,
		Get = (s, o) => (s.Rules.Flavors ?? []).Where(f => members(f).Contains(nameOf((T)o))).Select(f => f.Name).ToList(),
		Set = (s, o, v) => {
			string name = nameOf((T)o);
			foreach (FlavorRule f in s.Rules.Flavors ?? []) {
				members(f).Remove(name);
				if (((List<string>)v).Contains(f.Name)) {
					members(f).Add(name);
				}
			}
		},
	};

	// A copy of an item, under a name no other has.
	private static T Copy<T>(IList<T> list, T like, Func<T, string> nameOf, Action<T, string> setName) {
		T copy = SaveGame.DeepCopy(like);
		setName(copy, Rule.Unique(nameOf(like), list.Select(nameOf)));
		list.Insert(list.IndexOf(like) + 1, copy);
		return copy;
	}

	private static ID NewId(IEnumerable<ID> ids, string fallbackPrefix) {
		List<string> all = ids.Where(i => i != null).Select(i => i.ToString()).ToList();
		string prefix = all.FirstOrDefault(i => i.Contains('-'))?.Split('-')[0] ?? fallbackPrefix;
		return ID.FromString(Rule.NextId(prefix, all));
	}

	// Civ3's Natural Resources page.
	private static RulePage Resources() => new() {
		Name = "Natural Resources",
		ItemLabel = "Resource:",
		Items = s => s.Resources.Where(r => r.Category != ResourceCategory.NONE).ToList(),
		NameOf = (_, o) => ((Resource)o).Name,
		Rename = (s, o, name) => Renamed<Resource>(s.Resources, (Resource)o, name, r => r.Name, (r, n) => r.Name = n),
		Add = (s, o) => {
			Resource copy = Copy(s.Resources, (Resource)o, r => r.Name, (r, n) => r.Name = n);
			copy.Key = Rule.Unique(((Resource)o).Key, s.Resources.Where(r => r != copy).Select(r => r.Key));
			return copy;
		},
		Delete = (s, o) => {
			RuleReferences.ResourceGone(s, (Resource)o);
			s.Resources.Remove((Resource)o);
			return null;
		},
		UsedBy = (s, o) => RuleReferences.ResourceUses(s, (Resource)o),
		Restore = (s, d) => s.Resources = d.Resources,
		Fields = [
			Rule.Text<Resource>("", "Civilopedia Entry:", r => r.CivilopediaEntry, (r, v) => r.CivilopediaEntry = v, "The resource's Civilopedia key."),
			Rule.Number<Resource>("", "Icon:", r => r.Icon, (r, v) => r.Icon = v, 0, 99, "The resource's icon, by its place in the resource icons."),
			Rule.Choice<Resource>("", "Prerequisite:", (s, r) => TechName(s, r.Prerequisite), (s, r, v) => r.Prerequisite = TechId(s, v), TechNames,
				"The advance that shows the resource (and lets it be used).", allowNone: true),
			Rule.OneOf<Resource, ResourceCategory>("", "Type:", r => r.Category, (r, v) => r.Category = v,
				"Bonus (more yield), luxury (happiness through trade) or strategic (needed for units and buildings)."),
			Rule.Number<Resource>("Bonus", "Food:", r => r.FoodBonus, (r, v) => r.FoodBonus = v, -9, 99, "Food the resource adds to its tile.", column: 1),
			Rule.Number<Resource>("Bonus", "Shields:", r => r.ShieldsBonus, (r, v) => r.ShieldsBonus = v, -9, 99, "Shields it adds.", column: 1),
			Rule.Number<Resource>("Bonus", "Commerce:", r => r.CommerceBonus, (r, v) => r.CommerceBonus = v, -9, 99, "Commerce it adds.", column: 1),
			Rule.Number<Resource>("", "Appearance Ratio:", r => r.AppearanceRatio, (r, v) => r.AppearanceRatio = v, 0, 9999,
				"How common it is on a new map."),
			Rule.Number<Resource>("", "Disappearance Probability:", r => r.DisappearanceRatio, (r, v) => r.DisappearanceRatio = v, 0, 9999,
				"The chance a strategic resource runs out, a turn."),
		],
	};

	private static int TerrainBonus(SaveGame s, TerrainType t, string improvement, Tile.YieldType type) =>
		s.TerrainImprovements.Find(i => i.key == improvement)?.bonusYields.GetValueOrDefault(t.Key)?.GetValueOrDefault(type) ?? 0;

	private static void SetTerrainBonus(SaveGame s, TerrainType t, string improvement, Tile.YieldType type, int value) {
		SaveTerrainImprovement i = s.TerrainImprovements.Find(x => x.key == improvement);
		if (i == null) {
			return;
		}
		if (!i.bonusYields.TryGetValue(t.Key, out Dictionary<Tile.YieldType, int> yields)) {
			i.bonusYields[t.Key] = yields = [];
		}
		yields[type] = value;
	}

	private static RuleField Landmark(string label, Func<TerrainLandmark, int> get, Action<TerrainLandmark, int> set, int min, int max,
			bool notUsed = false, string help = "Its value on a landmark tile of this terrain, in place of the terrain's.") => new() {
		Group = "Landmark Terrain",
		Column = 2,
		Label = label,
		Kind = RuleFieldKind.Number,
		Min = min,
		Max = max,
		NotUsed = notUsed,
		Help = help,
		Get = (_, o) => get(((TerrainType)o).landmark ?? new TerrainLandmark()),
		Set = (_, o, v) => set(((TerrainType)o).landmark ??= new TerrainLandmark(), (int)v),
	};

	// Civ3's Terrain page (with its landmark terrain). Civ3's terrains are
	// fixed: no Add or Delete.
	private static RulePage Terrain() => new() {
		Name = "Terrain",
		ItemLabel = "Terrain:",
		Items = s => s.TerrainTypes,
		NameOf = (_, o) => ((TerrainType)o).DisplayName,
		Rename = (s, o, name) => Renamed<TerrainType>(s.TerrainTypes, (TerrainType)o, name, t => t.DisplayName, (t, n) => t.DisplayName = n),
		Restore = (s, d) => {
			s.TerrainTypes = d.TerrainTypes;
			s.TerrainImprovements = d.TerrainImprovements;
		},
		Fields = [
			Rule.Number<TerrainType>("Yield", "Food:", t => t.baseFoodProduction, (t, v) => t.baseFoodProduction = v, 0, 99, "Food the terrain yields."),
			Rule.Number<TerrainType>("Yield", "Shields:", t => t.baseShieldProduction, (t, v) => t.baseShieldProduction = v, 0, 99, "Shields it yields."),
			Rule.Number<TerrainType>("Yield", "Commerce:", t => t.baseCommerceProduction, (t, v) => t.baseCommerceProduction = v, 0, 99, "Commerce it yields."),
			new RuleField {
				Group = "Bonuses", Label = "Irrigation:", Kind = RuleFieldKind.Number, Min = 0, Max = 99, Help = "Food irrigation adds.",
				Get = (s, o) => TerrainBonus(s, (TerrainType)o, Tile.TileOverlays.IRRIGATION, Tile.YieldType.Food),
				Set = (s, o, v) => SetTerrainBonus(s, (TerrainType)o, Tile.TileOverlays.IRRIGATION, Tile.YieldType.Food, (int)v),
			},
			new RuleField {
				Group = "Bonuses", Label = "Mining:", Kind = RuleFieldKind.Number, Min = 0, Max = 99, Help = "Shields a mine adds.",
				Get = (s, o) => TerrainBonus(s, (TerrainType)o, Tile.TileOverlays.MINE, Tile.YieldType.Production),
				Set = (s, o, v) => SetTerrainBonus(s, (TerrainType)o, Tile.TileOverlays.MINE, Tile.YieldType.Production, (int)v),
			},
			new RuleField {
				Group = "Bonuses", Label = "Road:", Kind = RuleFieldKind.Number, Min = 0, Max = 99, Help = "Commerce a road (and a railroad) adds.",
				Get = (s, o) => TerrainBonus(s, (TerrainType)o, Tile.TileOverlays.ROAD, Tile.YieldType.Commerce),
				Set = (s, o, v) => {
					SetTerrainBonus(s, (TerrainType)o, Tile.TileOverlays.ROAD, Tile.YieldType.Commerce, (int)v);
					SetTerrainBonus(s, (TerrainType)o, Tile.TileOverlays.RAILROAD, Tile.YieldType.Commerce, (int)v);
				},
			},
			Rule.Number<TerrainType>("Movement and Defense", "Movement Cost:", t => t.movementCost, (t, v) => t.movementCost = v, 1, 99, "Moves a unit spends to enter."),
			Rule.Number<TerrainType>("Movement and Defense", "Defense Bonus (%):", t => (int)Math.Round(t.defenseBonus.amount * 100),
				(t, v) => t.defenseBonus = new StrengthBonus { description = t.defenseBonus.description ?? t.DisplayName, amount = v / 100.0 }, 0, 999,
				"The defense bonus of units on it."),
			Rule.Check<TerrainType>("Movement and Defense", "Impassable", t => t.impassable, (t, v) => t.impassable = v, "No land unit may enter."),
			Rule.Check<TerrainType>("Movement and Defense", "Impassable by Wheeled Units", t => t.impassableByWheeled, (t, v) => t.impassableByWheeled = v,
				"Wheeled units may not enter."),
			Rule.OneOf<TerrainType, TerrainType.Civ3FoliageAction>("Workers", "Worker Job Allowed:", t => t.allowedFoliageAction, (t, v) => t.allowedFoliageAction = v,
				"What workers may do to the vegetation here.", column: 1),
			Rule.Choice<TerrainType>("Workers", "Pollution Effect:", (_, t) => t.pollutionEffect ?? t.PollutionEffect(), (_, t, v) => t.pollutionEffect = v,
				s => [TerrainType.PollutionEffectNone, TerrainType.PollutionEffectBaseTerrain, .. s.TerrainTypes.Select(x => x.Key)],
				"What pollution leaves here: nothing, the terrain under the vegetation, or another terrain.", column: 1),
			Rule.Choices<TerrainType>("Workers", "Possible Resources:", (s, t) => ResourceNamesOf(s, t.allowedResources),
				(s, t, v) => t.allowedResources = ResourceKeysOf(s, v).ToHashSet(), ResourceNames, "The resources the terrain can have.", column: 1),
			Rule.Check<TerrainType>("Allows", "Cities", t => t.allowCities, (t, v) => t.allowCities = v, "Cities may be built here.", column: 1),
			Rule.Check<TerrainType>("Allows", "Colonies", t => t.allowColonies, (t, v) => t.allowColonies = v, "Colonies may be built here.", column: 1),
			Rule.Check<TerrainType>("Allows", "Airfields", t => t.allowAirfields, (t, v) => t.allowAirfields = v, "Airfields may be built here.", column: 1),
			Rule.Check<TerrainType>("Allows", "Forts", t => t.allowForts, (t, v) => t.allowForts = v, "Fortresses may be built here.", column: 1),
			Rule.Check<TerrainType>("Allows", "Outposts", t => t.allowOutposts, (t, v) => t.allowOutposts = v, "Outposts may be built here.", column: 1),
			Rule.Check<TerrainType>("Allows", "Radar Towers", t => t.allowRadarTowers, (t, v) => t.allowRadarTowers = v, "Radar towers may be built here.", column: 1),
			Rule.Check<TerrainType>("Disease", "Causes Disease", t => t.causesDisease, (t, v) => t.causesDisease = v, "Units and citizens here may fall ill.", column: 2),
			Rule.Check<TerrainType>("Disease", "Disease Can Be Cured", t => t.diseaseCanBeCured, (t, v) => t.diseaseCanBeCured = v,
				"An advance (Medicine) ends the disease.", column: 2),
			Rule.Number<TerrainType>("Disease", "Disease Strength:", t => t.diseaseStrength, (t, v) => t.diseaseStrength = v, 0, 100, "How likely it strikes.", column: 2),
			new RuleField {
				Group = "Landmark Terrain", Column = 2, Label = "Enabled", Kind = RuleFieldKind.Check,
				Help = "The terrain has a landmark version, placed with the overlay brush; a landmark tile has the values below.",
				Get = (_, o) => ((TerrainType)o).landmark?.enabled ?? false,
				Set = (_, o, v) => (((TerrainType)o).landmark ??= new TerrainLandmark()).enabled = (bool)v,
			},
			new RuleField {
				Group = "Landmark Terrain", Column = 2, Label = "Name:", Kind = RuleFieldKind.Text, Help = "The landmark's name, shown for the tile in the game.",
				Get = (_, o) => ((TerrainType)o).landmark?.name ?? "",
				Set = (_, o, v) => (((TerrainType)o).landmark ??= new TerrainLandmark()).name = (string)v,
			},
			Landmark("Food:", l => l.food, (l, v) => l.food = v, 0, 99),
			Landmark("Shields:", l => l.shields, (l, v) => l.shields = v, 0, 99),
			Landmark("Commerce:", l => l.commerce, (l, v) => l.commerce = v, 0, 99),
			Landmark("Irrigation Bonus:", l => l.irrigationBonus, (l, v) => l.irrigationBonus = v, 0, 99, true, "Civ3's irrigation bonus on the landmark."),
			Landmark("Mining Bonus:", l => l.miningBonus, (l, v) => l.miningBonus = v, 0, 99, true, "Civ3's mining bonus on the landmark."),
			Landmark("Road Bonus:", l => l.roadBonus, (l, v) => l.roadBonus = v, 0, 99, true, "Civ3's road bonus on the landmark."),
			Landmark("Movement Cost:", l => l.movementCost, (l, v) => l.movementCost = v, 0, 99),
			Landmark("Defense Bonus (%):", l => l.defenseBonus, (l, v) => l.defenseBonus = v, 0, 999),
		],
	};

	// Civ3's Governments page, with its ruler titles and how each
	// government stands against the others.
	private static RulePage Governments() => new() {
		Name = "Governments",
		ItemLabel = "Government:",
		Items = s => s.Governments,
		NameOf = (_, o) => ((Government)o).name,
		Rename = (s, o, name) => {
			Government g = (Government)o;
			string old = g.name;
			string why = Renamed<Government>(s.Governments, g, name, x => x.name, (x, n) => x.name = n);
			if (why == null) {
				RuleReferences.Government(s, old, g.name);
			}
			return why;
		},
		Add = (s, o) => {
			Government g = Copy(s.Governments, (Government)o, x => x.name, (x, n) => x.name = n);
			g.id = NewId(s.Governments.Select(x => x.id), "Government");
			g.defaultType = g.transitionType = false;
			return g;
		},
		Delete = (s, o) => {
			Government g = (Government)o;
			if (g.defaultType || g.transitionType) {
				return "The starting government and the transition government can't be deleted.";
			}
			RuleReferences.GovernmentGone(s, g);
			s.Governments.Remove(g);
			return null;
		},
		UsedBy = (s, o) => RuleReferences.GovernmentUses(s, (Government)o),
		Restore = (s, d) => s.Governments = d.Governments,
		Problem = s => s.Governments.Count(g => g.defaultType) != 1 ? "Exactly one government must be the starting government."
			: s.Governments.Count(g => g.transitionType) != 1 ? "Exactly one government must be the transition government (Anarchy)." : null,
		Fields = [
			Rule.Text<Government>("", "Civilopedia Entry:", g => g.civilopediaEntry, (g, v) => g.civilopediaEntry = v, "The government's Civilopedia key."),
			Rule.Choice<Government>("", "Required Advance:", (s, g) => TechName(s, g.prerequisiteTech), (s, g, v) => g.prerequisiteTech = TechId(s, v), TechNames,
				"The advance needed to adopt it.", allowNone: true),
			new RuleField {
				Label = "Default Type (starting government)", Kind = RuleFieldKind.Check, Help = "Civilizations start the game under it. One government is.",
				Get = (_, o) => ((Government)o).defaultType,
				Set = (s, o, v) => {
					if ((bool)v) {
						s.Governments.ForEach(g => g.defaultType = false);
					}
					((Government)o).defaultType = (bool)v;
				},
			},
			new RuleField {
				Label = "Transition Type (anarchy)", Kind = RuleFieldKind.Check, Help = "The government between two others. One government is.",
				Get = (_, o) => ((Government)o).transitionType,
				Set = (s, o, v) => {
					if ((bool)v) {
						s.Governments.ForEach(g => g.transitionType = false);
					}
					((Government)o).transitionType = (bool)v;
				},
			},
			Rule.Check<Government>("", "Requires Maintenance", g => g.RequiresMaintenance(), (g, v) => g.requiresMaintenance = v, "Buildings cost upkeep under it."),
			Rule.Check<Government>("", "Tile Penalty (despotism)", g => g.hasTilePenalty, (g, v) => g.hasTilePenalty = v,
				"Tiles yielding more than 2 of anything yield one less."),
			Rule.Check<Government>("", "Trade Bonus", g => g.hasTradeBonus, (g, v) => g.hasTradeBonus = v, "+1 commerce on tiles with commerce."),
			Rule.OneOf<Government, Government.CorruptionType>("", "Corruption and Waste:", g => g.corruptionType, (g, v) => g.corruptionType = v,
				"How much is lost to corruption and waste."),
			Rule.OneOf<Government, Government.HurryProductionType>("", "Hurrying:", g => g.hurryingType, (g, v) => g.hurryingType = v,
				"How production is hurried: not at all, with citizens, or with gold."),
			Rule.Number<Government>("", "Rate Cap (tenths):", g => g.rateCap, (g, v) => g.rateCap = v, 1, 10, "The highest a tax, science or luxury slider goes."),
			Rule.Number<Government>("", "War Weariness:", g => g.warWeariness, (g, v) => g.warWeariness = v, 0, 2, "0 none, 1 low, 2 high."),
			Rule.Check<Government>("", "Xenophobic", g => g.xenophobic, (g, v) => g.xenophobic = v, "No culture where under half the city is of the civilization."),
			Rule.Check<Government>("", "Forced Resettlement", g => g.forcedResettlement, (g, v) => g.forcedResettlement = v,
				"Adopting it costs every city some population."),
			Rule.Check<Government>("Units", "All Units Free", g => g.allUnitsFree, (g, v) => g.allUnitsFree = v, "No unit costs upkeep.", column: 1),
			Rule.Number<Government>("Units", "Free Units per Town:", g => g.freeUnitsPerTown, (g, v) => g.freeUnitsPerTown = v, 0, 99, "Units a town supports free.", column: 1),
			Rule.Number<Government>("Units", "Free Units per City:", g => g.freeUnitsPerCity, (g, v) => g.freeUnitsPerCity = v, 0, 99, "Units a city supports free.", column: 1),
			Rule.Number<Government>("Units", "Free Units per Metropolis:", g => g.freeUnitsPerMetropolis, (g, v) => g.freeUnitsPerMetropolis = v, 0, 99,
				"Units a metropolis supports free.", column: 1),
			Rule.Number<Government>("Units", "Cost per Unit:", g => g.unitCost, (g, v) => g.unitCost = v, 0, 99, "Gold each unit beyond those costs.", column: 1),
			Rule.Number<Government>("Units", "Draft Limit:", g => g.draftLimit, (g, v) => g.draftLimit = v, 0, 99, "Citizens a city may draft a turn.", column: 1),
			Rule.Number<Government>("Units", "Military Police Limit:", g => g.militaryPoliceLimit, (g, v) => g.militaryPoliceLimit = v, 0, 99,
				"Units in a city that each make a citizen content.", column: 1),
			Rule.Number<Government>("Units", "Worker Rate:", g => g.workerRate, (g, v) => g.workerRate = v, 0, 99, "How fast workers work.", column: 1),
			Rule.Lines<Government>("Ruler Titles", "Male (one a line):", g => g.maleRulerTitles, (g, v) => g.maleRulerTitles = v,
				"How a male leader is addressed, by era.", column: 1),
			Rule.Lines<Government>("Ruler Titles", "Female (one a line):", g => g.femaleRulerTitles, (g, v) => g.femaleRulerTitles = v,
				"How a female leader is addressed, by era.", column: 1),
			Rule.Number<Government>("Espionage", "Assimilation Chance (%):", g => g.assimilationChance, (g, v) => g.assimilationChance = v, 0, 100,
				"The chance, each turn, that a foreign citizen in one of its cities takes the city's nationality.", column: 2),
			Rule.Choice<Government>("Espionage", "Diplomats Are:", (_, g) => g.diplomatLevel, (_, g, v) => g.diplomatLevel = v,
				s => s.ExperienceLevels.Select(e => e.key).ToList(), "Its diplomats' experience: the embassy's missions get away more often the more experienced they are.", allowNone: true, column: 2),
			Rule.Choice<Government>("Espionage", "Spies Are:", (_, g) => g.spyLevel, (_, g, v) => g.spyLevel = v,
				s => s.ExperienceLevels.Select(e => e.key).ToList(), "Its planted spies' experience.", allowNone: true, column: 2),
			Rule.Choice<Government>("Espionage", "Immune To:", (s, g) => g.immuneTo >= 0 && g.immuneTo < s.Rules.EspionageMissions.Count ? s.Rules.EspionageMissions[g.immuneTo].Name : null,
				(s, g, v) => g.immuneTo = v == null ? -1 : s.Rules.EspionageMissions.FindIndex(m => m.Name == v),
				s => s.Rules.EspionageMissions.Select(m => m.Name).ToList(), "The espionage mission no one can carry out against it.", allowNone: true, column: 2),
			new RuleField {
				Group = "Against Each Government", Column = 2, Label = "Can Bribe:", Kind = RuleFieldKind.Choices,
				Help = "The governments its propaganda can work on.",
				Choices = GovernmentNames,
				Get = (_, o) => ((Government)o).relations.Where(r => r.canBribe).Select(r => r.government).ToList(),
				Set = (s, o, v) => {
					Government g = (Government)o;
					foreach (string name in GovernmentNames(s)) {
						Relation(g, name).canBribe = ((List<string>)v).Contains(name);
					}
				},
			},
			new RuleField {
				Group = "Against Each Government", Column = 2, Label = "Bribery Modifier:", Kind = RuleFieldKind.Numbers, Min = -999, Max = 999,
				Help = "Added to its propaganda's chance against each government (higher is easier).",
				NumberLabels = (s, _) => GovernmentNames(s),
				Get = (s, o) => GovernmentNames(s).Select(n => Relation((Government)o, n).briberyModifier).ToList(),
				Set = (s, o, v) => SetRelations(s, (Government)o, (List<int>)v, (r, x) => r.briberyModifier = x),
			},
			new RuleField {
				Group = "Against Each Government", Column = 2, Label = "Resistance Modifier:", Kind = RuleFieldKind.Numbers, Min = -999, Max = 999,
				Help = "Added to the chance that citizens of a city it takes from each government resist.",
				NumberLabels = (s, _) => GovernmentNames(s),
				Get = (s, o) => GovernmentNames(s).Select(n => Relation((Government)o, n).resistanceModifier).ToList(),
				Set = (s, o, v) => SetRelations(s, (Government)o, (List<int>)v, (r, x) => r.resistanceModifier = x),
			},
		],
	};

	private static GovernmentRelation Relation(Government g, string other) {
		GovernmentRelation r = g.relations.Find(x => x.government == other);
		if (r == null) {
			g.relations.Add(r = new GovernmentRelation { government = other });
		}
		return r;
	}

	private static void SetRelations(SaveGame s, Government g, List<int> values, Action<GovernmentRelation, int> set) {
		List<string> names = GovernmentNames(s);
		for (int i = 0; i < names.Count && i < values.Count; ++i) {
			set(Relation(g, names[i]), values[i]);
		}
	}

	private static readonly string[] EraKeyOrder = [EraUtils.ANCIENT_TIMES_CVLPD, EraUtils.MIDDLE_AGES_CVLPD, EraUtils.INDUSTRIAL_AGE_CVLPD, EraUtils.MODERN_ERA_CVLPD];

	// Civ3's Civilization Advances page. X and Y place the advance on the
	// science advisor's tree (the editor's tree view drags them).
	private static RulePage Advances() => new() {
		Name = "Civilization Advances",
		ItemLabel = "Advance:",
		Items = s => s.Techs,
		NameOf = (_, o) => ((SaveTech)o).Name,
		Rename = (s, o, name) => {
			SaveTech t = (SaveTech)o;
			string old = t.Name;
			string why = Renamed<SaveTech>(s.Techs, t, name, x => x.Name, (x, n) => x.Name = n);
			if (why == null) {
				RuleReferences.TechRenamed(s, old, t.Name);
			}
			return why;
		},
		Add = (s, o) => {
			SaveTech t = Copy(s.Techs, (SaveTech)o, x => x.Name, (x, n) => x.Name = n);
			t.id = NewId(s.Techs.Select(x => x.id), "Tech");
			t.Prerequisites = [((SaveTech)o).id];
			t.Y += 20;
			return t;
		},
		Delete = (s, o) => {
			RuleReferences.TechGone(s, (SaveTech)o);
			s.Techs.Remove((SaveTech)o);
			return null;
		},
		UsedBy = (s, o) => RuleReferences.TechUses(s, (SaveTech)o),
		Restore = (s, d) => s.Techs = d.Techs,
		Problem = s => {
			// No advance may need itself, even round about.
			Dictionary<string, SaveTech> byId = s.Techs.Where(t => t.id != null).ToDictionary(t => t.id.ToString());
			foreach (SaveTech t in s.Techs) {
				HashSet<string> seen = [];
				Stack<SaveTech> open = new([t]);
				while (open.Count > 0) {
					foreach (ID p in open.Pop().Prerequisites) {
						if (Same(p, t.id)) {
							return $"{t.Name} needs itself (through its prerequisites).";
						}
						if (seen.Add(p.ToString()) && byId.TryGetValue(p.ToString(), out SaveTech next)) {
							open.Push(next);
						}
					}
				}
			}
			return null;
		},
		Fields = [
			Rule.Text<SaveTech>("", "Civilopedia Entry:", t => t.CivilopediaEntry, (t, v) => t.CivilopediaEntry = v, "The advance's Civilopedia key."),
			Rule.Number<SaveTech>("", "Cost:", t => t.Cost, (t, v) => t.Cost = v, 0, 99999, "The advance's cost, before the world size's tech rate."),
			Rule.Choice<SaveTech>("", "Era:", (s, t) => EraName(s, t.EraCivilopediaName ?? EraKeyOrder[0]),
				(s, t, v) => t.EraCivilopediaName = EraKeyOrder.First(k => EraName(s, k) == v),
				s => EraKeyOrder.Select(k => EraName(s, k)).ToList(), "The era the advance belongs to."),
			Rule.Choices<SaveTech>("", "Prerequisites:", (s, t) => TechNamesOf(s, t.Prerequisites), (s, t, v) => t.Prerequisites = TechIdsOf(s, v).ToList(),
				TechNames, "The advances needed first (Civ3 allows four).", maxChoices: 4),
			Rule.Check<SaveTech>("", "Required for Era Advancement", t => t.RequiredForEraAdvancement, (t, v) => t.RequiredForEraAdvancement = v,
				"A civilization must know it to enter the next era."),
			Rule.Number<SaveTech>("Science Advisor", "X:", t => t.X, (t, v) => t.X = v, 0, 9999, "Where the advance is on the science advisor's tree."),
			Rule.Number<SaveTech>("Science Advisor", "Y:", t => t.Y, (t, v) => t.Y = v, 0, 9999, "Where the advance is on the science advisor's tree."),
			Rule.Text<SaveTech>("Science Advisor", "Icon:", t => t.SmallIconPath, (t, v) => t.SmallIconPath = v, "The advance's small icon file."),
			Rule.Flags<SaveTech, SaveTech.Flag>("Flags", "Flags:", t => t.flags, "What knowing the advance does.", column: 1),
			Flavors<SaveTech>("Flags", t => t.Name, f => f.Techs, column: 1),
		],
	};

	// Civ3's Civilizations page.
	private static RulePage Civilizations() => new() {
		Name = "Civilizations",
		ItemLabel = "Civilization:",
		Items = s => s.Civilizations,
		NameOf = (_, o) => ((Civilization)o).name,
		Rename = (s, o, name) => {
			Civilization c = (Civilization)o;
			string old = c.name;
			string why = Renamed<Civilization>(s.Civilizations, c, name, x => x.name, (x, n) => x.name = n);
			if (why == null) {
				RuleReferences.Civilization(s, old, c.name);
			}
			return why;
		},
		Add = (s, o) => {
			Civilization c = Copy(s.Civilizations, (Civilization)o, x => x.name, (x, n) => x.name = n);
			c.isBarbarian = c.isCityStates = false;
			return c;
		},
		Delete = (s, o) => {
			Civilization c = (Civilization)o;
			if (c.isBarbarian) {
				return "The barbarians can't be deleted.";
			}
			if (s.Players.Any(p => p.civilization == c.name)) {
				return $"A player of the scenario is {c.name}: remove the player first.";
			}
			RuleReferences.Civilization(s, c.name, null);
			s.Civilizations.Remove(c);
			return null;
		},
		UsedBy = (s, o) => RuleReferences.CivilizationUses(s, ((Civilization)o).name),
		Restore = (s, d) => {
			s.Civilizations = d.Civilizations;
			s.CultureGroups = d.CultureGroups;
		},
		Fields = [
			Rule.Text<Civilization>("Names", "Noun:", c => c.noun, (c, v) => c.noun = v, "The people: \"Romans\"."),
			Rule.Text<Civilization>("Names", "Adjective:", c => c.adjective, (c, v) => c.adjective = v, "\"Roman\"."),
			Rule.Check<Civilization>("Names", "Plural Name", c => c.pluralName, (c, v) => c.pluralName = v, "The civilization's name is plural.", notUsed: true),
			Rule.OneOf<Civilization, Gender>("Names", "Name's Gender:", c => c.nameGender, (c, v) => c.nameGender = v, "The name's grammatical gender.", notUsed: true),
			Rule.Text<Civilization>("Names", "Civilopedia Entry:", c => c.civilopediaEntry, (c, v) => c.civilopediaEntry = v, "The civilization's Civilopedia key."),
			Rule.Text<Civilization>("Leader", "Leader:", c => c.leader, (c, v) => c.leader = v, "The leader's name."),
			Rule.Text<Civilization>("Leader", "Title:", c => c.leaderTitle, (c, v) => c.leaderTitle = v, "The leader's title."),
			Rule.OneOf<Civilization, Gender>("Leader", "Gender:", c => c.leaderGender, (c, v) => c.leaderGender = v, "The leader's gender."),
			Rule.Text<Civilization>("Leader", "Leader Art:", c => c.leaderArtFile, (c, v) => c.leaderArtFile = v, "The leader's portrait file."),
			Rule.Number<Civilization>("Colors", "Default Color:", c => c.primaryColorIndex, (c, v) => c.primaryColorIndex = v, 0, 31, "The civilization's color."),
			Rule.Number<Civilization>("Colors", "Unique Color:", c => c.secondaryColorIndex, (c, v) => c.secondaryColorIndex = v, 0, 31, "Its second color."),
			Rule.Choice<Civilization>("Character", "Culture Group:", (s, c) => c.cultureGroupKey ?? c.cultureGroup?.name, (_, c, v) => c.cultureGroupKey = v,
				s => s.CultureGroups.Select(g => g.name).ToList(), "The look of its cities.", column: 1),
			Rule.Flags<Civilization, Civilization.Trait>("Character", "Traits:", c => c.traits, "Its two traits (Civ3's bonuses).", column: 1),
			Rule.Choices<Civilization>("Character", "Free Techs:", (s, c) => TechNamesOf(s, c.startingTechs), (s, c, v) => c.startingTechs = TechIdsOf(s, v).ToHashSet(),
				TechNames, "The advances it starts with.", column: 1),
			Rule.Choice<Civilization>("Character", "Favorite Government:", (_, c) => c.favoriteGovernment, (_, c, v) => c.favoriteGovernment = v, GovernmentNames,
				"The government its AI prefers.", allowNone: true, column: 1),
			Rule.Choice<Civilization>("Character", "Shunned Government:", (_, c) => c.shunnedGovernment, (_, c, v) => c.shunnedGovernment = v, GovernmentNames,
				"The government its AI avoids.", allowNone: true, column: 1),
			Rule.Number<Civilization>("Character", "Aggression (-2 to 2):", c => c.aggressionLevel, (c, v) => c.aggressionLevel = v, -2, 2, "How warlike its AI is.", column: 1),
			Rule.Choice<Civilization>("Character", "King Unit:", (_, c) => c.kingUnit, (_, c, v) => c.kingUnit = v, UnitNames, "Its king under Regicide.",
				allowNone: true, column: 1),
			Rule.Flags<Civilization, PlanFocus>("City Governor", "Build Often:", c => c.buildOften, "What its AI builds often.", column: 1),
			Rule.Flags<Civilization, PlanFocus>("City Governor", "Build Never:", c => c.buildNever, "What its AI never builds.", column: 1),
			Rule.Check<Civilization>("City Governor", "Manage Citizens", c => c.governorManagesCitizens, (c, v) => c.governorManagesCitizens = v, "Civ3's governor setting.", notUsed: true, column: 1),
			Rule.Check<Civilization>("City Governor", "Emphasize Food", c => c.governorEmphasizesFood, (c, v) => c.governorEmphasizesFood = v, "Civ3's governor setting.", notUsed: true, column: 1),
			Rule.Check<Civilization>("City Governor", "Emphasize Shields", c => c.governorEmphasizesShields, (c, v) => c.governorEmphasizesShields = v, "Civ3's governor setting.", notUsed: true, column: 1),
			Rule.Check<Civilization>("City Governor", "Emphasize Trade", c => c.governorEmphasizesTrade, (c, v) => c.governorEmphasizesTrade = v, "Civ3's governor setting.", notUsed: true, column: 1),
			Rule.Check<Civilization>("City Governor", "Manage Production", c => c.governorManagesProduction, (c, v) => c.governorManagesProduction = v, "Civ3's governor setting.", notUsed: true, column: 1),
			Rule.Check<Civilization>("City Governor", "No Wonders", c => c.governorNoWonders, (c, v) => c.governorNoWonders = v, "Civ3's governor setting.", notUsed: true, column: 1),
			Rule.Check<Civilization>("City Governor", "No Small Wonders", c => c.governorNoSmallWonders, (c, v) => c.governorNoSmallWonders = v, "Civ3's governor setting.", notUsed: true, column: 1),
			Rule.Lines<Civilization>("Lists", "City Names (one a line):", c => c.cityNames, (c, v) => c.cityNames = v, "Its cities' names, in order.", column: 2),
			Rule.Lines<Civilization>("Lists", "Great Leaders:", c => c.greatLeaderNames, (c, v) => c.greatLeaderNames = v, "Its great leaders' names, given in order.", column: 2),
			Rule.Lines<Civilization>("Lists", "Scientific Leaders:", c => c.scientificLeaderNames, (c, v) => c.scientificLeaderNames = v,
				"Its scientific leaders' names, given in order.", column: 2),
			Rule.Number<Civilization>("Lists", "Diplomacy Text Index:", c => c.diplomacyTextIndex, (c, v) => c.diplomacyTextIndex = v, 0, 999,
				"Which civilization's lines of diplomacy.txt it speaks (0: its own place in the list).", column: 2),
			Flavors<Civilization>("Lists", c => c.name, f => f.Civilizations, column: 2),
		],
	};

	// The improvement's category: Civ3's improvement, small wonder or wonder.
	private static readonly string[] Categories = ["Improvement", "Small Wonder", "Wonder"];

	private static RuleField WonderGain(string label, Func<SaveBuilding.GreatWonderProperties, string> get, Action<SaveBuilding.GreatWonderProperties, string> set, string help) => new() {
		Group = "Wonder",
		Column = 1,
		Label = label,
		Kind = RuleFieldKind.Choice,
		AllowNone = true,
		Help = help + " (a wonder's).",
		Choices = BuildingNames,
		Refuse = (_, o) => ((SaveBuilding)o).greatWonderProperties == null ? "Only a wonder gains improvements in cities (Category: Wonder)." : null,
		Get = (_, o) => ((SaveBuilding)o).greatWonderProperties is { } w ? get(w) : null,
		Set = (_, o, v) => {
			SaveBuilding b = (SaveBuilding)o;
			if (b.greatWonderProperties is { } w) {
				set(w, (string)v);
			}
		},
	};

	// Civ3's Improvements and Wonders page.
	private static RulePage Improvements() => new() {
		Name = "Improvements and Wonders",
		ItemLabel = "Improvement:",
		Items = s => s.Buildings,
		NameOf = (_, o) => ((SaveBuilding)o).name,
		Rename = (s, o, name) => {
			SaveBuilding b = (SaveBuilding)o;
			string old = b.name;
			string why = Renamed<SaveBuilding>(s.Buildings, b, name, x => x.name, (x, n) => x.name = n);
			if (why == null) {
				RuleReferences.Building(s, old, b.name);
			}
			return why;
		},
		Add = (s, o) => Copy(s.Buildings, (SaveBuilding)o, x => x.name, (x, n) => x.name = n),
		Delete = (s, o) => {
			RuleReferences.Building(s, ((SaveBuilding)o).name, null);
			s.Buildings.Remove((SaveBuilding)o);
			return null;
		},
		UsedBy = (s, o) => RuleReferences.BuildingUses(s, ((SaveBuilding)o).name),
		Restore = (s, d) => s.Buildings = d.Buildings,
		Fields = [
			Rule.Text<SaveBuilding>("", "Civilopedia Entry:", b => b.civilopediaEntry, (b, v) => b.civilopediaEntry = v, "The improvement's Civilopedia key."),
			new RuleField {
				Label = "Category:", Kind = RuleFieldKind.Choice, Help = "An improvement, a small wonder (one per civilization) or a wonder (one in the world).",
				Choices = _ => [.. Categories],
				Get = (_, o) => ((SaveBuilding)o) is { greatWonderProperties: not null } ? "Wonder" : ((SaveBuilding)o).isSmallWonder ? "Small Wonder" : "Improvement",
				Set = (_, o, v) => {
					SaveBuilding b = (SaveBuilding)o;
					b.isSmallWonder = (string)v == "Small Wonder";
					b.greatWonderProperties = (string)v == "Wonder" ? b.greatWonderProperties ?? new SaveBuilding.GreatWonderProperties() : null;
				},
			},
			Rule.Number<SaveBuilding>("Costs", "Cost (shields):", b => b.shieldCost, (b, v) => b.shieldCost = v, 0, 99999, "Shields to build it."),
			Rule.Number<SaveBuilding>("Costs", "Population Cost:", b => b.populationCost, (b, v) => b.populationCost = v, 0, 99, "Citizens it costs."),
			Rule.Number<SaveBuilding>("Costs", "Upkeep (gold):", b => b.maintenanceCost, (b, v) => b.maintenanceCost = v, 0, 99, "Gold a turn to keep it."),
			Rule.Number<SaveBuilding>("Costs", "Pollution:", b => b.pollution, (b, v) => b.pollution = v, -99, 99, "Pollution it adds to its city."),
			Rule.Choice<SaveBuilding>("Requires", "Advance:", (s, b) => TechName(s, b.requiredTech), (s, b, v) => b.requiredTech = TechId(s, v), TechNames,
				"The advance needed to build it.", allowNone: true),
			Rule.Choice<SaveBuilding>("Requires", "Obsolete With:", (s, b) => TechName(s, b.renderedObsoleteBy), (s, b, v) => b.renderedObsoleteBy = TechId(s, v), TechNames,
				"The advance that makes it stop working.", allowNone: true),
			Rule.Choice<SaveBuilding>("Requires", "Improvement:", (_, b) => b.requiredBuilding, (_, b, v) => b.requiredBuilding = v, BuildingNames,
				"An improvement the city needs first.", allowNone: true),
			Rule.Number<SaveBuilding>("Requires", "How Many of It:", b => b.requiredBuildingCount, (b, v) => b.requiredBuildingCount = v, 0, 99,
				"How many of that improvement the civilization needs (0 or 1: the city's own)."),
			Rule.Choice<SaveBuilding>("Requires", "Government:", (s, b) => GovernmentName(s, b.requiredGovernment), (s, b, v) => b.requiredGovernment = GovernmentId(s, v),
				GovernmentNames, "The government needed to build it.", allowNone: true),
			Rule.Choices<SaveBuilding>("Requires", "Resources:", (s, b) => ResourceNamesOf(s, b.requiredResources),
				(s, b, v) => b.requiredResources = ResourceKeysOf(s, v).ToHashSet(), ResourceNames, "Resources needed to build it.", maxChoices: 2),
			Rule.Number<SaveBuilding>("Requires", "Armies:", b => b.numberOfArmiesRequired, (b, v) => b.numberOfArmiesRequired = v, 0, 99, "Armies the civilization needs."),
			Rule.Number<SaveBuilding>("Effects", "Culture:", b => b.culturePerTurn, (b, v) => b.culturePerTurn = v, 0, 99, "Culture a turn.", column: 1),
			Rule.Number<SaveBuilding>("Effects", "Content Faces (city):", b => b.contentFacesInCity, (b, v) => b.contentFacesInCity = v, -9, 99,
				"Citizens it makes content in its city (below 0: unhappy).", column: 1),
			Rule.Number<SaveBuilding>("Effects", "Content Faces (all cities):", b => b.contentFacesAllCities, (b, v) => b.contentFacesAllCities = v, -9, 99,
				"Citizens it makes content in every city.", column: 1),
			Rule.Choice<SaveBuilding>("Effects", "Doubles Happiness Of:", (_, b) => b.doublesHappinessOf, (_, b, v) => b.doublesHappinessOf = v, BuildingNames,
				"An improvement whose content faces it doubles.", allowNone: true, column: 1),
			Rule.Number<SaveBuilding>("Effects", "Production Bonus (%):", b => b.productionBonusPercent, (b, v) => b.productionBonusPercent = v, 0, 999,
				"More shields in its city.", column: 1),
			Rule.Number<SaveBuilding>("Effects", "Gold a Turn:", b => b.goldPerTurn, (b, v) => b.goldPerTurn = v, -99, 999, "Gold it brings each turn.", column: 1),
			Rule.Number<SaveBuilding>("Effects", "Gold per Coast Tile:", b => b.goldPerWorkedCoastTile, (b, v) => b.goldPerWorkedCoastTile = v, 0, 99,
				"Gold for each worked coast tile.", column: 1),
			Rule.Check<SaveBuilding>("Effects", "Doubles Building Gold", b => b.doublesBuildingGold, (b, v) => b.doublesBuildingGold = v,
				"Doubles the gold the city's improvements bring.", column: 1),
			Rule.Choice<SaveBuilding>("Effects", "Units Produced:", (_, b) => b.unitProduced, (_, b, v) => b.unitProduced = v, UnitNames,
				"A unit it gives its owner now and then.", allowNone: true, column: 1),
			Rule.Number<SaveBuilding>("Effects", "Every (turns):", b => b.unitFrequency, (b, v) => b.unitFrequency = v, 0, 999, "How often.", column: 1),
			WonderGain("Gains in Every City:", w => w.buildingGainedInEveryCity, (w, v) => w.buildingGainedInEveryCity = v, "An improvement every city gets"),
			WonderGain("Gains on Continent:", w => w.buildingGainedInEveryCityOnContinent, (w, v) => w.buildingGainedInEveryCityOnContinent = v,
				"An improvement every city on its continent gets"),
			new RuleField {
				Group = "Wonder", Column = 1, Label = "Spaceship Part (number):", Kind = RuleFieldKind.Number, Min = -1, Max = 99,
				Help = "Which spaceship part it is (-1: none).",
				Get = (_, o) => ((SaveBuilding)o).spaceshipPart ?? -1,
				Set = (_, o, v) => ((SaveBuilding)o).spaceshipPart = (int)v < 0 ? null : (int)v,
			},
			Rule.Number<SaveBuilding>("Defense", "Defense Bonus (%):", b => (int)Math.Round(b.combatDefenseBonus * 100), (b, v) => b.combatDefenseBonus = v / 100.0, 0, 999,
				"Units' defense in the city against land attacks.", column: 2),
			Rule.Number<SaveBuilding>("Defense", "Naval Defense Bonus (%):", b => (int)Math.Round(b.navalDefenseBonus * 100), (b, v) => b.navalDefenseBonus = v / 100.0, 0, 999,
				"Against attacks from the sea.", column: 2),
			Rule.Number<SaveBuilding>("Defense", "Bombard Defense:", b => b.bombardDefense, (b, v) => b.bombardDefense = v, 0, 99, "Its strength against bombardment.", column: 2),
			Rule.Number<SaveBuilding>("Defense", "Air Power:", b => b.airPower, (b, v) => b.airPower = v, 0, 99, "Its fire at aircraft.", column: 2),
			Rule.Number<SaveBuilding>("Defense", "Naval Power:", b => b.navalPower, (b, v) => b.navalPower = v, 0, 99, "Its fire at ships passing.", column: 2),
			Rule.Number<SaveBuilding>("Look", "Icon Row (city view):", b => b.iconRowIndex, (b, v) => b.iconRowIndex = v, 0, 999,
				"Its picture, by its row in the improvement icons.", column: 2),
			Rule.Flags<SaveBuilding, SaveBuilding.Flag>("Flags", "Flags and Effects:", b => b.flags, "What it does (Civ3's improvement flags and wonder effects).", column: 2),
			Rule.Flags<SaveBuilding, Civilization.Trait>("Flags", "Traits:", b => b.traits, "Civilizations with these traits build it cheaper.", column: 2),
			Flavors<SaveBuilding>("Flags", b => b.name, f => f.Buildings, column: 2),
		],
	};

	private static RuleField Art(string label, Func<Art, string> get, Func<Art, string, Art> set, string help) => new() {
		Group = "Art",
		Label = label,
		Kind = RuleFieldKind.Text,
		Help = help,
		Get = (_, o) => get(((SaveUnitPrototype)o).art) ?? "",
		Set = (_, o, v) => ((SaveUnitPrototype)o).art = set(((SaveUnitPrototype)o).art, (string)v),
	};

	private static List<string> TerrainKeysToNames(SaveGame s, IEnumerable<string> keys) => keys.Select(k => s.TerrainTypes.Find(t => t.Key == k)?.DisplayName ?? k).ToList();

	// Civ3's Units page.
	private static RulePage Units() => new() {
		Name = "Units",
		ItemLabel = "Unit:",
		Items = s => s.UnitPrototypes,
		NameOf = (_, o) => ((SaveUnitPrototype)o).name,
		Rename = (s, o, name) => {
			SaveUnitPrototype u = (SaveUnitPrototype)o;
			string old = u.name;
			string why = Renamed<SaveUnitPrototype>(s.UnitPrototypes, u, name, x => x.name, (x, n) => x.name = n);
			if (why == null) {
				RuleReferences.Unit(s, old, u.name);
			}
			return why;
		},
		Add = (s, o) => Copy(s.UnitPrototypes, (SaveUnitPrototype)o, x => x.name, (x, n) => x.name = n),
		Delete = (s, o) => {
			RuleReferences.Unit(s, ((SaveUnitPrototype)o).name, null);
			s.UnitPrototypes.Remove((SaveUnitPrototype)o);
			return null;
		},
		UsedBy = (s, o) => RuleReferences.UnitUses(s, ((SaveUnitPrototype)o).name),
		Restore = (s, d) => {
			s.UnitPrototypes = d.UnitPrototypes;
			HashSet<string> names = s.UnitPrototypes.Select(u => u.name).ToHashSet();
			s.Units.RemoveAll(u => !names.Contains(u.prototype));
		},
		Fields = [
			Rule.Text<SaveUnitPrototype>("", "Civilopedia Entry:", u => u.civilopediaEntry, (u, v) => u.civilopediaEntry = v, "The unit's Civilopedia key."),
			Rule.Choices<SaveUnitPrototype>("", "Class:", (_, u) => u.categories.ToList(), (_, u, v) => u.categories = v.ToHashSet(),
				_ => ["Land", "Sea", "Air"], "Land, sea or air.", maxChoices: 1),
			Rule.Choice<SaveUnitPrototype>("Requires", "Advance:", (s, u) => TechName(s, u.requiredTech), (s, u, v) => u.requiredTech = TechId(s, v), TechNames,
				"The advance needed to build it.", allowNone: true),
			Rule.Choices<SaveUnitPrototype>("Requires", "Resources:", (s, u) => ResourceNamesOf(s, u.requiredResources),
				(s, u, v) => u.requiredResources = ResourceKeysOf(s, v).ToHashSet(), ResourceNames, "Resources needed to build it.", maxChoices: 3),
			Rule.Choices<SaveUnitPrototype>("Requires", "Available To:", (_, u) => u.producibleBy.ToList(), (_, u, v) => u.producibleBy = v.ToHashSet(),
				CivNames, "The civilizations that can build it."),
			Rule.Check<SaveUnitPrototype>("Requires", "Can't Be Built", u => u.unproducible, (u, v) => u.unproducible = v,
				"No city builds it (it comes some other way)."),
			Rule.Number<SaveUnitPrototype>("Stats", "Cost (shields):", u => u.shieldCost, (u, v) => u.shieldCost = v, 0, 9999, "Shields to build it."),
			Rule.Number<SaveUnitPrototype>("Stats", "Population Cost:", u => u.populationCost, (u, v) => u.populationCost = v, 0, 99, "Citizens it costs."),
			Rule.Number<SaveUnitPrototype>("Stats", "Attack:", u => u.attack, (u, v) => u.attack = v, 0, 999, "Attack strength."),
			Rule.Number<SaveUnitPrototype>("Stats", "Defense:", u => u.defense, (u, v) => u.defense = v, 0, 999, "Defense strength."),
			Rule.Number<SaveUnitPrototype>("Stats", "Moves:", u => u.movement, (u, v) => u.movement = v, 0, 99, "Moves a turn."),
			Rule.Number<SaveUnitPrototype>("Stats", "Hit Point Bonus:", u => u.hpBonus, (u, v) => u.hpBonus = v, 0, 99, "Hit points above its experience's."),
			Rule.Number<SaveUnitPrototype>("Stats", "Bombard Strength:", u => u.bombard, (u, v) => u.bombard = v, 0, 999, "Its bombardment."),
			Rule.Number<SaveUnitPrototype>("Stats", "Bombard Range:", u => u.bombardRange, (u, v) => u.bombardRange = v, 0, 99, "How far it bombards."),
			Rule.Number<SaveUnitPrototype>("Stats", "Rate of Fire:", u => u.rateOfFire, (u, v) => u.rateOfFire = v, 0, 99, "Hits a bombardment can do."),
			Rule.Number<SaveUnitPrototype>("Stats", "Transport Capacity:", u => u.capacity, (u, v) => u.capacity = v, 0, 99, "Units it carries."),
			Rule.Number<SaveUnitPrototype>("Stats", "Operational Range:", u => u.operationalRange, (u, v) => u.operationalRange = v, 0, 99, "An aircraft's range."),
			Rule.Number<SaveUnitPrototype>("Stats", "Air Defense:", u => u.airDefense, (u, v) => u.airDefense = v, 0, 99, "Its fire at attacking aircraft."),
			Rule.Number<SaveUnitPrototype>("Stats", "Worker Strength (%):", u => (int)Math.Round(u.workerStrength * 100), (u, v) => u.workerStrength = v / 100f, 0, 999,
				"How much work it does as a worker (100: one worker's)."),
			Rule.Choices<SaveUnitPrototype>("Upgrades and Captives", "Upgrade To:", (_, u) => u.upgradesTo ?? [], (_, u, v) => u.upgradesTo = v, UnitNames,
				"What it upgrades to.", column: 1),
			Rule.Choice<SaveUnitPrototype>("Upgrades and Captives", "Enslaves To:", (_, u) => u.enslavesTo, (_, u, v) => u.enslavesTo = v, UnitNames,
				"What a unit it beats becomes.", allowNone: true, column: 1),
			Rule.Choices<SaveUnitPrototype>("Upgrades and Captives", "Stealth Attack Targets:", (_, u) => u.stealthTargets ?? [], (_, u, v) => u.stealthTargets = v,
				UnitNames, "What it picks out of a stack when it attacks.", column: 1),
			Rule.Choices<SaveUnitPrototype>("Upgrades and Captives", "Ignores Movement Cost Of:", (s, u) => TerrainKeysToNames(s, u.ignoreMovementCost),
				(s, u, v) => u.ignoreMovementCost = v.Select(n => s.TerrainTypes.Find(t => t.DisplayName == n)?.Key ?? n).ToHashSet(), TerrainNames,
				"Terrain it crosses as if it were flat.", column: 1),
			Art("Animation Folder:", a => a.mainArt.defaultName, (a, v) => { a.mainArt.defaultName = v; return a; }, "The folder of its animations."),
			new RuleField {
				Group = "Art", Label = "Icon:", Kind = RuleFieldKind.Number, Min = 0, Max = 999, Help = "Its small icon, by its place in the unit icons.",
				Get = (_, o) => ((SaveUnitPrototype)o).art.thumbnailArt.defaultIndex,
				Set = (_, o, v) => {
					SaveUnitPrototype u = (SaveUnitPrototype)o;
					Art art = u.art;
					art.thumbnailArt.defaultIndex = (int)v;
					u.art = art;
				},
			},
			Art("Civilopedia Picture (small):", a => a.pediaArt.small, (a, v) => { a.pediaArt.small = v; return a; }, "Its small Civilopedia picture."),
			Art("Civilopedia Picture (large):", a => a.pediaArt.large, (a, v) => { a.pediaArt.large = v; return a; }, "Its large Civilopedia picture."),
			Rule.Flags<SaveUnitPrototype, SaveUnitPrototype.Flag>("Abilities", "Abilities:", u => u.flags, "What it can do (Civ3's unit abilities).",
				f => !f.ToString().StartsWith("AI"), column: 1),
			Rule.Flags<SaveUnitPrototype, SaveUnitPrototype.Flag>("Abilities", "AI Strategies:", u => u.flags, "What the AI uses it for.",
				f => f.ToString().StartsWith("AI"), column: 1),
			Rule.Flags<SaveUnitPrototype, UnitAction>("Abilities", "Orders and Actions:", u => u.actions, "The orders it takes.", column: 1),
			new RuleField {
				Group = "Abilities", Column = 1, Label = "Worker Jobs:", Kind = RuleFieldKind.Choices, Help = "The worker jobs it can do.",
				Choices = s => s.TerraForms.Select(t => t.Name).ToList(),
				Get = (s, o) => s.TerraForms.Where(t => ((SaveUnitPrototype)o).terraformActions.Any(i => Same(i, t.Id))).Select(t => t.Name).ToList(),
				Set = (s, o, v) => ((SaveUnitPrototype)o).terraformActions = s.TerraForms.Where(t => ((List<string>)v).Contains(t.Name)).Select(t => t.Id).ToHashSet(),
			},
		],
	};

	// Civ3's Flavors page: the flavors and how they relate (%). Adding one
	// adds its column to the others.
	private static RulePage FlavorsPage() => new() {
		Name = "Flavors",
		ItemLabel = "Flavor:",
		Items = s => s.Rules.Flavors ??= [],
		NameOf = (_, o) => ((FlavorRule)o).Name,
		Rename = (s, o, name) => Renamed<FlavorRule>(s.Rules.Flavors, (FlavorRule)o, name, f => f.Name, (f, n) => f.Name = n),
		Add = (s, o) => {
			List<FlavorRule> list = s.Rules.Flavors ??= [];
			FlavorRule added = new() { Name = Rule.Unique("New Flavor", list.Select(f => f.Name)) };
			foreach (FlavorRule f in list) {
				f.Relationships.Add(0);
			}
			list.Add(added);
			added.Relationships = Enumerable.Repeat(0, list.Count).ToList();
			return added;
		},
		Delete = (s, o) => {
			List<FlavorRule> list = s.Rules.Flavors;
			int index = list.IndexOf((FlavorRule)o);
			list.RemoveAt(index);
			foreach (FlavorRule f in list.Where(f => index < f.Relationships.Count)) {
				f.Relationships.RemoveAt(index);
			}
			return null;
		},
		Restore = (s, d) => s.Rules.Flavors = d.Rules.Flavors,
		Fields = [
			new RuleField {
				Label = "Relationships (%):", Kind = RuleFieldKind.Numbers, Min = -100, Max = 100, NotUsed = true,
				Help = "How strongly the AI that has this flavor leans to each flavor.",
				NumberLabels = (s, _) => FlavorNames(s),
				Get = (s, o) => {
					List<int> r = ((FlavorRule)o).Relationships;
					return Enumerable.Range(0, FlavorNames(s).Count).Select(i => i < r.Count ? r[i] : 0).ToList();
				},
				Set = (_, o, v) => ((FlavorRule)o).Relationships = (List<int>)v,
			},
			Rule.Choices<FlavorRule>("", "Advances:", (_, f) => f.Techs, (_, f, v) => f.Techs = v, TechNames, "The advances with this flavor.", notUsed: true),
			Rule.Choices<FlavorRule>("", "Improvements:", (_, f) => f.Buildings, (_, f, v) => f.Buildings = v, BuildingNames, "The improvements with it.", notUsed: true),
			Rule.Choices<FlavorRule>("", "Civilizations:", (_, f) => f.Civilizations, (_, f, v) => f.Civilizations = v, CivNames, "The civilizations with it.", notUsed: true),
		],
	};
}
