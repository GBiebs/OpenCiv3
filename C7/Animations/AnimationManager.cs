
// AnimationManager's purpose is to store the data associated with Civ 3 animations, for example the contents of each folder in Art/Units. It does lazy
// loading & memoization so each file is loaded only when it's needed and then stored so it is only ever loaded once per game. The main (and only)
// instance of AnimationManager is kept in Game, AnimationTracker holds a reference to it.

// It would be nice to load this data close to where it's used, e.g., have UnitLayer load the FlicSheets, instead of putting all the loading code in
// one detached class like this. That's how things originally worked but I created AnimationManager to solve two issues:
// 1. AnimationTracker and UnitLayer both need to load the unit INIs. So we either have a common place to store the INIs or duplication of work, and I
// think the former is the better choice.
// 2. AnimationTracker needs to know the duration of animations, which awkwardly cannot be determined based on the INI files alone. In order to know
// the duration of an anim you must know how many frames it has, and the only way to know that is to read its flic file.

// The intended usage is to access the animation data through a Civ3Anim object obtained through the "forUnit" or "forEffect" methods. For example:
//   AnimationManager.forUnit("Warrior", MapUnit.AnimatedAction.FORTIFY).playSound()
// To play the warrior's foritfy sound effect.

// In standalone mode there are no Civ3 files: a unit's art is then our own
// (C7/ModernArt/Art/Units/<art>), and what the INI and the flic headers would
// say is read from the metadata saved with it (UnitArtMetadata), while the
// frames come from its sheets (ModernGraphics). Units make no sounds then.

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using IniParser;
using IniParser.Model;
using C7GameData;
using ConvertCiv3Media;
using Serilog;

public partial class AnimationManager {

	private static readonly ILogger log = LogManager.ForContext<AnimationManager>();

	// Whether we have art for a unit art name. Missing art is remembered so
	// that it's only reported once.
	private static readonly Dictionary<string, bool> unitArtExists = new();

	// What a unit's own art name (MapUnit.GetArtName) depends on: its type,
	// its owner's era, for workers whether it's a captive, and for leaders
	// whether it's a scientific one.
	private readonly record struct ArtNameKey(UnitPrototype unitType, string era, bool captiveWorker, bool scientificLeader);

	// The results of ArtNameFor that don't depend on anything but the key.
	private static readonly Dictionary<ArtNameKey, string> artNames = new();

	// The art to draw a unit with. An army with members is drawn with the
	// art of its first member, like in Civ3; an empty one with its own art.
	// If its own art can't be found, like army art that isn't available, the
	// prototype's default art is tried, rather than failing to load the same
	// art every frame.
	public static string ArtNameFor(MapUnit unit) {
		if (unit.IsArmy()) {
			// The members change during the game, so which one is drawn isn't
			// cached here, but the member's own art name is.
			MapUnit member = unit.FirstPassenger();
			if (member != null && !member.IsArmy()) {
				string memberName = ArtNameFor(member);
				if (HasUnitArt(memberName)) {
					return memberName;
				}
			}
		}

		UnitPrototype unitType = unit.unitType;
		bool captiveWorker = unitType.isWorker && unitType.art.mainArt.variations != null && unit.IsCaptive();
		ArtNameKey key = new(unitType, unit.owner?.eraCivilopediaName, captiveWorker, unit.isScientificLeader);
		if (artNames.TryGetValue(key, out string cached)) {
			return cached;
		}

		string name = unit.GetArtName();
		if (HasUnitArt(name)) {
			artNames[key] = name;
			return name;
		}
		string defaultName = unitType.art.mainArt.defaultName;
		string result = HasUnitArt(defaultName) ? defaultName : FallbackArtFor(unitType) ?? name;
		artNames[key] = result;
		return result;
	}

	// The art of a stock unit like it, for a unit whose own art isn't there
	// (as a scenario's units can be in standalone mode, which has only its
	// own art), so the unit is drawn rather than failing to: a settler as a
	// Settler, a worker as a Worker, a ship as a Galley, a mounted unit as a
	// Horseman, a gun as a Catapult, anything else on foot as a Spearman.
	// Null if none of those is there either.
	private static string FallbackArtFor(UnitPrototype unitType) {
		string[] candidates = unitType.isSettler ? ["Settler"]
			: unitType.isWorker ? ["Worker"]
			: unitType.IsSeaUnit() ? ["Galley", "Caravel"]
			: unitType.movement > 1 && unitType.IsLandUnit() ? ["Horseman"]
			: unitType.bombard > 0 && unitType.defense <= 1 ? ["Catapult"]
			: [];
		foreach (string candidate in candidates.Append("Spearman").Append("Warrior")) {
			if (HasUnitArt(candidate)) {
				log.Warning($"Drawing {unitType.name} with the art of {candidate}");
				return candidate;
			}
		}
		return null;
	}

	// Whether the art a unit is drawn with may change while the unit, its
	// type and its owner's era stay the same, as for an army drawn with the
	// art of one of its members.
	public static bool ArtMayChange(MapUnit unit) {
		return unit.IsArmy();
	}

	// Whether units are drawn with our own art, described by its metadata,
	// rather than with Civ3's INIs and flics: in standalone mode.
	// A scenario's own unit art (an INI and its flics in its own folder, see
	// Util.ScenarioRoots) is drawn as Civ3's is, in either mode.
	private static bool OwnUnitArt(string artName) => C7Engine.C7Settings.UseStandaloneMode() && !ScenarioUnitArt(artName);

	// Whether the game can draw units with the art name (Check Scenario).
	public static bool CanDraw(string artName) => HasUnitArt(artName);

	private static bool ScenarioUnitArt(string artName) {
		return !string.IsNullOrEmpty(artName) && Util.ScenarioHasArt(string.Format("Art/Units/{0}/{0}.INI", artName));
	}

	private static bool HasUnitArt(string name) {
		if (string.IsNullOrEmpty(name)) {
			return false;
		}
		if (!unitArtExists.TryGetValue(name, out bool exists)) {
			if (OwnUnitArt(name)) {
				exists = ModernGraphics.LoadUnitMetadata(name) != null;
			} else {
				try {
					Util.Civ3MediaPath(string.Format("Art/Units/{0}/{0}.INI", name));
					exists = true;
				} catch (ApplicationException) {
					exists = false;
				}
			}
			if (!exists) {
				log.Warning($"No unit art found for {name}");
			}
			unitArtExists[name] = exists;
		}
		return exists;
	}

	// The metadata of our own art for a unit art name, which must exist.
	private static UnitArtMetadata OwnMetadata(string artName) {
		return ModernGraphics.LoadUnitMetadata(artName) ?? throw new ApplicationException($"No unit art metadata for {artName}");
	}

	private static string OwnFlicPath(string artName, string flicName) {
		return $"Art/Units/{artName}/{flicName}.flc";
	}

	// The flic of our own art an action is drawn with: its own if it has one
	// and its sheet is there (only those the game plays are committed, not
	// the other attacks or fidgets), else the DEFAULT one, as with an INI.
	private static string OwnFlicName(string artName, MapUnit.AnimatedAction action) {
		UnitArtMetadata metadata = OwnMetadata(artName);
		string own = metadata.FlicNameOf(action.ToString());
		if (own != null && ModernGraphics.HasFlicSheet(OwnFlicPath(artName, own))) {
			return own;
		}
		return metadata.FlicNameOrDefault(UnitArtMetadata.DefaultAction);
	}

	// The header values of the flic a unit art's action is drawn with.
	public Util.FlicSheet GetUnitFlicSheet(string artName, MapUnit.AnimatedAction action) {
		if (!OwnUnitArt(artName)) {
			return RemadeFlicSheet(artName, getUnitFlicFilepath(artName, action))
				?? getFlicSheet("Art/Units/" + artName, getUnitINIData(artName), action);
		}
		return SheetOf(OwnMetadata(artName).GetFlic(OwnFlicName(artName, action)));
	}

	// With Civ3's art and the graphics overhaul on, the header values of a
	// unit flic's remade sheet, or null if it hasn't one: the remade frames
	// are padded to one size for all of a unit's actions, so their size and
	// place on the canvas are the remade art's metadata's, not those of
	// Civ3's flic. Cutting the remade sheet by Civ3's frame size (as for
	// the Settler, whose flic frames are 30x55 against 67x93 remade) cut
	// mostly empty pieces of the wrong frames, and the units looked sunk
	// into the ground or weren't drawn (owner's report, 2026-11-07).
	private static Util.FlicSheet? RemadeFlicSheet(string artName, string flicPath) {
		if (flicPath == null || !ModernGraphics.HasFlicSheet(flicPath)) {
			return null;
		}
		UnitArtMetadata metadata = ModernGraphics.LoadUnitMetadata(artName);
		if (metadata == null || !metadata.TryGetFlic(System.IO.Path.GetFileNameWithoutExtension(flicPath), out UnitArtMetadata.Flic flic)) {
			return null;
		}
		return SheetOf(flic);
	}

	private static Util.FlicSheet SheetOf(UnitArtMetadata.Flic flic) {
		return new Util.FlicSheet {
			spriteOriginalWidth = flic.CanvasWidth,
			spriteOriginalHeight = flic.CanvasHeight,
			spriteWidth = flic.Width,
			spriteHeight = flic.Height,
			offsetLeft = flic.OffsetLeft,
			offsetTop = flic.OffsetTop,
			framesPerAnimation = flic.Frames,
			numberOfAnimations = flic.Directions,
			animationSpeed = flic.FrameMs,
		};
	}

	public static string BaseAnimationKey(string unitName, MapUnit.AnimatedAction action) {
		return String.Format("{0}_{1}", unitName, action.ToString());
	}

	public static string BaseAnimationKey(MapUnit unit, MapUnit.AnimatedAction action) {
		return BaseAnimationKey(ArtNameFor(unit), action);
	}

	public static string AnimationKey(string baseKey, TileDirection direction) {
		return String.Format("{0}_{1}", baseKey, direction.ToString());
	}

	public static string AnimationKey(MapUnit unit, MapUnit.AnimatedAction action, TileDirection direction) {
		return AnimationKey(BaseAnimationKey(unit, action), direction);
	}

	public static string AnimationKey(AnimatedEffect effect, MapUnit.AnimatedAction action) {
		return $"{effect.ToString()}_{action.ToString()}";
	}

	public static readonly Dictionary<string, ImageTexture> AnimationThumbnails = new();
	public static readonly Dictionary<string, ImageTexture> AnimationTintThumbnails = new();

	private const int thumbnailFrame = 0;
	private const TileDirection thumbnailDirection = TileDirection.SOUTHEAST;
	private const MapUnit.AnimatedAction thumbnailAction = MapUnit.AnimatedAction.DEFAULT;

	private AudioStreamPlayer audioPlayer;

	public SpriteFrames spriteFrames;
	public SpriteFrames tintFrames;

	// The INIs by path. Paths are compared ignoring case, since the same INI
	// is asked for as both "X.ini" and "X.INI" and the files are found
	// ignoring case anyway.
	private Dictionary<string, IniData> iniDatas = new Dictionary<string, IniData>(StringComparer.OrdinalIgnoreCase);

	// What's needed to draw an animation: its name in the SpriteFrames, its
	// frames and, for unit animations, the placement values from its flic.
	// These are looked up once per animation instead of once per frame.
	public sealed class AnimationInfo {
		public readonly string name;
		public readonly StringName animationName;
		public readonly int frameCount;
		// The size of the animation's frames, zero if the animation has no frames.
		public readonly Vector2 frameSize;
		public readonly Vector2I flicOffset;
		public readonly Vector2I flicOriginalSize;

		public AnimationInfo(SpriteFrames frames, string name, Util.FlicSheet flicSheet) {
			this.name = name;
			animationName = new StringName(name);
			frameCount = frames.GetFrameCount(animationName);
			frameSize = frameCount > 0 ? frames.GetFrameTexture(animationName, 0).GetSize() : Vector2.Zero;
			flicOffset = new Vector2I(flicSheet.offsetLeft, flicSheet.offsetTop);
			flicOriginalSize = new Vector2I(flicSheet.spriteOriginalWidth, flicSheet.spriteOriginalHeight);
		}

		// The frame to show at a given progress (0 to 1) through the animation.
		public int FrameAtProgress(float progress) {
			// AnimatedSprite2D has a settable FrameProgress field, which I expected to
			// update the current frame of the animation upon setting, but it did not
			// when I tried it, so instead, calculate what the next frame should be
			// based on the progress.
			int nextFrame = (int)((float)frameCount * progress);
			return nextFrame >= frameCount ? frameCount - 1 : (nextFrame < 0 ? 0 : nextFrame);
		}
	}

	private static readonly int animatedActionCount = (int)Enum.GetValues<MapUnit.AnimatedAction>().Max() + 1;
	private static readonly int tileDirectionCount = (int)Enum.GetValues<TileDirection>().Max() + 1;

	// The animations of one unit art, indexed by action and direction and
	// filled in as they're needed. Drawing code can hold on to one of these
	// to skip looking the art up by name every frame.
	public sealed class UnitArt {
		public readonly string artName;
		internal readonly AnimationInfo[] animations = new AnimationInfo[animatedActionCount * tileDirectionCount];

		internal UnitArt(string artName) {
			this.artName = artName;
		}
	}

	// The unit animations by art name.
	private readonly Dictionary<string, UnitArt> unitAnimations = new();
	private readonly Dictionary<(AnimatedEffect, MapUnit.AnimatedAction), AnimationInfo> effectAnimations = new();

	// Returns the animations of a unit art, which are loaded as they're asked for.
	public UnitArt GetUnitArt(string artName) {
		if (!unitAnimations.TryGetValue(artName, out UnitArt art)) {
			art = new UnitArt(artName);
			unitAnimations.Add(artName, art);
		}
		return art;
	}

	// Returns the animation for drawing a unit doing an action facing a
	// direction, loading the animation if it hasn't been loaded yet.
	public AnimationInfo GetUnitAnimation(MapUnit unit, MapUnit.AnimatedAction action, TileDirection direction) {
		return GetUnitAnimation(GetUnitArt(ArtNameFor(unit)), action, direction);
	}

	public AnimationInfo GetUnitAnimation(UnitArt art, MapUnit.AnimatedAction action, TileDirection direction) {
		int index = (int)action * tileDirectionCount + (int)direction;
		bool indexable = (uint)action < (uint)animatedActionCount && (uint)direction < (uint)tileDirectionCount;
		if (indexable && art.animations[index] is AnimationInfo cached) {
			return cached;
		}

		string artName = art.artName;
		LoadAnimation(artName, action);
		AnimationInfo info = new(spriteFrames, AnimationKey(BaseAnimationKey(artName, action), direction), GetUnitFlicSheet(artName, action));
		if (indexable) {
			art.animations[index] = info;
		}
		return info;
	}

	// Returns the animation for drawing an effect, loading it if it hasn't been loaded yet.
	public AnimationInfo GetEffectAnimation(C7Animation anim) {
		if (effectAnimations.TryGetValue((anim.effect, anim.action), out AnimationInfo cached)) {
			return cached;
		}

		anim.loadEffectAnimation();
		AnimationInfo info = new(spriteFrames, AnimationKey(anim.effect, anim.action), anim.getFlicSheet());
		effectAnimations[(anim.effect, anim.action)] = info;
		return info;
	}

	public AnimationManager(AudioStreamPlayer audioPlayer) {
		this.audioPlayer = audioPlayer;
		this.spriteFrames = new SpriteFrames();
		this.tintFrames = new SpriteFrames();
	}

	public IniData getINIData(string pathKey) {
		if (!iniDatas.TryGetValue(pathKey, out IniData tr)) {
			string fullPath = Util.Civ3MediaPath(pathKey);
			// Some INIs have junk before their first section, which Civ3
			// ignores: PTW's Medieval Japan Leader's starts with a time stamp,
			// "11:27 AM 5/10/2002[Speed]".
			string text = System.IO.File.ReadAllText(fullPath);
			int firstSection = text.IndexOf('[');
			if (firstSection > 0) {
				text = text[firstSection..];
			}
			tr = C7Engine.Util.GetFileIniDataParser().Parser.Parse(text);
			iniDatas.Add(pathKey, tr);
		}
		return tr;
	}

	public static string GetUnitDefaultThumbnailKey(MapUnit unit) {
		return $"{ArtNameFor(unit)}_{thumbnailDirection}_{thumbnailAction}_{thumbnailFrame}";
	}

	public (ImageTexture baseFrame, ImageTexture tintFrame) GetAnimationFrameAndTintTextures(MapUnit unit) {

		string key = GetUnitDefaultThumbnailKey(unit);

		if (AnimationThumbnails.TryGetValue(key, out ImageTexture baseImage)
		   && AnimationTintThumbnails.TryGetValue(key, out ImageTexture tintImage)) {
			return (baseImage, tintImage);
		}

		string filepath = getUnitFlicFilepath(unit, thumbnailAction);

		// This actually doesn't return the tint frame with the civ color applied. The shader still needs to be applied.
		ImageTexture baseFrame, tintFrame;
		bool own = OwnUnitArt(ArtNameFor(unit));
		Util.FlicSheet? remade = own ? null : RemadeFlicSheet(ArtNameFor(unit), filepath);
		if (own || remade != null) {
			Util.FlicSheet sheet = remade ?? GetUnitFlicSheet(ArtNameFor(unit), thumbnailAction);
			(baseFrame, tintFrame) = LoadOwnFrame(filepath, sheet, flicAnimationDirectionToRow(thumbnailDirection), thumbnailFrame);
		} else {
			Flic flic = Util.LoadFlic(filepath);
			(baseFrame, tintFrame) = LoadFrame(filepath, flic, flicAnimationDirectionToRow(thumbnailDirection), thumbnailFrame);
		}
		AnimationThumbnails[key] = baseFrame;
		AnimationTintThumbnails[key] = tintFrame;

		return (baseFrame, tintFrame);
	}

	// Looks up the name of the flic file associated with a given action in an animation INI. If there is no flic file listed for the action,
	// returns instead the file name for the default action, and if that's missing too, throws an exception.
	public string getFlicFileName(IniData iniData, MapUnit.AnimatedAction action) {
		string fileName = iniData["Animations"][action.ToString()];
		if (!string.IsNullOrEmpty(fileName)) {
			return fileName;
		} else if (action != MapUnit.AnimatedAction.DEFAULT) {
			return getFlicFileName(iniData, MapUnit.AnimatedAction.DEFAULT);
		} else {
			throw new Exception("Missing default animation"); // TODO: Add the INI's file name to the error message
		}
	}

	// Whether each unit art has its own animation for each action, which is
	// asked every frame for every worker shown.
	private static readonly Dictionary<(string, MapUnit.AnimatedAction), bool> unitActionExists = new();

	// Whether a unit's art has its own animation for an action, rather than
	// falling back on its default one.
	public bool HasUnitAction(string artName, MapUnit.AnimatedAction action) {
		if (artName == null) {
			return false;
		}
		if (!unitActionExists.TryGetValue((artName, action), out bool exists)) {
			try {
				exists = HasUnitArt(artName) && (OwnUnitArt(artName)
					? OwnFlicName(artName, action) == OwnMetadata(artName).FlicNameOf(action.ToString())
					: !string.IsNullOrEmpty(getUnitINIData(artName)["Animations"][action.ToString()]));
			} catch (Exception e) {
				log.Warning(e, "Couldn't read the {Action} animation of {Art}", action, artName);
				exists = false;
			}
			unitActionExists[(artName, action)] = exists;
		}
		return exists;
	}

	public IniData getUnitINIData(string unitTypeName) {
		return getINIData(string.Format("Art/Units/{0}/{0}.INI", unitTypeName));
	}

	public string GetFlicFilePath(string rootPath, IniData iniData, MapUnit.AnimatedAction action) {
		return rootPath + "/" + getFlicFileName(iniData, action);
	}

	public string getUnitFlicFilepath(MapUnit unit, MapUnit.AnimatedAction action) {
		return getUnitFlicFilepath(ArtNameFor(unit), action);
	}

	public string getUnitFlicFilepath(string artName, MapUnit.AnimatedAction action) {
		if (OwnUnitArt(artName)) {
			return OwnFlicPath(artName, OwnFlicName(artName, action));
		}
		string directory = string.Format("Art/Units/{0}", artName);
		IniData ini = getUnitINIData(artName);
		string filename = getFlicFileName(ini, action);
		return directory.PathJoin(filename);
	}

	// The flic loading code parses the animations into a 2D array, where each row is an animation
	// corresponding to a tile direction. flicRowToAnimationDirection maps row number -> direction.
	private static TileDirection flicRowToAnimationDirection(int row) {
		switch (row) {
			case 0: return TileDirection.SOUTHWEST;
			case 1: return TileDirection.SOUTH;
			case 2: return TileDirection.SOUTHEAST;
			case 3: return TileDirection.EAST;
			case 4: return TileDirection.NORTHEAST;
			case 5: return TileDirection.NORTH;
			case 6: return TileDirection.NORTHWEST;
			case 7: return TileDirection.WEST;
		}
		// Rows beyond the 8 real directions are unexpected. Return INVALID rather
		// than guessing a direction, so they don't interfere with the others.
		return TileDirection.INVALID;
	}

	private static int flicAnimationDirectionToRow(TileDirection tileDirection) {
		switch (tileDirection) {
			case TileDirection.SOUTHWEST: return 0;
			case TileDirection.SOUTH: return 1;
			case TileDirection.SOUTHEAST: return 2;
			case TileDirection.EAST: return 3;
			case TileDirection.NORTHEAST: return 4;
			case TileDirection.NORTH: return 5;
			case TileDirection.NORTHWEST: return 6;
			case TileDirection.WEST: return 7;
		}
		return 2;
	}

	public static void loadFlicAnimation(string path, string name, ref SpriteFrames frames, ref SpriteFrames tint) {
		Flic flic = Util.LoadFlic(path);

		for (int row = 0; row < flic.Images.GetLength(0); row++) {
			string direction = flicRowToAnimationDirection(row).ToString();
			string animationName = name + "_" + direction;
			frames.AddAnimation(animationName);
			tint.AddAnimation(animationName);

			for (int col = 0; col < flic.Images.GetLength(1); col++) {
				(ImageTexture bl, ImageTexture tl) = LoadFrame(path, flic, row, col);
				frames.AddFrame(animationName, bl, 0.5f); // TODO: frame duration is controlled by .ini
				tint.AddFrame(animationName, tl, 0.5f);   // TODO: frame duration is controlled by .ini
			}
		}
		// The frames are kept here, so the remade sheets needn't be.
		ModernGraphics.ForgetFlicSheets(path);
	}

	// Loads the animations of a flic of our own art, described by its header
	// values, from its sheets.
	private static void loadOwnAnimation(string path, Util.FlicSheet sheet, string name, SpriteFrames frames, SpriteFrames tint) {
		for (int row = 0; row < sheet.numberOfAnimations; row++) {
			string animationName = name + "_" + flicRowToAnimationDirection(row).ToString();
			frames.AddAnimation(animationName);
			tint.AddAnimation(animationName);
			for (int col = 0; col < sheet.framesPerAnimation; col++) {
				(ImageTexture bl, ImageTexture tl) = LoadOwnFrame(path, sheet, row, col);
				frames.AddFrame(animationName, bl, 0.5f);
				tint.AddFrame(animationName, tl, 0.5f);
			}
		}
		ModernGraphics.ForgetFlicSheets(path);
	}

	// A missing frame of our own art is drawn empty rather than failing.
	private static ImageTexture emptyFrame;

	private static (ImageTexture, ImageTexture) LoadOwnFrame(string path, Util.FlicSheet sheet, int row, int col) {
		(ImageTexture b, ImageTexture t) = ModernGraphics.LoadFlicFrame(path, sheet.spriteWidth, sheet.spriteHeight, sheet.framesPerAnimation, row, col);
		if (b != null) {
			return (b, t);
		}
		log.Warning("Missing frame {Col} of direction {Row} of {Path}", col, row, path);
		emptyFrame ??= ImageTexture.CreateFromImage(Image.CreateEmpty(1, 1, false, Image.Format.Rgba8));
		return (emptyFrame, emptyFrame);
	}

	// The base and tint textures of a frame of a flic, remade ones if the
	// graphics overhaul is on and the flic has been remade.
	private static (ImageTexture, ImageTexture) LoadFrame(string path, Flic flic, int row, int col) {
		(ImageTexture modernBase, ImageTexture modernTint) = ModernGraphics.LoadFlicFrame(path, flic, row, col);
		if (modernBase != null) {
			return (modernBase, modernTint);
		}
		return Util.LoadTextureFromFlicData(flic.Images[row, col], flic.Palette, flic.Width, flic.Height);
	}

	public static void loadFlicEffectAnimation(string path, string name, ref SpriteFrames frames, ref SpriteFrames tint) {
		Flic flic = Util.LoadFlic(path);

		for (int row = 0; row < flic.Images.GetLength(0); row++) {
			string animationName = name;
			frames.AddAnimation(animationName);
			tint.AddAnimation(animationName);

			for (int col = 0; col < flic.Images.GetLength(1); col++) {
				(ImageTexture bl, ImageTexture tl) = LoadFrame(path, flic, row, col);
				frames.AddFrame(animationName, bl, 0.5f); // TODO: frame duration is controlled by .ini
				tint.AddFrame(animationName, tl, 0.5f);   // TODO: frame duration is controlled by .ini
			}
		}
	}

	public bool LoadAnimation(MapUnit unit, MapUnit.AnimatedAction action) {
		return LoadAnimation(ArtNameFor(unit), action);
	}

	public bool LoadAnimation(string artName, MapUnit.AnimatedAction action) {
		string name = BaseAnimationKey(artName, action);
		string testName = AnimationKey(name, TileDirection.NORTH);
		if (spriteFrames.HasAnimation(testName) && tintFrames.HasAnimation(testName)) {
			return false;
		}
		string filepath = getUnitFlicFilepath(artName, action);
		bool own = OwnUnitArt(artName);
		Util.FlicSheet? remade = own ? null : RemadeFlicSheet(artName, filepath);
		if (own || remade != null) {
			loadOwnAnimation(filepath, remade ?? GetUnitFlicSheet(artName, action), name, spriteFrames, tintFrames);
		} else {
			loadFlicAnimation(filepath, name, ref this.spriteFrames, ref this.tintFrames);
		}
		return true;
	}

	public bool LoadAnimation(AnimatedEffect effect, MapUnit.AnimatedAction action, string flicFilePath) {
		string name = AnimationKey(effect, action);
		if (spriteFrames.HasAnimation(name) && tintFrames.HasAnimation(name)) {
			return false;
		}
		loadFlicEffectAnimation(flicFilePath, name, ref this.spriteFrames, ref this.tintFrames);
		return true;
	}

	// Returns the header values of the flic for an action. The values are
	// cached by Util.LoadFlicHeader, which reads them without decoding the flic.
	public Util.FlicSheet getFlicSheet(string rootPath, IniData iniData, MapUnit.AnimatedAction action) {
		return Util.LoadFlicHeader(GetFlicFilePath(rootPath, iniData, action));
	}

	private Dictionary<string, AudioStreamWav> wavs = new Dictionary<string, AudioStreamWav>();

	public void playSound(string rootPath, IniData iniData, MapUnit.AnimatedAction action) {
		string fileName = iniData["Sound Effects"][action.ToString()];
		if (fileName.EndsWith(".WAV", StringComparison.CurrentCultureIgnoreCase)) {
			AudioStreamWav wav;
			string pathKey = rootPath + "/" + fileName;
			if (!wavs.TryGetValue(pathKey, out wav)) {
				wav = Util.LoadCiv3WAVFromDisk(pathKey);
				wavs.Add(pathKey, wav);
			}
			if (wav != null) {
				audioPlayer.Stream = wav;
				audioPlayer.Play();
			}
		}
	}

	public C7Animation forUnit(MapUnit unit, MapUnit.AnimatedAction action) {
		return new C7Animation(this, unit, action);
	}

	public C7Animation forEffect(AnimatedEffect effect) {
		return new C7Animation(this, effect);
	}

	public static void ClearCache() {
		AnimationThumbnails.Clear();
		AnimationTintThumbnails.Clear();
		// Which art exists depends on the media paths, which may have changed.
		artNames.Clear();
		unitArtExists.Clear();
		unitActionExists.Clear();
	}

	// Forgets what's remembered by game object (unit prototypes), which would
	// otherwise keep the old game alive. A LAN client calls this whenever it
	// replaces its game with the host's snapshot.
	public static void ForgetGameObjects() {
		artNames.Clear();
	}
}

public partial class C7Animation {
	public AnimationManager animationManager { get; private set; }
	public string folderPath { get; private set; } // For example "Art/Units/Warrior" or "Art/Animations/Trajectory"
	public string iniFileName { get; private set; }
	// The art of a unit animation. The art name is kept rather than the unit,
	// so a long-lived animation doesn't keep the unit's game alive.
	private string artName;
	public AnimatedEffect effect;
	public MapUnit.AnimatedAction action { get; private set; }

	public C7Animation(AnimationManager civ3AnimData, MapUnit unit, MapUnit.AnimatedAction action) {
		string artName = AnimationManager.ArtNameFor(unit);
		this.animationManager = civ3AnimData;
		this.folderPath = "Art/Units/" + artName;
		// The same spelling as AnimationManager.getUnitINIData, though INIs are looked up ignoring case anyway.
		this.iniFileName = artName + ".INI";
		this.action = action;
		this.artName = artName;
	}

	public static readonly Dictionary<AnimatedEffect, string> effectCategories = new Dictionary<AnimatedEffect, string>
	{
		{ AnimatedEffect.Hit      , "Trajectory" },
		{ AnimatedEffect.Hit2     , "Trajectory" },
		{ AnimatedEffect.Hit3     , "Trajectory" },
		{ AnimatedEffect.Hit5     , "Trajectory" },
		{ AnimatedEffect.Miss     , "Trajectory" },
		{ AnimatedEffect.WaterMiss, "Trajectory" }
	};

	public static readonly Dictionary<AnimatedEffect, string> effectINIFileNames = new Dictionary<AnimatedEffect, string>
	{
		{ AnimatedEffect.Hit      , "hit.ini" },
		{ AnimatedEffect.Hit2     , "hit2.ini" },
		{ AnimatedEffect.Hit3     , "hit3.ini" },
		{ AnimatedEffect.Hit5     , "hit5.ini" },
		{ AnimatedEffect.Miss     , "miss.ini" },
		{ AnimatedEffect.WaterMiss, "water miss.ini" }
	};

	public C7Animation(AnimationManager civ3AnimData, AnimatedEffect effect) {
		this.animationManager = civ3AnimData;
		this.folderPath = "Art/Animations/" + effectCategories[effect];
		this.iniFileName = effectINIFileNames[effect];
		this.action = MapUnit.AnimatedAction.DEATH;
		this.effect = effect;
	}

	public IniData getINIData() {
		return animationManager.getINIData(folderPath + "/" + iniFileName);
	}

	private Util.FlicSheet? flicSheet = null;

	public Util.FlicSheet getFlicSheet() {
		flicSheet ??= artName != null
			? animationManager.GetUnitFlicSheet(artName, action)
			: animationManager.getFlicSheet(folderPath, getINIData(), action);
		return flicSheet.Value;
	}

	public void loadSpriteAnimation() {
		this.animationManager.LoadAnimation(this.artName, this.action);
	}

	public void loadEffectAnimation() {
		var path = animationManager.GetFlicFilePath(folderPath, getINIData(), action);
		this.animationManager.LoadAnimation(this.effect, this.action, path);
	}

	public void playSound() {
		// Our own unit art (standalone mode) has no sounds.
		if (artName != null && C7Engine.C7Settings.UseStandaloneMode()) {
			return;
		}
		animationManager.playSound(folderPath, getINIData(), action);
	}

	public double getDuration() {
		Util.FlicSheet flicSheet = getFlicSheet();
		return flicSheet.animationSpeed * flicSheet.framesPerAnimation;
	}

	public Vector2I GetFlicAnimationOffset() {
		Util.FlicSheet flicSheet = getFlicSheet();
		return new Vector2I(flicSheet.offsetLeft, flicSheet.offsetTop);
	}

	public Vector2I GetFlicAnimationOriginalSize() {
		Util.FlicSheet flicSheet = getFlicSheet();
		return new Vector2I(flicSheet.spriteOriginalWidth, flicSheet.spriteOriginalHeight);
	}
}
