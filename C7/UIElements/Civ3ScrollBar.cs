using System;
using C7.Textures;
using Godot;

// Civ3's scroll bar for a list (Art/scroll.pcx): green arrows at the ends,
// the parchment track between them, and a thumb that keeps its size
// (Godot's grows with the page). It drives a ScrollContainer whose own bar
// is hidden; the wheel still scrolls that. Set its Size before adding it.
public partial class Civ3ScrollBar : Control {
	private const int Side = 20;
	private readonly ScrollContainer target;
	private readonly int step;
	private TextureRect track;
	private TextureRect thumb;
	private TextureButton up;
	private TextureButton down;
	private bool dragging;

	public Civ3ScrollBar(ScrollContainer target, int step = 20) {
		this.target = target;
		this.step = step;
		target.VerticalScrollMode = ScrollContainer.ScrollMode.ShowNever;
	}

	public override void _Ready() {
		MouseFilter = MouseFilterEnum.Stop;
		track = new TextureRect {
			Texture = TextureLoader.Load("ui.scroll.track"),
			StretchMode = TextureRect.StretchModeEnum.Tile,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		AddChild(track);
		thumb = new TextureRect { Texture = TextureLoader.Load("ui.scroll.thumb"), MouseFilter = MouseFilterEnum.Ignore };
		AddChild(thumb);
		up = new TextureButton();
		TextureLoader.SetButtonTextures(up, "ui.scroll.up");
		up.Pressed += () => ScrollBy(-step);
		AddChild(up);
		down = new TextureButton();
		TextureLoader.SetButtonTextures(down, "ui.scroll.down");
		down.Pressed += () => ScrollBy(step);
		AddChild(down);
		Resized += Layout;
		Layout();
	}

	private void Layout() {
		if (track == null) {
			return;
		}
		track.Position = new Vector2(0, Side);
		track.Size = new Vector2(Side, Math.Max(0, Size.Y - 2 * Side));
		down.Position = new Vector2(0, Size.Y - Side);
	}

	// How far the list can scroll, in pixels.
	private double Range() {
		VScrollBar bar = target.GetVScrollBar();
		return Math.Max(0, bar.MaxValue - bar.Page);
	}

	private void ScrollBy(double pixels) => target.ScrollVertical = (int)Math.Clamp(target.ScrollVertical + pixels, 0, Range());

	private void ScrollTo(float y) {
		float travel = Math.Max(1, Size.Y - 3 * Side);
		target.ScrollVertical = (int)Math.Round(Math.Clamp((y - Side - Side / 2f) / travel, 0, 1) * Range());
	}

	public override void _Process(double delta) {
		double range = Range();
		float fraction = range > 0 ? (float)(target.ScrollVertical / range) : 0;
		thumb.Position = new Vector2(0, Side + fraction * Math.Max(0, Size.Y - 3 * Side));
	}

	public override void _GuiInput(InputEvent @event) {
		if (@event is InputEventMouseButton button) {
			if (button.ButtonIndex == MouseButton.WheelUp && button.Pressed) {
				ScrollBy(-step);
			} else if (button.ButtonIndex == MouseButton.WheelDown && button.Pressed) {
				ScrollBy(step);
			} else if (button.ButtonIndex == MouseButton.Left) {
				if (!button.Pressed) {
					dragging = false;
				} else if (button.Position.Y >= thumb.Position.Y && button.Position.Y < thumb.Position.Y + Side) {
					dragging = true;
				} else {
					// A click on the track moves a page toward it.
					ScrollBy((button.Position.Y < thumb.Position.Y ? -1 : 1) * target.GetVScrollBar().Page);
				}
			}
			AcceptEvent();
		} else if (@event is InputEventMouseMotion motion && dragging) {
			ScrollTo(motion.Position.Y);
			AcceptEvent();
		}
	}
}
