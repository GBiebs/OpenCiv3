using System.Collections.Generic;
using System.Linq;

namespace C7GameData.Save {

	public class SaveUnit : IHasID {
		public ID id { get; set; }
		public string name;
		public string nationality;
		public string prototype;
		public ID owner;
		public TileLocation previousLocation = new TileLocation();
		public TileLocation currentLocation;
		public List<TileLocation> path;
		public int hitPointsRemaining;
		public float movePointsRemaining;
		public string action; // "fortified"
		public TileDirection facingDirection = TileDirection.SOUTHEAST;
		public string experience;
		public float WorkerProgressTowardsJob;
		public ID WorkerJob;
		public ID loadedOnUnitId;

		// True for multiple types of automation, including worker automation
		// and automated exploring.
		public bool isAutomated;
		// Which one: "work" or "explore" (MapUnit.AutomationKind).
		public string automation;

		// Whether this unit has produced a great leader (Civ3's "Elite*").
		public bool hasProducedLeader;
		public bool scientificLeader;

		// Whether this unit has attacked or bombarded this turn.
		public bool attackedThisTurn;

		public SaveUnit() { }

		public SaveUnit(MapUnit unit) {
			id = unit.id;
			name = unit.name;
			nationality = unit.nationality.name;
			prototype = unit.unitType.name;
			owner = unit.owner.id;
			if (unit.previousLocation is not null) {
				previousLocation = new TileLocation(unit.previousLocation);
			}
			currentLocation = new TileLocation(unit.location);
			loadedOnUnitId = unit.loadedOnUnitId;
			if (unit.path?.PathLength() > 0) {
				path = unit.path.path.ToList().ConvertAll(tile => new TileLocation(tile));
			}
			hitPointsRemaining = unit.hitPointsRemaining;
			action = unit.isFortified ? "fortified" : unit.airSuperiority ? "airSuperiority" : "";
			isAutomated = unit.isAutomated;
			automation = unit.AutomationKind();
			hasProducedLeader = unit.hasProducedLeader;
			scientificLeader = unit.scientificLeader;
			attackedThisTurn = unit.attackedThisTurn;
			facingDirection = unit.facingDirection;
			experience = unit.experienceLevelKey;
			movePointsRemaining = unit.movementPoints.remaining;
			WorkerProgressTowardsJob = unit.WorkerProgressTowardsJob;
			WorkerJob = unit.WorkerJob?.Id;
		}


		public MapUnit ToMapUnit(List<UnitPrototype> prototypes, List<ExperienceLevel> experienceLevels, List<Player> players, List<Terraform> terraforms, GameMap map) {
			MapUnit unit = new MapUnit{
				id = id,
				unitType = prototypes.Find(p => p.name == prototype),
				experienceLevelKey = experience,
				experienceLevel = experienceLevels.Find(el => el.key == experience),
				owner = players.Find(player => player.id == owner),
				location = map.tileAt(currentLocation.X, currentLocation.Y),
				loadedOnUnitId = loadedOnUnitId,
				previousLocation = currentLocation.X == - 1 ? Tile.NONE : map.tileAt(previousLocation.X, previousLocation.Y),
				hitPointsRemaining = hitPointsRemaining,
				movementPoints = new MovementPoints(),
				isFortified = action == "fortified",
				airSuperiority = action == "airSuperiority",
				isAutomated = isAutomated,
				hasProducedLeader = hasProducedLeader,
				scientificLeader = scientificLeader,
				attackedThisTurn = attackedThisTurn,
				facingDirection = facingDirection,
				WorkerProgressTowardsJob = WorkerProgressTowardsJob,
				WorkerJob = WorkerJob == null ? null:terraforms.Find(tf => tf.Id == WorkerJob)
			};
			unit.location.unitsOnTile.Add(unit);
			unit.movementPoints.reset(movePointsRemaining);
			unit.name = string.IsNullOrEmpty(name) ? unit.unitType.name : name;
			// Older saves only say it was automated: workers worked, others explored.
			if (isAutomated) {
				unit.resumeAutomation = automation ?? (unit.unitType.isWorker ? "work" : "explore");
			}

			Player originalNationality = players.Find(player => player.civilization.name == nationality);
			if (originalNationality != null) {
				unit.nationality = originalNationality.civilization;
			} else {
				unit.nationality = unit.owner.civilization;
			}

			return unit;
		}
	}
}
