using System;
using System.Collections.Generic;
using System.Linq;
using C7GameData;
using C7GameData.Save;

namespace C7Engine.Editor;

// What in a scenario refers to a rule item by name or ID (unit types,
// improvements, civilizations, governments, advances, resources), so the
// rules pages can rename an item everywhere, list its uses before a
// Delete, and clear them when it goes.
public static class RuleReferences {
	private static void Replace(List<string> list, string old, string name) {
		if (list == null) {
			return;
		}
		for (int i = 0; i < list.Count; ++i) {
			if (list[i] == old) {
				list[i] = name;
			}
		}
		if (name == null) {
			list.RemoveAll(x => x == null);
		}
	}

	private static void Replace(ISet<string> set, string old, string name) {
		if (set != null && set.Remove(old) && name != null) {
			set.Add(name);
		}
	}

	private static IEnumerable<FlavorRule> Flavors(SaveGame s) => s.Rules.Flavors ?? [];

	// A unit type renamed (name) or gone (null).
	public static void Unit(SaveGame s, string old, string name) {
		foreach (SaveUnitPrototype p in s.UnitPrototypes) {
			Replace(p.upgradesTo, old, name);
			Replace(p.stealthTargets, old, name);
			if (p.enslavesTo == old) {
				p.enslavesTo = name;
			}
		}
		Rules r = s.Rules;
		if (r.StartUnitType1 == old) { r.StartUnitType1 = name; }
		if (r.StartUnitType2 == old) { r.StartUnitType2 = name; }
		if (r.ScoutUnitType == old) { r.ScoutUnitType = name; }
		if (r.BattleCreatedUnit == old) { r.BattleCreatedUnit = name; }
		if (r.BuildArmyUnit == old) { r.BuildArmyUnit = name; }
		if (r.SlaveUnitType == old) { r.SlaveUnitType = name; }
		if (r.FlagUnitType == old) { r.FlagUnitType = name; }
		BarbarianInfo b = s.BarbarianInfo;
		if (b.basicBarbarianUnit == old) { b.basicBarbarianUnit = name; }
		if (b.advancedBarbarianUnit == old) { b.advancedBarbarianUnit = name; }
		if (b.barbarianSeaUnit == old) { b.barbarianSeaUnit = name; }
		foreach (SaveBuilding building in s.Buildings.Where(x => x.unitProduced == old)) {
			building.unitProduced = name;
		}
		foreach (Civilization civ in s.Civilizations.Where(c => c.kingUnit == old)) {
			civ.kingUnit = name;
		}
		foreach (PlayerSlot slot in s.Scenario?.players ?? []) {
			slot.startUnits.RemoveAll(u => u.type == old && name == null);
			slot.startUnits.Where(u => u.type == old).ToList().ForEach(u => u.type = name);
		}
		foreach (SaveCity city in s.Cities.Where(c => c.producible == old)) {
			city.producible = name;
		}
		if (name == null) {
			s.Units.RemoveAll(u => u.prototype == old);
		} else {
			foreach (SaveUnit unit in s.Units.Where(u => u.prototype == old)) {
				unit.prototype = name;
				if (unit.name == old) {
					unit.name = name;
				}
			}
		}
	}

	public static List<string> UnitUses(SaveGame s, string type) {
		List<string> uses = [];
		int units = s.Units.Count(u => u.prototype == type);
		if (units > 0) {
			uses.Add($"{units} unit{(units == 1 ? "" : "s")} on the map (deleted with it)");
		}
		uses.AddRange(s.UnitPrototypes.Where(p => p.upgradesTo?.Contains(type) == true || p.enslavesTo == type || p.stealthTargets?.Contains(type) == true)
			.Select(p => $"the {p.name} unit"));
		uses.AddRange(s.Buildings.Where(b => b.unitProduced == type).Select(b => b.name));
		uses.AddRange(s.Civilizations.Where(c => c.kingUnit == type).Select(c => $"{c.name}'s king"));
		Rules r = s.Rules;
		if (new[] { r.StartUnitType1, r.StartUnitType2, r.ScoutUnitType, r.BattleCreatedUnit, r.BuildArmyUnit, r.SlaveUnitType, r.FlagUnitType,
				s.BarbarianInfo.basicBarbarianUnit, s.BarbarianInfo.advancedBarbarianUnit, s.BarbarianInfo.barbarianSeaUnit }.Contains(type)) {
			uses.Add("General Settings' default units");
		}
		return uses;
	}

	// An improvement renamed or gone.
	public static void Building(SaveGame s, string old, string name) {
		foreach (SaveBuilding b in s.Buildings) {
			if (b.requiredBuilding == old) { b.requiredBuilding = name; }
			if (b.doublesHappinessOf == old) { b.doublesHappinessOf = name; }
			if (b.greatWonderProperties is SaveBuilding.GreatWonderProperties w) {
				if (w.buildingGainedInEveryCity == old) { w.buildingGainedInEveryCity = name; }
				if (w.buildingGainedInEveryCityOnContinent == old) { w.buildingGainedInEveryCityOnContinent = name; }
			}
		}
		Replace(s.Rules.BuildingsKeptOnCapture, old, name);
		foreach (FlavorRule f in Flavors(s)) {
			Replace(f.Buildings, old, name);
		}
		foreach (SaveCity city in s.Cities) {
			if (city.producible == old) {
				city.producible = name;
			}
			if (name == null) {
				city.buildings.RemoveAll(b => b.building == old);
			} else {
				city.buildings.Where(b => b.building == old).ToList().ForEach(b => b.building = name);
			}
		}
	}

	public static List<string> BuildingUses(SaveGame s, string building) {
		List<string> uses = s.Cities.Where(c => c.buildings.Any(b => b.building == building)).Select(c => $"{c.name} (removed from it)").ToList();
		uses.AddRange(s.Buildings.Where(b => b.requiredBuilding == building || b.doublesHappinessOf == building
			|| b.greatWonderProperties?.buildingGainedInEveryCity == building || b.greatWonderProperties?.buildingGainedInEveryCityOnContinent == building)
			.Select(b => b.name));
		return uses;
	}

	// A civilization renamed (a civilization a player has can't go).
	public static void Civilization(SaveGame s, string old, string name) {
		foreach (SavePlayer p in s.Players.Where(p => p.civilization == old)) {
			p.civilization = name;
		}
		foreach (SaveUnit u in s.Units.Where(u => u.nationality == old)) {
			u.nationality = name;
		}
		foreach (SaveCity c in s.Cities) {
			c.residents.Where(r => r.nationality == old).ToList().ForEach(r => r.nationality = name);
			if (c.capturedFrom == old) {
				c.capturedFrom = name;
			}
		}
		foreach (SaveUnitPrototype p in s.UnitPrototypes) {
			Replace(p.producibleBy, old, name);
		}
		Replace(s.Scenario?.playableCivs, old, name);
		foreach (FlavorRule f in Flavors(s)) {
			Replace(f.Civilizations, old, name);
		}
	}

	public static List<string> CivilizationUses(SaveGame s, string civ) {
		return s.Players.Where(p => p.civilization == civ).Select(_ => "a player of the scenario").ToList();
	}

	// A government renamed or gone (by name; players and buildings refer by
	// ID: see GovernmentGone).
	public static void Government(SaveGame s, string old, string name) {
		foreach (Civilization c in s.Civilizations) {
			if (c.favoriteGovernment == old) { c.favoriteGovernment = name; }
			if (c.shunnedGovernment == old) { c.shunnedGovernment = name; }
		}
		if (s.Rules.OnlyGovernment == old) {
			s.Rules.OnlyGovernment = name;
		}
		foreach (Government g in s.Governments) {
			if (name == null) {
				g.relations.RemoveAll(r => r.government == old);
			} else {
				g.relations.Where(r => r.government == old).ToList().ForEach(r => r.government = name);
			}
		}
	}

	public static void GovernmentGone(SaveGame s, Government gone) {
		Government(s, gone.name, null);
		ID fallback = s.Governments.Find(g => g.defaultType && g != gone)?.id ?? s.Governments.Find(g => g != gone)?.id;
		foreach (SavePlayer p in s.Players.Where(p => p.governmentId?.ToString() == gone.id?.ToString())) {
			p.governmentId = fallback;
		}
		foreach (SaveBuilding b in s.Buildings.Where(b => b.requiredGovernment?.ToString() == gone.id?.ToString())) {
			b.requiredGovernment = null;
		}
	}

	public static List<string> GovernmentUses(SaveGame s, Government g) {
		List<string> uses = s.Players.Where(p => p.governmentId?.ToString() == g.id?.ToString())
			.Select(p => $"{p.civilization}'s government (becomes the default)").ToList();
		uses.AddRange(s.Buildings.Where(b => b.requiredGovernment?.ToString() == g.id?.ToString()).Select(b => b.name));
		uses.AddRange(s.Civilizations.Where(c => c.favoriteGovernment == g.name || c.shunnedGovernment == g.name).Select(c => $"{c.name}'s favourite or shunned"));
		return uses;
	}

	// An advance renamed (only flavors name it) or gone (everything refers
	// by ID).
	public static void TechRenamed(SaveGame s, string old, string name) {
		foreach (FlavorRule f in Flavors(s)) {
			Replace(f.Techs, old, name);
		}
	}

	private static bool Is(ID a, ID b) => a != null && b != null && a.ToString() == b.ToString();

	public static void TechGone(SaveGame s, SaveTech gone) {
		ID id = gone.id;
		TechRenamed(s, gone.Name, null);
		foreach (SaveTech t in s.Techs) {
			t.Prerequisites.RemoveAll(p => Is(p, id));
		}
		foreach (SaveUnitPrototype u in s.UnitPrototypes.Where(u => Is(u.requiredTech, id))) { u.requiredTech = null; }
		foreach (SaveInflow i in s.Inflows.Where(i => Is(i.requiredTech, id))) { i.requiredTech = null; }
		foreach (SaveBuilding b in s.Buildings) {
			if (Is(b.requiredTech, id)) { b.requiredTech = null; }
			if (Is(b.renderedObsoleteBy, id)) { b.renderedObsoleteBy = null; }
		}
		foreach (Resource r in s.Resources.Where(r => Is(r.Prerequisite, id))) { r.Prerequisite = null; }
		foreach (Government g in s.Governments.Where(g => Is(g.prerequisiteTech, id))) { g.prerequisiteTech = null; }
		foreach (CitizenType c in s.CitizenTypes.Where(c => Is(c.PrerequisiteTech, id))) { c.PrerequisiteTech = null; }
		foreach (SaveTerraform t in s.TerraForms.Where(t => Is(t.RequiredTech, id))) { t.RequiredTech = null; }
		foreach (Civilization c in s.Civilizations) {
			c.startingTechs.RemoveWhere(t => Is(t, id));
		}
		foreach (SavePlayer p in s.Players) {
			p.knownTechs.RemoveWhere(t => Is(t, id));
			p.researchQueue.RemoveAll(t => Is(t, id));
			if (Is(p.currentlyResearchedTech, id)) {
				p.currentlyResearchedTech = null;
			}
		}
	}

	public static List<string> TechUses(SaveGame s, SaveTech tech) {
		ID id = tech.id;
		List<string> uses = [];
		uses.AddRange(s.Techs.Where(t => t.Prerequisites.Any(p => Is(p, id))).Select(t => $"{t.Name}'s prerequisites"));
		uses.AddRange(s.UnitPrototypes.Where(u => Is(u.requiredTech, id)).Select(u => u.name));
		uses.AddRange(s.Buildings.Where(b => Is(b.requiredTech, id) || Is(b.renderedObsoleteBy, id)).Select(b => b.name));
		uses.AddRange(s.Inflows.Where(i => Is(i.requiredTech, id)).Select(i => i.name));
		uses.AddRange(s.Resources.Where(r => Is(r.Prerequisite, id)).Select(r => r.Name));
		uses.AddRange(s.Governments.Where(g => Is(g.prerequisiteTech, id)).Select(g => g.name));
		int knowing = s.Players.Count(p => p.knownTechs.Any(t => Is(t, id)));
		if (knowing > 0) {
			uses.Add($"{knowing} player{(knowing == 1 ? "" : "s")} who know it");
		}
		return uses;
	}

	// A resource gone (everything refers by key).
	public static void ResourceGone(SaveGame s, Resource gone) {
		string key = gone.Key;
		foreach (SaveTile t in s.Map?.tiles ?? []) {
			if (t.resource == key) {
				t.resource = null;
			}
		}
		foreach (TerrainType t in s.TerrainTypes) { t.allowedResources.Remove(key); }
		foreach (SaveUnitPrototype u in s.UnitPrototypes) { u.requiredResources.Remove(key); }
		foreach (SaveBuilding b in s.Buildings) { b.requiredResources.Remove(key); }
		foreach (SaveTerraform t in s.TerraForms) { t.RequiredResources.Remove(key); }
		foreach (SavePlayer p in s.Players) { p.obtainedResources?.Remove(key); }
		if (s.Rules.DefaultMoneyResource == gone.Name) {
			s.Rules.DefaultMoneyResource = null;
		}
	}

	public static List<string> ResourceUses(SaveGame s, Resource r) {
		int tiles = s.Map?.tiles.Count(t => t.resource == r.Key) ?? 0;
		List<string> uses = tiles > 0 ? [$"{tiles} tile{(tiles == 1 ? "" : "s")} of the map (taken off)"] : [];
		uses.AddRange(s.UnitPrototypes.Where(u => u.requiredResources.Contains(r.Key)).Select(u => u.name));
		uses.AddRange(s.Buildings.Where(b => b.requiredResources.Contains(r.Key)).Select(b => b.name));
		return uses;
	}
}
