using System.Collections.Generic;

namespace C7GameData.Save {
	// A class representing a single technology that can be researched.
	//
	// This is intended for use in save games, so it does not have recursive
	// references for prereqs.
	public class SaveTech {
		public enum Flag {
			BonusTechToFirstCivThatResearches,
			EnablesBridges,
			DoublesWealthProduction,
			// Irrigation no longer needs fresh water (Electricity in Civ3).
			EnablesIrrigationEverywhere,
			// Workers work twice as fast (Replaceable Parts in Civ3).
			DoublesWorkerRate,
			// Civs may sign right of passage agreements (Map Making in
			// Civ3).
			EnablesRightOfPassage,

			// The rest of the BIQ's tech flags ("advance flags" in the Civ3
			// editor). The Conquests editor added "Cannot be traded, Permits
			// Sacrifice, Bonus Tech, Reveal Map"
			// (https://forums.civfanatics.com/threads/civ3-conquests-editor-features.67911/).
			//
			// Embassies can be established (Writing in Conquests: "Writing
			// allows you to found embassies",
			// https://sullla.com/Civ3/CQ/CQ_techs.html). Used by
			// Tech.FindEmbassyTech.
			EnablesDiplomats,
			// Cures disease from curable terrain and halves the plague (see
			// Disease and Plague).
			DisablesDiseases,
			// TODO(consumer): drafting citizens into units.
			EnablesConscription,
			// TODO(consumer): the Mobilization war-time production mode.
			EnablesMobilization,
			// TODO(consumer): recycling centers / pollution.
			EnablesRecycling,
			// TODO(consumer): precision strikes by bombers.
			EnablesPrecisionBombing,
			// TODO(consumer): mutual protection pacts in diplomacy.
			EnablesMutualProtection,
			// TODO(consumer): military alliances in diplomacy.
			EnablesMilitaryAlliances,
			// TODO(consumer): trade embargoes in diplomacy.
			EnablesTradeEmbargoes,
			// Harbors link over sea tiles (see TradeNetwork).
			EnablesTradeOverSea,
			// Harbors link over ocean tiles (see TradeNetwork).
			EnablesTradeOverOcean,
			// Trading world and territory maps in diplomacy (Navigation in
			// Conquests, https://sullla.com/Civ3/CQ/CQ_techs.html; see TradeOffer).
			EnablesMapTrading,
			// Trading contacts in diplomacy (see TradeOffer.TradableContacts).
			EnablesCommunicationTrading,
			// The tech can't be traded in diplomacy (see
			// Player.GetTechsTradableTo).
			CannotBeTraded,
			// Units whose type has the Sacrifice action may be sacrificed
			// (Mesoamerica's Ritual Sacrifice; see MapUnit_Sacrifice.cs).
			PermitsSacrifice,
			// Learning the tech reveals the whole map (Satellites in
			// Conquests; see Player.RevealWholeMap).
			RevealsWorldMap,
		}

		public ID id;
		public string Name { get; set; }
		public string CivilopediaEntry { get; set; }
		public int Cost;
		public bool RequiredForEraAdvancement;

		// The civilopedia name of the era this tech is part of
		// (like ERA_Ancient_Times). This is what art lookups are based on.
		public string EraCivilopediaName { get; set; }

		// The path, like "Art\tech chooser\Icons\39-Mapmaking-small.pcx", of
		// the small icon for this tech.
		public string SmallIconPath;

		// The position of this tech within the tech advisor UI.
		public int X;
		public int Y;

		public List<ID> Prerequisites = new();

		// Assorted boolean flags for the tech. They're stored in this set
		// rather than as booleans to avoid bloating the json file.
		public HashSet<Flag> flags = new();

		public C7GameData.Tech ToTechWithoutPrereqs() {
			C7GameData.Tech result = new() {
				id = this.id,
				Name = this.Name,
				CivilopediaEntry = this.CivilopediaEntry,
				Cost = this.Cost,
				RequiredForEraAdvancement = this.RequiredForEraAdvancement,
				BonusTechToFirstCivThatResearches = this.flags.Contains(Flag.BonusTechToFirstCivThatResearches),
				EnablesBridges = this.flags.Contains(Flag.EnablesBridges),
				DoublesWealthProduction = this.flags.Contains(Flag.DoublesWealthProduction),
				EnablesIrrigationEverywhere = this.flags.Contains(Flag.EnablesIrrigationEverywhere),
				DoublesWorkerRate = this.flags.Contains(Flag.DoublesWorkerRate),
				EnablesRightOfPassage = this.flags.Contains(Flag.EnablesRightOfPassage),
				Flags = new HashSet<Flag>(this.flags),
				EraCivilopediaName = this.EraCivilopediaName,
				SmallIconPath = this.SmallIconPath,
				X = this.X,
				Y = this.Y,
				DataSource = this,
			};
			return result;
		}
	}

}
