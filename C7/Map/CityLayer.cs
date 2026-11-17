using System.Collections.Generic;
using C7GameData;
using Godot;
using Serilog;

namespace C7.Map {
	public class CityLayer : LooseLayer {
		private Dictionary<City, CityScene> citySceneLookup = new();

		// Whether cities show their names (the scenario editor's View > City
		// Names turns them off).
		public bool showLabels {
			get => labelsShown;
			set {
				if (labelsShown != value) {
					labelsShown = value;
					RequestRedraw();
				}
			}
		}
		private bool labelsShown = true;

		// Counts the times the layer was drawn, so scenes for cities that weren't drawn can be hidden.
		private int drawPass = 0;

		public CityLayer() {
			CityScene.ClearTextureCache();
		}

		public void UpdateAfterCityDestruction(City city) {
			EraseCity(city);
		}

		// The city is redrawn in its new owner's colors on the next draw.
		public void UpdateAfterCityCapture(City city) {
			EraseCity(city);
		}

		private void EraseCity(City city) {
			citySceneLookup.Remove(city, out CityScene cityScene);
			cityScene?.QueueFree();
		}

		// Keeps the scenes of cities that are still there with the same owner, pointing them at the new city objects.
		public override void onGameDataReplaced(GameData gameData) {
			Dictionary<ID, City> newCities = new();
			foreach (City city in gameData.cities) {
				newCities[city.id] = city;
			}

			Dictionary<City, CityScene> oldLookup = citySceneLookup;
			citySceneLookup = new();
			foreach ((City oldCity, CityScene scene) in oldLookup) {
				if (newCities.TryGetValue(oldCity.id, out City newCity) && newCity.owner?.id == oldCity.owner?.id) {
					scene.Rebind(newCity);
					citySceneLookup[newCity] = scene;
				} else {
					scene.QueueFree();
				}
			}
		}

		public override void onBeginDraw(LooseView looseView, GameData gameData) {
			++drawPass;
		}

		// A city may be drawn more than once, at each of its copies on a wrapping map, but it has one scene. The scene is shown at the copy
		// nearest the middle of the screen (see PlaceScenes), so it's only set up for the first copy drawn.
		public override void drawObject(LooseView looseView, GameData gameData, Tile tile, Vector2 tileCenter) {
			if (tile.cityAtTile is null) {
				return;
			}

			City city = tile.cityAtTile;
			citySceneLookup.TryGetValue(city, out CityScene scene);
			if (scene != null && scene.lastDrawnPass == drawPass) {
				return;
			}

			// Hide the city while the tile info box covers it.
			if (looseView.IsTileCoveredByTileInfo(tile)) {
				if (scene != null) {
					scene.lastDrawnPass = drawPass;
					scene.SetShown(false);
				}
				return;
			}

			if (scene == null) {
				scene = new CityScene(city);
				looseView.AddChild(scene);
				citySceneLookup[city] = scene;
			}

			scene.lastDrawnPass = drawPass;
			PlaceScene(looseView.mapView, scene, looseView.mapView.CameraCenterInMap());
			scene.SetShown(true);
			// Only the player's own cities show what they're doing; in observer mode there's no player, so every city does.
			bool showDetails = looseView.uiPlayer == null || city.owner == looseView.uiPlayer;
			scene.Refresh(looseView.mapView.contentVersion, showDetails);

			if (!showLabels || looseView.HasCityLabelToHideFromTileInfo(tile))
				scene.HideLabel();
			else
				scene.ShowLabel();
		}

		// Hide the cities that are no longer in view, so they're not processed.
		public override void onEndDraw(LooseView looseView, GameData gameData) {
			foreach (CityScene scene in citySceneLookup.Values) {
				if (scene.lastDrawnPass != drawPass) {
					scene.SetShown(false);
				}
			}
			lastPlacementCenter = looseView.mapView.CameraCenterInMap();
		}

		// Where the middle of the screen was when the scenes were last placed.
		private Vector2 lastPlacementCenter = new(float.NaN, float.NaN);

		// Moves the shown cities to their copies nearest the middle of the screen. The city view isn't redrawn while the camera moves within
		// the region it drew, so the MapView calls this whenever the camera moves or zooms; otherwise, on a wrapping map, a city could stay
		// at a copy that has gone off screen while another copy is on screen.
		public void PlaceScenes(MapView mapView) {
			Vector2 center = mapView.CameraCenterInMap();
			if (center == lastPlacementCenter) {
				return;
			}
			lastPlacementCenter = center;
			if (!mapView.wrapHorizontally && !mapView.wrapVertically) {
				return;
			}
			foreach (CityScene scene in citySceneLookup.Values) {
				if (scene.Visible) {
					PlaceScene(mapView, scene, center);
				}
			}
		}

		private static void PlaceScene(MapView mapView, CityScene scene, Vector2 center) {
			Vector2 tileCenter = mapView.NearestTileCenter(scene.City.location, center);
			scene.SetTileCenter(new Vector2I((int)tileCenter.X, (int)tileCenter.Y));
		}
	}
}
