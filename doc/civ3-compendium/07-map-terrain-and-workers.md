# 7. Map, terrain, resources, workers, barbarians and huts

Scope: Civ3 1.29f, PTW 1.27f, Conquests (C3C) 1.22, standard rules (`conquests.biq`).
Scenarios, mods (C3X etc.) and the special C3C game modes are excluded.

## Sources and how they are cited

Primary sources were read straight from the Steam "Civilization III Complete" install
(`D:\Steam\steamapps\common\Sid Meier's Civilization III Complete\`, called `<civ3>` below).
They are local files, not URLs, so each citation gives the file and the entry key.

| Tag | Source |
|---|---|
| **BIQ** | `<civ3>/Conquests/conquests.biq`, the C3C 1.22 default rules. Read with this repo's `QueryCiv3` (sections TERR, TFRM, GOOD, RULE, GOVT, DIFF, WSIZ). The quote is the field name and value. |
| **PEDIA** | `<civ3>/Conquests/Text/Civilopedia.txt` (C3C). The quote follows the `#KEY` of the entry. |
| **EDHELP** | The C3C editor help `<civ3>/Conquests/Civ3ConquestsEdit.chm`, decompiled. Cited by page name. |
| **README-129** | `<civ3>/README.txt`, the vanilla patch notes up to 1.29f. |
| **README-PTW** | `<civ3>/civ3PTW/readme.txt` (PTW 1.27f). |
| **README-C3C** | `<civ3>/Conquests/readme.txt` (C3C 1.20/1.22). |
| **README-CMPL** | `<civ3>/Civ Complete Readme.txt`. |
| **SCRIPT** | `<civ3>/Conquests/Text/script.txt`, the in-game messages. |
| **MANUAL** | The original Civilization III manual (2001, vanilla). It came from a PDF text extract with no URL available, so page numbers are approximate. |
| **INFOC** | CivFanatics Civ3 Info Center (Firaxis pre-release info): https://civfanatics.com/civ3/infocenter/ |
| **BIQ-V / BIQ-PTW** | `<civ3>/civ3mod.bic` (vanilla 1.29f rules) and `<civ3>/civ3PTW/civ3X.bix` (PTW 1.27f rules), decompressed and read raw (sections TFRM, TERR, GOOD, TECH) with a throwaway scratch script. Used for version differences. |
| **MANUAL-CMPL** | The *Civilization III Complete* manual (scratch PDF text extract), "Terrain Charts", printed p.212. It covers Marsh and Volcano, so it is the C3C-era edition. |
| **C3X-INI** | The base-game bug and behaviour notes in the C3X mod's `default.c3x_config.ini` (Flintlock / maxpetul), https://github.com/maxpetul/C3X/blob/master/default.c3x_config.ini (read 2026-10-20). The mod is out of scope. Only its descriptions of what the unmodded C3C 1.22 executable does are used; they come from reading the executable's code. |
| **C3X-THREAD** | https://forums.civfanatics.com/threads/c3x-exe-mod-including-bug-fixes-stack-bombard-and-much-more.666881/ (the C3X mod's forum thread, 2021-2026). Posts are cited as "post-NNNNNNNN" (thread URL plus `post-NNNNNNNN`) or "page-N #post-number". Flintlock, the C3X author, reads the 1.22 executable; his statements about the base game are code readings. Other posters report play, sometimes in modded games. Only statements about the unmodded game are used. Quotes were re-checked against the posts; the few that could not be are marked "(paraphrase from a summarised fetch)". |
| **CIVWIKI** | The German CivWiki (civforum.de community), https://www.civ-wiki.de/ . Community-tested rules, cited by page and revision. Quotes are German with an English gloss. |

Web sources are cited by URL. Forum findings are community testing, not official
documentation, so they are marked as such.

---

## 1. Terrain types

### 1.1 Base table (C3C BIQ TERR)

| Terrain | Food | Shield | Commerce | Irrigation + | Mine + | Road + | Defence % | Move cost | City allowed | Colony | Wheeled impassable | Fort / Airfield / Radar / Outpost | Pollution / global-warming effect (TERR "PollutionEffect") |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Desert | 0 | 1 | 0 | 1 | 1 | 1 | 10 | 1 | yes | yes | no | all yes | none |
| Plains | 1 | 1 | 0 | 1 | 1 | 1 | 10 | 1 | yes | yes | no | all yes | becomes Desert |
| Grassland | 2 | 0 | 0 | 1 | 1 | 1 | 10 | 1 | yes | yes | no | all yes | becomes Plains |
| Tundra | 1 | 0 | 0 | 0 | 1 | 1 | 10 | 1 | yes | yes | no | all yes | none |
| Flood Plain | 3 | 0 | 0 | 1 | 0 | 1 | 10 | 1 | yes | yes | no | all yes | none |
| Hills | 1 | 1 | 0 | 0 | 2 | 1 | 50 | 2 | yes | yes | no | all yes | none |
| Mountains | 0 | 1 | 0 | 0 | 2 | 1 | 100 | 3 | **no** | yes | **yes** | all yes | none |
| Forest | 1 | 2 | 0 | 0 | 0 | 1 | 25 | 2 | yes | yes | no | all yes | base terrain (14) |
| Jungle | 1 | 0 | 0 | 0 | 0 | 1 | 25 | 3 | yes | yes | **yes** | all yes | base terrain (14) |
| Marsh | 1 | 0 | 0 | 0 | 0 | 1 | 20 | 2 | **no** | **no** | **yes** | outpost only | becomes Coast (11) |
| Volcano | 0 | 3 | 0 | 0 | 0 | 0 | 80 | 3 | no | no | yes | none | none |
| Coast | 1 | 0 | 2 | 0 | 0 | 0 | 10 | 1 | no | no | — | none | none |
| Sea | 1 | 0 | 1 | 0 | 0 | 0 | 10 | 1 | no | no | — | none | none |
| Ocean | 0 | 0 | 0 | 0 | 0 | 0 | 10 | 1 | no | no | — | none | none |

- Source for every cell: BIQ TERR fields `Food`, `Shields`, `Commerce`, `IrrigationBonus`,
  `MiningBonus`, `RoadBonus`, `DefenseBonus`, `MovementCost`, `AllowCities`, `AllowColonies`,
  `ImpassableByWheeled`, `AllowForts`/`AllowAirfields`/`AllowRadarTowers`/`AllowOutposts`, `PollutionEffect`.
  For example, Hills reads `IrrigationBonus=0 MiningBonus=2 RoadBonus=1 DefenseBonus=50 MovementCost=2 Food=1 Shields=1 Commerce=0`.
- Meaning of the bonus fields. EDHELP "Irrigation (Terrain Terraform Bonuses)": "Determines the bonus to food production if a tile of the selected terrain type is irrigated."
  EDHELP "Road (Terrain Terraform Bonuses)": "Determines the bonus to commerce if a road is built on a tile of the selected terrain type."
- A zero RoadBonus means no road can be built. README-129: "Restricted roads/railroads from being built on terrain that has no road bonus." So Volcano and water tiles cannot have roads.
- Meaning of `PollutionEffect`. EDHELP "Pollution Effect": "Selects the terrain type (if any) that a tile of the selected terrain type will become if pollution stays on that tile for an extended length of time. For example, grassland would become plains, and plains would become desert." Value 14 is "Base Terrain Type".
- **Bonus grassland ("Grassland with shield").** PEDIA `#TERR_Grassland_with_Shield`: "Some grasslands contain mineral deposits ... These have a rock outcropping to symbolize this and produce a shield if worked in a city radius."
  README-PTW: "Updated placement of forests and jungles on bonus grassland to leave the bonus shield under the forest/jungle."
- **Fresh-water lakes are not a terrain type.** README-PTW: "NOTE: Fresh water lakes are not a terrain type. They are composed of coastal tiles surrounded by land."
  PEDIA `#GCON_Food` nevertheless lists "Fresh Water Lake 2 cannot be irrigated", against 1 food for Coastal. See Disputed.
- **Mountains cannot hold cities.** MANUAL p.~131 (Build/Join City): "You also cannot build on Mountains." This matches BIQ `AllowCities=0`. BIQ also has `AllowCities=0` for Marsh and Volcano.
- **Wheeled units need a road.** MANUAL ch.7: "wheeled units require a road to travel through Jungles and Mountains." BIQ also sets `ImpassableByWheeled=1` for Marsh and Volcano.
- **Movement.** EDHELP "Movement Cost (Terrain Properties)": "Determines the base movement cost for a tile of the selected terrain type."
- **Version differences (BIQ-V, BIQ-PTW).** The vanilla 1.29f and PTW 1.27f rule files have 12 terrains: no Marsh and no Volcano. All the shared terrains have the same yields, bonuses, defence and move costs as C3C. Forest and Jungle `PollutionEffect` is 12 there (the "base terrain" code before C3C added two terrains; 14 in C3C). In PTW, as in C3C, `ImpassableByWheeled` is set on Mountains and Jungle.
- **Terrain adjacency limits in the map generator (code reading).** Flintlock (C3X-THREAD post-16696797, page-119 #2363): "already in the base game desert can't border tundra." (post-16826088, page-137 #2730): "I know tundra and desert, for example, are not." [allowed next to each other]. And (post-16892444, page-160 #3198) there are no impassable terrains "under the standard rules."
- **Volcano yield confirmed.** MANUAL-CMPL's terrain chart gives the Volcano row as 0 food, 3 shields, 0 commerce, movement 3, defence 80. CIVWIKI `Terrain (Civ3)` (rev. 37385, 2012) gives Vulkan 0/3/0 and says "Kann von Radeinheiten nicht passiert werden" ("cannot be crossed by wheeled units") and "keine Stadtgründung möglich" ("no city can be founded"). Both agree with BIQ `Shields=3`. The PEDIA `#GCON_Shields` line "Volcano 0 cannot be mined" is the odd one out (former Disputed 9).

### 1.2 Defence bonuses (cross-check)

PEDIA `#GCON_Terrain_Combat` lists Grasslands, Plains, Desert, Floodplains, Tundra, Ocean, Sea, Coast and
Fresh Water Lake at 10%. It gives "Marsh 20%", "River 25%*", "Forest 25%", "Jungles 25%", "Hills 50%",
"Volcano 80%" and "Mountains 100%", with the footnote "* This bonus applies to the defender if attacked from the
other side of a river." These figures match BIQ exactly. The structural bonuses listed there are Fortified +25%,
Fortress +50%, Barricade +100% and City +50%. The combat section owns those.

### 1.3 Despotism tile penalty (affects every tile yield)

- PEDIA `#GOVT_Despotism`: "any city production square which produces more than two food, shields, or commerce in a despotic government instead produces one less."
- The same penalty applies under Anarchy per the government section. That is not re-verified here.

---

## 2. Rivers

| Effect | Rule | Source |
|---|---|---|
| Placement | Rivers run along tile edges. | PEDIA `#TERR_River`: "[Rivers] do not run [in] squares: instead they run along the edges of squares." |
| Commerce | +1 commerce to any tile with a river on one of its edges. | PEDIA `#TERR_River`: "Any square with a river along its edge produces one extra commerce if worked by citizen laborers within a city radius." |
| Fresh water | Lets adjacent tiles be irrigated before Electricity. | PEDIA `#TERR_River`: "rivers are sources of fresh water, allowing irrigation of adjacent squares without electricity." |
| Defence | +25% (`RULE.RiverDefensiveBonus=25`), only when attacked across the river. It stacks with other bonuses. | EDHELP "River (Defensive Bonuses)": "Default is 25. Note that this bonus is cumulative with other terrain and fortification-related defensive bonuses." The cross-river condition is from PEDIA §1.2. |
| Movement | No movement bonus along rivers. Crossing a river cancels the road bonus until Engineering. | MANUAL p.~59: "This game offers no movement bonus for river travel." and "Until you discover Engineering, your units do not enjoy the road bonus to movement when they cross a river." PEDIA `#TECH_Engineering`: "Roads now bridge rivers." |
| Flood plains | Flood plains only appear along rivers, so they always carry the river +1 commerce. | PEDIA `#TERR_Flood_Plain`: "[floodplains] appear along river banks". Sullla, https://sullla.com/Civ3/strat7.html: "The base numbers for floodplains tiles are 3 food, 0 shields, and 1 gold". |
| Ships | Ships cannot enter rivers. | MANUAL ch.7: "Ships cannot navigate any ground terrain in the game, including rivers, deltas, and flood plains." |

---

## 3. Water tiles, harbour, offshore platform

- The yields are in §1.1: Coast 1/0/2, Sea 1/0/1, Ocean 0/0/0 per BIQ. The vanilla and PTW rule files give the same three values (BIQ-V, BIQ-PTW). Other sources give Ocean 1 food (see Disputed 12).
- **Lakes (community).** CIVWIKI `Terrain (Civ3)` (rev. 37385) lists the lake ("Binnensee") at 2 food, 0 shields, 2 commerce, 10% defence, Fish possible, and "Zählt als Frischwasserquelle (wie Flüsse)" ("counts as a fresh-water source, like rivers"). This matches PEDIA "Fresh Water Lake 2" food. Since README-PTW says lakes are Coast tiles (Coast is 1/0/2 in BIQ), the extra food is presumably a hard-coded bonus for Coast tiles enclosed by land. That inference is not sourced.
- **Harbour.** PEDIA `#BLDG_Harbor`: "Coastal, sea, and ocean squares produce one extra food."
- **Offshore Platform.** PEDIA `#BLDG_Offshore_Platform`: "causes all worked coastal, sea, and ocean squares to produce one shield."
- **Movement safety.** PEDIA `#TERR_Coast`: naval units risk loss outside coast "But after the discovery of astronomy, this danger vanishes."
  PEDIA `#TERR_Sea`: "[Sea] squares are significant barriers to trade and travel until the discovery of astronomy."
  PEDIA `#TERR_Ocean`: "... until the discovery of Navigation or Magnetism."
- **Resources on water.** BIQ TERR/GOOD: Coast takes Fish, Sea takes Whales and Fish, Ocean takes none.

---

## 4. Tile improvements (worker jobs)

### 4.1 Allowed terrain and effects

| Job | Allowed on | Effect | Tech / resource prerequisite (BIQ TFRM `Required`, `RequiredResource1/2`) |
|---|---|---|---|
| Irrigate | Desert, Plains, Grassland, Flood Plain. These are the terrains with IrrigationBonus > 0. | + the terrain's IrrigationBonus in food (+1 on each). | none. Irrigating without fresh water needs Electricity. |
| Mine | Desert, Plains, Grassland, Tundra (+1); Hills, Mountains (+2). | + MiningBonus in shields. | none |
| Road | All land except Volcano. | + RoadBonus commerce (+1); movement cost 1/3 (`RULE.MovementAlongRoads=3`). | none |
| Railroad | A tile that already has a road. | Movement free; raises irrigation and mine output (see §4.3). | Steam Power + Iron + Coal |
| Plant Forest | Plains, Grassland, Tundra (BIQ `WorkerJobAllowed`=Plant Forest). | Tile becomes Forest. | Engineering |
| Clear Forest | Forest | Becomes its underlying base terrain; 10 shields go to the nearest city. | none |
| Clear Wetlands | Jungle, Marsh | Becomes Grassland or Flood Plain. | none |
| Clear Damage | Polluted, fallout or cratered tiles | Removes the damage. | none |
| Fortress | Land with `AllowForts=1`. | +50% defence, ZOC. | Construction |
| Barricade (C3C) | A fortress tile. | +100% defence; stops enemy ground movement. | Construction |
| Airfield (PTW+) | `AllowAirfields=1`, neutral or own land. | Air base. The worker is consumed. | Flight |
| Radar Tower (PTW+) | Own territory only. | +25% attack and defence within 2 tiles. The worker is consumed. | Advanced Flight (C3C 1.20+) |
| Outpost (PTW+) | Neutral or own land. | Vision 2/3/4 on flat/hills/mountains. The worker is consumed. | Masonry |
| Colony | A tile with a tradable resource outside your borders. | Gives access to the resource. The worker is consumed. | none |

Sources for §4.1:

- BIQ TFRM: `Fortress Required=20` (Construction), `Railroad Required=44 RequiredResource1=1 RequiredResource2=3` (Steam Power, Iron, Coal), `Plant Forest Required=23` (Engineering), `Airfield Required=58` (Flight), `Radar Tower Required=63` (Advanced Flight), `Outpost Required=1` (Masonry), `Barricade Required=20`. Mine, Irrigation, Road, Clear Forest, Clear Wetlands and Clear Damage all have `Required=-1`.
- PEDIA `#TFRM_Irrigation`: "Only squares adjacent to fresh water (river or lake) or to another irrigated square may be irrigated. After the discovery of Electricity, you can irrigate a tile without proximity to fresh water."
- MANUAL p.114 (Irrigate): "A suitable square can always be irrigated if it shares a side or a diagonal with a source of fresh water (terrain with a river running through it, a freshwater lake, or another irrigated square)." So the adjacency includes diagonals.
- MANUAL p.113 (orders table): the Irrigate row lists "Desert, Grassland, Plains, Flood Plains". The Mine row lists "Desert, Hills, Mountains, Plains, Grassland". The Reforest row lists "Engineering Grassland, Plains, Tundra".
- PEDIA `#GCON_Food`: "Hills 1 cannot be irrigated", "Tundra 1 cannot be irrigated", "Desert 0 1 if irrigated".
- PEDIA `#TFRM_Mine`: "Grasslands 0 shields, 1 if mined. w/ outcropping 1 shields, 2 if mined. Plains 1 shields, 2 if mined. Hills 1 shields, 3 if mined. Mountains 1 shields, 3 if mined."
- PEDIA `#TFRM_Road`: "units moving along roads expend one third of a movement point per square, regardless of terrain type." The table there gives +1 commerce with a road on Floodplains, Grasslands, Plains, Hills, Mountains, Forests and Jungles. PEDIA `#GCON_Commerce` adds "Marsh 0 1 with road."
- MANUAL p.~86: "All terrain types produce commerce once penetrated by roads."
- PEDIA `#TFRM_Railroad`: "Workers can build railroads after the discovery of Steam Power, but only if your civilization has access to the Strategic Resources Iron and Coal." MANUAL p.116: "You can only build them where you have already built roads."
- PEDIA `#TFRM_Clear_Forest`: "contribute 10 shields to the nearest controlled city. The resulting terrain will be either grasslands, plains, or tundra, depending upon the base terrain beneath the forest." BIQ `RULE.ForestValueInShields=10`.
- PEDIA `#TFRM_Clear_Wetlands`: "A worker can clear the jungle or marsh from a square, creating either floodplains or grasslands."
- PEDIA `#TFRM_Plant_Forest` says "any grassland or plains square". BIQ and MANUAL also allow Tundra. README-129 (v1.16f): "Forests planted on tundra always display as pine." CIVWIKI `Bautruppaufträge (Civ3)` (rev. 54705, 2024) also gives "Wald aufforsten: 18 (nur Tundra, Ebene, Weideland)" ("plant forest: 18, only tundra, plains, grassland"). So Tundra is allowed; the PEDIA line is incomplete (former Disputed 8).
- **Forest shields only once per tile.** README-129 (Changes v1.16f): "Shield bonus from clearing forest can only be received once per game." Community readings agree that the limit is per tile: justanick, https://forums.civfanatics.com/threads/not-getting-shields-when-cutting-forests.654695/ (C3C, Feb 2020): "You can get the 10 shields only once per tile." and "If the tile has been deforested before, then no shields are credited." CIVWIKI `Wald roden (Civ3)` (rev. 35891, 2011): the bonus comes when the order is carried out "erstmals auf einem Feld" ("for the first time on a tile").
- **Which city gets the chop (community).** CIVWIKI `Wald roden (Civ3)`: the game searches the tiles around the forest in a fixed clockwise order, and "Die Stadt, die auf dem Feld mit der kleinsten Nummer steht, erhält den Waldertrag." ("The city on the lowest-numbered tile gets the forest yield"). "Der Wald muß dazu nicht im Kulturgebiet der Stadt liegen." ("The forest does not have to be inside the city's borders"). "Städte außerhalb dieses Bereichs erhalten keine Gutschrift." ("Cities outside this area get nothing"). Cities building a wonder, small wonder, Palace, spaceship part or Wealth are skipped, so the shields go to the next city in order. TimBentley (https://forums.civfanatics.com/threads/barbarian-information.267101/, 2008) says the same search order is used for "the governor's order in looking for the next tile to work and forest chops going to cities". tjs282 (thread 654695): "They will not be applied to Great Wonders, Small Wonders(?), or Wealth".
- README-129 (Fixed v1.21f): "Fixed forest planting on enemy territory exploit." The exploit is not described.
- Forest shields cannot be used for wonders. Apolyton (piratebrun, quoted in https://apolyton.net/forum/miscellaneous/archives/civ3-play-the-world/85881-questions-about-terrain-and-the-explaination-the-prima-s-strategy-guide-gives): forests "CANNOT be harvested to speed up PALACES, SMALL WONDERS or WONDERS." This is community testing; the cities/production section should confirm it.
- **Fortress.** PEDIA `#GCON_Fortresses`: "They get a 50% defensive bonus if attacked." and "They have a zone of control and fire at passing enemy units without receiving return fire." BIQ `RULE.FortressDefensiveBonus=50`.
- **Barricade.** PEDIA `#GCON_Barricades`: "A Barricades can only be built on a square already containing a Fortress. Even if unoccupied, barricades automatically stops movement of a ground unit entering its square" ... "They get a 100% defensive bonus if attacked" ... "Barricades become a fortress if their square is pillaged."
- **Airfield.** PEDIA `#GCON_Airfields`: "Construction of the Airfield consumes the worker." and "If it falls into the territory of another civilization, however, the airfield becomes solely useable by the player whose territory it lies in (unless they have yet to research Flight, in which case the airfield is destroyed)." The GCON text says "Airfields can only be built in tiles that have no other improvements, an airfield, a colony, a road or a railroad." PEDIA `#TFRM_Airfield` says "Airfields can only be built in tiles that are empty or have a road and/or railroad."
- **Outpost.** PEDIA `#GCON_Outposts`: "The range of vision for the Outpost is 2 tiles on flat terrain, 3 on hills and 4 on mountains." ... "If an Outpost falls within the territory of any other civilization, it will be destroyed." ... "Outposts can only be built in a tile that is empty, has a mine, irrigation, a road and/or a railroad. You cannot build an Outpost on a tile with an Airfield or a Colony."
- **Radar Tower prerequisite by version.** BIQ-PTW TFRM `Radar Tower Required=64`, which is Radio in the PTW tech list. C3C BIQ `Required=63` is Advanced Flight (C3C removed Radio). So the PEDIA/MANUAL "Radio" text is right for PTW and outdated for C3C (former Disputed 15).
- **Radar Tower.** PEDIA `#GCON_Radar_Towers`: "The range of the effected area is 2 tiles. Friendly units within the Radar Towers range get a +25% attack and defense bonus. This bonus can only be applied once, even if multiple towers overlap." ... "Should a Radar Tower fall into the territory of another civilization, it will be destroyed." README-C3C v1.20: "Radar Towers require Advanced Flight" and "Radio Tech removed".
- **Colony.** PEDIA `#TFRM_Build_Colony`: "A colony is necessary only if the resource is not inside your borders. Note that when a city's borders expand to include a colony, the colony disappears because it is no longer needed. The worker is consumed by building a colony." PEDIA `#GCON_Colony`: "you need a road connecting the colony to one of your cities to get the benefits of the resource." SCRIPT `#COLONY_BARBS`: "Our $RESOURCE0 colony was overrun by a tribe of $BARBARIAN1s." So barbarians destroy colonies.
- **Outposts see no farther over water** (community). Alekseyev_ (C3X-THREAD post-16335948, page-50 #999): outposts "don't gain any extra LOS when looking at water, unlike normal units."
- **Railroad-to bypasses the resource check (stock bug, code reading).** tjs282 (C3X-THREAD post-16915830, page-169 #3371): (paraphrase from a summarised fetch) "the only absolute logic in the unpatched game for railroading a tile is that it must (first) be roadable". Flintlock (post-16916404, #3374): the game "gives a worker the railroad-to command after checking only that it has the tech & resources to build a road", and "the function that checks whether the needed resources are present always reports that they are" when the job is a road. So the railroad-to button can appear, and the order can lay rail, without Iron and Coal. The intended rule is the BIQ's (Iron and Coal needed, §4.1).
- **Workers can't improve tiles they can't enter.** Erlend Philip Wood (C3X-THREAD post-16697864, page-120 #2382): "Workers can't develop tiles that they cannot enter." Flintlock (post-16635376, page-108 #2147): (paraphrase from a summarised fetch) "the worker jobs are hard-coded into the game logic, except for what you can change through the editor."
- **City tiles and roads.** README-129: "Changed city code so that a road/railroad is not placed under a city if a road/railroad is not allowed on the terrain type the city is built upon." README-C3C v1.20: "Railroads will always appear under captured cities if you have Steam Power."

### 4.2 Craters, pollution damage

- PEDIA `#GCON_Craters`: "When a bombardment attack which eliminates the last unit or terrain improvement in a square, a crater may be formed. Like pollution, the presence of craters reduces a square's production, commerce, and food output by one."
- MANUAL p.89 ("Pollution's Effects"): "It reduces the production of food, raw materials, and commerce income to zero. Once the terrain is detoxified, production returns to pre-pollution levels." See Disputed.
- Long-term pollution transforms the terrain (`PollutionEffect`, §1.1). PEDIA `#GCON_Pollution` (Global Warming): "It can cause forests and jungles to disappear, and grasslands to become plains, and plains to become desert. World pollution is the sum of all city pollution, nuclear explosions, and meltdowns." SCRIPT `#GLOBALWARMING_RISING_COASTLINES`: "Coastlines are rising!" This matches Marsh→Coast in BIQ.
- **Sun icon.** README-129 (Changes v1.16f): "The Sun Icon on the Info Box represents the amount of global warming affecting the game. If the sun does not display, there is insufficient global warming to affect the terrain. Once the sun displays, the brighter it is, the greater the impact of global warming."
- **What drives global warming (community, vanilla).** WarpStorm, https://codehappy.net/apolyton/threads/60991-1.htm (Sep 2002): "It is caused by having pollution icons in your cities and by using nukes." He reports a custom-scenario test with a heavily polluted map and "No global warming", and second-hand that "Firaxis has said that polution on the ground does not affect global warming" (no source given). Zachriel (page 2, https://codehappy.net/apolyton/threads/60991-2.htm, Oct 2002) abandoned his metropolises and removed his polluting buildings and still got warming: "So yes, Global Warming is global." This fits PEDIA's "World pollution is the sum of all city pollution, nuclear explosions, and meltdowns." The thresholds and odds remain unknown.
- **Uncleaned pollution degrades the tile itself.** WarpStorm (same thread, "I tested this too"): "the actual polluted tile can turn worse even if the global warming isn't bad if it's not cleaned up soon enough." This matches EDHELP "Pollution Effect" ("if pollution stays on that tile for an extended length of time").
- **Citizens are taken off polluted tiles (code-level).** C3X-INI (`do_not_unassign_workers_from_polluted_tiles`): the option "keeps citizens working the tiles they've been assigned to when pollution spawns there instead of converting them to specialists". So the base game turns a citizen working a newly polluted tile into a specialist.
- MANUAL p.88: "Pollution appears within the City Radius of any city that is excessively productive (produces lots of shields) or has a very large population. ... The number of symbols corresponds to the probability each turn of a square within the city radius becoming polluted." The pollution formula belongs to the cities section.

### 4.3 Railroad yield bonus

- PEDIA `#GCON_Worker_Jobs`: "Increases irrigation or mine output, and provides unlimited movement." MANUAL p.116: "Railroads also increase the yields of both irrigation and mines."
- Community tests, Apolyton (David Weldon), https://apolyton.net/forum/miscellaneous/archives/civ3-strategy-archive/42626-distilled-civ3-tips-and-notes?p=953406:
  "RR through something that has been irrigated = +1 food. RR through something that has been mined = +1 shield."
  "RR through forest does nothing. RR through unimproved squares? Nothing."
  "on a coal mine with RR I get 5, not 6".
- Community tests, CFC (Yeti), https://forums.civfanatics.com/threads/does-railroad-provide-extra-shield-on-mined-grassland-plain.74236/: "If there's a mine, RR adds 1 shield." and "If there's irrigation, RR adds 1 food." Also "Tundra + Mine + Railroad = 1 food, 2 shields, 1 gold".
- Sullla, https://sullla.com/Civ3/strat7.html: forests "cannot be irrigated or mined, and they therefore get no benefit from rails." Deserts "with railroads can actually manage 2 food".
- **Working rule:** +1 food on an irrigated tile and +1 shield on a mined tile. No bonus on an unimproved or forest tile. A competing reading exists (Disputed).

### 4.4 Worker automation commands

- **The commands and their keys.** LABELS `#UNIT_ACTIONS`: "Road to (Ctrl-R)", "Rail to (Ctrl-Shift-R)", "Automate:^Build Trade (Ctrl-N)", "Automate:^Irrigate to Nearest City (Ctrl-I)", "Automate:^Clear Forest (Shift-F)", "Automate:^Clear Wetlands (Shift-W)", "Automate:^Clear Damage (Shift-D)", "Automate:^This City Only (Shift-I)", "Automate:^No (A)ltering (Shift-A)", "Automate:^No Altering/This City Only (Ctrl-Shift-I)". Each has a cell in the unit buttons (NormButtons.PCX, in the same order). LABELS also has the preference "Show Advanced Unit Action Buttons"; that it is what shows these buttons is an engine reading (UNVERIFIED).
- **What they do.** PEDIA `#GCON_Worker_Jobs`: Build Trade Network "giving it the task of linking all cities to your capital city"; Irrigate to Nearest City "the task of irrigating the nearest city"; Clear Forest "clearing all nearby forests"; Clear Wetlands "all nearby jungle and/or marsh"; Clear Damage "all nearby pollution, including craters and fallout from a volcanic eruption"; This City Only "to improve the nearest city". `#GCON_Worker_Jobs_Advanced`: No Altering "automated tasks that do not change existing tile improvements"; Road to / Rail to "Builds a road [railroad] to a particular destination." The hotkey list (`#GCON_Hotkeys_Units`) gives other names: "Build Road/Rail Net Ctrl-N", "Irrigate to city Ctrl-I", "Improve Nearest City Shift-I", "Improve Nearest City/Without Altering Ctrl-Shift-I", "Automate Worker (virgin terrain) Shift-A".
- **With nothing left, the worker stops.** tjs282 (C3X-THREAD p156 #3108): with no pollution left, "any Workers on mop-up duty just go to sleep".
- **Engine reading (unsettled points, see open questions):** "nearby" is taken as within our borders; "nearest city" is chosen when the order is given; Build Trade lays roads only, from each city cut off from the capital toward it; Irrigate to city irrigates each tile on the way from the worker to the city, and does not replace a mine; No Altering skips any job that replaces an improvement, or plants forest over irrigation or a mine.

---

## 5. Worker job times

### 5.1 Base turns (TFRM "Turns to Complete")

| Job | C3C 1.22 (BIQ) | PTW 1.27f (BIQ-PTW) | Vanilla 1.29f (BIQ-V) |
|---|---|---|---|
| Mine | 12 | 12 | 12 |
| Irrigation | 8 | 8 | 8 |
| Fortress | 16 | 16 | 16 |
| Road | 6 | 6 | 6 |
| Railroad | 12 | 12 | 12 |
| Plant Forest | 18 | 18 | 18 |
| Clear Forest | **4** | **10** | **10** |
| Clear Wetlands / Clear Jungle | 16 | 16 | 16 |
| Clear Damage / Pollution | 24 | 24 | 24 |
| Airfield, Radar Tower, Outpost | 1 | 1 | — (PTW+) |
| Barricade | 16 | — (C3C only) | — |

- C3C column: BIQ TFRM `TurnsToComplete`, for example `Name=Clear Forest TurnsToComplete=4`.
- PTW and vanilla columns: read from the rule files themselves (BIQ-PTW `Clear Forest turns=10`, BIQ-V `Clear Forest turns=10`; the vanilla and PTW jobs are named "Clear Jungle" and "Clear Pollution"). They match Shiplord's PTW table, https://forums.civfanatics.com/threads/hi-how-many-turns-for-each-worker-action.80432 ("Clear forest 10"). tjs282 (https://forums.civfanatics.com/threads/new-city-build-order-worker-mgmt-and-railroads.655880/, 2020): "Forest-chopping takes longer in PTW (10 turns for a single non-IND Worker) than it does in C3C (4 turns)." That fits base × forest move cost 2 ÷ 2. KayEss's article (below) says "Clear forest 4 (Vanilla & PtW are 5)"; the rule files show 10, so his 5 is wrong (former Disputed 5).
- EDHELP "Turns to Complete": "Determines the base rate (how many turns it takes) for workers to complete the selected worker job. This value can be further modified, depending on government type."

### 5.2 The formula

turns = base × terrain movement cost ÷ (government WorkerRate × worker strength/100 × trait × tech multipliers), with several workers pooling their work.

- Apolyton (Nero Would, 19 Dec 2001), https://apolyton.net/forum/miscellaneous/archives/civ3-strategy-archive/50097-worker-improvement-times:
  "To calculate the number of turns to complete a job, multiply the "effort" for the job by the basic Movement Cost";
  "foreign workers only count as half a worker";
  "The Work Rate is 1 for Anarchy, 3 for Democracy and 2 for all other governments.";
  "Also, divide by 2 for Industrious civs, divide by 2 if you have researched Replaceable Parts."
  The same page says the effect of roads and railroads on movement cost is ignored, so the base terrain cost is used.
- EDHELP "Worker Rate (Government Properties)": "The "base" worker rate is 2. Each digit increases the rate by 50%. Thus, 1=50% work rate (bad), 2 = 100% (normal), and 3 = 150% (speedy workers!!)."
- EDHELP "Worker Strength (Unit Statistics)": "If the Worker Strength value of a worker is set at "50" that worker works at "half" (50%) speed, and thus would take twice as long".
- Observed data (DoubleEdged, CFC 80432, 1 Mar 2004, non-industrious, Ancient era) matches base × move cost ÷ 2:
  "Build Mine: (6 grasslands,12 hills,18 mountains)" and "Build Road: (6 in Forest,Hills; 9 in Mountains,Jungle; 3 grasslands)".
- Jack Merchant (same thread): "'Base' turns is not for your regular non-industrious worker, but for slave workers (who work at half speed)."

**Government worker rate (BIQ GOVT `WorkerRate`)**

| Government | Anarchy | Despotism | Monarchy | Communism | Republic | Democracy | Fascism (C3C) | Feudalism (C3C) |
|---|---|---|---|---|---|---|---|---|
| WorkerRate | 1 | 2 | 2 | 2 | 2 | 3 | 4 | 2 |
| PEDIA "Worker efficiency" | 50% | 100% | — | — | — | 150% | 200% | — |

PEDIA `#GOVT_Fascism`: "Worker efficiency 200%". `#GOVT_Democracy`: "Worker efficiency 150%". `#GOVT_Anarchy`: "Worker efficiency 50%".

**Other modifiers**

- **Replaceable Parts.** PEDIA `#TECH_Replaceable_Parts`: "{New Ability} Workers work twice as fast."
- **Captured or enslaved workers.** README-C3C: "Note: Captured workers work at 50% the speed of regular workers." README-CMPL says the same. BIQ `RULE.Slave=1` (Worker). EDHELP "Slave Unit": "settlers represent two population points, so a captured settler would convert into two workers."
- **Industrious.** PEDIA `#GCON_Strengths`: "Workers complete tasks faster". The PEDIA gives no figure. Tested figure: ×1.5 in C3C, ×2 in vanilla and PTW. KayEss (Kirit Saelensminde), War Academy "Worker moves" (2004, tested in C3C), PDF https://civfanatics.com/wp-content/uploads/2024/04/civ3_acad_worker_moves.pdf : "The Industrious trait adds 50% to the amount of work that is done per worker turn. In Civilization III vanilla and Play the World this figure was 100%." Same PDF: "an ancient era worker for a non-industrious tribe will do two BWT per actual game turn (GT). For an industrious trait this changes to three." (Former Disputed 2.)

### 5.2a Worker points per turn, rounding and pooling (community-tested)

- **Per-worker integer output.** justanick, https://forums.civfanatics.com/threads/how-many-workers-do-i-need-to-do-this.452798/ (C3C, Jan 2012): "Every worker produces every turn an integer number." "performance = (government factor) x (industrious factor) x (slave factor) x (spezific efficieny) x (replaceable part factor)", with "government factor is 1 for anarchy, 3 for democrazy, 4 for fascism and 2 for every other goverment", "industrious factor is 1.5 for industrious civs and 1 for every other civ", "slave factor factor is 0.5 for nonnational units and 1 for every national unit", "replaceable part factor is 2 if RP is researched and 1 when not". "The number is always rounded down, but the total minimum of 1 is never violated."
- CIVWIKI `Bautruppaufträge (Civ3)` (rev. 54705, 2024) gives the same formula ("Bautruppleistung = Re × (Ind/100%) × (BT/100%) × (Eff/100%) × Ersatzteile") and the same rounding ("stets abgerundet", "always rounded down", never below 1).
- **Job cost.** A job needs base turns × the tile's base movement cost in worker points; roads and railroads on the tile do not reduce it. KayEss PDF: "the base BWT is multiplied up by the movement penalty on the tile." and "building a road on grassland requires 6 BWT (1 x 6), for hills 12 BWT (2 x 6) and for a mountain 18 BWT (3 x 6)." CIVWIKI `Bautruppaufträge` lists the jobs for move cost 1/2/3 as, for example, "Bergwerk: 12 / 24 / 36" (mine) and "Straße: 6 / 12 / 18" (road). So the mountain multiplier is 3, not 4 (former Disputed 4).
- **Several workers pool their points** into one job "bucket". KayEss PDF: "Ganging together a worker and a slave ... should be able to build a road on this flood land tile in 2 turns ([2 + 1] + [2 + 1] = 6 BWT)" and "Exactly as predicted the road is complete".
- **Single player: the stack finishes together and leftover points are lost.** KayEss PDF: "In a single player game the dynamics are such that all workers in a stack will finish at exactly the same time even if this involves wasting worker effort." In multiplayer (PBEM) extra workers are freed as soon as the bucket is full. Also: "cancelling one of the workers cancels both their moves for that turn." The forum thread of the article (https://forums.civfanatics.com/threads/worker-moves.93408/page-2) disputes how much is lost: GD_france (21 Oct 2004): "when a worker is ordered to cancel working, the entire job he made is lost." KayEss (22 Oct 2004) then called his cancellation rules "mainly conjecture". Thot (11 Jul 2004): with a slave, "you save one basic worker turn (bwt), when you move to the tile to be roaded" (untested). Lanzelot, "Interturn Mechanics", https://forums.civfanatics.com/threads/interturn-mechanics.443195/ (C3C, Oct 2011), puts this work at the start of the interturn: "First all worker actions are calculated." City food, shields and border growth come later in the same interturn.
- **Captured workers and the Industrious trait (resolves former Disputed 3).** With the formula above, an Industrious civ's slave makes 2 × 1.5 × 0.5 = 1.5, rounded down to 1, the same as a non-Industrious civ's slave. After Replaceable Parts it makes 3 against 2. This is why some players saw no Industrious bonus for slaves and others did. Spoonwood (thread 452798) adds that slaves "don't lose any speed pre Replaceable Parts" in Anarchy (1 × 0.5 = 0.5, raised to the minimum of 1).
- **Code reading agrees.** Flintlock (C3X-THREAD post-16784827, page-133 #2657): "The total number of points needed to complete a job equals the "turns to complete" set in the editor" (on a move-cost-1 tile; he does not mention the terrain multiplier) and "A typical worker contributes 2 points per turn".
- **Derived per-turn points** (Spoonwood, same thread): "Slaves have a factor of 1 at 100% worker speed." "Regular workers have a factor of 2 at 100% worker speed." "Industrious workers have a factor of 3 at 100% worker speed."

### 5.3 Derived turns, one normal native worker, WorkerRate 2, no trait, before Replaceable Parts

This table is DERIVED from base × move cost ÷ 2. The cells marked "obs" match DoubleEdged's observed numbers.

| Job | Move cost 1 (Desert/Plains/Grassland/Tundra/Flood Plain) | Move cost 2 (Hills/Forest/Marsh) | Move cost 3 (Mountains/Jungle) |
|---|---|---|---|
| Road | 3 (obs) | 6 (obs) | 9 (obs) |
| Irrigation | 4 | — | — |
| Mine | 6 (obs) | 12 (Hills, obs) | 18 (Mountains, obs) |
| Railroad | 6 | 12 | 18 |
| Fortress | 8 | 16 | 24 |
| Plant Forest | 9 | — | — |
| Clear Forest | — | C3C 4 / PTW 10 (obs 10) | — |
| Clear Wetlands | — | 16 (Marsh) | 24 (Jungle, obs "Clear jungle: 24") |
| Clear Damage | 12 | 24 | 36 |

---

## 6. Natural resources

### 6.1 Resource list (C3C BIQ GOOD)

| Resource | Type | Food/Shield/Commerce bonus | Prerequisite (revealed by) | Appearance ratio | Disappearance (1 in N per turn) | Terrain (BIQ TERR possible resources) |
|---|---|---|---|---|---|---|
| Horses | Strategic | 0/0/+1 | The Wheel | 160 | never (0) | Plains, Grassland, Hills |
| Iron | Strategic | 0/+1/0 | Iron Working | 160 | 800 | Hills, Mountains |
| Saltpeter | Strategic | 0/0/+1 | Gunpowder | 120 | 800 | Desert, Hills, Mountains |
| Coal | Strategic | 0/+2/+1 | Steam Power | 120 | 400 | Hills, Mountains, Jungle |
| Oil | Strategic | 0/+1/+2 | Refining | 120 | 200 | Desert, Tundra, Marsh |
| Rubber | Strategic | 0/0/+2 | Replaceable Parts | 120 | never (0) | Forest, Jungle, Marsh |
| Aluminum | Strategic | 0/+2/0 | Rocketry | 120 | 400 | Tundra, Hills |
| Uranium | Strategic | 0/+2/+3 | Fission | 100 | 100 | Mountains, Forest |
| Wines | Luxury | +1/0/+1 | — | 0 (random) | never | Plains, Grassland, Hills |
| Furs | Luxury | 0/+1/+1 | — | 0 | never | Tundra, Forest |
| Dyes | Luxury | 0/0/+1 | — | 0 | never | Forest, Jungle |
| Incense | Luxury | 0/0/+2 | — | 0 | never | Desert, Hills |
| Spices | Luxury | 0/0/+2 | — | 0 | never | Forest, Jungle |
| Ivory | Luxury | 0/0/+2 | — | 0 | never | Plains, Forest |
| Silks | Luxury | 0/0/+3 | — | 0 | never | Forest, Jungle |
| Gems | Luxury | 0/0/+4 | — | 0 | never | Mountains, Jungle |
| Whales | Bonus | +1/+1/+2 | — | 0 | never | Sea |
| Game | Bonus | +2/0/0 | — | 0 | never | Tundra, Forest, Marsh |
| Fish | Bonus | +2/0/+1 | — | 0 | never | Marsh, Coast, Sea |
| Cattle | Bonus | +2/+1/0 | — | 0 | never | Plains, Grassland |
| Wheat | Bonus | +2/0/0 | — | 0 | never | Plains, Grassland, Flood Plain |
| Gold | Bonus | 0/0/+4 | — | 0 | never | Hills, Mountains |
| Sugar | Bonus | +1/0/+1 | — | 0 | never | Plains, Hills |
| Tropical Fruit | Bonus | +1/0/+1 | — | 0 | never | Jungle |
| Oasis | Bonus | +2/0/0 | — | 0 | never | Desert |
| Tobacco | Bonus | 0/0/+1 | — | 0 | never | Grassland, Hills |

- Source: BIQ GOOD `Type` (0 = bonus, 1 = luxury, 2 = strategic), `FoodBonus`, `ShieldsBonus`, `CommerceBonus`, `Prerequisite`, `AppearanceRatio`, `DisappearanceProbability`, and the BIQ TERR→GOOD bit array. For example, `Name=Iron ... Type=2 AppearanceRatio=160 DisappearanceProbability=800 Prerequisite=7 ShieldsBonus=1`.
- The PEDIA `#TERR_*` "may contain" lists match the terrain column exactly.
- The PEDIA bonus tables (`#GCON_ResourcesB/L/S`) match BIQ, except that Dyes and Incense are swapped. See Disputed.
- **Version differences (BIQ-V, BIQ-PTW).** Vanilla 1.29f and PTW 1.27f have 22 resources: no Sugar, Tropical Fruit, Oasis or Tobacco (those four are C3C additions). All shared resources have the same values as C3C except **Incense, which is +1 commerce in vanilla and PTW** (+2 in C3C). Dyes is +1 in all three versions. The vanilla and PTW Civilopedias (`<civ3>/Text/Civilopedia.txt`, `<civ3>/civ3PTW/Text/Civilopedia.txt`) both give "Dyes +0 +0 +1" and "Incense +0 +0 +1", so they match their rule files. The C3C Civilopedia changed Dyes to +2 instead of Incense.
- README-129: "Game bonus resource now gives +2 food to its tile."
- CFC Resource reference, https://civfanatics.com/civ3resources.shtml, gives the same prerequisites ("Rubber requires Replaceable Parts", and so on).

### 6.2 Resource rules

- **Placement count.** EDHELP "Appearance Ratio": "In an eight-player game, a value of 160 would mean 2 of each resource per player. ... Setting this value to zero means the resource will be distributed at random. Note that resource placement is also limited by which terrain types can support the given resource." INFOC: "Appearance ratio has no effect on the bonus resources."
- **Disappearance.** EDHELP "Disappearance Probability": "A value of 800 would mean that there is a 1 in 800 chance each turn to "lose" the resource. Setting this value to zero means the resource will never disappear. Note that this value is not influenced by use; a resource can disappear even if a civilization has never used the resource." and "Note 2: "Lost" resources will always re-appear somewhere else on the map, so the fundamental amount of resources in a game world will never drop below the amount set in the appearance ratio."
- **Only resources in use deplete (tested; engine ruling).** Optional, "When do resources deplete? (Tested)", https://forums.civfanatics.com/threads/when-do-resources-deplete-tested.329362 (C3C, Jul 2009). Method: Iron's disappearance value set to 1 in the editor, then road, city connection, colony and unconnected roaded tile tried in turn. Findings: "A resource that can deplete, must be available to a city to stand a chance of depletion, being roaded alone is not enough." For a roaded tile inside the borders with no link to a town: "Then it does not deplete." On colonies (revised post #14): "if a resource is only available through a colony, it obviously cannot deplete." Bartleby disagreed from memory ("I've seen colonised resources deplete"), with no test. An earlier vanilla test agrees on roads: Allemand (Apolyton "Resources - The old way rules", https://codehappy.net/apolyton/threads/37568-1.htm, 11 Dec 2001): "there is a chance the resource will disappear when it is connected to your trade net" and "When there is no road the resource will not disappear. I've tested this and found it to be true." Badtz Maru, same thread, disagreed without a test. This contradicts the EDHELP note that depletion "is not influenced by use". The engine follows the tested rule (owner ruling: only resources in use deplete).
- **Reappearance (community).** wilbill, https://forums.civfanatics.com/threads/disappearance-of-resource.114748 (C3C, Mar 2005): "If it disappears, it reappears on the same turn at a different (or rarely, the same) location." gmaharriet, https://forums.civfanatics.com/threads/why-must-resources-deplete.246436 (2007): "Any resource which depletes will reappear somewhere...you just can't count on it being in your own territory." This agrees with EDHELP Note 2. Skanky Burns (Apolyton 37568 page 2, vanilla, Dec 2001) saw the opposite once: "EVERY SINGLE oil resource had been used up. No new sources appeared." One exception was observed by Kirejara, https://forums.civfanatics.com/threads/resource-probability-ratios.551214/ (2015, on a PTW scenario map): "Actually it can completly disappear if there is no other place for it to reappear."
- **Strategic counts in practice (community).** timerover51 (same thread): "the number of strategic resources was equal to plus or minus 1 of the number of players". That is about one per player, against EDHELP's "a value of 160 would mean 2 of each resource per player" (8 players). It is not stated which ratio values his games used. He repeats it in https://forums.civfanatics.com/threads/no-coal-anywhere.340842/ (Nov 2009): "Generally, you will have about the same number of each strategic resources as their are civilizations on the map." The generator pseudocode below accounts for this: a ratio of 120 with two or three eligible terrains gives 120 × civs / 100 × 0.75 = 0.9 per civ, and Iron and Horses (160) give 1.2 per civ (worked, not tested).
- **Luxury placement amount (code-level).** C3X-INI (`luxury_randomized_appearance_rate_percent`): "Under the standard rules, no luxuries have appearance rates set. Without a set rate, the game randomly chooses a rate between 50 and 100, biased toward the middle (75)."
- **How the map generator places resources (code-level).** C3X-NOTES `generate_resources.txt` (https://github.com/maxpetul/C3X/blob/master/Notes/generate_resources.txt, read 2026-10-20) is "the Civ 3 map generator's resource placement method converted into pseudocode". A resource can go on a tile only if all of these hold:
  - "The tile's terrain can hold the resource, according to the scenario data";
  - "For strategic resources, there are no resources of any type on the neighboring tiles";
  - "For luxury and bonus resources, there are no resources of a different type on the neighboring tiles";
  - "For luxuries, there are no luxuries of a different type nearby (subjectively, within an area of about 5 tiles)";
  - "For luxuries, the continent is large enough (37 or 75 tiles, depending on map size)";
  - "The tile does not have the bonus shield flag (indicating bonus grassland, but the flag is set for non-grassland tiles too, including hills and mountains)".
  The order is luxuries, then strategic resources, then bonus resources:
  - **Amount.** For luxuries and strategic resources, `appearance_rate` is the BIQ ratio, or "50 + x + y where x, y are random integers taken uniformly from [0, 25]" when the ratio is 0. It is then multiplied by `civ_count / 100`. Eligible terrains are counted (a land terrain counts 1, a water terrain 5). With one eligible terrain the rate is halved, with two or three it is ×0.75 (at least 1 in both cases), and otherwise it is at least 2. So Iron's 160 in an 8-civ game is 160 × 8 / 100 = 12; Iron has two eligible land terrains (Hills, Mountains), so ×0.75 gives 9 (worked from the pseudocode, ignoring rounding; whether `civ_count` includes the barbarians is not stated). EDHELP's "2 of each resource per player" is the 8-civ case before the terrain scaling.
  - **Luxuries come in clumps on one continent.** Each luxury type is tied to the continent of its first copy ("the tile is on the luxury's continent"). After each copy the generator may, on a coin flip, add copies on neighbouring tiles that have fewer than 3 copies of it around them.
  - **Strategic resources** are placed one copy at a time on random eligible tiles anywhere.
  - **Bonus resources** loop over the bonus types, each placed with a chance set by its eligible-terrain count (die of 6, 4 or 2 sides, a hit on 0 or 1), until the non-luxury total reaches `map.tile_count / 32` or a pass places nothing.
  - C3X-INI confirms the neighbour rule as stock: its option "Relaxes a rule that prevents resources from appearing directly next to another resource of a different type".
- **Phantom resource bug does not affect the standard rules.** C3X-INI: "Cities off the capital's network have reduced lists of just 32 bits ... Resources beyond the first 32 will share bits in those reduced lists." CIVWIKI `Der Phantom-Ressourcen Bug (Civ3)` (rev. 23050): "Der Bug tritt auf, wenn mehr als 32 Ressourcen in einem Szenario eingesetzt werden" ("the bug occurs when more than 32 resources are used in a scenario"). Standard C3C has 26 resources.
- **Only strategic resources come back (code reading).** Flintlock (C3X-THREAD post-16784827, page-133 #2657): "The code simply checks if a depleted resource is strategic before respawning it". So a depleted luxury is gone for good. This is the code against EDHELP Note 2 ("Lost" resources "will always re-appear") and the players above, who spoke of strategics (oil, iron). In stock C3C only strategic resources have a disappearance chance (BIQ GOOD as imported into the repo's `C7/Lua/civ3/ruleset.json`: Iron and Saltpeter 800, Coal and Aluminum 400, Oil 200, Uranium 100, every other resource 0), so the luxury case arises only in mods. Kokuryutei (post-16953376, page-181 #3618) reports depleted resources coming back at random.
- **Limits.** Flintlock (C3X-THREAD post-16959940, page-184 #3665): "The game is limited to 256 resources." timerover51 (post-16542201, page-86 #1714): 32 strategic and luxury types before the phantom bug starts. The generator ties strategic counts to the number of players (Kangaroo262 and Virote_Considon, post-16540000 and post-16540018, page-85 #1695/#1696), which matches the pseudocode above.
- README-129: "Fixed bug which sometimes caused depletion of resources so that there would be < 1 per civ."
- SCRIPT `#GOODDISAPPEARED`: "This source of $RESOURCES0 has been exhausted!" and `#GOODAPPEARED`: "We have discovered a new source of $RESOURCES0!"
- **Visibility.** PEDIA `#GCON_ResourcesN`: "Bonus resources are always visible in explored areas of the map." and "Strategic resources appear on the map when a Civilization Advance exposes their usefulness."
- **Terraforming leaves resources alone.** Apolyton (inca911), https://apolyton.net/forum/miscellaneous/archives/civ3-strategy-archive/42626-distilled-civ3-tips-and-notes?p=953406: "changing the terrain of a resource square does not change the resource as it did in Civ2 via terraforming." This is community testing.
- **Connection.** PEDIA `#GCON_Trade`: "Two cities are connected if any one of the following are true: there is a road or railroad running between them, there are harbors in both cities and a visible water route between them, or both cities contain an airport." The same entry adds: "two cities are not connected if the only road/rail that connects them passes through enemy territory, or if a harbor is blockaded by enemy naval forces." PEDIA `#GCON_Territory`: tradable resources "are easier to get when found within your territory (no need for a colony)." INFOC: "To access any resource, you need to build a road to that resource."
- **One source is enough.** PEDIA `#GCON_Trade`: "A single resource icon is enough to supply the needs of an entire civilization."
- **Required resources.** BIQ TFRM Railroad needs Iron and Coal (§4.1). Units and buildings are covered in their own sections.

---

## 7. Flood plains, disease, plague

- BIQ TERR `TerrainFlags`: the "causes disease" bit is set on Flood Plain, Jungle and Marsh. The "cured by Sanitation" bit is set on Flood Plain only. `DiseaseStrength=50` on all of them.
- PEDIA `#GCON_Disease`: "[Disease] kills citizens and units that spend too much time in unhealthy areas like floodplains, jungles and marsh. Military units that fortify in these terrains are susceptible to disease." ... "Your workers can clear jungles or marsh and eliminate that risk. Floodplains continue to be risky until you discover sanitation."
- PEDIA `#TECH_Sanitation`: "Disease from Floodplains is eliminated, and the effect of Plague is reduced."
- **Workers are immune.** README-129: "Corrected Disease Civilopedia chapter to reflect workers are immune to disease."
- EDHELP "Disease (Terrain Properties)": "By default, both Floodplain and Jungle are flagged as disease-causers. The "Disease Strength" dialog indicates how often disease will strike the indicated terrain. As an example, both Floodplain and Jungle have a 50% disease strength; the higher the number, the stronger the disease." See Disputed on marsh.
- SCRIPT `#UNITJUNGLEDISEASE`: "Our $UNIT0 has died of disease from the jungle!" and `#SUMMARY_DISEASE`: "Disease strikes $CITY0."
- **Executable bug: flood-plain disease actually stops with Writing, not Sanitation (code-level).** C3X-INI, "DISEASE CURING TECH FLAG BUG": "In the base game, the tech flag "Disables Diseases From Flood Plains" (applied to Sanitation) does nothing and instead flood plains disease stops once players discover Writing. This happens because that flag is encoded as number 8 and, instead of checking if players have a tech with that flag, the code mistakenly checks if players have tech #8, which is Writing under the standard rules." Tech #8 is Writing in all three rule files (BIQ, BIQ-PTW, BIQ-V TECH lists). So in unmodded C3C 1.22, flood plains stop causing disease at Writing, whatever the PEDIA says. Jungle and Marsh disease is not stopped by any tech. TimBentley, https://forums.civfanatics.com/threads/disease-from-flood-plain.102771/ (C3C, 2004): "Sanitation only removes disease from floodplains." That is what the PEDIA says; nobody in that thread timed it.
- **Flintlock confirms the bug and calls it unintended (C3X-THREAD, code reading).** (post-16693789, page-118 #2346): "the original developers called the wrong function." (post-16695019, #2354): "The game simply checks for tech #8, which is the ninth tech in the list since the first one is #0." (Writing in the stock rules, as C3X-INI says.) / "the original developers did not intend for it to work like that." Fixing it "causes a lot more disease under the standard rules" (post-16696797, page-119 #2363), since flood plains then stay unhealthy until Sanitation. Civinator (post-16955706, page-183 #3643): "In original C3C you only have to learn Writing." haluu (post-16343544, page-51 #1020) saw the Sanitation flag fail to stop flood-plain disease. The intended rule (Sanitation, as PEDIA says) is the one to follow; the engine does not reproduce the Writing bug. tjs282 (post-16955698, #3642): (paraphrase from a summarised fetch) "The pop-loss happens simply because the disease-causing terrain is (still) in the BFC."
- **Only fortified units die of disease (tested).** Brain, https://forums.civfanatics.com/threads/death-by-jungle-fever.113653/ (C3C, Chieftain, Mar 2005): 50 warriors fortified in jungle lost 20 to disease over 510 turns (gaps of 3 to 73 turns, average 25.6). Then: "I also ran the test with unfortified (but not moving) units and none of the 50 warriors had died after 200 turns." This matches PEDIA ("Military units that fortify in these terrains are susceptible to disease").
- **Cities lose two citizens (community).** CIVWIKI `Krankheit (Civ3)` (rev. 50341, 2014): a city with disease terrain in its radius and at least two citizens "tötet diese stets **zwei** Bevölkerungspunkte, soweit möglich" ("always loses two population points, if possible"), one in each of two consecutive turns. The chance is "auf ca. 1% geschätzt" ("estimated at about 1%"). morchuflex (CFC 102771, 2005): "you always get exactly two turns of disease in your city, losing 2 citizens" and "abstaining from working floodplain tiles does NOT stop the disease." HellboundHeathcliff, https://forums.civfanatics.com/threads/cause-of-plague.120157 (2005): "just the presence of disease causing terrain within the city radius will put that city/town at risk." Qitai, https://forums.civfanatics.com/threads/maya-flood-plain-start.74466/ (7 Jan 2004, C3C era): "If you get hit by disease once. You will get it again the next turn 100% unless you are already at size 1." and "The dice will be rerolled again on the 3rd turn." Those are community observations; the exact formula is still unknown.
- **Illness from the binary (authoritative since 2026-11-12, used by the engine).** Civ3 binary (Perez's decompile, `doc/perez-findings/Civ3_Findings.md`), "Pollution and illness" and Round 2b: a city falls ill on `Rand(0x300 - terrainDisease*512/100)` against its size; "ill cities lose a citizen every turn and recover when `Rand(128) > size`". The owner ruled on 2026-11-12 that the decompile is authoritative; it **supersedes the owner's 2026-10-23 ruling** (0.5% a turn, one citizen, then a 50% chance of a second, then 20 turns' immunity). The engine's reading (`C7Engine/C7GameData/Disease.cs`): terrainDisease is the strongest disease strength in the city's radius (50, so `Rand(512)`); the city falls ill when the roll is below its size (size 8: 1.6% a turn); the turn it falls ill costs a citizen, and each later ill turn costs one and then rolls for recovery against the new size. That gives the observed two citizens on two turns running (CIVWIKI, morchuflex, Qitai below). A size 1 city doesn't shrink. No building protects. The comparison direction and the ill-turn order are our reading; the decompile gives the rolls only.
- **Unit disease from the binary.** Civ3 binary (Perez's decompile, `doc/perez-findings/Civ3_Findings.md`), Round 2b, `Unit_StartOfTurn`: "fortified land units with a zero PRTO field in jungle (terrain 8) are lost to disease with chance 1/1000". Only jungle, not flood plains or marsh, and not scaled by disease strength. This matches Brain's rate (below). The engine keeps workers immune (README-129) and keeps the cure-tech check (no tech cures jungle in the standard rules).
- **Plague (C3C).** PEDIA `#GCON_Plague`: "Plague, if and when it occurs, can wipe out city population and land units (except Kings). The effect of plague is exacerbated in larger cities, walled cities and cities with commerce and a trade network."
- **Plague is off in the standard rules.** conquests.biq GAME section (read with QueryCiv3): `PermitPlagues=0`. The stored values, unused unless plague is enabled, are `PlagueName=Plague EarliestStart=-700 Variance=5 Duration=100 Strength=80 GracePeriod=1000 MaxOccurrences=3`. EDHELP "Permit Plagues (Disasters)" defines these fields ("Grace Period: Indicates how long in years between each plague", and so on). Pounder (CFC 120157, 2005): "As you can see the permit plague is not checked off in the random conquest game." bingen (same thread): "plague is only enabled in some Conquests unless you enable it with the editor." So plague occurs only in scenarios that enable it, and is out of scope here.
- **No disasters in multiplayer.** README-C3C: "Disasters such as plague and volcanic eruption do not occur in multiplayer games."

## 8. Volcanoes

- Volcano tile: 0/3/0, defence 80%, move cost 3. It allows no cities, colonies, roads or improvements (BIQ).
- EDHELP "Volcano Eruptions (Disasters)": "Each volcano on the map will erupt at least once within the indicated eruption period (shown in years)."
- **Default eruption period: 5000 years.** conquests.biq GAME section (read with QueryCiv3): `EruptionPeriod=5000`. Read with EDHELP, each volcano erupts at least once per 5000 game years.
- Community (scratchthepitch, https://forums.civfanatics.com/threads/settle-near-a-volcano.402191/, 2010): "A volcano is likely to erupt more than once in a regular unmodded game" and "2 tiles away from a volcano is generally safe enough, they only rarely lava a tile 2 tiles away." This is opinion, not a test.
- PEDIA `#GCON_Volcanic_Eruptions`: "Most of the time, however, a dark cloud begins to emanate from the volcano's "mouth" a few turns in advance of an eruption -- but such a warning cannot be guaranteed." and "When a volcano erupts it spews forth lava -- in the form of pollution -- that will mar the landscape of many of the tiles adjacent to the volcanic mountain." Workers clear the fallout with Clear Damage.
- SCRIPT: `#VLC_ACTIVE` "Volcano is now active!", `#VLC_KILLS_UNIT` "Our units were burned to a crisp by a lava flow!", `#VLC_KILLS_CITY` "$CITY0 is torched by the volcanic eruption!"
- Community (Optional, CFC), https://forums.civfanatics.com/threads/questions-regarding-volcanos.330741: "the chance decreases significantly as the game goes on." The cause is that the period is in game years.
- **Placement and eruption timing (code reading).** Flintlock (C3X-THREAD post-16620417, page-106 #2112): (paraphrase from a summarised fetch) "the way the game places volcanos is by randomly switching some would-be mountain tiles." / "The chance to place a volcano instead of a mountain is 2, 4, or 6% depending on the world age." (post-16878439, page-156 #3119): "The game only rolls for volcanos to become active at most every two years." and the chance for each volcano "is proportional to the number of years" since the last roll, "up to a maximum of 50". That fits the period in years and the falling rate as turns get shorter in years (Optional, above). Which world age gives which percentage is not stated.
- The damage radius is disputed (Disputed list).

---

## 9. City radius and city spacing

- **21-tile radius.** PEDIA `#GCON_Radius`: "A square is part of a city's radius if it is within the city's cultural borders, and within two squares of the city."
  MANUAL p.75: "The potential area of development, called the City Radius, extends out from a city in an area three map squares wide-- two squares to the northeast, northwest, southwest, and southeast. The resulting "radius" looks like a fat X."
  MANUAL ch.3: "each city can assign citizens to generate resources in any of the 20 surrounding terrain squares. Since this city is new, however, the workable radius is temporarily limited to the nearest eight. The pattern of 21 squares with the city at the center is called the City Radius."
- **No radius growth in standard C3C.** No base-game, PTW or C3C source found mentions the radius growing past the 21-tile fat cross. The C3C PEDIA `#GCON_Radius` text is unchanged. Larger radii exist only in mods (C3X), which are out of scope.
- **The city tile is always worked.** MANUAL p.75: "The terrain a city occupies is especially important, because it is always being worked. You cannot take the workforce off this square".
- **Minimum distance.** MANUAL p.~131: "Note that you cannot build cities in terrain squares directly adjacent to an existing city." So two cities need at least one tile between them, which makes the minimum distance 2. A Steam thread (https://steamcommunity.com/app/3910/discussions/0/3453716885198230413) shows the editor has no setting for this: "I didn't find an option for that in the editor." It is changeable only with C3X, which is out of scope.
- **Terrain under a new city.** Apolyton (piratebrun, thread 85881 above): when a city is built on a forest, "it is converted to its base terrain." This is community testing.
- **Minimum distance confirmed at code level.** C3X-INI (`minimum_city_separation`): "if it's set to 1, cities cannot be founded adjacent to one another, there must be at least one open tile separating them. A setting of 1 there corresponds to the standard game rules."
- **City tile yields.** These are covered in the city section (02, "City center tile"): food is fixed at 2 (Agricultural +1), shields at least 1, plus size bonuses. C3X-INI confirms the food rule from the code (`city_center_food_yield`): "standard: All CC tiles produce enough food to feed one citizen regardless of terrain or resources, as in the base game". It also notes an exception to the Despotism penalty: "Under the standard game rules, the Despotism tile penalty does not apply to food yield from city tiles of Agricultural civs with fresh water." (Former open question.)

---

## 10. Map generation basics (cheaply sourced only)

**World sizes (BIQ WSIZ)**

| Size | Width × Height | Default civs | DistanceBetweenCivs | OptimalNumberOfCities | TechRate |
|---|---|---|---|---|---|
| Tiny | 60×60 | 4 | 11 | 14 | 160 |
| Small | 80×80 | 6 | 11 | 17 | 200 |
| Standard | 100×100 | 8 | 12 | 20 | 240 |
| Large | 130×130 | 12 | 18 | 28 | 320 |
| Huge | 160×160 | 16 | 24 | 36 | 400 |

- EDHELP "Distance Between Civs": "Determines the desired minimum distance (in tiles) between the starting locations of civilizations on maps of the selected world size."
- **World setup.** MANUAL p.~45: "Arid: Choosing this option gives your world a larger number of dry terrain squares, such as Plains and Desert. ... Wet: This option produces a larger number of wet terrain squares, such as Jungle and Flood Plain." Temperature: "Cool:This option produces larger numbers of cold and cool terrain squares, like Tundra." and "Warm: This option yields a larger amount of tropical terrain, like Deserts and Jungles." Age: "3 Billion Years: This option yields a young, rough world, in which terrain types occur in clusters." and "5 Billion Years: ... allowing erosion and other natural forces to soften the terrain features." (manual text extract, "Age" and "Temperature"). Players (CFC 297735, https://forums.civfanatics.com/threads/what-is-the-best-map-settings-normal-wet-arid-warm-temperate-cool.297735/, 2008) add, from experience only: "3-billion yr old terrain = more hills/mountains" (capnvonbaron) and, for vanilla Wet, "I did get more rivers and didn't actually get a lot more jungles" (darski).
- **Start positions and generator bugs (C3X-THREAD).** Flintlock (post-16172297, page-23 #454): "starts are linked by continent not distance." (post-16354736, page-54 #1069): "This is an error in the base game's map generator" (it can place AI capitals next to each other, when many maps are generated without restarting the game). (post-16635376, page-108 #2147): "The logic for placing starting locations is surprisingly complicated." tjs282 (post-16953404, page-182 #3621) says the generator picks some or all of the American culture group as "Random" opponents first, which "is a known bug in the base game -- and not just on Continents maps." (he ties it to the Culturally Linked Starts option with all-random opponents). Both are shipped bugs; the intended behaviour (spaced starts, an even draw of opponents) is the rule.
- **Seed.** README-C3C v1.20: "Implemented ability to enter a seed value on the world setup screen." README-129: "Made a slight change to placement of goody huts and bonus grassland tiles." The generator algorithm itself is UNVERIFIED and out of cheap scope.
- **What the seed reproduces (community, C3C era).** https://forums.civfanatics.com/threads/what-does-the-seed-do.231006/ (Jul 2007). Bartleby: "If all the climate contitions, etc. are the same, if you enter the same seed number you'll get the exact same map." Tasslehoff: "You will get the same map if and _only if_ you have the same preferences." gmaharriet: "To get the same map you must use the same size (like huge), same wetness (like arid)," but "You can change the difficulty level, barbarian settings, aggression level", and "you must have exactly the same number of seafaring civs and in the exact same order", because "seafaring civs always start on a coast". These come from experience, not tests.

---

## 11. Barbarians

### 11.1 Activity levels

| Level | Meaning |
|---|---|
| No Barbarians | "No goody huts and no barbarian camps" |
| Sedentary | "Goody huts only; no barbarian camps" (EDHELP). The MANUAL instead says "Barbarians are restricted to their encampments. The surrounding terrain is free of their mischief." |
| Roaming | "Lowest level of barbarian camp activity". MANUAL: "less frequently and in smaller numbers than at higher levels. This is the standard level of barbarian activity." |
| Restless | "Median level of barbarian camp activity". MANUAL: "appear in moderate up to significant numbers, at shorter intervals than at lower levels." |
| Raging | "Highest level of barbarian camp activity". MANUAL: "The world is full of barbarians, and they appear in large numbers." |
| Random | "Randomly-selected level of barbarian camp activity" |

- Source: EDHELP "Barbarian Activity (Scenario Properties)" for the quoted level meanings, and MANUAL p.~45 as noted.
- README-129: "Added Barbarian Activity: No Barbarians."
- EDHELP "Select Barbarian Activity": "This controls how often barbarians will spawn, and how far they will stray from their encampments."

### 11.2 Camps, raids, uprisings

- **Camps only outside borders; their units outlive them.** MANUAL-CMPL p.120: "Barbarians arise in areas that are outside the borders of any civilization." "Thus, expanding your network of cities over a continent eventually removes the threat of barbarians". On taking a camp: "The village is destroyed and will create no more units. (Any units already outside the village, however, continue to exist.)"
- **Destroyed camps relocate.** PEDIA `#GCON_Barbarians` (Aggressive Tribes): "By destroying a tribal village you can stop the raids from that village. But the survivors will move to a new site in explored or unexplored territory and begin raiding from there."
- **Camps produce units at intervals; mass spawns at era changes (community).** CIVWIKI `Barbaren (Civ3)` (rev. 14 Aug 2023): camps appear "nur in Gebieten, die z.Z. von keiner Zivilisation kontrolliert werden" ("only in areas no civ currently controls") and produce units "in gewissen Zeitabständen" ("at certain intervals"). A mass spawn happens "wenn zwei der Zivilisationen auf der Karte in ein neues Zeitalter wechseln" ("when two of the civs on the map enter a new era"), and then "Dabei entstehen problemlos 10-20 Barbarenreiter" ("10 to 20 barbarian horsemen easily appear"). This reads as the same rule as INFOC's "the second time a civ enters a new age" (the second civ to enter an era), which is our interpretation.
- **Camps replace leaders.** MANUAL p.90: "If you find and obliterate an active tribe's village, you end the threat from that tribe (and get a bit of spare change in the process)." and "The village takes the place of the barbarian leader."
- **Camp dispersal pays 25 gold.** SCRIPT `#CAPTURE_BARBARIAN`: "We dispersed a $BARBARIAN0 encampment and took 25 gold!"
- **No barbarian leaders and no ransom in Civ3.** MANUAL p.~49 and INFOC: "There are no barbarian leaders. To get their gold, seek out and invade the barbarian encampments." The leader ransom does not exist. The CFC "ransom 50/100/150" thread (https://forums.civfanatics.com/threads/barbarian-leaders.27479) is about Civ2.
- **Where camps respawn.** MANUAL p.90: "new ones arise in areas that are outside your cultural borders, in areas that are not currently seen. As time passes, they appear at even farther distances from civilization."
- **Coastal camps.** INFOC: "Barbarian villages on the coast can spawn seagoing vessels."
- **Raids on cities.** INFOC: "Barbarians do not capture undefended cities. Now, they just pillage the place, take some of your gold, and move on." SCRIPT `#BARBARIAN_CAPTURE_CITY_PRODUCTION`: "Our work on ... has been destroyed!" `#BARBARIAN_CAPTURE_CITY_POPULATION`: "Some of our people were lost!" `#BARBARIAN_CAPTURE_CITY_GOLD`: "They have carried away $NUM0 gold!" `#BARBARIAN_DESTROY_WALLS`: "They have destroyed the $WALLS3!"
- **Uprisings.** INFOC (pre-release): "Barbarian uprisings are triggered the second time a civ enters a new age." SCRIPT `#SUMMARY_BARBARIAN_EXPLOSION_CITY`: "We have heard reports of a massive barbarian uprising near $CITY0!"
  Jaybe (Apolyton, https://codehappy.net/apolyton/threads/57058-1.htm, Jul 2002, second-hand Firaxis): "They occur at the time the 2nd civ advances to a new age, per Soren." Arrian (same thread, "IIRC"): "once 2 civs hit the Medieval Age". So "the second time a civ enters a new age" means the second civ to reach each new era (CIVWIKI says the same, §11.2). Sizes reported in that thread: "each encampment will put out 24 horsemen on 'raging.'" (Arrian); "upto 30 horsemen" (SpencerH); and in 1.29 "uprisings have been reduced to 6 barbs" (ALPHA WOLF 64, one game). All of these are play experience.
  A possible second trigger, second-hand: Konquest02 (Apolyton, https://codehappy.net/apolyton/threads/60492-1.htm, 30 Aug 2002, vanilla) says, crediting "Soren", that there are also uprisings if "you still have uncolonized tiles by the time 2 civs reach the industrial era". Arrian, same thread: "Barbarian uprisings occur when 2 civs have hit the middle ages."
  Community (Apolyton, https://codehappy.net/apolyton/threads/40765-1.htm): "The major uprisings come from a camp that you neglected to wipe out." Harovan: "Their number seems to depend on the barb setting (raging produces stacks of about 24)." ledj: "only 8 barb horsies will show on an uprising episode" on Roaming.
- **Spawn limits (community, TimBentley 2008).** https://forums.civfanatics.com/threads/barbarian-information.267101: "No barb camps will appear until everyone has settled and there are at least (2*NumPlayers) cities." "The maximum number of camps is the number of players". The maximum number of barbarians is "2, 3, or 4" "(for roaming, restless, and raging) * NumPlayers."
- **Raid order on a city (community).** TimBentley (CFC 267101, 2008): "In pillaging a city, barbs will destroy production of more than 10 shields, then kill a citizen, then take gold of any amount (which may be gold/NumCities, or it may be more complex; apparently I haven't tested), then <=10 shields." CIVWIKI `Barbaren (Civ3)` gives a different order: gold first ("Dabei wird 1/Städteanzahl-tel des Schatzkammerbestandes geraubt", "1/number-of-cities of the treasury is stolen"), else all invested shields, else one citizen. Both say the stolen gold is treasury ÷ number of cities. CIVWIKI also says barbarians entering a city destroy "alle dort befindlichen Einheiten mit Ausnahme von Schiffen" ("all units there except ships"). Civinator, https://forums.civfanatics.com/threads/barbarian-cities-in-civ-3.646253/ (2019): "I have never seen, that they razed a city."
- **Barbarian cities do nothing (tested in Debug mode).** Civinator (CFC 646253): "The Barbarian city was not able to build anything (no buildings and no units)." Its "culture ... always stayed at zero", and the barbarians did no research. A barbarian city captured by a player "became a normal city for the civ that has conquered it."
- **Targeting and movement (tested; cause found in the code).**
  - TimBentley (CFC 267101, 2008): "The barbs look for defenseless units or cities (both count the same as far as I can tell) within 2 tiles and 1 turn, then for anything on the NW/SE axis, then fortify or move randomly if NoAIPatrol=0." "In looking at the NW/SE axis, it looks for units within 12 tiles, and goes toward the one that can be reached in the fewest turns, and ends its turn on the tile with the highest defensive value." Ties: "it goes toward a city before a unit, then goes SE before NW."
  - Justus II, https://forums.civfanatics.com/threads/fix-for-the-barbarians-sort-of.100704/ (C3C, Sep 2004, Debug mode): "Range is 12 tiles (maybe 13)." Barbarians "will not react" when no land path exists.
  - **The cause is a bug in the executable.** C3X-INI, "BARBARIAN DIAGONAL BUG": "Barbarians can only search for targets on adjacent tiles or on tiles that are up to 12 steps directly SE or NW of their position. The intended behavior was for them to search for targets on all surrounding tiles up to 6 steps away." Flintlock, https://forums.civfanatics.com/threads/c3x-exe-mod-including-bug-fixes-stack-bombard-and-much-more.666881/page-70#post-16434678 (Mar 2023), reading the code of `ai_move_barbarian`: "The error is that it's adding the dx distance to both the unit's x & y coordinates, instead of adding dx to x and dy to y."
  - **Version history (tested by BlueSox14).** https://forums.civfanatics.com/threads/hunting-down-the-barbarian-nw-se-axis-bug.674365 (2021): in vanilla 1.29f "barbarian ai would appear to 'see' units up to and including six tiles away" in all directions. BlueSox14 says the bug appeared with PTW, was fixed in a later PTW patch, and "resurfaced again with Conquests" (his recollection, not a test). In C3C "This behaviour and distance was the same between the sedentary and raging barbarian settings."
  - **NoAIPatrol.** HellboundHeathcliff (CFC 100704, 2004): "Absence of NoAIPatrol flag is the same as NoAIPatrol=1 for C3C only." and "PTW defaults to =0, C3C defaults to =1." With NoAIPatrol=1, barbarians with no target fortify in place.
- **Code readings on barbarians (C3X-THREAD, Flintlock, C3C 1.22).**
  - Advanced units come with city count, not era: "The game starts spawning advanced instead of regular barbs when there are at least four cities per player on the map." (post-16644538, page-110 #2197). In the base game barbarian units are conscripts; making them start as regulars was a change Flintlock planned for C3X. On the spawn site: "Looking quickly over the barb unit spawning code, I don't see any check for visibility." That is about units spawning from an existing camp; camps themselves still appear only in unseen areas, as the MANUAL says (§11.2). vmxa (post-16644426, #2193): barbarians can spawn where no civ has vision.
  - Barbarian units do not come from city production: "Barb unit spawning is separate from city production." (post-16406050, page-66 #1311), and "it seems the AI production chooser logic just isn't running for the barbs at all." (post-16859226, page-150 #2996). That is why barbarian cities build nothing (Civinator, above). Quintillus (post-16435385, page-71 #1403): a barbarian city "does not grow, either in food nor in shields".
  - No trade network and no resources: "The game does not compute a trade network for the barb player." (#2996; also post-16443119, page-72 #1434). Civinator (post-16405433, page-66 #1303): "Barbarians have no access to resources." Vuldacon (post-16405787, #1309) found from the original editor that "Resources are not Required for Units concerning Barbarians." and that air units are not offered to them.
  - Capturing cities: Lord_Hill (post-16426275, page-68 #1358) says a barbarian unit (paraphrase from a summarised fetch) "cannot capture a city at all", and Flintlock gives the code: it "checks if the unit's owner's ID is 0, in which case the game runs the barbarian capture gold/destroy population logic" (post-16426700, page-68 #1360). Player slot 0 is "hardcoded as being barbarian" (post-16515622, page-80 #1589).
  - Their movement AI is its own routine: In the base game "there is a separate AI routine for all barb units regardless of their AI strategy, and evidently it can't handle settlers." (post-16767586, page-132 #2626). Barbarians cannot build workers or settlers (Civinator, post-16377947, page-60 #1193).
  - Unit availability: Civinator (#1303): every unit can be flagged as available to barbarians, but "this setting is overwritten by the setting for Barbarian units 1 and 2" (the BIQ basic and advanced types below).
  - Barbarians can win only by outlasting everyone: Belofon (post-16443020, page-72 #1433) says they can win only by eliminating all normal civs and then reaching the time limit.
- **Diagonal bug history (C3X-THREAD).** vmxa (post-16110765, page-13 #253): (paraphrase from a summarised fetch) "The barb diagonal bug was caused by a patch we asked for long ago." tjs282 (post-16434762, page-70 #1397) notes that vanilla 1.29f behaved differently, which agrees with BlueSox14's history above.
- **Scenario maps ignore their own barbarian setting.** C3X-INI (`override_barbarian_activity_level_for_scenario_maps`): "Normally, in that case, the game will use the barbarian settings kept in conquests.ini, ignoring what was set for the map in the editor." HellboundHeathcliff (CFC 100704) says the same from testing. This matters only for custom maps.
- **Units.** BIQ RULE `BasicBarbarianUnitType=6` (Warrior), `AdvancedBarbarianUnitType=11` (Horseman), `BarbarianSeaUnitType=29` (Galley). EDHELP: "Default is Warrior." / "Default is Horseman." / "Default is Galley." Players report no upgrade by era in vanilla and PTW: Kramsib (Apolyton "Stronger Barbarians.", https://codehappy.net/apolyton/threads/95755-1.htm, 29 Aug 2003): "the last unit they can get is the horseman." CerberusIV (same thread): "not possible to have barbarians improve over time in Civ III or PTW"; "You can set two types of barbarian land units in the general screen", and the unit must also be flagged as available to barbarians, or "you won't get the barbarians you want." Play experience, not a test; C3C is not covered. CIVWIKI `Barbaren (Civ3)` says only that Warriors come first and Horsemen "nach einer gewissen Zeit" ("after a certain time"), and that ships come only from coastal camps. The Civ1 thread in the inventory ("Barbarians spawn logic", 630389) has era upgrades, but those are Civ1 rules and do not apply.
- **Combat bonus against barbarians (BIQ DIFF `AttackBonusAgainstBarbarians`, %):**

| Chieftain | Warlord | Regent | Monarch | Emperor | Demigod | Deity | Sid |
|---|---|---|---|---|---|---|---|
| 800 | 400 | 200 | 100 | 50 | 25 | 0 | 0 |

  EDHELP: "Determines the attack bonus enjoyed by any player (human or AI) against barbarians. A value of zero means the barbarians have no combat disadvantage". How it enters the combat formula belongs to the combat section.
- **Expansionist.** PEDIA `#GCON_Strengths`: "passive minor barbarians are friendlier." MANUAL: "Barbarian villages are more lucrative."

---

## 12. Goody huts

### 12.1 Outcomes

| Outcome | In-game message (SCRIPT) | Condition (CFC 93244, copied from Apolyton) |
|---|---|---|
| Gold | `#GOODY_MONEY` "We got $NUM0 gold from the $BARBARIAN0 tribe's village." | "The tile must not have any type of resource or luxury on it." |
| Map | `#GOODY_MAPS` "The friendly $BARBARIAN0 tribe gave us maps of their region." | "always available" |
| Nothing | `#GOODY_NOTHING` "This $BARBARIAN0 village is deserted." | "always available" |
| Tech | `#GOODY_TECH` "The $BARBARIAN0 tribe has taught us ..." | "Player must still be in Ancient Times." |
| Free unit ("mercenaries") | `#GOODY_MERCENARIES` "This friendly $BARBARIAN0 village gave us a skilled $UNIT1." | "There must be a unit available to the Barbarians as well as the player" |
| Settler | `#GOODY_SETTLERS` "A friendly $BARBARIAN0 $UNIT1 wants to join our $GOVERNMENT2." | "Player must not have a settler (active or in production) or any unit with the Settle AI strategy." "Player must have less cities than (TotalCities / NumActivePlayers)." |
| City | `#GOODY_CITY` "An advanced $BARBARIAN0 village has joined us!" | no listed condition. Community: "On deity you will never get a city". |
| Barbarians | `#GOODY_BARBARIANS` "We have disturbed an angry $BARBARIAN0 $UNIT1." | "Player must not have Expansionist trait." "There must not be a city within a 1-tile radius." "The player must have at least 1 city." "The player must have at least 1 military unit." "The unit popping the hut must not have the "All Terrain As Roads" ability." |

- Condition source: https://forums.civfanatics.com/threads/probabilities-of-goody-huts-c3c.93244 (C3C; the author built an Excel odds calculator; the attached image and file were not readable).
- The MANUAL p.91 list of outcomes matches: town, advance, gold, military unit, hostile units, deserted, settler, map.
- **Difficulty.** INFOC: "Greater chance of getting goodies when you visit a goody hut on lower levels." INFOC: "Conscript units can be received when exploring goody huts".
- **Expansionist (community).** Same thread 93244: "expansionist can only get gold, map and warrior (equal probability." and "None-expansionists will only get map, warrior and barbarians" (both said of a specific high difficulty). Also "On deity you will never get a city, but settler is quite likely for expansionists."
- **Settler or city right after founding.** Thread 93244: "You cant get a settler or city when building a city, even if you use your last settler to build the city."
- **Outcome table by difficulty (C3C, community, from Oystein's table).** CIVWIKI `Bonushütte (Civ3)` (rev. 50187, Dec 2013; its sources are CFC 93244 and http://www.civforum.de/showthread.php?t=66671), in %. The German level names are mapped to Chieftain, Warlord, Regent, Monarch, Emperor, Demigod, Deity, Sid.

  Non-Expansionist civs:

  | Result | Chief | Warl | Regent | Monarch | Emp | Demi | Deity | Sid |
  |---|---|---|---|---|---|---|---|---|
  | Barbarians | 10 | 25 | 35 | 40 | 65 | 75 | 85 | 90 |
  | Gold | 15 | 10 | 10 | 10 | 5 | 5 | 5 | 0 |
  | Unit ("warriors") | 10 | 10 | 10 | 10 | 5 | 5 | 5 | 5 |
  | Nothing | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
  | Settler | 15 | 10 | 10 | 10 | 5 | 5 | 0 | 0 |
  | City | 5 | 10 | 5 | 5 | 5 | 0 | 0 | 0 |
  | Tech | 35 | 25 | 20 | 15 | 10 | 5 | 0 | 0 |
  | Map | 10 | 10 | 10 | 10 | 5 | 5 | 5 | 5 |

  Expansionist civs (never barbarians):

  | Result | Chief | Warl | Regent | Monarch | Emp | Demi | Deity | Sid |
  |---|---|---|---|---|---|---|---|---|
  | Gold | 15 | 15 | 13.3 | 15.4 | 16.7 | 14.3 | 20 | 33.3 |
  | Unit | 10 | 10 | 13.3 | 15.4 | 16.7 | 14.3 | 20 | 33.3 |
  | Nothing | 5 | 5 | 0 | 0 | 0 | 0 | 0 | 0 |
  | Settler | 15 | 15 | 13.3 | 15.4 | 16.7 | 14.3 | 20 | 0 |
  | City | 10 | 10 | 13.3 | 7.7 | 8.3 | 14.3 | 0 | 0 |
  | Tech | 35 | 35 | 33.3 | 30.8 | 25 | 28.6 | 20 | 0 |
  | Map | 15 | 15 | 13.3 | 15.4 | 16.7 | 14.3 | 20 | 33.3 |

  - These agree with what the 93244 thread says in text. Oystein: "None-expansionists will only get map, warrior and barbarians (clear overweight of barbarians)" (Sid); "On deity you will never get a city, but settler is quite likely for expansionists"; and Dragonlord's reading, confirmed by Oystein ("You are reading correctly"), that "nothing" has probability 0 for normal civs at every level and for Expansionists above Warlord. justanick (93244 page 4, Dec 2024), on Demigod Expansionist: "Tech has a probability of 2/7 vs. 1/7 for the 5 other options."
  - The CFC 24911 samples (vanilla, 2002) report many "deserted" huts, so the vanilla odds probably differed. For C3C, what happens when no listed outcome is possible is not documented.
- **Re-roll on an impossible result.** Oystein (93244, Jul 2004): "If the result is something that you cant get (ex: barbs for expansionists), there will be a redraw." CIVWIKI: "so wird einfach erneut mit unveränderten Wahrscheinlichkeiten gewürfelt (gedächtnislos)" ("it simply rolls again with unchanged probabilities (memoryless)"). Oystein's post #7 describes the draw as a random number compared with cumulative thresholds, for example "If the next number is between 0 and 0.05*(2**15), you will get a city."
- **Reloading does not change a hut.** Ming (Apolyton moderator), https://codehappy.net/apolyton/threads/43888-1.htm (vanilla 1.17f, 2002): "The random "number" is saved, you can reload all you want and the goody hut remains the same."
- **Gold amount (community).** CIVWIKI: normally 25 gold; "Befindet sich der Spieler bereits im Mittelalter, kommen auch Funde im Wert von 50 Goldstücken vor." ("If the player is already in the Middle Ages, finds worth 50 gold also occur.") No other source gives the amounts. The 93244 condition stands: no gold from a hut on a resource tile.
- **Tech: only Ancient-era techs (community).** CIVWIKI: "Man kann nur Technologien der Antike erhalten, wenn diese noch nicht erforscht wurden" ("you can only receive Ancient-era techs not yet researched"), "wobei billigere Technologien eine höhere Fundwahrscheinlichkeit besitzen als teurere" ("cheaper techs are likelier than dearer ones"), and "Das Zeitalter des Spielers ist dabei irrelevant." ("the player's era does not matter"). You get the tech being researched only if it is the last unknown Ancient tech (also Aeson, Apolyton 43888, vanilla 1.17f: huts "won't give the tech being researched unless it is the only Ancient Era tech left"). This replaces the 93244 wording "Player must still be in Ancient Times". In practice the two agree, because a player who knows every Ancient tech has entered the Middle Ages. Aeson's vanilla test (150+ huts on an edited Deity map): "It seems that goody huts don't give techs if you are in the Middle Ages." civforum.de 66671 page 2 (http://www.civforum.de/showthread.php?t=66671&page=2, Mar 2010) is split: justanick says Monarchy and Republic can be found "auch im Mittelalter" ("also in the Middle Ages"), while d73070d0's test (#30) found no Ancient tech from unit-opened huts after reaching the Middle Ages but still found some from culture-opened huts. Still open.
- **Unit ("mercenaries") conditions (tested, C3C).** Doc Tsiolkovski, https://forums.civfanatics.com/threads/on-barbarians-skilled-warriors-revealed-en-detail.103564/ (Oct 2004): "Your Civs must have at least one of the two Barbarian units enabled" and "At least four more Civs must have that unit enabled as well (doesn't matter if those Civs are in the game!)". For a unit that needs a tech, "your Civ and at least four Civs must theoretically know that Tech". "You do not require resources." Test: "Exactly 25 maps and 25 Skilled Warrior" for the Netherlands, "50 Huts, 50 Maps" for Sumeria (CIVWIKI notes that the rule matters mainly for the Sumerians, who get Horsemen instead), and "The very first hut gives a 'Skilled Horseman'" once Horseback Riding was given to Sumeria and four other civs. CIVWIKI counts "drei weiteren Zivilisationen" (three other civs) instead of four. The "two Barbarian units" are BIQ RULE `BasicBarbarianUnitType` (Warrior) and `AdvancedBarbarianUnitType` (Horseman). Doc Tsiolkovski adds that this "need[s not] be true for ptw/VC3".
  - Wolfhart (same thread, 2007): "Wheeled = you get no skilled warriors from Goody huts, not wheeled = you get them." ("it's tested and found to be the case").
- **Settlers and cities: more conditions (community-tested).**
  - A settler or city is never found when the hut is opened by founding a city. Oystein (93244, Sep 2004): "You cant get a settler or city when building a city, even if you use your last settler to build the city." CIVWIKI: "Öffnet man eine Hütte mit einer Stadtgründung, ist kein Siedlerfund möglich" ("a hut opened by founding a city cannot give a settler"), because the founding settler is removed only after the hut is resolved. d73070d0 (civforum.de 66671, Mar 2010): "Bei der Stadtgründung kann man grundsätzlich keine Siedler finden."
  - Owning a settler does not block a city result. microbe (93244, Mar 2005, with a C3C 1.22 save): "You CAN pop a city when you have a settler." CIVWIKI agrees.
  - A hut becomes a city only far from other cities. CIVWIKI: "Hütten müssen einen Abstand von mindestens 4 zur nächstgelegenen Stadt besitzen, damit sie zu einer Stadt werden können." ("a hut must be at least distance 4 from the nearest city to become a city"), with distance measured as for corruption (diagonal steps count 1.5). d73070d0 (civforum.de 66671): "Städte können nur in einem Mindestabstand von vier von anderen Städten gefunden werden." Also: "Es ist erst dann möglich, eine Stadt zu finden, wenn man bereits eine besitzt." ("you can only find a city once you own one"). The thread did not test whether foreign cities count.
  - Huts can also be opened by border growth. For an Expansionist civ a hut opened this way at the fourth culture ring (1000 culture) can give a city (CIVWIKI; d73070d0). Once all Ancient techs are known, a hut opened by border growth or city founding gives no unit, settler or city (CIVWIKI). Opening it with a unit still can (d73070d0, post #8: "Es ist offensichtlich möglich, weiterhin auch Städte, Krieger und Siedler zu finden, wenn man Hütten mit Einheiten aufdeckt").
  - Maps are still found when the whole map is known (d73070d0: "Umgebungskarten können selbst dann noch in Hütten gefunden werden, wenn die gesamte Weltkarte aufgedeckt ist", confirmed by test in post #8).
- **Barbarians: the "All Terrain As Roads" condition means Explorers and Conquistadors.** CIVWIKI: "Die aufdeckende Einheit darf kein Konquistador und kein Kundschafter sein." ("the unit opening the hut must not be a Conquistador or an Explorer"). TimBentley (93244, 2005): "The unit doesn't matter, unless you're a non-expansionist using an explorer or conquistador".
- **No barbarians from a hut opened by founding a city (engine ruling).** AutomatedTeller (93244, Apr 2006): "You cant' get barbarians if you pop the hut with a settler making a city." C1v1l1z@t10n (CFC 630389, a Civ1 thread, citing DarkPanda's Civ1 research): "If you build a city next to a hut, it can never be Barbarians." The second is a Civ1 rule. The Civ3 condition list's "There must not be a city within a 1-tile radius" also excludes it once the city exists. Owner ruling in the engine: a hut opened by founding a city never holds barbarians.
- **Community odds (small samples).** https://forums.civfanatics.com/threads/goody-hut-probabilities.24911/latest:
  - Regent: tech about 30%, gold about 10%, unit about 10%, map about 10%, barbarians about 35%, deserted about 5%.
  - Chieftain: tech about 50%, gold about 15%, unit about 10%, barbarians about 10%, map about 10%, nothing about 5%. "only Warriors and Horsemen are produced".
  - Deity, non-expansionist, after the first city: deserted about 55%.
- **Difficulty and city count (community).** Erlend Philip Wood (C3X-THREAD post-16989322, page-191 #3804, answering a question about huts): "You're not supposed to get cities on Deity." / "you won't get a city if you have more cities already than the average of the other players." The last is the settler condition above applied to cities; the Deity rule matches the table.
- **Huts and barbarian settings.** EDHELP: huts exist on Sedentary and above; "No barbarians" means "No goody huts". EDHELP "Goody Hut (Tile Properties)": a hut cannot share a tile with "a barbarian camp or starting location".
- **Bug fixes.** README-129: "Fixed bug in Civ3 where Scout runs an extra space after revealing a goody hut." README-129 also changed hut placement in map generation (§10).

---

## Disputed

Items marked RESOLVED keep their number so that cross-references still work. Their sourced rule is in the main text.

1. **Railroad bonus.** PEDIA and MANUAL say only "increases irrigation or mine output".
   - Community tests: +1 food if irrigated, +1 shield if mined, nothing on unimproved or forest tiles (Weldon on Apolyton 42626; Yeti on CFC 74236; Sullla).
   - Strollen (same Apolyton page): "railroads do work as they say in the manual that add 50% to any improvement (fractions round up)".
   - A Civ3 fandom wiki page (https://civilization.fandom.com/wiki/Improvement_(Civ3), not fetchable, HTTP 402) is reported to say "+1 shield to any tile that already produces at least one shield". No verbatim quote was obtainable.
   - This pass found no new test. The PTW thread CFC 655880 has only tjs282's untested "Once you've railed those tiles as well, the food-yield goes up". CIVWIKI `Bautruppaufträge` gives the railroad prerequisites but not the yield.
   - BlackBetsy (CFC "small farms", https://forums.civfanatics.com/threads/small-farms.693526/, C3C, Dec 2024), in passing: a grassland tile "you could irrigate/ railroad that tile to produce 4 food" (2 + 1 irrigation + 1 railroad). Not a test.
   - Recommended: +1 food / +1 shield on improved tiles.
2. **Industrious worker multiplier. RESOLVED (§5.2):** ×1.5 in C3C, ×2 in vanilla and PTW (KayEss's tested article, justanick's formula and CIVWIKI agree). The older readings are kept for the record:
   - Shiplord (CFC 80432): "In VC3/PTW an industrious native worker has a work rate of 4." / "In C3C, that same worker has a work rate of 3."
   - Padma (https://forums.civfanatics.com/threads/industrious-civs.70768/post-1408719): "In Conquests, they toned down the Industrious capabilities to only 150% on Non-Industrious (IIRC)."
   - Anarres: "I thought the bonus was set at 175% for C3C, not 150%?" Frostyboy: "industrious civs work beteween 25% and 33% faster depending on the task." Rounding down to whole points per worker (§5.2a) explains these apparent differences.
3. **Industrious and captured workers. RESOLVED (§5.2a):** per-worker points are rounded down, so an Industrious civ's slave makes 1 point before Replaceable Parts, the same as anyone's slave, and 3 against 2 after it. This explains both "the industrious trait does NOT make slave workers faster" (Jack Merchant) and "My captured workersw always got the double bonus" (bg2soatob).
4. **Mountain worker multiplier. RESOLVED (§5.2a):** ×3, the movement cost (KayEss: "for a mountain 18 BWT (3 x 6)"; CIVWIKI mine 12/24/36). BNut's "4 times as long" is wrong.
5. **Clear Forest base. RESOLVED (§5.1):** 4 in C3C, 10 in vanilla 1.29f and PTW 1.27f (read from all three rule files). KayEss's "Vanilla & PtW are 5" is wrong.
6. **Pollution yield effect.**
   - MANUAL: polluted tile output reduced "to zero".
   - PEDIA `#GCON_Craters`: "Like pollution, ... reduces a square's production, commerce, and food output by one."
   - Apolyton "Bug in food production", https://apolyton.net/forum/miscellaneous/archives/civ3-general-archive/48566-bug-in-food-production (vanilla, Dec 2001), now read. Trachmyr: "but you still get the FOOD from the tile (that is polluted)", and "(It might be the same for shields and trade, but I never checked)". Sevorak: "It affects food, production, AND commerce." Magician: "Polluted tile produce nothing but waste" (untested). The thread is mostly about a railroad food-display bug and settles nothing.
   - C3X-INI says the base game moves citizens off newly polluted tiles to specialists (§4.2). That suggests polluted tiles are a poor choice to work, but it does not give the yield.
   - Recommended: follow PEDIA (−1 to each yield, as for craters) until tested.
7. **Road commerce terrains.** PEDIA, MANUAL and BIQ all give +1 on every land terrain except Volcano. MANUAL-CMPL's terrain chart gives the same: a road bonus of +1 on every land terrain except Volcano. No counter-source was found.
8. **Plant Forest terrains. RESOLVED (§4.1):** Plains, Grassland and Tundra (BIQ, MANUAL, CIVWIKI). The PEDIA line is incomplete.
9. **Volcano yield. RESOLVED (§1.1):** 0/3/0 (BIQ, MANUAL-CMPL chart, CIVWIKI). The PEDIA shield table is wrong.
10. **Volcano damage radius.** Tom2050 (CFC 330741): "within the 8 tiles immediately surrounding the volcano"; "a city is ok if 2 tiles away". Commander Bello and timjack (https://forums.civfanatics.com/threads/volcanoes.73430/latest): "They may damage up to two tiles away." / "random tiles within a two tile radius." scratchthepitch (CFC 402191): "they only rarely lava a tile 2 tiles away." No test or code reading found.
11. **Disease on marsh. RESOLVED (§7):** Marsh causes disease. BIQ flags it, PEDIA `#GCON_Disease` lists it, and CIVWIKI `Krankheit (Civ3)` says "Die einzigen Terrainfelder, die Krankheiten verursachen können, sind Dschungel, Flussauen und Sümpfe." ("the only terrains that can cause disease are jungle, flood plains and marsh"). The EDHELP sentence simply leaves Marsh out.
12. **Ocean yield.** BIQ Ocean is 0/0/0 in all three versions (BIQ, BIQ-PTW, BIQ-V). Against that:
   - MANUAL-CMPL terrain chart: "Ocean 1 – –" (1 food, no shields, no commerce).
   - Sullla: "ocean tiles produce only 1 food."
   - CIVWIKI `Terrain (Civ3)`: Hochsee (Ocean) 1/0/1.
   - PEDIA `#GCON_Food` leaves Ocean out of its table.
   - Three sources against the rule file suggest the engine gives Ocean at least 1 food in play. The commerce value (0 or 1) is open. It needs an in-game check.
13. **Lake yield.** PEDIA `#GCON_Food` says "Fresh Water Lake 2". CIVWIKI gives the lake 2/0/2 and calls it a fresh-water source. README-PTW says lakes are ordinary Coast tiles, and Coast is Food=1 in BIQ. So the lake's +1 food looks like a hard-coded rule for land-locked Coast. The size limit that makes a body of water a "lake" has one code-level hint: C3X-SRC (`injected_code.c`) treats a water body ("continent") of at most 21 tiles as a lake, with the comment "Mimic vanilla behavior so AI doesn't build ports in small lakes" (`threshold_for_body_of_water = 21`), and uses the same 21 in its lake and fresh-water checks. That is the mod copying the stock value, not the stock code. Jaybe (Apolyton 121265 page 2, Dec 2003): in C3C "A "sea" tile doesn't invalidate a "lake" anymore." C3X-THREAD players give nearby numbers: Sutsuj (post-16219245, page-30 #590): "the cutoff is 20 squares."; Ozymandias (post-16219432, #591): "I think the actual number is, "23."" Sutsuj then reported (post-16220959) that a 21-square body of water could build galleys and a 20-square one could not, which fits C3X's 21 (a lake is 20 tiles or fewer). One player's check, but it agrees with the code-derived figure.
14. **Dyes and Incense commerce.** C3C: BIQ gives Dyes +1 and Incense +2; the C3C PEDIA `#GCON_ResourcesL` gives "Dyes +0 +0 +2" and "Incense +0 +0 +1". Vanilla and PTW: both rule files and both Civilopedias give Dyes +1 and Incense +1 (§6.1). So C3C raised one of the two to +2; the rule file says Incense, the PEDIA says Dyes. Use the BIQ (Incense +2).
15. **Radar Tower prerequisite. RESOLVED (§4.1):** Radio in PTW (BIQ-PTW `Required=64` = Radio), Advanced Flight in C3C.
16. **Sedentary barbarians.** EDHELP: "Goody huts only; no barbarian camps". MANUAL: "Barbarians are restricted to their encampments." BigFree (Apolyton, https://codehappy.net/apolyton/threads/83801-1.htm, 2003, untested): "'Sedentary' means that Barbarians can only come out of a hut that is explored (popped)". That supports EDHELP.
17. **Hut barbarians before the first city.** The Oystein condition list (C3C) requires "at least 1 city", and CIVWIKI repeats it. etj4Eagle's Deity sample (CFC 24911, vanilla) reports barbarians about 55% "before the first city". Use the C3C condition for C3C; the vanilla sample may reflect different rules.
18. **Airfield-eligible tiles.** PEDIA `#GCON_Airfields`: "no other improvements, an airfield, a colony, a road or a railroad". PEDIA `#TFRM_Airfield`: "empty or have a road and/or railroad".
19. **Barbarian raid order** (new). TimBentley: production over 10 shields, then a citizen, then gold, then smaller production. CIVWIKI: gold, then production, then a citizen. Both give the gold as treasury ÷ number of cities (§11.2).
20. **Hut unit: how many other civs** (new). Doc Tsiolkovski: the unit must be enabled for the player plus "at least four more Civs". CIVWIKI: the player, the barbarians and "drei weiteren Zivilisationen" (three more).

## UNVERIFIED / open questions

- **Goody hut gold amounts.** Only CIVWIKI gives them (25, or 50 in the Middle Ages). No CFC test or code reading confirms this.
- **Hut odds in vanilla and PTW.** The CIVWIKI table is for C3C. Vanilla samples (CFC 24911, Apolyton 43888) differ, and the 1.17f/1.21f changes were reported only anecdotally.
- **Hut unit status.** How "Conscript" or "skilled" status is set for a hut unit is unknown. So is which of the two barbarian units is given when both qualify.
- ~~**Whether rivers alone connect resources or cities for trade.**~~ *Resolved in the third round (code re-implementation): no.* PEDIA `#GCON_Trade` lists only road, rail, harbour and airport. Flintlock's Trade Net X (https://github.com/maxpetul/C3X/blob/master/Trade%20Net%20X/TradeNetX.cpp), which re-implements the stock 1.22 "Trade Net" road-network fill for speed, adds a tile to the network only if it has a road overlay (`has_road = tile->Overlays & 1`) and is not in the territory of a civ at war with the owner. Rivers play no part. See `02-city-economy.md` §6.
- **Global warming:** the trigger thresholds and the per-turn odds. Only the inputs (city pollution and nukes, worldwide) and the sun icon are sourced. Mad Bomber (Apolyton https://codehappy.net/apolyton/threads/76575-1.htm, Feb 2003): "One tile is affected by GW for every 9 tiles where pollution is present at the end of a turn", which he says is carried over from Civ2, so it is not used. What warming does (play reports, Apolyton https://codehappy.net/apolyton/threads/60246-2.htm and -3.htm, Aug 2002): Skanky Burns, "If global warming happens to a forest square, the forest dies rather than demoting the unerlying terrain."; Tiberius, "Forest to grassland to plains to desert, then increasing size of deserts"; planetfall, the terrain change can be switched off per terrain in the editor ("change global warming effect to none for the terrain type").
- ~~**Disease:** the per-turn chance formula.~~ *Resolved 2026-11-12 by the binary (§7):* `Rand(0x300 - strength*512/100)` against size to fall ill, `Rand(128) > size` to recover, 1/1000 a turn for units fortified in jungle. Still open: the exact comparison direction and order within an ill turn, and which PRTO field spares a unit.
- **Volcano eruption details:** the damage radius (Disputed 10) and what an eruption does to a city. The period (5000 years) is now sourced (§8).
- **Barbarian units by era** in the standard rules (C3X-THREAD: Flintlock's code reading has only the basic-to-advanced switch at four cities per player, §11.2. Ares de Borg and Civinator, post-16753791 and post-16754741, page-130 #2594/#2600, are about era graphics for barbarian units, not unit types; Civinator says the normal game shows era-1 graphics all game): any upgrade beyond Warrior, Horseman and Galley. Vanilla/PTW players say there is none (§11.2, Apolyton 95755); no C3C source.
- **Uprising trigger and size.** INFOC ("second time a civ enters a new age"), CIVWIKI ("when two of the civs enter a new era") and Jaybe's second-hand Soren Johnson quote ("the time the 2nd civ advances to a new age") agree; none is a test. The size (CIVWIKI "10-20 Barbarenreiter"; Apolyton "stacks of about 24" on Raging) depends on the activity level by an unknown rule. A second-hand Industrial-era trigger for unsettled land (Konquest02, §11.2) is unconfirmed.
- **Camp spawn rules.** TimBentley's 2008 limits (at least 2 × players cities before any camp; at most one camp per player) are the only figures. justanick's 2024 "up to 2, 4 or 6 camps" is an explicit guess.
- **The `RULE.Slave` field.** conquests.biq has `Slave=1`, and unit 1 is the Worker, so captured units become Workers. The 50% speed is the "nonnational" factor in the community formula (§5.2a), not the unit's Worker Strength field.
- **Hills irrigation.** PEDIA, BIQ, Sullla and MANUAL-CMPL (Hills irrigation "–") all say hills cannot be irrigated. No counter-source was found. Listed here only because the question is commonly asked. A separate C3C quirk: irrigation can be carried *through* a city built on a hill. Apolyton "Irrigation through Hills with Cities?" (https://apolyton.net/forum/miscellaneous/archives/civ3-strategy-archive/121265-irrigation-through-hills-with-cities, Dec 2003): ToeTruck, "Under PTW, Hills and Mountains blocked irrigation. Period.", but in C3C "the city allowed the water to cross the hill"; Arrian: "You can irrigate through a city built on a hill in Conquests." Catt calls it "most probably a bug". MrWhereItsAt (page 2) says PTW 1.27f allows it too; ToeTruck disputes that.
- **Ocean yield in play** (Disputed 12) and **lake size** (Disputed 13).
- **The worker automation commands' details** (§4.4). How far "nearby" reaches for the clearing orders (our borders, the city radius, or a distance); whether Build Trade ("Road/Rail Net" in the hotkey list) also lays railroad, or joins resources and cities on other landmasses; which way Irrigate to city runs (from the worker toward the city, as implemented, or from the city out), and what it does at a mine or a tile that can't be irrigated; whether "nearest city" is re-chosen as the worker moves; and whether a finished order sleeps the worker (tjs282's "go to sleep") or hands it back for orders, as the engine does.
- **Colony and radar tower sight.** No Civ3 text says a colony or radar tower clears fog of war; PEDIA gives a sight range only for the outpost. For radar towers, WarpStorm (Apolyton "Answers on radar towers", https://apolyton.net/forum/miscellaneous/archives/civ3-play-the-world/86998-answers-on-radar-towers, #14): "The radar towers give visibility 2 tiles out (not including the center square)." For colonies there is only GBiebs/OpenCiv3's reading of Civ3 saves, whose per-tile "seen by structures" bit covers the 3x3 around every colony (on hills and mountains too) and the 5x5 around every radar tower. The engine follows that (`Tile.HoldingSightRange`): colony 1, radar tower 2, on any terrain. Needs an in-game check.
- **Plague mechanics.** Sourced: the scenario settings and their meanings (EDHELP "Permit Plagues (Disasters)": Earliest Start year; Variance "how close in years to the earliest start the plague actually arrives, specified as a percentage of the plague Grace Period"; Duration in years; Strength 0-100; Grace Period in years between plagues; Max Occurrences), the factors (PEDIA `GCON_Plague`: larger, walled and trading cities suffer more, Sanitation "much reduced", Kings spared), and the 1.22 code's outline (Perez's decompile: processed once a round before global warming, "skipped in network mode; countdown with ±50 % jitter"). Conquests' scenarios: Middle Ages has the Black Death (start 1346, variance 50, duration 50, strength 100, grace 100, max 1), Fall of Rome a plague (350, 20, 10, 30, 50, 4); the others have it off. Not sourced: how the variance and jitter combine, the per-city chance, and what a strike costs. Players' reports (CivFanatics 471693) are from memory: it "affects all players at the same time" and hits both population and units in cities. The engine (`Plague.cs`) uses GBiebs/OpenCiv3's guesses for the strike: strength% × (1 + town-plus size + walls + trade link to the capital)/4 a turn per city, halved by Sanitation; a strike costs a citizen and kills each land unit with a quarter of that chance.
