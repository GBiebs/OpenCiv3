namespace C7GameData.AIData {
	public class ExplorerAIData : UnitAIData {
		public Tile destination;
		public TilePath pathToDestination;
		// Heading for a goody hut rather than unknown land.
		public bool goodyHut;

		public override string ToString() {
			return (goodyHut ? "heading for the goody hut at " : "exploring toward ") + destination;
		}
	}
}
