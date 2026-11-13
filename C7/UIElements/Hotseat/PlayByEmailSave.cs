using System;
using C7Engine;
using Godot;

// Play by email: the curtain at the end of a turn, when the next human's
// turn begins on their own computer. The player names the save (it starts
// with a name saying who plays next), saves it, and is told where it is to
// email it on, as Civ3's "Your turn is over. Please Save & Exit Game, and
// e-mail the save to the next player".
public partial class PlayByEmailSave : Curtain {
	private readonly string nextPlayerName;
	private readonly string suggestedName;
	private readonly string folder;
	// Saves under the file name given: null once saved, otherwise what went
	// wrong.
	private readonly Func<string, string> save;
	private readonly Action onMainMenu;

	private LineEdit name;
	private Label problem;
	private Button saveButton;
	private Button mainMenu;
	// A save of this name is there already, and the player was told.
	private string replacing;

	public PlayByEmailSave(string nextPlayerName, string suggestedName, string folder, Func<string, string> save, Action onMainMenu) {
		this.nextPlayerName = nextPlayerName;
		this.suggestedName = suggestedName;
		this.folder = folder;
		this.save = save;
		this.onMainMenu = onMainMenu;
	}

	protected override void Build() {
		AddLabel("Your turn is over", 36);
		// Civ3's MP_PBEM_QUESTION (script.txt).
		AddLabel($"Your turn is over.  Please Save & Exit Game, and e-mail the save to the next player ({nextPlayerName}).", 20);
		AddLabel("Save as:", 16);
		name = AddField(secret: false, System.IO.Path.GetFileNameWithoutExtension(suggestedName), width: 560);
		name.TextSubmitted += _ => Save();
		name.TextChanged += _ => problem.Text = "";
		problem = AddLabel("", 16, WarningColor);
		saveButton = AddButton("Save & Exit", Save);
		// Leaving without the save loses the turn, so only once it failed.
		mainMenu = AddButton("Main Menu", onMainMenu);
		mainMenu.Visible = false;

		name.CallDeferred(Control.MethodName.GrabFocus);
		name.CallDeferred(LineEdit.MethodName.SelectAll);
	}

	private void Save() {
		if (saveButton.Disabled) {
			return;
		}
		string fileName = PlayByEmail.FileNameTyped(name.Text);
		if (fileName == null) {
			problem.Text = $"A save needs a name, without any of these: {string.Join(" ", PlayByEmail.CharactersNotInAName.ToCharArray())}";
			return;
		}
		if (System.IO.File.Exists(System.IO.Path.Combine(folder, fileName)) && replacing != fileName) {
			replacing = fileName;
			problem.Text = "There's a save of that name already. Save again to replace it.";
			return;
		}
		string error = save(fileName);
		if (error != null) {
			problem.Text = $"The game couldn't be saved: {error}";
			mainMenu.Visible = true;
			return;
		}
		ShowSaved(fileName);
	}

	// Where the save is, to email it on.
	private void ShowSaved(string fileName) {
		name.Editable = false;
		saveButton.Visible = false;
		problem.AddThemeColorOverride("font_color", TextColor);
		problem.Text = $"Saved as \"{fileName}\" in {folder}.\nE-mail it to {nextPlayerName}, who loads it with Load Game.";
		Button open = AddButton("Open the Saves Folder", () => OS.ShellOpen(folder));
		mainMenu.Visible = true;
		// Last in the column, under the folder's button.
		mainMenu.GetParent().MoveChild(mainMenu, -1);
		open.CallDeferred(Control.MethodName.GrabFocus);
	}
}
