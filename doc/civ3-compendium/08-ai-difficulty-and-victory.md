# 8. AI behaviour, difficulty levels, civ traits, scoring and victory

Scope: Civ3 1.29f, PTW 1.27f and C3C 1.22, standard (non-scenario) games. The special Conquests
game modes (Regicide, Mass Regicide, Elimination, Capture the Flag/Princess, Victory Points) are out of
scope.

Each rule gives a source URL and a short quote from that source. Forum posts are labelled with the
poster's name. "Tested" means the poster says they tested it. "BIQ (repo)" means the value is in
`C7/Lua/civ3/ruleset.json` in this repository. `_Console/RegenerateRuleset/readme.md` says that file's
`difficulties` and `civilizations` sections come from Civ3's `conquests.biq`, and that the only change is
that "the highest is renamed from Sid to Creator". Those values are used here only to back up web
sources, never as the only source. Treat them as secondary because the import code could have bugs.

Local first-hand sources (deep pass of 2026-10-20), cited by these tags:

- **BIQ-C3C**: `<civ3>/Conquests/conquests.biq`, read with the repo's QueryCiv3 through a throwaway reader
  (`<civ3>` = `D:\Steam\steamapps\common\Sid Meier's Civilization III Complete`). This is a direct read of the
  game file, so it outranks "BIQ (repo)".
- **PEDIA-C3C / PEDIA-PTW / PEDIA-V**: `<civ3>/Conquests/Text/Civilopedia.txt`, `<civ3>/civ3PTW/Text/Civilopedia.txt`,
  `<civ3>/Text/Civilopedia.txt`.
- **SCRIPT**: `<civ3>/Conquests/Text/script.txt`. **README-V**: `<civ3>/README.txt` (vanilla patch notes to 1.29f).
  **README-PTW**: `<civ3>/civ3PTW/readme.txt`. **README-C3C**: `<civ3>/Conquests/readme.txt`.
- **MANUAL**: the Civilization III Complete manual (text extract of the Steam PDF,
  https://cdn.akamai.steamstatic.com/steam/apps/3910/manuals/manual.pdf).
- **EDHELP**: the Conquests editor help (`<civ3>/Conquests/Civ3ConquestsEdit.chm`, decompiled pages).
- **C3X-INI / C3X-SRC / C3X-NOTES**: the C3X mod's `default.c3x_config.ini`, `injected_code.c`, `Civ3Conquests.h`
  and `Notes/*.txt` (https://github.com/maxpetul/C3X, read 2026-10-20). The mod is out of scope. Only its
  descriptions of what the unmodded C3C 1.22 executable does are used; they come from reading the executable.
- **FLINTLOCK-WAR**: Flintlock, "All About AI Aggression Levels and War Plotting", 27 Jun 2026,
  https://forums.civfanatics.com/threads/all-about-ai-aggression-levels-and-war-plotting.703785/ . Flintlock is
  the C3X author; he says he converted "the original logic" to pseudocode (post #8: "In the original logic it's
  more clear. There's a switch statement over the AI aggression setting"). This is code reading of stock C3C, not testing.

**C3X-THREAD** is https://forums.civfanatics.com/threads/c3x-exe-mod-including-bug-fixes-stack-bombard-and-much-more.666881/ (the C3X mod's forum thread, 2021-2026). Posts are cited as "post-NNNNNNNN" (thread
URL plus `post-NNNNNNNN`) or "page-N #post-number". Flintlock's statements about "the base game" are code readings of
the 1.22 executable; other posters report play, sometimes in modded games. Only statements about the unmodded game are
used. Quotes were re-checked against the posts; the few that could not be are marked "(paraphrase from a
summarised fetch)".

### Owner rulings for the engine (recorded, not argued)

The project owner has ruled on these points. The sources that bear on each are listed so the ruling can be
traced.

- **AI cities keep a free tile between them (no overlapping first rings).** The stock minimum separation for
  every player is one empty tile. C3X-INI: "if it's set to 1, cities cannot be founded adjacent to one another,
  there must be at least one open tile separating them. A setting of 1 there corresponds to the standard game
  rules." Keeping first rings apart as well is the owner's rule for the AI. SJ Frank (3.3 below): "The AI will
  never settle a city that has first-ring conflict."
- **The AI keeps the luxury slider** (a trial showed it stronger). The stock sources describe an AI without it:
  alexman's PTW debug test (3.6), "I think this is the best the AI can do without using the luxury slider", and
  Ision, the AI "will NOT use the luxury slider".
- **The AI may change government in long wars.** chiefpaco (21298): after a long war it "switches to Comm or
  Monarchy" (3.6).
- **The AI uses Regent's optimal-city percentage.** alexman (Apolyton, 31 Jul 2002), https://codehappy.net/apolyton/threads/56739-1.htm:
  "The AI gets the "AI default difficulty level" percentage of OCN. If you haven't changed that, it's Regent."
  BIQ-C3C RULE `DefaultDifficultyLevel=2` (Regent). See 1.2 for the extra AI-only OCN term in the code.
- **The AI researches by alexman's tech values.** Stock C3C does not use tech flavors (3.6), so the AI's tech
  choice comes from its built-in valuation (C3X-SRC hooks a stock `Leader_ai_eval_technology`).

---

## 1. Difficulty levels (BIQ DIFF section)

### 1.1 Table (C3C 1.22 defaults)

| DIFF field | Chieftain | Warlord | Regent | Monarch | Emperor | Demigod | Deity | Sid |
|---|---|---|---|---|---|---|---|---|
| Citizens born content (human) | 4 | 3 | 2 | 2 | 1 | 1 | 1 | 1 |
| Max AI government transition time (turns) | - | - | - | 4 | 3 | 2 | 2 | 1 |
| AI extra defensive units at start | 0 | 0 | 0 | 2 | 4 | 6 | 8 | 12 |
| AI extra offensive units at start | 0 | 0 | 0 | 1 | 2 | 3 | 4 | 6 |
| AI extra start unit 1 (Settler) | 0 | 0 | 0 | 0 | 0 | 1 | 1 | 2 |
| AI extra start unit 2 (Worker) | 0 | 0 | 0 | 0 | 1 | 2 | 2 | 4 |
| AI additional free unit support (per civ) | 0 | 0 | 0 | 4 | 8 | 12 | 16 | 24 |
| AI unit support bonus per city | 0 | 0 | 0 | 1 | 2 | 3 | 4 | 8 |
| Attack bonus vs barbarians (%) (human) | 800 | 400 | 200 | 100 | 50 | 25 | 0 | 0 |
| AI cost factor (human = 10) | 20 | 12 | 10 | 9 | 8 | 7 | 6 | 4 |
| % of optimal cities (human OCN) | 100 | 95 | 90 | 85 | 80 | 70 | 60 | 50 |
| AI-to-AI trade rate | 110 | 120 | 130 | 140 | 150 | 160 | 170 | 200 |
| Citizens quelled by military law | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| Corruption modifier (%) | 100 | 100 | 100 | 100 | 100 | 100 | 100 | 100 |
| Score multiplier | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |

Sources for the table:

- **Vanilla/PTW six-level table (Pyrkaige, 25 Nov 2002).** https://forums.civfanatics.com/threads/ai-difficulty-level-bonuses.37490/post-614347
  gives Chieftain..Deity. It agrees with the table above except Deity, which it gives as OCN 70% and
  AI-AI trade 160 (the PTW values). Rows quoted: "AI cost factor (player is 10) | 20 | 12 | 10 | 9 | 8 | 6";
  "Optimal-city % before corruption | 100 | 95 | 90 | 85 | 80 | 70".
- **PTW to C3C Deity change (Pyrkaige, 13 Nov 2003).** https://forums.civfanatics.com/threads/difficulty-levels-ptw-vs-conquests.68642
  reports "In PTW, the Deity AI to AI trade rate is at 160, while in C3C it's at 170." He gives the OCN
  as 70% in PTW and 60% in C3C. He also corrects the editor help: a Sid AI starts with 2 settlers and 4 workers.
- **Same change (TimBentley).** https://forums.civfanatics.com/threads/civ3-conquests-additions-changes-list.104294/
  says "The AI-AI trade ratio for diety increased from 160 in PTW to 170 in C3C, and the OCN decreased from 70% to 60%."
- **Demigod and Sid (Arathorn), same thread.** "Demi-God AIs have a cost factor of .7"; "3 starting
  offensive units, 6 starting defensive units, 1 bonus settler, 2 bonus workers"; "Sid AIs have a cost
  factor of .4, 6 starting offensive units, 12 starting defensive units"; "human has 0% bonus vs. barbs
  and human has 50% of the normal OCN".
- **Sid (SirPleb, 14 Nov 2003).** https://forums.civfanatics.com/threads/some-comments-after-playing-a-bit.68813
  says "The big one - the AI's 'cost factor' is 4!" and "This means that the AIs need only 4/10th as much
  of anything as the human needs."
- **CFC FAQ.** https://civfanatics.com/civ3/faq/ lists the barbarian bonus as 800/400/200/100/50/25/0/0 and
  the AI anarchy cap as "4 turns on Monarch, 3 on Emperor, and 2 on Demigod". It also says "At Regent, the
  AI and you don't have any advantages nor disadvantages."
- **Score multiplier.** See section 6.
- **BIQ (repo).** `difficulties` in `C7/Lua/civ3/ruleset.json` has every value in the table above.
  Field names: `numberOfCitizensBornContent`, `maxAiGovernmentTransitionTime`,
  `numberOfAIDefensiveStartingUnits`, `numberOfAIOffensiveStartingUnits`, `extraStartUnit1/2`,
  `additionalFreeUnitSupport`, `unitSupportBonusForEachSettlement`, `attackBonusAgainstBarbarians`,
  `aiCostFactor`, `percentageOfOptimalCities`, `aiToAITradeRate`, `militaryLaw`.
- **BIQ-C3C (direct read).** DIFF gives content 4/3/2/2/1/1/1/1, cost factor 20/12/10/9/8/7/6/4, OCN %
  100/95/90/85/80/70/60/50, AI-AI trade 110/120/130/140/150/160/170/200 and corruption % 100 at every level.
  This confirms the table, including the C3C Deity values.
- **Corruption modifier.** EDHELP "Corruption Modifier (Difficulty Level)": "Determines the percentage of normal
  corruption experienced on the selected difficulty level. The default is 100% on all levels". It "has no effect on
  players whose government experiences catastrophic corruption, cities in civil disorder, or capital cities".
  README-V (1.21f): "Editor: Added a corruption slider to difficulty level page. It defaults to 100% for all
  difficulty levels."
- **Extra start units 1 and 2 are Settler and Worker.** BIQ-C3C RULE `StartUnitType1=0`, `StartUnitType2=1`,
  and PRTO 0 is Settler and PRTO 1 is Worker. So Sid's "2" and "4" are 2 settlers and 4 workers, as Pyrkaige
  (68642) said. EDHELP has it the wrong way round: "type 2 start units ... corresponds to the default start unit 2
  on the general settings page (normally a settler)".
- **Optimal city number by world size (C3C).** BIQ-C3C WSIZ: Tiny 14, Small 17, Standard 20, Large 28, Huge 36
  (tech rate 160/200/240/320/400; distance between civs 11/11/12/18/24; civs 4/6/8/12/16; maps 60x60 to 160x160).
  This settles the former UNVERIFIED item. The vanilla values were 12/14/16/24/32 (Pyrkaige 37490;
  `02-city-economy.md` reads the same from the vanilla and PTW rule files).

### 1.2 What the fields mean

- **AI cost factor.** It scales the AI's cost to build and research (units, improvements, wonders and
  techs), as a fraction of the human's 10. It does not change combat or movement.
  - https://forums.civfanatics.com/threads/ai-difficulty-level-bonuses.37490/post-614347 (thread summary):
    the cost factor applies to wonders, improvements and units, but not to combat or movement rates.
  - https://civfanatics.com/civ3/faq/ lists the bonuses above Regent as "extra starting units, faster
    production and research, and lower corruption".
  - **It also scales AI growth (Firaxis editor text).** EDHELP "Cost Factor (AI Bonuses)": "Determines the cost
    factor that is applied to AI growth, shields, and research. The player's cost factor is always 10."
    C3X-SRC names two stock functions that take an `ignore_difficulty` flag, `Leader_get_unit_cost` and
    `Leader_get_food_cost_factor`, which fits a difficulty factor on both shield and food costs.
- **Research penalty after patch 1.29f.** For research, the patched game makes the human's techs cost
  more instead of making the AI's cost less. Tested by theNiceOne, 19 Jan 2003, patch 1.29:
  https://apolyton.net/forum/miscellaneous/archives/civ3-general-archive/86979-ai-human-trade-revisited
  says "the human player's research cost is divided by the AI's cost factor (as found in the editor)".
  This only applies when the factor is below 1, so from Monarch up. Bamspeedy, in the 37490 thread
  above, also reports that later patches made the human, not the AI, the one adjusted by difficulty.
- **AI-to-AI trade rate.** It sets how much AIs pay each other in tech trades (and maybe other trades).
  theNiceOne, same Apolyton thread: "the AI-AI trade bonus that can be found in the editor does _not_
  affect AI-human trade in any way." Nor Me: "I cannot get reasonable results out of an AI-AI trade test."
  - **Definition (Firaxis editor text).** EDHELP "AI to AI trade rate (AI Bonuses)": the value "is multiplied by
    the total value (in gold) of the initiating civ's offerings, which is used by the "other" AI civ to determine
    whether a deal is acceptable or not. For example, with a rate of 120, an AI civ offering 100 gold to another AI
    civ for something would actually be worth 120 gold to the other AI civ. This rate directly affects the
    frequency and aggressiveness with which AI civs trade amongst themselves." The underlying item valuation is not
    documented.
  - **How often AIs negotiate with each other (code).** C3X-SRC, comment on its hook: "The intercepted call is
    the base game's 1-in-4 gate before ai_negotiate_with_other_ai." So each eligible AI pair gets a 1-in-4 roll
    to negotiate.
  - **AIs do not demand tribute from each other.** C3X-INI offers AI-to-AI demands as a new feature, using "the
    same demand selection, acceptance, diplomatic penalty, and possible war logic that the base game uses for
    demands involving a human player". FLINTLOCK-WAR: "As far as I know AIs won't demand things from each other."
- **% of optimal cities.** This is a percentage applied to the **human's** optimal city number, the
  number of cities before corruption from city count rises. Arathorn (104294): "human has 50% of the normal OCN" at Sid.
- **Each player has its own difficulty level; for the AI it is the default level (Regent).**
  - The executable stores a difficulty per player. C3X-SRC `Civ3Conquests.h`, `struct Leader`: `int
    player_difficulty;`. C3X-NOTES `ocn_formula.txt` says the OCN percentage "is scaling for player's set
    difficulty ... The level used here is per-player NOT the overall game difficulty."
  - alexman (Apolyton 56739, 31 Jul 2002, vanilla): "The AI gets the "AI default difficulty level" percentage
    of OCN. If you haven't changed that, it's Regent." player1, same thread: "AI always use Regent vlalue, so he'll
    have 90% of original optimal."
  - BIQ-C3C RULE `DefaultDifficultyLevel=2`, which is Regent (0 = Chieftain). EDHELP "Default Difficulty
    Level" describes the same field from the menu side: "Determines which difficulty level ... the game defaults
    to. Default is Regent."
  - This is the field Bamspeedy (37490) meant by the "default AI intelligence" setting, and it gives TheNiceOne's
    Regent answer (37490) unless a mod changes it. Pyrkaige's Monarch (68642) is the outlier.
  - **The same holds in Conquests (Firaxis).** In an Apolyton thread on the C3C 1.0 difficulty table
    (https://codehappy.net/apolyton/threads/101437-1.htm, 13 Nov 2003), Pyrkaige again said the AI uses Monarch
    values; alexman ("Firaxis Games Software Engineer") answered that the AI plays at Regent: "Yes, it does, unless
    you change the "AI Default Difficulty Level" in the General Settings tab." Harovan, same thread, on the start
    units: "1 is typically a settler and 2 a worker, and not vice versa."
  - **Content citizens: the AI's are Regent's (Firaxis).** Soren Johnson, answering "What does artificial
    intelligence default difficulty level mean?" (Apolyton, https://codehappy.net/apolyton/threads/52411-1.htm,
    8 Jun 2002, vanilla): "It determines the "difficulty level" for the AI civs." He gives the content citizens
    as the example ("on regent, cities start with 2 content citizens") and concludes: "Thus, for the standard
    bic file, the AI always plays at Regent, regardless of whether the human plays at Chieftain through Deity."
    So AI cities have 2 content citizens at every game difficulty in the standard rules. TheNiceOne (37490,
    25 Nov 2002) says the same from play: "on emperor/deity you have only your first citizen born content,
    while the AI still has two".
  - Military law and the barbarian bonus are per-difficulty DIFF fields too. Soren's "the AI always plays at
    Regent" covers them in general terms, but he names only the content citizens. TheNiceOne (37490): "the AI
    has still a 200% bonus against barbarians even though the human player loses its entire bonus on deity."
    Military law is 1 at every level in the stock DIFF table, so it cannot differ.
- **The AI gets extra optimal cities on Monarch and above (code).** C3X-NOTES `ocn_formula.txt`
  (https://github.com/maxpetul/C3X/blob/master/Notes/ocn_formula.txt) gives the stock OCN as
  `ocn = max (1, floor(s_diff * (b + n_fp + n_com + n_gov + n_ai) / 100))`, with "n_ai is special contrib for AI
  players: n_ai = 0 if human player else | 0 if game difficulty set <= 2 | floor(b/8) if set to 3 | floor(b/4) if
  set to 4 | floor(b/2) if set >= 5". So with the game difficulty at Monarch, Emperor, or Demigod and above, an AI's
  base OCN rises by 1/8, 1/4 or 1/2 of the map's base number before its own (Regent) percentage is applied. On a
  Standard map (b = 20) at Regent's 90%: Monarch 19 cities, Emperor 22, Demigod/Deity/Sid 27, against 18 at
  Regent and below (worked from the formula, not tested).
  The same note gives the other terms: Commercial "floor(b/4)"; Forbidden Palace-type wonders "floor((3*f*b) / d)",
  d = 8, or 1 under communal corruption; government "floor(b/8) if L's govt has minimal or nuisance corruption |
  floor(b/16) if problematic | 2*b if communal". This is the "lower corruption" the CFC FAQ lists as an AI bonus.
  Corruption itself belongs to `02-city-economy.md`.
- **Starting bonus units are the best available attacker and defender.** EDHELP "Number of Defensive Land Units
  (Additional AI Starting Units)": "the AI will determine which available unit (e.g. units that can currently be
  built) is its best defender." The offensive entry says the same with "best attacker". This settles the former
  UNVERIFIED item.
- **Max AI government transition time** caps the AI's anarchy. CFC FAQ: "on Emperor, the cost factor is
  .8, and the AI gets a reduced anarchy time!"

### 1.3 Other AI advantages ("cheats") beyond DIFF

- **Map knowledge.** The AI knows the map, and where resources will appear, before it should. These are
  community findings and are not confirmed by Firaxis.
  - TheNiceOne, 17 Jul 2002: https://forums.civfanatics.com/threads/the-complete-cheat-guide.27273/post-376681
    lists as proven "The AI knows the map even before exploring." He says he "tested by making a simple
    map" and that the AI "went for future resources spots".
  - Bamspeedy, same thread: "If the AI has seen the map, it does in fact know if a future resource will be there or not."
  - TheNiceOne, https://forums.civfanatics.com/threads/the-ai-dont-cheat.33788/post-530614: "They know where your units are and a few other things."
  - Harovan (Apolyton staff), https://codehappy.net/apolyton/threads/84970-1.htm (4 May 2003): "The only known
    and confirmed true cheat is, that the AI knows the entire map, including all resources and the location of all
    units." dexters, same thread, from debug-mode games: the AI knows where resources are but cannot see them
    without the tech (he saw AI cities next to unlinked oil). Jaybe: it knows where they are, "though not WHAT they are."
  - Soren Johnson, chat transcript quoted by zapperio, https://codehappy.net/apolyton/threads/34826-1.htm
    (20 Nov 2001): "the AI does not interface with the map like the player does." and "It will always be somewhat grey".
  - The war-target code reads the target's unit counts directly. FLINTLOCK-WAR: "Calculate LandUnitsPerCity as
    (number of units the target has with the offensive or defensive AI strategy // target's city count)". This
    needs no visibility of those units.
- **Code reading: fog of war does not apply to the AI, invisibility does.** Flintlock (C3X-THREAD post-16941749,
  page-179 #3580): "In general, the fog of war doesn't apply to the AI and unit invisibility does." Earlier
  (post-16582080, page-94 #1874): "There's no single point in the code where it's determined whether or not
  AIs can see through the fog of war". On recon (post-16393188, page-63 #1247): "As far as I know the AI
  never does recon missions." / "Where a human player would do a recon mission the AI cheats instead."
  This confirms Harovan's "knows the entire map" from the code, with one limit: hidden units (submarines and the like)
  stay hidden from the AI.
- **No combat, border or wonder cheats (tested).**
  - TheNiceOne (27273): combat "outcomes are as they should be" over many battles; "there's no AI wonder production cheats".
  - TheNiceOne (33788): on border rules, "the AI follows exactly the same rule as the human player."
- **AI galleys (Firaxis, second-hand).** TheNiceOne (27273) reports that Soren Johnson said "an AI galley
  will never end a turn where it could sink."
- **Tech trading.** On higher levels the AI charges the human more. theNiceOne (86979) gives the example
  "1000 / 0.6 * 1.15 = 1917 gold" on Deity.
- **Mid-turn AI trading (vanilla 1.16f).** In 1.16f AIs could trade with each other during the human's turn
  (solo, https://codehappy.net/apolyton/threads/39845-1.htm, Jan 2002, with a save). README-V 1.17f removed it:
  "The AI no longer trades during the players turn."
- **Difficulty works mainly through the cost factors** (community). tjs282 (C3X-THREAD post-16843587, page-144 #2879):
  (paraphrase from a summarised fetch) "the improvements in AI performance at higher levels are mainly down to the lower cost-factors" and the
  preferential AI-AI trade rates. justanick (post-16306886, page-43 #841): "At demigod AI gets additional 3
  free unit support per settlement and 12 additional free unit support outright." That matches the BIQ table
  (`05-governments.md` 5.3).
- **Difficulty raises the AI's war aggression (code).** The effective aggression level used by the war and
  diplomacy code adds a difficulty term. FLINTLOCK-WAR: `level = civ.AggressionLevel + GameDifficulty // 2 -
  DifficultyLevelCount // 6`, clamped to -2..2 (`GameDifficulty = 0 for Chieftain`). With C3C's 8 levels the term
  is -1 at Chieftain and Warlord, 0 at Regent and Monarch, +1 at Emperor and Demigod, and +2 at Deity and Sid
  (worked from the formula). justanick (same thread, #13): "The direct effect of aggression setting and difficulty
  setting is small." See 3.4.

---

## 2. Civilization traits (Conquests)

Each civ has two traits, and each trait gives one free starting tech. CFC FAQ
https://civfanatics.com/civ3/faq/: "Each civilization has two traits, and each trait grants one starting technology."
The trait bits are in BIQ RACE `Flags[0]` bits 0-7, in the order Militaristic, Commercial, Expansionist,
Scientific, Religious, Industrious, Agricultural, Seafaring (`QueryCiv3/BiqSections/Race.cs`). The BIQ
RULE value `buildingDiscountForCivTraits` is 0.5 (repo ruleset.json `rules`).

| Trait | Effect in C3C | Free tech | Source / quote |
|---|---|---|---|
| Militaristic | Promotions are twice as likely. Barracks, Walls, Harbor and Coastal Fortress cost half. | Warrior Code (or The Wheel) | CFC FAQ: "Unit promotions to higher levels are twice as likely as non-MIL civilizations."; "Militaristic buildings like Barracks, Walls, Harbors, etc. are all half cost." Sullla https://sullla.com/Civ3/strat3b.html: "double the chance to promote every time one of their units wins a battle" |
| Commercial | OCN +25% (exactly floor(base/4) added to the base OCN; +10% before 1.29f), which means less corruption. Extra commerce on the city tile at size 7+ and again at 13+ (amount: see `02-city-economy.md`). | Alphabet | C3X-NOTES `ocn_formula.txt`: "n_com = floor(b/4) if L has the trait else 0". alexman, https://codehappy.net/apolyton/threads/76255-1.htm (6 Feb 2003): "Firaxis has told us that the Commercial trait increases the OCN by 25% (it was 10% prior to 1.29f)". PEDIA-C3C `#GCON_Strengths`: "The center city squares of all cities and metros produce extra commerce, and less corruption is experienced." CFC FAQ: "The OCN (Optimal City Number) is increased 25% for Commercial civilizations." The FAQ also says city centres get an extra commerce on becoming a City and again at Metropolis. Sullla (pre-C3C): "an extra gold piece for each city (size 7-12) and 2 extra gold for each metropolis (size 13+)". |
| Expansionist | Starts with a Scout and is the only trait that can build Scouts (BIQ-C3C RULE `Scout` = PRTO 2 "Scout"). Never gets barbarians from goody huts, and gets better hut results. | Pottery | PEDIA-C3C `#GCON_Strengths`: "The civilization starts the game with a scout and can build more later, and passive minor barbarians are friendlier." README-PTW (1.21f): "Expansionist civs can no longer build Scouts when civ-specific abilities are disabled." CFC FAQ: "Expansionist civilizations do not get barbarians from goody huts". Sullla: "they cannot get barbarians from goody huts." |
| Industrious | Workers are +50% faster in C3C (+100% in vanilla/PTW), tested (`07-map-terrain-and-workers.md` 5.2, KayEss). Extra shield on the city tile at larger sizes. | Masonry | PEDIA-C3C `#GCON_Strengths`: "Workers complete tasks faster and the center city square of all cities produces extra shields in cities and metros." CFC FAQ: Industrious workers build "100% faster in Vanilla and Play the World, and 50% faster in Conquests." Artifex: https://forums.civfanatics.com/threads/updated-trait-rankings-for-c3c-emperor.75226/ "+50% instead of +100%", no bonus for slaves. |
| Religious | Temples and Cathedrals cost half. Anarchy lasts 2 turns in C3C (1 turn in vanilla/PTW). | Ceremonial Burial | CFC FAQ: "Religious buildings (Temples, Cathedrals) are all half price." The FAQ gives anarchy as 1 turn in Vanilla and PTW and 2 turns in Conquests. Artifex (75226): "2 turns instead of 1 turn". |
| Scientific | Library, University and Research Lab cost half. Free tech on entering each new era. Scientific Great Leaders appear in C3C. | Bronze Working | CFC FAQ: "Scientific buildings (Libraries, Universities, Research Labs) are half price." The FAQ adds a free technology on entering a new age. Sullla: "free tech at the start of each era". |
| Agricultural (C3C only) | +1 food on the city tile. Under Despotism the -1 penalty on tiles making more than 2 cancels it, unless the city has fresh water (river or lake), so the bonus survives Despotism only with fresh water. Irrigated desert gives 2 food instead of 1. Aqueduct, Recycling Center and Solar Plant are cheaper (BIQ-C3C BLDG Agricultural flag on exactly these three). | Pottery (Masonry if also Expansionist) | PEDIA-C3C `#GCON_Strengths`: "Agricultural city improvements (aqueduct, for instance) are easier to build, and the center city square and irrigated deserts produce one more food." C3X-INI: "Under the standard game rules, the Despotism tile penalty does not apply to food yield from city tiles of Agricultural civs with fresh water." Arathorn (104294): "Provides one bonus food in the city square in all governments except despotism." TimBentley, same thread: "When in despotism, agricultural only adds 1 food to cities if they are on a river." LouLong https://forums.civfanatics.com/threads/conquests-features-comprehensive-survey.61791: "Cheaper aqueducts, recycling plants and solar plants." |
| Seafaring (C3C only) | +1 commerce on the city tile of every coastal city (not only the capital). Ships +1 movement and a lower chance of sinking. Coastal start much more likely. Cheaper Harbor, Commercial Dock, Offshore Platform and Coastal Fortress (BIQ-C3C BLDG Seafaring flag on these four). | Alphabet (Pottery if also Commercial) | PEDIA-C3C `#GCON_Strengths`: "Each city built along the coast receives a commerce bonus in the center city square, and Seafaring city improvements (such as the Harbor or Commercial Dock) are easier to build.  Ships move faster and are less likely to sink in the sea or ocean." Arathorn (104294): "Cities on the coast of salt water gain one extra commerce in the city square." Dominae, AU501 info thread https://codehappy.net/apolyton/threads/106962-1.htm (21 Jan 2004): cities founded next to a coast tile get "+1 Commerce per turn bonus in the base city tile"; "All naval vessels have a +1 Movement bonus". LouLong (61791): "Ships have movement increased by 1"; "Decreased chance of sinking"; "Coastal start much more likely." README-C3C (v1.20): "Greatly improved the ability of Seafaring civs to start near the coast. It should happen most of the time now." Sinking odds: see `01-combat-and-units.md` (50% vs 75% survival). |

More trait rules:

- **Which ordinary buildings carry each trait flag (BIQ-C3C BLDG, excluding wonders).** These flags are what
  "half cost for trait buildings" (RULE `buildingDiscountForCivTraits` 0.5) reads.
  - Militaristic: Barracks, Walls, Coastal Fortress, Harbor, Airport, SAM Missile Battery, Civil Defense.
  - Religious: Temple, Cathedral. Scientific: Library, University, Research Lab.
  - Agricultural: Aqueduct, Recycling Center, Solar Plant.
  - Seafaring: Harbor, Commercial Dock, Offshore Platform, Coastal Fortress.
  - Commercial: Marketplace, Bank, Stock Exchange, Harbor, Airport, Commercial Dock. No source says Commercial
    civs get these cheaper, and PEDIA-C3C's Commercial entry lists no building discount (see UNVERIFIED).
  - Expansionist and Industrious: no ordinary building.
  - On wonders and small wonders the same flags mean the golden-age trigger instead. EDHELP "Other
    Characteristics (Improvement Properties)": "Any building civilization with "Agricultural" bonus may trigger a
    peaceful golden age" (and the same for each trait).

- **Military Great Leaders.** In C3C they can no longer rush Great Wonders, which affects Militaristic
  civs. stachnie (75226): "MGL's can't rush Great Wonders now".
- **C3C trait reassignments.** Arathorn (104294) lists them, for example "Aztecs - from Militaristic,
  Religious to Agricultural, Militaristic" and "England - from Commercial, Expansionist to Commercial, Seafaring".
- **Golden ages** come from owning Great Wonders that match the civ's traits, or from the first UU
  victory. That rule belongs in the golden-age section of this compendium and is not re-sourced here.

### 2.1 Per-civ AI personality (BIQ RACE, C3C defaults; BIQ (repo) values)

Aggression runs from -2 to +2 (`Race.cs`: `AggressionLevel; // -2 to 2`). Longasc's C3C list, in Ision's
article https://forums.civfanatics.com/threads/your-ai-opponent-by-ision.92917, uses a 1-5 scale. It puts
Germany, Zulu and Mongols at 5 and France and India at 1, which matches +2 and -2 below.

| Civ | Traits | Aggr. | Favourite gov | Shunned gov |
|---|---|---|---|---|
| Rome | Mil, Com | 1 | Republic | Communism |
| Egypt | Rel, Ind | 0 | Despotism | Republic |
| Greece | Com, Sci | 0 | Democracy | Despotism |
| Babylon | Sci, Rel | 0 | Monarchy | Despotism |
| Germany | Mil, Sci | 2 | Republic | Communism |
| Russia | Exp, Sci | 1 | Communism | Democracy |
| China | Mil, Ind | -1 | Communism | Feudalism |
| America | Exp, Ind | 0 | Democracy | Communism |
| Japan | Mil, Rel | 1 | Feudalism | Republic |
| France | Com, Ind | -2 | Republic | Fascism |
| India | Com, Rel | -2 | Democracy | Despotism |
| Persia | Sci, Ind | 1 | Monarchy | Republic |
| Aztecs | Mil, Agr | 1 | Despotism | Monarchy |
| Zululand | Mil, Exp | 2 | Despotism | Republic |
| Iroquois | Com, Agr | -1 | Communism | Monarchy |
| England | Com, Sea | 0 | Democracy | Fascism |
| Mongols | Mil, Exp | 2 | Despotism | Democracy |
| Spain | Rel, Sea | 0 | Monarchy | Despotism |
| Scandinavia | Mil, Sea | 1 | Monarchy | Republic |
| Ottomans | Sci, Ind | 0 | Republic | Despotism |
| Celts | Rel, Agr | 1 | Despotism | Feudalism |
| Arabia | Exp, Rel | 1 | Republic | Despotism |
| Carthage | Ind, Sea | -1 | Republic | Communism |
| Korea | Com, Sci | -1 | Monarchy | Republic |
| Sumeria | Sci, Agr | -1 | Monarchy | Republic |
| Hittites | Com, Exp | 1 | Fascism | Republic |
| Netherlands | Agr, Sea | 0 | Monarchy | Despotism |
| Portugal | Exp, Sea | 0 | Democracy | Fascism |
| Byzantines | Sci, Sea | 0 | Republic | Feudalism |
| Inca | Exp, Agr | 0 | Communism | Republic |
| Maya | Ind, Agr | 0 | Feudalism | Democracy |

The C3C setup screen also has an AI aggression setting that applies to all AIs. LouLong (61791):
"Random World Size & Adjustable AI aggression levels."

- **What the setting does (code).** It has five positions, 0 (least) to 4 (most). FLINTLOCK-WAR:
  "if AIAggression == 0: level = -2 / elif AIAggression == 1: level -= 1 / elif AIAggression == 2: pass # do
  nothing / elif AIAggression == 3: level += 1 / elif AIAggression == 4: level = 2", applied after the
  difficulty term (1.3) and clamped to -2..2. Flintlock (#8): "setting the overall AI aggression level to most or
  least overrides all the per-civ aggression settings." MANUAL p.33: "Normal means that the AI acts based on the
  tribe's default aggressiveness setting."
- **What the effective aggression level feeds (code, FLINTLOCK-WAR).** AI attitude ("When computing an AI's
  attitude toward other players, the game begins with the AI's aggression level calculated as above"), the "Chance
  to demand things from a human player", the "Chance to declare war on someone who's refused a demand or nuked a
  third party", trade-offer valuation ("Not sure if this applies to all offers"), the unit AI ("I haven't looked
  into the details"), and the war-plotting roll (3.4). He adds that the list "is not necessarily exhaustive".
- **Multiplayer.** Fergei (FLINTLOCK-WAR thread, #9): "you can't toggle aggression levels for the game in
  multiplayer". README-PTW (1.14f) fixed a "bug that caused MP difficulty level to always be Regent".

**Build-often flags of each civ (BIQ-C3C RACE, C3C defaults).** No civ has any Build-Never flag set, and every
civ has Manage Citizens and Manage Production on and none of Emphasize Food/Shields/Trade, No Wonders or No Small
Wonders. Barbarians have no flags.

| Civ | Build often |
|---|---|
| Rome | Offensive, Defensive, Growth, Production |
| Egypt | Growth, Production, Culture |
| Greece | Naval, Science, Trade, Culture |
| Babylon | Defensive, Growth, Science |
| Germany | Offensive, Science, Culture |
| Russia | Air, Growth, Science |
| China | Growth, Production, Science |
| America | Air, Growth, Production, Culture |
| Japan | Offensive, Naval, Happiness |
| France | Happiness, Trade, Culture |
| India | Growth, Wealth, Trade, Culture |
| Persia | Offensive, Wealth, Trade |
| Aztecs | Offensive, Growth, Explore |
| Zululand | Offensive |
| Iroquois | Growth, Production, Trade |
| England | Naval, Wealth, Trade, Culture |
| Mongols | Offensive, Production |
| Spain | Naval, Trade, Explore, Culture |
| Scandinavia | Offensive, Naval |
| Ottomans | Artillery, Happiness |
| Celts | Offensive, Production, Culture |
| Arabia | Growth, Culture |
| Carthage | Naval, Growth |
| Korea | Artillery, Wealth, Trade |
| Sumeria | Science, Culture |
| Hittites | Offensive, Trade |
| Netherlands | Defensive, Naval, Production |
| Portugal | Naval, Wealth, Trade, Explore |
| Byzantines | Defensive, Naval, Happiness, Trade, Culture |
| Inca | Offensive, Workers, Wealth, Culture |
| Maya | Defensive, Workers, Production, Culture |

No civ has Settlers often. Byzantines are the only civ with five flags, above Ozymandias's tested 3-4 limit (3.2).
alexman (vanilla, https://codehappy.net/apolyton/threads/56961-1.htm, 25 Jul 2002) made the same check on the
vanilla .bic: "none of them except the Iroquois have the Explore Units in their build-often list", and the
build-never list is empty in the standard setup.

---

## 3. How the AI behaves

### 3.1 Architecture and production choice (Firaxis)

- **Leader AI and unit AI.** A leader AI handles the high-level picture and unit AIs ask it questions.
  Soren Johnson, interview of 26 Oct 2001:
  https://apolyton.net/forum/apolyton-civilization-site-general/civilization-lost-history/255541-soren-johnson-civilization-iii-interview-2-26-oct-2001
  says "A leader AI keeps track of high-level concepts, like whether enough science is being generated".
  The same interview, as quoted in
  https://apolyton.net/forum/miscellaneous/archives/civ3-general-archive/41443-interview-soren-johnson-on-ai?p=926594,
  says "the unit AI controlling a tank might ask the leader AI which enemy city would be best to attack."
- **Production.** The city picks whatever item scores highest. Soren (255541): "the city simply builds the item with the highest value."
- **Personality.** Leaders are modelled on history. Soren (255541): "an aggressive ruler preferring
  despotism and offensive ground units".
- **How the production score works (code readings, C3X-THREAD, Flintlock).**
  - "The AI chooses to build whatever has the highest point value." (post-16158745, page-21 #409). "it
    considers improvements and units separately but it considers all kinds of improvements, wonders or not, together."
    (post-16149215, page-19 #367).
  - Randomness: randomness "works by adding a random number of points to the AI's valuations of its options." / "IIRC, the random number is chosen
    between -5 and +5." (post-16360495, page-56 #1101).
  - A unit type it has enough of scores zero: "the AI assigns a value of 0 to catapults when it doesn't want
    any more" (post-16157851, page-21 #406); the same holds for workers (3.2). His view that "the AI likes
    happiness regardless of whether or not it needs any more" (post-16150396, page-20 #386) is, by his own account, a
    guess.
  - An undefended city skips the main loop: "It runs a special bit of logic to pick a defender to build and
    skips over the main loop", and (paraphrase from a summarised fetch) "the function with the main loop is 3000 lines alone" (post-16760037,
    page-131 #2614). There are "(annoyingly) multiple production chooser methods even for the regular civ
    AI" (post-16859226, page-150 #2996).
  - It ignores resources that buildings would generate: "The AI doesn't account for generated resources at
    all when deciding what to build." (post-16902815, page-166 #3314).
  - Civs differ little: "the AI is almost identical for all civs." (post-16095472, page-12 #228).
  - Wartime (community): Predator145 (post-16288091, page-39 #764): "During war time, military units get much
    higher build preference than peace time."
- **Naval and air.** Soren (255541): "the AI is capable of launching naval invasions using a variety of
  land, sea, and air units". C3C added to this. LouLong (61791): "AI improvemement : picking bombardment
  targets; escorting key naval transports like carriers and transports."

### 3.2 Build-often / build-never flags (RACE) and unit AI strategies

- **The flags.** RACE has three groups of flags:
  - Never flags in `Flags[8..9]`: Offensive, Defensive and Artillery land units, Settlers, Workers,
    Naval, Air, Growth, Production, Happiness, Science, Wealth, Trade, Explore and Culture.
  - Often flags in `Flags[12..13]`, for the same categories.
  - Governor flags in `Flags[4]`: ManageCitizens, Emphasize Food/Shields/Trade, ManageProduction,
    NoWonders and NoSmallWonders.
  Source: `QueryCiv3/BiqSections/Race.cs`.
- **Small effect on units (tested, Ozymandias, 29 Nov 2023).** https://forums.civfanatics.com/threads/manipulating-ai-unit-builds.686973
  found the flags "Don't affect Unit Builds by more than ~5%". He also found "Selecting more than 3
  (_maybe_ 4) of these options will "confuse" the AI, causing it to ignore all of your selections".
  Predator145 (Jan 2025) gives a different, second-hand limit: "Alexmann who worked with Firaxis has
  confirmed many years ago that 6 is the max effective number". Source:
  https://forums.civfanatics.com/threads/ai-build-often-building-categories.692300/
- **Categories overlap, and the AI favours shield buildings (Firaxis).** Soren Johnson in
  https://codehappy.net/apolyton/threads/56961-1.htm (25 Jul 2002, vanilla): "Trade = harbors/marketplaces/airports"
  and "Wealth = marketplaces/banks/workers, not just 'Wealth'"; the AI "preprocesses every unit/building to put
  them into multiple categories" (a cultural bank counts as both culture and wealth); "the AI does place a higher
  priority on shield-increasing building"; "I would recommend rarely using the Never flags". Asked whether
  courthouses and markets fall under production and happiness, he answered "actually, the answer is yes in both
  cases...". The differences between civs were "meant to give the AI's personality".
- **How unit strategies drive behaviour (Firaxis).** Soren in https://codehappy.net/apolyton/threads/39744-1.htm
  (13 Jan 2002): "The AI plans are basically there to allow modders (as well as myself) the ability to give units a
  "personality"."; "units with the "Land Offensive" plan use a separate function to determine their actions";
  "a rifleman might decide to attack if the odds are in his favor"; "a city's governor will look at how many
  "offensive" and "defensive" units are in the city/continent"; "defensive units look to help defend offensive
  units out in the field".
- **Strategy prerequisites (Firaxis editor text).** EDHELP "AI Strategy Rules" lists what a unit needs before it
  may carry each strategy, for example Offense: "Offensive Power > 0"; Artillery: "Bombard Strength > 0" and "Must
  have BOMBARD action"; Settle: "Must have BUILD CITY action"; Terraform (workers): every worker job action plus
  "Must have AUTOMATE action" and "Must have JOIN CITY action". Antal1987's thread
  https://forums.civfanatics.com/threads/useful-patches-for-civ-3-conquests-v1-22.526285 lists as a known stock
  bug "worker units need all worker actions checked if it is to be used properly by the AI". AvalancheMaster,
  https://forums.civfanatics.com/threads/getting-the-ai-to-use-worker-units-without-all-worker-jobs-enabled.515992/
  (Dec 2013), found the limit is the editor's, not the game's: "this is not hardcoded in the game, it's only in the
  editor." With the Terraform flag set by hex edit, the AI used the unit anyway; Kirejara confirmed it with another
  editor. So what the game reads is the strategy flag.
- **Unit AI strategies in the code (C3X-THREAD, Flintlock).** "each unit type can only have one AI strategy."
  The AI keeps a count of units per strategy, and "the only time that count is ever decremented is when
  a unit is destroyed." (post-16531281, page-83 #1641). A unit with several strategies in the
  editor is stored as several copies in the BIQ (post-16375682, page-59 #1162). Which units need an escort is fixed in
  the executable: "it's hard-coded in the executable which unit types require escort." / (paraphrase from a summarised fetch) "I
  haven't seen the requires escort flag used anywhere in the unit AI." (post-16553267, page-89 #1765). Civinator
  (post-16843785, page-145 #2892): "AI units without any strategy are "dead" in the game."
- **Which buildings each category covers** (Predator145, 3 Oct 2024, thread 692300; community. Where it differs
  from Soren above, for Wealth, Soren's vanilla answer is the Firaxis one).
  - Growth: "Granary, Aqueduct, Hospital".
  - Production: Factory and power plants.
  - Science: buildings that multiply beakers.
  - Trade: "Improvements that give a gold income multiplier (market, bank, stock exchange)", plus Harbor and Airport.
  - Wealth: the shield-to-gold project.
  - There is no Military category: "there is no Military category to encourage the AI to build barracks/harbors/walls."
- **Unit mix (tested, Ozymandias, 686973, about 560 AI builds).**
  - In peacetime the split is "D : O : other ~= 53% : 43% : 4%". The AI "likes" "between a 3:2 and 4:3
    ratio of D:A units".
  - Artillery is about 1% of builds, and "the AI was proven not to take bombard into account."
  - The AI will "Re-Flag" a unit from offence to defence or back as it sees fit.
  - The AI keeps offensive and defensive units in balance ("YES" to "Does the AI seek equilibrium").
  - Builds are partly random, and difficulty and aggression did not change the mix.
- **Unit abilities the AI favours** (tom2050, relayed in 686973): it "absolutely loves and adores" extra
  hit points. Blitz "would only increase the AI building them ... by around 4%."
- **Production is a point score per option (code).** C3X-INI: "When choosing what to build, the AI computes a
  point value for each of its options ... point values are often in the hundreds for the most desirable options."
  The stock function is `City_ai_choose_production` (C3X-SRC `civ_prog_objects.csv`).
- **Artillery stays home.** C3X-INI describes its own fix as letting "artillery unit AI to grab escorts from city
  defenders, causing the AI to use its artillery offensively instead of leaving it in its cities." So stock
  artillery waits in cities for an escort it does not get.
- **Leader and army bugs (code).** C3X-INI on the stock leader AI: "a bug that allowed the AI to rush any kind of
  city production, including units and (with an MGL) wonders". On armies: "a major bug that prevented the AI from
  filling its armies" and "some logic that encouraged the AI to mix unit types in armies". This explains the
  "rare as a white rhino" armies (3.5). Third round (C3X-SRC `patch_Unit_ai_move_leader`): the stock leader AI first
  disbands a leader that is on a continent with no city of its civ ("Disband the unit if it's on a continent with no
  cities of its civ. This is what the original logic does.").
- **Workers: it stops building them once it has "enough" (code reading, Flintlock).** C3X thread page 95,
  https://forums.civfanatics.com/threads/c3x-exe-mod-including-bug-fixes-stack-bombard-and-much-more.666881/page-95#post-16587093:
  the AI "assigns a point value of zero to the worker build option when it feels it has enough of them", and its
  need is computed from a city count (his option "adjusts the city count that the AI uses to evaluate how many
  workers it needs"). He believes it keeps none in reserve "except for pollution cleanup". Erlend Philip Wood,
  same page (post 16587268), observed: "The AI would always build a number of Workers equivalent to 80-90% of its
  number of cities at the peak of its development", and "Setting the 'Build often' flag for Workers makes
  absolutely no difference."
- **The human can steer governors.** Soren (255541): you can "tell its governors to never build artillery and air units."

### 3.3 Settling: spacing, gaps and expansion

- **Early spacing of about 4 tiles.** Dominae, 31 Mar 2002, writing about a 4-tile spacing plan:
  https://apolyton.net/forum/miscellaneous/archives/civ3-strategy-archive/57896-early-gaem-deity-base-spacing-paradigm
  says "The AI pretty much follows this plan to the letter at all difficulty levels". Aeson, same thread:
  "I can sometimes build cities at a 5:6 ratio of what the AI does with 4 spacing."
- **Gaps that the AI later fills.**
  - Brain, Mar 2005: https://forums.civfanatics.com/threads/other-civs-encrouching-on-my-borders.113953
    says "The AI often leaves big gaps between their cities, which allows you to squeeze in."
  - The AI also fills empty land left inside a human's area. Grogs, same thread: the AI "just walked in
    and grabbed" an unclaimed area in the middle of his empire. Rohirrim: "If you can't block them
    completely, they just usually walk right past you and plop a city."
- **No settling in foreign borders or first-ring conflict.**
  - Grogs (113953): "You can't build a city within another nation's borders, and the AI cannot build one within yours."
  - SJ Frank: "The AI will never settle a city that has first-ring conflict."
  - Exception: eldar says that if an AI is "reduced to a single settler, they will settle in place (if they can)".
- **Expansion.**
  - Ision (92917) says the AI often builds settlers before its growth can support them.
  - "A sprawling Expansionist AI by the mid-Middle Ages is common".
  - The AI "will NEVER attempt a suicide run to find another land mass."
  - SirPleb (68813): "The AIs do not explore effectively, particularly large bodies of water."
  - With a revealed map the AI throws its scouts away. Fergei, https://forums.civfanatics.com/threads/suicidal-ai-scouts.660070/
    (C3C, 30 Jun 2020): "my Expansionist AI Civs all kill / disband their scouts on the first turn"; Kirejara's
    explanation, which Fergei accepted: "With the map visible for the KI they see no need for scouting". The
    thread does not say which setting revealed the map; it is a single report.
- **Minimum distance (all players).** The rule is one empty tile between cities. C3X-INI: "if it's set to 1,
  cities cannot be founded adjacent to one another, there must be at least one open tile separating them. A
  setting of 1 there corresponds to the standard game rules." Antal1987's patch notes (thread 526285, v1) also
  describe a stock limit that blocks a new city when the settler is "less than 2 tiles from an existing city".
  For the engine's AI, see the owner ruling at the top.
- **What the site score favours (code readings, C3X-THREAD, Flintlock).** (post-16169633, page-23 #442):
  "the AI is programmed to find locations less desirable the farther they are from its already existing cities",
  "the AI's city placement is dominated by terrain (it loves grassland)", and "the AI almost
  completely ignores coastal resources and has no qualms about founding directly on a resource". (post-16149858,
  page-19 #375): "it doesn't consider how placing a city somewhere will affect the placement of others around
  it." (post-16831749, page-140 #2788): "There are no knobs inside the AI that control how quickly it expands
  other than its preference for producing settlers", and its city location evaluator steered it away from overlapping
  work areas. (post-16575327, page-93 #1841): "Under the standard game rules the AI would be better off
  packing more cities in closer together". He could not find where the AI is kept from founding inside its own borders
  (post-16843718, page-145 #2886: "it's not obvious in the AI's logic where this restriction is"). The
  owner's spacing ruling (top of section) is unaffected.
- **Site choice is a desirability score per tile (code).** The stock function is `Match_ai_eval_city_location`
  (C3X-SRC `civ_prog_objects.csv`, signature `int (... int x, int y, int civ_id, bool param_4, int *
  out_breakdown)`). C3X-INI's debug overlay "Shades/highlights each tile to show how desirable the AI considers it
  as a city location". The scoring terms themselves are not published.
- **Settlers are a very strong AI build preference.** C3X-INI: "The AI has a very strong tendency to build
  settlers so you'll need to set these options quite high to slow it down significantly. For example, a perfume
  of -200% over a 30 turn duration left the AI with about half as many cities after 50 turns in my test."
- **It fills every spot, good or bad (vanilla observations).** DrFell, https://codehappy.net/apolyton/threads/42759-1.htm
  (19 Feb 2002, 1.17): "Watch them cover every free space on the board with a city, including completely
  unproductive tundra." solo, same thread: it founds cities "wherever spots are left until the map is full."
  Coracle, https://codehappy.net/apolyton/threads/50345-1.htm (May 2002): "It wastes a lot of its resources sending
  out settlers to every crappy piece of land anywhere." solo (39845, Jan 2002): AI settlers went for an open
  horse or iron tile and ignored empty tiles without resources. =DrJambo= (48213, Apr 2002) on 1.21f: AI
  expansion was "Definitely toned down".
- **City limit.** README-V 1.17f: "Fixed bug which allowed the AI to keep trying to build cities if the city
  limit had been reached." The stock limit is 512 (C3X-INI: "For base game behavior, set to 512.").
- **Settler escorts.** Settlers do travel with escort relationships: C3X-INI, describing a stock freeze, says the
  disembark logic does "not consider escort relationships (e.g. for settlers)". How many escorts the AI gives a
  settler, and when, is not sourced (see UNVERIFIED). The thread on blocking AI settlers that an earlier search
  found is about Civ5.

### 3.4 War: when it declares

#### 3.4.1 War plotting (code reading of stock C3C, FLINTLOCK-WAR)

The AI first decides to plot a war, then picks a target. justanick (#13): "It chooses to plot a war first and
then chooses a target second."

- **The per-turn chance to start plotting.** "Each AI player has a chance at the start of each turn to begin
  plotting war." Every roll below must pass:
  - "Roll one in (32 - PowerRank)" "where PowerRank = 31 if it's the strongest civ in the game, 30 if it's
    second, and so forth." "The power values used here are the same shown on the F8 histograph screen."
    (C3X-SRC `struct Leader`: `int power_rank; // 31 = strongest, 30 = second strongest`.)
  - "If the game is set to the lowest difficulty, roll one in 32"
  - "Roll one in (12 + -2 * EffectiveAggressionLevel)" (effective level: 1.3 and 2.1; range 8 to 16)
  - "If already at war, roll one in 16"
  - "If in anarchy, roll one in 8"
  - "Have a capital city"
  - "If have fewer than 2 * OCN cities and there are any open city spots on the capital's continent, roll one in 2"
  - "If in a government that experiences war weariness, roll one in 2"
  - "If do not have at least one defensive unit per city and one offensive unit per two cities, roll one in 2"
  - "If do not have at least two defensive units per city and one offensive unit per two cities, roll one in 2"
  - "If do not have at least two defensive units per city and one offensive unit per city, roll one in 2"
  - "If treasury is too low, roll one in 2 (too low here means < 16 per city" "plus more if the AI has any Wall
    Street type small wonders to ensure it's receiving the maximum interest possible)"
  - justanick's worked example (#6): strongest civ, aggression 0, a war-weariness government, a mid-sized
    military and a low treasury gives "in the end it is 1/48 for the most powerful tribe", and "the second most
    powerful tribe has a chance of only 1/96 per turn." He adds (#13): "Once AI has 2 defenders and 1 attacker per
    city it is eager to start wars, but not before that."
- **Who can be a target.** "Other players with whom the AI has a mutual protection pact are excluded". "Each
  player has a cooldown timer that may exclude them. I believe this blocks war plotting within 8 turns of signing
  peace." (the 8 is his belief). "Lastly the AI randomly excludes some players": it rolls "**not** one in X", with
  "X = 1 to start", "X += 2 if the AI has a peace treaty with the target", "X += 2 for each common enemy", "X += 2
  for each military alliance the AI and the target have". (Flintlock #8 corrected the direction: "This check is
  reversed from all the others.") "The AI will skip this check if the target is a human player who is ranked first
  in power."
- **Target score** ("Higher values mean more attractive targets"; whether the AI takes the top score or a weighted
  pick is not stated):
  - "To start, X = 2 * (PlayerCount - 32 + PowerRank)." So stronger targets score higher. Flintlock: "So AIs are
    more inclined to attack stronger opponents. Not sure if this is a bug."
  - "If the AI has a capital, X = X + 64 // NearestCityDistance" (distance from the AI's capital to the target's
    nearest city). justanick: "Notably this does not scale with map size."
  - "X = X + DiploEffect where DiploEffect = 8 if furious toward the target, 4 if annoyed, 2 if cautious, 1 if
    polite, and 0 if gracious."
  - Land units per city of the target (units with the offensive or defensive strategy // cities): "X = X + 4 if
    LandUnitsPerCity is 0, + 2 if it's 1, + 1 if it's 2, and + 0 if it's 3 or more."
  - "If target is in anarchy, X = X + 1"
  - "If the target is a human player ranked top in power and twice as powerful as the second-ranked player, X = 2 * X"
  - "If the AI has a resource trade with the target, X = 3 * X // 4"; "For each common enemy that the AI and the
    target share, X = 2 * X // 3"; "If the AI and the target do not share a continent, X = X // 2"; "If the target
    has a peace treaty or mutual protection pact with any player, X = X // 2."; "If the AI has a peace treaty or
    mutual protection pact with the target, X = X // 2."; "X = X // (AllianceCount + 1)".
  - Past war damage: "If DamageBalance > 0, X = X // (1 + DamageBalance // max(1, AI's city count))", where the
    balance is damage the target dealt to the AI minus damage the AI dealt to the target.
  - Flintlock's summary: "the AI tends to declare war on targets that are stronger and closer, that it's angry with".
- **Human-player bias is in the stock code.** C3X-INI offers to remove "the AI's special preference for targeting
  human players when plotting wars. Rules for targeting runaway players still apply". The bias is the two human
  checks above (no random exclusion for a top-ranked human; doubled score for a runaway human).
- **Unit quality does not deter.** The rolls and the score count units per city, not their strength. justanick
  (#20): "Having units stronger than warriors offers no deterrence whatsover against AI plotting a war against you."
  Kokuryutei (#19) disagrees without a source.
- **From plotting to declaration.** Not in the code reading. Lanzelot (#16), tested "a few years ago" at Emperor
  or Deity: "nothing can revert that decision, once taken" (MPPs, alliances, trades, gifts and embargoes all
  failed). Southern_Man (#14) and Fergei (#15) guess about 4 turns from plotting to the declaration; Ruin (#17)
  speculates that a plot "times out" if the AI cannot attack. Fergei: "The AI will declare war on you, even if
  you have an MPP".
- **Other ways into war (code).**
  - A refused demand or a nuke on a third party (aggression-based chance, 2.1). C3X-SRC names the stock
    `Leader_ai_roll_to_declare_after_provocation`. The gates that decide when the AI makes a demand at all
    (power place, attitude and aggression rolls, war-damage balance, a 32-turn cooldown per target) are in
    `06-diplomacy-and-espionage.md` 6.9, from C3X-SRC's re-creation of the stock sequence. The war roll after a
    refusal is skipped when the two have a mutual protection pact.
  - The submarine bug. C3X-INI: "The AI runs into invisible units (such as submarines) belonging to other players
    because it blindly follows paths produced for it by the game's pathfinder ... After colliding with an invisible
    unit, the AI initiates combat, declaring war if necessary." README-V 1.16f: "The AI no longer reacts to
    submarines that it cannot see."
- **When the AI accepts peace (Firaxis).** Soren Johnson (https://codehappy.net/apolyton/threads/41866-1.htm,
  post #27, 11 Feb 2002, vanilla): "The AI's willingness to discuss peace terms depends on a) how long the war has
  lasted and b) how much it is winning or losing the war." The time factor is "there to prevent people from
  war-jacking the AI by continually declaring war and then agreeing to lop-sided peace agreements." Full entry in
  `06-diplomacy-and-espionage.md` 6.5.
- **Chokepoints (Firaxis).** Mike Breitkreutz (Firaxis programmer), https://codehappy.net/apolyton/threads/89862-1.htm
  (20 Jun 2003): "The AI has always been programmed to seize chokepoints..."; on PTW 1.21f: "I don't believe any
  changes were made to the AI for 1.21f."

#### 3.4.2 Community observations

These are community observations from
https://forums.civfanatics.com/threads/when-does-ai-declare-war.244088 (several agree with the code above:
power, attitude and open land):

- MAS: "The most important factor is probably military strength."
- Nergal: "General attitude, ie annoyed or furious are way more likely to attack than Cautious." Also "The
  Zulus and Germans are pretty much always up for a war" (aggression +2 in RACE).
- Lord Emsworth: "If the AI gets something from you on per turn basis (esp.for free) it is extremely
  unlikely that you will be attacked." He also says that tribute demands follow aggression.
- rysingsun: "they are far less likely to declare war if you have rop with them"; "they don't declare
  much when there is lots of land still to be settled".
- civverguy: "they move a couple units in your borders and declare war on you the next turn."
- I. Larkin: "if AI have reach the point that they can't support their military anymore they declare war no matter what..."
- Risa (Apolyton, https://codehappy.net/apolyton/threads/102345-1.htm, C3C, 23 Nov 2003): "Once an AI runs out of
  space to expand, it's almost guaranteed to be involved in wars within 20 turns." This fits the code roll that
  halves the plotting chance while open city spots remain on the capital's continent (3.4.1).
- Aeson (Apolyton, https://codehappy.net/apolyton/threads/39845-2.htm, 22 Jan 2002, vanilla): "The AI is actually
  more willing to declare war if you have something they want and won't trade it to them." This fits the demand
  path (a refused demand can lead to war), not the plotting code.
- Several PTW 1.21 players (Apolyton, https://codehappy.net/apolyton/threads/89030-1.htm, Jun 2003) say the AI
  declares war when it can no longer pay a per-turn deal. alva: "If the AI goes bankrupt, he will declare war on
  you, yes."; Blaupanzer: "the AI will declare war on a civilizatioin if it is no longer capable of fulfilling a
  per turn deal with it." dexters calls the idea that the AI ignores its per-turn deals "another myth". No test or
  code reading; the plotting rolls above have no deal term besides the resource-trade score cut.
- **Units in their territory.** The AI first asks you to leave. If you are still there the next turn, it
  can force you out or go to war. This is a forum summary from the search for "units in our territory"
  and has not been checked against a primary quote (the turn count is UNVERIFIED). The two-step order (ask, then "leave or
  declare war") matches the AI Attitude Study in `06-diplomacy-and-espionage.md` 6.7 (asked to leave +1; told to
  leave or declare war +4). Players can make the same request: README-PTW fixed a "bug where you could not ask an
  AI civ to remove its units from your territory." notyoueither (Apolyton 102345, C3C, Nov 2003): "1 out of 3
  or 4 times the AI will respond to demands to leave my territory with a DoW." (one player's count)

### 3.5 War: how it fights

- **Piecemeal attacks and poor artillery use.** Ision (92917): AI attacks are "piecemeal", and "the AI has
  no concept of proper artillery use". AI armies are "rare as a white rhino".
- **Artillery mostly stays in cities (C3C).** From
  https://apolyton.net/forum/civilization-series/civilization-iii/121250-does-the-computer-use-siege-units-in-conquests:
  - Jeem: "The AI does 'attempt' to use ground artillery offensively now, but it's still pretty rare".
  - ChrisiusMaximus: "they still seem to keep them holed up in their cities, and only seem to use them when I attack as a defensive strike."
  - Catt: "the AI won't usea stack of 10+ artillery units, with appropriate defense, to lay siege to a city".
- **Heavy garrisons, defensive escorts, quick naval retreat, small landings.** Ision (92917): the AI keeps
  heavy garrisons in every city and escorts its offensive stacks with many defensive units. It retreats
  ships to port after minor damage, and its amphibious invasions use too few units.
- **AI-vs-AI wars.** Ision: they are "wars of attrition" that drain the winner too.
- **Naval transport escorts (code).** C3X-INI: "In the base game, the AI will assign 1, 2, or 3 escort units to its
  naval transports and carriers depending in their shield cost and the number of units they're carrying."
- **Transports wait for each other; damaged units in your land keep fighting (1.29f observations).**
  https://codehappy.net/apolyton/threads/59444-1.htm (Aug 2002): Catt saw an AI transport and escort that "waited 3
  tiles off-shore" until a second transport joined, and "knocking a single HP off of a transport vessel doesn't
  result in the transport heading home for repairs". DeepO: "when an AI troop is in your territory, it will not
  retreat when damaged", and "there are very few improvements to the naval AI" in 1.29f. Jon Shafer (Firaxis),
  relaying Soren on 1.29f: "all he told me was that he changed the AI offensive/defensive 'bug'."
- **Naval play improves when isolated (PTW 1.27f observation).** dexters, https://codehappy.net/apolyton/threads/96269-1.htm
  (5 Sep 2003): an island AI "promtly sent a fleet of galleys" and landed units over four landings; "it was not
  uncommon to see civs stuck in islands totally neglect their navies" in his earlier debug games.
- **The "disembark all" freeze (code).** C3X-INI: "When using "disembark all" (which the AI always does) on a boat
  containing units that can't be disembarked, the game" can freeze. So AI landings always unload everything.
- **Multiple stacks (PTW observation).** Jaybe, https://codehappy.net/apolyton/threads/84396-1.htm (27 Apr 2003):
  "Multiple stacks, occasionally diverging to separate targets"; "They still try to enter at the most
  advantageous entry point". alexman (Firaxis) in the same thread lists "fewer SOD's" among the PTW changes.
- **The unit AI in the code (C3X-THREAD, Flintlock, C3C 1.22).**
  - Little planning, no coordination: "Internally the unit AI does little to no analysis or planning, it just
    performs basic reactions to its surroundings." (post-16163990, page-22 #429). "there is no unit stack AI
    in Civ 3" / "It's every unit for itself"; "one unit may have multiple escorters but an
    escorting unit may only have one escortee" (post-16444427, page-73 #1455). "the unit AI for the
    attackers never considers artillery and vice-versa" (post-16898219, page-164 #3269). A better unit AI exists in the
    executable but is unused: "the game already includes a half decent unit AI except it's never activated
    in the base game" (post-16270971, page-34 #668).
  - Artillery: "arty units in a city will never leave that city unless there are too many of them in it"
    (post-16075105, page-8 #150); "AI artillery will only path toward an enemy city" (post-16729166, page-124
    #2476); "The reason AI artillery leaves its stack is because its escorting unit was destroyed" (#1455);
    captured artillery "must return to a city so it can be assigned an escorter" (#3269). He thinks the stock
    swarm without artillery "may actually be superior" (post-16937054, page-177 #3521).
  - Armies: "There are two bugs that prevent the AI from using armies in the base game." (post-16111169,
    page-13 #258); the add-to-army filter "failed to consider that armies have an extra move" and
    "The reason the AI doesn't attack high health armies is that each unit is processed individually"
    (post-16304748, page-40 #795).
  - Naval escorts and invasions: "Normally the AI will assign between 1 and 3 escorts for each of its naval
    transports" (post-16441638, page-71 #1419), and "The AI almost always ends up assigning three escorts to
    each transport." (post-16857204, page-150 #2981), the only place it weighs unit shield costs. The escort check
    ignores that escorts are damaged if they can't heal in place (post-16102748, page-12 #233). Loading:
    "units load up for a naval invasion if they happen to find themselves in a city with a naval transport and
    have nothing better to do." / (paraphrase from a summarised fetch) "There's no way for them to seek out a transport, which is why the AI often
    sends out transports that are only partially filled." (post-16341094, page-51 #1018). "The AI can't
    transport caravans between continents. It can't even transport workers between continents, as far as I know." (#2981).
    It never uses land transports (page-165 #3287) or loads explorers (page-169 #3365).
  - Air: "the AI's fighters and bombers do not coordinate at all" / "the AI does not remember where
    or when it's been bombed." (post-16523964, page-82 #1623). "the AI doesn't build many air units unless it's
    triggered to do so by getting bombed itself." (post-16217348, page-30 #586, his belief). The AI has no rebase validity
    check (post-16783087, page-133 #2653). Cruise missiles: "The cruise missile AI looks for a target within
    bombard range and attacks if it finds one or otherwise fortifies in place." (post-16089946, page-11 #211). Nukes: it
    "considers the value of resources on a tile when deciding whether to nuke it" (post-16965737, page-185 #3694).
    Predator145 (post-16393228, page-63 #1248, play report): the AI will not use its nukes unless it has been hit by one.
  - Tactics it lacks: ZOC ("I don't think the AI understands ZoC at all", post-16537815, page-84 #1671) and
    stealth attack (post-16853988, page-148 #2959; section 1 §1.1).
  - Disorder: "The AI won't let its cities disorder" (post-16929609, page-174 #3467). Earlier, more hedged
    (post-16844914, page-146 #2910): "For the AI, I don't think I've ever seen it leave a city in disorder for
    multiple turns", with an exception for extreme war weariness.
- **Patrol.** README-PTW (1.26f): "the AI will no longer use its units to randomly patrol if they have nothing else to do"
  when the INI option `NoAIPatrol` is 1; "The default is 0 (meaning the AI will patrol)."

### 3.6 Workers, sliders, government, trade

- **Too few workers.** Ision (92917): "the AIs never-ever build anywhere near enough workers." See 3.2 for the
  code reading (zero value once it has "enough") and the 80-90%-of-cities observation. vmxa (CFC "No Settlers, No
  Workers?", https://forums.civfanatics.com/threads/no-settlers-no-workers.704355/, C3C, Aug 2026, an explanation
  from play, not a test): "It is not uncommon for AI to be starved of workers on Sid" because "They can build units
  so cheaply that they hit their support cap and start disbanding workers."
- **Worker habits (Firaxis, PTW).** alexman ("Firaxis Games Software Engineer"),
  https://codehappy.net/apolyton/threads/84396-1.htm (26 Apr 2003), on PTW 1.21f against 1.29f: "The AI no longer
  sells workers for just 30 gold, crippling themselves and helping the human" (now about 120 gold), and "The AI no
  longer irrigates grassland tiles in Despotism, unless there is a good reason to do so". README-V 1.17f: "Fixed
  AI worker loop bug." dexters, https://codehappy.net/apolyton/threads/88537-1.htm (Jun 2003), saw the AI disband
  workers "rathern than sell off its improvements when it has 0 gold in the treasury". joncnunn
  (https://codehappy.net/apolyton/threads/76212-1.htm, PTW, 5 Feb 2003) saw the same change in play: "Under the
  latest PTW patch, the AI stops building irrigation on grassland under despotism." Automated workers put pollution
  first: Harovan (https://codehappy.net/apolyton/threads/59554-1.htm, Aug 2002): "Cleaning pollution is high on
  their priority list".
- **Out of money (code). Corrected in the third round.** The C3X-INI passage quoted here before (sell buildings,
  then disband units, then switch to Wealth; "For AI players, the game alternates the order of (1) and (2) each
  turn"; the research cut every third turn) describes the mod option `aggressively_penalize_bankruptcy` (default
  `false`), not stock. C3X `changelog.txt`: "Option to aggressively penalize bankrupt players" (Release 14) and
  "aggressively_penalize_bankruptcy now alternates between selling buildings or disbanding units first for bankrupt
  AI players and every third turn cuts their research spending". With the option off, C3X-SRC calls the stock
  `Leader_pay_unit_maintenance`. What the code does show for stock: building and unit upkeep are paid in random order
  (C3X-SRC comment: "the randomization of whether improv or unit maintenance gets paid first"), and the stock AI
  slider routine already cuts spending ("ai_adjust_sliders will already cut AI spending but maybe not all the way to
  zero"). See `02-city-economy.md` §7.
- **AI workers in the code (C3X-THREAD, Flintlock).** "for irrigating and mining it orders its workers around
  "manually"", "it will never tell a worker to replace a mine with irrigation or vice-versa", and the stock
  bug "The bug is that the logic to handle the despotism penalty uses the scaled yields instead of the raw yields."
  (post-16191224, page-25 #494). The irrigate-or-mine choice is one yes/no function (post-16563260, page-90 #1793:
  "It returns only true or false, if true the tile will be irrigated and otherwise it will be mined."). Radar
  towers: workers "consider every tile on their continent that could have a radar tower" and build on the
  highest-valued one (post-16589155, page-96 #1907). Predator145 (post-16094435, page-12 #225, play report): the AI does
  not clear marshes, build airfields or irrigate mined plains after Electricity.
- **Sliders are set by code each turn (code).** C3X-SRC: the stock "preproduction player update method" makes a
  "human-ness test and call to Leader::ai_adjust_sliders" for AI players, and "The rate cap limits AI players'
  spending on the gold slider (in Leader::ai_adjust_sliders)", where the cap is the government's rate cap. The
  code reading does not say how the split is chosen.
- **Code reading: the stock AI does move the luxury slider, too little.** Flintlock (C3X-THREAD post-16797044,
  page-135 #2687): the AI's slider use "it's not aggressive enough on the luxury slider". That corrects Ision's "will NOT use" below, and is consistent with alexman's "best the AI can do without
  using the luxury slider" if the AI used it little in his test. The owner ruling (the AI keeps the slider) stands.
- **Tiles: every city for itself (code reading).** Flintlock (post-16813639, page-136 #2702): "The AI never,
  as far as I know, considers how to distribute tiles among multiple cities." / "It's every city for itself,
  choosing the strongest among all the ones it can work." For every player (post-16848272, page-147 #2928): the game
  "never considers which city would be the best to work a given tile."
- **Upgrades: a gold reserve gates them (code reading).** Flintlock first wrote (post-16168856, page-22 #439, "AFAIK")
  that the AI always upgrades when it can; he corrected this later (post-16937054, page-177 #3521): the upgrade pass is
  "behind a weird little check", "The reserve amount is 16 gold per city plus enough to earn the AI
  full interest income" "if it has any wonders that grant that", and "the AI will not upgrade anything when it's below the minimum gold reserve even
  if the upgrade is free." He adds (post-16583488, page-95 #1884): "the AI isn't smart enough to move its
  obsolete units to the cities where it has barracks". The later reading supersedes the first.
- **Rush-buying: improvements only, above a reserve (code reading).** Flintlock (post-16787843, page-134 #2662):
  "Its reserve target is 1000/number_of_interest_generating_wonders + 16 * number_of_cities", and "it won't
  rush an improvement if doing so would leave its treasury below that level." The check is whether
  rushing "would reduce the AI's treasury enough to reduce its income from interest" (post-16783087, #2653).
  Spoonwood (post-16286783, page-39 #763, observation): "The AI also does not appear to cash rush anything."
- **Selling buildings: one routine, for replacing power plants (code reading).** Flintlock (post-16787843, page-134
  #2662): "There's only one routine where the AI considers selling city improvements"; per the thread notes it
  weighs replacing a plant on production bonus, pollution and upkeep only (summary wording). The production chooser
  makes the same assumption (post-16823297, page-136 #2720): "the AI's logic for choosing production assumes
  that buildings with the replaces-others flag are powerplants."
  Predator145 (post-16345873, page-52 #1031): (paraphrase from a summarised fetch) "The AI will not sell obsolete improvements and will only lose
  them if they can't pay maintenance."
- **Luxury slider.** Ision: the AI "will NOT use the luxury slider to compensate for any happiness
  issues". Instead it uses entertainers. alexman's PTW debug test, https://codehappy.net/apolyton/threads/66928-1.htm
  (7-8 Nov 2002; six one-city islands): AI cities were "Always irrigating like crazy to compensate for the lack of
  food from assigning entertainers"; city governor settings "made no difference"; "I think this is the best the AI
  can do without using the luxury slider." Owner ruling: the engine's AI keeps the slider (top of section).
- **Research rate (vanilla observations).** https://codehappy.net/apolyton/threads/42759-1.htm (Feb 2002): Pius
  Popprasch saw AI research "remains stable at 40% no matter what you do"; solo saw rates "range from 70% down to
  30%". Harovan: the AI does not pay gold per turn when its own balance is not positive.
- **Tech choice: no flavors in the stock game (Firaxis).** alexman (Firaxis),
  https://codehappy.net/apolyton/threads/102278-1.htm (22 Nov 2003): of C3C's tech flavors, "None of them are
  currently used" in the epic game; flavors "can't make the AI want to research something more than if it didn't
  have any flavors"; with every item at 50% the AI researched in the same order as with none. donZappo: "the
  flavors are not incorporated at all into the epic game." EDHELP "Flavor Page": flavors are "a way to modify how
  non-human (or AI) civilizations choose which paths to develop along the technology tree". The stock tech
  valuation is `Leader_ai_eval_technology` (C3X-SRC); C3X-INI: each point of tech value is "worth roughly one gold
  in trade, and also affects what the AI chooses to research". README-V 1.17f: "Fixed AI bug giving value to
  worthless techs."
- **Changes government often.** Ision: "Changing governments is a favorite AI past time, doing so FAR too
  often through the course of a game."
- **Government switching rules (chiefpaco, 27 Apr 2002).** https://forums.civfanatics.com/threads/about-governments.21298
  - A peaceful AI in Monarchy or Communism "often switches to Democracy or Republic".
  - After a long war it "switches to Comm or Monarchy".
  - "If the AI discovers a new govt while in Desp, it switches."
  - chiefpaco: "I think it is entirely based on War & war weariness."
  - It is unclear whether the favourite government is a goal or only affects attitude. Propaganda said
    that even with Democracy shunned, "all of them always choose Democracy." (see Disputed)
- **Favourite and shunned government (Firaxis editor text and code).**
  - EDHELP "Favorite Government": "the civilization will immediately switch to this government type as soon as
    it becomes available." EDHELP "Shunned Government": the civ "will avoid this government type at all costs, and
    if it is forced to operate under this government type, it will immediately switch to another government type
    as soon as another government type becomes available."
  - They also feed attitude. FLINTLOCK-WAR: "if the other player is in the AI's shunned government, it adds 5
    points" (higher is worse). `06-diplomacy-and-espionage.md` 6.7 has the attitude study's -5/+4.
  - The choice is a score per government. C3X-SRC hooks a stock `Leader_ai_eval_government`; C3X-INI: "I don't
    know how perfuming governments affects the AI's willingness to overthrow its current govt. The perfume for sure
    applies when the AI is selecting a new govt type after anarchy." So the stock AI picks its new government by
    score after anarchy; how the favourite enters that score is not published. See also `05-governments.md` 5.8.
  - **Code reading of the score (C3X-THREAD, Flintlock):** points in gold per turn, with the tile penalty worth 8 per city,
    corruption levels 12/8/4/4/2 per city, and "Shunned/favorite government is worth 12.5%" (post-16217016,
    page-30 #583; full quotes in `05-governments.md` 5.8).
  - Tavis (Firaxis), quoted in `05-governments.md`: free Feudalism maintenance in testing meant "the AI wouldn't
    switch from it!!", which shows the switch decision weighs the government's economy.
- **Scientific AIs broker techs.** Ision: they gain techs and gold by trading techs on, and often end up
  with the most gold. Commercial AIs "rarely translates its inherent gold advantage out properly."
- **Early tech trading.** eric2075 (68813): "the AI won't trade techs very easily in the early game either."
- **Lopsided deals (Firaxis).** Soren Johnson, quoted in https://codehappy.net/apolyton/threads/56854-1.htm
  (Jul 2002): under a "renegotiate every 20 turns" rule, "if you had a lopsided deal with the AI, IT would decide to
  renegotiate the deal." In practice badams52 (same thread, 1.21) found that after the AI connected a resource it had
  bought, "he wont ask to renegotiate the deal with me".

### 3.7 "The AI never / rarely does X" (community)

| Observation | Source / quote |
|---|---|
| Few or no armies | Ision (92917): armies are "rare as a white rhino" |
| Coordinated artillery sieges | Catt (121250), quoted in 3.5 |
| Sending ships across ocean to explore | Ision (92917): "will NEVER attempt a suicide run to find another land mass" |
| Ending a galley's turn where it could sink | Soren via TheNiceOne (27273): "an AI galley will never end a turn where it could sink." |
| Building Barracks, Walls or Harbors in time (no Military category) | Predator145 (692300) |
| Using artillery in the field | C3X-INI (3.2): stock artillery is left "in its cities" |
| Filling armies | C3X-INI (3.2): "a major bug that prevented the AI from filling its armies" |
| Weighing unit quality in war plans | FLINTLOCK-WAR (3.4.1): only units per city count |
| Building Explorers | alexman (56961, vanilla): "I have never seen the AI build one." |
| Using leaders properly | C3X-INI (3.2): the leader AI can rush "any kind of city production" |
| Upgrading most units | a modder in https://forums.civfanatics.com/threads/ai-auto-producing-units-but-wont-upgrade.692035/ (search summary): it upgrades "occasionally, but has a habit of leaving most as the near useless initial unit" |
| Coming back from behind | Ision (92917): the AI is "almost always incapable of 'coming back from behind.'" |

---

## 4. Victory conditions (C3C defaults)

| Victory | Condition | Source / quote |
|---|---|---|
| Conquest | Defeat all other civs | CFC FAQ https://civfanatics.com/civ3/faq/: "defeat all other civilizations" |
| Domination | More than 66% of the land **and** more than 66% of the population (binary finding: "land % and population % both strictly above" the thresholds, Perez's decompile, `doc/perez-findings/Civ3_Findings.md` Round 2 "Victory conditions"; authoritative per the owner's 2026-11-12 ruling, superseding a reading of "66%" as "at least"). Ocean tiles don't count. Sea tiles count for score but not for domination. | BIQ-C3C GAME `DominationTerrain=66`, `DominationPopulation=66`. PEDIA-V: "Have 66% of the world's land surface within your borders as well as 66% of the world's population within your cities." MANUAL p.31: "conquering and controlling two-thirds of the world's territory". PEDIA-PTW and PEDIA-C3C say "70%" instead; the C3C rule file says 66, so the PTW/C3C text is out of date (or never matched). FAQ: "66% of the land AND population". BIQ GAME `DominationTerrain` / `DominationPopulation` (`Game.cs`). https://civfanatics.com/civ3/strategy/game-mechanics/maximizing-your-score/: "Ocean does not matter here, it is not included in your territory nor in the domination limit." and "Sea is important though – it counts toward increasing your territory but does not count towards the domination limit." |
| Space race | Build all 10 spaceship parts after the Apollo Program. The game is won at **launch**, which follows the tenth part; there is no flight time. Capturing (or razing) the builder's capital before launch destroys its ship, and it may then build a new one. Each civ builds one ship at a time. In the C3C BIQ, Apollo Program is a Small Wonder (`isSmallWonder: true`, 500 shields, `buildSpaceshipParts`, BIQ (repo)); RULE `NumberOfSpaceshipParts=10` (BIQ-C3C). | MANUAL p.174: "The competition ends when either you or one of your opponents launches a spaceship to Alpha Centauri with colonists. The civilization that wins the race to launch wins the game." and "You can construct a second spaceship only if your current one is destroyed--that is, if your capital city is captured while your ship is under construction (the conquerors destroy it on the launch pad)." PEDIA-C3C `#GCON_Victory`: "the player must be the first to launch a completed spaceship". README-V 1.17f: "Fixed bug which did not destroy an enemy spaceship if the enemy capital was razed." SCRIPT `#DESTROYEDSPACESHIP`: "We have destroyed the $CIVADJ0 spaceship!" DickK (https://codehappy.net/apolyton/threads/35851-1.htm, Nov 2001): "once you finish the last part you trigger the end game sequence whether you want it or not". FAQ: "build all 10 required spaceship parts" |
| Diplomatic (UN) | The UN builder is offered an election and may decline it every **10** turns (binary finding: "vote every 10 turns", Perez's decompile, Round 2 "Victory conditions"; authoritative per the owner's 2026-11-12 ruling; *superseded:* the FAQ's "every 11 turns"). Candidates: the UN builder, and any civ with **more than** 25% of the land **or** population (same binary finding: "more than 25% of land or population"; *superseded:* "at least 25%"); if only one is eligible, the most populous other civ joins. One vote per civ, and winning needs a majority of all votes; otherwise "Election was inconclusive". Details in `06-diplomacy-and-espionage.md` 6.12. | PEDIA-C3C: "There are always two, and sometimes three, candidates", "have built the UN", "control at least 25% of the world's territory", "control at least 25% of the world's population", "If only one civilization is eligible, then the second candidate is the civilization with the largest population." SCRIPT `#DIPLOVICTORYVOTEOPTION`: "Would you like to hold elections for UN Secretary-General?"; `#NODIPLOVICTORY`: "Election was inconclusive; no majority was found." FAQ: "a majority of the votes in the election"; "25% of the land OR population"; "every 11 turns" |
| Cultural (C3C) | One city with 20,000 culture, **or** a whole-civ total that depends on map size: 60k Tiny, 80k Small, 100k Standard, 130k Large, 160k Huge. The whole-civ total must also be at least twice any rival's; the one-city win has no such condition in the rule text. | PEDIA-V and PEDIA-PTW `#GCON_Victory`: "A city with a Cultural value of 20,000 or if your civilization is worth 100,000 and at least twice as much as any rival, you win." PEDIA-C3C drops the "twice" clause ("A single city with 20,000 culture points will satisfy this victory condition. For your overall culture to do so, a variable number of total culture points is needed, based on map size"), but C3C players still report it: Twonky, https://forums.civfanatics.com/threads/guide-to-cultural-victory.124277/ (Jul 2005): "Remember you need twice the culure of the next best civ in order to win."; keldr, https://forums.civfanatics.com/threads/100-k-culture-requirements.356991/ (2010): the values "depend on You having at least twice as much culture as #2". BIQ-C3C GAME `OneCityCultureWin=20000`, `AllCitiesCultureWin=100000` (the Standard figure). FAQ: "have either 20,000 culture in one city, or 100,000 culture across your empire"; in Conquests "60,000 for a Tiny map, 80,000 for Small, 100,000 for Standard, 130,000 for Large, and 160,000 for Huge." SirPleb (104294): "The required amount of culture for the "100K" cultural victory condition is variable in C3C, depending on the map size." BIQ GAME `OneCityCultureWin` / `AllCitiesCultureWin` (`Game.cs`). |
| Histographic (time) | Highest score at the end of the game (2050 AD on default timescale). The players are warned 20 turns before the last turn (binary finding: "a warning 20 turns before `g_LastTurn`, final result at `g_LastTurn`", Perez's decompile, `doc/perez-findings/Civ3_Findings.md` Round 2 "Victory conditions"; authoritative per the owner's 2026-11-12 ruling), in SCRIPT `#RETIREWARNING`'s words: "MANDATORY RETIREMENT YEAR! GAME WILL END AT $YEAR0". | PEDIA-C3C: "[Civilization III] runs until the year 2050."; "The civilization that generated the highest score -- on average -- wins. Being powerful in Ancient Times is just as valuable as being powerful in the Modern Era." SCRIPT `#TIMELIMIT`: "WINNER BY SCORE: $CIVNAME1". FAQ: "have the highest score at the end of the game" |
| Wonder victory (C3C, editor) | Build all wonders. It is off by default. It ends the game like a time limit: the game ends when the last Great Wonder is built and the winner is decided as at the time limit. | Thunderfall (104294): "Add Wonder Victory (all wonders built)"; repo `allowWonderVictory: false`; BIQ-C3C GAME `WonderVictory` off. README-C3C: "The "Wonder Victory" is actually a Time Limit type of victory condition; i.e., when the Wonder Victory is in effect, the game ends immediately upon completion of the last Great Wonder." |

C3C's F8 screen shows the domination limit and how far the leader is with its spaceship. Arathorn
(104294): "F8 now shows domination limit, spaceship parts completed by the leader, and a lot of other win
condition information."

- **Spaceship parts: one city per part.** README-V 1.16f: "Palaces and spaceship parts can only be built in one
  city at a time." Freiherr, https://codehappy.net/apolyton/threads/37264-1.htm (Dec 2001), on the wording: "two or
  more cities will no longer be allowed to build the same space ship part". PEDIA-C3C: "The components may be
  built in any order." Seeing rival parts needs a spy: README-V 1.17f: "Spaceship parts pop-ups now only appear if
  you have a spy with the corresponding civ."
- **Mass Regicide, Elimination, Regicide, Victory Points and Capture the Princess** are out of scope (header).
- **Respawn AI Players.** MANUAL p.31: "When this option is checked, computer-controlled civilizations that are
  eliminated early in the game are "reborn" as new civilizations." README-V 1.29f renamed "Restart Players" to
  "Respawn AI Players". War Academy, https://civfanatics.com/civ3/strategy/misc/strategy-for-emperor-and-deity-levels/:
  after an early wipeout "the computer will respawn that AI, with ADDITIONAL techs, and a new settler". GePap,
  https://codehappy.net/apolyton/threads/36170-1.htm (Nov 2001): "Killing them means destroying all cities and all
  settlers." The stock routine is `Leader_create_city_for_ai_respawn` (C3X-SRC). How "early" is defined is not sourced.
  Civinator (C3X-THREAD post-16707594, page-121 #2417) lists (paraphrase from a summarised fetch) "the normal C3C respawn logic": the civ gets
  "1 town, 3 defensive units, 1 offensive unit, 1 settler, 1 worker, 100 additional gold" plus its difficulty
  start units; there is "Only one respawn of each civ in a game" (post-16708435, page-122 #2422); and the
  option must be on when the game starts (post-16699411, page-120 #2396). He adds conditions on the site (its old
  continent, a tile without culture, no foreign unit or border within about 5 tiles; summary wording, not checked).
  Flintlock (post-16929609, page-174 #3467): "The reason AI players don't get respawned is that there's no
  space for them."
- **Elimination edge cases (stock bugs).** A civ whose last city is lost survives while it has a settler at sea (the
  "houseboat" bug): Civinator (post-16083900, page-10 #182) says it does not happen with a settler on land, and ynot56
  (post-16085984, page-11 #201): "settlers on land are eliminated when the last city of a civilization is
  taken." Flintlock (post-16084386, page-10 #183) found the related crash: it happens "whenever the civ's
  stored capital ID doesn't correspond to an actual city." Lord_Hill (post-16224617, page-30 #596): "if your
  first and only city is destroyed by a volcano, you automatically win by conquest!" These are bugs; the intended rule is
  elimination when a civ has no cities and no settlers (land or sea), and no win from losing your own last city.
- **Engine limits (code readings, C3X-THREAD, Flintlock).** "The 31 civ + barbs limit is baked very deeply into
  the game, it's even baked into the BIQ format" (post-16888798, page-159 #3163). "The unmodded game's limit of
  8192 units is arbitrary." (post-16606824, page-102 #2039). The turn limit is capped at 1000 (page-106 #2110). Cities: 512
  (`02-city-economy.md` §11). In hotseat play "all human players move before any AIs" (post-16384097, page-62
  #1222).
- **Retiring.** README-V 1.16f: "Retiring from a Conquest only game no longer is recorded as a victory."

---

## 5. Score

### 5.1 Per-turn score and final score

- **Per-turn score.** `(Territory + 2*HappyCitizens + ContentCitizens + Specialists) * Difficulty`
  - https://civfanatics.com/civ3/strategy/game-mechanics/maximizing-your-score/ gives this formula.
    Territory: "“Territory” is the number of tiles which are within your sphere of influence."
  - CFC FAQ (https://civfanatics.com/civ3/faq/) gives the same formula.
  - SirPleb, 20 Jan 2002: https://forums.civfanatics.com/threads/counting-final-points.14617 says
    "Multiply the total by the difficulty level." He counts 1 per tile, 2 per happy citizen and 1 per
    content citizen or specialist. He calls it his understanding from testing.
- **Final score is the average of the per-turn scores.** Maximizing-your-score: "The total of all your
  per-turn scores is divided by the number of turns played so far to get your actual game score." So
  late changes do little. col, https://forums.civfanatics.com/threads/increasing-final-score.37091:
  "Unlike Civ2 where your score is calculated at the end, this doesnt help much."
- **Things that don't count.**
  - Maximizing-your-score: "Wonders, cultural value, military size, science learned, etc. – none of these affect the score."
  - Bamspeedy (37091): "score doesn't take into account improvements, wonders, great leaders, diplomacy, etc."
  - Future techs are the one small exception. Maximizing-your-score:
    "it counts for so little in score that it is not worth devoting any energy to it."
- **Each future tech counts like one content citizen in every later turn (tested).** Bamspeedy,
  https://forums.civfanatics.com/threads/scoring-of-future-tech.31261 (2 Sep 2002, after 1.29f), with a save
  edited to start with every tech and Future Tech 1 learned in 2950 BC on Deity: "1 FUTURE TECH IS WORTH 1 CONTENT
  CITIZEN OR SPECIALIST!!". The histograph showed "(1/21)*6 = .3" and then "(2/22)*6 = .5", which is the same
  per-turn term averaged over the turns played. Cartouche Bee's formula, same thread, with his "*4" dropped:
  "InternalScore = [(Happy Citizens * 2) + Content Citizens + Specialists + ScoringTiles + (FutureTechs)] *
  difficulty". Aeson, https://codehappy.net/apolyton/threads/70419-1.htm (Dec 2002), only "17.7 points from 26
  future techs on Deity" (fetch summary), which fits a per-turn term earned late. MANUAL p.123: "Each Future
  Technology you discover adds to your final score". README-V 1.17f: "Fixed bug which miscalculated average scores
  (esp. for future techs)." This settles the former UNVERIFIED item.
- **Aeson's summary (vanilla), same 70419 thread:** "Everything is multiplied by the difficulty factor. 1-6,
  Chieftain-Deity."; "Population consists of 1 point for every specialist or content citizen in your civ, and 2
  points for every happy citizen."; "Territory is 1 point for every tile within your cultural borders."; "Adding up
  every turn score and then dividing by the number of turns played yeilds the base score."; the bonus is "(2050 -
  Date) * Difficulty" "for any victory condition".
- **Territory is cultural borders, not city radius.** chiefpaco, https://codehappy.net/apolyton/threads/43643-1.htm
  (28 Feb 2002, one Regent test): territory score is the average land area from F11 ("square miles ... divided by
  100") over the turns played, times difficulty. Technoron, same thread: cultural borders, not city radius,
  decide the land count. PEDIA-C3C `#GCON_Victory`: the histograph "looks at the total "Score" generated by all
  civilizations turn by turn over the course of the game."
- **Land and coast tiles; the average is rounded down (Hall of Fame staff).** Dianthus, explaining a 0-point
  score, https://forums.civfanatics.com/threads/lulthymes-0-point-histograph.114890 (22 Mar 2005, C3C era): "The
  score for each turn is based on the number of land/coast tiles in your territory" and "the number of
  happy/content citizens and the number of specialists"; "No points are gained for destroying a civilization."
  The player "finished in 2050AD" (no finish bonus) and "His average was above 0, but less than 0.5, so the score
  got rounded down to 0." LulThyme (same thread): "I am not sure how rounding is done for score."
- **Difficulty multiplier.** In C3C it is 1 to 8. SirPleb (68813): Conquests multipliers are "Chieftain:
  1, Warlord: 2, Regent: 3, Monarch: 4, Emperor: 5, Demigod: 6, Deity: 7, Sid: 8". Before Conquests they
  were "Chieftain: 1, ... Deity: 6".

### 5.2 Early-victory ("finish") bonus

- **The bonus.** A win before 2050 AD adds `(2050 - FinishYear) * Difficulty`, and BC years count as
  negative.
  - Maximizing-your-score: "If you win before 2050AD, you get a bonus which is added to your regular
    score." Its example is a Regent win in 310 BC: (2050-(-310))*3 = 7080.
  - SirPleb (14617): "The bonus is (2050 - CurrentDate) * difficulty." He says it is the same for every
    victory type.
  - Bamspeedy (37091): "the finish bonus which is only dependant on what year you win."
- **Based on year, not turns.** Because it is based on the year, earlier wins get a larger bonus. BC-era
  wins benefit most because BC years count as negative. The term "Jesus score" was not found in any
  source (see UNVERIFIED).

### 5.3 Demographics screen (community, PTW debug tests)

The demographics screen does not feed the score. Its rows were worked out by Bamspeedy, "How the Demographics
works", https://forums.civfanatics.com/threads/how-the-demographics-works.37033/ (19 Nov 2002, PTW, debug mode).
Quotes:

- Approval rating: "The percentage of your people that are happy." Bamspeedy later agreed (post #17, Dec 2002)
  that specialists are not counted as content.
- Population: "Add up all the population you get from the city view from all your cities."
- GNP: "Total gold in all your cities before corruption takes a bite out of it."
- Mfg. Goods: "Total unwasted shields in all your cities."
- Land Area: "# of tiles in your territory * 100."
- Literacy: "% of your citizens who live in a city with a library, university, AND research lab." Edit (post #10):
  "When you get the literature tech, your literacy goes from a minimum of 0% to 3%, and 3% is added to your literacy
  rate." Great Wonders with the scientific flag can also count for a city, though not Copernicus's (his edit,
  summarised).
- Disease: "% of the tiles in your territory that is floodplains or jungle."
- Life expectancy: "% of your citizens who live in a city with a granary, aqueduct, AND hospital", between 20
  and 99 (summarised).
- Family size: "The average amount of excess food that each city is producing/2."
- Military service: "10 years * # of military units / # of citizens."
- Pollution: the number of currently polluted tiles (post #10, summarised).

Not rechecked for C3C, which added Marsh as a disease terrain.

---

## Disputed

Items marked RESOLVED were settled in the deep pass of 2026-10-20 and are kept so the history can be traced.

1. **Which difficulty the AI uses for content citizens, quelling, barbarian bonus and corruption/OCN.
   LARGELY RESOLVED (1.2).** Each player has its own difficulty level (`Leader.player_difficulty`, C3X-SRC), and
   for the OCN percentage the code uses that per-player level (C3X-NOTES `ocn_formula.txt`). For the AI it is the
   default level, Regent in the stock rules (alexman 56739: "If you haven't changed that, it's Regent"; BIQ-C3C
   `DefaultDifficultyLevel=2`). That matches TheNiceOne (37490) and Bamspeedy's "default AI intelligence" (37490).
   Pyrkaige's Monarch (68642) is not supported; alexman answered it for C3C 1.0 (Apolyton 101437, 13 Nov 2003,
   1.2). Content citizens are settled by Soren Johnson (Apolyton 52411,
   1.2: "for the standard bic file, the AI always plays at Regent"). Military law is 1 at every level. Still
   open: a code or test confirmation for the barbarian bonus (TheNiceOne's 200% at Deity is a claim). Note the separate AI-only OCN term for Monarch and
   above (1.2), which is tied to the game's difficulty, not the AI's own level.
2. **Deity values. RESOLVED.** BIQ-C3C DIFF: Deity cost factor 6, OCN 60%, AI-AI trade 170; Demigod 7, 70%, 160.
   The 0.7 figure belongs to C3C Demigod. Vanilla/PTW Deity was 70% and 160 (Pyrkaige 37490, 68642; TimBentley 104294).
3. **How the cost factor applies to research. RESOLVED in `04-science-and-wonders.md` 4.1.** Since 1.29f the AI
   pays a constant (Regent) tech cost and the human's cost is scaled (alexman, Firaxis: "constant for all levels for
   the AI"). EDHELP still describes the factor as "applied to AI growth, shields, and research".
4. **Commercial. OCN PART RESOLVED.** The code adds floor(base/4) to the base OCN (C3X-NOTES), and Firaxis via
   alexman (76255): "increases the OCN by 25% (it was 10% prior to 1.29f)". Sullla's "12.5%" was a pre-1.29f
   corruption reading. The city-tile commerce amount is still open; see `02-city-economy.md` Disputed 11. Nor Me
   (76255, PTW 1.14f) gave "+2 commerce per city and +3 per metropolis" (fetch summary), against the FAQ's +1/+2.
5. **Industrious in C3C. RESOLVED** by KayEss's test (`07-map-terrain-and-workers.md` 5.2): +50% in C3C, +100% in
   vanilla and PTW. Slaves round down to no gain before Replaceable Parts, which explains stachnie's view.
6. **Agricultural under Despotism. RESOLVED.** C3X-INI (code): "the Despotism tile penalty does not apply to food
   yield from city tiles of Agricultural civs with fresh water." TimBentley's "on a river" is the river case of
   fresh water. Arathorn's "except despotism" is the case without fresh water.
7. **Seafaring city-tile commerce. RESOLVED: every coastal city.** PEDIA-C3C: "Each city built along the coast
   receives a commerce bonus in the center city square"; Dominae (AU501, 106962); Arathorn (104294). The
   StrategyWiki "capital" claim (never fetched) has no support.
8. **Favourite government.**
   - EDHELP: the civ "will immediately switch to this government type as soon as it becomes available", and avoids
     its shunned one "at all costs".
   - Code: the shunned government adds 5 attitude points (FLINTLOCK-WAR), so the setting affects attitude too.
     The government choice itself is a score (`Leader_ai_eval_government`) whose terms are not published.
   - chiefpaco and Propaganda (21298), wilbill (`05-governments.md`): it only affects attitude, and AIs "always
     choose Democracy" even when it is shunned. Not resolved: whether the "immediate switch" happens in play.
   - *C3X-THREAD (code reading):* Flintlock (post-16217016, page-30 #583): "Shunned/favorite government is
     worth 12.5%" of the government score. So in the code the setting is a weight in the score, not EDHELP's
     immediate switch. That is the stronger source and explains the players' "always choose Democracy".
9. **Build-often/never: how many flags are effective.**
   - Ozymandias (tested): more than 3-4 and the AI ignores them all.
   - Predator145, citing alexman second-hand: 6.
   - C0rTeZ48 recalls more than 2 Build Often having no effect.
   - The stock rules give every civ 1 to 5 flags (2.1 table), so the default civs sit under any of these limits
     except possibly the Byzantines (5).
10. **AI amphibious ability.**
    - Soren (255541) and the C3C notes say it can mount naval invasions and escorts transports. C3X-INI (code):
      transports get 1 to 3 escorts by cost and cargo.
    - Ision (92917) and several posters say its landings are too small to hold what they take. dexters (88537,
      PTW): "landing parties sail back and forth between targets and never are able to land".
11. **UN election interval.** CFC FAQ: every 11 turns. Jaybe, https://codehappy.net/apolyton/threads/93177-1.htm
    (29 Jul 2003), correcting a "20 turns IIRC" post: "11 turns, actually." Other recollections in
    https://codehappy.net/apolyton/threads/51960-1.htm (Jun 2002) say 4, 10, 20 or 40, all hedged. Lean: 11.
    **Resolved (2026-11-12): 10.** Perez's decompile of the Conquests executable
    (`doc/perez-findings/Civ3_Findings.md`, Round 2 "Victory conditions") has the vote "every 10 turns"; the
    owner ruled that this finding is authoritative. The FAQ's and Jaybe's 11 is superseded.
12. **AI city spacing.**
    - Dominae (57896): the AI follows about 4-tile spacing at all levels.
    - Brain (113953): the AI "often leaves big gaps". Vanilla posters (42759, 50345) say it fills every spot.
    - Code gives only the hard minimum (one empty tile) and a per-tile desirability score (3.3). Nobody measured
      the typical spacing.
13. **The build categories.** Soren (56961, vanilla): "Wealth = marketplaces/banks/workers, not just 'Wealth'".
    Predator145 (692300, C3C): Wealth is "the shield-to-gold project". Soren's answer is the developer's. C3C may
    differ.
14. **Domination percentage in the Civilopedia.** PEDIA-V says 66%. PEDIA-PTW and PEDIA-C3C say 70%. BIQ-C3C
    GAME stores 66 and 66, and the MANUAL says "two-thirds". Treat 66% as the C3C rule. The PTW figure is not
    checked against the PTW rule file.

15. **AI extra start units: at game start or at the first city?** (37490 page 2,
    https://forums.civfanatics.com/threads/ai-difficulty-level-bonuses.37490/page-2.) Spoonwood (21-22 Sep 2025,
    debug-mode tests on a scenario save at Demigod and Sid): "difficulty level isn't the only factor for the AIs to
    receive extra units"; extra units appeared only after an AI founded a city, and a 4000 BC retire screen showed
    equal power bars, "suggesting that AIs don't start in 4000 BC with extra units." justanick (22 Sep 2025): "When
    you start a regular game, a Sid AI will start immediately with all extra units it is supposed to have"; he
    guesses the scenario lacked starting positions. Pyrkaige (page 1, 2002): "At Monarch and above, the AI civs get
    extra offensive and defensive units when they start the game." *Largely resolved by code reading:* Flintlock
    (C3X-THREAD post-16708336, page-122 #2421): "AI players receive their set of free units from difficulty
    level whenever they found their "first" city", that is, "if the owner is an AI and their city count now equals 1,
    spawn in the free units." So the units come with the first city, as Spoonwood saw;
    on a random map the AI usually founds it on turn 1, which is what justanick and Pyrkaige saw. It also means an AI
    that loses all cities and re-founds one gets the units again (Flintlock's explanation of tjs282's report, #2418).

## UNVERIFIED / open questions

- **"Jesus score."** No source uses this term. The likely meaning is the year-based finish bonus, where
  BC years count as negative, so a BC win gets more than 2050 points × difficulty. Confirm what the
  requester meant.
- **Culture victory in C3C.** The twice-the-rival condition is in PEDIA-V and PEDIA-PTW and in C3C player
  reports, but not in PEDIA-C3C. Whether eliminated civs count as "the next best civ" is unanswered (Jivilov,
  356991).
- **Domination tiles.** Which water tiles count. Coast and sea count for territory (but Dianthus, 5.1, says the
  score counts "land/coast tiles"). Spoonwood (https://forums.civfanatics.com/threads/histograpgh-attempt-close-but-no-cigar.657407,
  Apr 2020): "sea squares do NOT count towards your domination limit." Aeson and SirPleb disagree
  about the domination share (43643), and Maximizing-your-score says sea is excluded from the limit.
- **Seafaring sinking.** The tested 50%/75% survival figures are in `01-combat-and-units.md`. There is no Firaxis number.
- **Commercial building discount.** BIQ-C3C flags Marketplace, Bank, Stock Exchange, Harbor, Airport and
  Commercial Dock as Commercial, but neither PEDIA-C3C nor the FAQ lists a Commercial building discount. Test it.
- **Industrious city-tile shield bonus.** The size thresholds are in `02-city-economy.md`, not re-sourced here.
- **Expansionist.** The exact goody-hut table shift is in `07-map-terrain-and-workers.md` (community tables).
  PEDIA-C3C's "passive minor barbarians are friendlier" is not quantified.
- **Settlers and workers.** How many escorts the AI gives settlers, and when. How the AI scores city sites
  (`Match_ai_eval_city_location`) and worker tiles. How it computes how many workers it "needs".
- **War.** The delay from plotting to declaring (guesses of about 4 turns), whether the target is the top score or
  a weighted pick, the exact cooldown after peace (Flintlock believes 8 turns), and how the unit AI uses aggression.
- **AI sliders.** Flintlock's reading (3.6): the stock AI uses the luxury slider too timidly. The code sets them each turn (`ai_adjust_sliders`, capped by the government rate cap). How it
  chooses the split is not published. Observed research rates were 30-70% (vanilla).
- **Difficulty fields for the AI.** Content citizens are Regent's (Soren, 1.2) and military law is the same at
  every level. The barbarian bonus still lacks a code reading or test.
- **When the AI's extra starting units appear.** See Disputed 15.
- **Golden-age trigger.** It depends on traits and wonder flags (EDHELP quoted in section 2). Leave it to the
  golden-age section.
