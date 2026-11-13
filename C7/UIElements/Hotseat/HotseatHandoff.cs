using System;
using C7Engine;
using C7GameData;
using Godot;

// A full-screen curtain shown between human players' turns in a hotseat
// game, and at the start of a turn played by email. It hides the map so the
// next player doesn't see the previous player's view, and waits for them to
// say they're at the screen. A player with a password gives it first; at
// the start of their first turn a player may choose one (Civ3's faction
// password, in its words).
public partial class HotseatHandoff : Curtain {
	private readonly string title;
	private readonly string message;
	private readonly string buttonText;
	private readonly Action onContinue;
	// Whose password is asked for, if anyone's.
	private readonly Player player;
	private readonly bool mayChoosePassword;
	private readonly Action onMainMenu;

	private LineEdit password;
	private LineEdit verify;
	private Label problem;
	private Button button;

	public HotseatHandoff(string title, string message, string buttonText, Action onContinue,
		Player player = null, bool mayChoosePassword = false, Action onMainMenu = null) {
		this.title = title;
		this.message = message;
		this.buttonText = buttonText;
		this.onContinue = onContinue;
		this.player = player;
		this.mayChoosePassword = mayChoosePassword;
		this.onMainMenu = onMainMenu;
	}

	// Whether the player gives their password.
	private bool AsksPassword => player != null && player.HasPassword();

	// Whether the player may choose a password: only once, at the start of
	// their turn.
	private bool ChoosesPassword => player != null && mayChoosePassword && !player.HasPassword() && !player.passwordChosen;

	protected override void Build() {
		AddLabel(title, 36);
		AddLabel(message, 20);

		if (AsksPassword) {
			// Civ3's ENTERPASSWORDFACTION (script.txt).
			AddLabel($"Please enter password for the {player.civilization.noun}", 16);
			password = AddField(secret: true);
			password.TextSubmitted += _ => Continue();
		} else if (ChoosesPassword) {
			// Civ3's ENTERPASSWORDNEW and VERIFYPASSWORD (script.txt).
			AddLabel("Please Enter New Password (Leave blank for none). This will be your faction password for this new game.", 16);
			password = AddField(secret: true);
			AddLabel("Please Verify New Password", 16);
			verify = AddField(secret: true);
			password.TextSubmitted += _ => verify.GrabFocus();
			verify.TextSubmitted += _ => Continue();
		}
		if (password != null) {
			problem = AddLabel("", 16, WarningColor);
		}

		button = AddButton(buttonText, Continue);
		if (onMainMenu != null) {
			// Whoever opened the save may not be this player.
			AddButton("Main Menu", onMainMenu);
		}

		Control focus = password != null ? password : button;
		focus.CallDeferred(Control.MethodName.GrabFocus);
	}

	private void Continue() {
		if (button.Disabled) {
			return;
		}
		if (AsksPassword && !player.CheckPassword(password.Text)) {
			// Civ3's BADPASSWORD.
			problem.Text = "That is not the correct password.";
			password.Clear();
			password.GrabFocus();
			return;
		}
		if (ChoosesPassword) {
			if (password.Text != verify.Text) {
				// Civ3's DIFFERENTPASSWORDS.
				problem.Text = "Password was not verified. Please try again.";
				password.Clear();
				verify.Clear();
				password.GrabFocus();
				return;
			}
			new MsgSetPassword(password.Text) { playerID = player.id }.send();
		}
		CloseAndThen(button, onContinue);
	}
}
