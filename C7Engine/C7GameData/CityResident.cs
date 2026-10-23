namespace C7GameData {
	public class CityResident {
		public CitizenType citizenType;

		// Only relevant if citizenType.IsDefaultCitizen == true
		public Tile tileWorked = Tile.NONE;
		public Civilization nationality;
		public City city;

		// Only relevant if citizenType.IsDefaultCitizen == true
		public enum Mood {
			Happy,
			Content,
			Unhappy
		};
		public Mood mood;

		// A resister refuses to work for the city's owner, who captured the
		// city from a civ it's still at war with (see Resistance). Resisters
		// work no tile, eat nothing and don't count towards the city's mood.
		public bool resisting;
	}
}
