# 1. Combat and units

Scope: base game, Play the World (PTW) and Conquests (C3C), patched (1.29f / PTW 1.27f / C3C 1.22).
Scenarios, mods and the special C3C game modes (Regicide, Elimination, Capture the Princess, victory-point
games) are out of scope. C3C is the target ruleset. Where vanilla or PTW differ, the difference is called out.

## Sources used

Local files are from a Steam *Civilization III Complete* install (`.../Sid Meier's Civilization III Complete/`).
Quotes from local files are exact. Quotes from web pages were pulled out with a fetch tool that summarizes pages,
so short web quotes should be spot-checked against the page before anything leans on their exact wording.

| Key | Source |
|---|---|
| **PEDIA** | `Conquests/Text/Civilopedia.txt`, the C3C 1.22 in-game Civilopedia. Cited by entry key (`GCON_Combat`, `PRTO_Galley`, ...). |
| **PEDIA-V** / **PEDIA-PTW** | `Text/Civilopedia.txt` (vanilla 1.29f) and `civ3PTW/Text/Civilopedia.txt` (PTW 1.27f). |
| **EDHELP** | `Conquests/Civ3ConquestsEdit.chm`, the official C3C editor help (decompiled). Cited by page name. |
| **BIQ** | `Conquests/conquests.biq` (C3C 1.22 default rules), as imported into this repo's `C7/Lua/civ3/ruleset.json` by `ImportCiv3` (`_Console/RegenerateRuleset`). Only fields that come from the BIQ are used. |
| **README-V** | `README.txt`, the vanilla patch notes up to 1.29f, plus a "Changes" list of differences from the printed manual. |
| **README-PTW** | `civ3PTW/readme.txt` (PTW patch notes up to 1.27f). |
| **README-C3C** | `Conquests/readme.txt` (C3C 1.20 to 1.22). |
| **CFC-ARMY** | https://civfanatics.com/civ3/strategy/introductory/the-basics-of-armies/ (C3C only) |
| **CFC-LEAD** | https://civfanatics.com/civ3/strategy/military/creating-and-using-leaders/ |
| **CFC-SINK** | https://civfanatics.com/civ3/strategy/game-mechanics/sinking-chances-of-seafaring-and-non-seafaring-civilizations/ ("All tests done with Conquests, v1.22.") |
| **CFC-RULES** | https://forums.civfanatics.com/threads/editor-rules-explained.210035/ (Bluemofia's C3C editor field guide) |
| **CFC-CSE** | https://forums.civfanatics.com/threads/combat-system-explained.7679/ (Valant2, 2001-2002, vanilla) |
| **CFC-RET** | https://forums.civfanatics.com/threads/how-do-retreats-work.100878 and https://forums.civfanatics.com/threads/retreating-units.104978/ |
| **APO-ZOC** | https://apolyton.net/forum/civilization-series/civilization-iii/124182-zoc-defensive-bombardment (Jan 2004, includes **alexman, Firaxis**) |
| **CFC-RSTUDY** | https://forums.civfanatics.com/threads/comprehensive-retreat-study.64755/ (BomberEscort and others, Sep-Oct 2003, 3 pages, 11,274 trials; PTW 1.21f and vanilla 1.29f) |
| **APO-WALLS** | https://codehappy.net/apolyton/threads/84569-1.htm ("How do walls really work?", Apr-Jun 2003, PTW; alexman's tested posts. alexman later worked at Firaxis as a software engineer, which his profile shows; in 2003 he was a community tester.) |
| **CFC-AA** | https://forums.civfanatics.com/threads/aa-formula-discussed.76227/ (BomberEscort, Jan 2004, C3C; 500-trial tests, alexman agrees in #13) |
| **CFC-BOMB122** | https://forums.civfanatics.com/threads/bombardment-of-settlements-in-c3c-1-22.647207/ (justanick, 2019, reporting his 2013 tests on C3C 1.22; 2 pages) |
| **CFC-LCODE** | https://forums.civfanatics.com/threads/c3c-mechanics-birth-of-military-leader.526511 (Antal1987, May 2014, reading the C3C 1.22 executable: function `Process_Combat_Victory`) |
| **APO-1GL** | https://codehappy.net/apolyton/threads/50260-1.htm ("One leader per elite unit!", May 2002; alexman relays a rule "Mike B. (Firaxis) confirmed") |
| **C3X-INI** | https://github.com/maxpetul/C3X/blob/master/default.c3x_config.ini (the config file of Flintlock's C3X executable mod for C3C 1.22). Used only for its descriptions of stock 1.22 behaviour ("Under the standard game rules", "In the base game"), which come from reading the executable. The mod's own options are out of scope. |
| **C3X-THREAD** | https://forums.civfanatics.com/threads/c3x-exe-mod-including-bug-fixes-stack-bombard-and-much-more.666881/ (the C3X mod's forum thread, 192 pages, 2021-2026). Cited as "post-NNNNNNNN" (the post's URL is the thread URL plus `post-NNNNNNNN`) or "page-N #post-number". Flintlock, the C3X author, reads the 1.22 executable; his descriptions of what "the base game" or "the original game" does are code readings. Other posters' claims are play reports and may describe modded games. Only statements about the unmodded game are used. Quotes were re-checked against the posts; the few that could not be are marked "(paraphrase from a summarised fetch)". |
| **CFC-FAQ** | https://civfanatics.com/civ3/faq/ and its forum copy https://forums.civfanatics.com/threads/frequently-asked-questions-civ3-play-the-world-conquests.170282/ (Ginger_Ale, 2006, covers vanilla, PTW and C3C) |

Note: the repo file `C7/Civilopedia/Text/Civilopedia.txt` is OpenCiv3's own text ("It is not Civ3's
Civilopedia"), so it is **not** used as a source. The repo's ruleset also carries a `promotionChance` per
experience level; that field is **not** in the BIQ (the BIQ EXPR record holds only a name, base hit points and a
retreat bonus, see `QueryCiv3/BiqSections/Expr.cs`), so it is not a source either.

---

## 1.1 Combat resolution

- **Four kinds of combat.** Ground/naval combat, bombardment, missile attacks and air combat.
  PEDIA `GCON_Combat`: "There are four types of combat: ground/naval combat, bombardment, missile attacks, and air combat."
- **A battle is fought in rounds until one side has no hit points left; the loser dies, the winner may be damaged.**
  PEDIA `GCON_Combat`: "The two units trade blows until one of them loses its health bar and is destroyed.
  The winner may be damaged (its health bar is reduced)."
- **Each round costs the loser exactly 1 hit point. Civ3 has no firepower.** Every round removes one HP.
  - CFC-CSE (Valant2): "During battle each time the unit loses on a battle roll they loose 1 hp." and
    "If the unit wins the turn, it causes 1hp of damage to the opponent."
  - https://forum.freeciv.org/f/viewtopic.php?p=97232 (Akechi, from memory): "There is no Firepower status in Civ 3. all combat round deals 1 damage to each round loser."
  - Pre-release Firaxis statements on firepower are not available; the Apolyton thread
    https://apolyton.net/forum/miscellaneous/archives/civ3-general-archive/45717-probability-and-combat (Grunthex)
    says: "Uh, units have between 2 and 5 hp, not 10. Depending on their veterancy."
- **Per-round odds are A / (A + D)**, with A the attacker's modified attack and D the defender's modified defense.
  Community-derived, never confirmed by Firaxis in the sources found.
  - CFC-CSE (Valant2): "Crude Turn Win % = (attackers offensive rating)/ ((attackers offensive rating)+(defenders defensive rating))"
  - https://forums.civfanatics.com/threads/made-attacker-win-probability-page.7886/page-2 (Grendel): "the probability of the attacker winning one round (causing a HP loss in the defender) is a/(a+x)."
  - The same thread gives the whole-battle odds as a negative binomial: "(a / (a+x))^y * (x / (a+x))^z * nCr(y+z-1, y-1)", summed over z = 0 .. b-1 (b = attacker HP, y = defender HP, z = rounds the attacker loses).
  - alexman (APO-WALLS #25, Jun 2003, PTW), describing bombardment in terms of normal combat: "Bombardment works like
    normal combat, except that the odds are determined using the bombard strength" / "of the attacking unit, instead of
    its attack strength." / "The defender's strength and modifiers are the same in both cases."
  - Xerxes314's vanilla combat calculator (https://civfanatics.com/wp-content/uploads/2024/01/civ3combatcalc.html, readable
    JavaScript) uses the same model: no firepower, per-round odds from the two strengths, HP 2/3/4/5 and 3x that for armies.
- **The game stores round odds as an integer out of 1024 (C3C 1.22 code, via C3X).** C3X wraps the stock
  `Fighter_get_combat_odds` (address listed in https://github.com/maxpetul/C3X/blob/master/civ_prog_objects.csv) and
  converts its return value for its odds display as `1.0 - ((double)odds / 1024.0)`, the attacker's per-round win chance
  (https://github.com/maxpetul/C3X/blob/master/injected_code.c, `attacker_round_win_chance_from_fighter_odds`). So the
  stock function returns the defender's share of a round on a 0-1024 scale, and the mod forces a certain loss by
  returning 1025. The mod's display then sums the same negative binomial as above
  (`calc_attacker_combat_win_chance_from_round_chance`), one HP per round from the two units' current HP. For tile
  improvements under bombardment, the mod's re-implementation says it keeps "the game's integer rounding and 1..1023
  threshold": `defense = base_defense * (100 + terrain_bonus + building_bonus) / 100` (integer division), then
  `threshold = clamp(1, 1023, 1024 * defense / (defense + Bombard_Strength))`, one `rand_int(1024)` roll per point of
  rate of fire, any roll at or above the threshold succeeding (`get_tile_bombard_defense_threshold`,
  `patch_Fighter_roll_for_bombard`). Read together: strengths are integer-truncated after percentage bonuses that are
  added together, and no single roll is ever certain (1/1024 at the extremes). Only the tile-bombard path is shown
  in full; that unit-vs-unit odds use the same truncation and clamp is an inference.
- **C3C beta "less streaky" combat change: announced, but not in the shipped patch notes.** Tavis's patch notice
  (https://forums.civfanatics.com/threads/civ-iii-conquests-patch-notice.71474/, #1, 9 Dec 2003, beta 1.10): "Modified
  combat calculations to make combat appear less "streaky."" and (#13) "The Change was having the random number generator
  (rng) calculate the Results 4 times instead of 1." Tall Stranger (https://codehappy.net/apolyton/threads/107356-1.htm,
  #29, Jan 2004, second-hand): "this 4 roll combat change was never added to the C3C patch." README-C3C (1.20 to 1.22)
  lists no combat-calculation change. Treat 1.22 combat as the single-roll model above.
- **Bombard odds use the same ratio, with bombard strength in place of attack.** EDHELP's own example shows a 50%
  chance at equal values. EDHELP *Building (Defensive Bonuses)*: "a unit with a bombard strength of 4 would have a
  50% chance of destroying a building with a defense rating of 4."
  https://forums.civfanatics.com/threads/bombard-question.19648 (dunk): a bombard-8 cannon against a defense-3 cavalry has "an 8 out of 11 chance of hurting the cavalry."
- **Random seed is saved with the game** (reloading replays the same results) unless the rule is turned off.
  README-V 1.21f: "Added Preserve Random Seed rule. Turning this off will allow random seeds to be changed when a saved game is reloaded (which makes combat different...)."
  Dan Magaha (Firaxis), https://forums.civfanatics.com/threads/as-promised-a-civ-iii-combat-test-map.15208/: "the game was designed so that save + reload wouldn't be a very effective or efficient way to cheat"
- **Combat arithmetic is integer, and there is one simple random generator (code reading).** Flintlock (C3X-THREAD
  post-16902126, page-166 #3309, C3C 1.22): corruption and combat "are all integers under the hood", and
  "the only places I recall seeing floating point ops in game logic are for worker strength and the science age
  bonus". C3X-THREAD page-165 #3294 (Flintlock): "Its main, and probably only, random number generator
  is a basic linear congruential generator". This fits the 1024-scale integer odds above.
- **Defender choice: the unit with the highest defense value defends.** EDHELP *Stealth Attack Barrier (Improvement
  Flag)* describes a normal attack as hitting "the defending unit currently with the highest defense value". Players
  found that current HP also counts, so a healthy weaker unit can defend before a badly damaged stronger one:
  - https://apolyton.net/forum/miscellaneous/archives/civ3-strategy-archive/83449-why-do-offensive-units-defend-before-defensive-ones
    (Dec 2002): Dominae, "The game will always select the unit in a stack that has the best chances at winning a combat
    on defense." / "This is a combination of Defense value and HPs."; IthacaMike, "I've always figured that it was HPs
    times Defense."; theNiceOne, "But notice that factors like fortifying counts."
  - DeepO (https://codehappy.net/apolyton/threads/59461-1.htm, Aug 2002): "defense goes to that unit who has the
    highest combination of HPs and Defense stat".
  - Ties (community, untested): IthacaMike, "In case of ties the defender seems to be the unit with the lowest Attack.";
    bongo, "In ties the weakest attacker will defend."; Dominae and theNiceOne guess the cheapest unit.
  - Ships (Soren Johnson, Firaxis, second-hand via Jaybe in thread 83449): "if there are two ships of same defensive
    strength (e.g., two transports), the one withOUT cargo will defend."
  - The exact weighting of defense against HP is UNVERIFIED (no test or code reading found).
  - C3X-INI (code reading) names two keys before the tie-breaks, "the same effective defense strength and the same
    king priority", and says shield cost is not one of the stock tie-breaks (its option to "prefer the unit with the
    lower shield cost before applying the base game's remaining tie-breakers" is a mod change). King units "always
    [are] the last to defend in a stack".
- **Units without attack can't attack; units without defense are captured, not fought** (see 1.13).
- **Stealth attack lets the attacker pick its target in a stack** (C3C). PEDIA `GCON_Stealth_Attack`: "Stealth
  attack allows a unit to specify which enemy unit -- in a stack -- it will attack." Submarines have it
  (PEDIA `PRTO_Submarine`: "Submarines have $LINK<Stealth Attack=GCON_Stealth_Attack>").
  - **Stock 1.22 offers the choice only when the stack holds at least two eligible targets (code reading).** With
    one eligible target the attack goes against the normal defender. Flintlock (C3X-THREAD post-16043842, page-3
    #41) describes his fix of the stock check: "So instead I implemented a more basic fix for this issue, just
    patching a check for "> 1" into ">= 1"." (the choice now appears with a single eligible target).
    Civinator (post-16853888, page-148 #2955): "In normal C3C stealth attack can only be performed if
    there are at least two valid stealth attack targets in a stack." Flintlock (post-16378096, page-60 #1195):
    "In the base game, a stealth attack won't trigger when Snake attacks Sam Hammer alone on a tile".
    The players treat it as a flaw; whether Firaxis meant it is not stated. The AI never uses the ability:
    Flintlock (post-16853988, page-148 #2959): "I don't see anywhere that the unit AI considers stealth
    attack at all."
- **Collateral damage** (C3C ability). EDHELP *Create Craters and Collateral Damage*: if the defender "takes damage,
  then: If the defending unit is not in a city, the artillery/bombard value of the attacking unit is used to conduct
  a bombard attack on that space to determine if any terrain improvements are also destroyed. If the defending unit is
  in a city, the the artillery/bombard value of the attacking unit is used to conduct a bombard attack on that city".
- **Craters** (C3C). PEDIA `GCON_Craters`: "When a bombardment attack which eliminates the last unit or terrain
  improvement in a square, a crater may be formed. Like pollution, the presence of craters reduces a square's
  production, commerce, and food output by one."
- **"Starts Golden Age" units.** EDHELP *Unit Abilities*: "If victorious in combat, this unit triggers a golden age".

### Healing

- **1 HP per turn, only in friendly or neutral territory, only if the unit does not move.**
  PEDIA `GCON_Combat`: "adding one $LINK<block=GCON_Experience> per turn back to its health bar. To do so, it
  ^* must be in friendly or neutral territory. ^* must not move for a complete turn."
- **Full heal in one turn:** ground units in a city with Barracks, ships with a Harbor, air units with an Airport.
  PEDIA `GCON_Combat`: "A city with a $LINK<barracks=BLDG_Barracks> heals ground units completely in one turn." (same wording for Harbor and Airport).
- **Battlefield Medicine** (small wonder, needs 5 Hospitals) allows healing in enemy territory.
  PEDIA `BLDG_Battlefield_Medicine`: "Allows military units to heal in enemy territory. ^{Requires:} The civilization must have at least five hospitals".
- **A city without a Barracks heals 2 HP per turn.** BIQ heal-rate table: `friendly_field 1, neutral_field 1,
  hostile_field 0, city 2`. CFC-FAQ (Ginger_Ale): "If the unit is in a city, but it has no barracks, it will heal 2 Hit
  Points per turn;". Theoden (https://forums.civfanatics.com/threads/question-concerning-healing-units.113864/, #10, 2005):
  "I can confirm. Unts will always heal 2pts in cities." Quintillus
  (https://forums.civfanatics.com/threads/fortification.266551/, #7, 2008): "Military units inside cities without
  barracks/harbor/airport heal at 2 damage per turn." Whether fortifying matters is disputed in thread 113864 (shadow2k:
  1 unfortified, 2 fortified; k-a-bob: "Fortification has no effect."); the BIQ has no such field. Arexander
  (C3X-THREAD post-17000888, page-192 #3826) restates it: "By default units in cities heal two HP per turn".
  Flintlock (post-16714924, page-122 #2426) adds that one stock function sets every unit's healing rate, and that
  the game processes every unit at the start of each turn, healing them and sinking galleys at sea (post-16644827,
  page-110 #2200, paraphrase).
- **Upgrading a unit heals it fully** (community). Pyrrhos (thread 266551, #9): "Now there remains only one, rarely used,
  way to heal even redlined troops in one turn to mention - to upgrade them." massa (thread 113864, #3), on the
  no-move rule for healing: "Exceptions are upgrades and promotions."
- **A human's fortified unit wakes once fully healed (binary finding).** Perez's decompile of the Conquests
  executable (`doc/perez-findings/Civ3_Findings.md`, Round 2b "Units", `Unit_HealPerTurn`): "A fully healed fortified
  unit of a human player wakes up." The owner ruled on 2026-11-12 that this finding is authoritative. AI units stay
  fortified.
- **Ships heal only in port; in C3C their passengers heal at sea** (community). The Professor (thread 266551, #2):
  "Ships can only heal in port". PaperBeetle (same thread, #11, C3C): passengers heal aboard; "To get this healing, you
  need to be in friendly or neutral waters." UNVERIFIED by a test.

---

## 1.2 Experience and hit points

- **Hit points by experience level.** BIQ EXPR and PEDIA agree.

  | Level | HP (blocks) | Retreat bonus (BIQ) |
  |---|---|---|
  | Conscript | 2 | 34 |
  | Regular | 3 | 50 |
  | Veteran | 4 | 58 |
  | Elite | 5 | 66 |

  PEDIA `GCON_Experience`: "Conscripts 2 ^Regulars 3 ^Veterans 4 ^Elite units 5".
- **Unit HP bonus** (editor, since 1.21f): added to the level's base HP, from -20 to +20, never below 1 HP. All stock
  units have 0 (UNVERIFIED for every unit; the BIQ `hpBonus` field is 0 for the units checked).
  EDHELP *HP Bonus*: "This bonus is added to the base hitpoint value (determined by the unit's experience level) ...
  the unit's total number of HP will never drop below 1."
- **What produces each level.** Cities build regulars; a Barracks (Harbor for ships, Airport for planes) builds
  veterans; draft and barbarian-hut units are conscripts; elite only comes from combat.
  PEDIA `GCON_Experience`: "Normally, cities produce regulars. But if a city has a $LINK<barracks=BLDG_Barracks>, it
  produces [veterans]. ... [Elite units] cannot be built: they gain this status only through actual combat experience."
  PEDIA `BLDG_Harbor` / `BLDG_Airport`: "produce $LINK<veteran=GCON_Experience> naval units" / "air units instead of regulars."
  Sun Tzu's Art of War gives a Barracks in every city on its continent (PEDIA `BLDG_Art_of_War`: "Puts a $LINK<barracks=BLDG_Barracks> in every city on the same continent.").
- **Battlefield promotion after a win.** PEDIA `GCON_Experience`: "When one of your units is victorious in battle,
  there is a chance it will gain combat experience and be promoted to the next level." The chances are not in the
  Civilopedia or BIQ. Community figures:

  | Promotion | Non-militaristic | Militaristic |
  |---|---|---|
  | Conscript to Regular | 1/2 | doubled |
  | Regular to Veteran | 1/4 | 1/2 |
  | Veteran to Elite | 1/8 | 1/4 |

  - https://forums.civfanatics.com/threads/barbarians-gaining-experience.115547 (Theoden, #18): odds roughly 1 in 2,
    1 in 4 and 1 in 8, "Double the chances for militaristic civs and halve them when fighting barbarians."
  - https://forums.civfanatics.com/threads/heroic-epic-militaristic-civs.26455 (Catt, #2): "A veteran militaristic
    unit has a 1 in 4 chance of being promoted to elite after a victorious battle against an AI civ." and "A veteran
    non-militaristic unit has a 1 in 8 chance." Catt (#6) credits this to a Soren Johnson post that is not reproduced.
  - PEDIA `GCON_Strengths` (Militaristic): "combat $LINK<experience=GCON_Experience> is gained more quickly."
  - CFC-FAQ (Ginger_Ale) gives the full table, saying the chance "depends on 3 things" (current level, Militaristic,
    barbarian opponent): non-militaristic 1 in 2 / 1 in 4 / 1 in 8, halved against barbarians (1 in 4 / 1 in 8 / 1 in
    16); militaristic 1 in 1 / 1 in 2 / 1 in 4, and 1 in 2 / 1 in 4 / 1 in 8 against barbarians. So a militaristic
    conscript that wins against a civ always becomes a regular. No source for the table is given.
  - **The C3C 1.22 code confirms the table** (CFC-LCODE, Antal1987, #8, `class_Unit::Process_Combat_Victory`, re-read
    in the second pass). The ratio is set by the winner's level: "_Comb_Exp_Promote_Ratio = 2;" for experience 0, 4 for
    1 and 8 for 2. Against barbarians (the loser's civ ID is 0): "if ( !Target_Unit->Body.CivID )
    _Comb_Exp_Promote_Ratio *= 2;". Then a race check, "if ( BIC_Data.Races[Civilizations[_this->Body.CivID].RaceID]
    .vtable->CheckBonus(" ... "0) )", halves it: "_Comb_Exp_Promote_Ratio /= 2;". The roll is "if ( _this->Body.Status
    & 2 || !class_Random::Get_Random(&Random_1, _Comb_Exp_Promote_Ratio) )", so the chance is 1/ratio.
    Antal1987 concluded "Promotion of unit's combat experience doesn't depends on Civ having Militaristic flag", but
    he did not identify the bonus being checked. In the C3X mod's header for the same executable
    (https://github.com/maxpetul/C3X/blob/master/Civ3Conquests.h) the race method is `byte (__fastcall * CheckBonus)
    (Race *, __, enum RaceBonuses);` and `RB_Militaristic = 0x0`. So `CheckBonus(race, 0)` is the Militaristic test,
    and the code gives exactly the CFC-FAQ table: conscript 1/2, regular 1/4, veteran 1/8; doubled for Militaristic
    (1/1, 1/2, 1/4); halved against barbarians. What the `Status & 2` bit is (a forced promotion) is not explained; it
    might be what lies behind watorrey's claim below, but that is a guess.
  - watorrey (https://codehappy.net/apolyton/threads/57625-1.htm, 2002, opinion): "any unit winning 2 battles in one
    turn is automatically promoted". No test; UNVERIFIED.
- **Experience is only the named level** (code reading). Flintlock (C3X-THREAD page-170 #3399): "the game
  doesn't track experience outside of conscript, regular, etc." and "some unit stats like fire rate are properties of
  unit types not the units themselves".
- **Free defenders given with a captured or flipped city keep the default level (stock bug).** Flintlock
  (post-16624151, page-107 #2125): "the game simply never sets the experience level on the free
  defenders". ynot56 (post-16681754, page-116 #2317) saw a city taken by culture or a spy give a regular unit although
  it had a Barracks; Flintlock calls that a bug. The intended rule is presumably the city's normal build level (veteran
  with a Barracks); UNVERIFIED.
- **Barbarians do not promote** (C3C 1.22 test). Same thread 115547 (Theoden, #14, C3C 1.22): "all dying without any of the mech infs gaining any experience." This is about barbarian units gaining levels, not about your units promoting while fighting barbarians (see Disputed).
- **A damaged unit keeps its level.** Experience is the level, not the current HP. A veteran at 2/4 HP is still a
  veteran for retreat and promotion. Apolyton (Ethelred) https://apolyton.net/forum/miscellaneous/archives/civ3-general-archive/75784-fast-units-no-longer-retreating-at-low-health: "The editor's retreat setting is for the experience level not the hit points."
- **A unit makes at most one leader, until it is upgraded. Units that made a leader are marked.** README-V 1.29f:
  "Added an '*' next to experience level name for units that have produced a leader." The rule, relayed by alexman as
  something "Mike B. (Firaxis) confirmed" (APO-1GL #1, May 2002), quoted by nato (#11): "Each unit can produce at most
  one great leader." / "Each time a unit is upgraded, however, it can once again produce a great leader." The C3C code
  skips the leader roll for a unit that is "already extra elite" (CFC-LCODE #1). An upgraded elite drops to veteran
  (see 1.14), so it must be promoted again before it can make a second leader
  (https://codehappy.net/apolyton/threads/90368-1.htm, CerberusIV: "if you later upgrade the unit it goes back to
  being veteran and can again be promoted to Elite").

---

## 1.3 Great Leaders (military)

- **A leader can appear when an elite unit wins a battle.** PEDIA `GCON_Leaders`: "A {Military leader} is born in
  combat. This [can] occur when an $LINK<elite unit=GCON_Experience> wins a battle."
- **Chance per elite win: 1/16; 1/12 with the Heroic Epic; half that when the win is on defense.** Not in the BIQ or
  Civilopedia. The C3C 1.22 executable confirms it (CFC-LCODE, Antal1987, #1): "Military leader appears if random(0,
  Ratio) == 0. That means the odds = 1 / Ratio." / "Ratio = 4 \* (Small\_Wonder\_Count <= 0) + 12, where
  Small\_Wonder\_Count is count of Small Wonders" / "with flag "Increases\_Chance\_Of\_Leader\_Appearance"" / "If the
  combat is defence, Ratio doubles". That gives 1/16 attacking, 1/12 attacking with the Heroic Epic, 1/32 and 1/24 on
  defense (#7: "Minimum is 1 / 32, when you don't have required small wonder and a unit is in defense."). The 4 and 12
  are hard-coded (#8). Community figures agree:
  - CFC-LEAD: "Each time one of your elite units wins a fight against a rival, there is a 1 in 16 chance that a leader will appear." / "your chance of getting a leader improves to 1 in 12 when you win a fight with an elite." / "The chances of getting a leader from a successful defense are 1/2 the chances while attacking."
  - https://apolyton.net/forum/miscellaneous/archives/civ3-play-the-world/80836-how-a-leader-is-born (Catt): "The chance of a leader generation is 1/12, 1/16, 1/24, or 1/32, depending on circumstances".
  - PEDIA `BLDG_Epic`: "Increases the chances of the appearance of $LINK<leaders=GCON_Leaders> from victorious combat."
- **When no leader can appear (C3C 1.22 code).** CFC-LCODE (Antal1987, #1) lists the cases that skip the roll: "if the
  unit is not elite" / "if it is naval or air" / "if it kills barbarian unit" / "if it is already extra elite" / "if
  it is inside an army" / "if current battle created unit count > 0; Default battle created unit type is "Leader"".
  So in C3C **elite ships and planes never make leaders**. Moosezilla (#14) recalls a leader from a naval battle in
  older PTW games; Puppeteer (#15) answers that PTW and C3C differ. Whether naval leaders were possible before C3C is
  UNVERIFIED.
- **No leaders from fighting barbarians.** CFC-LEAD: "You can not get a leader by fighting a barbarian." Confirmed by the
  code list above ("if it kills barbarian unit").
- **The militaristic trait does not change leader odds** (it only raises promotion odds). Thread 26455 (Catt): "The
  odds of leader generation for ALL civs, militaristic or not, is 1/16 without the Heroic Epic". CFC-LCODE (#8): "It
  seems, that there is no checking for militaristic flags of civilizations."
- **Armies do not generate leaders.** Thread 80836 (Catt): "Armies cannot generate leaders." Killing an army can
  (Theseus, same thread: "You can, however, generate a GL by an elite killing an Army."). The C3C code skips the roll
  for a unit "inside an army" (CFC-LCODE).
- **Only one military leader alive at a time.** CFC-LEAD: "You can only have one leader at a time." The rule relayed
  from Firaxis in APO-1GL (#11): "You can have more than one great leader per game but only one at a time." In the C3C
  code the roll is skipped while the civ owns any unit of the Battle-Created Unit type (CFC-LCODE: "if current battle
  created unit count > 0"). CFC-LEAD's "You can however produce more than one leader in one game turn" fits this only if
  the first leader was used (made an army or hurried) before the next elite win; that reading is an inference.
  C3X-THREAD agrees: Erlend Philip Wood (post-16934723, page-175 #3488, testing): "It seems there is zero
  chance to get another battle-created unit if you already have one!"; Flintlock (post-16935268, #3491) names "the
  blocker that's stopping battle created units from being spawned for players that already have one".
- **Only a win over a unit with a defence value can make a leader** (community, consistent with the code's "elite
  unit wins a battle"). Spoonwood (C3X-THREAD post-16307796, page-43 #854): "One can only get a military
  great leader in the classic game by an elite defeating a unit with defense value." and "Leader fishing on AI
  artillery is impossible." (Capturing a defenceless unit is not a battle.)
- **Leader unit.** BIQ: Leader costs 0, A/D 0/0, movement 3. The unit the editor spawns is the "Battle Created Unit"
  (CFC-RULES: "Determines which unit is spawned when an elite unit wins a battle."; BIQ `battleCreatedUnit: Leader`).
  A leader cannot fight and is captured or destroyed if caught alone (see 1.13).
- **Uses (C3C): make an army, or finish an improvement or small wonder in the city it is in. It cannot hurry a Great Wonder.**
  PEDIA `GCON_Leaders`: "A Military Leader can be used to either create an $LINK<army=PRTO_Army> or to
  $LINK<hurry=GCON_Hurry_Production> production of an Improvement or Small Wonder in a city. When a military leader
  does either of these actions he disappears."
- **Vanilla and PTW: leaders hurry anything, including Great Wonders.** PEDIA-V / PEDIA-PTW `GCON_Leaders`: "Leaders
  can be used to either create an $LINK<army=GCON_Armies> or to $LINK<hurry=GCON_Hurry_Production> production in a city."
  https://forums.civfanatics.com/threads/what-is-a-leader-and-how-do-i-get-one.21132 (Fez_Monk): "Using the leader is
  the only way of hurrying a wonder."
- **C3C scientific leaders** (from being first to a tech) can hurry Great Wonders. They belong with science, not
  combat. PEDIA `GCON_Leaders`: "A Scientific Leader can also be used to $LINK<hurry=GCON_Hurry_Production> the
  construction of any [improvement] (including a Great Wonder)".
- **A unit that makes a leader can be renamed.** README-V: "Added the ability to rename units if they produce a great leader during combat."

---

## 1.4 Armies

- **Capacity 3 units, 4 with the Pentagon. The Pentagon needs 3 armies in the field.**
  PEDIA `BLDG_Pentagon`: "[The Pentagon] increases the unit capacity of all the civilization's armies from three to
  four. ^{Requires:} The civilization must have at least three $LINK<armies=GCON_Armies> in the field." (BIQ Army `capacity 3`.)
- **Built by a leader in any city, or built (400 shields in every version) in the city with the Military Academy.**
  PEDIA `GCON_Armies`: "Armies can be built in cities that have the $LINK<Military Academy=BLDG_Military_Academy>
  or a $LINK<Leader=GCON_Leaders> in them." BIQ: Army `shieldCost 400`.
  The CivFanatics FAQ v3.0 (https://civfanatics.com/civ3/strategy/misc/civilization-iii-faqstrategy-guide-v3-0/)
  says "you can use 200 shields to construct an army". The vanilla and PTW rule files don't support that: a raw read of
  the decompressed `civ3mod.bic` (1.29f) and `civ3PTW/civ3X.bix` (PTW 1.27f), at the C3C PRTO field offsets, gives Army
  cost 40 in both, and those files store unit costs in tens (Settler 3, Warrior 1, Spearman 2, Tank 10, against C3C's
  30, 10, 20, 100). So the Army is 400 shields in all three versions (see Disputed 4).
- **At most one army per 4 cities.** PEDIA `GCON_Armies`: "you must have at least four cities for every army you
  create." EDHELP *Cities Needed to Support an Army*: "Default is 4 cities." CFC-ARMY: dropping below the ratio keeps
  existing armies but blocks new ones.
- **Military Academy and Heroic Epic both need a victorious army first.** PEDIA `BLDG_Military_Academy`: "The city with
  the [Military Academy] can build $LINK<armies=GCON_Armies> without the benefit of a $LINK<leader=GCON_Leaders>.
  Armies from this civilization are more effective in combat. ^{Requires:} The civilization must have had a
  victorious $LINK<army=GCON_Armies>." PEDIA `BLDG_Epic` has the same requirement.
- **Units in an army pool their hit points; the whole army must be killed to kill any unit in it.** README-V
  ("Changes" from the manual): "Armies now literally share the hit points of the units within it. For example, an
  army of 3 regular spearman would have hit points equal to 9. To kill any unit in the army, the entire army must be
  eliminated."
- **Loading.** Units can be loaded or unloaded only while the army is still in the city that made it; after it leaves,
  the contents are fixed. README-V: "Once that army leaves the city of it's construction, you cannot add or remove
  units from the army." BIQ `allowUnloadFromArmy: false`. Units in an army cannot be upgraded (CFC-ARMY: "Units in
  armies cannot be upgraded."). Only units with both attack and defense can be loaded (CFC-ARMY: "Only combat units
  (those with an attack and defense) can be loaded into an army.").
- **Abilities.** Armies have zone of control and blitz (vanilla 1.17f on), movement = slowest unit + 1 (C3C),
  pillage (PTW 1.04f on), and see 2 tiles.
  - README-V 1.17f: "Armies now have the zone of control and blitz ability."
  - README-C3C: "an Army unit also has the "Blitz" capability (it can attack twice in the same turn), its movement
    rate has been increased by one, and units in an Army heal faster than they would if they were not within the Army unit."
  - README-V: "The only unit abilities that an army can inherit is mobility, and only when all units in that army have that ability."
  - README-PTW 1.04f: "Added the Pillage ability to Armies."
  - PEDIA `GCON_Armies`: "An army can "see" up to two squares away, due to its intrinsic scouts."
  - CFC-ARMY: "When no units have yet loaded into the army, the army will only have a movement of one."
    C3X-INI calls this a stock bug: "Empty armies have one less than a full move's worth of movement points regardless
    of how many moves their unit type has set in the editor. Under the standard game rules, this means they can only
    move two tiles along roads." (Two road tiles cost 2/3 of a move, so "one less than a full move" leaves under one MP.)
  - AI-only army bugs (C3X-INI): "the AI's ability to form armies with leader units is not gated by those units having
    the "build army" special ability", and a "major bug that prevented the AI from filling its armies".
  - Pillage (C3C): CFC-FAQ: "Armies in Conquests can also pillage without using up any movement points." TimBentley
    (thread 113864, #13): "Pillaging counts as a move for units other than armies."
- **Army combat strength (C3C).** The fighting unit gets a share of the others' strength:
  `Attack = A + (TA / N)`, N = 4 with the Military Academy, else 6, rounded down. Defense is the same with defense values.
  CFC-ARMY: "A is the attack of the unit currently doing the fighting." / "TA is the attacks of all the units added
  together." / "N is 4 if you own the military academy, else just 6." / "This formula is rounding fractions down."
  TA includes the fighting unit: Oystein's worked example in the article's forum thread
  (https://forums.civfanatics.com/threads/the-basics-of-armies.99688/, #15) sums the whole army, and he adds that terrain
  bonuses apply to the army's full improved defense. Arathorn (quoted by Puppeteer in
  https://forums.civfanatics.com/threads/weak-armies-in-vanilla-and-ptw.489054/, #2): "The bonus to A is equal to the sum
  of the A values of all units in the army divided by 6 and rounded down". The Firaxis editor help describes the
  Military Academy's flag differently (see Disputed: EDHELP says +25%).
- **Vanilla and PTW armies get no strength bonus.** Puppeteer (thread 489054, #2): "Before Conquests an army was just a
  group of units that fought with collective hit points." / "A longbow army would attack with strength 4."
- **Armies get normal defensive bonuses.** alexman (https://codehappy.net/apolyton/threads/58980-1.htm, #11, 2002):
  "Yes, the defender bonus applies to whomever is not the attacker."
- **Which unit fights.** PEDIA `GCON_Armies`: "An army uses one, some, or all its internal units to fight a battle,
  depending upon the course of the fight." CFC-CSE (Valant2, vanilla): "When an army attacks or defends the first unit
  continues to fight/defend until the unit is down to 1hp." CFC-ARMY (Theoden): "the strongest unit in the army will
  begin then descending down and ending with the weakest unit." A tested account (Theseus,
  https://codehappy.net/apolyton/threads/59461-1.htm, Aug 2002): the remaining HP "are divided between all units at the
  start of each battle event", and the strongest unit for the job fights with its share before handing off.
  Code (third round): the stock routine is `Unit_select_army_member_for_combat (army, hp_remaining, attacking)`, and it
  takes the army's remaining HP as input. C3X's comments on it (https://github.com/maxpetul/C3X/blob/master/injected_code.c):
  "the base function preserves the army's HP-band rotation" and "Keep the base selector's HP allocation and
  tie-breaking". That is Theseus's model: the fighter is chosen by bands of the pooled HP, not by one unit fighting to
  1 HP. When an army dies, its contents always die too ("If the unit has the army ability, the game will always despawn
  the passengers", `patch_Unit_has_army_ability_for_passenger_despawn`).
- **Units in an army can be promoted, but only the one doing the fighting** (community). Tomoyo (thread 99688, #6);
  Bagatur (#5): "units in armies can get promotion with blitz."
- **Army healing.** CFC-ARMY: "Armies heal at the rate of one HP per unit in the army." and "armies are also able to
  heal in one turn in a city containing barracks." CFC-ARMY also says armies heal in enemy territory without
  Battlefield Medicine ("This can also be done in enemy territory even before battlefield medicine."). Arathorn (via
  thread 489054, C3C): "Armies also heal in enemy territory (one hp/unit pre-Battlefield Medicine and two hp/unit
  after)". Vanilla was slower: Alan (thread 99688, #14, vanilla): "only one unit per turn heals, even in a barracks".
- **Army retreat.** Whether armies can withdraw is disputed (see Disputed).
- **Code readings on armies (C3X-THREAD, Flintlock, C3C 1.22).**
  - Loading needs only matching unit classes (post-16512698, page-79 #1575): "the condition for armies is
    only that the two units must have matching classes." (CFC-ARMY's "attack and defense" rule above is the stock
    Army unit's own limit; how the two combine is not stated.)
  - The game keeps a passenger count only for armies (post-16893099, page-161 #3205): the game keeps no general passenger count, and "For whatever reason, they added a counter only
    for armies." Loaded army members are handled like units in a transport (post-16607583, page-103 #2053).
  - Armies can board ships, and take more room than a single unit (post-16838218, page-142 #2832:
    (paraphrase from a summarised fetch) "armies take up more space in transports than single units"). tjs282 (post-16826417, page-137 #2737):
    "Armies can be loaded onto ships because they _aren't_ transport-units as such".
  - Pillaging costs an army no movement (post-16401798, page-64 #1270): the code "checks whether the unit has the
    army ability" and then skips the movement-point deduction. This confirms CFC-FAQ's "without using up any
    movement points".
  - The player record holds a single army count (post-16587093, page-95 #1897): "the game only stores a
    single army count per player".

---

## 1.5 Retreat (withdrawal)

- **Barbarians never retreat from an attack (binary finding).** Perez's decompile
  (`doc/perez-findings/Civ3_Findings.md`, 3.2 "Combat"): "Retreat (only the attacker, never barbarians)"; the owner
  ruled on 2026-11-12 that this is authoritative. The engine still lets a barbarian defender withdraw by the rules
  below (that part of the finding is not applied).
- **Fast units withdraw when losing, unless the opponent is also fast.** PEDIA `PRTO_Horseman`: "move fast and will
  [withdraw] from combat if losing (unless fighting another fast unit)." (`PRTO_Cavalry` has the same wording.)
- **Since 1.17f, withdrawal is a die roll that depends on experience.** README-V 1.17f: "Mobile units now have to make
  a die roll to determine if they withdraw. Success is also modified by their experience level." Before that, fast
  units always withdrew (thread 100878, The Last Conformist: "In Original Release Vanilla, units always retreated if able.").
- **Retreat bonus by level (BIQ, editable since 1.21f):** conscript 34, regular 50, veteran 58, elite 66.
  README-V 1.21f: "Editor: Added Retreat Bonus controls to Combat Experience page." CFC-RULES: "Determines the ratio
  of retreating when a fast unit is fighting a slow unit." EDHELP *Retreat Bonus*: "The higher the number listed for
  the retreat bonus, the more likely it is that the unit will successfully retreat when losing a fight."
- **"Fast"** means more movement points than the opponent. CFC-CSE (Valant2): retreat requires higher movement and
  unused movement points. Jaybe (https://codehappy.net/apolyton/threads/47749-1.htm, #2, Apr 2002, before 1.21f): "If
  both units have more than 1MP, there is NO retreat." He adds "The movement point and hit point rules are official
  (either documentation or Firaxian)" but cites neither. On that reading "fast" is base movement above 1, and two units
  with 2+ MP never retreat from each other. Steph (C3C modder, from memory,
  https://forums.civfanatics.com/threads/useful-patches-for-civ-3-conquests-v1-22.526285/page-3 #57) says the same: "If
  both units have more than 1 MP, then retreat never happens, even if one unit is faster", and (#60) "A unit with 10 MP
  has the same chance to withdraw than a unit with 2 MP, if they have the same experience." Whether a 3-MP unit can
  retreat from a 2-MP unit is still untested (two independent "no" recollections). C3X-INI (code reading) points the
  same way. Among its alternatives to "standard: No change from base game rules" it lists "all-units: All non-immobile
  units may retreat, even if they have only one move or are up against a faster opponent" and "if-faster: Units may
  retreat when they are faster than their opponent, for example a cavalry may retreat from a knight". Offering
  cavalry-from-knight as a change implies the stock rule does not allow it. It does not spell out the stock rule.
  C3X-THREAD posters state it outright, as a description of the stock rule the mod's options change: WildWeazel
  (post-16306973, page-43 #845): "Default, 2+ can retreat from 1"; Shmelkin (post-16207917, page-28
  #543): "In original game fast unit can retreat only against slow unit which is hardcoded to have 1
  movement". So a 3-MP unit does not retreat from a 2-MP unit (community, consistent with Jaybe, Steph and
  C3X-INI). Flintlock adds an oddity of the stock check (post-16747059, page-129 #2564): "Oddly,
  immobile units can retreat in the base game as long as they have more than one move."
- **Cases with no retreat at all** (CFC-RSTUDY, BomberEscort, #19, PTW): "Retreat is not possible (no matter the
  experience of the unit); these instances are:" / "(1) When the fast-mover has only 1 HP left (at the beginning of the
  attack)" / "(2) When both units are fast-movers" / "(3) When there is no available tile for retreat" / "(4) When the
  fast-mover is fortified in a city (or colony?)" / "(5) When the Defender has 1hp remaining." Item 4 matches an older
  vanilla report (Kriton, https://codehappy.net/apolyton/threads/35589-1.htm: "if your cavalry/fast unit is fortified,
  it will not disengage.").
  C3X-INI (C3C 1.22 code reading) confirms items 1 and 4 in a stricter form and adds water: "units cannot retreat when
  they start combat with 1 HP or are defending a city", and "Under the base game rules, no unit may retreat defensively
  if it's located on a water tile." So any defender in a city is excluded, fortified or not, and ships never retreat
  when attacked.
- **Bombardment never triggers retreat.** BomberEscort (CFC-RSTUDY, #33): "Retreating does not apply to bombardment,
  since bombardment is a free attack for the bombarding unit..." (A unit damaged by defensive bombard can still retreat
  later in the fight, #35.)
- **When the roll happens (community-tested):** when the losing fast unit is about to drop to its last HP, the opponent
  has 2 or more HP, and the fast unit has movement left.
  - CFC-RET 104978 (Offa, #9): "Retreat possible if attacking unit drops from 2 HP to 1 HP, and is implemented before
    the attacker attacks with 1HP left." / "Zero chance if defender has 1 HP, or fast defender." / "Only one chance per turn to retreat, barring odd circumstances."
  - CFC-RET 100878 (boogaboo): the roll applies only if the opponent has 2HP or more, the retreating unit has 1HP, and
    it would have at least 1/3 movement point left after the attack.
  - A retreating unit survives with 1 HP and does not move (IbnSina, 100878: "Retreat does not require any special ground - no movement actually takes place.").
- **Retreat formula (confirmed from the C3C 1.22 code):** P(retreat) = attacker retreat bonus / (defender retreat
  bonus + 50). Antal1987 read the check in the 1.22 executable (`class_Combat_Controller::process_Combat`,
  https://forums.civfanatics.com/threads/useful-patches-for-civ-3-conquests-v1-22.526285/page-4, #62): "Cavalry
  retreats only if random(Hoplite.Retreat_Bonus + 50) < Cavalry.Retreat_Bonus." His code line compares
  `Get_Random(BIC_Data.CombatExperience[...Defence_Unit...Combat_Experience].Retreat_Bonus + 50)` with the attack unit's
  `Retreat_Bonus`, so both units' experience levels enter (CyberChrist, #67: "the code already makes use of both
  defender and attacker Exp. Lvls"). If `Get_Random(n)` returns 0..n-1 (assumed, not stated), regular vs regular is
  50/100 and an attacker bonus of (defender bonus + 50) or more always retreats. The thread does not show what checks
  run before this roll (movement, HP), so the measured cap near 66 below is not explained by it. He also found the RNG state is stored
  in the save, so reloading repeats the result (#62: "the game stores in save-file current value of a randomizer"),
  and that the check "toggles only after 1-st round complete". The community form came first:
  CFC-RET 104978 (Offa): "prob of retreat = (attacker retreat bonus) / (defender retreat bonus + 50)", described as
  "thought to be". BomberEscort (quoted in 100878): "(100*Attacker Retreat Bonus)/(50+Defense Retreat Bonus)" and
  "When the Attackers retreat value is 50 percentage points greater than the Defender, the Attacker always retreats."
  **Flintlock confirms the roll (C3X-THREAD post-16308752, page-43 #857, code reading):** in "the original game" the unit trying to retreat
  "rolls a number between 0 and its opponent's retreat bonus + 50"; it retreats if the roll is "less than its own retreat bonus". His test
  (post-16308991, page-44 #863), "attacking 40 infantry with 40 1-HP horsemen": (paraphrase from a summarised fetch) "None of them retreated,
  39 died, 1 won." That confirms the 1-HP exclusion below.
  Measured success ranges for fast attackers in valid cases (same post): "elite fast-attacker is 55-75% retreat";
  veteran 50-66%; regular 43-56%; conscript 29-38%.
  The original study is CFC-RSTUDY (11,274 trials; the formula is Oystein's, #44: "Maybe the formula is 100a/(d+50)").
  It fits most results but not all (the 80-vs-80 case measured 56.75% against 61.54% predicted). Further findings
  there:
  - The slow unit's own retreat bonus matters. BomberEscort (#51): "The slow-Defenders retreat bonus does factor into the
    ability of the fast-Attacker to retreat." / "There is no way to create a slow-unit that will always block a
    fast-units retreat." Oystein (#23, vanilla 1.29f): "Seems like defenders retreat bouns decreases the chance for the
    attacker if above 50."
  - Edited bonuses above 66 seem capped. BomberEscort (#26): "Retreat bonuses 50 or under seem to work. Anything higher
    that 66% seems to get capped at 66%." Krayzeenbk (#22): "the 100% retreat horsemen continued to fight in 29.69% of
    cases". This conflicts with the "always retreats" line above, which came from edited values; UNVERIFIED.
  - The overall chance that a fast attacker retreats in a battle is the chance it reaches 1 HP first (with the opponent
    on 2+ HP) times the retreat roll (Krayzeenbk, #22: "multiply that by his actual chance to retreat").
- **Defending fast units can also withdraw** when attacked by a slower unit, using the same formula. CFC-RET 100878
  (boogaboo): the retreat bonus applies "both to retreating when failing to attack and to retreaing when failing to
  defend." BomberEscort (CFC-RSTUDY, #51): "They seem to use the same formula for slow-attacker v. fast-defender...".
  A defender needs a free tile to retreat into (case 3 above; Krayzeenbk, #22: "this is really only a problem when the
  fast unit is being attacked"). Offa's "Zero chance if defender has 1 HP, or fast defender" is best read as "against a
  fast opponent".

---

## 1.6 Defense modifiers

- **Terrain bonuses (C3C).** PEDIA `GCON_Terrain_Combat` and BIQ TERR agree:

  | Terrain | Defense | Move cost (BIQ) | Wheeled can enter (BIQ) |
  |---|---|---|---|
  | Grassland, Plains, Desert, Floodplain, Tundra | +10% | 1 | yes |
  | Coast, Sea, Ocean (and Fresh Water Lake) | +10% | 1 | n/a |
  | Marsh (C3C) | +20% | 2 | no |
  | Forest | +25% | 2 | yes |
  | Jungle | +25% | 3 | no |
  | Hills | +50% | 2 | yes |
  | Volcano (C3C) | +80% | 3 | no |
  | Mountains | +100% | 3 | no |

  PEDIA `GCON_Terrain_Combat`: "^Marsh 20% ^River 25%* ^Forest 25% ^Jungles 25% ^Hills 50% ^Volcano 80% ^Mountains 100%".
  Vanilla and PTW have no Marsh or Volcano rows (PEDIA-V and PEDIA-PTW tables stop at the 14 terrain rows plus River).
- **River: +25% for a defender attacked across a river.** PEDIA `GCON_Terrain_Combat`: "* This bonus applies to the
  defender if attacked from the other side of a river." BIQ/EDHELP *River*: "Default is 25."
  Code (third round, C3X-SRC `counter_attack_crosses_river`): "Match the base game: only the defender tile's river edge
  toward the attacker counts for the river defense bonus". A river elsewhere on the defender's tile gives nothing.
- **Structural bonuses (C3C).** PEDIA `GCON_Terrain_Combat`:

  | Source | Bonus |
  |---|---|
  | Fortified | +25% |
  | Fortress | +50% |
  | Barricade (C3C) | +100% |
  | Town Walls (town only) | +50% |
  | Civil Defense | +50% |
  | City (size 7-12) | +50% |
  | Metropolis (size 13+) | +100% |

  "^Fortified Units +25% ^Fortress +50% ^Barricade +100% ^Town Walls +50% ^Civil Defense +50% ^City +50% ^Metro +100%".
  EDHELP gives the defaults for the General-page fields: *Town*: "Default is 0 (no bonus)." *City*: "Default is 50."
  *Metropolis*: "Default is 100." *Fortifications*: "Default is 25." *Fortress*: "Default is 50."
  City size levels (BIQ): town up to 6, city up to 12 (`maximumLevel1CitySize 6`, `maximumLevel2CitySize 12`).
  Vanilla's table has no Civil Defense row (PEDIA-V); PTW adds it (PEDIA-PTW).
- **Bonuses add up (they are not multiplied).** EDHELP *Fortifications*: "Note that this bonus is cumulative with
  other terrain and fortification-related defensive bonuses." EDHELP *Defense Bonus (Improvement Properties)*: "This
  bonus is cumulative with other improvement and general defense bonuses". CFC-CSE (Valant2): "Bonus percentages add,
  so 2 50% bonuses result in a 100% total bonus." Xerxes314's calculator code adds terrain, fortress or city size,
  river and fortify and then multiplies once: `defbase*(100.0+defbonus)/100`. WarpStorm
  (https://codehappy.net/apolyton/threads/71511-1.htm, Dec 2002, PTW): "You add the bonuses first". See Disputed for
  the multiplicative view.
- **Walls: +50% in towns only, and land bombard defense 8.** PEDIA `BLDG_Walls`: "[Walls] can be built only in
  town-size settlements and provide a land bombardment defense of eight, and a 50% defensive bonus to units defending
  in the town. When a town becomes a city, the walls no longer provide a defensive bonus." BIQ: Walls `combatDefenseBonus 0.5`, `bombardDefense 8`, flag `canOnlyBeBuiltInTowns`.
  Walls do not add to the size 7+ bonus (Mike B., Firaxis, quoted by Catt in
  https://apolyton.net/forum/miscellaneous/archives/civ3-strategy-archive/70027-question-about-great-wall: "It has no
  effect on Cities or Metropolises (since walls are ineffective for these pop. levels)."). README-PTW: "Fixed bug which
  caused walls not to be obsoleted by growing from a town to a city."
- **"Walls" means any improvement with a bombard defense.** EDHELP *Notes & Tips*: "Any Improvements or Wonders that
  have a defensive value against bombardment (> 0) is considered walled." alexman (APO-WALLS #10, PTW) explains the PTW
  bug fixed above: "In vanilla civ3, walls were defined as any improvement with either a non-zero defensive bonus," "or a
  non-zero bombard defense." / "Firaxis decided to change the definition of walls to be any improvement with a non-zero
  bombard defense." / "Unfortunately, they forgot to make all other properties besides the bombard defense" "(such as
  the defensive bonus) of such improvements become obsolete after size 6." / "Note that the bombard defense still
  becomes negated after size 6, even in PTW."
- **The wall bonus also applies against air attack; the wall's bombard defense does not.** alexman (APO-WALLS #1): "When
  you attack a city with a ground unit (or a marine), the defender gets the defensive bonus of the wall." / "When you
  attack a city with an air unit, the bombard defense of walls do not apply, but the defensive bonus does."
- **Civil Defense: +50% at any size.** PEDIA `BLDG_Civil_Defense`: "[Civil Defense] can be built in any size settlement
  and provides a 50% defensive bonus to units defending in the town. Civil Defense can only be built in a city that has
  a barracks." README-C3C: "Civil Defense requires Electronics".
- **Building defense bonuses don't add up: only the best one counts** (code re-implementation). C3X's
  `optimize_improvement_loops` option, described as rewriting stock loops "to improve efficiency", replaces the stock
  `City_get_building_defense_bonus` with a loop that keeps the largest `multiplier * improv->Combat_Defence` over the
  city's active defensive buildings, `if (building_defense > tr) tr = building_defense;`
  (https://github.com/maxpetul/C3X/blob/master/injected_code.c, `patch_City_get_building_defense_bonus`). Buildings
  with a bombard defense ("walls") count only while the city is at town size
  (`Population.Size <= MaximumSize_Town`), and their bonus is doubled (`multiplier = 2`) when the owner has a wonder
  with the "Doubles City Defenses" flag. So in C3C a town with both Walls and Civil Defense gets +50%, not +100%, and
  at size 7+ only Civil Defense counts. A raw read of the rule files shows which wonder has that flag: the vanilla and
  PTW Great Wall has both "Doubles City Defenses" and "Double Combat vs Barbarians"; the C3C Great Wall has only the
  barbarian flag (and gives free walls instead). That matches the two Civilopedias below.
  Flintlock describes the same flag from the code (C3X-THREAD post-16676988, page-115 #2296): "That flag
  doubles the defense strength of any improvement with a bombard combat value > 0" and "Level 2 & 3
  cities can't benefit from that effect since they don't get any defense bonus from walls." It is empire-wide
  (post-16677107, #2299: "it's global as in it applies to all cities in your empire") and touches only the
  defence bonus, not the bombard defence (post-16677715, page-116 #2303: "It only doubles the regular
  defense stat."). Predator145 (post-16677223, #2301) agrees that in C3C the flag is unused by the Great Wall.
- **Great Wall.** C3C: a free Wall in every city on its continent, and double combat values against barbarians.
  PEDIA `BLDG_Great_Wall`: "Doubles unit combat values versus $LINK<Barbarians=GCON_Barbarians>. ^Places a
  $LINK<Wall=BLDG_Walls> in each city of the continent on which it is built."
  Vanilla/PTW: doubles walls instead. PEDIA-V `BLDG_Great_Wall`: "The [Great Wall] doubles the effects of
  $LINK<Walls=BLDG_Walls> in cities that have them." Mike B. (Firaxis, via thread 70027): "The Great Wall doubles the
  defensive value of Walls in all Towns that have them."
- **Cities in resistance give no defense bonus.** PEDIA `GCON_Terrain_Combat`: "Cities with resisters do not give defensive bonuses."
- **Terrain under a city still counts.** CFC-CSE (Xerxes314): "Terrain under a city does affect its defense value."
  The printed manual (p. 110, quoted by royfurr in thread 7886): "Terrain bonuses are figured in BEFORE THE CITY SIZE
  AND WALLS TAKE EFFECT."
- **Fortify needs movement points left (binary finding).** Perez's decompile (`doc/perez-findings/Civ3_Findings.md`,
  3.2 "Combat"): "the fortified bonus requires the unit to be fortified *and have movement points remaining*"; the owner
  ruled on 2026-11-12 that this is authoritative. A unit that fortifies after spending its moves gets no bonus until
  its moves are restored at the start of the next turn.
- **Fortify.** +25%; fortify, fortress and walls stack per EDHELP's "cumulative" wording. Some players believed the
  wall or fortress replaces fortify (https://forums.civfanatics.com/threads/defense-bonuses-question.21715). See
  Disputed. Which units cannot fortify is UNVERIFIED. Sentry gives the same bonus, per two C3X-THREAD posters
  (community): Arexander (post-16670295, page-114 #2278): sentried units "get the same defensive boost as
  with fortifying"; Erlend Philip Wood (post-16670390, #2280): "if both options are checked (like all
  vanilla units), the bonus is applied." Fortifying a unit that has no moves left is a stock bug: Flintlock
  (post-16645129, page-111 #2204): "The code for fortify all doesn't ask for state 34 directly, that's just what it gets by
  trying to fortify exhausted units." / "It simply resets the unit to idle." He is not sure what state 34 is.
  (Intended: the unit fortifies.)
- **Fortress.** +50%, gives zone of control to units inside. PEDIA `GCON_Fortresses`: "They get a 50% defensive bonus
  if attacked. ^* They have a $LINK<zone of control=GCON_ZOC> and fire at passing enemy units without receiving return
  fire." Built after Construction (same entry).
- **Barricade (C3C).** Built on a fortress, +100%, stops enemy ground movement into the tile. PEDIA `GCON_Barricades`:
  "barricades automatically stops movement of a ground unit entering its square (unless that unit belongs to the same
  civilization currently controlling the barricade's square, or if that unit has a $LINK<Right of Passage=GCON_Agreements>
  agreement through that land." / "Barricades become a fortress if their square is pillaged." CFC-FAQ: a barricade
  "provides 100% bonus to defense as well as uses up all of a unit's movement points."
- **Radar Tower (C3C): +25% attack and defense to friendly units within 2 tiles, once.** PEDIA `GCON_Radar_Towers`:
  "The range of the effected area is 2 tiles. Friendly units within the Radar Towers range get a +25% attack and
  defense bonus. This bonus can only be applied once, even if multiple towers overlap."
- **Coastal Fortress: naval bombard defense 8, and it bombards passing enemy ships. It gives no defense bonus to land
  units.** PEDIA `BLDG_Coastal_Fortress`: "A [Coastal Fortress] gives the city a naval bombardment defense of eight.
  ^The fortress automatically bombards passing enemy ships." README-PTW: "Fixed Civilopedia entry for Coastal Fortress
  by removing reference to, "50% defense bonus from naval attacks". There are no direct naval attacks on land units --
  only bombardment." (The BIQ still holds a naval defense bonus of 50 for it, which per this note has no target.)
- **Attacker bonuses.** Terrain gives no attack bonus (CFC-CSE: "terrain does not give any offensive bonuses what so
  ever"). The only attack modifiers found are the Radar Tower and the bonus against barbarians.
- **Rounding of modified strengths.** Unknown. https://forums.civfanatics.com/threads/civ3-combat-calculator.454569/
  (vmxa): "I think it is rounded down, after all modifiers are applied." Xerxes314 (CFC-CSE): "I don't know why
  anybody would think that there's rounding involved at any stage of the calculation." UNVERIFIED.

### Versus barbarians

- **Attack bonus against barbarians, by difficulty** (editor DIFF field, added in 1.16f). README-V 1.16f: "Editor:
  Added Barbarian Combat Bonus to difficulty level tab." Values from
  https://forums.civfanatics.com/threads/ai-difficulty-level-bonuses.37490/ (Pyrkaige, read from the PTW/1.29f editor):
  "Attack bonus against barbarians: 800 400 200 100 50 0" for Chieftain, Warlord, Regent, Monarch, Emperor, Deity.
  Arathorn's later post in the same thread gives Demigod 25 and Sid 0 (C3C levels).

  | Chieftain | Warlord | Regent | Monarch | Emperor | Deity | Demigod | Sid |
  |---|---|---|---|---|---|---|---|
  | +800% | +400% | +200% | +100% | +50% | 0 | +25%? | 0 |

  EDHELP *Attack Bonus Against Barbarians*: "Determines the attack bonus enjoyed by any player (human or AI) against
  barbarians." Whether it is attack only or also defense is UNVERIFIED (the field name says attack). Xerxes314's vanilla
  calculator applies it both when attacking and when defending against barbarians, with smaller values ("Chieftan" 400,
  Warlord 200, Regent 100, Monarch 50, Emperor 25, Deity 0; its changelog says "Version 1.1 - added correct bonuses for
  Barbarians at multiple difficulty levels"). That is probably the pre-1.16f vanilla default, before the field became
  editable; UNVERIFIED.
- **Great Wall doubles combat values against barbarians** (all versions; quoted above). The editor help for the
  underlying wonder flag is narrower. EDHELP *Miscellaneous Effect Flags (Wonder Properties)*: "Double Combat Strength
  vs Barbarians: Doubles defense value vs Barbarians of city that the wonder resides in". See Disputed.

---

## 1.7 Bombardment

- **Bombard stats: strength, range, rate of fire.** EDHELP *Rate of Fire*: "Rate of fire defines how many times a
  bombarding unit will attack per turn." CFC-RULES: "Determines the number of times the unit initiates a bombard check
  when bombarding. (0-10)". EDHELP *Bombard Range*: "how many squares away a unit may bombard cities or units."
- **Non-lethal bombard can't kill: it stops at 1 HP.** PEDIA `GCON_Combat`: "Bombardment inflicts losses to the
  defender's health bar but never completely destroys them." CFC-RULES: lethal bombard lets a unit "kill units when
  bombarding, and to bombard units with 1 HP."
- **A failed bombard roll hurts nobody; the bombarding unit never takes damage.** freeciv thread (Akechi): "If attacker
  lose round during this combat, attacker bombard unit does not receive damage." https://forums.civfanatics.com/threads/artillery-civ3-vs-civ4.145967 (search excerpt): a bombard "does not expose the artillery to harm."
- **Each hit removes 1 HP; a bombard is RoF separate rolls against the target's modified defense.** CFC-CSE (Valant2):
  "I assume all successful bombarbments on a military units only results in 1hp lost." Thread 19648 (warpstorm): "You
  fire twice. Each shot is tallied up individually." alexman (APO-WALLS #25, PTW): "If the attacker wins, the defender
  gets one HP of damage. If the attacker loses, nothing happens." / "Bombardment consists of as many rounds of combat as
  the ROF value of the attacking unit." / "So each bomardment can result in between zero and ROF hit points of damage."
  Against a unit the defense used is "the modified defensive strength of the defending unit (which always includes any
  wall and terrain bonus)" (#1). BomberEscort's calculator thread
  (https://forums.civfanatics.com/threads/civ-iii-combat-calculator-exe.75765/, #19) gives the chance of no damage as
  "(BombardAttack / (BombardAttack + ModifiedDefense))^RateOfFire" (he means the defender's share; his example is
  (6/10)^3 = 0.216).
- **Bombarding a city can damage a unit, kill 1 citizen, or destroy a building.** Thread 19648 (Park Ranger) lists
  "destroy an improvement," "kill 1 population point," and "damage a defending unit"; and "bombarding will not kill
  citizens if the city is only size 1."
- **Which target a city bombard hits depends on the version.**
  - Vanilla (early 2002): one third each. Whoha (https://codehappy.net/apolyton/threads/44680-1.htm, citing korn469's
    tests): "it does a random check for what to target", a 1/3 chance each for population, building or unit, then
    "another random chance with the defense of the pop point/building/unit to determine if it actually hits that."
  - PTW (alexman, APO-WALLS, 2003): #25 "When you target a city, you have a 50% chance of targetting the top unit," / "a
    25% chance of targetting population, and a 25% chance of targetting improvements." / "If during an attack an
    improvement is targetted but the city has no improvements, it's an automatic miss." / "Similarly, if population is
    tagretted but the city is of size 1, it's a also a sure miss."
  - C3C 1.22 (CFC-BOMB122, justanick, #1): a fixed priority order by attacker type. Land artillery: "City walls are hit
    first." / "If there is are no city walls or the settlement has already reached city level, then land units are
    hit." / "If neither city walls nor land units are left to be hit, then citizens or buildings are hit," / "apparently
    with same probability." / "Ships and air units cannot be hit with land based artillery." Ships: "A coastal fortress
    is hit first." then ships, then air units, then land units, then citizens or buildings. Air units: "If air units are
    left in the settlement, then air units or citizens are hit." / "If no air units are left in the settlement, then
    ships, citizens or buildings are hit." then land units, citizens or buildings. Cruise missile: "Ships are hits
    first." then air units, land units, citizens or buildings. "Left to be hit" means units that can still lose HP: a
    unit at 1 HP no longer counts against non-lethal bombard (PaperBeetle,
    https://forums.civfanatics.com/threads/what-makes-bombardment-fail.307328/, #5, C3C: "units are always targetted
    while they still have hp left to lose."). Lanzelot (page 2) suggests the order may be a strong bias rather than a
    fixed rule ("city walls are hit with 95% probability"). C3X-INI describes the stock C3C rule as fixed: "PTW-like
    means there is a 1-in-3 chance they will target each population, buildings, and units. This is unlike Conquests
    artillery targeting, which always goes after units first." (It agrees with justanick that units come before
    citizens and buildings; it says nothing about walls.)
  - C3X-THREAD on the same point. Flintlock, C3C code (post-16393188, page-63 #1247): "the normal bombard
    method prioritizes damaging units." and (post-16311320, page-44 #878): "The bombard code is inflexible
    in that it rolls first to determine a target then rolls for damage." Predator145 (post-16262382, page-32 #637,
    community): "In C3C artillery is "smart" and only targets units until all of them have been
    redlined", air bombard "can lethally affect air units and will smartly target them first", and
    cruise missiles "will always target sea units first". These agree with justanick's order.
  - **PTW: three accounts disagree.** alexman's test (above) gives 50% the top unit, 25% population, 25% buildings.
    Predator145 (same post): "In vanilla and PTW, artillery units target cities the same way air bombard
    does" with "a 1/3 chance to either hit the units, the improvements or the population"; BlueSox14
    (post-16523719, page-81 #1620), summarising Sullla: "In PTW, bombardment would have a random 1/3
    chance of striking either units, population or buildings."; Flintlock (post-16369990, page-56 #1116): PTW
    artillery "had a 2/3 chance of attacking buildings or pop in a city instead of the garrisoning units"
    (a recollection; he reads the C3C executable, not PTW's). alexman's figures come from a test, so they are the
    stronger source for PTW. The vanilla 1/3 split (korn469's tests, above) is not in doubt.
  - Aircraft in an airfield can't be damaged by bombardment, and they shield the tile. C3X-INI: "The game rules do not
    permit aircraft in an airfield to be damaged by bombardment", and "if you bombard a tile with a road, airfield, and
    fighter on it, you will do no damage because the attack will target the fighter which is invulnerable".
  - Tile improvements have a fixed bombard defense. C3X-INI: "In the base game, tile defense is hardcoded at 16." The
    Citizen and Building values in the editor are "base strength values", not bonuses.
    Flintlock gives the tile roll in words (C3X-THREAD post-16584231, page-95 #1889): "the chance to do
    damage on each roll equals 1 - X/(S + X)", with S the bombard strength and X = "floor((100 + D) * 16 / 100)", D
    the tile's total defence in %; (paraphrase from a summarised fetch) "The attacker gets to roll multiple times up to its rate of fire and
    if it succeeds on any roll then the tile is damaged." The defence (post-16583488, #1884): "The chance
    to do damage depends only on the tile's terrain, whether it has a fortress or barricade" / (paraphrase from a summarised fetch) "I don't
    see any references to the building or citizen defense bonuses." This matches the integer form in 1.1.
  - **Land artillery can't damage aircraft; ships can kill them on land** (code reading). Flintlock (C3X-THREAD
    post-16021251, page-1 #10): "land artillery can't damage aircraft" and "naval artillery
    can not only damage but kill aircraft on land". This agrees with justanick's order (land artillery never hits
    ships or air units; ships hit air units). Flintlock also mentions a stock "weird little bombard rule
    that limits what land artillery can damage" (post-16507213, page-78 #1558), probably the same rule.
- **Civil Defense absorbs building hits (C3C).** CFC-BOMB122 (#1): "If a regular building(meaning neither a coastal
  fortress nor city walls) is hit and a civil defence is in the settlement," / "then it is the civil defence that is hit."
- **Building and citizen defense against bombard: 16** (since 1.17f; BIQ 16). README-V 1.17f: "Building and citizen
  defense against bombardment increased to 16." BIQ `buildingDefensiveBonus 16`, `citizenDefensiveBonus 16`.
  alexman (APO-WALLS #25, PTW): "Population and improvements are treated like a unit with a defensive strength of 16."
  Steph (https://forums.civfanatics.com/threads/defensive-artillery-in-city.329600/, #17): "if it's 16 and the
  bombardment strength is 8, then the bombardment has 33% to destroy the building." EDHELP still says "Default is 4"
  (text left over from before 1.17f).
- **Walls and Coastal Fortresses are a shield that must be broken first; their 8 is not added to the 16.** alexman
  (APO-WALLS #25): "When you use a ground unit to bombard a city with walls, you have to first destroy the walls" "(by
  damaging them as if they were a unit with a defensive strength of 8) before you can harm anything else." / "After the
  walls are destroyed, each subsequent bombard unit can destroy a unit, improvement, or population in the city." (#1):
  "If the walls win, then all improvements, citizens, and units in the city are safe." / "This happens only if the city
  is size 6 or smaller." Naval bombardment works the same way with a Coastal Fortress instead of walls (#1); against
  ships and planes, walls are an ordinary improvement (#7: "walls are just as likely to be destroyed as any other
  improvement in the city").
- **Great Wall walls make a town immune to land bombard (C3C bug).** Walls from the Great Wall are hit first and are
  rebuilt at once. Lanzelot (CFC-BOMB122, #13): "In a town with "Great Wall - induced" walls, all buildings and units" /
  "are immune to bombardment." / "(Because the wall is always hit first, and if it gets destroyed, the Great Wall
  immediately "resurrects" it...)". He adds that the Great Wall's own city does not get the free wall: "The town that
  builds the Great Wall does not get a free wall from that!" (tmn, #14, disputes this.) jarred! (page 2): "It seems the
  walls are immediately restored."
  C3X-THREAD posters describe the bug differently: the hit goes to another building instead. sirati97
  (post-16319627, page-46 #916): "bombarding a city with a wall given by a wonder will instead destroy
  random other buildings"; Pifou42 (post-16891617, page-160 #3187) calls it a known vanilla bug where "bombarding a city
  with virtual walls given by Great Wall" can destroy the next building. Both are play reports, and both call it a bug.
  The intended handling of a hit on a free wall is not documented. Engine ruling (owner, 2026-10-21): Lanzelot's
  form is kept on purpose; a town with wonder-given walls is immune to land bombardment, while ships, aircraft and
  cruise missiles hit it in their own order.
- **Great Wonders can be destroyed by bombardment until the owner reaches the Industrial Age** (C3C 1.22, community).
  CFC-BOMB122 (#1): "If the bombardement occurs against a settlement of a civilization that has reached the industrial
  age," / "then any world wonder in this settlement is immune against any bombardment." / "Before the target
  civilization has reached the industrial age this is not the case". zerksees
  (https://forums.civfanatics.com/threads/using-bombardment-to-wreck-great-wonder.278312/, #1, C3C 1.22): "Last night I
  destroyed Sun Tzu Art of war with a trebuchet!" Dominus Romae (https://forums.civfanatics.com/threads/offensive-defense-the-tactical-and-strategic-values-of-the-great-wall.128868/,
  #9) says the opposite: "No, you can't destroy Great Wonders with bombardment." See Disputed.
  Flintlock (C3X-THREAD post-16725029, page-124 #2465) gives a narrower window: "the game has a really
  weird little rule that you can destroy wonders by bombardment but only in the ancient era." Earlier, as hearsay
  (post-16320772, page-46 #918): "I'd heard it's possible to destroy wonders through bombardment under some
  circumstances". He does not say whose era counts (Disputed 15).
- **Air units in a city can be killed by any bombard; land and sea units only by a lethal one.** CFC-BOMB122 (#1): "Land
  units can only be destroyed by bombardment, if the bombarding unit has the capability "lethal land bombardment"." /
  "Air units can be destroyed by bombardment." / "There is no such attribute as "lethal air bombardment"." justanick
  (https://forums.civfanatics.com/threads/air-battle-questions-only.565813/, #2): "your bombers and your battleships can
  destroy enemy aircrafts while they are at their base by bombing the base."
- **Bombard can hit tile improvements outside cities, and PTW+ airfields, outposts and radar towers.**
  README-PTW 1.01f: "You can destroy through bombarding Airfields, Outposts and Radar Towers." README-PTW: "Fixed bug
  where auto-bombard would not auto-bomb multiple terrain improvements in neutral territory."
- **Automated bombard fires at one chosen square every turn until nothing is left there** (community). labels.txt
  (#UNIT_ACTIONS): "Automate:^Bombard (Shift-B)", "Automate:^Bombing (Shift-B)", "Automate:^Precision Bombing
  (Shift-P)". https://forums.civfanatics.com/threads/stack-bombardment.87621: "You press Shift-B then select your
  target. What it does is cause your bombard units to keep firing on that same target, turn after turn, until there is
  nothing left to bombard." Bucephalus (https://forums.civfanatics.com/threads/how-exactly-do-i-use-auto-bombard.276907/,
  #2): "press the auto-bombard button then indicate the square you wish to bombard by clicking on it"; Gosnork (#5):
  "Auto-bombard just tells 1 Artillery to bombard the same square every turn." meriadoc
  (https://codehappy.net/apolyton/threads/71619-1.htm): "until either the square can't be bombarded or you stop the
  unit"; there "Wake when next to enemy unit" cancels it, and WarpStorm: "It does work fine for ships and planes
  though." README-PTW: "ICBM's can no longer auto-bombard." UNVERIFIED: whether it can hit a civ we're at peace with
  (C3 only fires at civs at war with us, owner's decision), and when in the turn it fires (C3: at once, then at the
  start of each turn).
- **Ships in port are extra vulnerable** (C3C). PEDIA `GCON_Bombardment_in_Ports`: "naval units "in port" (i.e., in a
  coastal city square being bombarded) are especially vulnerable to bombardment attacks." The size of the effect is UNVERIFIED.
- **Lethal bombard (editor ability since 1.21f; used by stock units in C3C only).** README-V 1.21f: "Editor: Added new
  unit abilities to allow bombarding to be lethal against land and/or sea units." C3C stock units with it (PEDIA):
  Cruise Missile, Bomber, Stealth Bomber, Stealth Fighter, F-15, Hwach'a (land and sea); Fighter, Jet Fighter, Dromon
  (sea only). Examples: `PRTO_Bomber`: "The Bomber has both Lethal Land and Sea Bombardment." `PRTO_Fighter`: "The
  Fighter has Lethal Sea Bombardment." Vanilla/PTW have no lethal units in the stock rules (freeciv thread, Akechi:
  "If vanilla Civ 3, there is no Lethal Land/Sea Bombardment unit.").
  The Cruise Missile ability implies lethal bombard by itself (community): Predator145 (C3X-THREAD post-16401109,
  page-64 #1266): (paraphrase from a summarised fetch) "giving a unit the cruise missile ability will automatically give it lethal land and
  sea bombard"; Belofon (post-16305295, page-41 #809, "IIRC") says the same. Before C3C no bombard unit could kill:
  Sullla, quoted by Ozymandias (post-16319042, page-46 #912): pre-C3C bombard units "could NEVER kill
  other units", the cruise missile being the only exception.
- **Stock bombard units (BIQ, C3C): bombard / range / rate of fire.**

  | Unit | B | R | RoF |
  |---|---|---|---|
  | Catapult | 4 | 1 | 1 |
  | Trebuchet (C3C) | 6 | 1 | 1 |
  | Cannon / Hwach'a | 8 | 1 | 1 |
  | Artillery | 12 | 2 | 2 |
  | Radar Artillery | 16 | 2 | 3 |
  | Cruise Missile | 16 | 4 | 3 |
  | Archer / Bowman | 1 | 0 | 1 |
  | Longbowman / Musketeer | 2 | 0 | 1 |
  | Guerilla | 3 | 0 | 1 |
  | TOW Infantry | 6 | 0 | 1 |
  | Frigate | 3 | 1 | 2 |
  | Ironclad / Destroyer / AEGIS Cruiser | 6 | 1 / 1 / 2 | 2 |
  | Cruiser | 7 | 1 | 2 |
  | Battleship | 8 | 2 | 2 |
  | Bomber | 12 | 0 | 3 |
  | Stealth Bomber | 18 | 0 | 3 |

  Range 0 units can only bombard defensively (see next item). Air units use operational range instead of bombard range.
- **Defensive bombard ("Artillery Free Shot").** When a land tile holding a bombard-capable unit is attacked, each
  such unit (other than the one defending) fires once at the attacker before the battle.
  README-V ("Changes"): "Artillery Free Shot: When a land tile with a bombard capable unit in it is attacked, that
  bombard unit will take one free shot at the attacking unit."
  APO-ZOC (Harovan): "The defense bombard is used once in a turn per unit with this feature and only, if the unit isn't
  the defender itself."
  https://forums.civfanatics.com/threads/zones-of-control.682034/ (Flintlock, #20): "Yes, defensive bombard only uses bombard strength."
  In C3C, Archers and Longbowmen got a range-0 bombard for this (APO-ZOC, Willem: "As of Conquests, Archer/Longbowmen units now get one as well").
  tjs282 (https://forums.civfanatics.com/threads/coastal-forts-and-bombardment.690690/, #4, 2024, C3C): "DB happens
  when a stack containing a bombard-capable unit gets attacked, provided that the bombarding unit" / "(1) has not
  already fired a DB during that interturn, and (2) is not the unit being attacked." / "IIRC the DB-unit's B-value is
  compared with the attacking unit's A-value, using the usual combat-success RNG dice-roll" / "if the DB hits, the
  attacker loses 1 HP before it engages its actual target." So each bombard unit fires once per enemy turn, not once
  per attack; README-V's "one free shot" fits that. Which strength of the attacker is used (attack or defense) is
  UNVERIFIED (tjs282 says "IIRC").
  **The free shot never kills, and only land and sea bombard units fire it** (code reading). C3X-INI lists, as mod
  options that change the stock rule: "lethal: Attackers may be destroyed by defensive bombard if the bombarding unit
  has the appropriate lethal land/sea bombard ability", "aerial: Air units may perform defensive bombard",
  "not-invisible: Attacking units are immune to defensive bombard if they are invisible and not detected", "blitz:
  Units with blitz may perform defensive bombard multiple times per turn" and "docked-vs-land: Docked ships (i.e. in a
  city) can perform defensive bombard against land units attacking their city". So in stock 1.22 the shot can't kill
  even with a lethal unit, aircraft don't fire it, undetected invisible attackers are still shot at, blitz gives no
  extra shots, and ships in a city don't fire at land attackers.
  **One shot per attack, from the strongest eligible bombarder (code, third round).** The stock routine is
  `Fighter_find_defensive_bombarder (attacker, defender)`. It returns a single unit, and C3X wraps it
  (https://github.com/maxpetul/C3X/blob/master/injected_code.c, `patch_Fighter_find_defensive_bombarder`). When any of
  its DB options are on, the mod re-creates the choice, and with them off it calls the stock function. The re-creation
  gives no shot when the attacker's defence strength is below 1 or the attacker has 1 HP left. Otherwise it takes the
  unit on the defending tile with the highest bombard strength, among units with bombard > 0 that are not cruise
  missiles, have not fired DB this turn (unit status bit `USF_USED_DEFENSIVE_BOMBARD = 0x40`, "the unit has performed
  def. bombard already this turn", Civ3Conquests.h), are not the defender or the defender's army, and are of the
  attacker's class (land against land, sea against sea). So each attack draws at most one free shot, and a stack with
  three catapults answers three separate attacks, not one attack three times. This refines README-V's "one free shot",
  and the opening line of this bullet should be read that way. Skipping attackers with no defence fits the shot
  rolling bombard against the attacker's *defence*, as a normal bombard does, but that is an inference: the damage
  roll itself (`Fighter_damage_by_defensive_bombard`) is not re-implemented.
  **Flintlock states the stock rule directly (C3X-THREAD post-16536435, page-83 #1655):** "In the base
  game, defensive bombard can do at most one damage and will never activate against an attacker with one HP, so it
  can never outright kill an attacker." / "They roll only once. Defensive bombard doesn't take rate of
  fire into account at all." So a DB shot is one roll, at most 1 HP, whatever the unit's rate of fire. Civinator
  (post-16536154, #1651) agrees: (paraphrase from a summarised fetch) "the programming always cut it down to a ROF of 1".
  **Multiplayer bug (play report).** Sutsuj (post-16194164, page-26 #510): "In multiplayer, defensive
  bombardment units get extreme buffs." / (paraphrase from a summarised fetch) "The more players in the game, the stronger they are." A
  shipped bug if true; the engine follows the single-player rule.
- **Cruise missiles die after bombarding.** CFC-RULES: "a unit which, when bombarding, will die after it is done bombarding."
  PEDIA `GCON_Combat`: missiles "never return to its base and the attacks can destroy enemy units."
- **Artillery have no attack or defense and must be escorted.** PEDIA `PRTO_Artillery`: "Artillery has no attack or
  defense value, so they should be protected by an escort." If caught alone they are captured (see 1.13).
- **Bombard uses the attack for the turn.** freeciv thread (Akechi): "if unit does not have Blitz ability, this unit
  can only one attack per turn". Whether bombarding costs all movement is UNVERIFIED.

---

## 1.8 Zone of control

- **ZOC does not block movement; it gives a free shot at an enemy moving past.** PEDIA `GCON_ZOC`: "Such units attack
  enemies that enter the zone of control without initiating a full battle." EDHELP / CFC-RULES: "Determines if the
  unit will automatically bombard passing units." APO-ZOC (Harovan): "In Civ2 or CtP the movement would be blocked due
  to ZoC, in Civ3 it is still possible, but the other civ gets a free shot on the passing unit."
- **Units with ZOC (C3C):** AEGIS Cruiser, Armies, Cavalry, German Panzer, Mech Infantry, Modern Armor, Mongol Keshik,
  Ottoman Sipahi, Modern Paratroops, Radar Artillery, Russian Cossack, Spanish Conquistador, Tank, and any unit in a
  fortress or barricade (PEDIA `GCON_ZOC`, `GCON_Fortresses`, `GCON_Barricades`). Vanilla also lists Marines and
  (plain) Paratroops and lacks the PTW/C3C unique units (PEDIA-V `GCON_ZOC`).
- **Trigger: an enemy moves from one tile next to the ZOC unit to another tile next to it.** APO-ZOC (geniemalin):
  the shot triggers only when an enemy moves between two squares adjacent to the unit.
  https://apolyton.net/forum/civilization-series/civilization-iii/110683-how-do-the-zone-of-control-work (CerberusIV):
  "It only works when an enemy unit moves from a tile adjacent to the fortress to another tile also adjacent to the fortress".
- **Each shot takes 0 or 1 HP and never kills.** Thread 682034 (Theov): "The outcome is binary and costs one HP."
  Ozymandias (same thread, quoting civ-wiki.de): "The control zone has no effect on units that have only one health point."
- **Hit chance (reverse-engineered, C3C):** `1 - (D*16 / (D*16 + A))`, D = the moving unit's defense with no terrain
  bonus, A = the higher of the ZOC unit's attack or bombard, using the strongest ZOC unit in the stack.
  Thread 682034 (Flintlock, #3): "1 - (D*16 / (D*16 + A))"; "The game will always select the strongest available unit
  to try to do damage."; Ozymandias: "The defense value of the moving units does not enjoy bonuses from the terrain."
  This implies low odds (for example A 16 vs D 2: 16/48 = 33%).
  Flintlock restates it in C3X-THREAD (post-16537815, page-84 #1671): "The formula for the chance to do
  damage by ZoC is: 1 - (D*16 / (D*16 + A))", with D the moving unit's defence and A the ZoC unit's attack or bombard
  strength; it "also applies to coastal fortresses". His examples: strength 160 against defence 8 gives
  55.6%, strength 8 gives 5.9%. He adds: "I don't think the AI understands ZoC at all". A later post
  (post-16559552, page-90 #1785: "They use their bombard value", with attack usable only against the same
  class) answers a question about air units, which have ZOC only under C3X's rules, so it most likely describes the
  mod and is not used for stock. The shot comes only once the unit actually moves
  (post-16589155, page-96 #1907): "the unit doesn't experience ZoC until it moves and it doesn't move
  until it has dealt with any enemy units".
- **Coastal Fortress quirks (community).** Erlend Philip Wood (C3X-THREAD post-16536518, page-83 #1659):
  (paraphrase from a summarised fetch) "If a ship already adjacent to the Fortress moves to another adjacent tile, then the CF will (attempt to)
  shoot at it." / "a CF inside a bay (where there is only 1 coastal tile adjacent) will never fire at enemy
  ships." Both follow from the adjacent-to-adjacent trigger above.
- **Walls in a town give its units ZOC; the effect ends at size 7** (community, C3C). Thread 682034 (Ozymandias,
  quoting civ-wiki.de): "All units in a settlement (size 1 to 6) with a city wall automatically have a control zone."
  justanick (thread 690690, #5, 2024), after tjs282 notes that fortresses give ZOC: "This is also true for towns with
  walls. Notably this effect is lost when the town becomes a city(size 7+)." Not confirmed by a Firaxis text.
- **Stock limits on who exerts ZOC** (C3X-INI, mod options that change the stock rule): "amphibious: Land units may
  exert zone of control over sea units and vice-versa", "lethal: Units may be destroyed by zone of control", "aerial:
  Air units can exert zone of control", "not-from-inside: Land units may not exert zone of control while inside a
  transport". So in stock 1.22 land and sea ZOC don't cross, ZOC never kills, air units have none, and land units
  exert ZOC even from inside a transport.
- **One unit can take several ZOC shots per enemy turn** (unlike defensive bombard). tjs282 (thread 690690, #4): "But
  unlike DB, the same unit can make multiple ZoC-shots per interturn."
- **Coastal Fortress ZOC uses the same trigger.** tjs282 (thread 690690, #2): "The CF Zone of Control can/will only
  activate if a ship moves from one of the 8 (Coast) tiles" around it to another of them.

---

## 1.9 Movement

- **Terrain costs** are in the table in 1.6 (BIQ): 1 for flat land and water, 2 for hills, forest and marsh, 3 for
  mountains, jungle and volcano. EDHELP *Movement Cost*: "Determines the base movement cost for a tile of the selected terrain type."
- **Roads: 1/3 movement point per tile regardless of terrain.** PEDIA `TFRM_Road`: "units moving along roads expend one
  third of a movement point per square, regardless of terrain type." BIQ `movementAlongRoads 3`.
- **Railroads: zero movement.** PEDIA `TFRM_Railroad`: "Units moving along a railroad expend zero movement points."
  Railroads need Steam Power plus Iron and Coal (same entry). README-C3C: "Railroads will always appear under captured cities if you have Steam Power."
- **Rivers give no movement bonus in Civ3** (rivers run between tiles). Jon Shafer, in
  https://apolyton.net/forum/miscellaneous/archives/civ3-general-archive/58801-rivers (April 2002, before he joined
  Firaxis): "Rivers are placed in between spaces in Civ III, unlike in Civ II".
- **Roads cross rivers only after Engineering.** PEDIA `TECH_Engineering` (all three versions): "{New Ability} Roads now
  bridge rivers." Before Engineering, a road move across a river does not get the road rate; how much it costs instead
  is not in the Civilopedia. Community claims that crossing an unbridged river costs a full movement point: TheBB
  (https://forums.civfanatics.com/threads/rivers-and-roads.120751/, #2, 2005, C3C): "a whole movement point is lost when
  crossing a river"; mastertyguy (#3): "It is also in vanilla". UNVERIFIED by a test. Bridges go when the road is
  pillaged (Mark Anthony, #8: "to destroy the bridge (available only after engineering), you must also destroy the road
  leading up to it.").
- **Wheeled units can't enter mountains, volcano, jungle or marsh except along a road.** PEDIA `PRTO_Catapult`:
  "catapults are wheeled so they may not enter $LINK<mountain=TERR_Mountains>, $LINK<volcano=TERR_Volcano>,
  $LINK<jungle=TERR_Jungle> or $LINK<marsh=TERR_Marsh> terrain unless along a road." BIQ `impassableByWheeled` is set
  on exactly those four terrains.
- **"All Terrain as Roads" (Explorer) and "Ignore Movement Cost" per terrain.** PEDIA `PRTO_Explorer`: "unarmed,
  non-combat units that treat all terrain as if it were a road." EDHELP *Ignore Movement Cost*: "Units with a terrain
  movement bonus will ignore movement costs of the selected terrain types." README-V 1.21f: "Updated All Terrain As
  Roads unit ability to work with water units."
- **Air units ignore terrain costs; water units pay terrain cost.** README-V 1.29f: "Changed air units to ignore
  movement costs of terrain types". README-V 1.21f: "Updated water unit movement to take into account the cost of the terrain type."
- **Naval movement bonuses:** Great Lighthouse +1 (PEDIA `BLDG_Lighthouse`: "all naval unit movement rates are
  increased by one."), Magellan's +1 (PEDIA `BLDG_Circumnavigation`: "The movement rate of all naval units is
  increased by one."), Seafaring trait (PEDIA `GCON_Strengths`: "Ships move faster and are less likely to sink in the sea or ocean.").
- **Partial moves.** A unit with 1/3 move left can still enter any tile. Community support only: general-jcl
  (C3X-THREAD post-16827533, page-138 #2751), on the stock rule a mod option would change, says a unit
  (paraphrase from a summarised fetch) "can still climb a nearby mountain" with too few moves left. No test or code reading found.
- **Movement points (code reading).** Flintlock (C3X-THREAD page-6 #110): "In the base game, assuming road
  movement rate is left at 3, one full move is 3 movement points (MP)". Railroad moves cost nothing: Tsubasanut
  (post-16044203, page-3 #44) found the stock rail cost zeroed by an XOR in the move-cost code. Flintlock (page-4
  #70): "Movement in the base game doesn't scale with map size".
- **Movement quirks (code readings, C3X-THREAD, Flintlock).**
  - A unit can't enter a tile holding another civ's units, Hidden Nationality or not (post-16569253, page-91
    #1808): the code "prevents units from moving onto tiles that are occupied by other civs' units,
    ignoring HN status".
  - Skipping a turn empties the unit's moves (post-16441638, page-71 #1419): "Skipping a unit's turn
    removes all its remaining moves."
  - Goto paths are recomputed as the unit moves (page-148 #2945): "The game simply stores the
    destination tile and recomputes the path along the way." Pathfinder bugs: it stores remaining MP per step in
    one byte (#1419), and it (paraphrase from a summarised fetch) "gives up" after adding 256 directions (page-148 #2943).
  - A stack moved together stops where its slowest member must (ynot56, post-16163674, page-22 #425): it
    "goes to the maximum distance of the unit with the lowest MP".
- **Foreign roads and railroads give no movement bonus inside their owner's territory without a Right of Passage.**
  PEDIA `GCON_Territory`: "Rival civilizations cannot benefit from the roads or rails within your territory without a
  $LINK<right of passage=GCON_Agreements> agreement." PEDIA `GCON_Agreements` (Right of Passage): "You agree to allow
  his or her military units to move through your $LINK<territory=GCON_Territory> and use your transportation network
  (roads and railroads)." Roads in neutral territory, and roads in a tile that becomes yours when you take a city, work
  normally (the territory rule above; the War Academy's "Early Industrial Railroad Intrusion",
  https://civfanatics.com/civ3/strategy/military/early-industrial-railroad-intrusion/, depends on this).
- **Units that win an attack advance into the tile only if it is empty.** README-V 1.21f: "Fixed bug involving extra
  movement costs if the unit can't advance after combat." The exact advance rule is UNVERIFIED.

---

## 1.10 Naval units

- **Ships can't slip between two land tiles on a straight-line move (code, third round).** Antal1987's reading of the
  1.22 `class_Unit::Can_Pass_Tile` (https://forums.civfanatics.com/threads/c3c-new-movement-possibilities.529671/ #1,
  2014, as reported by the fetch tool). A move into a city of the unit's own civ, or by an air unit, always passes.
  For a naval move from water to water in directions 2, 4, 6 or 8 (E, S, W, N in C3X's `enum direction`: the moves
  that pass the corner between two tiles), the code checks the two flanking tiles: "If the direction is even, it
  checks neighbour tiles (clockwise and counter clockwise) to be water". The move is allowed if either flank is water,
  and refused only if both are land. So two land tiles that touch only at a corner close the water passage between
  them. A ship can still enter a coastal city of its own civ (the bypass above), which is how a city acts as a canal (Oni Ryuu, CFC 526285 page 11 #213: "The canals are just the ability to place
  cities next to one another"). The land branch, in the same post: a land unit can't enter water unless it boards a
  transport, and on impassable terrain (or wheeled-impassable terrain for wheeled units) it needs a road on both the
  origin and the target tile.

- **Curragh and Galley must end turns in coast; they risk sinking in sea or ocean.** PEDIA `PRTO_Galley`: "A galley
  must end each turn in a $LINK<coastal=TERR_Coast> square -- they are in danger of sinking if they end their turn in a
  $LINK<sea=TERR_Sea> or $LINK<ocean=TERR_Ocean> square. If $LINK<The Great Lighthouse=BLDG_Lighthouse> is built,
  galleys may safely end turns at sea, but never ocean squares." (`PRTO_Curragh` has the same rule.)
- **Caravel is safe in sea, at risk in ocean.** PEDIA `PRTO_Caravel`: "may safely traverse $LINK<coast=TERR_Coast>
  and $LINK<sea=TERR_Sea> squares. If it ends a turn in an $LINK<ocean=TERR_Ocean> square, it is in danger of sinking."
- **The sink flags are per unit: "Sink in Sea" and "Sink in Ocean".** EDHELP *Unit Abilities*: "Sink in Sea Unit dies
  if it ends its turn on a sea square Sink in Ocean Unit dies if it ends its turn on an ocean square". CFC-RULES:
  "can possibly sink and die without the right technology". The techs that make sea and ocean safe are given in
  PEDIA `TERR_Sea` (Astronomy) and `TERR_Ocean` (Navigation or Magnetism) for trade and travel. CFC-FAQ (Ginger_Ale,
  who also wrote CFC-SINK) says those techs remove the risk for every ship: after Astronomy "all ships (even old
  curraghs and galleys) will be able to end their turns at sea safely"; after Navigation or Magnetism "every ship is
  safe to end their turns in any tile." This conflicts with PEDIA `PRTO_Galley` ("never ocean squares", said of the
  Great Lighthouse only) only if read strictly; no test of galleys in ocean after Navigation was found. Community
  rule, UNVERIFIED by a test.
- **Sinking chance per turn ended in danger: 50%, or 25% for Seafaring civs (75% survive)** (C3C 1.22 test).
  CFC-SINK: "If you are Seafaring, it's 75% of survival, regardless of difficulty level, terrain, ship, turns spent in
  water." / "If you are not Seafaring, it's 50%." Tested on curraghs and caravels in ocean, galleys in sea.
- **Ships do not attack land units; they only bombard them** (PTW note above: "There are no direct naval attacks on
  land units -- only bombardment."). script.txt `#NOTAMPHIB`: "Only Marine units can attack from the sea." and
  `#LANDCONQUER`: "Only [ground] units can be used to attack settlements." Whether a ship can attack a land tile that
  holds only non-combat units is UNVERIFIED.
  - Known C3C 1.22 bug: AI galleys sometimes attack a coastal city directly (the "privateer bug").
    https://forums.civfanatics.com/threads/new-attack-ability-galleys-can-attack-units-in-cities.677328/ (Takhisis, #4,
    "Civ3 Complete 1.22"): "a roaming AI galley finds a city of mine and sails up close, then declares war and attacks
    head-on." r16 (#2): it is a variation of the privateer bug and "does not require any ship in port." Not a rule.
    Flintlock traced the cause (C3X-THREAD page-147 #2940, code and test): "the pathfinder in rare cases
    generates paths through other civs' cities for AI naval units", and "when I tested with the
    unmodded EXE, the bug still occurred." The older form, with a Hidden Nationality ship as a city's only
    garrison, is also stock: tjs282 (post-16376491, page-59 #1179) and Civinator (post-16377056, #1182):
    (paraphrase from a summarised fetch) "it happens even with an unmodded standard C3C game." Both are bugs; at peace, ships should not
    attack a city.
- **Amphibious attack: only units with the Amphibious ability attack from a ship onto land** (Marines; Viking Berserk).
  EDHELP *Unit Abilities*: "Amphibious Unit may attack from a ship onto land". PEDIA `PRTO_Marine`: "They can unload
  from a sea $LINK<transport=PRTO_Transport> into any coastal land square, including enemy cities and enemy-occupied
  squares." PEDIA `PRTO_Berserk`: "allows them to attack enemy units or structures directly from within sea-based
  transport vessels." No amphibious attack penalty or bonus was found in a Firaxis source. Sullla
  (https://sullla.com/Civ3/strat8b.html, vanilla) says Marines attacking from ships "ignore city defensive bonuses";
  alexman (APO-WALLS #1) says the opposite for walls ("When you attack a city with a ground unit (or a marine), the
  defender gets the defensive bonus of the wall."). See Disputed.
  **Code reading: amphibious attacks get a 25% modifier.** Flintlock (C3X-THREAD post-16716406, page-122 #2435):
  "Amphibious attacks definitely do get a 25% bonus." / "I remember seeing that in the code a
  while back when I was looking into how the combat odds get calculated." He gives no more detail (a recollection
  of the odds function, not a quoted line); the post does not say which side the 25% favours (UNVERIFIED).
- **C3C: any unit can step off a ship onto a coastal tile that holds only non-combat units and capture them.**
  https://forums.civfanatics.com/threads/plucking-workers-from-the-coast.72176/ (Fanny Brice, #1): "ALL units can
  attack enemy workers along the coast right off of transports." Chieftess (moderator, #9): "It's a new feature to
  prevent the coastal blockades with units and workers, especially in MP games."
- **Landing.** Units unloaded onto land can't move again that turn (CFC artillery article:
  https://civfanatics.com/civ3/strategy/military/how-to-use-artillery-defensively/ says landed enemy units "cannot
  move until the next turn"). The exact rule is UNVERIFIED.
- **Submarines are invisible** except to subs, AEGIS Cruisers and Destroyers. PEDIA `PRTO_Submarine`: "They remain
  invisible until they attack a surface ship ... other submarines and $LINK<AEGIS Cruisers=PRTO_AEGIS_Cruiser> and
  $LINK<Destroyers=PRTO_Destroyer> can see them plainly." README-V: "You no longer declare war by accidentally passing over a hidden submarine."
- **Privateers hide their nationality** (attack without declaring war). PEDIA `PRTO_Privateer`: "allowing them to
  attack and be attacked [without revealing the nationality] of the ship."
- **Transports.** Ships are boarded by moving onto them from shore or loaded in a coastal city (PEDIA `GCON_Transport`).
  Transport units can't carry other transports (Mike Breitkreutz, Firaxis,
  https://apolyton.net/forum/civilization-series/civilization-iii/53195-some-questions-for-the-developers-on-editing-and-the-civ3mod-file:
  "No. Transport units cannot carry other transport units."). tjs282 (C3X-THREAD post-16826417, page-137 #2737):
  "The programmed rule is that no transport-unit (i.e. a unit with a non-zero transport-capacity...) can be
  loaded onto another transport-unit." Flintlock (post-16667922, page-114 #2273): "transports are the
  only units that never stack", and (#2269) "Units track what unit they're contained in but container
  units don't track what units are inside them."
- **A go-to doesn't board a ship at sea** (community). Reviewing OpenCiv3's transport PR
  (https://github.com/C7-Game/OpenCiv3/pull/936), stavrosfa: to send a land unit onto a transport on a sea tile next
  to the shore "I can't use the go-to (this is faithful to the original)"; the last step is a manual move. C3 keeps it
  so. Loading asks which transport when several could take the unit (script.txt `SELECT_TRANSPORT`: "What
  Transport?", "Select a transport to board:"), and unloading asks which units (script.txt `DISEMBARK`:
  "Disembark?", "Never mind.", "Unload all."). UNVERIFIED: whether moving onto a tile with several transports asks too
  (C3 picks one).
- **Ships can enter a land tile only if it holds a city; doing so unloads everything** (code reading). Flintlock
  (C3X-THREAD post-16787843, page-134 #2662): "It's technically already possible for sea units to enter
  land tiles, if those tiles have cities on them." / (paraphrase from a summarised fetch) "Moving a transport onto a land tile triggers the
  unload all operation".
- **What happens to carried units when a ship sinks or loses** is UNVERIFIED (presumably they die with it).

---

## 1.11 Air units

- **Air units stay in cities, airfields or carriers and fly missions within operational range.** PEDIA
  `GCON_Air_Missions`: "Air units are not moved around the map like other units. Instead, they are stationed in a city,
  and assigned missions which they execute immediately against targets within their operational range."
- **Missions (C3C):** Air Superiority, Bombing, Precision Strike, Recon, Re-Base.
  - Air Superiority. PEDIA: "The plane attempts to intercept enemy aircraft that attack targets within the inner half of its operational range."
  - Bombing. PEDIA: "The plane flies to the target and attacks, sometimes affecting city improvements, and sometimes population."
  - Precision Strike. PEDIA: "Performed by $LINK<stealth aircraft=PRTO_Stealth_Fighter>, precision strikes destroy
    city improvements only, unless there are none in which case population is affected." README-V ("Changes"): needs
    the Smart Weapons advance; "Useful only against cities, this mission targets improvements."
  - Recon. PEDIA: "The plane flies to the target and reveals the map." It can't be intercepted (CFC-RULES: "(Recon and Rebase can not be intercepted)").
    C3X-INI: "Under the standard rules, performing a recon mission or intercepting an attacker causes a unit to lose all
    of its remaining moves", and the recon area is "the stock 5x5 area". Vision from a recon mission is removed at the
    start of the unit's next turn ("Recon missions expire at the start of a unit's turn").
  - Re-Base. PEDIA: "a distance equal to up to Six times the plane's operational range."
- **Interception chance: 50% per interceptor, 5% against stealth.** EDHELP *Chance to Intercept Enemy Air Missions*:
  "Default is 50%." *... Stealth Missions*: "Default is 5%."
  alexman (Firaxis), APO-ZOC #11: "A fighter on air superiority has a 50% chance to intercept a bomber (5% if the bomber
  is stealth)." / "If it fails to intercept, it can try to intercept the next incoming bomber." / "If it succeeds, it's
  done intercepting for that turn."
- **Which fighter intercepts: the first available, not the best (code reading).** Flintlock (C3X-THREAD
  post-16521876, page-81 #1609): "instead the game picks the first
  available fighter to intercept." The interception roll happens as the plane flies over the target tile
  (post-16858118, page-150 #2987, and the routine `Unit::try_flying_over_tile`, post-16395511, page-63 #1253, which
  (paraphrase from a summarised fetch) "rolls for interception, does air combat, and also plays the flying animation"). Other stock details
  from #1222: "Normally the game does not give any indication when a plane dodges interception." and
  "a plane with multiple movement points can bomb multiple times but only perform reconaissance once per
  turn". Ground anti-air kills outright (post-16512698, page-79 #1575: "air combat involves instant
  kills"), which matches korn469's "simple yes or no" below.
- **Flak and Mobile SAM defend only their own tile.** alexman (APO-ZOC #11, as summarized): Flak and Mobile SAMs only
  defend against aircraft attacking their own tile. SAM Battery: PEDIA `BLDG_SAM_Missile_Battery`: "[SAM Missile
  Batteries] attack enemy air units that attempt to attack the SAM site's city." Anti-air stat, BIQ: Flak 2, Mobile
  SAM 4, AEGIS Cruiser 3, Battleship 2, Destroyer/Cruiser/Carrier 1.
- **Anti-air units (C3C): up to 4 per tile fire, each at one tenth of its anti-air value against the plane's defense.**
  Tested by BomberEscort (CFC-AA, 500 trials per case, C3C 1.00), with alexman agreeing (#13):
  - #7: "Chance Air Unit is Destoyed By AAUnit #1 = 1 - [AirD/(AirD + (AAUnitD#1 * 0.1))]"; the next unit fires only
    if the one before missed ("It is done as above because AAUnit#2 only gets a chance to fire if AAUnit#1 Misses...");
    "AAUnits must be sorted in decending order".
  - #5: "If more than 4 AA in a square, only the best four will defend..." / "Hit Points and Experience do not affect
    interception by AA." / "Stealth Ability does not affect interception by AA." / "Terrain has no effect." / "Radar
    Towers have no effect." / "A Defense of zero will result in always being shot down, this is why Stealth Aircraft
    now have a Defense...".
  - Results fit the formula: 1 Flak (AA 2) against a defense-2 plane gave "48/500 Destroyed----- 9.6%" (formula 9.1%);
    2 Flak gave 17.6% (formula 17.4%).
  - Same rule reported by justanick (https://forums.civfanatics.com/threads/air-battle-questions-only.565813/, #4):
    "In fact having up to 4 units will have a chance to attack you. However, only 0.1 times their attack value is
    applied." and Ozymandias (https://forums.civfanatics.com/threads/air-units-bombing-mechanics-in-the-open.678152/,
    #2): "The real strength of each AA unit is 1/10th of the value shown in the editor." / "The maximum number of AA
    units that are given a chance to shoot down the plane is 4."
  - CFC-FAQ: "the chance to intercept an air unit with stealth and without stealth is the same".
  - AA fire is a single yes/no roll: korn469 (https://apolyton.net/forum/miscellaneous/archives/civ3-strategy-archive/120873-has-anyone-figured-out-the-aa-formula-yet,
    #12): "fighters vs. other air units does result in a hitpoint loss, whereas with AA units (either land or sea) it
    results in a simple yes or no situation".
  - An independent editor test fits the one-tenth rule and shows the interception setting doesn't apply to AA units.
    Vingrjoe (https://forums.civfanatics.com/threads/anti-air-defense-combat.100020, #1, PTW/C3C era): "one Aegis
    cruiser with an air defense of 100, and had 20 bombers with defense of only one"; "On 0, 25, and 100 percent
    chance air interception the results were 90% of the aircraft killed" (one tenth of 100 against 1 gives 10/11 = 91%).
- **SAM Battery: fires with the interception chance, then rolls A/(A+D) at full strength.** alexman (CFC-AA #13): "For
  each bomber, there is a chance equal to the interception chance for the SAM to fire" / "If the SAM fires, it has a
  A/(A+D) probability to shoot down the plane." justanick (thread 565813, #4): "However here the full attack values
  applies, but only in 50% or 5% of chances, that will depend on stealth or no stealth." BomberEscort's SAM-only test
  (CFC-AA #5) gave 3.6%, lower than this predicts; he called it possibly "a statistical fluke" (#20). Whether the SAM
  adds to mobile AA on the same tile: BomberEscort (#5) "SAM Missile Battery is cumulative with Mobile AA."; the list
  alexman quotes in #13 says it is not. UNVERIFIED.
- **Air units on interception stay on it when their carrier moves** (README-V 1.21f).
- **Air and sea units in a captured city are destroyed.** CFC-CSE (Xerxes314): "air and sea units caught in a city are
  automatically destroyed. They have no combat value." README-PTW: "Fixed bug that caused air units to not be destroyed
  if they are not in a city." Which units this covers exactly is UNVERIFIED.
- **Airfields (PTW+)** work like an airport for air units; one use consumes a worker; pillaging destroys them.
  PEDIA `GCON_Airfields`: "Airfields function in the same manner that a city with an airport would for air units." /
  "Airfields can be pillaged like a terrain improvement, which also destroys the airfield."
- **Helicopters carry foot soldiers only, 3 at a time (BIQ capacity 3).** Other capacities (BIQ): Transport 6,
  Galleon 4, Carrier 4 air units, Nuclear Submarine 1 tactical nuke.
  PEDIA `PRTO_Helicopter`: "Helicopters may carry foot soldiers only -- no mechanized units allowed."
  PEDIA `GCON_Transport`: "To board a helicopter a ground unit must start its turn in a city with the chopper and use the Load command button."
- **Paratroopers airdrop** from a city with an Airport (or an airfield, PTW 1.04f). PEDIA `PRTO_Paratrooper`: "When a
  paratrooper is active in a city with an $LINK<airport=BLDG_Airport> it may perform an airdrop." README-PTW 1.04f:
  "Added ability to airdrop from airfields." Drop range is the operational range (BIQ: Paratrooper 6, Modern Paratrooper 8).
  C3X-INI: "Under the standard game rules, units lose all their remaining moves after airdropping." Re-basing range is
  six times the operational range (C3X-INI: "For standard game rules, set to 6", matching PEDIA's "Six times").
  Flintlock (C3X-THREAD post-16823297, page-136 #2720) says "the factor of six is baked into the logic" (in a
  discussion of a C3X rebase feature, so it may describe the mod's copy). Two
  stock limits on airdrops, tested (post-16539545, page-85 #1690): "you can't airdrop into an enemy city even if
  it's undefended." / "You also can't airdrop onto an enemy worker."
- **A bomber that dodges SAM damage still loses one move** (C3X-INI: "there is no indication that anything happened
  in that case except for the bomber losing one movement point").
- **Air unit stats (BIQ, C3C):** Fighter A4 D2 B3 range 6; Jet Fighter A8 D4 B3 range 9; F-15 A8 D4 B6 range 9;
  Stealth Fighter A8 D6 B6 range 12; Bomber D2 B12 RoF3 range 10; Stealth Bomber D5 B18 RoF3 range 16.
- **An intercepted mission always fails; the two planes then fight a normal battle to the death.** tjs282 (thread
  565813, #9, C3C): "Interception =/= Shootdown." / "(1) Is a Fighter scrambled to intercept the Bomber? (result based
  on 'Intercept rate')" / "If yes, Bombing mission fails, goto (2)" / "(2) Does the Fighter defeat the Bomber? (result
  based on relative A/D values and HP)" / "If yes, Bomber will crash'n'burn (Fighter may be damaged)" / "If no, Fighter
  will crash'n'burn (Bomber may be damaged)". Ozymandias (thread 678152, #4) quotes a FAQ: "If an aircraft is
  intercepted it will always fail to bomb the target even if it wins the air battle." The game's messages have both
  outcomes (script.txt `#AIRINTERCEPTED` "We were shot down by an enemy interceptor!", `#AIRINTERCEPTORDESTROYED` "We shot
  down an enemy interceptor!", `#AIRINTERCEPTFAILURE` "Our interceptor was shot down!", plus `#AIRSAMINTERCEPTED` and
  `#AIRFLAKINTERCEPTED`). Which unit uses attack and which defense in that battle is UNVERIFIED (presumably the
  interceptor attacks).
- **A fighter intercepts at most once per turn and needs a turn to activate.** CFC-FAQ: "A plane can only engage
  another plane once per turn." tjs282 (thread 565813, #9): "a Fighter can only be scrambled once per turn". Kirejara
  (#12): "Interceptors never launched in the round they are ordered to perform CAP". tjs282
  (https://forums.civfanatics.com/threads/air-unit-interception-range.678374/, #2) calls the delay a bug: the mission
  "does not activate on the interturn directly after being selected".
  - **Code (third round): the delay is a human-only interface bug, and one interception ends the mission.** C3X-INI,
    "INTERCEPT LOST TURN BUG": "When a human player sets a fighter to intercept mode, the game discards all of its
    remaining movement points, hence it cannot intercept during the next interturn. This bug does not affect the AI and
    is part of the interface logic, not the game logic." The intended rule (which the engine should follow) is that a
    fighter set to air superiority can intercept in the next interturn. Once per turn is stock: C3X-SRC
    (`patch_Unit_set_state_after_interception`) says the mod "can't knock them out of the interception state as soon as
    they intercept something like in the base game". So the stock game takes the fighter off its mission after one
    interception and gives it its full moves again (`Unit_get_moves_after_interception` returns the max move points).
- **Several fighters on air superiority raise the interception chance in PTW and C3C, not in vanilla.** Catt (Apolyton
  thread 120873, #8): stacking fighters "did not increase the chances of interception" in vanilla, which changed in PTW
  and Conquests. Jaybe (https://apolyton.net/forum/miscellaneous/archives/civ3-general-archive/72889-how-do-intercept-and-air-supremacy-really-work,
  #4) cites the PTW strategy guide: the chance "DOES increase as you have more units on Air Superiority available."
  alexman's per-fighter 50% roll (above) is the C3C model.

---

## 1.12 Nuclear weapons

- **Manhattan Project enables nukes for every civ.** PEDIA `BLDG_Manhattan_Project`: "Allows the construction of
  nuclear weapons by {all} civilizations. Nuclear Weapons can {Not} be built until this Wonder has been completed."
- **Tactical Nuke: bombard range 6, can be carried by nuclear subs.** PEDIA `PRTO_Nuke`: "They can be loaded onto
  nuclear subs and launched from the sea." / "The bombard range of a Tactical Nuke is 6 squares." Needs Aluminum and
  Uranium. BIQ: 300 shields.
- **ICBM: unlimited range, can't move from the city that built it.** PEDIA `PRTO_ICBM`: "An [ICBM] ... has an
  unlimited range" / "They cannot be moved from the city that builds them but can strike any target on the map." BIQ: 500 shields.
- **Using nukes angers the world.** PEDIA `PRTO_ICBM`: "Nuclear weapons, if used, have a tremendous negative effect upon world opinion."
- **SDI (small wonder, needs 5 SAM Batteries): 75% chance to stop incoming nukes.** PEDIA `BLDG_SDI`: "Offers a 75%
  chance of intercepting enemy ICBMs." EDHELP: "the selected Small Wonder will cause enemy missile attacks to fail 75%
  of the time." It also stops tactical nukes (https://apolyton.net/forum/civilization-series/civilization-iii/135536-strategic-missile-defense,
  MattPilot: "Infact it shoots down everything with the "nuke" flag."). C3X-THREAD posters are split. Erlend Philip
  Wood (post-16497435, page-78 #1545, testing) says nukes in general (paraphrase from a summarised fetch) "will be targeted by the Strategic
  Missile Defense, with a probability of 75% to get shot down"; Alekseyev_ (post-16495192, #1542, "IIRC") says
  non-ICBM nukes are not intercepted. Predator145 (post-16395546, page-63 #1254), on Cruise Missile and Nuclear Weapon units:
  "Yeah, only SDIs can shoot them down. But interceptors and SAM batteries as well as Air Defense value units
  can't."
- **Nuclear units are immune to nukes; nuke damage is fixed in code.** Civinator (C3X-THREAD post-16395572,
  page-63 #1256): "The nuclear flag (not the ICBM or tactical nuke strategy) also makes a unit immun
  against nuclear attacks." This matches justanick's "All nuclear weapons are immune" below. Flintlock
  (post-16896015, page-163 #3246): (paraphrase from a summarised fetch) "Modifying damage from nuclear explosions isn't easy since it's
  hardcoded."
- **Blast effects: each unit in the blast has a 50% chance to die, and the city loses half its population.**
  CFC-CSE (Valant2, vanilla): "Has a 50% chance to destroy each unit caught in its blast area" and a nuke "reduces a
  city's population by 50%." WarpStorm (Apolyton thread 73776 below, 2002): "Every unit and building has a 50-50 chance
  of dying outright."
- **C3C 1.22 nuke effects (tested by justanick, CFC-BOMB122 #1):** "All nuclear weapons are immune against nuclear
  attack." / "All other units are destroyed at a chance of 2:1." / "All units that survive that loose 2 hitpoints." /
  "All units with 2 HP, 1 HP or no HP are killed instantly." / "Each building is destroyed with 50% probability." / "50%
  of all citizens are killed, the amount surviving is rounded up to integer." "2:1" is ambiguous (a 2/3 or a 1/2 kill
  chance); the vanilla-era 50% per unit fits "1 in 2". UNVERIFIED which. vmxa (thread 413455 below, #2) says
  survivors "lose half their hit points" and "Three hits will kill all units".
- **Blast area: the target tile and the 8 around it.** Proteus_MST
  (https://apolyton.net/forum/miscellaneous/archives/civ3-general-archive/73776-effects-of-nuclear-weapons, Sep 2002):
  "Every Unit at Ground Zero and all adjacent Tiles of a Nuke has a Chance of 50% of dying." vmxa
  (https://forums.civfanatics.com/threads/nuclear-weapons.413455/, #2, 2011): "It will wreck all 9 tiles in the radius
  of ground zero." / "They will be polluted and all roads and mines and such will be removed." Community, consistent.
  The exact fallout (pollution) pattern is UNVERIFIED.
- **ICBMs can't be moved at all, in any version** (community). Fiddlin Nero (thread 413455, #7): "In Vanilla/PTW you
  can't move the ICBM at all"; vmxa (#8): "You cannot move icbm's in C3C either". README-PTW: "ICBM's can no longer
  auto-bombard."

---

## 1.13 Capture, enslavement, pillage

- **Non-combat units caught alone are captured.** PEDIA `PRTO_Worker`: "Note that workers may be captured and used by
  rival civilizations." Artillery with no escort is captured too (thread
  https://apolyton.net/forum/miscellaneous/archives/civ3-general-archive/122803-siege-city-defense, Kef: "If they get
  captured, you're doing it wrong."). What a captured Settler becomes (Settler, or Worker(s)) is UNVERIFIED.
- **Captured workers are foreign and work at half speed** (C3C). README-C3C: "CAPTURED WORKERS: In order to
  differentiate a foreign (i.e., captured or enslaved) worker unit, it now displays different unit art than the
  standard worker." / "Note: Captured workers work at 50% the speed of regular workers."
- **Barbarians destroy the workers and settlers they catch.** https://apolyton.net/forum/civilization-series/civilization-iii/236659-unusual-barbarians
  (Quintillus): "They can't build them, and when they capture workers or settlers they are destroyed."
- **Any defense-0 unit is captured when an enemy enters its tile; attack-0 units can't take cities.** EDHELP *Defense
  Strength (Unit Statistics)*: "Note that units with a defense strength of 0 are automatically captured when an enemy
  unit moves into their square." EDHELP *Attack Strength*: "Note that units with an attack strength of 0 cannot capture
  cities." script.txt `#COMBATCONQUER`: "Only [combat] units can capture cities and improvements." / `#NONCOMBATANT`:
  "Non-combat units may not attack." EDHELP *Immobile*: an immobile unit "is not able to take any movement actions,
  though it may be captured."
- **You can only capture a unit type you could build; otherwise it is destroyed** (C3C, community).
  https://forums.civfanatics.com/threads/captured-units-dying-instead-of-switching-sides.622068/ (JustLuthor, #1):
  capturable units "just die off, instead of becoming mine"; Oni Ryuu (#2): "The unit must be available to your
  civilisation and you need to learn the tech before you can capture it." / "The unit cannot have an attack or defence
  value." tjs282 (#3) narrows it: a civ whose unique unit replaces a capturable unit can still capture the generic one,
  so "it's only having the right tech (and the capturable unit having A+D=0) that is important." Oni Ryuu (#5): "Other
  nations could capture the cannon but not the hwach'a because it never becomes available to them."
  **Code reading confirms it.** Flintlock (C3X-THREAD post-16577386, page-93 #1847): "the game does not
  let you capture units you couldn't have built yourself." Civinator (post-16377888, page-60 #1191):
  "Decisive for capturing a unit is, that the civ that attacks that unit can build the attacked unit
  themselves." Thorvald of Lym (post-16408200, page-67 #1321, community) adds that workers on a tile the attacker can't move
  into are lost: "they're killed instead of captured."
- **Air units caught by a land unit are destroyed, not captured** (community). Civinator (C3X-THREAD
  post-16616478, page-105 #2095): (paraphrase from a summarised fetch) "there is a special programming, that destroys every mobile air unit
  when a land unit of another civ is drawing on the tile"; tjs282 (post-16374946, page-58 #1152): air units (paraphrase from a summarised fetch) "in the
  base-game, get destroyed on capture". Alekseyev_ (#1153) says Hidden Nationality air units are an exception;
  UNVERIFIED.
- **Hidden Nationality and cities** (community, Civinator, C3X-THREAD post-16192126, page-25 #496):
  (paraphrase from a summarised fetch) "the human player can capture cities with HN land units, but only after declaring war to the owner of
  the city" and "The AI HN unit can capture the city without declaring war". The AI half looks like a bug.
- **Barbarians never enslave** (Civinator, C3X-THREAD post-16049378, page-4 #64: "Barbarians cannot
  enslave").
- **Captured settlers stay settlers (Civilopedia).** PEDIA `GCON_Nationality` (C3C and vanilla): "Captured workers and
  settlers retain their original nationality as well, and, if ordered to join a city, become foreign nationals within
  that city." A conflicting recollection, eris
  (https://forums.civfanatics.com/threads/capture-settler-but-get-a-worker-instead.314474/, #4, in a Civ4 thread): "In
  Civ 3, you got two workers for capturing a settler." No test found; the Civilopedia wording is preferred.
- **Capturing a unit counts as an act of war** (UNVERIFIED in a fetched source; one search excerpt says so).
- **The "Capture" unit action only applies to capturable special units** (kings, princesses) in game modes outside
  scope. EDHELP *Capture*: "the unit has the capability to capture units which are vulnerable to being captured (i.e.
  king units, princess units, etc.)"
- **Enslave (C3C): 1/3 chance after each win to turn the loser into a set unit type.** PEDIA `GCON_Enslavement`: "A unit
  with the "enslave" capability has a 1/3 chance of enslaving a defeated unit everytime it wins a battle. ... the unit
  is turned into another unit type (as specificed in the editor) and is immediately under the control of the victorious
  player." Stock enslavers: Privateer, English Man-O-War (makes another Man-O-War: `PRTO_Man-O-War`), Mayan Javelin
  Thrower (makes a Worker: `PRTO_Javelin_Thrower`). BIQ `slaveUnitType: Worker`. Only the attacker enslaves
  (https://forums.civfanatics.com/threads/anyone-know-exactly-how-enslavement-works.107253, Willem: "It's the unit that is doing the attacking.").
  Code (third round): the roll is out of 100 and succeeds below 33, so it is 33%, not exactly 1/3. C3X-SRC
  `patch_rand_int_to_enslave`: "lim is 100, enslaving happens if the return value is < 33". A kill by bombardment
  can also enslave in stock: C3X-INI's mod option "Stops units from enslaving a target they've destroyed by bombarding
  as opposed to direct combat", and the code credits a defensive-bombard kill to the bombarder through the same
  `Unit_score_kill`.
- **Pillage destroys a tile improvement.** EDHELP *Pillage*: "If checked, the selected unit may pillage terrain
  improvements." Pillaged barricades fall back to fortresses (PEDIA `GCON_Barricades`) and pillaged airfields are
  destroyed (PEDIA `GCON_Airfields`). Pillaging counts as the unit's move, except for C3C armies (TimBentley,
  https://forums.civfanatics.com/threads/question-concerning-healing-units.113864/, #13: "Pillaging counts as a move for
  units other than armies."; CFC-FAQ: "Armies in Conquests can also pillage without using up any movement points.").
  Which improvement goes first when a tile has several: one community statement, Vasyl-67 (C3X-THREAD
  post-16725406, page-124 #2468): "The order of destroying improvements on tiles is hard-coded in the
  game: railroad / road and mine (irrigation) / fort." Untested.
  **Pillaging a barricade costs all remaining moves; the AI won't pillage itself into a trap (code reading).**
  Flintlock (post-16401798, page-64 #1270): the pillage code "also checks whether the tile has a
  barricade, if it does the unit loses all of its remaining moves". Same post:
  "There is a condition stopping AI units from pillaging tiles if doing so would leave the unit itself
  stranded."

---

## 1.14 Upgrades

- **Where:** ground units in a city with a Barracks, ships with a Harbor, air units with an Airport.
  PEDIA `BLDG_Barracks`: "A city with a [Barracks] can be used to upgrade ground units." (`BLDG_Harbor`, `BLDG_Airport`
  say the same for naval and air units.) README-PTW: "Added information about upgrading units to civilopedia entries
  for Barracks, Harbor, and Airport." The new unit's strategic resources must be available in that city (Tom's
  Hardware, https://forums.tomshardware.com/threads/upgrades.167686/, "The Stare": "You must have any required
  strategic resources in that citys' resource display.").
- **Cost = (new shield cost - old shield cost) x multiplier gold. Multiplier 3 in C3C, 2 in vanilla/PTW.**
  EDHELP *Unit Upgrade Cost*: "if the base unit costs 20 shields and its upgrade costs 40 shields, the difference is 20
  shields. Under this example, then, the exact cost for the upgrade would be 60 shields (20 x 3 = 60)." BIQ
  `upgradeCostPerShield 3`. https://forums.civfanatics.com/threads/upgrade-cost.94826 (Ginger_Ale): "times 2 (in
  Vanilla civ and PtW) and times 3 in Conquests."
- **Leonardo's Workshop halves the cost.** PEDIA `BLDG_Inventor's_Workshop` (all three versions): "Obsolete military
  units can be upgraded at half price."
- **Experience is kept, except elite drops to veteran** (community, consistent). https://forums.civfanatics.com/threads/upgrade-or-disband.231677
  (Tone, #13): "elites are dowgraded to vets but all others hold their status." CerberusIV
  (https://codehappy.net/apolyton/threads/90368-1.htm, #3, 2003): "if you later upgrade the unit it goes back to being
  veteran and can again be promoted to Elite". Velociryx (https://codehappy.net/apolyton/threads/44520-1.htm, vanilla
  2002), on upgraded elites: "Win a couple more fights with them, and they're right back to their elite status." The
  Firaxis-relayed leader rule ("Each time a unit is upgraded, however, it can once again produce a great leader", APO-1GL)
  is consistent with an upgrade clearing elite status.
- **An upgraded unit is fully healed** (community; see 1.1 Healing: Pyrrhos, thread 266551).
- **Upgrade All (Shift-U) upgrades every unit of the active unit's type; fortified units stay fortified.** README-V:
  "Upgrade All: To use the Upgrade All order (SHIFT-U) you must activate a unit. All units of this type will then be
  upgraded." README-V 1.21f: "Upgrade all now leaves fortified units fortified."
- **Unique units are in the normal upgrade chain** (README-V 1.17f: "Civ-specific units are now part of the regular upgrade chain."; README-V 1.21f: "War chariots now upgrade to knights.").
- **Units inside armies can never be upgraded** (all versions). CFC-ARMY: "Units in armies cannot be upgraded." player1
  (https://codehappy.net/apolyton/threads/49454-1.htm, #19, 2002, second-hand): "FIRAXIS said several times, that
  UPGRADEABLE armes were too powerfull (unbalancing) in beta."
- **The upgrade target must be buildable in that city (code reading).** Flintlock (C3X-THREAD post-16636518,
  page-108 #2157): "you can't upgrade to a unit type unless you can also build that type in the city in
  question." / "This also explains why upgrading armies requires a military academy." (post-16750370,
  page-130 #2581): "the game relies on its usual CanBuildUnit function to gate upgrades", and
  "Upgrading all units in all cities is actually part of the base game, the hotkey for it is Shift+U."
  So resources, techs and the building check (Barracks, Harbor, Airport) all come from the build test.
- **A unit becomes unbuildable once its upgrade is buildable** (code reading). Flintlock (post-16832498, page-140
  #2794): "connecting iron for swordsmen annoyingly makes warriors unbuildable." A unique unit follows
  the same rule after its Golden Age: tjs282 (post-16978459, page-187 #3732) says it stays buildable only if
  "your UU is the last link in an upgrade-chain, or if you haven't fulfilled the requirements" (techs and
  resources) to build the upgrade; timerover51 checked the stock BIQ and agreed
  (#3733: "you are correct and I stand corrected"). Android Assembly (post-16884159, page-158 #3145):
  giving a unit an "innocent" upgrade target makes it go obsolete.
- **Elite status is lost on upgrade** (community): bacchant (C3X-THREAD post-16644543, page-110 #2198):
  "When a unit is upgraded it loses its 'elite' status." (Consistent with the elite-to-veteran drop above.)
- **Barracks-type buildings: veterans, healing and upgrades come from one flag** (community). tjs282 (C3X-THREAD
  post-16388661, page-62 #1239): "If the Barracks is modded so that doesn't produce vets, then it won't
  heal or upgrade units either".
- **Whether an upgraded unit keeps its remaining moves:** UNVERIFIED.

---

## 1.15 AI and difficulty

- **Difficulty does not change combat odds** except through the barbarian bonus. CFC-CSE (Valant2): the difficulty
  level "has NO effect on attack/defense calculations." No AI attack or defense bonus field exists in the editor's
  difficulty tab per CFC-RULES and EDHELP.
- **The AI gets free units, not combat bonuses.** Thread 37490 (Pyrkaige, editor table): "Number of extra defensive
  units: 0 0 0 2 4 8" and "Number of extra offensive units: 0 0 0 1 2 4" (Chieftain..Deity), and "At Monarch and above,
  the AI civs get extra offensive and defensive units when they start the game."
- **AI barbarian bonus.** TheNiceOne (thread 37490): "the AI has still a 200% bonus against barbarians even though the
  human player loses its entire bonus on deity." This conflicts with EDHELP's "any player (human or AI)" (see Disputed).

---

## Disputed

1. **Stacking of defense bonuses: additive vs multiplicative.** EDHELP calls the fortify, river and fortress bonuses
   "cumulative" and Valant2 (CFC-CSE) says "Bonus percentages add". vulture
   (https://apolyton.net/forum/miscellaneous/archives/civ3-strategy-archive/64407-civ-3-combat-calculations-with-retreat-odds):
   "I believe the factors combine together as I've done above, rather than adding together to give a 70% increase".
   The manual (p. 110, via royfurr in thread 7886) says terrain is figured "BEFORE THE CITY SIZE AND WALLS TAKE
   EFFECT", which some read as compounding. Additive has the Firaxis help text behind it, and Xerxes314's calculator
   and WarpStorm ("You add the bonuses first") also add. No test separating the two was found. The C3X mod's code
   (https://github.com/maxpetul/C3X/blob/master/injected_code.c) leans additive in two places, neither a disassembly
   of the unit-combat function: its tile-bombard re-implementation, which says it keeps the game's rounding, uses
   `base_defense * (100 + terrain_bonus + building_bonus) / 100`; and its defender-ranking helper
   (`counter_defender_selection_defensive_bonus_percent`) sums terrain, the town/city/metropolis bonus, the building
   bonus, fortress (barricade twice), fortified and river into one percentage before applying it.
2. **Fortify inside walls or a fortress.** EDHELP says cumulative. https://forums.civfanatics.com/threads/defense-bonuses-question.21715
   (search excerpt): "the wall bonus supercedes the fortification bonus" and "in a fortress, there is no point in
   Fortifying." A related claim is that units in cities count as fortified automatically. Neither claim was confirmed.
   (Thread 287626, "Fortress vs. Fortifications", was read and has nothing on this.)
3. ~~**Promotion against barbarians.**~~ *Resolved: halved.* The 1.22 code doubles the promotion ratio when the
   loser's civ ID is 0, "if ( !Target_Unit->Body.CivID ) _Comb_Exp_Promote_Ratio *= 2;" (CFC-LCODE #8, §1.2), which
   matches Theoden (thread 115547) and CFC-FAQ's table. Catt's "the promotion odds do not apply" (thread 80836) is not
   supported.
4. ~~**Army cost.**~~ *Resolved: 400 in every version.* The vanilla and PTW rule files store Army cost 40 in tens of
   shields, the same 400 as C3C (raw read, §1.4). CivFanatics FAQ v3.0's "200 shields" matches
   no default rule file.
5. **Do armies retreat?** https://forums.civfanatics.com/threads/are-armies-fast-units.84239/ (Longasc): "No, Armies
   NEVER retreat even if they have increased movement." Chieftess: "Armies DO retreat, atleast in C3C." README-V says an
   army inherits "mobility" only when all its units have it. DeepO (Apolyton 59461, vanilla): an army of Modern Armor
   "will not retreat from any other fastmover", which says nothing about slow opponents.
6. ~~**Retreat formula.**~~ *Resolved:* Antal1987's reading of the 1.22 code (526285 page 4, #62) has
   `random(defender bonus + 50) < attacker bonus`, which is Oystein's `100 * att / (def + 50)` (§1.5). Still open: the
   measured cap near 66 for edited bonuses.
7. **Barbarian bonus for the AI.** EDHELP: the bonus applies to "any player (human or AI)" at the game's difficulty.
   TheNiceOne (thread 37490): the AI always uses the Regent value (200%).
8. **Leonardo's Workshop.** Civ3's Civilopedia says it halves upgrade costs (all versions). A CFC post (Randy, thread
   94826) says "Militaristic Halves the upgrade cost of units", and some posters describe free upgrades (Civ2-style).
   Trust the Civilopedia; the militaristic claim is unconfirmed. Against it: maurcus tested it in
   https://forums.civfanatics.com/threads/things-civilopedia-cant-explain.470744/ (#4): "it doesnt appear militaristic
   does anything with upgrade costs". The opening post's halving came after capturing Leonardo's Workshop.
9. **ZOC hit chance.** Harovan (APO-ZOC): the shot always fires and works like a normal attack. Willem: "more like 30%".
   Flintlock's reverse-engineered `1 - 16D/(16D + A)` (thread 682034) is the most specific.
10. **Seafaring and sinking.** CFC-SINK found survival depends only on the trait (50% vs 75%). Older posts tie it to
    sea vs ocean squares; the unit's Sink in Sea / Sink in Ocean flags decide *where* it can sink, the trait decides how often.
11. **Military Academy's effect on armies: +25% or a smaller divisor.** EDHELP *Miscellaneous Effect Flags (Wonder
    Properties)*: "Increased Army Value Each army owned by a civilization that has built a structure with this attribute
    has its overall attack and defense values increased by 25%." CFC-ARMY, CFC-FAQ and Arathorn: the divisor in
    `A + TA/N` drops from 6 to 4. tom2050's tests
    (https://forums.civfanatics.com/threads/increased-army-value-flag.424279/, #14, 180 battles per setup): "Didn't see
    any difference", possibly because the wonder was placed in debug mode. Unresolved; the help text is Firaxis's, the
    formula is the community's. A raw read of the C3C BIQ shows the Military Academy carries the "Increased Army Value"
    flag (and the vanilla/PTW Military Academy does not), so the flag the help text describes is the one in play.
12. **Great Wall against barbarians: all combat, or only defense in its own city.** PEDIA `BLDG_Great_Wall`: "Doubles
    unit combat values versus $LINK<Barbarians=GCON_Barbarians>." EDHELP's flag text: "Doubles defense value vs
    Barbarians of city that the wonder resides in". Xerxes314's vanilla calculator applies +100% to attack and defense
    against barbarians. Unresolved.
13. **Vanilla/PTW army blitz.** README-V 1.17f: "Armies now have the zone of control and blitz ability." Puppeteer
    (thread 489054, #2): "Vanilla and PTW armies don't blitz; you're just seeing each unit in the army take a turn as the
    collective health goes down." The readme is Firaxis's; the post is a 2013 recollection.
14. ~~**Militaristic and promotion.**~~ *Resolved: Militaristic doubles the chance.* The routine Antal1987 posted
    (CFC-LCODE #8) halves the promotion ratio when `CheckBonus(race, 0)` is true, and index 0 is `RB_Militaristic` in
    the C3X header for the same executable (§1.2). His "no militaristic check" missed what that call tests.
15. **Can bombardment destroy Great Wonders?** *Engine ruling (owner, 2026-10-21): never, in any era.* justanick (CFC-BOMB122, C3C 1.22): yes, until the owner reaches the
    Industrial Age; zerksees (thread 278312, C3C 1.22) destroyed Sun Tzu's with a trebuchet. Dominus Romae (thread
    128868, #9, "At least in PTW 1.27f and C3C 1.22"): no. TimBentley (#12): the Great Wall itself can be bombed away in
    a town of size 6 or less, "a rather annoying bug". *C3X-THREAD (code reading):* Flintlock (post-16725029,
    page-124 #2465): wonders can be destroyed by bombardment "but only in the ancient era" (§1.7). That agrees with
    justanick that the window closes, but puts the end at the Middle Ages, not the Industrial Age, and does not say
    whose era counts. zerksees's Sun Tzu (a Middle Ages wonder) fits it only if the owner was still in the ancient
    era. Lean: Flintlock's reading, as the code source; the era test (owner's or attacker's) is open.
16. **Marines and city defense bonuses.** Sullla (strat8b, vanilla): Marines attacking from ships "ignore city defensive
    bonuses". alexman (APO-WALLS #1): a marine attacking a city still faces the wall bonus. alexman tested; Sullla's
    line is a one-line unit note. *C3X-THREAD:* Flintlock recalls from the combat-odds code that "Amphibious attacks
    definitely do get a 25% bonus" (post-16716406, §1.10); which side it favours is not stated, so it settles
    neither claim.
17. **PTW city-bombard targeting** (C3X-THREAD posters against alexman's test). See §1.7: alexman's PTW test gives
    50% unit / 25% population / 25% buildings; Predator145, BlueSox14 (relaying Sullla) and Flintlock (a
    recollection) give 1/3 each. Lean: alexman (tested). Out of scope for the C3C engine either way.
18. **Captured settlers.** PEDIA `GCON_Nationality` implies captured settlers stay settlers; eris (in a Civ4 thread)
    recalls two workers. Untested.

Resolved since the first pass: fast-defender retreat (CFC-RSTUDY: same formula on defense) and building/citizen
bombard defense 16 (README-V 1.17f, BIQ, alexman's tests) are now in the main text.

## UNVERIFIED / open questions

- The per-round formula A/(A+D) and 1 HP per round are community-derived (alexman's 2003 bombardment description
  assumes the same model). The code stores round odds on a 1024 scale (§1.1, via C3X). Rounding of modified strengths
  is shown only for tile bombardment (integer truncation after added bonuses); unit-vs-unit rounding is inferred.
- Stack defender: the exact weighting of defense and current HP, and the tie-break (lowest attack? cheapest?).
- Promotion chances: now read from the 1.22 code (§1.2). Still open: what the `Status & 2` bit that forces a
  promotion is, and so whether "two wins in a turn always promote" has any basis. (Third round: C3X's header names unit
  status bits 0x1 `USF_SKIPPED_FULL_TURN_WITH_DAMAGE`, 0x4 `USF_USED_ATTACK` "the unit has attacked at least once
  this turn", 0x8 `USF_PERFORMED_AIR_RECON`, 0x40 `USF_USED_DEFENSIVE_BOMBARD`, 0x80/0x100 sentry, and leaves 0x2
  unnamed, so the code readings do not identify it.)
- Leaders: whether naval or air elites could make leaders before C3C; how "more than one leader in one game turn"
  happens (presumably only after the first one is used).
- Retreat: whether a 3-MP unit can retreat from a 2-MP unit (two recollections say no, and C3X-THREAD posters
  WildWeazel and Shmelkin state "2+ can retreat from 1" as the stock rule, §1.5; still no test); whether a retreating attacker
  keeps its remaining moves; whether armies retreat; the cap above 66. The roll itself is now from code (§1.5).
- Defensive bombard: whether it rolls against the attacker's attack (tjs282, "IIRC") or defense. (That it never kills,
  even with a lethal unit, is now in §1.7 from C3X-INI. Third round: one shot per attack from the strongest eligible
  unit, and no shot at an attacker with defence below 1, which points to defence; §1.7.)
- Size of the "Bombardment in Ports" vulnerability. The C3C target order is fixed per C3X-INI ("always goes after units
  first"); whether walls come before units (justanick) is not stated there.
- Which units can fortify; whether units in cities get the fortify bonus automatically; whether fortifying speeds
  healing in a city without a Barracks.
- Movement: entering a tile with fewer moves left than its cost (one community statement says it is allowed, §1.9); the cost of crossing an unbridged river (one MP per
  community posts, untested).
- Naval: galleys in ocean after Navigation (CFC-FAQ says safe, the Galley entry does not say); what happens to cargo
  when a transport dies; whether ships can attack a coastal tile holding only non-combat units; whether units that land
  from ships lose their remaining moves.
- Air: which unit attacks and which defends in an interception battle (Zeekater, thread 100020 #4: "the defense of the
  bomber is weighed against the attack of the fighter, just like in regular combat", untested); whether a SAM Battery adds to mobile AA on its
  tile; whether a stealth fighter can intercept (MoonWolf, thread 86619: "stealth fighters haven't the ability to
  shoot down incoming bombers/fighters").
- Nukes: whether "2:1" means a 2/3 or 1/2 kill chance; the fallout pattern; launch restrictions.
- Automated bombard: whether it may hit civs we're at peace with, when in the turn it fires, and whether it fires at
  once when ordered (§1.7). Whether a go-to can board a ship at sea rests on one review comment (§1.10).
- Capture: whether capturing counts as declaring war; whether a Leader caught alone is captured or killed.
- Pillage: order of improvements removed (one untested statement, Vasyl-67, §1.13).
- Capture: the nationality of units the AI captures. Spoonwood (C3X-THREAD post-16305156, page-41 #808): "captured
  artillery type units for an AI don't have a nationality"; justanick (post-16305660, #816): "units captured by AI
  retain their nationality." Unresolved.
- Nukes and SDI: whether SDI stops tactical nukes as well as ICBMs (MattPilot and Erlend Philip Wood: yes;
  Alekseyev_: no, "IIRC"), §1.12.
- Upgrades: whether an upgraded unit keeps its moves. The elite-to-veteran drop and the full heal are consistent
  community reports but untested.
- Town walls giving ZOC: two community sources (civ-wiki.de, justanick), no Firaxis text.
- Every URL read for this section, with what it gave, is listed in `reading-log-01.md`.
