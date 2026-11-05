using System.Collections.Generic;
using System.Linq;
using C7GameData;

namespace C7Engine {
	// Hotseat: who each message to the UI is news for. A human who isn't
	// playing when it's sent sees it at the start of their next turn. A
	// message for no one in particular is part of what the player in
	// control is doing (a confirmation, a map update) and is never held;
	// a prompt that waits for an answer hands its human the UI first
	// instead (TurnHandling.HandControlTo). Online, the same says which
	// computers show it (ForComputerOf).
	public static class MessageRecipients {
		// News for every player: each one's UI decides whether it concerns
		// them (whether they know the civs involved, for instance).
		public static bool ForEveryone(MessageToUI msg) => msg is MsgCivilizationDestroyed or MsgWarDeclarationNotification
			or MsgNuclearAttack or MsgPrincessDelivered or MsgSpaceshipPart or MsgElectionResults or MsgPeaceTreatySigned
			or MsgGlobalWarming or MsgVictory;

		// The players a message is news for (none for everyone's or the
		// acting player's own).
		public static List<ID> Of(MessageToUI msg) {
			IEnumerable<ID> ids = msg switch {
				MsgCityDestroyed m => [m.city.owner?.id],
				MsgCityRazed m => [m.razerId, m.formerOwnerId],
				MsgCityFlipped m => [m.toId, m.fromId],
				MsgCityCaptured m => [m.conquerorId, m.formerOwnerId],
				MsgCityRansacked m => [m.city.owner?.id],
				MsgCityRiotWarning m => [m.city.owner?.id],
				MsgBarbarianCampDispersed m => [m.controllerId],
				MsgBuildingSold m => [m.playerId],
				MsgChangeMobilization m => [m.controllerId],
				MsgDescendIntoAnarchy m => [m.controllerId],
				MsgGoldenAge m => [m.playerId],
				MsgAgeOfScience m => [m.playerId],
				MsgTechsFromWonder m => [m.playerId],
				MsgCelebration m => [m.playerId],
				MsgGoodyHut m => [m.playerId],
				MsgMeltdown m => [m.playerId],
				MsgEmbassyEstablished m => [m.playerId],
				MsgSpyCaught m => [m.victimId],
				MsgBorderWarning m => [m.intruder?.id],
				MsgAirMission m => [m.playerId],
				MsgShipSank m => [m.playerId],
				MsgSpaceshipComplete m => [m.playerId],
				MsgSpaceshipLost m => [m.loser?.id, m.captor?.id],
				MsgDisease m => [m.playerId],
				MsgVolcano m => [m.playerId],
				MsgPlague m => [m.playerId],
				MsgPollution m => [m.playerId],
				MsgResistanceQuelled m => [m.playerId],
				MsgTilePillaged m => [m.pillagerId, m.landOwnerId],
				MsgUnitsCaptured m => [m.capturerId, m.victimId],
				MsgLeaderBorn m => [m.playerId],
				MsgUnitEnslaved m => [m.enslaverId, m.victimId],
				MsgDiplomacyNews m => [m.playerId],
				_ => [],
			};
			return ids.Where(id => id != null).Distinct().ToList();
		}

		// The humans other than `controller` who should see `msg` at the
		// start of their next turn. The player in control always gets it now
		// too (the UI keeps the map up to date with it, and shows the news
		// only to whom it concerns).
		public static List<ID> HeldFor(MessageToUI msg, GameData gameData, ID controller) {
			List<ID> recipients = ForEveryone(msg) ? null : Of(msg);
			if (recipients?.Count == 0) {
				return [];
			}
			return gameData.players.Where(p => p.isHuman && !p.defeated && p.id != controller && (recipients == null || recipients.Contains(p.id)))
				.Select(p => p.id).ToList();
		}

		// Online: whether `msg` is for the computer `local` plays at while
		// `acting` plays: what that computer's own screens sent, news for
		// its player or for everyone, and what the player in control does
		// when that's its player.
		public static bool ForComputerOf(MessageToUI msg, ID local, ID acting) {
			if (msg.fromScreens) {
				return true;
			}
			// (The UI checks whose each of these is.)
			if (msg is MsgStartTurn or MsgHandOver or MsgTurnReady or MsgEnvoyArrives or MsgShowHumanDeal or MsgDiplomacyNews or MsgNewsWhileAway) {
				return true;
			}
			if (ForEveryone(msg)) {
				return true;
			}
			List<ID> recipients = Of(msg);
			if (recipients.Count > 0) {
				return recipients.Contains(local);
			}
			return acting == local;
		}
	}
}
