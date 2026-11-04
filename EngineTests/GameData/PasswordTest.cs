using System.Linq;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Hotseat passwords, as Civ3's: optional, asked before a player's turn
// shows their empire, kept hashed in the save.
public class PasswordTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player human;
	private readonly Player other;

	public PasswordTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		human = gameData.players.First(p => p.isHuman);
		other = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		other.isHuman = true;
	}

	[Fact]
	public void APasswordIsCheckedAndOptional() {
		Assert.False(human.HasPassword());
		Assert.True(human.CheckPassword(""));
		Assert.True(human.CheckPassword("anything"));

		human.SetPassword("hammurabi");
		Assert.True(human.HasPassword());
		Assert.True(human.CheckPassword("hammurabi"));
		Assert.False(human.CheckPassword("Hammurabi"));
		Assert.False(human.CheckPassword(""));

		human.SetPassword("");
		Assert.False(human.HasPassword());
	}

	// Two players with the same password don't share a hash (it's salted),
	// and the save never shows it.
	[Fact]
	public void TheSaveKeepsItHashed() {
		human.SetPassword("hammurabi");
		other.SetPassword("hammurabi");
		Assert.NotEqual(human.passwordHash, other.passwordHash);

		SaveGame save = SaveGame.FromGameData(gameData);
		Assert.DoesNotContain(save.Players, p => p.passwordHash.Contains("hammurabi"));
		C7GameData.GameData reloaded = save.Clone().ToGameData(fixture.behaviors);
		Assert.True(reloaded.GetPlayer(human.id).CheckPassword("hammurabi"));
		Assert.False(reloaded.GetPlayer(human.id).CheckPassword("nebuchadnezzar"));
	}

	// Playing by email the save travels, so the hash is slow to guess at
	// and salted at random.
	[Fact]
	public void TheHashIsSlowAndSaltedAtRandom() {
		human.SetPassword("hammurabi");
		string first = human.passwordHash;
		Assert.StartsWith("pbkdf2:100000:", first);
		human.SetPassword("hammurabi");
		Assert.NotEqual(first, human.passwordHash);
		Assert.True(human.CheckPassword("hammurabi"));
	}

	// Hotseat saves from before keep their passwords.
	[Fact]
	public void AnOlderHashStillWorks() {
		human.passwordHash = System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
			System.Text.Encoding.UTF8.GetBytes($"{human.id}:hammurabi")));
		Assert.True(human.CheckPassword("hammurabi"));
		Assert.False(human.CheckPassword("nebuchadnezzar"));
	}

	// Only the player in control sets their own.
	[Fact]
	public void OnlyThePlayerInControlSetsTheirs() {
		EngineStorage.uiControllerID = human.id;
		new MsgSetPassword(human.id, "hammurabi").process();
		new MsgSetPassword(other.id, "sneaky").process();
		Assert.True(human.CheckPassword("hammurabi"));
		Assert.False(other.HasPassword());
	}
}
