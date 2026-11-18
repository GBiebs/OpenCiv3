using System.Linq;
using C7Engine;

namespace C7GameData;

// Scientific great leaders (Conquests). "Each time a technology is researched
// for the first time by any civilization there is a chance that a Scientific
// leader will arise (this chance is increased slightly if the researching
// civilization has the "scientific" strength)" (the Conquests Civilopedia,
// GCON_Leaders). The chances are 3%, and 5% for a Scientific civ: "The
// chances of a Scientific civilization to get a SGL upon researching a tech is
// 5% - for non-Scientific civilizations it is 3%"
// (https://civfanatics.com/civ3/faq/), the figures the owner gave too
// (owner's decision, 2026-10-11).
//
// Only research counts, as the Civilopedia says: not techs traded, stolen,
// found in huts, or given by the Great Library or Theory of Evolution, nor a
// Scientific civ's free tech on entering an era. Philosophy's free tech is
// the exception, counting as a discovery: "You can also get one for
// researching philosophy and getting that free tech" (AutomatedTeller,
// https://forums.civfanatics.com/threads/how-do-you-get-a-scientific-leader.211436;
// see Player.SetCurrentlyResearchedTech).
//
// As in Conquests, a scientific leader is the same unit type as a military
// one (the RULE section's battle-created unit, "Leader"), drawn with the
// type's "SCI" art. It "can also be used to hurry the construction of any
// improvement (including a Great Wonder) of the city he's currently in"
// (GCON_Leaders); players report it can "rush anything in a city
// (improvement, wonder, unit)" (https://civfanatics.com/civ3/faq/). So it
// can hurry anything a city builds, any improvement, wonder or unit, not
// only wonders (owner's decision, 2026-10-12). It can't form an army. Its
// other use is starting an Age of Science: "Simply move the scientific leader
// to your capital and press the "Increase Scientific Output" button" / "When
// the leader does either of these actions he disappears" (GCON_Leaders).
// The shipped game's age gives no research (see Player.StartScienceAge); we
// give the intended bonus.
public partial class MapUnit {
	internal const double ScientificLeaderChance = 0.03;
	internal const double ScientificTraitLeaderChance = 0.05;
	internal const string ScientificLeaderName = "Scientific Leader";

	// The art variation of the leader unit type for scientific leaders.
	internal const string ScientificLeaderArt = "SCI";

	public bool IsScientificLeader() {
		return isScientificLeader && IsLeader();
	}

	// A scientific leader in its owner's capital, with moves left, can
	// start an Age of Science (see above).
	public bool CanStartScienceAge() {
		return IsScientificLeader() && movementPoints.canMove && Tile.IsTileValid(location)
			&& location.HasCity() && location.cityAtTile.owner == owner && location.cityAtTile.IsCapital()
			&& owner.CanStartScienceAge();
	}

	// Starts the age; the leader is used up. Returns whether it started.
	public bool StartScienceAge() {
		if (!CanStartScienceAge()) {
			return false;
		}
		Player player = owner;
		EngineStorage.gameData.RemoveUnit(this);
		player.StartScienceAge();
		return true;
	}

	public static double ScientificLeaderChanceFor(Player player) {
		return player.civilization?.traits.Contains(Civilization.Trait.Scientific) == true
			? ScientificTraitLeaderChance
			: ScientificLeaderChance;
	}

	// Rolls for a scientific leader for a player who has just researched the
	// tech, having been the first civ to know it. The leader appears in the
	// capital, or failing that the player's first city; a player with no
	// city gets none. Returns the leader, or null.
	internal static MapUnit RollForScientificLeader(GameData gameData, Player player, Tech tech) {
		if (gameData.rules?.ScientificLeaders == false || player.IsIndependent) {
			return null;
		}
		UnitPrototype proto = LeaderPrototype();
		City city = player.cities.FirstOrDefault(c => c.capital) ?? player.cities.FirstOrDefault();
		if (proto == null || city == null) {
			return null;
		}
		if (GameData.rng.NextDouble() >= ScientificLeaderChanceFor(player)) {
			return null;
		}

		MapUnit leader = gameData.SpawnUnit(player, proto, city.location);
		leader.isScientificLeader = true;
		leader.name = NextLeaderName(gameData, player, player.civilization?.scientificLeaderNames) ?? ScientificLeaderName;
		RecordGreatLeader(gameData, leader);
		log.Information("{Player}'s discovery of {Tech} has produced a scientific leader in {City}", player, tech.Name, city);
		if (player.IsToldNews) {
			new MsgShowScienceAdvisorPopup(player,
				$"Our discovery of {tech.Name} has inspired a great scientist in {city.name}!\nA scientific leader can complete anything one of our cities is building, even a great wonder.",
				MsgShowScienceAdvisorPopup.Mood.Happy).send();
		}
		return leader;
	}
}
