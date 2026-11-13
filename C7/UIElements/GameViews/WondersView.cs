using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using Godot;

// Civ3's Wonders of the World (F7): a box for each great wonder, six to a
// page, with its name, its picture and, with labels.txt's words, who owns
// it, when it was built and where ("Owned by:", "Constructed in:",
// "Located in:"), or before that who is building it ("Being Constructed by
// the").
//
// The boxes are Civ3's wondersBOX.pcx on its wonders_background.pcx, and
// the pictures the wonders' large Civilopedia icons. With Modern graphics
// on, or in standalone mode, they're our own: the remade background and box
// (ArtGen/screens/wonders.py) and our Civilopedia's icons.
[GlobalClass]
[Tool]
public partial class WondersView : Control {

	[Export] public TextureRect background;

	private TextureButton _close;
	private Control content;
	private TextureButton previousPage, nextPage;
	private Label pageLabel;
	private int page;

	private const int PerPage = 6;
	// Where the boxes go, two columns of three on the page.
	private static readonly Vector2 FirstBox = new(122, 75);
	private static readonly Vector2 BoxStep = new(410, 210);
	// Inside a box (see ArtGen/screens/wonders.py for its layout).
	private static readonly Rect2 NameRect = new(18, 11, 334, 26);
	private static readonly Rect2 PictureRect = new(19, 50, 128, 128);
	private static readonly Rect2 TextRect = new(170, 50, 178, 130);

	private static readonly Color Ink = new(0.2f, 0.14f, 0.07f);
	private static readonly Color Heading = new(0.45f, 0.34f, 0.18f);

	private Texture2D boxTexture;
	// The pictures by wonder, and the PediaIcons.txt they come from by path.
	private static readonly Dictionary<string, Texture2D> pictures = new();
	private static readonly Dictionary<string, PediaIcons> pediaIcons = new();
	private static bool? picturesWereOurs;

	public WondersView() {
		MouseFilter = MouseFilterEnum.Stop;
	}

	public override void _Ready() {
		this.CreateUI();
	}

	private void CreateUI() {
		background.Texture = TextureLoader.Load("screens.wonders.background");
		boxTexture = TextureLoader.Load("screens.wonders.box");

		content = new Control { MouseFilter = MouseFilterEnum.Ignore, Size = background.Texture.GetSize() };
		background.AddChild(content);

		_close = AdvisorUtils.CreateExitButton(background);
		_close.Pressed += () => { this.GetParent<GameViews>().Hide(); };

		AdvisorUtils.CreateAdvisorTitle(background, background.Texture.GetWidth(), "WONDERS OF THE WORLD");

		previousPage = PageButton("Previous", "advisors.science.navigation.arrow_previous", new Vector2(512 - 128 - 100, 720), new Vector2(-44, 13), -1);
		nextPage = PageButton("Next", "advisors.science.navigation.arrow_next", new Vector2(512 + 100, 720), new Vector2(129, 13), 1);
		pageLabel = new Label {
			Position = new Vector2(512 - 60, 726),
			Size = new Vector2(120, 22),
			HorizontalAlignment = HorizontalAlignment.Center,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		background.AddChild(pageLabel);
	}

	// Civ3's advisors' Previous and Next (labels.txt's SOREN_ADVISOR_BUTTONS),
	// on the science advisor's buttons.
	private TextureButton PageButton(string text, string arrow, Vector2 position, Vector2 arrowPosition, int step) {
		TextureButton button = new() { FocusMode = FocusModeEnum.None };
		TextureLoader.SetButtonTextures(button, "advisors.science.navigation.button");
		button.SetPosition(position);
		background.AddChild(button);
		button.Pressed += () => {
			page += step;
			Redraw();
		};
		TextureRect arrowRect = new() { Texture = TextureLoader.Load(arrow), MouseFilter = MouseFilterEnum.Ignore };
		button.AddChild(arrowRect);
		arrowRect.SetPosition(arrowPosition);
		Label label = new() { MouseFilter = MouseFilterEnum.Ignore };
		button.AddChild(label);
		label.SetTextAndCenterLabel(text);
		label.Position += new Vector2(0, 7);
		return button;
	}

	public void ShowView() {
		Show();
		Redraw();
	}

	private void Redraw() {
		// Take the old boxes out now: freeing is deferred, and they would
		// otherwise still be drawn with the new ones until then.
		foreach (Node child in content.GetChildren()) {
			content.RemoveChild(child);
			child.QueueFree();
		}
		EngineStorage.ReadGameData(DrawWonders);
	}

	private void DrawWonders(GameData gameData) {
		Player viewer = gameData.GetUIControllerPlayer();
		List<Building> wonders = gameData.Buildings.Where(b => b.IsGreatWonder()).ToList();
		int pages = Math.Max(1, (wonders.Count + PerPage - 1) / PerPage);
		page = Math.Clamp(page, 0, pages - 1);
		previousPage.Visible = nextPage.Visible = pageLabel.Visible = pages > 1;
		previousPage.Disabled = page == 0;
		nextPage.Disabled = page == pages - 1;
		pageLabel.Text = $"{page + 1} / {pages}";

		Dictionary<Building, (City city, CityBuilding built)> builtIn = new();
		Dictionary<IProducible, List<City>> buildingCities = new();
		foreach (City city in gameData.cities) {
			foreach (CityBuilding cityBuilding in city.GetBuildings()) {
				if (cityBuilding.building.IsGreatWonder()) {
					builtIn[cityBuilding.building] = (city, cityBuilding);
				}
			}
			if (city.itemBeingProduced != null) {
				if (!buildingCities.TryGetValue(city.itemBeingProduced, out List<City> list)) {
					list = new();
					buildingCities[city.itemBeingProduced] = list;
				}
				list.Add(city);
			}
		}

		List<Building> shown = wonders.Skip(page * PerPage).Take(PerPage).ToList();
		for (int i = 0; i < shown.Count; i++) {
			Vector2 at = FirstBox + new Vector2(i % 2 * BoxStep.X, i / 2 * BoxStep.Y);
			AddBox(at, shown[i], Lines(gameData, shown[i], builtIn, buildingCities), viewer);
		}
	}

	// What a box says: who owns the wonder, when and where it was built, or
	// who is building it.
	private static List<(string text, bool heading)> Lines(GameData gameData, Building wonder,
			Dictionary<Building, (City city, CityBuilding built)> builtIn, Dictionary<IProducible, List<City>> buildingCities) {
		if (builtIn.TryGetValue(wonder, out (City city, CityBuilding built) where)) {
			return [
				("Owned by:", true), (AdvisorUtils.CivTitle(where.city.owner), false),
				("Constructed in:", true), (When(gameData, where.built.year), false),
				("Located in:", true), (where.city.name, false),
			];
		}
		if (gameData.hostFacts?.wonders.Find(w => w.wonder == wonder.name) is WonderFacts facts) {
			// On a LAN guest's machine, in a city it isn't sent.
			Player owner = gameData.players.Find(p => p.id.ToString() == facts.owner);
			return [("Owned by:", true), (AdvisorUtils.CivTitle(owner), false), ("Located in:", true), (facts.city, false)];
		}
		if (gameData.GreatWondersBuilt.Contains(wonder.name)) {
			return [("Lost with the city that held it.", false)];
		}
		if (buildingCities.TryGetValue(wonder, out List<City> cities)) {
			IEnumerable<string> civs = cities.Select(c => c.owner).Distinct().Select(p => p.civilization?.noun ?? AdvisorUtils.CivTitle(p));
			return [("Being Constructed by the", true), (string.Join(", ", civs), false)];
		}
		return [("Not yet built.", false)];
	}

	// A year as the game's calendar shows it.
	private static string When(GameData gameData, int year) {
		try {
			if (gameData.timeOptions != null) {
				return gameData.timeOptions.GetDisplayTimeFromRaw(year);
			}
		} catch (Exception) {
			// (Shown as a plain year below.)
		}
		return year < 0 ? $"{-year} BC" : $"{year} AD";
	}

	private void AddBox(Vector2 at, Building wonder, List<(string text, bool heading)> lines, Player viewer) {
		content.AddChild(new TextureRect { Texture = boxTexture, Position = at, MouseFilter = MouseFilterEnum.Ignore });

		if (Picture(wonder, viewer) is Texture2D picture) {
			content.AddChild(new TextureRect {
				Texture = picture,
				Position = at + PictureRect.Position,
				Size = PictureRect.Size,
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
				TextureFilter = TextureFilterEnum.Linear,
				MouseFilter = MouseFilterEnum.Ignore,
			});
		}

		AddLabel(wonder.name, new Rect2(at + NameRect.Position, NameRect.Size), 17, Ink, wrap: false);
		Vector2 position = at + TextRect.Position;
		foreach ((string text, bool heading) in lines) {
			Label label = AddLabel(text, new Rect2(position, new Vector2(TextRect.Size.X, 0)), heading ? 13 : 15, heading ? Heading : Ink, wrap: true);
			position.Y += heading ? 17 : Math.Max(24, label.GetLineCount() * 20 + 4);
		}
	}

	private Label AddLabel(string text, Rect2 rect, int fontSize, Color color, bool wrap) {
		Label label = new() { Text = text, MouseFilter = MouseFilterEnum.Ignore, ClipText = !wrap };
		label.AddThemeColorOverride("font_color", color);
		label.AddThemeFontSizeOverride("font_size", fontSize);
		if (wrap) {
			// Before the size, so it wraps at it rather than growing.
			label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		} else {
			label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		}
		label.Position = rect.Position;
		label.Size = rect.Size;
		content.AddChild(label);
		return label;
	}

	// The wonder's large Civilopedia picture: Civ3's, or with Modern
	// graphics on or in standalone mode, our Civilopedia's. Else the city
	// screen's picture of it.
	private static Texture2D Picture(Building wonder, Player viewer) {
		bool ours = ModernGraphics.Enabled || C7Settings.UseStandaloneMode();
		if (picturesWereOurs != ours) {
			pictures.Clear();
			picturesWereOurs = ours;
		}
		if (pictures.TryGetValue(wonder.name, out Texture2D cached)) {
			return cached;
		}
		Texture2D picture = null;
		try {
			string entry = wonder.civilopediaEntry;
			int era = viewer?.EraIndex() ?? 0;
			int culture = viewer?.civilization?.cultureGroup?.index ?? 0;
			if (ours) {
				PediaIcons icons = LoadIcons(Util.FileExistsIgnoringCase(CivilopediaArt.OurFolder, "Text/PediaIcons.txt"));
				string path = icons?.GetCivilopediaIcon(entry, true, era, culture);
				string file = path == null ? null : Util.FileExistsIgnoringCase(CivilopediaArt.OurFolder, path.Replace('\\', '/'));
				if (file != null) {
					picture = ImageTexture.CreateFromImage(Image.LoadFromFile(file));
				}
			} else if (Util.HasCiv3Media("Text/PediaIcons.txt")) {
				PediaIcons icons = LoadIcons(Util.Civ3MediaPath("Text/PediaIcons.txt"));
				picture = CivilopediaArt.Load(icons?.GetCivilopediaIcon(entry, true, era, culture));
			}
		} catch (Exception) {
			picture = null;
		}
		picture ??= TextureLoader.Load("building_icons.large", wonder, useCache: true);
		pictures[wonder.name] = picture;
		return picture;
	}

	private static PediaIcons LoadIcons(string path) {
		if (path == null) {
			return null;
		}
		if (!pediaIcons.TryGetValue(path, out PediaIcons icons)) {
			icons = new PediaIcons(path);
			pediaIcons[path] = icons;
		}
		return icons;
	}
}
