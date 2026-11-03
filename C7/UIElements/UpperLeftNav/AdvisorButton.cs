using C7Engine;
using Godot;
using static TemporaryPopup;

// The advisors button opens the domestic advisor; the list down each
// advisor's left border switches to the others (user, 2026-11-03).
[Tool]
public partial class AdvisorButton : Civ3TextureButton {
	[Signal] public delegate void ShowSpecificAdvisorEventHandler(string advisor);

	public override void _Ready() {
		TextureLoader.SetButtonTextures(this, "upper_left_navigation.advisor");
		this.TooltipText = "Advisors";
		this.Theme = GetToolTipTheme();
	}

	public override void _Pressed() {
		this.FocusMode = FocusModeEnum.None;
		EmitSignal(SignalName.ShowSpecificAdvisor, C7Action.ShowDomesticAdvisor);
	}
}
