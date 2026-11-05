using System;
using System.Collections.Generic;
using Godot;

// Online, over the map: what the players say and the game's notices (who
// left), the latest at the bottom, each fading after a while; and, opened
// with ~ (Civ3's key), a line to say something (to the players whose chat
// balloon is lit in the player list).
public partial class GameChat : Control {
	private const int MaxLines = 8;
	private const double ShownSeconds = 15;
	private const double FadeSeconds = 2;
	private const int Width = 520;

	private readonly VBoxContainer lines = new() { Position = new Vector2(10, 60) };
	private readonly LineEdit input = new() { MaxLength = 200, Visible = false, PlaceholderText = "Say something (Enter), or Escape: those with a lit balloon see it" };
	private readonly List<(Label label, double shownAt)> shown = new();
	private double now;

	// Something typed and sent.
	public event Action<string> Said;

	private ulong closedFrame;

	private bool Typing => input.Visible;

	// Whether the map's keys should wait: a line is being typed, or was
	// just sent or closed (its Enter or Escape is still pressed for the
	// game's actions: Enter would end the turn).
	public bool Busy => Typing || Engine.GetProcessFrames() - closedFrame <= 1;

	public override void _Ready() {
		MouseFilter = MouseFilterEnum.Ignore;
		SetAnchorsPreset(LayoutPreset.FullRect);
		lines.MouseFilter = MouseFilterEnum.Ignore;
		lines.AddThemeConstantOverride("separation", 2);
		AddChild(lines);

		StyleBoxFlat box = new() {
			BgColor = new Color(0, 0, 0, 0.6f),
			ContentMarginLeft = 6,
			ContentMarginRight = 6,
			ContentMarginTop = 3,
			ContentMarginBottom = 3,
		};
		input.AddThemeStyleboxOverride("normal", box);
		input.AddThemeStyleboxOverride("focus", box);
		input.AddThemeColorOverride("font_color", Colors.White);
		input.AddThemeFontSizeOverride("font_size", 14);
		input.Size = new Vector2(Width, 28);
		input.TextSubmitted += Submit;
		AddChild(input);
		PlaceInput();
	}

	public void Add(string text) {
		Label label = new() {
			Text = text,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			CustomMinimumSize = new Vector2(Width, 0),
			MouseFilter = MouseFilterEnum.Ignore,
		};
		// (Readable on bright land too.)
		label.AddThemeStyleboxOverride("normal", new StyleBoxFlat {
			BgColor = new Color(0, 0, 0, 0.45f),
			ContentMarginLeft = 6,
			ContentMarginRight = 6,
			ContentMarginTop = 1,
			ContentMarginBottom = 1,
		});
		label.AddThemeColorOverride("font_color", Colors.White);
		label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.9f));
		label.AddThemeConstantOverride("shadow_offset_x", 1);
		label.AddThemeConstantOverride("shadow_offset_y", 1);
		label.AddThemeFontSizeOverride("font_size", 14);
		lines.AddChild(label);
		shown.Add((label, now));
		while (shown.Count > MaxLines) {
			shown[0].label.QueueFree();
			shown.RemoveAt(0);
		}
		CallDeferred(nameof(PlaceInput));
	}

	public void Open() {
		input.Text = "";
		input.Visible = true;
		input.GrabFocus();
		PlaceInput();
	}

	public void Close() {
		closedFrame = Engine.GetProcessFrames();
		input.Visible = false;
		input.ReleaseFocus();
	}

	private void Submit(string text) {
		text = text.Trim();
		Close();
		if (text.Length > 0) {
			Said?.Invoke(text);
		}
	}

	// (Below the lines.)
	private void PlaceInput() {
		input.Position = new Vector2(10, lines.Position.Y + lines.Size.Y + 4);
	}

	public override void _Process(double delta) {
		now += delta;
		// While typing, every line stays.
		foreach ((Label label, double shownAt) in shown) {
			double age = now - shownAt;
			label.Modulate = new Color(1, 1, 1, Typing ? 1 : (float)Math.Clamp((ShownSeconds + FadeSeconds - age) / FadeSeconds, 0, 1));
		}
	}

	public override void _Input(InputEvent @event) {
		if (Typing && @event is InputEventKey { Pressed: true, Keycode: Key.Escape }) {
			Close();
			GetViewport().SetInputAsHandled();
		}
	}
}
