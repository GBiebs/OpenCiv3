using System;
using System.Collections.Generic;
using System.Linq;

namespace C7GameData;

// Civ3's cultural victory (Civilopedia "Victory Conditions"): one city with
// 20,000 culture points, or a civ with 100,000 (on a standard map; scaled
// by the map's size) and no rival with more than half as much (CivFanatics'
// Civ3 Info Center).
public class CulturalVictory : IVictory {
	private readonly int _cityPoints;
	private readonly int _civPoints;
	private readonly int _lead;

	public CulturalVictory(int cityPoints, int civPoints, int lead) {
		_cityPoints = cityPoints;
		_civPoints = civPoints;
		_lead = lead;
	}

	public static CulturalVictory FromRules(GameData gameData) {
		VictoryRules rules = gameData.rules.victory;
		return new CulturalVictory(rules.cultureCityPoints, CivPointsNeeded(gameData), rules.cultureCivLead);
	}

	// The civ's culture needed on this map: the rule scaled by the percent
	// for the world size the map is closest to (Tiny, Small, Standard,
	// Large, Huge).
	public static int CivPointsNeeded(GameData gameData) {
		VictoryRules rules = gameData.rules.victory;
		int size = WorldSizeIndex(gameData);
		int percent = size >= 0 && size < rules.cultureCivMapSizePercent.Count ? rules.cultureCivMapSizePercent[size] : 100;
		return rules.cultureCivPoints * percent / 100;
	}

	// The world size whose area is closest to the map's, or -1.
	public static int WorldSizeIndex(GameData gameData) {
		if (gameData.worldSizes.Count == 0 || gameData.map == null) {
			return -1;
		}
		int area = gameData.map.numTilesWide * gameData.map.numTilesTall;
		return gameData.worldSizes
			.Select((size, index) => (index, distance: Math.Abs(size.width * size.height - area)))
			.OrderBy(s => s.distance)
			.First().index;
	}

	public string Header() => "Cultural";

	public VictoryStatus Evaluate(Player player, GameData gameData) {
		return new VictoryStatus {
			Player = player,
			CityCulture = player.cities.Select(c => c.GetCulture()).DefaultIfEmpty(0).Max(),
			Culture = player.TotalCulture(),
			RivalCulture = gameData.GetRivals(player).Select(p => p.TotalCulture()).DefaultIfEmpty(0).Max(),
		};
	}

	public bool HasVictory(VictoryStatus status) {
		return status.CityCulture >= _cityPoints
			|| (status.Culture >= _civPoints && status.Culture >= _lead * status.RivalCulture);
	}

	public IEnumerable<string[]> GenerateStatusRows(VictoryStatus status, List<VictoryStatus> rivalStatuses) {
		VictoryStatus topCity = rivalStatuses.OrderByDescending(r => r.CityCulture).FirstOrDefault();
		yield return [
			"Culture in one city:",
			$"{_cityPoints}",
			"Your best city:",
			$"{status.CityCulture}",
			topCity?.Player?.civilization?.name ?? "",
			topCity == null ? "" : $"{topCity.CityCulture}"
		];
		VictoryStatus topCiv = rivalStatuses.OrderByDescending(r => r.Culture).FirstOrDefault();
		yield return [
			"Culture of the civ:",
			$"{_civPoints}",
			"Your culture:",
			$"{status.Culture}",
			topCiv?.Player?.civilization?.name ?? "",
			topCiv == null ? "" : $"{topCiv.Culture}"
		];
	}
}
