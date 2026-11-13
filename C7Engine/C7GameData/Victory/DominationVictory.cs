using System.Collections.Generic;
using System.Linq;
using C7GameData;

namespace C7Engine;

// Won by controlling a large enough share of both the world's land and its
// population.
public class DominationVictory : IVictory {
	private readonly int territoryPercentNeeded;
	private readonly int populationPercentNeeded;

	public DominationVictory(int territoryPercentNeeded, int populationPercentNeeded) {
		this.territoryPercentNeeded = territoryPercentNeeded;
		this.populationPercentNeeded = populationPercentNeeded;
	}

	public string Header() => "Domination";

	// The number of tiles counted for domination, cached since it takes a
	// scan of the whole map and is needed for every player. Base terrain is
	// only set while making the map, but to be safe this is recounted each
	// turn, and whenever the map or its list of tiles is replaced.
	private sealed class TerritoryCount {
		internal GameMap map;
		internal List<Tile> tiles;
		internal int tileCount;
		internal int turn;
		internal int total;
	}

	private TerritoryCount territoryCount;

	private int TotalTerritory(GameData gameData) {
		GameMap map = gameData.map;
		List<Tile> tiles = map.tiles;
		TerritoryCount count = territoryCount;
		if (count != null && ReferenceEquals(count.map, map) && ReferenceEquals(count.tiles, tiles)
			&& count.tileCount == tiles.Count && count.turn == gameData.turn) {
			return count.total;
		}

		int total = 0;
		foreach (Tile t in tiles) {
			if (t.IsCountedForDomination()) {
				++total;
			}
		}
		territoryCount = new TerritoryCount { map = map, tiles = tiles, tileCount = tiles.Count, turn = gameData.turn, total = total };
		return total;
	}

	private static int Population(Player player) {
		if (player.hostFacts != null) {
			return player.hostFacts.population;
		}
		int result = 0;
		foreach (City c in player.cities) {
			result += c.residents.Count;
		}
		return result;
	}

	public VictoryStatus Evaluate(Player player, GameData gameData) {
		// A LAN guest isn't sent all of the world, so the host counts it.
		int totalTerritory = gameData.hostFacts?.dominationTiles ?? TotalTerritory(gameData);
		int ourTerritory = player.hostFacts?.dominationTiles ?? player.tileKnowledge.CountDominationTiles();

		// Population is cheap to sum (no tiles involved), and it can change
		// during a turn, so it isn't cached.
		int totalPopulation = 0;
		foreach (Player p in gameData.players) {
			totalPopulation += Population(p);
		}
		int ourPopulation = Population(player);

		return new VictoryStatus {
			Player = player,
			TerritoryPercent = totalTerritory == 0 ? 0 : 100f * ourTerritory / totalTerritory,
			PopulationPercent = totalPopulation == 0 ? 0 : 100f * ourPopulation / totalPopulation,
		};
	}

	// Both shares must be strictly above the thresholds: "land % and
	// population % both strictly above g_DominationLandPct /
	// g_DominationPopPct" (Perez's decompile of Civ3 Conquests,
	// doc/perez-findings/Civ3_Findings.md, Round 2 "Victory conditions";
	// authoritative per the owner's 2026-11-12 ruling).
	public bool HasVictory(VictoryStatus status) {
		return status.TerritoryPercent > territoryPercentNeeded
			&& status.PopulationPercent > populationPercentNeeded;
	}

	public IEnumerable<string[]> GenerateStatusRows(VictoryStatus status, List<VictoryStatus> rivalStatuses) {
		VictoryStatus topTerritory = rivalStatuses.OrderByDescending(r => r.TerritoryPercent).FirstOrDefault();
		VictoryStatus topPopulation = rivalStatuses.OrderByDescending(r => r.PopulationPercent).FirstOrDefault();

		yield return [
			"Territory needed:",
			$"{territoryPercentNeeded}%",
			"Territory:",
			$"{status.TerritoryPercent:0}%",
			topTerritory?.Player?.civilization?.name ?? "",
			topTerritory == null ? "" : $"{topTerritory.TerritoryPercent:0}%",
		];
		yield return [
			"Population needed:",
			$"{populationPercentNeeded}%",
			"Population:",
			$"{status.PopulationPercent:0}%",
			topPopulation?.Player?.civilization?.name ?? "",
			topPopulation == null ? "" : $"{topPopulation.PopulationPercent:0}%",
		];
	}
}
