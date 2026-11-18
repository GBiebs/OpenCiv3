using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using C7.Textures;
using C7Engine;
using C7Engine.Lua;
using ConvertCiv3Media;
using Godot;
using QueryCiv3;

public partial class Util {
	private static readonly Serilog.ILogger log = LogManager.ForContext<Util>();

	private static string civ3Root = GetCiv3Path();

	// Changing the root invalidates every path resolved against it.
	static public string Civ3Root {
		get => civ3Root;
		set {
			civ3Root = value;
			ClearMediaPathCache();
		}
	}

	static public string GetCiv3Path() {
		string path = C7Settings.GetSettingValue("locations", "civ3InstallDir");
		if (path != null) return path;

		return Civ3Location.GetCiv3Path();
	}

	// Checks if a file exists ignoring case on the latter parts of its path. If the file is found, returns its full path re-capitalized as
	// necessary, otherwise returns null. This function is needed for the game to work on Linux & Mac with the .NET Core runtime. It's not needed
	// on Windows, which has a case insensitive filesystem, or when using the Mono runtime, which emulates case insensitivity out of the
	// box. Arguments:
	//   exactCaseRoot: The first part of the file path, not made case-insensitive. This is intended be the root Civ 3 path from GetCiv3Path().
	//   ignoredCaseExtension: The second part of the file path that will be searched ignoring case.
	public static string FileExistsIgnoringCase(string exactCaseRoot, string ignoredCaseExtension) {
		if (exactCaseRoot == null || ignoredCaseExtension == null) {
			return null;
		}
		// In debug builds paths that are specified relative to the project directory
		// will start with res://. We want to turn those into real paths.
		if (exactCaseRoot.Contains("res://")) {
			exactCaseRoot = ProjectSettings.GlobalizePath(exactCaseRoot);
		}

		// If we aren't on windows, fix any windows path separators that may
		// appear in the path. This is particularly relevant for scenarios,
		//where the mod path frequently looks like  '..\conquests\Rise of Rome'.
		//
		// This is a bit of a hack, but it fixes scenario loading.
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			ignoredCaseExtension = ignoredCaseExtension.Replace('\\', '/');
		}

		// First try the basic built-in File.Exists method since it's adequate
		// in most cases.
		//
		// We use GetFullPath to handle any ../Conquests/<scenario name> bits
		// in the path, which happens with scenario mod paths.
		string fullPath = System.IO.Path.Combine(exactCaseRoot, ignoredCaseExtension);
		fullPath = System.IO.Path.GetFullPath(fullPath);
		if (System.IO.File.Exists(fullPath))
			return fullPath;

		// If that didn't work, do a case-insensitive search starting at the root path and stepping through each piece of the extension. Skip
		// this step if the root directory doesn't exist or if running on Windows.
		string tr = null;
		if ((!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) &&
			System.IO.Directory.Exists(exactCaseRoot)) {
			// Compare full paths, so a relative root or one ending in a
			// separator still lines up with the full path.
			string root = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(exactCaseRoot));
			// A path that climbs out of the root (e.g. a scenario's
			// ../<scenario name>) is searched for from the top of the file
			// system instead.
			if (!fullPath.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal)) {
				root = System.IO.Path.GetPathRoot(fullPath);
			}
			tr = root;

			// We need to update the ignored case extension before doing this
			// search, in case the ignored case extension previously had
			// ../Conquests/<scenario name> in it.
			//
			// If we didn't do this we'd end up with ".." as one of our steps
			// below, which derails the searching logic.
			//
			// We also strip any leading slashes, which can show up if the civ3
			// root doesn't end in a slash.
			ignoredCaseExtension = fullPath.Substring(root.Length);
			ignoredCaseExtension = ignoredCaseExtension.TrimPrefix("\\").TrimPrefix("/");

			foreach (string step in ignoredCaseExtension.Replace('\\', '/').Split('/')) {
				string goal = System.IO.Path.Combine(tr, step);
				string match = null;
				foreach (string entry in ListDirectory(tr)) {
					if (entry.Equals(goal, StringComparison.OrdinalIgnoreCase)) {
						match = entry;
						break;
					}
				}

				if (match != null)
					tr = match;
				else {
					tr = null;
					break;
				}
			}
		}
		return tr;
	}

	// The entries of the directories searched by FileExistsIgnoringCase. Media
	// files don't change while the game runs, so a directory is listed only
	// once instead of once per path segment per lookup.
	private static readonly Dictionary<string, string[]> directoryListings = new();

	private static string[] ListDirectory(string directory) {
		if (!directoryListings.TryGetValue(directory, out string[] entries)) {
			try {
				entries = System.IO.Directory.GetFileSystemEntries(directory, "*");
			} catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException) {
				// A directory that can't be listed has nothing to find in it.
				entries = [];
			}
			directoryListings[directory] = entries;
		}
		return entries;
	}

	/// <summary>
	/// Sets the Civ3 legacy mod path, or clears it when given null or an empty path.
	/// This is here so Civ3MediaPath can refer to it, without having to grab it from all the places we might need to call
	/// it, which is in 25 places currently. It must be set (or cleared) for every game created or loaded, or a game
	/// would go on using the art of the scenario played before it.
	/// </summary>
	private static string modPath;
	public static void setModPath(string modPathParam) {
		modPath = modPathParam;
		// Specifically fix up the conquests mod path for case sensitive
		// platforms. If we didn't do this then our path searching logic below
		// would find the default PediaIcons.txt instead of the scenario
		// specific file.
		modPath = modPath?.Replace("\\conquests\\", "\\Conquests\\");
		// A new game or scenario: its file's folder, if any, is set after.
		scenarioFolder = null;
		ClearMediaPathCache();
	}

	// Sets the scenario's search folders and returns its PediaIcons file:
	// its own if its folders have one, else Civ3's, else the game's own
	// (standalone mode's Civilopedia). Without Civ3 the game's media has no
	// PediaIcons file, so a scenario with search folders but none of its own
	// (one made in the editor, with only art) mustn't ask for one.
	public static string PediaIconsPathFor(string searchPath) {
		setModPath(searchPath);
		if (HasCiv3Media("Text/PediaIcons.txt")) {
			return Civ3MediaPath("Text/PediaIcons.txt");
		}
		string civ3s = System.IO.Path.Combine(Civ3Root ?? "", "Conquests", "Text", "PediaIcons.txt");
		if (!string.IsNullOrEmpty(Civ3Root) && System.IO.File.Exists(civ3s)) {
			return civ3s;
		}
		return ProjectSettings.GlobalizePath("res://Civilopedia/Text/PediaIcons.txt");
	}

	// Whether a scenario's mod path is set.
	public static bool HasModPath => !string.IsNullOrEmpty(modPath);

	// The folder of the scenario file being played or edited, where its
	// search folders are looked for first (see ScenarioRoots), or null.
	private static string scenarioFolder;
	public static void setScenarioFolder(string folder) {
		scenarioFolder = string.IsNullOrEmpty(folder) ? null : folder;
		ClearMediaPathCache();
	}

	// The game's own Scenarios folder, where Load Scenario and the editor
	// keep scenarios.
	public static string GameScenariosFolder => System.IO.Path.Combine(C7Settings.WritableDirectory, "Scenarios");

	// Where a scenario's search folders (its mod path) are looked for before
	// Civ3's folders: next to the scenario file, and in the game's Scenarios
	// folder. A scenario saved as Scenarios/Name.json with its art in
	// Scenarios/Name/Art/... (search folder "Name") finds it there, with or
	// without Civ3, wherever the game is installed.
	private static IEnumerable<string> ScenarioRoots() {
		if (scenarioFolder != null) {
			yield return scenarioFolder;
		}
		yield return GameScenariosFolder;
	}

	// The media in the scenario's own folders (not Civ3's), or null.
	private static string FindInScenarioFolders(string mediaPath) {
		if (string.IsNullOrEmpty(modPath) || string.IsNullOrEmpty(mediaPath)) {
			return null;
		}
		foreach (string path in modPath.Split(";")) {
			if (string.IsNullOrWhiteSpace(path)) {
				continue;
			}
			foreach (string root in ScenarioRoots()) {
				if (FileExistsIgnoringCase(root, System.IO.Path.Combine(path.Trim(), mediaPath)) is string found) {
					return found;
				}
			}
		}
		return null;
	}

	// The media if the scenario has its own copy in its folders next to the
	// scenario file or in the game's Scenarios folder, else null.
	public static string ScenarioMediaPath(string mediaPath) => FindInScenarioFolders(mediaPath);

	// Civ3MediaPath results by media path. A null value means the media
	// couldn't be found. The results are only valid for the current roots,
	// mod path and standalone setting, so they're forgotten when any of those
	// change.
	private static readonly Dictionary<string, string> mediaPathCache = new();
	private static bool mediaPathCacheStandalone;

	private static void ClearMediaPathCache() {
		mediaPathCache.Clear();
		scenarioReplacesStock.Clear();
		directoryListings.Clear();
	}

	/// <summary>
	/// Pass this function a relative path (e.g. Art/Terrain/xpgc.pcx) and it will grab the correct version
	/// Assumes Conquests/Complete
	/// </summary>
	/// <param name="mediaPath">The media path, e.g. Art/Units/units_32.pcx</param>
	/// <returns>The path to the media on the file system, or an exception if it cannot be found</returns>
	/// <exception cref="ApplicationException"></exception>
	public static string Civ3MediaPath(string mediaPath) {
		string result = CachedCiv3MediaPath(mediaPath);
		if (result == null)
			throw new ApplicationException("Media path not found: " + mediaPath);
		return result;
	}

	// Whether Civ3MediaPath can find the media.
	public static bool HasCiv3Media(string mediaPath) {
		return CachedCiv3MediaPath(mediaPath) != null;
	}

	private static string CachedCiv3MediaPath(string mediaPath) {
		bool standalone = C7Settings.UseStandaloneMode();
		if (standalone != mediaPathCacheStandalone) {
			ClearMediaPathCache();
			mediaPathCacheStandalone = standalone;
		}

		if (!mediaPathCache.TryGetValue(mediaPath, out string result)) {
			result = FindCiv3MediaPath(mediaPath, standalone);
			mediaPathCache[mediaPath] = result;
		}
		return result;
	}

	// The uncached search behind Civ3MediaPath. Returns null if the media can't
	// be found.
	private static string FindCiv3MediaPath(string mediaPath, bool standalone) {
		//First, check if the file exists via a scenario's mod path
		//For now this is only checked relative to Civ3, not relative to C7.
		if (FindInScenarioFolders(mediaPath) is string own) {
			return own;
		}
		if (!string.IsNullOrEmpty(modPath)) {
			string[] paths = modPath.Split(";");
			foreach (string path in paths) {
				string[] tryPaths = new string[] {
					path,
					// Needed for some reason as Steam version at least puts some mod art in Extras instead of Scenarios
					//  Also, the case mismatch is intentional. C3C makes a capital C path, but it's lower-case on the filesystem
					"Conquests/Conquests/" + path, "Conquests/Scenarios/" + path, "civ3PTW/Scenarios/" + path
				};
				for (int i = 0; i < tryPaths.Length; i++) {
					string actualCasePath = CheckForCiv3Media(mediaPath, tryPaths[i]);
					if (actualCasePath != null)
						return actualCasePath;
				}
			}
		}

		// Next, before trying the base Civ paths, see if we have it packaged
		// with C7 and are in standalone mode.
		string c7BasePath = GetC7AssetsPath();
		string c7Path = FileExistsIgnoringCase(c7BasePath, mediaPath);

		if (standalone) {
			return c7Path;
		}

		//Next, check the base Civ paths
		string[] basePaths = new string[] {
			"Conquests",
			"civ3PTW",
			""
		};
		for (int i = 0; i < basePaths.Length; i++) {
			string actualCasePath = CheckForCiv3Media(mediaPath, basePaths[i]);
			if (actualCasePath != null)
				return actualCasePath;
		}

		// Finally, use a c7 path even if we aren't in standalone mode, but only
		// if we can't find the path in civ3 graphics. This allows us to toggle
		// to c7 art when we aren't in standalone mode.
		return c7Path;
	}

	// Whether the scenario being played has its own file for the media in
	// place of one of the game's (as MDJ has its own resources.pcx, from the
	// Sengoku conquest). The remade art of the Graphics Overhaul remakes the
	// game's files, so it mustn't stand in for a scenario's (see
	// ModernGraphics.UsedFor). Art only a scenario has, like its own units,
	// may have been remade from it, and isn't replacing anything.
	private static readonly Dictionary<string, bool> scenarioReplacesStock = new();

	public static bool ScenarioReplacesStockArt(string mediaPath) {
		if (string.IsNullOrEmpty(modPath)) {
			return false;
		}
		if (!scenarioReplacesStock.TryGetValue(mediaPath, out bool result)) {
			result = ScenarioHasArt(mediaPath) && new[] { "Conquests", "civ3PTW", "" }.Any(b => CheckForCiv3Media(mediaPath, b) != null);
			scenarioReplacesStock[mediaPath] = result;
		}
		return result;
	}

	// Whether the scenario being played has its own file for the media,
	// whether or not the game has one too.
	public static bool ScenarioHasArt(string mediaPath) {
		if (string.IsNullOrEmpty(modPath) || string.IsNullOrEmpty(mediaPath)) {
			return false;
		}
		if (FindInScenarioFolders(mediaPath) != null) {
			return true;
		}
		foreach (string path in modPath.Split(";")) {
			foreach (string tryPath in new[] { path, "Conquests/Conquests/" + path, "Conquests/Scenarios/" + path, "civ3PTW/Scenarios/" + path }) {
				if (CheckForCiv3Media(mediaPath, tryPath) != null) {
					return true;
				}
			}
		}
		return false;
	}

	private static string CheckForCiv3Media(string relPath, string rootPath) {
		// Combine TryPaths[i] and relPath. Make sure not to leave an erroneous forward slash at the start if TryPaths[i] is empty
		string fullPath = rootPath != "" ? rootPath + "/" + relPath : relPath;

		return FileExistsIgnoringCase(Civ3Root, fullPath);
	}

	static public (ImageTexture, ImageTexture) LoadTextureFromFlicData(byte[] image, byte[,] pallete, int width, int height) {
		var (baseImage, tintImage) = PCXToGodot.ByteArrayWithTintToImage(image, pallete, width, height, shadows: true);
		return (ImageTexture.CreateFromImage(baseImage), ImageTexture.CreateFromImage(tintImage));
	}

	// Decoded files are only needed until their textures have been built, so
	// a decoded file is only kept while it's in recent use (for example while
	// the frames of all of a unit's animations are being loaded).
	private static readonly RecentlyUsedCache<Flic> flicCache = new(4);

	static public Flic LoadFlic(string path) {
		if (flicCache.TryGet(path, out Flic result)) {
			return result;
		}
		result = new ConvertCiv3Media.Flic(Util.Civ3MediaPath(path));
		flicCache.Add(path, result);
		return result;
	}

	// A cache that keeps strong references to the few most recently used
	// values, and only weak references to the rest. Used for decoded media
	// files, which can be large, are expensive to decode and are typically
	// used a few times in quick succession and then not again once the
	// textures made from them exist.
	public sealed class RecentlyUsedCache<T> where T : class {
		// The recently used values, most recently used first.
		private readonly string[] recentKeys;
		private readonly T[] recentValues;
		private int recentCount = 0;
		private readonly Dictionary<string, WeakReference<T>> weak = new();

		// The weak references to collected values are dropped when there are
		// this many references, so they don't pile up.
		private const int MinPruneThreshold = 64;
		private int pruneThreshold = MinPruneThreshold;

		public RecentlyUsedCache(int recentCapacity) {
			recentKeys = new string[recentCapacity];
			recentValues = new T[recentCapacity];
		}

		public bool TryGet(string key, out T value) {
			for (int i = 0; i < recentCount; i++) {
				if (recentKeys[i] == key) {
					value = recentValues[i];
					MoveToFront(i);
					return true;
				}
			}
			if (weak.TryGetValue(key, out WeakReference<T> reference)) {
				if (reference.TryGetTarget(out value)) {
					KeepRecent(key, value);
					return true;
				}
				weak.Remove(key);
			}
			value = null;
			return false;
		}

		public void Add(string key, T value) {
			if (!weak.ContainsKey(key) && weak.Count >= pruneThreshold) {
				PruneCollected();
			}
			weak[key] = new WeakReference<T>(value);
			KeepRecent(key, value);
		}

		// Makes the value the most recently used one, dropping the least
		// recently used if there's no room.
		private void KeepRecent(string key, T value) {
			if (recentKeys.Length == 0) {
				return;
			}
			int index = Array.IndexOf(recentKeys, key, 0, recentCount);
			if (index < 0) {
				if (recentCount < recentKeys.Length) {
					recentCount++;
				}
				index = recentCount - 1;
			}
			recentKeys[index] = key;
			recentValues[index] = value;
			MoveToFront(index);
		}

		private void MoveToFront(int index) {
			if (index == 0) {
				return;
			}
			string key = recentKeys[index];
			T value = recentValues[index];
			Array.Copy(recentKeys, 0, recentKeys, 1, index);
			Array.Copy(recentValues, 0, recentValues, 1, index);
			recentKeys[0] = key;
			recentValues[0] = value;
		}

		// Drops the weak references whose values have been collected.
		private void PruneCollected() {
			List<string> collected = null;
			foreach ((string key, WeakReference<T> reference) in weak) {
				if (!reference.TryGetTarget(out _)) {
					(collected ??= new()).Add(key);
				}
			}
			if (collected != null) {
				foreach (string key in collected) {
					weak.Remove(key);
				}
			}
			// Prune again once the cache has doubled, so pruning stays cheap
			// on average even if most values are still alive.
			pruneThreshold = Math.Max(MinPruneThreshold, weak.Count * 2);
		}

		public void Clear() {
			Array.Clear(recentKeys);
			Array.Clear(recentValues);
			recentCount = 0;
			weak.Clear();
			pruneThreshold = MinPruneThreshold;
		}
	}

	static private string GetC7AssetsPath() {
		if (OS.HasFeature("editor")) {
			return "res://Assets";
		}

		return System.IO.Path.Combine(GamePaths.BaseDir, "Assets");
	}

	// Replaces image colors based on a given dictionary
	public static Image TransformColors(Image origin, Dictionary<Color, Color> colorReplacements) {
		if (origin.GetFormat() == Image.Format.Rgba8 && !origin.HasMipmaps()) {
			return TransformRgba8Colors(origin, colorReplacements);
		}

		Image result = (Image)origin.Duplicate();

		for (int y = 0; y < origin.GetHeight(); ++y) {
			for (int x = 0; x < origin.GetWidth(); ++x) {
				Color origin_color = origin.GetPixel(x, y);

				if (colorReplacements.TryGetValue(origin_color, out Color new_color)) {
					result.SetPixel(x, y, new_color);
				}
			}
		}

		return result;
	}

	// TransformColors for RGBA8 images, working on the image's bytes instead
	// of getting and setting each pixel through the engine. The colors are
	// converted to and from bytes by the engine itself, so the result is the
	// same as getting and setting the pixels one at a time.
	private static Image TransformRgba8Colors(Image origin, Dictionary<Color, Color> colorReplacements) {
		Image probe = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
		Dictionary<uint, uint> byteReplacements = new();
		foreach ((Color from, Color to) in colorReplacements) {
			// The only pixel value GetPixel could turn into `from` is the
			// nearest one, if GetPixel actually turns it into `from`.
			byte[] fromBytes = [ToByte(from.R), ToByte(from.G), ToByte(from.B), ToByte(from.A)];
			probe.SetData(1, 1, false, Image.Format.Rgba8, fromBytes);
			if (probe.GetPixel(0, 0) != from) {
				continue;
			}
			probe.SetPixel(0, 0, to);
			byteReplacements[BitConverter.ToUInt32(fromBytes)] = BitConverter.ToUInt32(probe.GetData());
		}

		byte[] data = origin.GetData();
		if (byteReplacements.Count > 0) {
			Span<uint> pixels = MemoryMarshal.Cast<byte, uint>(data.AsSpan());
			for (int i = 0; i < pixels.Length; i++) {
				if (byteReplacements.TryGetValue(pixels[i], out uint replacement)) {
					pixels[i] = replacement;
				}
			}
		}
		return Image.CreateFromData(origin.GetWidth(), origin.GetHeight(), false, Image.Format.Rgba8, data);
	}

	private static byte ToByte(float channel) {
		return (byte)Math.Clamp(Math.Round(channel * 255.0), 0, 255);
	}

	// Creates a texture from raw palette data. The data must be 256 pixels by 3 channels. Returns a 16x16 unfiltered RGB texture.
	public static ImageTexture createPaletteTexture(byte[,] raw) {
		if ((raw.GetLength(0) != 256) || (raw.GetLength(1) != 3))
			throw new Exception("Invalid palette dimensions. Palettes must be 256x3.");

		// Flatten palette data since CreateFromData can't accept two-dimensional arrays
		byte[] flatPalette = new byte[3*256];
		for (int n = 0; n < 256; n++)
			for (int k = 0; k < 3; k++)
				flatPalette[k + 3 * n] = raw[n, k];

		var img = Image.CreateFromData(16, 16, false, Image.Format.Rgb8, flatPalette);
		return ImageTexture.CreateFromImage(img);
	}

	// A FlicSheet holds the header values of a Flic file: the size, offset,
	// number of frames and timing of its animations. The frames themselves are
	// turned into SpriteFrames by the AnimationManager.
	public struct FlicSheet {
		public int spriteOriginalWidth, spriteOriginalHeight;
		public int spriteWidth, spriteHeight;
		public int offsetLeft, offsetTop;
		public int framesPerAnimation, numberOfAnimations;
		public int animationSpeed, animationTime;
	}

	// The size of a Flic file's header, and how much of it LoadFlicHeader needs.
	private const int FlicHeaderSize = 128;
	private const int FlicHeaderBytesUsed = 110;

	private static readonly Dictionary<string, FlicSheet> flicHeaderCache = new();

	// Reads the header values of a Flic file, without decoding its frames. The
	// values are the same as those of the Flic loaded from the same path.
	public static FlicSheet LoadFlicHeader(string filePath) {
		if (flicHeaderCache.TryGetValue(filePath, out FlicSheet sheet)) {
			return sheet;
		}

		byte[] header = new byte[FlicHeaderSize];
		using (var stream = System.IO.File.OpenRead(Util.Civ3MediaPath(filePath))) {
			stream.ReadAtLeast(header, FlicHeaderBytesUsed, throwOnEndOfStream: true);
		}

		// The offsets match those read by ConvertCiv3Media.Flic.Load.
		int fileFormat = BitConverter.ToUInt16(header, 4);
		if (fileFormat != 0xaf12) {
			throw new ApplicationException("Flic version # " + fileFormat.ToString("X4") + "does not match 0xaf12");
		}

		int numFrames = BitConverter.ToUInt16(header, 6);
		int numAnimations = BitConverter.ToUInt16(header, 0x60);
		int framesPerAnimation = BitConverter.ToUInt16(header, 0x62);
		// Leaderheads don't have the Civ3-specific values, and act like a regular Flic
		if (numAnimations == 0) {
			numAnimations = 1;
			framesPerAnimation = numFrames;
		}

		sheet = new FlicSheet {
			spriteOriginalWidth = BitConverter.ToUInt16(header, 104),
			spriteOriginalHeight = BitConverter.ToUInt16(header, 106),
			spriteWidth = BitConverter.ToUInt16(header, 8),
			spriteHeight = BitConverter.ToUInt16(header, 10),
			offsetLeft = BitConverter.ToUInt16(header, 100),
			offsetTop = BitConverter.ToUInt16(header, 102),
			framesPerAnimation = framesPerAnimation,
			numberOfAnimations = numAnimations,
			animationSpeed = BitConverter.ToInt32(header, 16),
			animationTime = BitConverter.ToUInt16(header, 108),
		};
		flicHeaderCache[filePath] = sheet;
		return sheet;
	}

	// Loads the header values of a Flic, along with the decoded Flic.
	public static (FlicSheet, Flic) loadFlicSheet(string filePath) {
		return (LoadFlicHeader(filePath), LoadFlic(filePath));
	}

	// The media paths that couldn't be loaded, so each is only warned about
	// once (sounds are looked up again each time they play).
	private static readonly HashSet<string> warnedMissingMedia = new();

	private static void WarnOnce(Exception e, string kind, string path) {
		lock (warnedMissingMedia) {
			if (!warnedMissingMedia.Add(path)) {
				return;
			}
		}
		// Standalone mode has no Civ3 sounds, and only some of its own: a
		// sound it lacks is expected, not worth a warning and a stack trace
		// in the log each time a new one is asked for.
		if (C7Settings.UseStandaloneMode() && !HasCiv3Media(path)) {
			log.Debug("No {Kind} {Path} in standalone mode", kind, path);
			return;
		}
		log.Warning(e, "Couldn't load {Kind} {Path}", kind, path);
	}

	// Like LoadWAVFromDisk, but the path is a relative path, not the result of
	// calling Civ3MediaPath.
	//
	// The result may be null if modern graphics are active, as we do not yet
	// have sound replacements.
	public static AudioStreamWav? LoadCiv3WAVFromDisk(string path) {
		try {
			return LoadWAVFromDisk(Civ3MediaPath(path));
		} catch (Exception e) {
			WarnOnce(e, "sound", path);
			return null;
		}
	}

	private const ushort WavePcm = 1, WaveMsAdpcm = 2, WaveImaAdpcm = 0x11;

	// Loads a WAV file: PCM of 8 to 32 bits, or Microsoft or IMA ADPCM, which
	// are decoded to 16 bit PCM (Godot's own IMA ADPCM isn't laid out like a
	// WAV file's).
	public static AudioStreamWav LoadWAVFromDisk(string path) {
		using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
		if (file == null) {
			throw new Exception($"Couldn't open {path}: {FileAccess.GetOpenError()}");
		}

		byte[] riffBytes = file.GetBuffer(4);
		if (!"RIFF"u8.SequenceEqual(riffBytes)) {
			throw new Exception("Unsupported file, missing 'RIFF' tag");
		}
		file.Get32();   // The file size, minus 8 bytes

		byte[] waveBytes = file.GetBuffer(4);
		if (!"WAVE"u8.SequenceEqual(waveBytes)) {
			throw new Exception("Unsupported file, missing 'WAVE' tag");
		}

		ushort compressionCode = 0, channels = 0, blockAlign = 0, formatBits = 0;
		int sampleRate = 0;
		byte[] formatExtra = [];
		byte[] data = null;

		ulong length = file.GetLength();
		while (file.GetPosition() + 8 <= length) {
			byte[] chunkBytes = file.GetBuffer(4);
			uint chunkSize = file.Get32();
			ulong position = file.GetPosition();
			// A chunk can't go past the end of the file.
			uint available = (uint)Math.Min(chunkSize, length - position);

			if ("fmt "u8.SequenceEqual(chunkBytes)) {
				if (available < 16) {
					throw new Exception("The format chunk is too short");
				}
				compressionCode = file.Get16();
				channels = file.Get16();
				sampleRate = (int)file.Get32();
				file.Get32();   // The average bytes per second
				blockAlign = file.Get16();
				formatBits = file.Get16();
				if (available >= 18) {
					ushort extraSize = file.Get16();
					formatExtra = file.GetBuffer(Math.Min(extraSize, available - 18));
				}
			} else if ("data"u8.SequenceEqual(chunkBytes)) {
				data = file.GetBuffer(available);
			}

			// Chunks are padded to an even size.
			file.Seek(position + chunkSize + (chunkSize & 1));
		}

		if (channels == 0 || data == null) {
			throw new Exception("Failed to find both the format and data chunks");
		}
		if (channels > 2) {
			throw new Exception("Only mono and stereo WAV files are supported");
		}

		AudioStreamWav wav = new() {
			Stereo = channels == 2,
			MixRate = sampleRate,
		};
		switch (compressionCode) {
			case WavePcm when formatBits == 8:
				// WAV's 8 bit samples are unsigned, Godot's are signed.
				for (int i = 0; i < data.Length; i++) {
					data[i] ^= 0x80;
				}
				wav.Format = AudioStreamWav.FormatEnum.Format8Bits;
				wav.Data = data;
				break;
			case WavePcm when formatBits == 16:
				wav.Format = AudioStreamWav.FormatEnum.Format16Bits;
				wav.Data = data;
				break;
			case WavePcm when formatBits is 24 or 32:
				wav.Format = AudioStreamWav.FormatEnum.Format16Bits;
				wav.Data = PcmTo16Bits(data, formatBits / 8);
				break;
			case WaveMsAdpcm:
				wav.Format = AudioStreamWav.FormatEnum.Format16Bits;
				wav.Data = DecodeMsAdpcm(data, channels, blockAlign, formatExtra);
				break;
			case WaveImaAdpcm:
				wav.Format = AudioStreamWav.FormatEnum.Format16Bits;
				wav.Data = DecodeImaAdpcm(data, channels, blockAlign);
				break;
			default:
				throw new Exception($"Unsupported WAV format {compressionCode} with {formatBits} bits per sample");
		}
		return wav;
	}

	// Keeps the two most significant bytes of each little-endian sample.
	private static byte[] PcmTo16Bits(byte[] data, int bytesPerSample) {
		int samples = data.Length / bytesPerSample;
		byte[] result = new byte[samples * 2];
		for (int i = 0; i < samples; i++) {
			int last = i * bytesPerSample + bytesPerSample - 1;
			result[2 * i] = data[last - 1];
			result[2 * i + 1] = data[last];
		}
		return result;
	}

	private static short ClampToShort(int value) {
		return (short)Math.Clamp(value, short.MinValue, short.MaxValue);
	}

	private static void WriteSample(List<byte> output, int sample) {
		short s = ClampToShort(sample);
		output.Add((byte)s);
		output.Add((byte)(s >> 8));
	}

	private static readonly int[] msAdpcmAdaptation = [230, 230, 230, 230, 307, 409, 512, 614, 768, 614, 512, 409, 307, 230, 230, 230];
	private static readonly (int, int)[] msAdpcmDefaultCoefficients = [(256, 0), (512, -256), (0, 0), (192, 64), (240, 0), (460, -208), (392, -232)];

	// Decodes Microsoft ADPCM into interleaved 16 bit samples.
	private static byte[] DecodeMsAdpcm(byte[] data, int channels, int blockAlign, byte[] formatExtra) {
		// The format's extra bytes hold the samples per block, then the
		// prediction coefficients.
		(int, int)[] coefficients = msAdpcmDefaultCoefficients;
		if (formatExtra.Length >= 4) {
			int count = BitConverter.ToUInt16(formatExtra, 2);
			if (count > 0 && formatExtra.Length >= 4 + 4 * count) {
				coefficients = new (int, int)[count];
				for (int i = 0; i < count; i++) {
					coefficients[i] = (BitConverter.ToInt16(formatExtra, 4 + 4 * i), BitConverter.ToInt16(formatExtra, 6 + 4 * i));
				}
			}
		}

		int headerSize = 7 * channels;
		if (blockAlign <= headerSize) {
			throw new Exception("Invalid MS ADPCM block size");
		}
		List<byte> output = new(data.Length * 4);
		int[] coef1 = new int[channels], coef2 = new int[channels], delta = new int[channels];
		int[] sample1 = new int[channels], sample2 = new int[channels];

		for (int block = 0; block + headerSize <= data.Length; block += blockAlign) {
			int end = Math.Min(block + blockAlign, data.Length);
			int p = block;
			for (int c = 0; c < channels; c++) {
				(coef1[c], coef2[c]) = coefficients[Math.Min(data[p++], coefficients.Length - 1)];
			}
			for (int c = 0; c < channels; c++, p += 2) {
				delta[c] = BitConverter.ToInt16(data, p);
			}
			for (int c = 0; c < channels; c++, p += 2) {
				sample1[c] = BitConverter.ToInt16(data, p);
			}
			for (int c = 0; c < channels; c++, p += 2) {
				sample2[c] = BitConverter.ToInt16(data, p);
			}
			// The header's samples come first, the older one first.
			for (int c = 0; c < channels; c++) {
				WriteSample(output, sample2[c]);
			}
			for (int c = 0; c < channels; c++) {
				WriteSample(output, sample1[c]);
			}

			// Each byte holds two samples, high nibble first, alternating
			// between the channels in stereo.
			int channel = 0;
			for (; p < end; p++) {
				foreach (int nibble in new[] { data[p] >> 4, data[p] & 0xF }) {
					int signedNibble = nibble >= 8 ? nibble - 16 : nibble;
					int predicted = (sample1[channel] * coef1[channel] + sample2[channel] * coef2[channel]) >> 8;
					int sample = ClampToShort(predicted + signedNibble * delta[channel]);
					WriteSample(output, sample);
					sample2[channel] = sample1[channel];
					sample1[channel] = sample;
					delta[channel] = Math.Max(16, (msAdpcmAdaptation[nibble] * delta[channel]) >> 8);
					channel = (channel + 1) % channels;
				}
			}
		}
		return output.ToArray();
	}

	private static readonly int[] imaIndexTable = [-1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8];
	private static readonly int[] imaStepTable = [
		7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107, 118,
		130, 143, 157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796, 876, 963, 1060,
		1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484,
		7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767,
	];

	// Decodes IMA ADPCM, as laid out in WAV files, into interleaved 16 bit
	// samples.
	private static byte[] DecodeImaAdpcm(byte[] data, int channels, int blockAlign) {
		int headerSize = 4 * channels;
		if (blockAlign <= headerSize) {
			throw new Exception("Invalid IMA ADPCM block size");
		}
		List<byte> output = new(data.Length * 4);
		int[] predictor = new int[channels], index = new int[channels];
		// A block's samples, by channel, before they're interleaved.
		List<int>[] decoded = new List<int>[channels];
		for (int c = 0; c < channels; c++) {
			decoded[c] = new();
		}

		for (int block = 0; block + headerSize <= data.Length; block += blockAlign) {
			int end = Math.Min(block + blockAlign, data.Length);
			int p = block;
			for (int c = 0; c < channels; c++, p += 4) {
				predictor[c] = BitConverter.ToInt16(data, p);
				index[c] = Math.Clamp((int)data[p + 2], 0, imaStepTable.Length - 1);
				decoded[c].Clear();
				decoded[c].Add(predictor[c]);
			}

			// Each channel has four bytes in turn, eight samples, low nibble
			// first.
			while (p + 4 * channels <= end) {
				for (int c = 0; c < channels; c++) {
					for (int i = 0; i < 4; i++, p++) {
						foreach (int nibble in new[] { data[p] & 0xF, data[p] >> 4 }) {
							int step = imaStepTable[index[c]];
							int diff = step >> 3;
							if ((nibble & 1) != 0) diff += step >> 2;
							if ((nibble & 2) != 0) diff += step >> 1;
							if ((nibble & 4) != 0) diff += step;
							predictor[c] = ClampToShort((nibble & 8) != 0 ? predictor[c] - diff : predictor[c] + diff);
							index[c] = Math.Clamp(index[c] + imaIndexTable[nibble], 0, imaStepTable.Length - 1);
							decoded[c].Add(predictor[c]);
						}
					}
				}
			}

			for (int i = 0; i < decoded[0].Count; i++) {
				for (int c = 0; c < channels; c++) {
					WriteSample(output, decoded[c][i]);
				}
			}
		}
		return output.ToArray();
	}

	public static AudioStreamMP3? LoadCiv3Mp3FromDisk(string path) {
		try {
			return LoadMp3FromDisk(Civ3MediaPath(path));
		} catch (Exception e) {
			WarnOnce(e, "music", path);
			return null;
		}
	}

	public static AudioStreamMP3 LoadMp3FromDisk(string path) {
		AudioStreamMP3 mp3 = AudioStreamMP3.LoadFromFile(path);
		mp3.Loop = true; // TODO: beyond simple path loading: add looping as a property
		return mp3;
	}

	public static AudioStreamOggVorbis? LoadCiv3OggFromDisk(string path) {
		try {
			return LoadOggFromDisk(Civ3MediaPath(path));
		} catch (Exception e) {
			WarnOnce(e, "music", path);
			return null;
		}
	}

	public static AudioStreamOggVorbis LoadOggFromDisk(string path) {
		AudioStreamOggVorbis ogg = AudioStreamOggVorbis.LoadFromFile(path);
		ogg.Loop = true;
		return ogg;
	}


	// This method is intended for use within overrides of Godot object _ValidateProperty method.
	// Its purpose is to prevent values of properties listed in validProperties from being saved as
	// part of the scene.  It's useful when using [Tool] scripts to execute code in editor. It
	// allows to load Civ3 textures as part of such scripts, but prevents these textures from being
	// saved into the scene.
	//
	// See Godot docs on _ValidateProperty:
	// https://docs.godotengine.org/en/4.2/classes/class_object.html#class-object-private-method-validate-property
	public static void ApplyNoSaveFlag(Godot.Collections.Dictionary property, HashSet<StringName> validProperties) {
		StringName propertyName = property["name"].AsStringName();

		if (validProperties.Contains(propertyName)) {
			property["usage"] = (int)PropertyUsageFlags.NoInstanceState;
		}
	}

	// Shows an error in a plain dialog window over the given node, for screens
	// that have no popup overlay of their own, like the new game setup.
	public static void ShowErrorDialog(Node parent, string title, string message) {
		AcceptDialog dialog = new() {
			Title = title,
			DialogText = message,
			Exclusive = true,
		};
		dialog.Confirmed += dialog.QueueFree;
		dialog.Canceled += dialog.QueueFree;
		parent.AddChild(dialog);
		dialog.PopupCentered();
	}

	// Allow clearing the caches, so that scenarios with different files that
	// have the same name can be loaded independently.
	public static void ClearCaches() {
		flicCache.Clear();
		flicHeaderCache.Clear();
		ClearMediaPathCache();
		PCXToGodot.ClearCache();
		TextureLoader.ClearCache();
		ModernGraphics.ClearCache();
		CivilopediaArt.ClearCache();
		AnimationManager.ClearCache();
		PlayerTextureUtil.ClearCache();
		UICaches.Clear();
		C7.Map.CityScene.ClearTextureCache();
	}
}
