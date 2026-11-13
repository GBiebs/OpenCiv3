using System;
using Godot;

// A full-screen black curtain over the map, with a column of text, fields
// and buttons in the middle: the hotseat handoff between players' turns,
// and the end of a turn played by email.
public abstract partial class Curtain : ColorRect {
	protected static readonly Color WarningColor = new(1.0f, 0.45f, 0.4f);
	protected static readonly Color TextColor = new(0.95f, 0.93f, 0.88f);

	private VBoxContainer box;

	public override void _Ready() {
		Color = Colors.Black;
		MouseFilter = MouseFilterEnum.Stop;
		// SetAnchorsPreset alone would keep this control's initial 0x0 size;
		// the offsets also need resetting for it to fill the screen.
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

		CenterContainer center = new();
		AddChild(center);
		center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

		box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 16);
		center.AddChild(box);

		Build();
	}

	// Fills the column.
	protected abstract void Build();

	protected Label AddLabel(string text, int fontSize, Color? color = null) {
		Label label = new() {
			Text = text,
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			CustomMinimumSize = new Vector2(640, 0),
		};
		label.AddThemeFontSizeOverride("font_size", fontSize);
		// The game's theme writes in ink, for parchment.
		label.AddThemeColorOverride("font_color", color ?? TextColor);
		box.AddChild(label);
		return label;
	}

	protected LineEdit AddField(bool secret, string text = "", int width = 320) {
		LineEdit field = new() {
			Secret = secret,
			Text = text,
			SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
			CustomMinimumSize = new Vector2(width, 36),
			MaxLength = secret ? 40 : 120,
		};
		field.AddThemeFontSizeOverride("font_size", 18);
		box.AddChild(field);
		return field;
	}

	protected Button AddButton(string text, Action pressed) {
		Button button = new() {
			Text = text,
			SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
			CustomMinimumSize = new Vector2(240, 48),
		};
		button.AddThemeFontSizeOverride("font_size", 20);
		foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) {
			button.AddThemeColorOverride(state, TextColor);
		}
		button.AddThemeColorOverride("font_disabled_color", new Color(0.5f, 0.5f, 0.5f));
		foreach ((string state, float shade) in new[] { ("normal", 0.15f), ("hover", 0.28f), ("pressed", 0.35f), ("focus", 0.15f), ("disabled", 0.1f) }) {
			StyleBoxFlat style = new() {
				BgColor = new Color(shade, shade, shade),
				BorderColor = TextColor,
				ContentMarginLeft = 12,
				ContentMarginRight = 12,
			};
			style.SetBorderWidthAll(state == "focus" ? 2 : 1);
			style.SetCornerRadiusAll(4);
			if (state == "focus") {
				style.DrawCenter = false;
			}
			button.AddThemeStyleboxOverride(state, style);
		}
		button.Pressed += pressed;
		box.AddChild(button);
		return button;
	}

	// Takes the curtain down and goes on, once the key press that pressed
	// the button has been read. Otherwise the key (e.g. Enter) would still
	// read as "just pressed" by the game's input polling once the player's
	// turn has started, and could immediately trigger an action such as
	// ending the turn.
	//
	// This deliberately doesn't use Callable.From(then).CallDeferred(): a
	// callable made from a lambda that isn't bound to a GodotObject has no
	// target object, and Godot silently drops deferred calls without one.
	protected async void CloseAndThen(Button button, Action then) {
		button.Disabled = true;
		button.ReleaseFocus();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		QueueFree();
		then();
	}
}
