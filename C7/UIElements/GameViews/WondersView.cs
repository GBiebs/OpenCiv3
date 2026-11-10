using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData;
using Godot;

// Civ3's Wonders of the World (F7): a box for each great wonder, six to a
// page, with its picture and, once built, who owns it, when it was built
// and where; before that, which civs we know are building it (Civ3's
// "Owned by:", "Constructed in:", "Located in:", "Being Constructed by
// the").
[GlobalClass]
[Tool]
public partial class WondersView : Control {

	[Export] public TextureRect background;

	private TextureButton _close;
	private Control content;
	private int page;

	private const int PerPage = 6;
	private static readonly Vector2 BoxSize = new(370, 200);
	private static readonly Color Ink = new(0.24f, 0.16f, 0.07f);
	private static readonly Color Faded = new(0.42f, 0.33f, 0.2f);

	// Civ3's Civilopedia names of the wonders whose names don't give them
	// ("The Great Lighthouse" is BLDG_Lighthouse); the others are their
	// names without "The", spaces as underscores.
	private static readonly Dictionary<string, string> PediaNames = new() {
		["The Great Lighthouse"] = "Lighthouse",
		["Sun Tzu's Art of War"] = "Art_of_War",
		["Magellan's Voyage"] = "Circumnavigation",
		["Copernicus' Observatory"] = "Solar_System",
		["Shakespeare's Theater"] = "Great_Playhouse",
		["Leonardo's Workshop"] = "Inventor's_Workshop",
		["JS Bach's Cathedral"] = "Grand_Cathedral",
		["Newton's University"] = "Great_University",
		["Smith's Trading Company"] = "Trading_Company",
		["The Temple of Artemis"] = "Artemis",
		["The Statue of Zeus"] = "Zeus",
		["The Mausoleum of Mausollos"] = "Mausoleum",
		["Apollo Program"] = "Apollo_Project",
	};

	private static PediaIcons pediaIcons;
	private static ImageTexture boxTexture;
	private static readonly Dictionary<string, Texture2D> pictures = new();

	public WondersView() {
		MouseFilter = MouseFilterEnum.Stop;
	}

	public override void _Ready() {
		this.CreateUI();
	}

	private void CreateUI() {
		background.Texture = TextureLoader.Load("screens.wonders.background");

		_close = AdvisorUtils.CreateExitButton(background);
		_close.Pressed += () => { this.GetParent<GameViews>().Hide(); };

		AdvisorUtils.CreateAdvisorTitle(background, background.Texture.GetWidth(), "WONDERS OF THE WORLD");

		content = new Control { MouseFilter = MouseFilterEnum.Ignore };
		content.Size = background.Texture.GetSize();
		background.AddChild(content);
	}

	public void ShowView() {
		Show();
		Rebuild();
	}

	private void Rebuild() {
		foreach (Node child in content.GetChildren()) {
			child.QueueFree();
		}
		EngineStorage.ReadGameData((GameData gameData) => {
			Player us = EngineStorage.GetViewer();
			List<Building> wonders = gameData.Buildings.Where(b => b.IsGreatWonder() && b.spaceshipPart == null).ToList();
			int pages = Math.Max(1, (wonders.Count + PerPage - 1) / PerPage);
			page = Math.Clamp(page, 0, pages - 1);
			List<Building> shown = wonders.Skip(page * PerPage).Take(PerPage).ToList();
			for (int i = 0; i < shown.Count; i++) {
				AddBox(gameData, us, shown[i], new Vector2(122 + (i % 2) * 410, 68 + (i / 2) * 213));
			}
			if (pages > 1) {
				AddPager(pages);
			}
		});
	}

	// Civ3's page arrows under the boxes.
	private void AddPager(int pages) {
		Button previous = new() { Text = "<-", Position = new Vector2(440, 708), Size = new Vector2(44, 24), FocusMode = FocusModeEnum.None, TooltipText = "Previous" };
		previous.Pressed += () => {
			page = (page + pages - 1) % pages;
			Rebuild();
		};
		Button next = new() { Text = "->", Position = new Vector2(540, 708), Size = new Vector2(44, 24), FocusMode = FocusModeEnum.None, TooltipText = "Next" };
		next.Pressed += () => {
			page = (page + 1) % pages;
			Rebuild();
		};
		content.AddChild(previous);
		content.AddChild(next);
		AddLabel($"{page + 1} / {pages}", new Rect2(484, 710, 56, 22), 14, HorizontalAlignment.Center);
	}

	private void AddBox(GameData gameData, Player us, Building wonder, Vector2 at) {
		boxTexture ??= PCXToGodot.getImageTextureFromPCX(TextureLoader.LoadPCX("Art/Advisors/wondersBOX.pcx"));
		TextureRect box = new() { Texture = boxTexture, Position = at, Size = BoxSize, MouseFilter = MouseFilterEnum.Ignore };
		content.AddChild(box);

		Texture2D picture = Picture(wonder);
		if (picture != null) {
			Vector2 size = picture.GetSize();
			float scale = Math.Min(128f / size.X, 128f / size.Y);
			TextureRect image = new() {
				Texture = picture,
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
				MouseFilter = MouseFilterEnum.Ignore,
			};
			image.Position = at + new Vector2(25, 36);
			image.Size = new Vector2(128, 128);
			content.AddChild(image);
		}

		AddLabel(wonder.name, new Rect2(at + new Vector2(22, 14), new Vector2(330, 24)), 15);
		Vector2 text = at + new Vector2(170, 52);
		const float width = 180;
		foreach ((string line, bool header) in Lines(gameData, us, wonder)) {
			AddLabel(line, new Rect2(text, new Vector2(width, 40)), header ? 13 : 14, wrap: true, color: header ? Faded : Ink);
			text.Y += header ? 16 : 22;
		}
	}

	// What the box says: who owns it, when and where it was built (as far
	// as we know them), or who we know is building it.
	internal static List<(string line, bool header)> Lines(GameData gameData, Player us, Building wonder) {
		List<(string, bool)> lines = [];
		(City city, CityBuilding built) = gameData.cities
			.SelectMany(c => c.constructed_buildings.Where(cb => cb.building == wonder).Select(cb => (c, cb)))
			.FirstOrDefault();
		if (built != null) {
			Player owner = city.owner;
			bool met = owner == us || us.playerRelationships.ContainsKey(owner.id);
			lines.Add(("Owned by:", true));
			lines.Add((met ? $"The {owner.civilization.noun}" : "A distant civilization", false));
			lines.Add(("Constructed in:", true));
			lines.Add((When(gameData, built), false));
			lines.Add(("Located in:", true));
			lines.Add((met && us.tileKnowledge.isTileKnown(city.location) ? city.name : "Unknown", false));
			return lines;
		}
		if (gameData.GreatWondersBuilt.Contains(wonder.name)) {
			lines.Add(("Lost with the city that held it.", false));
			return lines;
		}
		List<string> building = gameData.players
			.Where(p => !p.isBarbarians && !p.defeated && (p == us || us.playerRelationships.ContainsKey(p.id))
				&& p.cities.Any(c => c.itemBeingProduced == wonder))
			.Select(p => p.civilization.noun)
			.ToList();
		if (building.Count > 0) {
			lines.Add(("Being Constructed by the", true));
			lines.Add((string.Join(", ", building), false));
		} else {
			lines.Add(("Not yet built.", false));
		}
		return lines;
	}

	// The date it was built, as the calendar shows it.
	private static string When(GameData gameData, CityBuilding built) {
		try {
			if (gameData.timeOptions != null && built.turnBuilt >= 0) {
				return gameData.timeOptions.GetDisplayTime(built.turnBuilt);
			}
		} catch (Exception) {
			// (A turn the calendar doesn't know: the year below.)
		}
		return built.year < 0 ? $"{-built.year} BC" : $"{built.year} AD";
	}

	// The wonder's large Civilopedia picture, else its city screen icon.
	private static Texture2D Picture(Building wonder) {
		if (pictures.TryGetValue(wonder.name, out Texture2D cached)) {
			return cached;
		}
		Texture2D picture = null;
		try {
			pediaIcons ??= new PediaIcons(Util.Civ3MediaPath("Text/PediaIcons.txt"));
			string entry = wonder.civilopediaEntry ?? "BLDG_" + (PediaNames.GetValueOrDefault(wonder.name)
				?? (wonder.name.StartsWith("The ") ? wonder.name[4..] : wonder.name).Replace(' ', '_'));
			if (pediaIcons.GetBuildingLargeIcon(entry) is string path) {
				picture = PCXToGodot.getImageTextureFromPCX(TextureLoader.LoadPCX(path.Replace('\\', '/')));
			}
		} catch (Exception) {
			picture = null;
		}
		picture ??= TextureLoader.Load("building_icons.large", wonder, useCache: true);
		pictures[wonder.name] = picture;
		return picture;
	}

	private void AddLabel(string text, Rect2 rect, int fontSize, HorizontalAlignment align = HorizontalAlignment.Left, bool wrap = false, Color? color = null) {
		Label label = new() { Text = text, HorizontalAlignment = align, MouseFilter = MouseFilterEnum.Ignore, ClipText = !wrap };
		label.AddThemeColorOverride("font_color", color ?? Ink);
		label.AddThemeFontSizeOverride("font_size", fontSize);
		if (wrap) {
			// Before the size, so it wraps at it rather than growing.
			label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		}
		label.Position = rect.Position;
		label.Size = rect.Size;
		content.AddChild(label);
	}
}
