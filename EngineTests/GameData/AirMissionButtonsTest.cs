using System;
using System.IO;
using C7Engine;
using C7Engine.Lua;
using EngineTests.Utils;
using MoonSharp.Interpreter;
using Xunit;

namespace EngineTests.GameData;

// The air missions and the airdrop have unit command buttons in both modes:
// Civ3's own (NormButtons.pcx) and, in standalone mode, ours
// (C7/ModernArt/Art/interface/AirMissionButtons.png, made by
// ArtGen/ui_buttons/build.py), since the standalone asset pack's
// button sheet is blank there.
public class AirMissionButtonsTest {
	private static readonly string[] actions = [
		C7Action.UnitBombing, C7Action.UnitPrecisionBombing, C7Action.UnitRecon,
		C7Action.UnitAirSuperiority, C7Action.UnitRebase, C7Action.UnitAirdrop,
	];

	private static readonly Lazy<GameMode> civ3 = new(() => SaveGameFixture.LoadGameMode(new GameMode.Config("civ3")));
	private static readonly Lazy<GameMode> standalone = new(() => SaveGameFixture.LoadGameMode(new GameMode.Config("civ3", ["standalone"])));

	private static Table Button(GameMode mode, string action) {
		Table ui = (Table)mode.textures.Item2["ui"];
		Table unitControl = (Table)ui["unit_control"];
		return unitControl.Get(action).Table;
	}

	[Fact]
	public void EveryAirMissionHasAButtonAndATooltip() {
		foreach (string action in actions) {
			Assert.NotNull(C7Action.ToUnitAction(action));
			Assert.NotNull(C7Action.ToTooltip(action));
			foreach (GameMode mode in new[] { civ3.Value, standalone.Value }) {
				Table button = Button(mode, action);
				Assert.NotNull(button);
				foreach (string state in new[] { "normal", "hover", "pressed" }) {
					Assert.NotNull(button.Get(state).Table);
				}
			}
		}
	}

	// Automated bombarding's buttons are Civ3's (labels.txt's "Automate:
	// Bombard", "Automate: Bombing" and "Automate: Precision Bombing",
	// #UNIT_ACTIONS 43, 54 and 55) and, in standalone mode, those of the
	// orders they automate.
	[Fact]
	public void AutomatedBombardingHasButtons() {
		(string action, int x, int y, string standaloneAs)[] buttons = [
			(C7Action.UnitAutoBombard, 3, 5, C7Action.UnitBombard),
			(C7Action.UnitAutoBombing, 6, 6, C7Action.UnitBombing),
			(C7Action.UnitAutoPrecisionBombing, 7, 6, C7Action.UnitPrecisionBombing),
		];
		foreach ((string action, int x, int y, string standaloneAs) in buttons) {
			Assert.NotNull(C7Action.ToUnitAction(action));
			Assert.NotNull(C7Action.ToTooltip(action));
			Table crop = Button(civ3.Value, action).Get("normal").Table.Get("crop_region").Table;
			Assert.Equal(x * 32, crop.Get(1).Number);
			Assert.Equal(y * 32, crop.Get(2).Number);
			Assert.Same(Button(standalone.Value, standaloneAs), Button(standalone.Value, action));
		}
	}

	[Fact]
	public void StandaloneAirMissionButtonsAreOurOwn() {
		string sheet = PathUtils.getBasePath("../C7/ModernArt/Art/interface/AirMissionButtons.png");
		Assert.True(File.Exists(sheet), sheet);
		foreach (string action in actions) {
			Table normal = Button(standalone.Value, action).Get("normal").Table;
			Assert.Equal("Art/interface/AirMissionButtons.png", normal.Get("path").String);
		}
	}

	// Airlift's button is Civ3's (labels.txt lists it after Unload, so it's
	// the cell after Unload's) and, in standalone mode, our own
	// (C7/ModernArt/Art/interface/unit_airlift.png, made by
	// ArtGen/ui_buttons/build.py).
	[Fact]
	public void AirliftHasAButtonInBothModes() {
		Table civ3Normal = Button(civ3.Value, C7Action.UnitAirlift).Get("normal").Table;
		Table unloadNormal = Button(civ3.Value, C7Action.UnitUnload).Get("normal").Table;
		Assert.Equal(unloadNormal.Get("crop_region").Table.Get(1).Number + 32, civ3Normal.Get("crop_region").Table.Get(1).Number);
		Assert.Equal(unloadNormal.Get("crop_region").Table.Get(2).Number, civ3Normal.Get("crop_region").Table.Get(2).Number);

		string file = PathUtils.getBasePath("../C7/ModernArt/Art/interface/unit_airlift.png");
		Assert.True(File.Exists(file), file);
		foreach (string state in new[] { "normal", "hover", "pressed" }) {
			Assert.Equal("Art/interface/unit_airlift.png", Button(standalone.Value, C7Action.UnitAirlift).Get(state).Table.Get("path").String);
		}
	}
}
