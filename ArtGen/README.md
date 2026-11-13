# ArtGen: the remade ("modern") map art

These scripts draw the art for the **Graphics Overhaul** setting (Main Menu →
Settings → Graphics). The art is drawn with code (procedural textures,
lighting, and a small software 3D renderer for units and cities) and written
to `C7/ModernArt/`, laid out like the game's media folders. With the setting
on, the game uses a remade file wherever one exists and falls back to the
original Civ3 art everywhere else. See `C7/Textures/ModernGraphics.cs`.

Build everything: `python ArtGen/build_all.py`. Each area can also be built on
its own: `python ArtGen/<area>/build.py`.

`civilopedia_icons/` is the odd one out: it makes the Civilopedia's icons for
standalone mode (a 128x128 and a 32x32 picture per entry, in
`C7/Civilopedia/Art/Civilopedia/Icons/`, listed in
`C7/Civilopedia/Text/PediaIcons.txt`) from the other areas' models and art,
so `build_all.py` runs it last. See its `build.py`. Its `ui_icons.py` makes
the advance and building icons of the tech tree, production menus and city
screen in standalone mode from the same pictures (in `C7/ModernArt`, listed
in `C7/Lua/standalone/icon_files.lua`).

`ui_buttons/` draws the unit order buttons standalone mode's asset pack lacks,
into `C7/ModernArt/Art/interface/`, which `C7/Lua/standalone/textures.lua`
uses: Build Outpost, Build Colony, Build Airfield, Build Radar Tower, Airlift,
Sentry, Sentry (enemy only), Load, Unload, Build Army, Hurry Building, Upgrade
and Build Barricade, each a file of its own, and the air missions and the
airdrop, together in `AirMissionButtons.png`. It also draws the icons by a
city for its barracks, airport and harbor, into
`C7/ModernArt/Art/Cities/CityBuildingIcons.png`.

`screens/` remakes the art of two screens the owner asked for in the remade
style: the Wonders of the World screen's background and box
(`Art/Advisors/wonders_background.png`, `wondersBOX.png`), and the city
screen's icons, draft button and governor button (`Art/city screen/CityIcons.png`,
`draftButton.png`, `govern.png`). Like the map art they keep Civ3's layout at
twice the size, so they're used with the Modern graphics option, and in
standalone mode for whatever the asset pack lacks. It also draws the
end-of-game screen (`screens/victory.py`, into `Art/VictoryScreen/`): a
background for each kind of ending with niches where Civ3 puts the heads,
the speech bubble, and our own frames, heart badge, close and Next buttons
for the remade portraits, which that screen uses with Modern graphics on and
always in standalone mode. See its `build.py`.

## The contract with the game

* **Same file, same layout, twice the pixels.** The remake of
  `Art/Terrain/xtgc.pcx` is `C7/ModernArt/Art/Terrain/xtgc.png`, exactly
  2× the original's width and height. Every sprite sits in its original cell,
  scaled by 2, because the game crops sprite sheets with the original's
  coordinates (×2). Use `civ3art.save_modern(rel, rgba)`, which checks the size.
* **Same footprint.** A sprite is drawn where the original's was, so it must
  occupy the same area: tiles fill the same diamond, mountains rise from the
  same base, a city covers the same ground. Use the original's alpha as the
  reference footprint (upscaled ×2) and stay close to it.
* **Transparency is real alpha** (the game reads PNG alpha directly). Shadows
  are black with partial alpha.
* **Base terrain tiles have hard edges.** A base tile's alpha must be exactly
  the original's diamond mask upscaled ×2 (nearest neighbor), so tiles still
  join without seams. Everything drawn on top of tiles (hills, forests, rivers,
  roads, cities, units) gets smooth anti-aliased edges instead.
* **Unit animations** (`.flc`) are remade as two sheets per flic:
  `<flic name>.png` and `<flic name>_tint.png`, in the flic's folder. A row per
  direction, in the flic's order (SW, S, SE, E, NE, N, NW, W), and a column per
  frame, with exactly the flic's number of directions and frames. Each cell is
  2× the flic's frame size, and the unit stands at the same spot in the frame
  as in the original. The tint sheet holds the civ-colored parts (clothing,
  flags, banners), shaded in grayscale/white, which the game multiplies by the
  civ color; those pixels are transparent in the base sheet.
* **Animations are optional.** The game is played with animations off, so the
  committed art is what shows then: units standing or fortified (stored as a
  one-column "still" of the flic's last frame) and workers at work (whole
  loops). Each unit's RUN flic is also committed as a "move cycle" of every
  other frame (a 256-color sheet with half the columns; the game shows each
  column for the frames it stands for), and so is what combat plays, as
  256-color sheets: the ATTACK1 (the only attack the game plays) and DEATH
  flics with every frame, and VICTORY with every other frame.
  `python ArtGen/units/build.py` renders just those; `--animations` also
  renders the other attacks, fidgets, whole runs and the like, and every
  animation whole in full color, into `C7/ModernArtAnimations/`, which isn't
  committed and is used by the game when present.
* **Standalone mode draws units only with this art.** Without Civ3's files,
  the game reads each unit folder's `<unit>.json` (written by
  `ArtGen/units/metadata.py`: the flic of each action and the header values
  of each flic) in place of its `.INI` and `.flc` files, and shows
  `<unit>_32.png` (64x64, `ArtGen/units/thumbnails.py`) in place of its spot
  in `units_32.pcx`. In standalone mode everything under `Art/Units` comes
  from here whatever the Graphics Overhaul setting.
* **Don't remake** `Territory.pcx` (borders are recolored by exact color),
  `FogOfWar.pcx`, or interface art other than the screens' above.
* **Deterministic.** Fixed random seeds, so a rebuild gives the same art.
* Only numpy and Pillow are guaranteed (`pip install scipy` is fine if needed).

## The look

**KISS: keep it simple.** Modern, but still unmistakably Civ3 (2005): a calm,
clean, readable isometric map that's easy to look at for hours. A careful
repaint of the originals at a higher resolution, not a different game. It must
always be obvious what's going on: what each tile is, what each unit is.

* **Respect the originals.** Each remake keeps what its original shows and
  where: the same subject, silhouette, composition, and color identity.
  Players should recognize every tile and unit at a glance.
* **Muted, natural color. Never more saturated than the original.** Sample
  each original's colors (median per region) and use them as the ceiling for
  saturation; when in doubt, go a little greyer, not brighter. No neon
  turquoise water, no bright lime grass. Gentle contrast. The map should feel
  restful.
* **Simple and legible.** Terrain types must stay instantly distinguishable at
  1x zoom. No busy micro-detail, no strong repeating patterns or waves, no
  photorealism. Large areas should be calm, with soft, low-contrast variation.
* **Clean surfaces.** No palette dithering or pixel speckle. Smooth gradients,
  soft low-contrast procedural texture.
* **Light from the upper left (northwest), high in the sky.** Lit faces face
  up-left; shadows fall down-right, soft, about 35-45% black. Every set uses
  the same light.
* **Soft ambient occlusion** where things meet the ground, so sprites sit on
  the map instead of floating.
* **Sprites (units, cities, resources, improvements):** soft shading, a thin
  darker contour so they read against any terrain.
* **Units must be unmistakable.** Each unit's defining features (weapon,
  headgear, shield, armor, mount, hull) are clear at 1x, and units look
  different from each other. The civ color is an accent, used only on the
  parts the original colors (about as much area as the original's tinted
  pixels, never more): most of a unit is its own natural colors.

## Checking your work

* `civ3art.save_preview(name, original_2x, remake)` writes side-by-side images
  to `ArtGen/_previews/` (not committed).
* In the game: from `C7/`, run
  `C:/Dev/Godot_v4.4-stable_mono_win64/Godot_v4.4-stable_mono_win64_console.exe --path . res://_devshot/devshot.tscn -- "--save=<SAV>" --out=<dir> --name=<prefix> --zoom=1,2 [--tile=x,y] [--modern=both|0|1]`.
  It loads a save, takes screenshots of the map with the original and the
  modern art at each zoom, and quits (it needs about 20–40 seconds). A good save:
  `D:/Steam/steamapps/common/Sid Meier's Civilization III Complete/Conquests/Saves/Auto/Conquests Autosave 280 AD.SAV`.
  The C# is already built; don't edit or rebuild the C# project.

## The Civilopedia's art

`python ArtGen/civilopedia/build.py` draws the art of OpenCiv3's own
Civilopedia (page backgrounds, covers, name bars, buttons, arrows and section
icons) into `C7/Civilopedia/Art/Civilopedia/`, which is used in standalone
mode and wherever Civ3's own Civilopedia art is missing (see
`C7Engine/C7GameData/CivilopediaFiles.cs`). Unlike the map art, these are our
own designs rather than remakes, at the originals' size (1x, not 2x): each
file is the PNG of the same name as a Civ3 file, with the same size and the
same sprite rectangles, and every panel, tab and well where the Civilopedia's
layout code expects it. The layout is measured from Civ3's art and written down
at the top of the script.

## The leader heads

The leader heads are lifelike portraits: a realistic photograph of each
leader, generated with FLUX.2 [klein] 4B (Apache-2.0) from a historical
picture of them and a description of how they looked, by
`ArtGen/leaderheads/lifelike.py` (see its docstring; it needs `mflux`, so an
Apple Silicon Mac, or the same model in diffusers elsewhere). The pictures
picked for the game are committed in `ArtGen/leaderheads/lifelike/`, and
`python ArtGen/leaderheads/lifelike.py --install` turns them into
`C7/ModernArt/Art/Leaderheads/<civ name>.png` (230x230) and writes their
credits to `CREDITS.txt` there.

Before them they were the historical pictures themselves, with a shared
painted finish: `python ArtGen/leaderheads/build.py --painted-set` still makes
that set, over the lifelike one. It made a leader head for every
civilization of the bundled rules, for standalone mode, into
`C7/ModernArt/Art/Leaderheads/<civ name>.png` (230x230: twice Civ3's 115x115).
Unlike the rest of ArtGen these aren't drawn: each is a real historical
likeness of the leader (a painting, photograph, bust, relief or engraving)
from Wikimedia Commons, public domain or CC0 only. The script's `SOURCES`
table names each Commons file and the square to crop; it downloads the files
into `ArtGen/leaderheads/_cache/` (not committed), checks their licenses,
crops them to the head and shoulders and gives them all the same grade (a
common brightness, black and white toned warm, a little warmth),
vignette and edge. It writes each picture's source, artist and license to
`C7/ModernArt/Art/Leaderheads/CREDITS.txt`. `--sources` also writes each
whole source with a grid and the crop to `ArtGen/_previews/leaderheads_sources/`,
for choosing crops. `C7/Lua/standalone/textures.lua` lists the civilizations
that have one; there's one per leader, not one per era and mood. A contact
sheet goes to `ArtGen/_previews/leaderheads.png`.

MDJ's clans (`C7/Lua/mdj`) have banners instead, as MDJ's own leader heads
are: each clan's mon on dark cloth. `python ArtGen/leaderheads/mdj_mon.py`
remakes them from the originals (the Sengoku conquest's
`Art/Advisors/<clan>_all.pcx`) like the rest of ArtGen, into
`C7/ModernArt/Art/Leaderheads/<clan>.png`, named by Civilopedia entry
(`RACE_ODA`: `Oda.png`); see its docstring.
