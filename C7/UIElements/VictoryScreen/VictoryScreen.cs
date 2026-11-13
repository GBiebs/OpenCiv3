using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData;
using ConvertCiv3Media;
using Godot;
using Serilog;

// Civ3's end-of-game screen (see VictoryScreenLayout): the background for
// how the game ended, the leaders' heads with faces to suit, and every few
// seconds one of the other leaders has something to say in a speech
// bubble (Civ3 FUN_005de580, FUN_005de5c0).
//
// With Modern graphics on, and always in standalone mode, it's drawn in our
// own art (ArtGen/screens/victory.py): our backgrounds, with the leaders'
// remade portraits (LeaderHeadTextures.Portrait) in frames that show their
// moods, as the portraits have one face, and our own bubble and buttons.
// The heads go in the same places either way, so the bubbles point at the
// same heads.
//
// A click anywhere, Enter, Space, Escape, the Next button or the close
// button in the corner all go on to the next page (the replay).
public partial class VictoryScreen : Control {
	private static readonly ILogger log = LogManager.ForContext<VictoryScreen>();

	public event Action Closed;

	// Our own art's files that have no Civ3 original.
	private const string OwnArt = "Art/VictoryScreen/";
	private const string HeartBadge = OwnArt + "heart_badge.png";
	private const string ExitButton = OwnArt + "exit.png";
	private const string NextButton = OwnArt + "next.png";

	// The Next button's word, Civ3's own (labels.txt).
	private const string NextLabel = "Next";

	// Clicks and keys this soon after the screen opens are the ones that
	// opened it, not a wish to go on.
	private const ulong InputGraceMsec = 300;

	// A head to draw: its place, its face (one of our portraits or Civ3's),
	// the cultural winner's face to put over it, if any, and what its
	// leader can say.
	private sealed record Head(VictoryScreenHead Place, Texture2D Face, Texture2D Loved, List<string> Remarks);

	private readonly int screen;
	private readonly bool ownArt;
	private readonly Texture2D background;
	private readonly List<Head> heads;
	private readonly Texture2D bubbleTop, bubbleMiddle, bubbleBottom;
	private readonly Texture2D exitNormal, exitHover;

	private Control board;
	private Control bubble;
	private ulong openedAt;
	private ulong bubbleShownAt;
	private ulong nextBubbleAt;
	private bool closed;

	private VictoryScreen(int screen, bool ownArt, Texture2D background, List<Head> heads, Texture2D bubbleTop,
			Texture2D bubbleMiddle, Texture2D bubbleBottom, Texture2D exitNormal, Texture2D exitHover) {
		this.screen = screen;
		this.ownArt = ownArt;
		this.background = background;
		this.heads = heads;
		this.bubbleTop = bubbleTop;
		this.bubbleMiddle = bubbleMiddle;
		this.bubbleBottom = bubbleBottom;
		this.exitNormal = exitNormal;
		this.exitHover = exitHover;
		Name = "VictoryScreen";
		MouseFilter = MouseFilterEnum.Stop;
	}

	// The screen for the player, or null if it can't be built. Reads the
	// game, so call it with the game data at hand.
	public static VictoryScreen TryCreate(GameData gameData, Player player, Player winner, GameEndReason reason,
			Civ3EndGameText text) {
		try {
			return Create(gameData, player, winner, reason, text);
		} catch (Exception e) {
			log.Warning(e, "Couldn't build the victory screen");
			return null;
		}
	}

	// Whether the screen is drawn in our own art.
	public static bool UsesOwnArt => ModernGraphics.Enabled || C7Settings.UseStandaloneMode();

	private static VictoryScreen Create(GameData gameData, Player player, Player winner, GameEndReason reason,
			Civ3EndGameText text) {
		if (player == null || text == null) {
			return null;
		}
		bool ownArt = UsesOwnArt;
		int screen = VictoryScreenLayout.ScreenFor(reason, player == winner);
		string backgroundPath = VictoryScreenLayout.Background(screen);
		Texture2D background = ownArt
			? ModernGraphics.Load(backgroundPath, null) ?? LoadArt(backgroundPath)
			: LoadArt(backgroundPath) ?? ModernGraphics.Load(backgroundPath, null);
		PediaIcons icons = LoadPediaIcons();

		List<Player> civs = gameData.players.Where(p => !p.IsIndependent && p.civilization != null).ToList();
		bool lockedAlliances = civs.Any(p => p.alliance != null);
		bool Alive(Player p) => !p.defeated;
		bool Allied(Player a, Player b) => gameData.AreInLockedPeace(a, b);
		List<VictoryScreenHead> places = VictoryScreenLayout.Heads(screen, civs, player, winner, Alive, lockedAlliances, Allied);

		// The winner's small "loved" face, drawn over the rivals' hearts on
		// the cultural victory screen: in our art, their portrait.
		Texture2D loved = null;
		if (screen == VictoryScreenLayout.CulturalScreen && winner != null) {
			loved = ownArt
				? LeaderHeadTextures.Portrait(winner.civilization) ?? LoadFace(icons, VictoryScreenLayout.Loved, winner)
				: LoadFace(icons, VictoryScreenLayout.Loved, winner);
		}

		List<Head> heads = [];
		foreach (VictoryScreenHead place in places) {
			// Our portrait, or Civ3's face where there's none (as for a
			// scenario's own leaders), or the other way round.
			Texture2D face = ownArt
				? LeaderHeadTextures.Portrait(place.Player.civilization) ?? LoadFace(icons, place.Mood, place.Player)
				: LoadFace(icons, place.Mood, place.Player) ?? LeaderHeadTextures.Portrait(place.Player.civilization);
			if (face == null) {
				// Civ3 complains of a missing PediaIcons entry; we just
				// leave the head out.
				log.Information("No victory screen face for {Civ}", place.Player.civilization?.name);
				continue;
			}
			Player speaker = place.Player;
			// Who destroyed whom isn't kept, so the remarks that depend on
			// it are only the ones for civs that never met the player.
			List<string> remarks = place.Slot == 0 ? [] : VictoryScreenLayout.Remarks(screen, player, winner, speaker,
					Alive(speaker), lockedAlliances, winner != null && Allied(speaker, winner),
					speakerDestroyer: null, playerDestroyer: null, metPlayer: player.playerRelationships.ContainsKey(speaker.id))
				.Select(text.Label).Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
			bool showLoved = loved != null && VictoryScreenLayout.ShowsLovedFace(screen, place, Alive(speaker));
			heads.Add(new Head(place, face, showLoved ? loved : null, remarks));
		}

		Texture2D exitNormal, exitHover, top, middle, bottom;
		if (ownArt) {
			exitNormal = ModernGraphics.Load(ExitButton, new CropRegion(1, 1, 36, 38));
			exitHover = ModernGraphics.Load(ExitButton, new CropRegion(38, 1, 36, 38));
			top = ModernGraphics.Load(VictoryScreenLayout.BubbleTop, null);
			middle = ModernGraphics.Load(VictoryScreenLayout.BubbleMiddle, null);
			bottom = ModernGraphics.Load(VictoryScreenLayout.BubbleBottom, null);
		} else {
			exitNormal = LoadArt(VictoryScreenLayout.ExitButtonArt, new CropRegion(1, 1, 36, 38));
			exitHover = LoadArt(VictoryScreenLayout.ExitButtonArt, new CropRegion(38, 1, 36, 38));
			top = LoadArt(VictoryScreenLayout.BubbleTop);
			middle = LoadArt(VictoryScreenLayout.BubbleMiddle);
			bottom = LoadArt(VictoryScreenLayout.BubbleBottom);
		}
		return new VictoryScreen(screen, ownArt, background, heads, top, middle, bottom, exitNormal, exitHover);
	}

	private static string pediaIconsPath;
	private static PediaIcons pediaIcons;

	private static PediaIcons LoadPediaIcons() {
		if (C7Settings.UseStandaloneMode() || !Util.HasCiv3Media("Text/PediaIcons.txt")) {
			return null;
		}
		string path = Util.Civ3MediaPath("Text/PediaIcons.txt");
		if (path != pediaIconsPath) {
			pediaIcons = new PediaIcons(path);
			pediaIconsPath = path;
		}
		return pediaIcons;
	}

	// A leader's Civ3 victory-screen face, like HAPPY_RACE_ROMANS in
	// PediaIcons.txt, or null without it.
	private static ImageTexture LoadFace(PediaIcons icons, string mood, Player player) {
		if (icons == null) {
			return null;
		}
		foreach (string race in VictoryScreenLayout.RaceKeys(player.civilization)) {
			IReadOnlyList<string> block = icons.GetBlock(mood + race);
			if (block is { Count: > 0 }) {
				return LoadArt(block[0]);
			}
		}
		return null;
	}

	// Civ3 art from the install, or null if it isn't there. Palette
	// entries 254 and 255 are see-through, as in all of Civ3's art (the
	// loved faces' black surround, the bubble's and button's magenta).
	private static ImageTexture LoadArt(string path, CropRegion? crop = null) {
		if (C7Settings.UseStandaloneMode() || !Util.HasCiv3Media(path)) {
			return null;
		}
		Pcx pcx = TextureLoader.LoadPCX(path);
		PCXToGodot.ColorOptions options = new(shadows: false, transparentColorIndexes: [254, 255]);
		return crop is CropRegion region
			? PCXToGodot.getImageTextureFromPCX(pcx, region, options)
			: PCXToGodot.getImageTextureFromPCX(pcx, options);
	}

	public override void _Ready() {
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		openedAt = Time.GetTicksMsec();

		// Civ3 draws the screen's 1024x768 in the middle of a black one.
		ColorRect black = new() { Color = Colors.Black, MouseFilter = MouseFilterEnum.Ignore };
		black.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(black);
		CenterContainer center = new() { MouseFilter = MouseFilterEnum.Ignore };
		center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(center);
		board = new Control {
			CustomMinimumSize = new Vector2(VictoryScreenLayout.Width, VictoryScreenLayout.Height),
			MouseFilter = MouseFilterEnum.Ignore,
		};
		center.AddChild(board);

		if (background != null) {
			board.AddChild(new TextureRect {
				Texture = background,
				TextureFilter = TextureFilterEnum.Linear,
				MouseFilter = MouseFilterEnum.Ignore,
			});
		} else {
			// Without any art, a dark field.
			board.AddChild(new ColorRect {
				Color = Color.Color8(28, 30, 36),
				Size = new Vector2(VictoryScreenLayout.Width, VictoryScreenLayout.Height),
				MouseFilter = MouseFilterEnum.Ignore,
			});
		}
		foreach (Head head in heads) {
			AddHead(head);
		}

		AddExitButton();
		AddNextButton();

		nextBubbleAt = Time.GetTicksMsec() + VictoryScreenLayout.FirstBubbleDelay + Jitter();
	}

	private void AddHead(Head head) {
		(int x, int y) = VictoryScreenLayout.Place(screen, head.Place.Slot);
		if (!ownArt) {
			board.AddChild(new TextureRect { Texture = head.Face, Position = new Vector2(x, y), MouseFilter = MouseFilterEnum.Ignore });
			if (head.Loved != null) {
				board.AddChild(new TextureRect { Texture = head.Loved, Position = new Vector2(x, y), MouseFilter = MouseFilterEnum.Ignore });
			}
			return;
		}

		// Our art: the face fills the place (a square portrait cropped at
		// its sides), tinted for the mood, in a frame for the mood.
		bool large = head.Place.Slot == 0;
		Vector2 size = large ? new Vector2(200, 240) : new Vector2(120, 150);
		VictoryScreenLayout.FrameStyle style = VictoryScreenLayout.FrameFor(head.Place.Mood);
		Control place = new() { Position = new Vector2(x, y), Size = size, ClipContents = true, MouseFilter = MouseFilterEnum.Ignore };
		board.AddChild(place);
		place.AddChild(Picture(head.Face, size, MoodMaterial(style)));
		string frameName = $"{OwnArt}frame_{(large ? "large" : "small")}_{style.ToString().ToLowerInvariant()}.png";
		if (ModernGraphics.Load(frameName, null) is Texture2D frame) {
			place.AddChild(Picture(frame, size));
		}
		if (head.Loved != null) {
			// The winner's face in a heart in the rival's lower right
			// corner, as Civ3 draws it inside a heart over the rival.
			Vector2 badgeAt = new(x + size.X - 60, y + size.Y - 54);
			Control window = new() { Position = badgeAt + new Vector2(14, 8), Size = new Vector2(36, 36), ClipContents = true, MouseFilter = MouseFilterEnum.Ignore };
			window.AddChild(Picture(head.Loved, new Vector2(36, 36)));
			board.AddChild(window);
			if (ModernGraphics.Load(HeartBadge, null) is Texture2D badge) {
				board.AddChild(new TextureRect { Texture = badge, Position = badgeAt, TextureFilter = TextureFilterEnum.Linear, MouseFilter = MouseFilterEnum.Ignore });
			}
		}
	}

	// A picture filling a box, cropped to its shape, smoothly scaled.
	private static TextureRect Picture(Texture2D texture, Vector2 size, Material material = null) => new() {
		// Free to size before it's sized, or it keeps the texture's size.
		ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
		StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
		Texture = texture,
		Size = size,
		TextureFilter = TextureFilterEnum.Linear,
		Material = material,
		MouseFilter = MouseFilterEnum.Ignore,
	};

	// The portraits have one face, so the mood tints them a little: the
	// beaten are drained of color, the angry flushed, the loving warmed.
	private static readonly Dictionary<VictoryScreenLayout.FrameStyle, ShaderMaterial> moodMaterials = [];

	private static ShaderMaterial MoodMaterial(VictoryScreenLayout.FrameStyle style) {
		if (style == VictoryScreenLayout.FrameStyle.Glory) {
			return null;
		}
		if (moodMaterials.TryGetValue(style, out ShaderMaterial material) && IsInstanceValid(material)) {
			return material;
		}
		(float gray, Color tint) = style switch {
			VictoryScreenLayout.FrameStyle.Hurt => (0.75f, new Color(0.8f, 0.78f, 0.78f)),
			VictoryScreenLayout.FrameStyle.Angry => (0.15f, new Color(1.0f, 0.8f, 0.76f)),
			_ => (0.0f, new Color(1.0f, 0.93f, 0.95f)),
		};
		Shader shader = new() {
			Code = """
				shader_type canvas_item;
				uniform float gray;
				uniform vec4 tint : source_color;
				void fragment() {
					vec4 c = texture(TEXTURE, UV);
					float l = dot(c.rgb, vec3(0.299, 0.587, 0.114));
					COLOR = vec4(mix(c.rgb, vec3(l), gray) * tint.rgb, c.a);
				}
				""",
		};
		material = new ShaderMaterial { Shader = shader };
		material.SetShaderParameter("gray", gray);
		material.SetShaderParameter("tint", tint);
		moodMaterials[style] = material;
		return material;
	}

	private void AddExitButton() {
		TextureButton exit = new() {
			Position = new Vector2(VictoryScreenLayout.ExitButtonX, VictoryScreenLayout.ExitButtonY),
			CustomMinimumSize = new Vector2(VictoryScreenLayout.ExitButtonSize, VictoryScreenLayout.ExitButtonSize),
			FocusMode = FocusModeEnum.None,
			TextureFilter = TextureFilterEnum.Linear,
		};
		if (exitNormal != null) {
			exit.TextureNormal = exitNormal;
			exit.TextureHover = exitHover ?? exitNormal;
			exit.TexturePressed = exitHover ?? exitNormal;
		} else {
			TextureLoader.SetButtonTextures(exit, "ui.cancel");
		}
		exit.Pressed += Close;
		board.AddChild(exit);
	}

	// The Next button in the lower right corner, so it's plain that the
	// screen goes on: our art's, or on Civ3's a dark one edged in gold.
	private void AddNextButton() {
		Texture2D normal = ownArt ? ModernGraphics.Load(NextButton, new CropRegion(0, 0, 128, 36)) : null;
		Texture2D hover = ownArt ? ModernGraphics.Load(NextButton, new CropRegion(0, 36, 128, 36)) : null;
		Button next = new() {
			Name = "Next",
			Text = NextLabel,
			Position = new Vector2(VictoryScreenLayout.Width - 128 - 18, VictoryScreenLayout.Height - 36 - 16),
			Size = new Vector2(128, 36),
			FocusMode = FocusModeEnum.None,
		};
		next.AddThemeFontSizeOverride("font_size", 17);
		if (normal != null) {
			StyleBoxTexture Box(Texture2D t) => new() { Texture = t, ContentMarginRight = 20 };
			next.AddThemeStyleboxOverride("normal", Box(normal));
			next.AddThemeStyleboxOverride("hover", Box(hover ?? normal));
			next.AddThemeStyleboxOverride("pressed", Box(hover ?? normal));
			next.AddThemeColorOverride("font_outline_color", Color.Color8(40, 28, 12));
			next.AddThemeConstantOverride("outline_size", 4);
		} else {
			StyleBoxFlat Box(Color fill) {
				StyleBoxFlat box = new() { BgColor = fill, BorderColor = Color.Color8(196, 164, 92) };
				box.SetBorderWidthAll(2);
				box.SetCornerRadiusAll(6);
				return box;
			}
			next.Text = NextLabel + "  ▸";
			next.AddThemeStyleboxOverride("normal", Box(new Color(0.08f, 0.06f, 0.04f, 0.82f)));
			next.AddThemeStyleboxOverride("hover", Box(new Color(0.24f, 0.18f, 0.08f, 0.9f)));
			next.AddThemeStyleboxOverride("pressed", Box(new Color(0.24f, 0.18f, 0.08f, 0.9f)));
		}
		foreach (string color in new[] { "font_color", "font_hover_color", "font_pressed_color" }) {
			next.AddThemeColorOverride(color, Color.Color8(250, 240, 214));
		}
		next.Pressed += Close;
		board.AddChild(next);
	}

	private static ulong Jitter() => (ulong)(Random.Shared.Next(2) * VictoryScreenLayout.BubbleJitter);

	public override void _Process(double delta) {
		ulong now = Time.GetTicksMsec();
		if (now >= nextBubbleAt) {
			ShowNextRemark(now);
		}
		if (bubble != null) {
			ulong shown = now - bubbleShownAt;
			if (shown >= VictoryScreenLayout.BubbleFadeEnd) {
				bubble.Visible = false;
			} else if (shown > VictoryScreenLayout.BubbleShown) {
				float fade = (shown - VictoryScreenLayout.BubbleShown) / (float)(VictoryScreenLayout.BubbleFadeEnd - VictoryScreenLayout.BubbleShown);
				bubble.Modulate = new Color(1, 1, 1, 1 - fade);
			}
		}
	}

	// One of the other leaders, at random, says one of their lines.
	private void ShowNextRemark(ulong now) {
		nextBubbleAt = now + VictoryScreenLayout.BubbleInterval + Jitter();
		bubble?.QueueFree();
		bubble = null;

		List<Head> speakers = heads.Where(h => h.Place.Slot > 0 && h.Remarks.Count > 0).ToList();
		if (speakers.Count == 0) {
			return;
		}
		Head speaker = speakers[Random.Shared.Next(speakers.Count)];
		string remark = speaker.Remarks[Random.Shared.Next(speaker.Remarks.Count)];
		// In the tree before it's filled in, so its text is measured in the
		// theme's font.
		bubble = new Control { MouseFilter = MouseFilterEnum.Ignore };
		board.AddChild(bubble);
		FillBubble(bubble, remark, VictoryScreenLayout.Place(screen, speaker.Place.Slot));
		bubbleShownAt = now;
	}

	// The bubble is centered on the speaker's head's top left corner, kept
	// on the screen (FUN_005dd960).
	private void FillBubble(Control box, string remark, (int x, int y) place) {
		const int width = 200;
		const int fallbackLine = 16;

		int inset = VictoryScreenLayout.BubbleTextInset;
		Label label = new() {
			Text = remark,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			Position = new Vector2(inset, inset),
			Size = new Vector2(width - 2 * inset, 0),
			MouseFilter = MouseFilterEnum.Ignore,
		};
		label.AddThemeColorOverride("font_color", ownArt ? Color.Color8(52, 38, 24) : Colors.Black);
		label.AddThemeFontSizeOverride("font_size", 13);
		box.AddChild(label);
		int textHeight = Math.Max(1, label.GetLineCount()) * label.GetLineHeight();

		int topHeight = bubbleTop?.GetHeight() ?? inset;
		int middleHeight = bubbleMiddle?.GetHeight() ?? fallbackLine;
		int bottomHeight = bubbleBottom?.GetHeight() ?? inset;
		int middles = Math.Max(1, (textHeight + inset - topHeight + middleHeight - 1) / middleHeight);
		int height = topHeight + middles * middleHeight + bottomHeight;

		if (bubbleTop != null && bubbleMiddle != null && bubbleBottom != null) {
			TextureRect Piece(Texture2D texture, int y) => new() {
				Texture = texture,
				Position = new Vector2(0, y),
				TextureFilter = TextureFilterEnum.Linear,
				MouseFilter = MouseFilterEnum.Ignore,
			};
			box.AddChild(Piece(bubbleTop, 0));
			for (int i = 0; i < middles; i++) {
				box.AddChild(Piece(bubbleMiddle, topHeight + i * middleHeight));
			}
			box.AddChild(Piece(bubbleBottom, topHeight + middles * middleHeight));
		} else {
			// Without any bubble art, a plain rounded box.
			Panel panel = new() { Size = new Vector2(width, height - bottomHeight / 2), MouseFilter = MouseFilterEnum.Ignore };
			StyleBoxFlat style = new() { BgColor = Color.Color8(233, 224, 198), BorderColor = Color.Color8(120, 100, 80) };
			style.SetBorderWidthAll(2);
			style.SetCornerRadiusAll(10);
			panel.AddThemeStyleboxOverride("panel", style);
			box.AddChild(panel);
		}
		box.MoveChild(label, -1);

		int x = place.x - width / 2;
		int y = Math.Max(0, place.y - height / 2);
		box.Position = new Vector2(x, y);
	}

	private bool TakesInput => !closed && Time.GetTicksMsec() - openedAt >= InputGraceMsec;

	// A click anywhere goes on.
	public override void _GuiInput(InputEvent @event) {
		if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) {
			AcceptEvent();
			if (TakesInput) {
				Close();
			}
		}
	}

	public override void _Input(InputEvent @event) {
		if (closed || @event is not InputEventKey { Pressed: true } key) {
			return;
		}
		// The screen takes every key, so none reach the map underneath.
		GetViewport().SetInputAsHandled();
		if (key.Keycode is Key.Escape or Key.Enter or Key.KpEnter or Key.Space && TakesInput) {
			Close();
		}
	}

	private void Close() {
		if (closed) {
			return;
		}
		closed = true;
		// The next page comes up once this frame is done with, so the key
		// that closed this doesn't also act on it.
		Action whenClosed = Closed;
		Callable.From(() => whenClosed?.Invoke()).CallDeferred();
		QueueFree();
	}
}
