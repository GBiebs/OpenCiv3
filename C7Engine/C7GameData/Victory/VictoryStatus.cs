namespace C7GameData;

/// Shared status class for easy abstractions
public class VictoryStatus {
	public Player Player { get; set; }
	public int CurrentTurn { get; set; }
	public float TurnScore { get; set; }
	public int RivalsAlive { get; set; }
	public float DominationArea { get; set; }
	public float DominationPopulation { get; set; }
	public float Score { get; set; }
	// Culture: the player's best city, the whole civ, and the most of any
	// other civ.
	public int CityCulture { get; set; }
	public int Culture { get; set; }
	public int RivalCulture { get; set; }
	// The United Nations: who has it, and whether the player was elected.
	public Player UnitedNationsOwner { get; set; }
	public bool ElectedSecretaryGeneral { get; set; }
}
