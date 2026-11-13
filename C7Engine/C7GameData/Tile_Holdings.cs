using C7Engine;
using Serilog;
using static C7GameData.Tile.TileOverlays;
using Layer = C7GameData.TerrainImprovement.Layer;

namespace C7GameData;

// Outposts, colonies, airfields and radar towers: holdings a worker builds
// for its civ, which belong to that civ. A tile has at most one of them. An
// outpost, airfield or radar tower sits on the tile's holdings layer, so it
// doesn't go with a fortress or with one another; a colony has a layer of
// its own, as Civ3 lets a colony share its tile with a fortress (one of the
// Civ3 saves looked at has a tile with both).
//
// Outposts, from the Conquests Civilopedia (GCON_Outposts, TFRM_Outpost):
//   "Outposts eliminate fog of war. To construct an Outpost, move a worker
//   to any land tile in neutral or owned territory and select the Build
//   Outpost unit action. Construction of the Outpost consumes the worker.
//   The range of vision for the Outpost is 2 tiles on flat terrain, 3 on
//   hills and 4 on mountains. Outposts can only be used by the player who
//   created them. If an Outpost falls within the territory of any other
//   civilization, it will be destroyed. Outposts can only be built in a tile
//   that is empty, has a mine, irrigation, a road and/or a railroad. You
//   cannot build an Outpost on a tile with an Airfield or a Colony. The
//   "Build Outpost" unit-action becomes available after the Masonry advance
//   is researched."
//
// Colonies, from the same Civilopedia (TFRM_Build_Colony, GCON_Colony):
//   "A worker can establish a colony on any square that contains a tradable
//   resource. If the colony is then connected to a city, that city gains
//   access to the resource. A colony is necessary only if the resource is
//   not inside your borders. Note that when a city's borders expand to
//   include a colony, the colony disappears because it is no longer needed.
//   The worker is consumed by building a colony."
// and "[Colonies] ... enable exploitation of tradable resources that lie
// outside your territory. As with all tradable resources (strategic and
// luxury), you need a road connecting the colony to one of your cities to
// get the benefits of the resource."
//
// Airfields, from the same Civilopedia (GCON_Airfields, TFRM_Airfield):
//   "Airfields function in the same manner that a city with an airport would
//   for air units. To construct an Airfield, move a worker to any neutral or
//   owned land tile and select the Build Airfield unit action. Construction
//   of the Airfield consumes the worker. Airfields can be a re-base mission
//   destination. All air missions can be launched from an airfield ...
//   Airfields can only be used by the player who created it. If it falls
//   into the territory of another civilization, however, the airfield
//   becomes solely useable by the player whose territory it lies in (unless
//   they have yet to research Flight, in which case the airfield is
//   destroyed). Airfields can be pillaged like a terrain improvement, which
//   also destroys the airfield. Airfields can only be built in tiles that
//   are empty or have a road and/or railroad."
//
// Radar towers, from the same Civilopedia (GCON_Radar_Towers,
// TFRM_Radar_Tower):
//   "Radar Towers provide a combat bonus to units within their affected
//   area. To construct a Radar Tower, move a worker to any land tile within
//   your territory and select the "Build Radar Tower" unit action.
//   Construction of the Radar Tower consumes the worker. The range of the
//   effected area is 2 tiles. Friendly units within the Radar Towers range
//   get a +25% attack and defense bonus. This bonus can only be applied once,
//   even if multiple towers overlap. Radar Towers can only be used by the
//   player who created them. Should a Radar Tower fall into the territory of
//   another civilization, it will be destroyed. Radar Towers can only be
//   built in a tile that is empty, has a mine, irrigation, a road and/or a
//   railroad."
public partial class Tile {
	private static readonly ILogger holdingLog = Log.ForContext<Tile>();

	// The civ whose outpost, colony, airfield or radar tower is on the tile,
	// or null if there is none. One placed by a scenario without an owner
	// has none either, and does nothing for anyone.
	public Player holdingOwner;

	// Whether the tile has a fortress, or a barricade, which is built on one
	// and "become[s] a fortress if their square is pillaged" (Conquests
	// Civilopedia, GCON_Barricades).
	public bool HasFortress() {
		return overlays.HasImprovementWithKey(FORTRESS, Layer.Holdings) || overlays.HasImprovementWithKey(BARRICADE, Layer.Holdings);
	}

	public bool HasOutpost() {
		return overlays.HasImprovementWithKey(OUTPOST, Layer.Holdings);
	}

	public bool HasColony() {
		return overlays.HasImprovementWithKey(COLONY, Layer.Colonies);
	}

	public bool HasAirfield() {
		return overlays.HasImprovementWithKey(AIRFIELD, Layer.Holdings);
	}

	public bool HasRadarTower() {
		return overlays.HasImprovementWithKey(RADAR_TOWER, Layer.Holdings);
	}

	// The civ whose airfield is on the tile, or null.
	public Player AirfieldOwner() {
		return holdingOwner != null && HasAirfield() ? holdingOwner : null;
	}

	// The civ whose radar tower is on the tile, or null.
	public Player RadarTowerOwner() {
		return holdingOwner != null && HasRadarTower() ? holdingOwner : null;
	}

	// The civ whose outpost is on the tile, or null.
	public Player OutpostOwner() {
		return holdingOwner != null && HasOutpost() ? holdingOwner : null;
	}

	// The civ whose colony is on the tile, or null.
	public Player ColonyOwner() {
		return holdingOwner != null && HasColony() ? holdingOwner : null;
	}

	// How far an outpost here sees: "2 tiles on flat terrain, 3 on hills and
	// 4 on mountains". In the standard rules hills have a height of 2 and
	// mountains (and volcanoes) of 4; anything lower counts as flat.
	public int OutpostSightRange() {
		int height = overlayTerrainType.height;
		if (height >= 4) {
			return 4;
		}
		if (height >= 2) {
			return 3;
		}
		return 2;
	}

	// How far a colony sees: the tiles next to it. UNVERIFIED: no Civ3 text
	// says a colony sees at all; GBiebs/OpenCiv3 found that Civ3's saves
	// mark the 3x3 around every colony as seen by structures, on any
	// terrain, which this follows (see the compendium's open questions).
	public const int ColonySightRange = 1;

	// How far a radar tower sees: "The radar towers give visibility 2 tiles
	// out (not including the center square)" (WarpStorm, Apolyton, "Answers
	// on radar towers",
	// https://apolyton.net/forum/miscellaneous/archives/civ3-play-the-world/86998-answers-on-radar-towers),
	// the 5x5 square that GBiebs/OpenCiv3 also found Civ3's saves mark as
	// seen by structures.
	public const int RadarTowerSightRange = 2;

	// How far the holding here shows its owner the map, whatever the terrain
	// between: an outpost further from hills and mountains, a colony and a
	// radar tower the same on any terrain. -1 if there is no holding that
	// sees (an airfield doesn't).
	public int HoldingSightRange() {
		if (HasOutpost()) {
			return OutpostSightRange();
		}
		if (HasRadarTower()) {
			return RadarTowerSightRange;
		}
		if (HasColony()) {
			return ColonySightRange;
		}
		return -1;
	}

	// Whether the player has a holding here that sees around it.
	public bool HasSightedHoldingOf(Player player) {
		return player != null && holdingOwner == player && HoldingSightRange() >= 0;
	}

	// The tile's outpost, colony, airfield or radar tower, or null.
	public TerrainImprovement OwnedHolding() {
		TerrainImprovement colony = overlays.ImprovementAtLayer(Layer.Colonies);
		if (colony != null && IsOwnedHolding(colony)) {
			return colony;
		}
		TerrainImprovement holding = overlays.ImprovementAtLayer(Layer.Holdings);
		return holding != null && IsOwnedHolding(holding) ? holding : null;
	}

	// Each of these holdings also needs terrain whose BIQ flags allow it
	// (TERR AllowOutposts, AllowAirfields, AllowRadarTowers, AllowColonies;
	// see TerrainType.allowOutposts): in Civ3's rules a Marsh takes only an
	// outpost, and a Volcano none.
	//
	// Whether the player's worker could build an outpost here: a land tile
	// that is neutral or the player's own, without a city, and without a
	// fortress, colony or other holding (mines, irrigation and roads are
	// fine).
	public bool CanTakeOutpost(Player player) {
		if (!IsLand() || HasCity() || !overlayTerrainType.allowOutposts) {
			return false;
		}
		Player owner = OwningPlayer();
		if (owner != null && owner != player) {
			return false;
		}
		return overlays.ImprovementAtLayer(Layer.Holdings) == null && overlays.ImprovementAtLayer(Layer.Colonies) == null;
	}

	// Whether the player's worker could build an airfield here: a land tile
	// that is neutral or the player's own, without a city, and with nothing
	// built on it but a road or railroad ("tiles that are empty or have a
	// road and/or railroad").
	public bool CanTakeAirfield(Player player) {
		if (!IsLand() || HasCity() || !overlayTerrainType.allowAirfields) {
			return false;
		}
		Player owner = OwningPlayer();
		if (owner != null && owner != player) {
			return false;
		}
		foreach (TerrainImprovement ti in overlays.GetImprovements()) {
			if (TileOverlays.IsManMade(ti) && ti.layer != Layer.Roads) {
				return false;
			}
		}
		return true;
	}

	// Whether the player's worker could build a radar tower here: a land
	// tile inside the player's borders, without a city, and without a
	// fortress, colony or other holding (mines, irrigation and roads are
	// fine).
	public bool CanTakeRadarTower(Player player) {
		if (!IsLand() || HasCity() || !overlayTerrainType.allowRadarTowers || player == null || OwningPlayer() != player) {
			return false;
		}
		return overlays.ImprovementAtLayer(Layer.Holdings) == null && overlays.ImprovementAtLayer(Layer.Colonies) == null;
	}

	// A radar tower's "affected area is 2 tiles", taken as the square of
	// tiles up to two steps away, like an outpost's sight on flat land.
	// UNVERIFIED: whether Civ3 counts the corners of that square.
	public const int RadarTowerRange = 2;

	// Whether one of the player's radar towers covers this tile. A plain
	// loop over the square, as it is asked in every fight.
	public bool IsCoveredByRadarOf(Player player) {
		if (player == null || map == null) {
			return false;
		}
		int count = (RadarTowerRange * 2 + 1) * (RadarTowerRange * 2 + 1);
		(int, int)[] offsets = NeighborIndexOffsets(count);
		for (int i = 0; i < count; ++i) {
			Tile t = i == 0 ? this : map.tileAt(XCoordinate + offsets[i].Item1, YCoordinate + offsets[i].Item2);
			if (t != NONE && t.holdingOwner == player && t.HasRadarTower()) {
				return true;
			}
		}
		return false;
	}

	// Whether the player's worker could found a colony here: a land tile
	// with a strategic or luxury resource the player knows about, outside
	// every civ's borders (inside them it would be taken in by the borders at
	// once), without a city, a colony or an outpost. A fortress is fine.
	// UNVERIFIED: the Civilopedia says "any square that contains a tradable
	// resource"; whether Civ3 lets a colony go up inside a rival's borders,
	// or next to an outpost, isn't stated. Since borders take a colony in,
	// it is only offered outside them, and one civ's holding isn't put on
	// another's.
	public bool CanTakeColony(Player player) {
		if (!IsLand() || HasCity() || !overlayTerrainType.allowColonies || Resource == null || Resource == Resource.NONE) {
			return false;
		}
		if (Resource.Category != ResourceCategory.STRATEGIC && Resource.Category != ResourceCategory.LUXURY) {
			return false;
		}
		if (OwningPlayer() != null || OwnedHolding() != null) {
			return false;
		}
		return player.KnowsAboutResource(Resource);
	}

	// Puts the player's holding on the tile. One that sees (an outpost,
	// colony or radar tower) shows its owner what it can see at once; a
	// colony may give them a resource.
	internal void PlaceHolding(TerrainImprovement holding, Player owner) {
		TryRemoveRuins(this);
		overlays.Add(holding);
		holdingOwner = owner;
		TileChangeJournal.Record(this);
		if (holding.key == COLONY) {
			EngineStorage.gameData?.InvalidateCachedTradeNetwork();
		}
		if (HoldingSightRange() >= 0) {
			owner?.tileKnowledge.AddTilesToKnown(this);
		}
	}

	// Called when an outpost or colony leaves the tile, however it goes.
	internal void ForgetHolding(TerrainImprovement holding) {
		holdingOwner = null;
		TileChangeJournal.Record(this);
		if (holding.key == COLONY) {
			EngineStorage.gameData?.InvalidateCachedTradeNetwork();
		}
	}

	// Called when the city whose borders hold the tile changes. Borders take
	// in any colony, and destroy another civ's outpost or radar tower.
	// Another civ's airfield becomes theirs if they know the tech airfields
	// need (Flight), and is destroyed if not.
	internal void CheckHoldingAgainstBorders() {
		City city = owningCity;
		if (city == null) {
			return;
		}
		TerrainImprovement holding = OwnedHolding();
		if (holding == null) {
			return;
		}
		if (holding.key == AIRFIELD && city.owner != holdingOwner && CanUseAirfields(city.owner)) {
			holdingLog.Information("The airfield of {Owner} at {Tile} passes to {NewOwner}", holdingOwner, this, city.owner);
			holdingOwner = city.owner;
			TileChangeJournal.Record(this);
			return;
		}
		if (holding.key == COLONY || city.owner != holdingOwner) {
			holdingLog.Information("The borders of {City} take in the {Holding} of {Owner} at {Tile}", city.name, holding.key, holdingOwner, this);
			overlays.Remove(holding);
		}
	}

	// Whether the player knows the tech the game's airfield job needs
	// (Flight in Civ3), as they must to take over an airfield in their
	// borders. Without such a job, anyone can.
	private static bool CanUseAirfields(Player player) {
		Terraform airfield = TerrainImprovement.ToTerraform(AIRFIELD);
		return airfield?.RequiredTech == null || player.HasTech(airfield.RequiredTech);
	}
}
