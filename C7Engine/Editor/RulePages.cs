using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using C7GameData;
using C7GameData.Save;

namespace C7Engine.Editor;

// The rules pages, in Civ3's tab order. Phase 5's here: Citizens, Combat
// Experience, Culture, Difficulty Levels, Diplomats and Spies, Eras, General
// Settings, World Sizes, Worker Jobs; Phase 6's in RulePages_Big.cs. Ranges
// are Civ3's where its help gives one, otherwise roomy.
public static partial class RulePages {
	// Civ3's tabs, in its order.
	public static readonly List<RulePage> All = [
		Citizens(), Civilizations(), Advances(), Experience(), Culture(), Difficulties(), Espionage(), Eras(), FlavorsPage(),
		General(), Governments(), Improvements(), Resources(), Terrain(), Units(), WorkerJobs(), WorldSizes(),
	];

	public static RulePage Find(string name) => All.Find(p => p.Name == name);

	// Advances and resources by name, as the pages show them.
	internal static List<string> TechNames(SaveGame s) => s.Techs.Select(t => t.Name).ToList();
	internal static string TechName(SaveGame s, ID id) => id == null ? null : s.Techs.Find(t => t.id?.ToString() == id.ToString())?.Name;
	internal static ID TechId(SaveGame s, string name) => name == null ? null : s.Techs.Find(t => t.Name == name)?.id;
	internal static List<string> UnitNames(SaveGame s) => s.UnitPrototypes.Select(u => u.name).ToList();
	internal static List<string> ResourceNames(SaveGame s) => s.Resources.Where(r => !string.IsNullOrEmpty(r.Key)).Select(r => r.Name).ToList();

	private static string Renamed<T>(IList items, T item, string name, Func<T, string> nameOf, Action<T, string> set) {
		name = name?.Trim();
		if (string.IsNullOrEmpty(name)) {
			return "A name can't be empty.";
		}
		if (items.Cast<T>().Any(i => !ReferenceEquals(i, item) && nameOf(i) == name)) {
			return $"There's already one called {name}.";
		}
		set(item, name);
		return null;
	}

	// Civ3's Citizens page: the default citizen (the laborer) and the
	// specialists, their names, the advance a specialist needs and what each
	// adds to the city.
	private static RulePage Citizens() => new() {
		Name = "Citizens",
		ItemLabel = "Citizens:",
		Items = s => s.CitizenTypes,
		NameOf = (_, o) => ((CitizenType)o).SingularName,
		Rename = (s, o, name) => Renamed<CitizenType>(s.CitizenTypes, (CitizenType)o, name, c => c.SingularName, (c, n) => c.SingularName = n),
		Add = (s, o) => {
			CitizenType like = (CitizenType)o;
			string name = Rule.Unique("New Specialist", s.CitizenTypes.Select(c => c.SingularName));
			CitizenType added = new() {
				Id = ID.FromString(Rule.NextId("CitizenType", s.CitizenTypes.Select(c => c.Id?.ToString()))),
				IsDefaultCitizen = false,
				SpecialistIndex = s.CitizenTypes.Select(c => c.SpecialistIndex).DefaultIfEmpty(0).Max() + 1,
				SingularName = name,
				PluralName = name + "s",
				CivilopediaEntry = "CTZN_" + name.Replace(' ', '_'),
				PrerequisiteTech = like?.PrerequisiteTech,
				Luxuries = like?.Luxuries ?? 0,
				Research = like?.Research ?? 0,
				Taxes = like?.Taxes ?? 0,
				Corruption = like?.Corruption ?? 0,
				Construction = like?.Construction ?? 0,
			};
			s.CitizenTypes.Add(added);
			return added;
		},
		Delete = (s, o) => {
			CitizenType citizen = (CitizenType)o;
			if (citizen.IsDefaultCitizen) {
				return "The default citizen can't be deleted.";
			}
			// Its citizens in the scenario's cities become default citizens.
			ID laborer = s.CitizenTypes.Find(c => c.IsDefaultCitizen)?.Id;
			foreach (SaveCityResident resident in s.Cities.SelectMany(c => c.residents).Where(r => r.citizenType?.ToString() == citizen.Id?.ToString())) {
				resident.citizenType = laborer;
			}
			s.CitizenTypes.Remove(citizen);
			return null;
		},
		UsedBy = (s, o) => s.Cities.Where(c => c.residents.Any(r => r.citizenType?.ToString() == ((CitizenType)o).Id?.ToString()))
			.Select(c => $"citizens of {c.name}").ToList(),
		Restore = (s, d) => {
			s.CitizenTypes = d.CitizenTypes;
			HashSet<string> ids = s.CitizenTypes.Select(c => c.Id?.ToString()).ToHashSet();
			ID laborer = s.CitizenTypes.Find(c => c.IsDefaultCitizen)?.Id;
			foreach (SaveCityResident resident in s.Cities.SelectMany(c => c.residents).Where(r => r.citizenType != null && !ids.Contains(r.citizenType.ToString()))) {
				resident.citizenType = laborer;
			}
		},
		Problem = s => s.CitizenTypes.Count(c => c.IsDefaultCitizen) == 1 ? null : "Exactly one citizen must be the default citizen.",
		Fields = [
			Rule.Text<CitizenType>("", "Plural name:", c => c.PluralName, (c, v) => c.PluralName = v, "What several of them are called."),
			Rule.Text<CitizenType>("", "Civilopedia Entry:", c => c.CivilopediaEntry, (c, v) => c.CivilopediaEntry = v, "The citizen's Civilopedia key."),
			new RuleField {
				Label = "Default Citizen", Kind = RuleFieldKind.Check,
				Help = "The citizen who works the land (the laborer); every other one is a specialist. One must be the default.",
				Get = (_, o) => ((CitizenType)o).IsDefaultCitizen,
				// Only one is: ticking it unticks the old default.
				Set = (s, o, v) => {
					if ((bool)v) {
						s.CitizenTypes.ForEach(c => c.IsDefaultCitizen = false);
					}
					((CitizenType)o).IsDefaultCitizen = (bool)v;
				},
			},
			Rule.Choice<CitizenType>("", "Prerequisite:", (s, c) => TechName(s, c.PrerequisiteTech), (s, c, v) => c.PrerequisiteTech = TechId(s, v),
				TechNames, "The advance a civilization needs to make this specialist.", allowNone: true),
			Rule.Number<CitizenType>("Bonuses", "Luxuries:", c => c.Luxuries, (c, v) => c.Luxuries = v, 0, 99, "Luxuries the specialist adds to its city.", column: 1),
			Rule.Number<CitizenType>("Bonuses", "Research:", c => c.Research, (c, v) => c.Research = v, 0, 99, "Beakers the specialist adds.", column: 1),
			Rule.Number<CitizenType>("Bonuses", "Taxes:", c => c.Taxes, (c, v) => c.Taxes = v, 0, 99, "Gold the specialist adds.", column: 1),
			Rule.Number<CitizenType>("Bonuses", "Corruption:", c => c.Corruption, (c, v) => c.Corruption = v, 0, 99, "Corruption the specialist takes away.", column: 1),
			Rule.Number<CitizenType>("Bonuses", "Construction:", c => c.Construction, (c, v) => c.Construction = v, 0, 99, "Shields the specialist adds.", column: 1),
		],
	};

	// Civ3's Combat Experience page: each level's hit points and retreat
	// bonus (and the game's promotion chance, which Civ3's page hasn't).
	private static RulePage Experience() => new() {
		Name = "Combat Experience",
		ItemLabel = "Experience Level:",
		Items = s => s.ExperienceLevels,
		NameOf = (_, o) => ((ExperienceLevel)o).displayName,
		// The level is known by its key; the name is what players see.
		Rename = (s, o, name) => Renamed<ExperienceLevel>(s.ExperienceLevels, (ExperienceLevel)o, name, e => e.displayName, (e, n) => e.displayName = n),
		Restore = (s, d) => {
			s.ExperienceLevels = d.ExperienceLevels;
			s.DefaultExperienceLevel = d.DefaultExperienceLevel;
			HashSet<string> keys = s.ExperienceLevels.Select(e => e.key).ToHashSet();
			foreach (SaveUnit unit in s.Units.Where(u => u.experience != null && !keys.Contains(u.experience))) {
				unit.experience = s.DefaultExperienceLevel;
			}
		},
		Fields = [
			Rule.Number<ExperienceLevel>("", "Hit Points:", e => e.baseHitPoints, (e, v) => e.baseHitPoints = v, 1, 99, "A unit's hit points at this level."),
			Rule.Number<ExperienceLevel>("", "Retreat Bonus (%):", e => e.RetreatBonus(), (e, v) => e.retreatChance = v / 100.0, 0, 100,
				"The chance a fast unit at this level retreats from a lost battle."),
			Rule.Number<ExperienceLevel>("", "Promotion Chance (%):", e => (int)Math.Round(e.promotionChance * 100), (e, v) => e.promotionChance = v / 100.0, 0, 100,
				"The chance of reaching the next level after a battle won (the game's; Civ3's page hasn't it)."),
		],
	};

	// Civ3's Culture page: the cultural opinions, highest first, each
	// held of a civ with at least this ratio of our culture.
	private static RulePage Culture() => new() {
		Name = "Culture",
		ItemLabel = "Cultural Opinion:",
		Items = s => s.Rules.CultureOpinions,
		NameOf = (_, o) => ((CultureOpinion)o).Name,
		Rename = (s, o, name) => Renamed<CultureOpinion>(s.Rules.CultureOpinions, (CultureOpinion)o, name, c => c.Name, (c, n) => c.Name = n),
		Add = (s, o) => {
			CultureOpinion like = (CultureOpinion)o;
			CultureOpinion added = new() {
				Name = Rule.Unique("new opinion of", s.Rules.CultureOpinions.Select(c => c.Name)),
				Numerator = like?.Numerator ?? 1,
				Denominator = like?.Denominator ?? 1,
				PropagandaChance = like?.PropagandaChance ?? 0,
				InitialResistanceChance = like?.InitialResistanceChance ?? 0,
				ContinuedResistanceChance = like?.ContinuedResistanceChance ?? 0,
			};
			s.Rules.CultureOpinions.Insert(s.Rules.CultureOpinions.IndexOf(like) + 1, added);
			return added;
		},
		Delete = (s, o) => {
			if (s.Rules.CultureOpinions.Count <= 1) {
				return "There must be at least one opinion.";
			}
			s.Rules.CultureOpinions.Remove((CultureOpinion)o);
			return null;
		},
		Restore = (s, d) => s.Rules.CultureOpinions = d.Rules.CultureOpinions,
		Problem = s => {
			List<CultureOpinion> list = s.Rules.CultureOpinions;
			for (int i = 1; i < list.Count; ++i) {
				if ((double)list[i].Numerator / list[i].Denominator > (double)list[i - 1].Numerator / list[i - 1].Denominator) {
					return "The opinions go from the highest culture ratio to the lowest.";
				}
			}
			return null;
		},
		Fields = [
			Rule.Number<CultureOpinion>("Culture Ratio", "Their culture:", c => c.Numerator, (c, v) => c.Numerator = v, 1, 99,
				"With the next number: the opinion is held of a civ with at least this much culture for so much of ours (3 : 1 is three times ours)."),
			Rule.Number<CultureOpinion>("Culture Ratio", "Our culture:", c => c.Denominator, (c, v) => c.Denominator = v, 1, 99, "See above."),
			Rule.Number<CultureOpinion>("Chances (%)", "Successful Propaganda:", c => c.PropagandaChance, (c, v) => c.PropagandaChance = v, 0, 100,
				"The chance propaganda works on a city whose people hold this opinion of us.", notUsed: true, column: 1),
			Rule.Number<CultureOpinion>("Chances (%)", "Resistance:", c => c.InitialResistanceChance, (c, v) => c.InitialResistanceChance = v, 0, 100,
				"The chance a captured city's citizens resist.", notUsed: true, column: 1),
			Rule.Number<CultureOpinion>("Chances (%)", "Continued Resistance:", c => c.ContinuedResistanceChance, (c, v) => c.ContinuedResistanceChance = v, 0, 100,
				"The chance they go on resisting each turn.", notUsed: true, column: 1),
		],
	};

	// Civ3's Difficulty Levels page.
	private static RulePage Difficulties() => new() {
		Name = "Difficulty Levels",
		ItemLabel = "Difficulty Level:",
		Items = s => s.Difficulties,
		NameOf = (_, o) => ((Difficulty)o).Name,
		Rename = (s, o, name) => {
			Difficulty level = (Difficulty)o;
			string old = level.Name;
			string why = Renamed<Difficulty>(s.Difficulties, level, name, d => d.Name, (d, n) => d.Name = n);
			if (why == null) {
				// What names it: players' difficulties, the default level.
				foreach (PlayerSlot slot in (s.Scenario?.players ?? []).Where(p => p.difficulty == old)) {
					slot.difficulty = level.Name;
				}
				if (s.Rules.DefaultDifficulty == old) {
					s.Rules.DefaultDifficulty = level.Name;
				}
			}
			return why;
		},
		Add = (s, o) => {
			Difficulty like = (Difficulty)o;
			Difficulty added = new() {
				id = ID.FromString(Rule.NextId("Difficulty", s.Difficulties.Select(d => d.id?.ToString()))),
				Name = Rule.Unique("New Level", s.Difficulties.Select(d => d.Name)),
				NumberOfCitizensBornContent = like.NumberOfCitizensBornContent,
				MaxAiGovernmentTransitionTime = like.MaxAiGovernmentTransitionTime,
				NumberOfAIDefensiveStartingUnits = like.NumberOfAIDefensiveStartingUnits,
				NumberOfAIOffensiveStartingUnits = like.NumberOfAIOffensiveStartingUnits,
				ExtraStartUnit1 = like.ExtraStartUnit1,
				ExtraStartUnit2 = like.ExtraStartUnit2,
				AdditionalFreeUnitSupport = like.AdditionalFreeUnitSupport,
				UnitSupportBonusForEachSettlement = like.UnitSupportBonusForEachSettlement,
				AttackBonusAgainstBarbarians = like.AttackBonusAgainstBarbarians,
				AiCostFactor = like.AiCostFactor,
				HumanCostFactor = like.HumanCostFactor,
				PercentageOfOptimalCities = like.PercentageOfOptimalCities,
				AIToAITradeRate = like.AIToAITradeRate,
				CorruptionPercentage = like.CorruptionPercentage,
				MilitaryLaw = like.MilitaryLaw,
			};
			s.Difficulties.Insert(s.Difficulties.IndexOf(like) + 1, added);
			return added;
		},
		Delete = (s, o) => {
			Difficulty level = (Difficulty)o;
			if (s.Difficulties.Count <= 1) {
				return "There must be at least one difficulty level.";
			}
			foreach (PlayerSlot slot in (s.Scenario?.players ?? []).Where(p => p.difficulty == level.Name)) {
				slot.difficulty = null;
			}
			if (s.Rules.DefaultDifficulty == level.Name) {
				s.Rules.DefaultDifficulty = null;
			}
			s.Difficulties.Remove(level);
			return null;
		},
		UsedBy = (s, o) => {
			string name = ((Difficulty)o).Name;
			List<string> uses = (s.Scenario?.players ?? []).Where(p => p.difficulty == name)
				.Select(p => $"the difficulty of {s.Players.Find(x => x.id?.ToString() == p.player)?.civilization ?? p.player}").ToList();
			if (s.Rules.DefaultDifficulty == name) {
				uses.Add("the default difficulty level");
			}
			return uses;
		},
		Restore = (s, d) => {
			s.Difficulties = d.Difficulties;
			HashSet<string> names = s.Difficulties.Select(x => x.Name).ToHashSet();
			foreach (PlayerSlot slot in (s.Scenario?.players ?? []).Where(p => p.difficulty != null && !names.Contains(p.difficulty))) {
				slot.difficulty = null;
			}
		},
		Fields = [
			Rule.Number<Difficulty>("", "Citizens Born Content:", d => d.NumberOfCitizensBornContent, (d, v) => d.NumberOfCitizensBornContent = v, 0, 99,
				"How many citizens of a city are content before any happiness: the rest are born unhappy."),
			Rule.Number<Difficulty>("Additional AI Starting Units", "Defensive Land Units:", d => d.NumberOfAIDefensiveStartingUnits, (d, v) => d.NumberOfAIDefensiveStartingUnits = v, 0, 99,
				"Extra defenders each computer player starts with."),
			Rule.Number<Difficulty>("Additional AI Starting Units", "Offensive Land Units:", d => d.NumberOfAIOffensiveStartingUnits, (d, v) => d.NumberOfAIOffensiveStartingUnits = v, 0, 99,
				"Extra attackers each computer player starts with."),
			Rule.Number<Difficulty>("Additional AI Starting Units", "Start Unit Type 1:", d => d.ExtraStartUnit1, (d, v) => d.ExtraStartUnit1 = v, 0, 99,
				"Extra units of start unit 1 (General Settings; a Settler) for each computer player."),
			Rule.Number<Difficulty>("Additional AI Starting Units", "Start Unit Type 2:", d => d.ExtraStartUnit2, (d, v) => d.ExtraStartUnit2 = v, 0, 99,
				"Extra units of start unit 2 (a Worker) for each computer player."),
			Rule.Number<Difficulty>("AI Unit Support Bonuses", "Additional Free Support:", d => d.AdditionalFreeUnitSupport, (d, v) => d.AdditionalFreeUnitSupport = v, 0, 99,
				"Units a computer player supports for free, beyond its government's.", column: 1),
			Rule.Number<Difficulty>("AI Unit Support Bonuses", "Bonus for Each City:", d => d.UnitSupportBonusForEachSettlement, (d, v) => d.UnitSupportBonusForEachSettlement = v, 0, 99,
				"More free units for each city a computer player has.", column: 1),
			Rule.Number<Difficulty>("AI Bonuses", "Max Government Transition Time:", d => d.MaxAiGovernmentTransitionTime, (d, v) => d.MaxAiGovernmentTransitionTime = v, 0, 99,
				"The longest a computer player's anarchy lasts (0: as the rules say).", column: 1),
			Rule.Number<Difficulty>("AI Bonuses", "Cost Factor:", d => d.AiCostFactor, (d, v) => d.AiCostFactor = v, 1, 99,
				"What a computer player pays for what it builds and grows, in tenths: 10 is the human's cost.", column: 1),
			Rule.Number<Difficulty>("AI Bonuses", "AI to AI Trade Rate (%):", d => d.AIToAITradeRate, (d, v) => d.AIToAITradeRate = v, 0, 999,
				"How much better computer players trade with each other than with the human.", column: 1),
			Rule.Number<Difficulty>("", "Attack Bonus Against Barbarians (%):", d => d.AttackBonusAgainstBarbarians, (d, v) => d.AttackBonusAgainstBarbarians = v, -100, 9999,
				"The human's bonus attacking barbarians."),
			Rule.Number<Difficulty>("", "Percentage of Optimal Cities:", d => d.PercentageOfOptimalCities, (d, v) => d.PercentageOfOptimalCities = v, 0, 999,
				"The share of the world size's optimal number of cities a civilization may have before corruption grows."),
			Rule.Number<Difficulty>("", "Corruption Modifier (%):", d => d.CorruptionPercentage, (d, v) => d.CorruptionPercentage = v, 0, 999,
				"Civ3's corruption setting for the level.", notUsed: true),
			Rule.Number<Difficulty>("", "Citizens Quelled by Military:", d => d.MilitaryLaw, (d, v) => d.MilitaryLaw = v, 0, 99,
				"How many resisting citizens each military unit in a captured city calms a turn."),
		],
	};

	// Civ3's Diplomats and Spies page: each mission's name, who performs it,
	// its description and base cost.
	private static RulePage Espionage() => new() {
		Name = "Diplomats and Spies",
		ItemLabel = "Mission:",
		Items = s => s.Rules.EspionageMissions,
		NameOf = (_, o) => ((EspionageMissionRule)o).Name,
		Rename = (s, o, name) => Renamed<EspionageMissionRule>(s.Rules.EspionageMissions, (EspionageMissionRule)o, name, m => m.Name, (m, n) => m.Name = n),
		Restore = (s, d) => s.Rules.EspionageMissions = d.Rules.EspionageMissions,
		Fields = [
			Rule.Check<EspionageMissionRule>("Performed By", "Diplomat", m => m.Diplomat, (m, v) => m.Diplomat = v,
				"An embassy may carry it out (the game's own rules say which missions may).", notUsed: true),
			Rule.Check<EspionageMissionRule>("Performed By", "Spy", m => m.Spy, (m, v) => m.Spy = v,
				"A planted spy may carry it out.", notUsed: true),
			Rule.Text<EspionageMissionRule>("", "Civilopedia Entry:", m => m.CivilopediaEntry, (m, v) => m.CivilopediaEntry = v, "The mission's Civilopedia key."),
			Rule.Text<EspionageMissionRule>("", "Description:", m => m.Description, (m, v) => m.Description = v, "The mission's description.", notUsed: true),
			Rule.Number<EspionageMissionRule>("", "Base Cost:", m => m.BaseCost, (m, v) => m.BaseCost = v, 0, 9999,
				"The mission's cost in gold before distance and care are added."),
		],
	};

	private static readonly string[] EraKeys = [EraUtils.ANCIENT_TIMES_CVLPD, EraUtils.MIDDLE_AGES_CVLPD, EraUtils.INDUSTRIAL_AGE_CVLPD, EraUtils.MODERN_ERA_CVLPD];
	private static readonly string[] StandardEraNames = [EraUtils.ANCIENT_TIMES, EraUtils.MIDDLE_AGES, EraUtils.INDUSTRIAL_AGE, EraUtils.MODERN_ERA];

	public static string EraName(SaveGame s, string key) => s.Rules.EraNames.TryGetValue(key, out string name) && !string.IsNullOrWhiteSpace(name)
		? name : StandardEraNames[Array.IndexOf(EraKeys, key)];

	// Civ3's Eras page: the four eras' names and their researchers' names
	// (the scientific leaders a civ gets in the era).
	private static RulePage Eras() => new() {
		Name = "Eras",
		ItemLabel = "Era:",
		Items = _ => EraKeys,
		NameOf = (s, o) => EraName(s, (string)o),
		Rename = (s, o, name) => {
			name = name?.Trim();
			if (string.IsNullOrEmpty(name)) {
				return "A name can't be empty.";
			}
			if (EraKeys.Any(k => k != (string)o && EraName(s, k) == name)) {
				return $"There's already one called {name}.";
			}
			s.Rules.EraNames[(string)o] = name;
			return null;
		},
		Restore = (s, d) => {
			s.Rules.EraNames = d.Rules.EraNames;
			s.Rules.EraResearcherNames = d.Rules.EraResearcherNames;
		},
		Fields = [
			new RuleField {
				Label = "Civilopedia Entry:", Kind = RuleFieldKind.Text, ReadOnly = true, Help = "The era's key (the game's four eras are fixed).",
				Get = (_, o) => (string)o, Set = (_, _, _) => { },
			},
			new RuleField {
				Label = "Researchers (one a line):", Kind = RuleFieldKind.Lines, Help = "The names of the scientific leaders civilizations get in this era.",
				Get = (s, o) => s.Rules.EraResearcherNames.TryGetValue((string)o, out List<string> names) ? names : [],
				Set = (s, o, v) => s.Rules.EraResearcherNames[(string)o] = (List<string>)v,
			},
		],
	};

	private static int Bonus(SaveGame s, string description) {
		int i = s.StrengthBonuses.FindIndex(b => b.description == description);
		return i < 0 ? 0 : (int)Math.Round(s.StrengthBonuses[i].amount * 100);
	}

	private static void SetBonus(SaveGame s, string description, int percent) {
		int i = s.StrengthBonuses.FindIndex(b => b.description == description);
		StrengthBonus bonus = new() { description = description, amount = percent / 100.0 };
		if (i < 0) {
			s.StrengthBonuses.Add(bonus);
		} else {
			s.StrengthBonuses[i] = bonus;
		}
	}

	private static RuleField Unit(string label, Func<SaveGame, string> get, Action<SaveGame, string> set, string help, bool notUsed = false) =>
		Rule.Choice<SaveGame>("Default Units", label, (s, _) => get(s), (s, _, v) => set(s, v), UnitNames, help, allowNone: true, notUsed: notUsed, column: 0);

	// Civ3's General Settings page, one page of the game-wide rules, in
	// Civ3's boxes (in two columns, not Civ3's three: the editor's type is
	// bigger than Civ3's; the default units go under the first).
	private static RulePage General() => new() {
		Name = "General Settings",
		Items = s => new[] { s },
		NameOf = (_, _) => "General Settings",
		// The page's own fields only: the scenario's switches (Scenario
		// Properties) stay.
		Restore = (s, d) => {
			foreach (RuleField f in General().Fields) {
				f.Set(s, s, f.Get(d, d));
			}
		},
		Problem = s => s.Rules.MaximumLevel1CitySize >= s.Rules.MaximumLevel2CitySize ? "Level 2 cities must be bigger than level 1's."
			: s.Rules.MinimumResearchTime > s.Rules.MaximumResearchTime ? "The minimum research time can't be over the maximum." : null,
		Fields = [
			Rule.Number<SaveGame>("Road Movement", "Movement Rate Along Roads:", s => s.Rules.MovementAlongRoads, (s, v) => s.Rules.MovementAlongRoads = v, 1, 99,
				"How many road tiles a unit goes for one move."),
			Rule.Number<SaveGame>("Food and Gold", "Upgrade Cost:", s => s.Rules.UpgradeCostPerShield, (s, v) => s.Rules.UpgradeCostPerShield = v, 0, 99,
				"Gold per shield of difference to upgrade a unit."),
			Rule.Number<SaveGame>("Food and Gold", "Food Consumption/Citizen:", s => s.Rules.FoodConsumptionPerCitizen, (s, v) => s.Rules.FoodConsumptionPerCitizen = v, 0, 99,
				"Food each citizen eats a turn."),
			Rule.Number<SaveGame>("Food and Gold", "Starting Treasury:", s => s.Rules.StartingTreasury, (s, v) => s.Rules.StartingTreasury = v, 0, 99999,
				"Gold each civilization starts a new game with."),
			Rule.Number<SaveGame>("Wealth", "Shield Cost Per Gold:", s => s.Rules.ShieldCostPerGold, (s, v) => s.Rules.ShieldCostPerGold = v, 1, 99,
				"Shields building Wealth takes for each gold.", notUsed: true),
			Rule.Number<SaveGame>("Hurry Production", "Shield Value in Gold:", s => s.Rules.ShieldValueInGold, (s, v) => s.Rules.ShieldValueInGold = v, 0, 99,
				"Gold to hurry a shield."),
			Rule.Number<SaveGame>("Hurry Production", "Citizen Value in Shields:", s => s.Rules.CitizenValueInShields, (s, v) => s.Rules.CitizenValueInShields = v, 0, 999,
				"Shields a citizen sacrificed to hurry is worth."),
			Rule.Number<SaveGame>("Citizen Mood", "Chance of Rioting (%):", s => s.Rules.ChanceOfRioting, (s, v) => s.Rules.ChanceOfRioting = v, 0, 100,
				"The chance a city in disorder riots."),
			Rule.Number<SaveGame>("Citizen Mood", "Minimum Population for We Love the King:", s => s.Rules.MinimumPopulationForWeLoveTheKing,
				(s, v) => s.Rules.MinimumPopulationForWeLoveTheKing = v, 0, 99, "The smallest city that can celebrate."),
			Rule.Number<SaveGame>("Citizen Mood", "Citizens Affected by Each Happy Face:", s => s.Rules.CitizensAffectedByEachHappyFace,
				(s, v) => s.Rules.CitizensAffectedByEachHappyFace = v, 1, 99, "How many citizens one happy face moves up a mood."),
			Rule.Number<SaveGame>("Citizen Mood", "Turn Penalty for Each Hurry Sacrifice:", s => s.Rules.TurnPenaltyForEachHurrySacrifice,
				(s, v) => s.Rules.TurnPenaltyForEachHurrySacrifice = v, 0, 99, "Turns a citizen sacrificed to hurry is remembered with unhappiness."),
			Rule.Number<SaveGame>("Citizen Mood", "Turn Penalty for Each Drafted Citizen:", s => s.Rules.TurnPenaltyForEachDraftedCitizen,
				(s, v) => s.Rules.TurnPenaltyForEachDraftedCitizen = v, 0, 99, "Turns a drafted citizen is remembered with unhappiness."),
			Rule.Number<SaveGame>("Technology", "Future Tech Cost:", s => s.Rules.FutureTechCost, (s, v) => s.Rules.FutureTechCost = v, 0, 99999,
				"What a future technology costs."),
			Rule.Number<SaveGame>("Technology", "Minimum Research Time (turns):", s => s.Rules.MinimumResearchTime, (s, v) => s.Rules.MinimumResearchTime = v, 1, 999,
				"The fewest turns an advance takes."),
			Rule.Number<SaveGame>("Technology", "Maximum Research Time (turns):", s => s.Rules.MaximumResearchTime, (s, v) => s.Rules.MaximumResearchTime = v, 1, 999,
				"The most turns an advance takes."),
			Rule.Number<SaveGame>("Golden Age", "Duration (turns):", s => s.Rules.GoldenAgeDuration, (s, v) => s.Rules.GoldenAgeDuration = v, 1, 999,
				"How long a golden age lasts."),

			Rule.Number<SaveGame>("Various Unit Abilities", "Forest Value in Shields:", s => s.Rules.ForestValueInShields, (s, v) => s.Rules.ForestValueInShields = v, 0, 999,
				"Shields a city gets when a forest near it is cleared.", column: 1),
			Rule.Number<SaveGame>("Various Unit Abilities", "Cities Needed to Support an Army:", s => s.Rules.CitiesNeededToSupportAnArmy,
				(s, v) => s.Rules.CitiesNeededToSupportAnArmy = v, 1, 99, "Cities a civilization needs for each army.", column: 1),
			Rule.Number<SaveGame>("Various Unit Abilities", "Chance to Intercept Air Missions (%):", s => s.Rules.ChanceToInterceptAirMissions,
				(s, v) => s.Rules.ChanceToInterceptAirMissions = v, 0, 100, "A fighter's chance to intercept a plane.", column: 1),
			Rule.Number<SaveGame>("Various Unit Abilities", "Chance to Intercept Stealth Missions (%):", s => s.Rules.ChanceToInterceptStealthMissions,
				(s, v) => s.Rules.ChanceToInterceptStealthMissions = v, 0, 100, "A fighter's chance to intercept a stealth plane.", column: 1),
			Rule.Lines<SaveGame>("City Size Levels", "Labels (level 1, 2, 3):", s => s.Rules.CityLevelNames, (s, v) => s.Rules.CityLevelNames = v,
				"What towns, cities and metropolises are called.", notUsed: true, column: 1),
			Rule.Number<SaveGame>("City Size Levels", "Level 1 Maximum Size:", s => s.Rules.MaximumLevel1CitySize, (s, v) => s.Rules.MaximumLevel1CitySize = v, 1, 99,
				"The largest a town gets.", column: 1),
			Rule.Number<SaveGame>("City Size Levels", "Level 2 Maximum Size:", s => s.Rules.MaximumLevel2CitySize, (s, v) => s.Rules.MaximumLevel2CitySize = v, 1, 99,
				"The largest a city gets before it's a metropolis.", column: 1),
			Rule.Lines<SaveGame>("Culture", "Cultural Levels (lowest first):", s => s.Rules.CultureLevelNames, (s, v) => s.Rules.CultureLevelNames = v,
				"The names of a civilization's culture levels.", column: 1),
			Rule.Number<SaveGame>("Culture", "Level Multiplier:", s => s.Rules.BorderExpansionMultiplier, (s, v) => s.Rules.BorderExpansionMultiplier = v, 1, 99999,
				"Culture between levels.", notUsed: true, column: 1),
			Rule.Number<SaveGame>("Culture", "Border Factor:", s => s.Rules.BorderFactor, (s, v) => s.Rules.BorderFactor = v, 1, 99,
				"How fast borders grow with culture.", column: 1),
			Rule.Number<SaveGame>("Defensive Bonuses (%)", "Town:", s => Bonus(s, "Town"), (s, v) => SetBonus(s, "Town", v), 0, 999, "A town's defense bonus.", column: 1),
			Rule.Number<SaveGame>("Defensive Bonuses (%)", "City:", s => Bonus(s, "City"), (s, v) => SetBonus(s, "City", v), 0, 999, "A city's defense bonus.", column: 1),
			Rule.Number<SaveGame>("Defensive Bonuses (%)", "Metropolis:", s => Bonus(s, "Metropolis"), (s, v) => SetBonus(s, "Metropolis", v), 0, 999,
				"A metropolis's defense bonus.", column: 1),
			Rule.Number<SaveGame>("Defensive Bonuses (%)", "Fortress:", s => s.Rules.FortressDefensiveBonus, (s, v) => s.Rules.FortressDefensiveBonus = v, 0, 999,
				"A fortress's defense bonus.", column: 1),
			Rule.Number<SaveGame>("Defensive Bonuses (%)", "River:", s => Bonus(s, "Behind river"), (s, v) => SetBonus(s, "Behind river", v), 0, 999,
				"The bonus defending behind a river.", column: 1),
			Rule.Number<SaveGame>("Defensive Bonuses (%)", "Fortifications:", s => Bonus(s, "Fortified"), (s, v) => SetBonus(s, "Fortified", v), 0, 999,
				"A fortified unit's bonus.", column: 1),
			Rule.Number<SaveGame>("Defensive Bonuses (%)", "Citizen:", s => s.Rules.CitizenDefensiveBonus, (s, v) => s.Rules.CitizenDefensiveBonus = v, 0, 999,
				"A citizen's strength defending an empty city.", column: 1),
			Rule.Number<SaveGame>("Defensive Bonuses (%)", "Building:", s => s.Rules.BuildingDefensiveBonus, (s, v) => s.Rules.BuildingDefensiveBonus = v, 0, 999,
				"A defensive building's bonus.", column: 1),
			Rule.Numbers<SaveGame>("Spaceship Parts", "Parts needed of each kind:", s => s.Rules.SpaceshipPartsRequired, (s, v) => s.Rules.SpaceshipPartsRequired = v, 0, 99,
				"How many of each spaceship part a spaceship needs.", column: 1),

			Unit("Captured Unit:", s => s.Rules.SlaveUnitType, (s, v) => s.Rules.SlaveUnitType = v, "What an enslaved or captured worker becomes."),
			Unit("Start Unit 1:", s => s.Rules.StartUnitType1, (s, v) => s.Rules.StartUnitType1 = v, "The first unit each civilization starts with."),
			Unit("Start Unit 2:", s => s.Rules.StartUnitType2, (s, v) => s.Rules.StartUnitType2 = v, "The second unit each civilization starts with."),
			Unit("Scout:", s => s.Rules.ScoutUnitType, (s, v) => s.Rules.ScoutUnitType = v, "The scout an expansionist civilization starts with."),
			Unit("Battle-Created Unit:", s => s.Rules.BattleCreatedUnit, (s, v) => s.Rules.BattleCreatedUnit = v, "The leader a victory may bring."),
			Unit("Build-Army Unit:", s => s.Rules.BuildArmyUnit, (s, v) => s.Rules.BuildArmyUnit = v, "The army a leader builds."),
			Unit("Basic Barbarian:", s => s.BarbarianInfo.basicBarbarianUnit, (s, v) => s.BarbarianInfo.basicBarbarianUnit = v, "The barbarians' usual unit."),
			Unit("Advanced Barbarian:", s => s.BarbarianInfo.advancedBarbarianUnit, (s, v) => s.BarbarianInfo.advancedBarbarianUnit = v, "The barbarians' stronger unit."),
			Unit("Barbarian Sea Unit:", s => s.BarbarianInfo.barbarianSeaUnit, (s, v) => s.BarbarianInfo.barbarianSeaUnit = v, "The barbarians' ship."),
			Unit("Flag Unit:", s => s.Rules.FlagUnitType, (s, v) => s.Rules.FlagUnitType = v, "The unit captured in capture-the-flag games.", notUsed: true),
			Rule.Choice<SaveGame>("Artificial Intelligence", "Default Difficulty Level:", (s, _) => s.Rules.DefaultDifficulty, (s, _, v) => s.Rules.DefaultDifficulty = v,
				s => s.Difficulties.Select(d => d.Name).ToList(), "The level computer players play at when a scenario doesn't set one.", allowNone: true, notUsed: true, column: 0),
			Rule.Choice<SaveGame>("Resources", "Default Money Resource:", (s, _) => s.Rules.DefaultMoneyResource, (s, _, v) => s.Rules.DefaultMoneyResource = v,
				ResourceNames, "The resource standing for money in trades.", allowNone: true, notUsed: true, column: 0),
		],
	};

	// Civ3's World Sizes page.
	private static RulePage WorldSizes() => new() {
		Name = "World Sizes",
		ItemLabel = "World Size:",
		Items = s => s.WorldSizes,
		NameOf = (_, o) => ((WorldSize)o).name,
		Rename = (s, o, name) => Renamed<WorldSize>(s.WorldSizes, (WorldSize)o, name, w => w.name, (w, n) => w.name = n),
		Restore = (s, d) => s.WorldSizes = d.WorldSizes,
		Fields = [
			Rule.Number<WorldSize>("", "Width:", w => w.width, (w, v) => w.width = v, 10, 362, "The map's width in tiles."),
			Rule.Number<WorldSize>("", "Height:", w => w.height, (w, v) => w.height = v, 10, 362, "The map's height in tiles."),
			Rule.Number<WorldSize>("", "Number of Civs:", w => w.numberOfCivs, (w, v) => w.numberOfCivs = v, 1, 31, "How many civilizations a game of this size has."),
			Rule.Number<WorldSize>("", "Distance Between Civs:", w => w.distanceBetweenCivs, (w, v) => w.distanceBetweenCivs = v, 1, 99,
				"How far apart civilizations start."),
			Rule.Number<WorldSize>("", "Optimal Number of Cities:", w => w.optimalNumberOfCities, (w, v) => w.optimalNumberOfCities = v, 1, 999,
				"The cities a civilization may have before corruption grows (with the difficulty's percentage)."),
			Rule.Number<WorldSize>("", "Tech Rate:", w => w.techRate, (w, v) => w.techRate = v, 1, 9999, "How costly advances are on a world this size."),
		],
	};

	// Civ3's Worker Jobs page.
	private static RulePage WorkerJobs() => new() {
		Name = "Worker Jobs",
		ItemLabel = "Worker Job:",
		Items = s => s.TerraForms,
		NameOf = (_, o) => ((SaveTerraform)o).Name,
		Rename = (s, o, name) => Renamed<SaveTerraform>(s.TerraForms, (SaveTerraform)o, name, t => t.Name, (t, n) => t.Name = n),
		Restore = (s, d) => s.TerraForms = d.TerraForms,
		Fields = [
			Rule.Text<SaveTerraform>("", "Civilopedia Entry:", t => t.CivilopediaEntry, (t, v) => t.CivilopediaEntry = v, "The job's Civilopedia key."),
			Rule.Choice<SaveTerraform>("", "Required Advance:", (s, t) => TechName(s, t.RequiredTech), (s, t, v) => t.RequiredTech = TechId(s, v),
				TechNames, "The advance a civilization needs for its workers to do this.", allowNone: true),
			Rule.Choices<SaveTerraform>("", "Required Resources:",
				(s, t) => t.RequiredResources.Select(k => s.Resources.Find(r => r.Key == k)?.Name ?? k).ToList(),
				(s, t, v) => t.RequiredResources = v.Select(n => s.Resources.Find(r => r.Name == n)?.Key ?? n).ToList(),
				ResourceNames, "Resources a civilization needs for the job (as railroads need iron and coal).", maxChoices: 2),
			Rule.Number<SaveTerraform>("", "Turns to Complete:", t => t.TurnsToComplete, (t, v) => t.TurnsToComplete = v, 0, 99,
				"Worker-turns the job takes, before the terrain's own factor."),
		],
	};
}
