namespace C7GameData;

public class HistTurnRecord {
	public int Date { get; set; }
	public int Power { get; set; }
	public int Score { get; set; }
	public int Culture { get; set; }
	public int VP { get; set; }

	// The score's parts, averaged over the turns like the score itself
	// (Civ3's histograph summary): happy citizens, content citizens and
	// specialists, territory, and future techs.
	public float ScoreHappy { get; set; }
	public float ScoreContent { get; set; }
	public float ScoreTerritory { get; set; }
	public float ScoreFutureTech { get; set; }
}
