using C7Engine;

// The settings page's Edge scrolling: whether the map pans while the mouse
// rests at an edge of the window. Off unless the player turns it on, as the
// map only moved by dragging before.
public static class EdgeScrolling {
	private const string Section = "display";
	private const string Key = "edgeScrolling";

	// How close to an edge, in the UI's logical pixels, the mouse must be,
	// and how fast the map pans then, in logical pixels a second. Logical
	// pixels grow with the interface scale, so both do too.
	public const float Margin = 8;
	public const float Speed = 900;

	// Read once from C7.ini, as the map asks every frame.
	private static bool? enabled;

	public static bool Enabled => enabled ??= C7Settings.GetSettingsValueOrDefault(Section, Key, "false") == "true";

	public static void SetEnabled(bool on) {
		enabled = on;
		C7Settings.SetValue(Section, Key, on ? "true" : "false");
		C7Settings.SaveSettings();
	}
}
