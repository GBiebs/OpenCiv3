using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using Serilog;

namespace C7GameData {
	// A unit entering a minor tribe's village (goody hut, issue #934). The
	// chances come from CivFanatics' "Probabilities of goody huts (C3C)": a
	// table by difficulty for most civs, and for an Expansionist civ the next
	// easier level's with no barbarians. Outcomes the article's conditions
	// rule out drop out and the rest share their chance (its spreadsheet
	// recomputes the table the same way). The tables are rules
	// (Rules.GoodyHutOdds, ExpansionistGoodyHutOdds). Messages are Civ3's
	// (script.txt GOODY_*), $BARBARIAN0 a name from the barbarian
	// civilization's list.
	public static class GoodyHuts {
		private static ILogger log = Log.ForContext(typeof(GoodyHuts));

		public enum Outcome { City, Tech, Gold, Settler, Map, Mercenaries, Nothing, Barbarians }

		// The rules' row for the game's difficulty: the one named after it,
		// else the one at its place among the difficulties (Monarch's if the
		// difficulty isn't found). Civ3's BIQ calls its hardest level "Sid".
		public static GoodyHutOdds Odds(GameData gameData, Player player) {
			List<GoodyHutOdds> table = IsExpansionist(player) ? gameData.rules.ExpansionistGoodyHutOdds : gameData.rules.GoodyHutOdds;
			if (table == null || table.Count == 0) {
				return null;
			}
			GoodyHutOdds named = table.Find(row => row.difficulty == gameData.gameDifficulty?.Name);
			if (named != null) {
				return named;
			}
			int index = gameData.difficulties.IndexOf(gameData.gameDifficulty);
			if (index < 0) {
				index = gameData.difficulties.FindIndex(d => d.id == gameData.gameDifficulty?.id);
			}
			return table[Math.Clamp(index < 0 ? 3 : index, 0, table.Count - 1)];
		}

		private static double Chance(GoodyHutOdds odds, Outcome outcome) {
			return outcome switch {
				Outcome.City => odds.city,
				Outcome.Tech => odds.tech,
				Outcome.Gold => odds.gold,
				Outcome.Settler => odds.settler,
				Outcome.Map => odds.map,
				Outcome.Mercenaries => odds.mercenaries,
				Outcome.Nothing => odds.nothing,
				Outcome.Barbarians => odds.barbarians,
				_ => 0,
			};
		}

		private static bool IsExpansionist(Player player) {
			return player.civilization?.traits.Contains(Civilization.Trait.Expansionist) == true;
		}

		// The chance of each outcome for this unit entering this hut, once the
		// impossible ones are out; they add up to 1 (or nothing is possible).
		public static Dictionary<Outcome, double> OutcomeChances(GameData gameData, MapUnit unit, Tile tile) {
			GoodyHutOdds odds = Odds(gameData, unit.owner);
			Dictionary<Outcome, double> chances = Enum.GetValues<Outcome>()
				.Where(o => odds != null && Chance(odds, o) > 0 && IsPossible(o, gameData, unit, tile))
				.ToDictionary(o => o, o => Chance(odds, o));
			double total = chances.Values.Sum();
			return chances.ToDictionary(kv => kv.Key, kv => kv.Value / total);
		}

		public static bool IsPossible(Outcome outcome, GameData gameData, MapUnit unit, Tile tile) {
			Player player = unit.owner;
			switch (outcome) {
				case Outcome.City:
					// Not in an elimination game (Conquests readme: "City goodie
					// hut spawns are not possible in Elimination Mode").
					return tile.IsAllowCities() && !tile.HasCity() && tile.neighbors.Values.All(n => !n.HasCity())
						&& gameData.victoryConditions?.CityElimination != true;
				case Outcome.Tech:
					return player.EraIndex() == 0 && player.GetAvailableTechsToResearch(gameData.techs).Count > 0;
				case Outcome.Gold:
					return tile.Resource == null || tile.Resource == Resource.NONE;
				case Outcome.Settler: {
						// Only for a civ with no settler about and fewer cities
						// than the average.
						List<Player> civs = gameData.players.Where(p => !p.isBarbarians && !p.defeated).ToList();
						double average = (double)civs.Sum(p => p.cities.Count) / Math.Max(1, civs.Count);
						return SettlerType(gameData, player) != null && player.cities.Count < average
							&& !player.units.Any(u => u.unitType.actions.Contains(UnitAction.BuildCity))
							&& !player.cities.Any(c => c.itemBeingProduced is UnitPrototype p && p.actions.Contains(UnitAction.BuildCity));
					}
				case Outcome.Mercenaries:
					return MercenaryType(gameData, player) != null;
				case Outcome.Map:
				case Outcome.Nothing:
					return true;
				case Outcome.Barbarians:
					return !IsExpansionist(player)
						&& gameData.barbarianInfo.barbarianActivity != BarbarianActivity.None
						&& !tile.HasCity() && tile.neighbors.Values.All(n => !n.HasCity())
						&& player.cities.Count > 0 && player.units.Any(u => u.IsCombatUnit())
						&& BarbarianSites(tile).Count > 0;
				default:
					return false;
			}
		}

		private static UnitPrototype SettlerType(GameData gameData, Player player) {
			return gameData.unitPrototypes
				.Where(u => u.actions.Contains(UnitAction.BuildCity) && (u.requiredTech == null || player.knownTechs.Contains(u.requiredTech.id)))
				.OrderByDescending(u => u.name == "Settler")
				.FirstOrDefault();
		}

		// "A unit available to the Barbarians as well as the player": the best
		// of the barbarians' land units the player has the advance for.
		private static UnitPrototype MercenaryType(GameData gameData, Player player) {
			return new[] { gameData.barbarianInfo.advancedBarbarian, gameData.barbarianInfo.basicBarbarian }
				.Where(u => u != null && (u.requiredTech == null || player.knownTechs.Contains(u.requiredTech.id)))
				.OrderByDescending(u => u.attack)
				.FirstOrDefault();
		}

		// The land around the hut where angry tribesmen can appear.
		private static List<Tile> BarbarianSites(Tile tile) {
			return tile.neighbors.Values.Where(n => n.IsLand() && !n.HasCity() && n.unitsOnTile.Count == 0 && !n.overlayTerrainType.impassable).ToList();
		}

		private static string TribeName(GameData gameData) {
			List<string> names = gameData.players.Find(p => p.isBarbarians)?.civilization?.cityNames;
			return names == null || names.Count == 0 ? "local" : names[GameData.rng.Next(names.Count)];
		}

		// The unit enters the village: it's gone, and something happens.
		public static Outcome Enter(GameData gameData, MapUnit unit, Tile tile) {
			tile.hasGoodyHut = false;
			Dictionary<Outcome, double> chances = OutcomeChances(gameData, unit, tile);
			Outcome outcome = Outcome.Nothing;
			double roll = GameData.rng.NextDouble();
			foreach ((Outcome o, double chance) in chances) {
				outcome = o;
				roll -= chance;
				if (roll < 0) {
					break;
				}
			}
			string text = Apply(outcome, gameData, unit, tile, TribeName(gameData));
			log.Information($"{unit.owner} entered a goody hut at {tile}: {text}");
			new MsgGoodyHut(unit.owner.id, outcome, text, tile).send();
			return outcome;
		}

		public static string Apply(Outcome outcome, GameData gameData, MapUnit unit, Tile tile, string tribe) {
			Player player = unit.owner;
			Rules rules = gameData.rules;
			switch (outcome) {
				case Outcome.City: {
						CityInteractions.BuildCity(tile, player, player.GetNextCityName());
						return $"An advanced {tribe} village has joined us!";
					}
				case Outcome.Tech: {
						List<Tech> techs = player.GetAvailableTechsToResearch(gameData.techs);
						Tech tech = techs[GameData.rng.Next(techs.Count)];
						player.AcquireTechs(gameData, [tech]);
						return $"The {tribe} tribe has taught us {tech.Name}.";
					}
				case Outcome.Gold: {
						int gold = rules.GoodyHutGold.Count == 0 ? 0 : rules.GoodyHutGold[GameData.rng.Next(rules.GoodyHutGold.Count)];
						player.gold += gold;
						return $"We got {gold} gold from the {tribe} tribe's village.";
					}
				case Outcome.Settler: {
						UnitPrototype settler = SettlerType(gameData, player);
						gameData.SpawnUnit(player, settler, tile);
						return $"A friendly {tribe} {settler.name} wants to join our {player.government.name}.";
					}
				case Outcome.Map: {
						List<Tile> region = [.. tile.GetTilesWithinRankDistance(rules.GoodyHutMapRadius)];
						foreach (Tile t in region) {
							player.tileKnowledge.AddTilesToKnown(t, recomputeActiveTiles: t == region.Last());
						}
						return $"The friendly {tribe} tribe gave us maps of their region.";
					}
				case Outcome.Mercenaries: {
						UnitPrototype type = MercenaryType(gameData, player);
						// "A skilled unit": a veteran.
						gameData.SpawnUnit(player, type, tile).Promote();
						return $"This friendly {tribe} village gave us a skilled {type.name}.";
					}
				case Outcome.Barbarians: {
						Player barbarians = gameData.players.Find(p => p.isBarbarians);
						List<Tile> sites = BarbarianSites(tile);
						UnitPrototype type = GameData.rng.Next(100) < rules.GoodyHutAdvancedBarbarianPercent
							? gameData.barbarianInfo.advancedBarbarian : gameData.barbarianInfo.basicBarbarian;
						for (int i = 0; i < rules.GoodyHutBarbarians && sites.Count > 0; ++i) {
							Tile site = sites[GameData.rng.Next(sites.Count)];
							gameData.SpawnUnit(barbarians, type, site);
						}
						return $"We have disturbed an angry {tribe} {type.name}.";
					}
				default:
					return $"This {tribe} village is deserted.";
			}
		}
	}
}
