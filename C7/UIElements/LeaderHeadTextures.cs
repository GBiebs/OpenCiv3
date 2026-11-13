using System.Collections.Generic;
using System.Runtime.CompilerServices;
using C7GameData;
using Godot;

// Caches leader head textures, so screens showing them don't run the texture
// mapping again each time they are opened. The cache is held per player or
// civilization object, so it goes away with the game (or setup screen) they
// belong to.
public static class LeaderHeadTextures {
	private static readonly ConditionalWeakTable<object, Dictionary<int, ImageTexture>> cache = new();

	// A player's leader head changes with their era.
	public static ImageTexture Get(Player player) {
		return Get(player, player.civilization, player.EraIndex());
	}

	public static ImageTexture Get(Civilization civilization) {
		return Get(civilization, civilization, 0);
	}

	// A player's leader head with the given face (the Foreign Advisor's,
	// see ForeignReport.FaceToward). The neutral face is the one Get(player)
	// shows.
	public static ImageTexture Get(Player player, LeaderFace face) {
		return Get(player, player.civilization, player.EraIndex(), face);
	}

	// Cached apart from Civ3's heads, so turning the overhaul on or off
	// shows the other ones.
	private const int RemadeKey = 1000;

	private static ImageTexture Get(object playerOrCivilization, Civilization civilization, int eraIndex,
			LeaderFace face = LeaderFace.Neutral) {
		Dictionary<int, ImageTexture> byEra = cache.GetOrCreateValue(playerOrCivilization);
		bool remade = UsesRemadeHead(civilization);
		// The remade portraits have one face for every era and mood.
		int key = remade ? RemadeKey : eraIndex * 3 + (int)face;
		if (!byEra.TryGetValue(key, out ImageTexture texture) || !GodotObject.IsInstanceValid(texture)) {
			texture = remade ? RemadeHead(civilization) : null;
			texture ??= TextureLoader.Load("leader_heads", face == LeaderFace.Neutral || playerOrCivilization is not Player player
				? playerOrCivilization : new LeaderPortrait(player, face));
			byEra[key] = texture;
		}
		return texture;
	}

	// With Civ3's art and the graphics overhaul on, a civ's leader is shown
	// with its remade portrait (C7/ModernArt/Art/Leaderheads/<civ name>.png,
	// made by ArtGen/leaderheads), as in standalone mode, where the texture
	// config already maps to it. Before, the overhaul showed Civ3's heads
	// (owner's report, 2026-11-07). There's one portrait per leader, the
	// same in every era and mood. Not for a civ whose leader art a scenario
	// has its own of: the portraits are of the game's leaders.
	private static bool UsesRemadeHead(Civilization civilization) {
		return !string.IsNullOrEmpty(civilization?.name) && ModernGraphics.Enabled && !C7Engine.C7Settings.UseStandaloneMode()
			&& !Util.ScenarioHasArt(civilization.leaderArtFile) && ModernGraphics.HasRemadeArt(RemadePath(civilization));
	}

	// A civ's remade portrait for screens drawn in our own art (the end of
	// the game's, with Modern graphics on or in standalone mode), or null if
	// there's none to use: the civ isn't one of the game's, or a scenario
	// has its own art of its leader.
	public static ImageTexture Portrait(Civilization civilization) {
		if (string.IsNullOrEmpty(civilization?.name)
			|| (!C7Engine.C7Settings.UseStandaloneMode() && Util.ScenarioHasArt(civilization.leaderArtFile))
			|| !ModernGraphics.HasRemadeArt(RemadePath(civilization))) {
			return null;
		}
		return RemadeHead(civilization);
	}

	private static string RemadePath(Civilization civilization) => $"Art/Leaderheads/{civilization.name}.png";

	private static ImageTexture RemadeHead(Civilization civilization) {
		return ModernGraphics.Load(RemadePath(civilization), null);
	}
}
