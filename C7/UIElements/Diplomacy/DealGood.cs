using System;
using System.Collections.Generic;
using C7GameData;

// One of the goods a civ can put on the deal table, under one of Civ3's
// headings: a map, an introduction, a resource, a city or a worker.
public class DealGood {
	public string heading;
	public string text;
	public Func<TradeOffer, bool> isIn;
	public Action<TradeOffer> add;
	public Action<TradeOffer> remove;

	// Civ3's headings for goods, before gold and after technology.
	public static readonly string[] HeadingsBeforeGold = ["Maps", "Communications", "Resources", "Luxuries"];
	public static readonly string[] HeadingsAfterTechnology = ["Cities", "Workers"];

	// What `giver` could give `receiver`.
	public static List<DealGood> Of(GameData gameData, Player giver, Player receiver) {
		List<DealGood> goods = new();
		if (giver.HasTechWithFlag(C7GameData.Save.SaveTech.Flag.EnablesMapTrading)) {
			goods.Add(new DealGood {
				heading = "Maps",
				text = "World Map",
				isIn = o => o.worldMap,
				add = o => o.worldMap = true,
				remove = o => o.worldMap = false,
			});
			goods.Add(new DealGood {
				heading = "Maps",
				text = "Territory Map",
				isIn = o => o.territoryMap,
				add = o => o.territoryMap = true,
				remove = o => o.territoryMap = false,
			});
		}
		if (giver.HasTechWithFlag(C7GameData.Save.SaveTech.Flag.EnablesCommunicationTrading)) {
			foreach (Player third in Trading.Introductions(gameData, giver, receiver)) {
				goods.Add(new DealGood {
					heading = "Communications",
					text = $"with the {third.civilization.noun}",
					isIn = o => o.contacts.Contains(third.id),
					add = o => o.contacts.Add(third.id),
					remove = o => o.contacts.Remove(third.id),
				});
			}
		}
		foreach (Resource resource in Trading.Tradeable(gameData, giver)) {
			goods.Add(new DealGood {
				heading = resource.Category == ResourceCategory.LUXURY ? "Luxuries" : "Resources",
				text = resource.Name,
				isIn = o => o.resources.Contains(resource.Key),
				add = o => o.resources.Add(resource.Key),
				remove = o => o.resources.Remove(resource.Key),
			});
		}
		foreach (City city in Trading.TradeableCities(giver)) {
			goods.Add(new DealGood {
				heading = "Cities",
				text = $"{city.name} ({city.residents.Count})",
				isIn = o => o.cities.Contains(city.id),
				add = o => o.cities.Add(city.id),
				remove = o => o.cities.Remove(city.id),
			});
		}
		foreach (MapUnit worker in Trading.TradeableWorkers(giver)) {
			string where = worker.location.cityAtTile?.name ?? $"{worker.location.XCoordinate}, {worker.location.YCoordinate}";
			goods.Add(new DealGood {
				heading = "Workers",
				text = $"{worker.unitType.name} ({where})",
				isIn = o => o.workers.Contains(worker.id),
				add = o => o.workers.Add(worker.id),
				remove = o => o.workers.Remove(worker.id),
			});
		}
		return goods;
	}
}
