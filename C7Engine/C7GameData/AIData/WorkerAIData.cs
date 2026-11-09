namespace C7GameData.AIData {
	public class WorkerAIData : UnitAIData {
		public Terraform workerMove;
		public Tile destination;
		public TilePath pathToDestination;
		// The kind of automation it plans for (null: everything).
		public WorkerAutomation automation;

		public override string ToString() {
			return workerMove.UIAction + " at " + destination;
		}
	}
}
