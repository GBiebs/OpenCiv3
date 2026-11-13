# 3. Happiness, culture and borders

How citizens' moods, civil disorder, celebrations, war weariness, culture, borders, culture flips, resistance and nationality work in Civ3 (base game 1.29f, PTW 1.27f, Conquests 1.22). Scenario-only and special game modes are excluded.

Where sources are cited:

- **Civilopedia (C3C)** is the game's own `Conquests/Text/Civilopedia.txt` (Steam "Civilization III Complete" install). It's a local file, so there's no URL.
- **C3C editor help** is `Conquests/Civ3ConquestsEdit.chm` (Firaxis' editor help, local).
- **labels.txt / script.txt** are `Conquests/Text/labels.txt` and `script.txt` (the game's own UI strings, local).
- **1.29f readme** is `README.txt` in the install root. It holds the base game's patch history from 1.16f to 1.29f (local).
- **Manual (Complete)** is https://cdn.akamai.steamstatic.com/steam/apps/3910/manuals/manual.pdf. Its text partly predates Conquests.
- **C3C BIQ** is the stock rules file `Conquests/conquests.biq` (Steam install, Conquests 1.22), read with this repo's `QueryCiv3` BIQ reader. Values are given as `SECTION.Field = value`. These are the shipped defaults, so they settle "what number does C3C use" questions, but not how the engine applies a number.
- **BIQ-V / BIQ-PTW** are the base-game `civ3mod.bic` (1.29f) and PTW `civ3PTW/civ3X.bix` (1.27f), decompressed with QueryCiv3's `Util.ReadFile` and read raw at the C3C field offsets (BLDG, GOVT, DIFF), as in section 2. The BLDG records are 252 bytes against C3C's 268, and the shared fields (costs, culture, content faces, flag bits such as the Library's +50% research and the Marketplace's +50% tax) read the same as C3C, which supports the offsets. Treat these as a careful raw read.
- **C3C editor help (What's New)** is the "What's New in this version of Civ3XEdit" page of the decompiled editor help.
- **C3X-THREAD** is https://forums.civfanatics.com/threads/c3x-exe-mod-including-bug-fixes-stack-bombard-and-much-more.666881/ (the C3X mod's forum thread, 2021-2026). Posts are cited as "post-NNNNNNNN" (thread URL plus `post-NNNNNNNN`) or "page-N #post-number". Flintlock, the C3X author, reads the 1.22 executable; his statements about "the base game" are code readings. Other posters report play, sometimes in modded games. Only statements about the unmodded game are used. Quotes were re-checked against the posts; the few that could not be are marked "(paraphrase from a summarised fetch)".

## 3.1 Content citizens and "overcrowding"

- **Base content citizens per city, by difficulty.** In each city the first N citizens are born content and the rest are born unhappy. The BIQ field is `DIFF.NumberOfCitizensBornContent`.

  | Difficulty | Chieftain | Warlord | Regent | Monarch | Emperor | Demigod | Deity | Sid |
  |---|---|---|---|---|---|---|---|---|
  | Born content | 4 | 3 | 2 | 2 | 1 | 1 | 1 | 1 |

  - Civilopedia (C3C), `#GCON_Overpopulation`: "In each city, a number of citizens are born content. Thereafter, the rest are born unhappy." The same table follows ("Chieftain 4 … Sid 1").
  - Spoonwood, https://forums.civfanatics.com/threads/overcrowding-happiness-mechanics.355989: "c(Cheiftain)=4 c(Warlord)=3 c(Regent)=2 c(Monarch)=2 c(Emperor)=1 c(Demi-God)=1 c(Deity)=1 c(Sid)=1."
  - C3C editor help, "Number of Citizens Born Content": "Determines how many citizens in a new city are born content for the selected difficulty level."
  - C3C BIQ: `DIFF.NumberOfCitizensBornContent` = 4, 3, 2, 2, 1, 1, 1, 1 (Chieftain to Sid), the same as the table.
- **The unhappiness reason for over-the-base citizens is "It's just way too crowded."** It depends only on that city's size and the difficulty level.
  - labels.txt `#CITIZEN_UNHAPPY_REASONS`: "\"It's just way too crowded.\""
  - Skanky Burns, https://apolyton.net/forum/miscellaneous/archives/civ3-strategy-archive/50384-its-just-way-too-crowded: it "is related soley to the population of that city". On city count: "I dont think its related to number of cities at all."
- **Civ3 has no empire-size ("number of cities") unhappiness.** The optimal number of cities (OCN) affects only corruption/waste and the Forbidden Palace requirement. No content face is removed for going over the OCN. The evidence is indirect but consistent:
  - Civ3's complete list of unhappiness reasons (labels.txt `#CITIZEN_UNHAPPY_REASONS`) is: way too crowded, "give peace a chance" (war weariness), "Stop the aggression against our mother country!", "All the propaganda surrounding your regime is confusing.", "No More Draft!…", "We cannot forget the cruel oppression you have bore down upon us." (hurry/forced labour), and "Some improvements in this/another city are annoying". None of them is about city count.
  - C3C editor help, "Optimal Number of Cities": "Determines the optimal number of cities before rampant corruption occurs."
  - CFC FAQ, https://civfanatics.com/civ3/faq/: OCN "has two main purposes": the Forbidden Palace check, and "If a player's number of cities exceeds that number, corruption will increase dramatically".
  - alexman et al., https://civfanatics.com/civ3/strategy/game-mechanics/everything-about-corruption-c3c-edition/, uses OCN only for rank corruption, with `Nopt = max(OCN * (L/100 * (1 + c + Gr + Gp*Nwe) + 0.25*Ni), 1)`. Here L is `DIFF.PercentageOfOptimalCities`.
  - The "riot factor"/"each new city adds an unhappy citizen" rule players remember is Civ2's. For example, DaveV, https://codehappy.net/apolyton/threads/2061-1.htm (a Civ2 thread from 2000): "Each time you exceed a multiple of the magic number, your happiness calculation goes up one difficulty level in some cities."

## 3.2 Faces: what makes citizens content or happy

- **Content faces vs happy faces.** A content face turns one unhappy citizen content. A happy face turns one content citizen happy. Buildings, wonders and military police give content faces. Luxuries, entertainers and the luxury slider give happy faces.
  - Civilopedia (C3C), `#GCON_Happy_Faces`: "Each [content face] makes one unhappy citizen content." / "Each [happy face] makes one content citizen happy." The table there lists: "City improvements content faces / Wonders content faces / Military police content faces / Luxuries happy faces / Entertainers happy faces / Entertainment happy faces".
- **Excess happy faces carry over to unhappy citizens one step at a time.** A happy face first makes a content citizen happy. Only when no content citizen is left does it make an unhappy citizen content, and a second face then makes that citizen happy. So it takes two happy faces to turn one unhappy citizen happy. This is the "reduced efficiency" the Civilopedia mentions. The rule comes from Firaxis' wording plus community worked examples; nobody quotes code for it.
  - Civilopedia (C3C), `#GCON_Happy_Faces`: "the remaining 3 happy faces will carry over to unhappy citizens at reduced efficiency."
  - Manual, luxuries: "Each type makes one content citizen happy or (if there are no content citizens) one unhappy citizen content."
  - justanick, https://forums.civfanatics.com/threads/mechanics-of-happy-content-faces.701169/ #2 (Dec 2025, C3C): "it takes 2 happy faces to turn an unhappy citizen into a happy one. So that is a two step process." #9: "Happy faces do only turn unhappy citizens content if no content citizen exists."
  - Spiffor, https://codehappy.net/apolyton/threads/83368-1.htm #2 (Apr 2003): "Content faces are used to turn unhappy citizens into content citizens." bongo, same thread, #15: "first content citizens are made happy, then unhappy are made content."
- **Order of application (community worked example).** For a Republic at war, justanick works a size-12 Chieftain city in this order:
  1. 4 born content and 8 born unhappy.
  2. Building and wonder content faces (Temple 1 + Mausoleum 3) turn 4 unhappy citizens content.
  3. War weariness (50%, i.e. 6) turns 6 content citizens unhappy.
  4. Happy faces from luxuries make the 2 remaining content citizens happy, then work on the unhappy at 2 faces each.

  The thread's starter confirmed the in-game result ("exactly as that WW thread I linked to predicts"). Where martial law and entertainers fall in this order is not covered (UNVERIFIED).
  - justanick, 701169 #2: "You start at 4 content and 8 unhappy citizens." / "4 content faces and 6 unhappy faces turn it into 2 content and 10 unhappy citizens."
- **Citizens per face.** This is the BIQ field `RULE.CitizensAffectedByEachHappyFace`, default 1.
  - C3C editor help: "Determines how many citizens' moods are improved by each \"happy face\"… Default is 1."

### Buildings (C3C Civilopedia)

| Building | Effect | Culture/turn (CFC Civilopedia) |
|---|---|---|
| Temple | 1 content face | 2 |
| Marketplace | +50% tax; boosts luxury resources (table below) | 0 |
| Colosseum | 2 content faces | 2 |
| Cathedral (needs Temple) | 3 content faces | 3 |
| Police Station | "combats war weariness", reduces corruption | 0 |
| Courthouse | reduces corruption, "more resistant to propaganda" | 0 |

- Civilopedia (C3C), `#BLDG_Temple`: "produces one content face, making one unhappy citizen content". `#BLDG_Colosseum`: "produces two content faces". `#BLDG_Cathedral`: "produces three content faces … You must have a Temple". `#BLDG_Police_Station`: "combats war weariness and decreases corruption".
- Culture values: https://civfanatics.com/civ3/civilopedia/improvements/ ("Temple 2 … Makes 1 unhappy citizen content"), and the CFC Info Center, https://civfanatics.com/civ3/infocenter/: "Cathedral(3), Colosseum(2), Library(3), Palace(1), Research Lab (2), Temple (2), University (4)". The Complete manual's culture chart (p.147) gives the same 3/2/3/1/2/2/4 sequence.
- C3C BIQ confirms the table: Temple `ContentFaces` = 1, Colosseum 2, Cathedral 3, with culture 2/2/3. Of the improvements, only the Police Station has the "Reduces War Weariness" flag, and only the Courthouse has the "Resistant to Propaganda" flag. The C3C editor help says that flag makes citizens "less likely to defect". No default building has an unhappy-faces value.

### Wonders affecting happiness

| Wonder | Effect (C3C Civilopedia) |
|---|---|
| Temple of Artemis (C3C) | "Puts a Temple in every city on the same continent." |
| The Oracle | "Doubles the effect of all Temples in the Empire; they produce two content faces instead of one." |
| Hanging Gardens | "makes three unhappy citizens content in its city and one unhappy citizen content in all other friendly cities." |
| Mausoleum of Mausollos | "makes three unhappy citizens content in its city." |
| Sistine Chapel (Michelangelo's) | "Doubles the effects of all Cathedrals in your cities." |
| JS Bach's Cathedral | "Decreases the number of unhappy citizens on the continent by two per city." |
| Shakespeare's Theater | "Eight unhappy citizens in the city are made content." |
| Cure for Cancer | "Makes one unhappy citizen content in every city." |
| Universal Suffrage | "Reduces war weariness in all cities." |

- The Civilopedia (C3C) `#GCON_Moods` list of happiness wonders is: Oracle, Mausoleum, Sistine Chapel, Hanging Gardens, Shakespeare's Theater, JS Bach's Cathedral, Cure for Cancer.
- **In C3C all of these give content faces, not happy faces.** The C3C BIQ holds them in the content-face fields:
  - Hanging Gardens: `ContentFaces` = 3, `ContentFacesAllCities` = 1.
  - Mausoleum: `ContentFaces` = 3.
  - Shakespeare's Theater: `ContentFaces` = 8.
  - JS Bach's: `ContentFaces` = 2 and `ContentFacesAllCities` = 2, with the "continental mood effects" flag.
  - Cure for Cancer: `ContentFaces` = 1, `ContentFacesAllCities` = 1.
  - The Oracle's "doubles happiness of" target is the Temple, and the Sistine Chapel's is the Cathedral.
  - The Temple of Artemis gives a free Temple in every city on its continent (`GainInEveryCityOnContinent`).
  - Universal Suffrage has the great-wonder flag "Reduced War Weariness". It has no face values.

  The manual and CFC "happy" wordings are older or loose (see Disputed).

## 3.3 Luxury resources and the luxury rate

- **Each distinct luxury type available to a city gives happy faces.** The city must be connected (by road, river, harbour or airport) to the luxury inside your borders or at a colony, or receive it by trade. Traded luxuries reach the cities connected to your capital.
  - Manual: "one content citizen of the city is made happy for each of these luxuries", plus "If you receive a luxury in trade, all your cities that are connected to your capital … have access to it".
  - Civilopedia (C3C), `#GCON_ResourcesL`: "When available to a city from foreign or domestic trade routes, luxury resources produce [happy faces], making content citizens happy."
- **Duplicates add nothing.** One source per type supplies the whole civ.
  - Manual: "it takes only one square's worth of a resource to supply your entire civilization, any surplus … is available for trading".
- **Diminishing (actually increasing) order with a Marketplace.** The bonus applies through the building flag "Increases Luxury Trade". Faces per luxury: 1st and 2nd 1 each, 3rd and 4th 2 each, 5th and 6th 3 each, 7th and 8th 4 each. Without a Marketplace each luxury is 1 face.

  | Luxuries | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
  |---|---|---|---|---|---|---|---|---|
  | Faces with Marketplace | 1 | 2 | 4 | 6 | 9 | 12 | 16 | 20 |

  - Civilopedia (C3C), `#BLDG_Marketplace`: "3 luxuries = 4 happy faces … 8 luxuries = 20 happy faces".
  - C3C editor help, "Increases Luxury Trade": "1st and 2nd luxury = 1 happy face 3rd and 4th luxury = 2 happy faces 5th and 6th luxury = 3 happy faces 7th and 8th luxury = 4 happy faces".
- **The luxury slider ("Entertainment")** turns commerce into happy faces, one face per luxury point.
  - Civilopedia (C3C), `#GCON_Commerce`: tax revenue goes partly to "The entertainment of the population to produce [happy faces]".
  - nbarclay, https://codehappy.net/apolyton/threads/61665-1.htm #1 (Sep 2002, base game): "For each gold spent, one citizen goes from unhappy to content or from content to happy."
  - justanick, https://forums.civfanatics.com/threads/mechanics-of-happy-content-faces.701169/ #2 (Dec 2025, C3C), on a 10% luxury rate turning 60 commerce into 6 happy faces: "Apart from possible rounding issues that is correct."
  - It is consistent with the entertainer: C3C BIQ `CTZN Entertainer.Luxuries` = 1 (§3.9), and the Civilopedia's "An entertainer produces one happy face". `RULE.CitizensAffectedByEachHappyFace` = 1.
  - How the luxury share is rounded per city is UNVERIFIED.
- **No default building boosts slider luxury in C3C.** The editor's building flag "+50% Luxury Output" ("luxury revenue produced in the city … is increased by 50%") is set on no building in the C3C BIQ. The Marketplace carries only "Increases Luxury Trade", which affects luxury *resources* (the table above). The C3C Civilopedia's Marketplace entry likewise mentions only tax and luxury resources. Players report the same:
  - Rhothaerill, https://codehappy.net/apolyton/threads/61665-1.htm #19 (Jul 2003, PTW era), on the Marketplace and luxury resources: "Neither one affects the happiness generated from the luxury slider."
  - vulture, https://codehappy.net/apolyton/threads/83368-1.htm #16 (Apr 2003): Marketplaces boost luxury resources but not the luxury slider (paraphrased by the fetch).
  - The base game and PTW are the same. In BIQ-V and BIQ-PTW the Marketplace has "+50% Commerce" and "Increases Luxury Trade" but not "+50% Luxury Output", and no building has that flag.
- **At most eleven luxuries count (code reading).** Flintlock (C3X-THREAD post-16541856, page-86 #1711): "cities are limited to eleven luxuries anyway" / (paraphrase from a summarised fetch) "Any more beyond that won't contribute happiness and won't appear on the interface." (post-16652030, page-112 #2240): "the game still has a soft limit of 11 luxury resources." Stock C3C has eight luxury types, so the limit only matters in mods.
- **Importing a luxury does not refresh happiness at once (stock bug).** Flintlock (C3X-THREAD post-16044363, page-3 #45): "the game does not recompute city happiness when you sign a deal to import a luxury". The intended rule is that the new luxury counts immediately.
- **Base-game luxury types:** Dyes, Furs, Gems, Incense, Ivory, Silk, Spice, Wine. Civilopedia (C3C), `#GCON_ResourcesL`, lists their tile yields, e.g. "Gems +0 +0 +4".

## 3.4 Martial law (military police)

- **Each military unit in the city makes one unhappy citizen content, up to a limit set by the government.** Only units with attack > 0 count. The unit cap is `GOVT.MilitaryPoliceLimit`. How many citizens each unit quells is a *difficulty-level* field, `DIFF` "Number of Citizens Quelled by Military", which is 1 at every level.
  - C3C editor help (What's New): "Added option to Difficulty Level Page for number of citizens quelled by a military unit. It defaults to 1 for all levels." Its page "Number of Citizens Quelled by Military" says: "Determines how many rioting-citizens are quelled with the presence of military units."
  - C3C BIQ: `DIFF.MilitaryLaw` = 1 for all eight levels. WillJ (§3.13) applies the same field to quelling resisters.
  - Manual: "A small number of military units, each with an attack factor of at least one, can be stationed in a city as military police. Each military unit makes one unhappy citizen in a city content."
  - C3C editor help: "Determines how many military units (units with attack rating greater than zero) can be garrisoned in a city to reduce unhappiness."

  | Government | Anarchy | Despotism | Monarchy | Feudalism | Communism | Fascism | Republic | Democracy |
  |---|---|---|---|---|---|---|---|---|
  | Military police limit | 0 | 2 | 3 | 3 | 4 | 4 | 0 | 0 |

  - Civilopedia (C3C) government entries, e.g. `#GOVT_Communism`: "Military Police Limit 4". The same figures appear at https://civfanatics.com/civ3/civilopedia/governments/.
  - C3C BIQ `GOVT.MilitaryPoliceLimit`: Anarchy 0, Despotism 2, Monarchy 3, Communism 4, Republic 0, Democracy 0, Fascism 4, Feudalism 3. These match the table.
  - Two CFC threads listed as martial-law sources turn out to be Civ2 threads: https://forums.civfanatics.com/threads/help-martial-law.86270 and https://forums.civfanatics.com/threads/martial-law-not-working.33328. Their "3 units" and "Communism 2 per unit" figures are Civ2 rules.
  - The manual's older text names only "Despotism, Monarchy, or Communism", because Feudalism and Fascism are PTW/C3C additions.

## 3.5 War weariness

- **Which governments suffer war weariness (C3C):**

  | Government | Despotism | Monarchy | Communism | Fascism | Anarchy | Feudalism | Republic | Democracy |
  |---|---|---|---|---|---|---|---|---|
  | War weariness | None | None | None | None | None | Low | Low | High |

  - The BIQ field is `GOVT.WarWeariness`, with editor labels "Citizens do not get unhappy / Citizens get unhappy / Citizens get very unhappy" (labels.txt).
  - Civilopedia (C3C), `#GCON_War_Weariness`: "citizens living under a Republic, Feudalism, or Democracy become [war weary]". Government entries, e.g. `#GOVT_Democracy`: "War Weariness High".
  - The base-game manual names only Republic and Democracy: "If you're ruling under a representative form of government (Republic or Democracy), war weariness makes your citizens unhappy." Feudalism didn't exist yet, so this is a version difference, not a conflict.
  - C3C BIQ `GOVT.WarWeariness`: Republic 1, Feudalism 1, Democracy 2, all others 0. So Feudalism has exactly the same setting as Republic ("Citizens get unhappy"). The engine reads only this per-government value, so Feudalism should use Republic's effect table. That last step is an inference; nobody has tested Feudalism.
- **What raises and lowers it** (Firaxis' description). Attacking raises it. Being declared on, or fighting at home, is more tolerable.
  - Civilopedia (C3C): "When [you] initiate the conflict your people will tolerate it for a while … When someone else declares the war, especially a traditional enemy, your people are more forgiving. Carrying on a defensive war, that is keeping your troops at home, is more tolerable than offensive campaigns."
  - Manual: "The most important factors in causing war weariness are stationing your units in a rival's territory, enemies having troops in your territory, declaring war, and engaging in battle. Having a rival declare war on you actually decreases war weariness".
- **Community-measured model** (credited to Bamspeedy and DaveMcW), from https://civfanatics.com/civ3/strategy/game-mechanics/how-does-war-weariness-work/:
  - **Points.** Each civ keeps war weariness points (wwp) against each other civ: "Each civ have one wwp number against each of the other civs."
  - **Gains:**
    - +1 for each lost unit without defence, each improvement pillaged or bombed, and each unit bombarded down to 1 hp.
    - "Add 2 wwp when a human attacker is defeated".
    - "Add 2 wwp when a unit with defence value is attacked. (Even if you win)".
    - "Add 16 wwp when a size 1 city is captured 17 wwp for bigger cities".
    - +1 per turn while you have units in enemy territory.
  - **Losses:**
    - "Subtract 30 wwp if the AI attacks you", unless the AI was provoked (e.g. by nuclear use or a failed spy mission).
    - "Subtract 1/20 of current wwp each turn in peace (round up)".
  - **Levels:** 0–30 no effect, 31–60 level 1, 61–90 level 2, 91–120 level 3, 121 and up level 4. A level of −1 (being attacked) makes 25% of people happy ("war happiness").
  - **Effects:**
    - Republic: L1 25%, L2 50%, L3 50%, L4 100% unhappy.
    - Democracy: "Level 1: 50% unhappy people", "Level 2: 100% unhappy people", "Level 3: Revolt".
    - Rounded down and summed over enemies, then reduced: "subtract 25% for police station and 1 for US (Universal Sufferage)".
  - **Decay example:** from 121 wwp at peace, "It will take 19 turns to get down to level 0, and 43 turn until the war is totally forgotten."
- **Follow-up findings in the forum thread** of that article, https://forums.civfanatics.com/threads/how-does-war-weariness-work.61628/ (5 pages, 2003–2015, PTW and C3C):
  - **Points build up under any government.** Oystein, #67 (Feb 2005): "The calculation of wwp is independant of government." Lord Emsworth, #92 (Dec 2012): you can build up WW points under a WW-immune government such as Monarchy without suffering from them. Soren Johnson (Firaxis), as relayed by Jaybe at https://codehappy.net/apolyton/threads/62518-1.htm #5 (Sep 2002): "Soren said recently that WW accumulates even if you are in a government with no WW." / "The government just suppresses the opportunity for the WW to be expressed."
  - **Points carry into a later war.** TimBentley, #66 (Feb 2005), on WW returning in a new war after peace and a government change: "Yes, you will (assuming you still have enough WW left after the peace)." Oystein, #55 (Apr 2004): "But 20 turns of peace does NOT mean that ww is gone."
  - **Only war expresses it.** justanick, https://forums.civfanatics.com/threads/mechanics-of-happy-content-faces.701169/ #6 (Dec 2025): "During peace WW is suspended. Only in war you can have war weariness." Soren Johnson (Firaxis), https://codehappy.net/apolyton/threads/55847-2.htm #33 (Sep 2002, vanilla/PTW): "there is a war weariness factor that is maintained the same way throughout your entire game" and "The only time this factor affects your citizens is if you are at war while in a demo/rep." Read literally, the points also build and persist under a government without war weariness, and show up after a switch to Republic or Democracy during a war.
  - **Rounding and caps.** Oystein, #47 (Mar 2004): "The number from ww is rounded down." Oystein, #24 (Oct 2003): "The total number of unhappy citizen from ww can not exceed number of citizen." / "The ww and wh are calculated independent." / "If you have 125% or more ww, the police station would not have any effect since you are above the limit anyway." That implies the per-enemy percentages add up past 100% before the Police Station's 25% comes off.
  - **Threshold.** Oystein, #51 (Apr 2004): "you need 31 wwp to get ww. 30 wwp gives no effect."
  - **Combat.** Oystein, #40 (Feb 2004): "If you attack and kill an AI, none gets wwp. If the AI wins, both get wwp." Also: "It does not mather where the battle are. I claimed so earlier, but that turned out to be wrong."
  - **Culture flips don't cause it.** Lord Emsworth, #80 (Mar 2009): "No WW from flipped cities. I checked for it just a few days ago with an old game." In #83 he adds that a recapture plus a flip did cause it.
  - **Universal Suffrage.** MadScot, quoted at #76 (Jan 2006): "Universal Suffrage makes 1 unhappy citizens content in all cities". This agrees with the article's "1 for US". In the C3C BIQ, US carries the great-wonder flag "Reduced War Weariness", and the Police Station the improvement flag "Reduces War Weariness". The editor help defines the improvement flag only as "the effects of war weariness in the city … are decreased", with no amount.
  - **AI bugs.** The thread reports AI war weariness being applied inconsistently, depending on playing order (Oystein, #1 and #42). alexman (Firaxis) first said Conquests fixed this (#25), then retracted it: "My bad. Sorry." (#27, Nov 2003).
- **Code readings on war weariness (C3X-THREAD, Flintlock, C3C 1.22).**
  - **The anarchy trigger is total WW over contacts, above 90.** Flintlock (post-16199613, page-27 #523): "a player gets kicked into anarchy if their WW is over 90", and "it checks against total WW divided by the number of contacts the player has." 91 is where the community's level 3 starts, so this fits "Democracy: Level 3: Revolt" above. It also shows that, for this test at least, the points are summed over enemies and divided by the number of civs the player has met (contacts, not just enemies). Arcangelus (post-16198175, #522): "When a Civ on a government with high WW reaches level 3, it falls into anarchy." Which governments the check applies to is not stated; the community tables give revolt only for Democracy.
  - **WW is charged when a battle starts.** Flintlock (post-16608179, page-103 #2060): "the WW gets added before the outcome of the battle has been determined". tjs282 (post-16607694, #2057): "the hardcoded +1 WWP for every attack on a friendly unit". This fits the article's "Add 2 wwp when a unit with defence value is attacked. (Even if you win)" in kind, not in amount.
  - **Two stores.** Flintlock (post-16336306, page-51 #1003): (paraphrase from a summarised fetch) "there are two types of WW that the game tracks, recent and long term". How they combine is not given. Later, hedged (post-16857204, page-150 #2981): "I'm pretty sure war weariness depends only on the number of units lost and whether they were lost in foreign territory."
  - **AI WW bugs (play reports, consistent with the article's bug note).** Arcangelus (post-16197906, page-26 #520): "In human-AI wars the AI gets the WW of the human." justanick (post-16335527 and post-16335628, page-50 #996/#998): in an AI-AI war "The AI later in the turn gets the WW points that it should get and the WW points its enemy should get on top of that.", and "the AI earlier in the turn order gets no WW points from the war and thus no war weariness." tjs282 (post-16198003, #521) wonders whether it is multiplayer only. These are shipped bugs; the intended rule is each side's own count.
  - Spoonwood (post-16704243, page-121 #2408): "Captured workers raise war weariness." (play report).
- **War happiness** (WW points below zero, e.g. after an AI declares war on you): one happy citizen per 4 citizens, per enemy.
  - Qitai, 61628 #35 (Feb 2004): "WH gives one happiness for every 4 pop." / "When it reaches zero, you lose the WH." / "once it reaches 30, you start to get WW for representative governments."
  - I. Larkin, https://forums.civfanatics.com/threads/war-happines-works.246590/ #1 (Oct 2007, C3C): it "is rounded down first and than added from each happy war". He also found that declarations the AI makes because of a military alliance or mutual protection pact give no war happiness.
- **Multiplayer.** alexman (Firaxis), https://codehappy.net/apolyton/threads/92121-1.htm #3 (Jul 2003): "I tested this once (I think it was under 1.21) and there was indeed war weariness in PBEM." / "However, the unhappiness levels rose much more slowly than in SP."
- **Democracy falls into Anarchy when war weariness reaches 100%.**
  - https://civfanatics.com/civ3/civilopedia/governments/: "It also happens when WW reaches 100% when under Democracy."
  - script.txt: "Our people are overthrowing our $GOVERNMENT0. Our Civilization is descending into Anarchy!"
- **Ending it.** Peace stops new war weariness, and so does eliminating the enemy civ.
  - Civilopedia (C3C): "The only way to end [war weariness] is to stop the war."
  - Civ3 Strategy Compilation, https://www.civfanatics.com/wp-content/uploads/2024/07/Civ3_Strat_Compilation.pdf: "Wiping out a Civ stops war weariness with it (just as peace would)".
- **There is no Senate in any Civ3 version.** War weariness replaces it.
  - Manual: "Republics and Democracies no longer have those pesky Senators refusing to let you go to war and forcing you into unwanted treaties. However, your citizens' war weariness affects your decisions in a similar way."
  - PTW/C3C: no source mentions a Senate (UNVERIFIED, but no readme adds one).

## 3.6 Nationality-based unhappiness, draft and hurry

- **"Stop the aggression against our mother country!"** Foreign nationals are unhappy while you're at war with their mother country, and become unhappy "more quickly than others". This applies under every government and is separate from war weariness. The exact count or fraction is UNVERIFIED.
  - Civilopedia (C3C), `#GCON_Nationality`: "Having a city with foreign nationals is not a problem unless you are at war with the nation that originally produced them. When at war, foreign nationals become unhappy more quickly than others and are more likely to be affected by hostile propaganda from their mother country."
  - labels.txt: "\"Stop the aggression against our mother country!\"".
  - Qitai, https://forums.civfanatics.com/threads/why-are-the-citizens-of-my-newly-conquered-cities-so-unhappy.79817/: they "will remain unhappy until you peace with the civ concern".
  - dunk, https://codehappy.net/apolyton/threads/57182-1.htm #7 (Jul 2002, base game): "Foreign citizens can always become war-weary regardless of government type." The count is still unsourced.
- **Draft.**
  - Requires Nationalism and a city of size 7 or more.
  - The draft limit per city per turn is Republic/Democracy 1, and Despotism, Monarchy, Feudalism, Communism and Fascism 2. Anarchy can't draft.
  - Each drafted citizen makes the city unhappy for 20 turns (`RULE.TurnPenaltyForEachDraftedCitizen`).
  - Civilopedia (C3C), `#GCON_Draft`: "You may only draft citizens from a city with a population of 7 or higher." / "drafting citizens will cause unhappiness in the city."
  - C3C editor help: "Determines how long your citizens' moods will be affected by each citizen drafted. Default is 20 turns."
  - 1.29f readme, Changes v1.21f: "Draft anger and hurrying unhappiness reduced to 20 turns."
  - C3C BIQ `GOVT.DraftLimit`: Anarchy 0, Despotism 2, Monarchy 2, Communism 2, Republic 1, Democracy 1, Fascism 2, Feudalism 2. `RULE.TurnPenaltyForEachDraftedCitizen` = 20. BIQ-V (1.29f) and BIQ-PTW (1.27f) have the same limits for their six governments (Anarchy 0, Despotism 2, Monarchy 2, Communism 2, Republic 1, Democracy 1), and the same assimilation chances (1, 1, 2, 4, 2, 4). Forum claims of 3 per turn for Communism or Fascism are wrong for the final patches. Examples: eldar and Ginger_Ale, https://forums.civfanatics.com/threads/question-about-drafting-in-civ3.107429/ #3/#5 (Dec 2004), and Toasty's "4 per turn under Communism, 3 per turn under Monarchy", https://forums.civfanatics.com/threads/how-do-i-draft-military.13697/ #4 (Jan 2002, early base game).
  - **The drafted unit is a conscript defender** chosen by tech level. Civilopedia (C3C), `#GCON_Draft`: "The draft produces the following [conscript] units, depending upon your level of scientific development: Spearman, Pikeman, Musketman, Rifleman, Infantry, Mech. Infantry".
  - Nationality: Toasty, 13697 #4 (Jan 2002, base game): "You can only draft the number of citizens that are of YOUR nationality in the city." ThePlagueRat's contrary report of drafting foreign citizens was questioned in the same thread, with no answer: https://codehappy.net/apolyton/threads/53252-1.htm #3/#4 (Jun 2002). Both are unconfirmed, so this stays UNVERIFIED.
- **Hurrying by forced labour.** This applies under Despotism, Communism, Feudalism and Fascism. Each sacrificed citizen causes unhappiness ("cruel oppression") for 20 turns (`RULE.TurnPenaltyForEachHurrySacrifice`). Hurrying is impossible while the city has resisters.
  - C3C editor help: "Default is 20 turns."
  - C3C BIQ: `RULE.TurnPenaltyForEachHurrySacrifice` = 20. `GOVT.Hurrying` is 1 for Despotism, Communism, Fascism and Feudalism, 2 for Monarchy, Republic and Democracy, and 0 for Anarchy. That fits forced labour = 1 and paid = 2; the enum mapping is inferred from the grouping.
  - 1.29f readme, Changes v1.17f: "Unhappiness from population rushing production increased to 40 turns." This was later cut to 20 in v1.21f (above).
  - script.txt `#HURRY_RESISTANCE`: "We cannot hurry production while our city is in resistance."
  - **Several rushes add up rather than overlap** (base game, Dec 2001, untested models). absimiliard, https://codehappy.net/apolyton/threads/37559-1.htm #2: "Those turns add up, so if you rush 3 pop you get your 60 turns." David Weldon (#6): "What is absolutely certain is that the penalties don't just overlap", with frequent rushers oppressed "over 250 turns"; his "citizen-turns" model (#4) is a guess ("I haven't done tests to prove it"). How the pool turns into unhappy faces each turn is UNVERIFIED.

## 3.7 Civil disorder

- **Trigger.** A city is in disorder when its unhappy citizens outnumber its happy ones. Content citizens, specialists and resisters are ignored.
  - Manual: "A city suffers civil disorder when unhappy people outnumber happy people. Resistors, content people, and Specialists are ignored in the calculation."
  - Civilopedia (C3C), `#GCON_Moods`: "Content citizens and specialists are ignored."
- **Effects.**
  - No shields, no commerce, and no surplus food is stored.
  - Each turn in disorder carries a `RULE.ChanceOfRioting` chance (default 20%) of a riot that can destroy improvements.
  - The city's culture flip chance is doubled (§3.11).
  - Prolonged disorder can overthrow the government.
  - A Nuclear Plant may melt down.
  - Order returns the turn after the cause is fixed.
  - Civilopedia (C3C), `#GCON_Disorder`: "Production ceases, No revenue comes in from commerce, and No excess food is stored." It continues: "Prolonged periods of civil disorder can lead to: Destruction of existing city improvements … Defection of the city … An overthrow of your government and descent of your empire into anarchy."
  - C3C editor help, "Chance of Rioting": "Determines the percentage chance of rioting occurring in any city experiencing disorder. Default is 20%."
  - Manual: "When order is restored, the city returns to normal operation the next turn." / "The risk of meltdown always exists when a city that has a Nuclear Plant goes into civil disorder."
  - C3C BIQ: `RULE.ChanceOfRioting` = 20.
- **Upkeep is still paid during disorder** (play report). Kokuryutei (C3X-THREAD post-16941923, page-180 #3583): "During riots, you don't receive any products or income from them, and the cost of maintenance doesn't go anywhere." Consistent with [PEDIA]'s list, which names no upkeep relief.
- **A riot destroys one improvement.** The game's only riot message names a single improvement per event. The engine follows the owner's ruling: one random non-wonder improvement. *Superseded in part:* the binary settles the exemptions (below); how the improvement is picked is still open.
  - **The binary** ([BINARY] Perez's decompile of the C3C executable, `doc/perez-findings/Civ3_Findings.md`, authoritative by the owner's ruling of 2026-11-12, "Happiness, disorder, celebration" and Round 2b): never in the capital; `Rand(100) < g_RuleRiotIntensifyChance` (a percent), then for a town (size ≤ MaxTown) a second `Rand(100) < 2×` that chance; then up to size + 20 tries of `Rand(bldgCount)`. Wonders, small wonders, flag-0x1 (palace-class) and aqueduct/sewer-class buildings are never destroyed. The engine reads the town roll as a second gate both must pass, and picks among the city's destroyable buildings rather than modelling the tries.
  - script.txt `#CIVIL_DISORDER_INTENSIFIES`: "Civil Disorder intensifies in $CITYNAME0! The mob has destroyed $IMPROVEMENT1!"
  - Civilopedia (C3C), `#GCON_Disorder`: "Destruction of existing city improvements or even sabotage to ongoing projects by angry mobs." No source describes the "sabotage to ongoing projects" part (UNVERIFIED).
- **Government overthrow.** How long "prolonged" disorder must last before the government falls is still UNVERIFIED. The collapses players report are Democracy falling to war weariness, with or without disorder:
  - camens, https://forums.civfanatics.com/threads/cant-stop-my-descent-into-anarchy.28256 #1 (Jul 2002, base game): "So even with no civil disorder I still fall into Anarchy."
  - LordKestrel, https://forums.civfanatics.com/threads/how-does-war-weariness-work.61628/ #58 (Jun 2004), on Democracy's war-weariness revolt: "Your civ descends into Anarchy, just like if you had clicked the Revolt button in the city advisor screen."
  - Egbert, https://apolyton.net/forum/miscellaneous/archives/civ3-general-archive/116300-various-questions-civ-improvements #3 (Oct 2003), hedged: "I vaguely recollect reading somewhere that anarchy may last longer if you leave cities in civil disorder".
  - The rule "if any of your cities is in disorder for more than 1 turn, your government will collapse into anarchy" turned up only as a web-search snippet; the thread wasn't identified or read. It comes from a list that also names the Senate and units outside cities causing unhappiness. Those are Civ2 Democracy rules, so it isn't evidence for Civ3.

## 3.8 We Love the King Day (celebration)

- **Conditions:**
  - not starving;
  - no resisters;
  - no unhappy citizens;
  - happy at least equal to content (see Disputed);
  - size at least `RULE.MinimumPopulationForWeLoveTheKing`, default 6.
  - Civilopedia (C3C), `#GCON_We_Love_The_King_Day`: "The city cannot be starving, The city cannot have any resisters, The city cannot have any unhappy citizens, There must be more happy citizens than content citizens, and The city must be large enough (typically size 6)."
  - Manual: "There must be at least as many happy citizens as content citizens" (example: "a city with five happy citizens, four content citizens, and no unhappy citizens celebrates").
  - C3C editor help: "Default is 6 citizens." C3C BIQ: `RULE.MinimumPopulationForWeLoveTheKing` = 6.
  - Dan Magaha (Firaxis), https://codehappy.net/apolyton/threads/44579-1.htm #3 (Mar 2002, base game), gives the same five conditions, including "There must be more happy citizens than content ones." and "The city must be large enough. This number is exposed in the editor, and defaults to a size 6 city."
- **Effects:**
  - The culture-flip chance is halved.
  - Waste (shield loss) is reduced. Commerce corruption isn't, per alexman.
  - Enemy propaganda is less effective.
  - The effects start on the first full turn of celebrating.
  - Civilopedia (C3C): "The chance of the city converting to another civ is halved, Waste in the city is reduced, and The effectiveness of enemy spy propaganda missions against the city is reduced."
  - Dan Magaha (Firaxis), 44579 #3: "The chance of a city converting to another civ are halved." / "The corruption and waste in a city is reduced, effectively increasing the city's shield output." / "The effectiveness of propaganda missions against the city are reduced." / "I don't think I missed anything...?" His effect is stated in shields, which fits alexman's waste-only finding.
  - alexman et al., https://civfanatics.com/civ3/strategy/game-mechanics/everything-about-corruption-c3c-edition/: "For waste calculations only, when the city is in a WLTKD celebration, divide da by 2." / "… add OCN/4 to Nopt."
  - Manual: "You will see the effects of celebration begin on the first full turn that a city celebrates".
  - 1.29f readme, Changes v1.21f: "Increased waste fighting ability of We Love The King Day."
- **Civ3 has no rapture growth and no per-government celebration bonus.** These sources contain none: the Civilopedia benefit list above is complete, and the C3C editor's government flags are unconditional. They are "Standard tile penalty – Under the selected government, no tile may generate more than 2 food and shields" and "Standard trade bonus – … each tile generates an additional +1 to commerce", with no "when celebrating" option. Rapture growth is a Civ1/Civ2 mechanic. Firaxis' own list of benefits (Magaha, above) has no government-specific item. Players at the time agreed: Thoth, https://codehappy.net/apolyton/threads/35096-1.htm #4 (Nov 2001), said WLTKD is "nowhere near as powerful as it was in Civ I/ II". The manual's sentence "the city enjoys certain benefits, depending on your civilization's type of government" reads as a Civ2 holdover (see Disputed).

## 3.9 Specialists

| Specialist | Base game 1.29f / PTW | C3C | Availability |
|---|---|---|---|
| Entertainer | 1 happy face | 1 happy face | always |
| Tax collector | 1 gold | 2 gold | always |
| Scientist | 1 beaker | 3 beakers | always |
| Police officer | – | reduces corruption/waste | Nationalism (C3C) |
| Civil engineer | – | +2 shields toward improvements and wonders only | Replaceable Parts (C3C) |

- Civilopedia (C3C), `#GCON_Specialists`: "An entertainer produces one happy face." / "a police officer can be used to help keep a lid on corruption" / "a Civil Engineer can be used to produce additional shields which can be applied toward the production of city Improvements and wonders."
- https://civfanatics.com/?p=145 ("The Role of the Specialist Citizen"):
  - "1 scientist equals 1 beaker in CivIII 1.29f and PTW" / "the scientist adds thee beakers" (C3C).
  - "The tax collector now adds two gpt" (C3C).
  - "Each hardhat adds two shields to the net shield output of the city when building city improvements".
  - "Corruption and waste do not reduce the specialist's output."
- **How the governor picks specialists (code reading).** Flintlock (C3X-THREAD post-16840588, page-143 #2849): the governor "tries to assign whichever specialist type gives the most happiness when adding specialists", and (paraphrase from a summarised fetch) "the game always recomputes a city's food balance whenever anything related to specialists changes." tjs282 (post-16843397, #2874): the policeman and civil-engineer functions "were added in Conquests".
- Specialists are ignored in the disorder test (§3.7) and count toward score: manual, "your content and happy citizens (including Specialists)".
- The BIQ CTZN section holds per-type `Luxuries`, `Research`, `Taxes`, `Corruption` and `Construction`. The C3C editor's "Luxury Bonus (Citizen Properties)" is "The bonus to a city's luxuries produced by one population point of the selected citizen type." C3C BIQ CTZN values:
  - Entertainer `Luxuries` = 1.
  - Tax Collector `Taxes` = 2.
  - Scientist `Research` = 3.
  - Policeman `Corruption` = 1.
  - Civil Engineer `Construction` = 2.

  So one entertainer is one luxury point, i.e. one happy face (§3.3). The Policeman and Civil Engineer need prerequisite techs, as in the table above.
- Early base-game players reported that specialists get no building multipliers. Sarxis, https://codehappy.net/apolyton/threads/31220-1.htm #1 (Oct 2001): "It looks like the multipliers that local city improvements gave to the scientists and taxman no longer apply." (Separately, the CFC article above says corruption doesn't reduce specialist output.) Whether Marketplace-type multipliers apply in C3C is UNVERIFIED.

## 3.10 Culture: points, doubling, totals, borders

- **Culture per turn.** Each building and wonder makes its BIQ `culture` value per turn. Per-city totals accumulate, and the civ's culture is the sum over its cities.
  - Civilopedia (C3C), `#GCON_Culture`: "All Wonders of the World and many city improvements generate culture points every turn." / "The sum of the cultural values of all of your cities is your [civilization's cultural value.]"
  - Building values are in §3.2.
  - **C3C great wonder culture (C3C BIQ `BLDG.Culture`):**

    | Wonder | Culture | Wonder | Culture | Wonder | Culture |
    |---|---|---|---|---|---|
    | Pyramids | 4 | Sistine Chapel | 6 | Theory of Evolution | 3 |
    | Hanging Gardens | 4 | Magellan's | 3 | United Nations | 4 |
    | Colossus | 3 | Copernicus' | 4 | Manhattan Project | 2 |
    | Great Lighthouse | 2 | Shakespeare's | 8 | Cure for Cancer | 3 |
    | Great Library | 6 | Leonardo's | 2 | Longevity | 3 |
    | Oracle | 4 | JS Bach's | 6 | SETI | 3 |
    | Great Wall | 2 | Newton's | 6 | Internet | 4 |
    | Sun Tzu's | 2 | Smith's | 3 | Temple of Artemis | 4 |
    | Statue of Zeus | 4 | Universal Suffrage | 4 | Hoover Dam | 2 |
    | Mausoleum | 2 | Knights Templar | 2 | | |

  - **C3C small wonder culture (C3C BIQ):** Heroic Epic 4, Iron Works 2, Forbidden Palace 2, Wall Street 2, Apollo Program 2, Military Academy 1, Pentagon 1, Strategic Missile Defense 1, Intelligence Agency 1, Battlefield Medicine 1, Secret Police HQ 0. Palace 1.
  - The Complete manual's chart (p.147) has different values for several wonders: Cure for Cancer 5, Great Library 5, Hoover Dam 3, JS Bach 5, Michelangelo 4, Newton 5, Shakespeare 5. CFC's Civilopedia has others again (see Disputed). Treat these as other versions' values; the BIQ is what C3C 1.22 ships.
- **Doubling after 1000 years.** A building's or wonder's culture doubles once it has stood 1000 game years. A rebuilt building starts over.
  - Civilopedia (C3C): "The amount of culture each Wonder or improvement contributes doubles after 1000 years."
  - 1.29f readme, Changes v1.17f: "Added mention of Wonder and improvement culture contributions doubling after 1000 years to Culture Civilopedia entry."
  - Turner, https://forums.civfanatics.com/threads/about-culture-points.116213, on rebuilt buildings: "those do not get back the 1000 yr doubling if it already had it."
  - This is game years, not turns. MTheil3508, https://forums.civfanatics.com/threads/culture.33034: "early in the game, the years go by faster".
  - Tested by The Mad Monk, https://codehappy.net/apolyton/threads/76110-1.htm #13 (Feb 2003). He compared the same building in two cities, one just under and one just over 1000 years old: "It's definately 1,000 years." / "you will find that the one improment is now producing twice the culture of the newer one."
  - The 1000 is counted in years. The C3X config (https://github.com/maxpetul/C3X/blob/master/default.c3x_config.ini): "Buildings double their culture production this many years after they were built. For standard game rules, set to 1000."
  - Corollary: with a 2050 AD end, anything built after 1050 AD never doubles.
  - **Code (2026-11-12):** Civ3 binary (Perez's decompile, `doc/perez-findings/Civ3_Findings.md`), "Culture and flipping": per-building culture is `BLDG.culture`, "doubled when the building is 1000+ years old", so the doubling starts at exactly 1000 years (inclusive, as the tourism bands do). The owner ruled on 2026-11-12 that the decompile is authoritative; the engine uses `>= 1000`. (An earlier save reading, a library from 620 AD still making 3 in 1620 AD, pointed to "more than 1000"; it is superseded.)
- **Mobilization halves culture.** Culture from improvements is halved while the civ is on a war footing.
  - Manual: "During wartime footing, cultural improvements produce half the number normally produced per turn."
  - CFC FAQ: "All culture per turn from your city is halved as well".
- **Fascism (C3C) is xenophobic.** A city makes no culture while fewer than half its citizens are of the owner's nationality.
  - C3C editor help, Government flags: "Xenophobic – Under the selected government, no culture is gained in cities where less than half of the population of the city is from the same race as the controlling civilization."
  - Civilopedia (C3C), `#GOVT_Fascism`: newly conquered cities "do not generate culture until a popular majority has been assimilated."
  - C3C BIQ: `GOVT.Xenophobic` = 1 for Fascism only.
  - Borders follow: tjs282, https://forums.civfanatics.com/threads/problem-culture-border-of-city-doesnt-expand.537508/ #2 (Nov 2014, C3C, Fascism): "you don't get any cultural expansion from a captured city until your Civ's citizens outnumber the 'foreigners'."
- **Per-civ memory in each city; capture.**
  - A city stores how much culture each civ made in it.
  - On capture, every building with culture > 0 is destroyed, plus 25% of other buildings and all small wonders. Great wonders survive, but give the captor no culture.
  - The captor gets the city's culture only with the scenario flag "Retain Culture on Capture".
  - C3C editor help, "Retain Culture on Capture": "the full cultural value of a city is retained by a conquering civ when an enemy captures that city, even though all possible buildings with a culture value > 0 are still destroyed, as well as 25% of other buildings and any small wonder (as normal)."
  - ainwood, https://forums.civfanatics.com/threads/about-culture-points.116213: "The city stores the culture generated by the buildings of each civilization that has owned the city at any time."
  - Turner, same thread: "captured wonders do not give culture to the capturing civ."
  - 1.29f readme, Changes v1.16f: "Capturing a population 1 city that has no culture will automatically raze it."
- **Border levels by city culture.** With the default `RULE.BorderFactor` of 10, the levels are at 0, 10, 100, 1000, 10000 and 100000 city culture, i.e. `BorderFactor^n`. Each level grows the border radius by one. A new city starts at level 0 with radius 1 (its 9 tiles). For the shape of each radius, see "Distance for border ranks" below (community, not fully tested). C3C BIQ: `RULE.BorderFactor` = 10.
  - C3C editor help, "Border Factor": "There are 6 levels of border expansion and under the default Border Factor of 10, they occur at 0, 10, 100, 1000, 10000, 100000."
  - CFC Info Center, https://civfanatics.com/civ3/infocenter/: "When culture reaches a certain point, the radius of your borders grows by one." / "The first increase comes at 10 Culture points" / "The amount you need to grow your borders increase by a factor of 10 each time".
  - Renata, https://forums.civfanatics.com/threads/culture-expansion.52924: "a city's original nine tiles".
  - The C3C Civilopedia instead lists 20,000 as the fifth threshold (see Disputed).
  - 1.29f readme, Changes v1.16f: "Editor: On General Settings, added Border Factor which controls border expansion."
  - **Code (2026-11-12):** Civ3 binary (Perez's decompile, `doc/perez-findings/Civ3_Findings.md`), "Culture and flipping": "Culture level = 1 + number of times `g_RuleCultureBase` fits into the owner's city culture, max 6; it drives borders." The level is capped at 6, whatever the culture. The engine reads "fits" as successive powers of `RULE.BorderFactor`, as the editor help describes, and caps the level at 6 (`City.GetBorderExpansionLevel`). That `g_RuleCultureBase` is the Border Factor is our reading; the decompile does not name the BIQ field.
- **Civ-wide culture levels** are a separate scale (`RULE` "Lvl. Multiplier", default 1000). They are used for the cultural level labels, not borders.
  - C3C editor help, "Level Multiplier": "Under the default level multiplier of 1000, the values for these cultural levels are 1000, 2000, 4000, 8000, 16000, and 32000."
  - What these levels do in play beyond labels is UNVERIFIED. script.txt uses them only as adjectives (`$CULTURAL_LEVEL`, e.g. "We have destroyed the $CULTURAL_LEVEL3 $CIVNOUN0").
- **Borders never shrink for culture reasons, except to a stronger neighbour.** On contested tiles, the closer city keeps its own first ring.
  - CFC Info Center: "Your borders never shrink due to culture." / "the only real way for your borders to shrink is if the borders of a nearby city with higher culture expand".
  - Civilopedia (C3C), `#GCON_Culture`: civ culture "comes into play … when you and a rival both have a legitimate claim on a terrain square."
  - Sirp and Shillen, https://forums.civfanatics.com/threads/culture-expansion.52924: you can't take a tile closer to another civ's city unless that city exerts no cultural control over it. Shillen: "Their city will only control their first ring, while you control their second ring." Renata retracted her "equidistant only" claim: "Yep, you're both right."
- **Border assignment: TemporalAnomaly's "Eight Laws"** (community, C3C), https://forums.civfanatics.com/threads/the-eight-laws-of-border-dynamics.106882/ #1 (Dec 2004):
  - Each city "places claims" on tiles. A city at its nth culture threshold "gains the ability to place claims of rank n+1 on all tiles at a distance n+1", where "threshold values are of the form 10^n c.p." (Law I).
  - "Ocean tiles may only hold claims of rank 2." (Law II)
  - "Each tile is assigned to the civ that controls the city that has the lowest rank claim on it." (Law III) So a tile goes to the city it is *nearest* in rank, which is why a city keeps its own first ring.
  - Ties at the same rank go to "the competing city with the largest culture" (Law IV). If culture is equal, the tile goes to the older city (Law V). If the founding year is also equal, the tile checks N, NW, W, SW and S in that order (Law VI).
  - Gaps get filled. An unclaimed tile with one civ's tiles both NW and SE of it (Law VII), or both NE and SW (Law VIII), goes to that civ. Law VII is checked first.
- **Distance for border ranks** (punkbass2000, same thread, page 2 #29, Dec 2004): "adjacent tiles are counted as 1, and tiles that only connect at a single point are counted as 1.5" / "these numbers are rounded down."
  - That is ⌊max(dx,dy) + ½·min(dx,dy)⌋, the metric the flip calculators also use (§3.15).
  - Under it, radius 1 is the 8 neighbours and radius 2 is the 21-tile fat cross ((2,2) gives 3, so it is out).
  - punkbass2000 adds: "I haven't fully tested this, so let me know if its wrong."
- **Disputes in that thread:**
  - Crimso (#2) says the city that expands over a tile first can keep it.
  - Theoden (#5) says a tile taken through Law VII/VIII can still be claimed by another civ's real culture expansion.
  - TemporalAnomaly (#4): "there is a city age affect", which became Law V.

  Whether civ-wide culture (as opposed to city culture) plays any part in contested tiles is UNVERIFIED. The Civilopedia's "legitimate claim" sentence is the only hint.
- **What borders do** (Civilopedia (C3C), `#GCON_Territory`):
  - "Rival civilizations cannot benefit from the roads or rails within your territory without a right of passage agreement."
  - "You can always 'see' squares in friendly territory."
  - Resources inside borders need no colony.
  - "You can eject rival military units from your territory".
  - The city radius is only the tiles within 2 that are inside the city's borders (`#GCON_Radius`): "within the city's cultural borders, and within two squares of the city."
- **Tourist attractions.** A great wonder older than 1000 years earns gold for its city, but only wonders with the "Tourist Attraction" flag do.
  - Civilopedia (C3C), `#GCON_Tourist_Attraction`: "1000 (years) - 1500 +2 (gold) … 2251 - 2500 +12". Wonder entries say so one by one, e.g. `#BLDG_Mausoleum`: "This Great Wonder can become a tourist attraction."
  - C3C BIQ flags it on: Pyramids, Hanging Gardens, Colossus, Great Lighthouse, Great Library, Oracle, Great Wall, Sistine Chapel, Copernicus, Shakespeare, Leonardo, JS Bach, Newton, Hoover Dam, United Nations, Temple of Artemis, Statue of Zeus, Mausoleum.
  - It is not set on: Sun Tzu, Magellan, Smith's, Universal Suffrage, Theory of Evolution, Manhattan Project, Cure for Cancer, Longevity, SETI, Internet, Knights Templar.
  - The bit's decoding comes from the repo's BIQ reader. It agrees exactly with the Civilopedia (C3C): the 18 flagged wonders are exactly the 18 entries that say "can become a tourist attraction".
  - The age is counted in game years, not turns. The C3X mod's config, describing the 1.22 code (https://github.com/maxpetul/C3X/blob/master/default.c3x_config.ini): "the standard 1000 years" for 2 GPT and "the standard 1500" for 4 GPT.

## 3.11 Culture victory (as relevant)

- **Thresholds.** One city with 20,000 culture wins. The civ-wide threshold depends on map size: Tiny 60,000, Small 80,000, Standard 100,000, Large 130,000, Huge 160,000. The BIQ fields are `GAME.OneCityCultureWin` and `GAME.AllCitiesCultureWin`.
  - Civilopedia (C3C), `#GCON_Victory`: "A single city with 20,000 culture points will satisfy this victory condition." … "Tiny: 60,000 Small: 80,000 Standard: 100,000 Large: 130,000 Huge: 160,000".
  - CFC FAQ (standard map): "have either 20,000 culture in one city, or 100,000 culture across your empire".
  - The rule "no other civ can have over half your culture points" (dac, https://apolyton.net/forum/miscellaneous/archives/civ3-strategy-archive/58212-culture-victory) is from memory: UNVERIFIED.

## 3.12 Cultural opinion: diplomacy, propaganda, resistance

- **Cultural opinion.** Each civ holds a cultural opinion of every other, set by their culture ratio. Defaults are from Dan Magaha (Firaxis), https://apolyton.net/forum/miscellaneous/archives/civ3-strategy-archive/42123-propaganda-chances:

  | Opinion (target's view of you) | Your culture : theirs | Propaganda success | Initial resistance | Continued resistance (per resister per turn) |
  |---|---|---|---|---|
  | disdainful of | 1:3 | 3% | 90% | 80% |
  | dismissive of | 1:2 | 5% | 80% | 70% |
  | unimpressed by | 3:4 | 10% | 70% | 60% |
  | impressed with | 1:1 | 20% | 60% | 50% |
  | admirers of | 2:1 | 25% | 50% | 40% |
  | in awe of | 3:1 | 30% | 40% | 30% |

  - Magaha: "Chance of resistance means that if you take one of their cities, the chance of resisters in the city is 90%," / "and each turn there is an 80% chance that each resister will continue to resist." / "the more culture advantage you have over another civ, the easier it is to take over cities".
  - C3C editor help, "Culture Ratio": "if the Germans had 3000 culture points and the Americans had only 1000, the Americans would be said to be \"in awe of\" the Germans."
  - The BIQ CULT section has `ChanceOfSuccessfulPropaganda`, `CultureRatioPercentage`, `CultureRatioNumerator/Denominator`, `InitialResistanceChance` and `ContinuedResistanceChance`.
  - C3C BIQ: the six CULT records match Magaha's table exactly. For example, "in awe of" is propaganda 30, ratio 300% (3/1), initial 40, continued 30; "disdainful of" is 3, 33% (1/3), 90, 80.
- **Diplomacy.** Culture affects AI attitude, and the diplomacy screen shows the opinion. The size of the effect is UNVERIFIED.
  - Civilopedia (C3C), `#GCON_Culture`: civ culture "comes into play when you … conduct diplomacy with a rival".
  - script.txt: "Our people are [$CULTURAL_OPINION0] the $CIVADJ1 cultural achievements."
- **Propaganda (spy mission).** A success makes the city defect. Some sources say it can instead cause only unhappiness.
  - Democracies are immune. Courthouses, capitals and WLTKD resist it. Cities in disorder are easier targets.
  - Manual: "Cities of a Democracy are immune to propaganda. Enemy capitals and cities with Courthouses are less likely to revolt. Also, it is easier to push a city already in civil disorder into open revolt".
  - Bluemofia, https://forums.civfanatics.com/threads/da-rules-explained.210035/post-5161431: "propaganda will either cause the citizens to join your civilization, or simply cause unhappiness."
  - labels.txt has the unhappiness reason "All the propaganda surrounding your regime is confusing."
  - script.txt has both outcomes as messages to the target civ: `#ESPIONAGE_MISSED_PROPAGANDA_LOST_CITY` "We have lost $CITY0 because of a propaganda campaign!" and `#ESPIONAGE_MISSED_PROPAGANDA` "A propaganda campaign in $CITY0 has upset its citizens." The spy's side reads "The citizens of $CITY0 have overthrown their government and joined our cause!" on success, and "…are not sufficiently sympathetic to our cause" on failure. So a propaganda campaign can end in defection or in unhappiness only.
  - C3C editor help, "Resistant to Propaganda (Improvement Flag)": "citizens in the city in which the selected improvement is built are less likely to defect." In the C3C BIQ only the Courthouse has it.

## 3.13 Resistance in captured cities

- **Which citizens may resist.** On capture, each foreign national of the defeated civ may resist, with the "initial resistance" chance from the opinion table. Your own former citizens don't resist when you retake a city, but citizens born under the enemy do.
  - Civilopedia (C3C), `#GCON_Resistance`: "a comparison of the civilization cultural values of the two civilizations determines if any of the foreign nationals will resist."
  - Manual: "If you recapture one of your cities from the Romans, the folks who were previously under your rule won't resist. Any new citizens created after the city was originally taken, however, might resist".
- **Resisters:**
  - don't work;
  - don't eat;
  - block hurrying;
  - block WLTKD;
  - count double toward culture flips.
  - Civilopedia (C3C): "Resisters are unwilling to work under the new regime."
  - 1.29f readme, Changes v1.29f: "Corrected Resistance Civilopedia chapter to reflect resisting citizens do not consume food."
  - Flips: §3.14.
- **Ending resistance.** Each turn, each resister keeps resisting with the "continued" chance, modified by the owner's government and by the garrison ("the more the better"). Making peace with the mother country ends resistance.
  - C3C editor help, "Resistance Modifier (Government Properties)": "the modifier to a single population point's chance of continuing to resist each turn. Positive numbers mean that the resisting population has a greater chance of continuing to resist".
  - Civilopedia (C3C): "Resisters can be quelled by ending the war or garrisoning strong military units in the city -- the more the better."
  - Manual: "They stay that way until you make peace with their mother country or 'win them over'". Also: "A more impressive culture, a government that allows more personal freedom, and a greater supply of luxuries all help."
  - script.txt: "Our troops have quelled $NUM0 $<#0:resister:resisters>!"
- **Garrison rule (WillJ, from Mike B. of Firaxis).** Each unit can quell at most one resister per turn, whatever its strength. That is, units × the difficulty's "citizens quelled by military", which is 1 by default (§3.4). The garrison caps how many of the resisters that fail their "continued resistance" roll actually stop resisting.
  - https://civfanatics.com/civ3/strategy/game-mechanics/the-inner-workings-of-resistance-revealed/ and its forum thread https://forums.civfanatics.com/threads/the-inner-workings-of-resistance-revealed.56487/ (WillJ, Jun 2003, PTW). The article thanks "forum member Mike B. FIRAXIS", and "Much of the credit for the information in this article goes to him."
  - "the maximum number of resistors that can be quelled on one turn is the number of units stationed in that city." / "Note that unit strength does not matter."
  - Excluded: "Air units, water units, artillery units, settlers, workers, and other non-ground and/or non-combat units".
  - Nothing else matters: happiness, "distance to capital, culture of the city captured, etc., doesn't matter either."
  - Example from the article: with 6 resisters at a 50% continuation chance, about 3 are expected to give up, but with only 2 ground combat units only 2 can be quelled.
  - Resisters "are the first in line to die from starvation and disease".
  - Test: KrikkitTwo, 56487 page 2 #24 (Jun 2005), with a modified ruleset: "So the quelling process is ONLY based on the number of quellers (military units)". A city "will never get rid of resisters without units". WillJ, #23: for resistance alone "there's no point in having more units than the number of resistors".
  - How armies count is UNVERIFIED. vondrack and DeepO, https://codehappy.net/apolyton/threads/58829-1.htm #2/#6 (Aug 2002), guess "1-4 units, depending on how many of them is actually in the army" (DeepO: maybe +1 for the army itself). Neither tested it.
- **Government modifier on resistance (±5 points).** Governments rank Democracy, Communism, Republic, Monarchy, Despotism, Anarchy. If the capturer's government ranks higher than the loser's, the resistance chances (initial and continued) drop 5 points; if lower, they rise 5. There are two exceptions where they rise anyway: a Republic capturing from a Monarchy, and a Democracy capturing from a Communist civ.
  - WillJ (article): "If your gov is higher up on the above list than theirs, it goes down by 5%." / "republic civ captures city of civ under monarchy: +5%" / "democratic civ captures city of communist civ: +5%".
  - C3C BIQ, the per-government-pair `ResistanceModifier` (row = capturer, column = loser), confirms this and adds the C3C governments:
    - Democracy vs Anarchy, Despotism, Monarchy and Republic: −5. Democracy vs Communism: +5.
    - Republic vs Anarchy and Despotism: −5. Republic vs Monarchy, Communism and Democracy: +5.
    - Communism vs Democracy: +5. Communism vs Anarchy, Despotism, Monarchy and Republic: −5.
    - Monarchy vs Anarchy and Despotism: −5. Monarchy vs Communism, Republic and Democracy: +5.
    - Despotism vs Anarchy: −5. Despotism vs Monarchy, Communism, Republic and Democracy: +5.
    - Anarchy vs the five other base-game governments: +5.
    - Fascism: −5 vs Anarchy, Despotism, Republic and Democracy; −10 vs Communism; 0 vs Monarchy.
    - Feudalism: −5 vs Anarchy and Despotism; +5 vs Monarchy, Communism and Fascism; 0 vs Republic and Democracy.
    - Every government vs Fascism or Feudalism is 0, except Feudalism vs Fascism (+5).
  - The editor help calls the field "the modifier to a single population point's chance of continuing to resist each turn". WillJ's charts apply it to the initial chance as well.

## 3.14 Assimilation and nationality

- **Nationality.** Every citizen has a nationality. Citizens born in a city take the owner's nationality; captured citizens keep theirs. Captured workers or settlers that join a city become foreign nationals.
  - Civilopedia (C3C), `#GCON_Nationality`: "Each civilization produces citizens of its own [nationality], a characteristic that persists until assimilated" / "Captured workers and settlers retain their original nationality as well, and, if ordered to join a city, become foreign nationals within that city."
  - CFC Info Center: "the citizens in the conquered city will retain their nationality".
- **Assimilation (what the sources say; the engine follows the owner's ruling that foreign citizens never assimilate, though they can be starved out).** Each turn, each foreign national has a per-government percentage chance (`GOVT.AssimilationChance`) of taking the city's nationality. A culture flip does not change citizens' nationality.
  - C3C BIQ `GOVT.AssimilationChance`: Anarchy 1, Despotism 1, Monarchy 2, Communism 4, Republic 2, Democracy 4, Fascism 2, Feudalism 3.
  - WarpStorm, https://apolyton.net/forum/miscellaneous/archives/civ3-general-archive/81314-nationality-assimilation #3 (Nov 2002), gives the same pre-C3C figures: "Communism and Democracy have the best chance of assimilation with a 4% chance a turn (per citizen I think)." / "Republic and Monarchy are slower with a 2% chance." / "The rest are very slow with only a 1% chance."
  - **A precondition, per Firaxis' text:** the city must have been yours longer than it was theirs. Civilopedia (C3C), `#GCON_Assimilation`: "When a foreign national has been a part of a minority population longer than not, he or she assimilates and becomes a member of the majority nationality." Mad Bomber, 81314 #5: "assimilation begins at the point in which a city has belonged to your civ more than the civ that possessed" it, so "if you capture Paris (founded turn #1) at turn # 271 then you will never be able to assimilate the population." Traelin, #6: "It only works this way if the Civ that originally contained the city is still in the game." player1, https://codehappy.net/apolyton/threads/57182-1.htm #3 (Jul 2002), heard instead "(by Firaxis)" that "assimilation only starts after you have more of your own pop in that city then foreigners".
  - **Editor tests found no effect, which supports the owner's ruling.** Hondo, https://apolyton.net/forum/miscellaneous/archives/civ3-general-archive/44876-holding-onto-conquered-cities #1 (Nov 2001, base game 1.0x): "No matter what percentage I set this to it doesn't seem to make any difference." whosurdaddy, 57182 #1 (Jul 2002): "I set the assimlation rates to 90% for each government type", and after "20 or 30 turns" foreign citizens "are still retaining their old nationality"; "this editor option does not completely control the assimilation percentages". Both tests are consistent with the time precondition above, so neither shows that assimilation never happens.
  - C3C editor help: "Determines the percentage chance, per turn, that a foreign national in city with the selected government type will assimilate into the current nationality of that city."
  - alexman (Firaxis), https://codehappy.net/apolyton/threads/51715-4.htm #118: "the rate is between a 1 and 4 percent chance per citizen per turn, depending on your government."
  - CFC Info Center: "How fast this happens depends both on their national culture and the culture of the city they currently reside in," "as well as the government type of their current city."
  - CKS, https://forums.civfanatics.com/threads/culture-flips.661194/: "Your citizens don't change nationality in a flip".
- **Effects of nationality:**
  - culture-flip input F (§3.15);
  - resistance (§3.13);
  - "mother country" unhappiness while at war (§3.6);
  - greater vulnerability to the mother country's propaganda;
  - lower city defence while resisting;
  - Fascist culture gating (§3.10).
  - CFC Info Center: resisters "will lower the defense value of the city so that Babylonian forces can easily retake it."

## 3.15 Culture flips (cultural reversion / "City Defection")

- **Firaxis' qualitative list** (Civilopedia (C3C), `#GCON_Conversion`):
  - Defections are made more likely by: "The presence of foreign nationals of the other civilization in the city", "The application of propaganda by the other civilization", "Civil disorder in the city", "The proximity of foreign territory", "The proximity of a foreign capital".
  - They are made less likely by: "The presence of ground combat units in the city", "We Love The ____ Day", "The proximity of your capital".
- **Formula from the binary (authoritative since 2026-11-12, used by the engine).** Civ3 binary (Perez's decompile, `doc/perez-findings/Civ3_Findings.md`), "Appendix: culture flipping, exact math" (`City_CheckCultureFlip`) and "Round 2c: Culture flip: the setters". The owner ruled on 2026-11-12 that this decompile is authoritative; it supersedes the owner's 2026-10-20 ruling below. For each candidate civ X, in slot order (1..31, the owner skipped), with integer arithmetic:
  1. `S = max(T - 2, 0) + resisters + nationals`, where T is the city's radius tiles (positions 1..20, centre excluded) inside X's borders, nationals are the citizens of X's nationality and resisters those of them who resist (so resisters count twice). Skip X if `S <= 0`; cap S at 10.
  2. `S *= 2` in civil disorder; `S *= 2` if X has more culture in this city than the owner; with A the owner's total culture and B X's: if `(A+1)*4 < B+1` then `S <<= 2` (a fixed x4), else `S = (B+1)*S/(A+1)` (which can shrink S a lot); `S /= 2` while celebrating.
  3. `G` = military land units of **any owner** on the city tile x the difficulty's `DIFF+0x78` (the BIQ's "citizens quelled by military", `Difficulty.MilitaryLaw`; 0x78 = 0x44 + 13x4 in our `DIFF` layout). Skip X if `S - G <= 0`, else `S -= G`.
  4. `R = clamp(2000 * d(city, X's capital) / d(city, owner's capital), 500, 8000)`, and **R = 2000 if either capital is missing**. `d = max(dx,dy) - (((dx+dy)/2 - min(dx,dy)) + 1)/2` with absolute, wrap-aware dx and dy.
  5. The city flips to X if `Rand(R) < S`. Each civ rolls in slot order and the **first success wins**.
  - **Capitals never flip.** A city whose flip cooldown is >= 1 is skipped and the cooldown goes down by 1. `City_SetFlipCooldown(city, n)` sets it to n and raises every other owner's city within the first 76 city-radius offsets (about radius 5) to at least n. Founding sets 10; a flip or a peaceful transfer (trade, gift) sets 10; a plain military conquest sets 1, which is why "a newly-captured town will _never_ flip on the first interturn".
  - **Open:** when the owner lost its capital last turn (`Player+0x15c8`, `capitalLostTimer`, set to 1 by `City_Transfer` when a capital changes hands) S is replaced by a floating-point result whose formula Perez did not recover. Not implemented.
  - The default per-unit quell value is not in the decompile; it is the BIQ's, 1 at every level in the standard rules.
  - Example (Perez): 3 nationals, 5 foreign tiles: 3 + 3 = 6; more local culture: 12; equal totals: 12; 2 units x 2 quell: 8; R = 2000: 0.4% a turn. The largest chain gives S = 160: 8% a turn at R = 2000, 32% at R = 500.

- **Community formulas (superseded by the binary for the engine).** Per interturn, for each rival civ:

  `P = [(F + T) * Cc * H * (Cte / Cty) - G] / D` (the community calculators' form)

  `P = [(F + T) * Cc * H - G * (Cty / Cte)] / D` (the owner's ruling of 2026-10-20, **superseded on 2026-11-12** by the binary above). Here the ratio scales the garrison, as in Soren's worked example "16 - (4 X 2) = 8". Both forms give zero chance at the same garrison. The binary confirms the calculators' shape (the ratio multiplies the influence, not the garrison) and adds the "first two tiles" rule, the cap of 10, the x4 cap on the ratio, integer math and the strict > 0 test.

  Sources:
  - Dan Magaha (Firaxis), https://forums.civfanatics.com/threads/culture-flipping-exposed.14337/. He describes the terms but not the arithmetic.
  - Soren Johnson's worked examples, https://codehappy.net/apolyton/threads/51715-1.htm #30 and 51715-2 #36/#48.
  - Flip Calc, https://forums.civfanatics.com/threads/flip-calc-calculates-the-probability-of-culture-flips.53157/: "P=[(F+T)*Cc*H*(Cte/Cty) - G]/D".
  - anarres, https://forums.civfanatics.com/threads/stopping-culture-flip.69295/page-2 #24 (PTW era), the same form: "P=((F+T)\*Cc\*H\*(Cte/Cty) - G)/(2000 \* D)", with "D = relative distance to capitals" and "Government type is not relevent at all".
  - F: foreign nationals (of that civ), resisters twice. Magaha: "The number of foreign nationals in the city in question (resisters are counted twice)".
  - T: city-radius tiles inside that civ's borders, of 21. Magaha: "The number of the 21-tile city-radius squares … that fall inside your cultural borders." Soren: "they count just as much as citizens."
  - Cc = 2 if that civ has more historical culture in the city than the owner, else 1. Magaha: each city "remembers the total amount of culture generated by any civ who has ever occupied it".
  - H = 2 in disorder, 0.5 in WLTKD, else 1. Magaha: "Civil Disorder in a city doubles the chance" / "We Love the King (or whatever) Day in a city halves the chance".
  - Cte / Cty: the two civs' total culture. Soren confirmed the ratio always applies, "it is the latter...", 51715-2 #48.
  - G: the owner's land units with attack ≥ 1 and defence ≥ 1 in the city. Magaha: "any unit with at least 1 point of offensive and defensive capability". Soren: "16 combat (don't count artillery) units". alexman, 51715-4: every unit in an army counts, "including the actual army unit"; "If the army is empty, it doesn't count at all though"; air units don't count. Troops count against each rival's roll separately (Soren: "one spearman could negate both the Greek and the French citizen").
  - D: the die size, 2000 × d, where d is the ratio of the city's distance to the rival capital over its distance to its own capital, capped to 0.25–4. That makes D range 500–8000. Soren: "The capital factor affects how big the 'die' is". Soren via DeepO, 51715-3 #69: "it goes from 500 to 8000, with an average of 2000". Flip Calc v1.5 distance: max(x,y) + 0.5·min(x,y).
- **Code reading: the formula sits inside the flip routine.** Flintlock (C3X-THREAD post-16902815, page-166 #3314): "the formula to compute the risk is embedded in the method that flips cities." (post-16892444, page-160 #3198): "The game internally doesn't break out each factor like culture ratio, foreign citizen count, disorder, and so forth". So no separate function confirms the community terms. Asked whether rioting raises the chance (post-16926492, page-173 #3449): "Yes, I believe it's twice as likely to flip." That agrees with H = 2.
- **Garrisons can bring the chance to zero.** justanick (C3X-THREAD post-16318921, page-46 #909): "Starving actually helps against flip risk and flip risk can be reduced to zero", though it takes very many units unless your culture is clearly higher. Brenshar (#908) says the opposite; the 1.17f readme below sides with justanick.
- **Patch history:**
  - 1.29f readme, Changes v1.16f: "It's slightly easier to culturally acquire cities."
  - 1.29f readme, Changes v1.17f: "It is now possible to completely suppress a city's cultural reversion with enough military units."
- **Timing and consequences.** A city never flips on the interturn it was captured. On a flip, the units inside are lost, citizens keep their nationality, and the new owner gets a free defender.
  - tjs282, https://forums.civfanatics.com/threads/culture-flips.661194/: "a newly-captured town will _never_ flip on the first interturn."
  - Units: https://forums.civfanatics.com/threads/armies-captured-in-culture-flips.90904/ ("All units that are in the city are lost"); Strat Compilation: "if the city revolts you loose the troops in it!"
  - Flips happen at peace too. justanick, 661194: "Once at peace you cannot undo a flip by retaking the city".
  - The BIQ game option: Bluemofia, "Allow Cultural Conversions: Allow cities to convert to a rival civ culturally."
  - Halcyon, https://forums.civfanatics.com/threads/cultural-conversion-question.87425/ #2 (May 2004, C3C): "All the city defenders die." / "The 'capturing' civ gets one free defender in the city." Grille, #3, on the owner's units outside the city but inside its radius: "No, you keep them."
- **The receiving civ can refuse a flip.** A human receiver is asked, and an AI receiver can also refuse. Either way the city stays with its owner. How the AI decides is UNVERIFIED.
  - script.txt `#WE_CONVERT_CITY` offers "Great! Install a new governor." / "We don't want $CITY1. Rebuff the rebels."
  - `#THEY_REFUSED_TO_CONVERT_CITY`: "the people of $CITY1 have risen up in revolt and pledged allegiance to the $CIVNOUN3! However, the $CIVADJ2 government refuses to acknowledge their claim! Thus, we have regained control of $CITY1!"
  - A separate message, `#WE_CONVERT_CITY_WITH_WONDER`, announces a flipped city that "contains $WONDER3", so wonders go with the city.
- **Free defender quality.** Players kill flip-backs by cutting the city off: an isolated city flips back with a weak defender.
  - anarres, https://forums.civfanatics.com/threads/invading-when-youre-low-on-culture.61562/ #6 (Aug 2003, PTW): "as long as that city has no trade route to the AI capital" / "they will only get a Rifleman at best as a defender when it flips back". ainwood, #1: "By pillaging and blocking the roads, the city was cut-off from the empire" / "the strongest unit would only ever be a rifleman, or even a spearman in the early industrial age".
  - This suggests the defender is the best one the new owner could build *in that city*, given its resource connections. That is an inference.
- **Magaha's ranking of the factors**, https://codehappy.net/apolyton/threads/39825-1.htm #1 (Jan 2002, the Apolyton copy of "Culture-Flipping Exposed"). He gives them in order of importance:
  1. distance to the capitals ("if you're closer to your capital than the other city is to its capital, you've got a better chance of getting a flip");
  2. the civs' culture ratio;
  3. the city's historical culture;
  4. disorder;
  5. WLTKD;
  6. garrison ("This factor is relatively low on the totem pole", "this shows you why cities can flip even with huge militias garrisoned in them").

  Relayed in 39825-2 #32: "I would assume this is measured in tiles."
- **Community observations (untested):**
  - **Only to a civ with a stake in the city.** A city flips only to a civ whose nationals live in it or whose territory overlaps its radius. DeepO, https://codehappy.net/apolyton/threads/62844-1.htm #11 (Sep 2002): "If there were a bunch of Americans inside, and a city does not overlap with the Aztecs, it will never flip to the Aztecs." This follows from the formula, since F + T = 0.
  - **No flips to dead civs.** Explorer579, 39825-5 #130 (Oct 2002): "I have never seen a civilization regaining a city through 'cultureflip' after it has been destroyed." vmxa1 (39825-2 #35) says the same.
  - **Capitals.** DeepO, 62844 #16: capitals can't flip "(or at least I have never heared of a capital flipping)". This is still UNVERIFIED.
  - **Courthouses don't help.** Purest Warrior, 39825-3 #86 (Feb 2002): "Building a courthouse does not lower the chances of culture-flipping."
  - **Patch 1.17f garrisons.** Jameschipmunk, 39825-4 #91 (Apr 2002), on 1.17f: "if you have enough troops in the city it will NOT flip." This matches the readme. Soren's die range is relayed by Fitz, https://codehappy.net/apolyton/threads/49417-1.htm #5 (May 2002): "Soren said the distance to the capitols has a range of dividing by 500 to 8000."

### Where our implementation (`C7Engine/C7GameData/CultureFlip.cs`) stands against the sources

Since 2026-11-12 the engine follows the binary's formula above. Earlier rows that described the community model are superseded.

| Point | Sources | CultureFlip.cs | Note |
|---|---|---|---|
| Formula shape and terms | Binary (above) | Binary: `max(T-2,0)` + nationals + resisters, cap 10, then the multipliers in the binary's order in integer math | Matches the binary. T counts tiles within `MaxRankOfWorkableTiles` (21 by default, excluding the city tile). |
| Where Cte/Cty applies | Binary: on the influence, x4 at most, `(B+1)*S/(A+1)` otherwise | Same | *Superseded:* the 2026-10-20 ruling that the ratio scales the garrison. |
| Integer truncation | Binary: integer arithmetic throughout | Integer | Resolved. |
| Garrison | Binary: military land units of any owner x `DIFF+0x78` | Units with attack >= 1 and defence >= 1 (and armies holding one), any owner, x `Difficulty.MilitaryLaw` | Which units count as "military" (count mode 4) is our reading of Magaha. |
| Distance metric | Binary: `max - ((sum/2 - min) + 1)/2` | Same, integer | *Superseded:* real-valued max+½min. Pathfinding (Magaha's hedge) is ruled out. |
| Missing capital | Binary: R = 2000 if either capital is missing | Same | *Superseded:* d capped at 4 (R = 8000) for a civ without a capital. |
| Propaganda as a flip factor | The Civilopedia lists "The application of propaganda by the other civilization" as making defection more likely | Not modelled (propaganda handled as its own mission) | Not in the binary's flip routine. |
| Capitals | Binary: the owner's capital is skipped | Never flip | Resolved. |
| Cooldown | Binary: `City_SetFlipCooldown`; 10 on founding, flip or peaceful transfer, 1 on conquest, raised for other owners' cities within 76 offsets | `City.flipCooldown` (saved), counted down by each round's check | The 76 offsets: rank <= 4 plus the first 15 of rank 5 in our spiral order (Civ3's order within the ring is unknown). |
| capitalLostTimer | Binary: a float replacement for S, formula not recovered | Not implemented | Open. |
| Refusal | script.txt: the receiver may "Rebuff the rebels"; the AI may refuse too | Human asked; AI refuses an ally's city (heuristic) | See "The receiving civ can refuse a flip". |
| Dead civs | Binary: only active civs are candidates | Defeated civs and barbarians skipped | Matches. |
| Courthouse | Civilopedia: courthouse resists *propaganda* only | No effect on flips | Consistent with the binary. |
| Order of rolls among several rivals | Binary: slot order, first success wins | Player order, first success wins | *Superseded:* likeliest first. |
| Free defender type | "The 'capturing' civ gets one free defender in the city" (Halcyon, https://forums.civfanatics.com/threads/cultural-conversion-question.87425/ #2, re-fetched). A city cut off from the AI capital gets "only … a Rifleman at best" (anarres, 61562 #6). | Best-defence buildable land unit | Exact type UNVERIFIED. |
| WLTKD and disorder | Binary: x2, then /2 after the culture scaling | Same | Matches. |
| Fascism, Retain Culture, assimilation | Change F and the culture totals over time | Inputs only | Make sure F, the city memory and Cte/Cty follow assimilation, Fascist xenophobia and capture rules (§3.10, §3.14). |
| Timing | Binary: at the start of each city's processing, in its owner's turn | Once per round, after every player has played | Kept from before. |

## Disputed

- **When happy citizens are enough for WLTKD.**
  - Civilopedia (C3C): "There must be more happy citizens than content citizens." Dan Magaha (Firaxis), https://codehappy.net/apolyton/threads/44579-1.htm #3: "There must be more happy citizens than content ones."
  - The manual says "at least as many happy citizens as content citizens", with the example of 5 happy and 4 content celebrating; that example satisfies both readings.
  - Players in the same Apolyton thread report that ties celebrate. star mouse, #9: "I'm pretty sure I have seen WLT?D when the number of happy citizens was the same." Unregistered, #14: "I'm sure. # happy >= #content will trigger wltkd." Neither cites a test.
  - CFC FAQ: "at least half the citizens must be happy".
- **Fifth border threshold: 20,000 vs 100,000.** Civilopedia (C3C): "(10, 100, 1,000, 10,000, 20,000, etc.)". C3C editor help: "0, 10, 100, 1000, 10000, 100000". CFC Info Center: "increase by a factor of 10 each time". TemporalAnomaly's Law I: "threshold values are of the form 10^n c.p." The editor's `BorderFactor^n` description supports 100,000, and the C3C BIQ has `RULE.BorderFactor` = 10. The 20,000 may be confused with the one-city culture victory.
- **Content base at Deity in early patches.** Skanky Burns (Dec 2001, base game ~1.16f): "On Deity it is 2 people". The C3C Civilopedia, the C3C BIQ and Spoonwood give Deity 1. BIQ-V (1.29f) and BIQ-PTW (1.27f) also give Deity 1 (and Chieftain 4, Warlord 3, Regent 2, Monarch 2, Emperor 1, as in C3C). So if Skanky Burns was right, it was an early-patch value that 1.29f no longer has.
- **WLTKD: corruption and waste, or waste only.**
  - The manual says WLTKD "lowers the levels of corruption and waste". Magaha (Firaxis) wrote "The corruption and waste in a city is reduced, effectively increasing the city's shield output".
  - The C3C Civilopedia says only "Waste in the city is reduced", and alexman's article applies it to waste only.
  - Tassadar, https://forums.civfanatics.com/threads/we-love-the-king-days.34249/: "it cut down shield waste like a courthouse but it doesnt cut down commerce corruption."
  - Thoth, https://codehappy.net/apolyton/threads/35096-1.htm #4 (Nov 2001): it "reduces corruption (more than a courthouse does)".
  - T-hawk, https://forums.civfanatics.com/threads/we-love-the-king-day-defeats-corruption.13177/ #9 (vanilla era): "(WLTKD applies to shield production only, not commerce income.)", working "exactly as if the city had an additional courthouse". This matches alexman's waste-only model (an extra courthouse step for waste, section 2 §5).
  - The measured C3C rule is waste only; Firaxis' wording is looser.
- **Wonder culture values.** Three sources disagree:
  - The C3C BIQ (§3.10): Great Library 6, Sistine 6, Shakespeare 8, Newton 6, Cure for Cancer 3, JS Bach 6, Hoover 2.
  - CFC's Civilopedia page (https://civfanatics.com/civ3/civilopedia/wonders/, probably the base game): Great Library 6, Sistine Chapel 6, Shakespeare 6, Newton 6, Cure for Cancer 4.
  - The Complete manual chart: 5, 4, 5, 5, 5, plus JS Bach 5 and Hoover 3.

  It is not a difference between the final patches. BIQ-V (1.29f) and BIQ-PTW (1.27f) have the same culture values as C3C for every wonder (Great Library 6, Sistine 6, Shakespeare 8, Newton 6, Cure for Cancer 3, JS Bach 6, Hoover 2; the shared buildings match too). The CFC page and the manual chart must reflect an earlier patch or an error. The BIQ values are the defaults in all three final versions.
- ~~**Distance for flips.**~~ *Resolved 2026-11-12 by the binary:* a straight-line tile distance, `max - ((sum/2 - min) + 1)/2` (§3.15).
- ~~**Where Cte/Cty applies in the flip formula.**~~ *Resolved 2026-11-12 by the binary:* on the influence, capped at x4 (§3.15). The owner's 2026-10-20 ruling (on the garrison) is superseded.
- **Assimilation.** The BIQ ships 1–4% chances (§3.14), and the Civilopedia describes assimilation. Two editor tests saw none, and the engine follows the owner's ruling that it never happens.

### Resolved in the 2026-10-20 deep pass (kept for the record)

- **Which governments suffer war weariness.** This is a version difference. Feudalism exists only in C3C, and the C3C BIQ gives it `WarWeariness` = 1, the same as Republic (§3.5).
- **Communism draft rate.** C3C BIQ `GOVT.DraftLimit` for Communism = 2, as the 1.17f readme fix says.
- **Celebration benefits by government.** Firaxis' list of WLTKD benefits (Magaha, 44579 #3) has nothing government-specific, and the C3C editor has no such flag. The manual's sentence is treated as a Civ2 holdover.
- **Content or happy faces from wonders.** The C3C BIQ stores the Hanging Gardens, Mausoleum, Shakespeare, JS Bach and Cure for Cancer effects as content faces (§3.2). The Civilopedia wording is right for C3C, and the "happy" wordings are older or loose.
- **How war weariness ends.** Both sources are right. Unhappiness is expressed only while at war (justanick: "During peace WW is suspended"). The points decay about 1/20 per peace turn and carry into a later war (TimBentley, 61628 #66; Oystein, #55).

## UNVERIFIED / open questions

- How the luxury-slider share is rounded per city. (The "+50% Luxury Output" flag is on no building in any version: BIQ-V, BIQ-PTW and the C3C BIQ, §3.3.)
- Where martial law and entertainers fall in the order of face application. The rest of the order has only a community worked example (§3.2).
- How many citizens become unhappy from "mother country" aggression. Forum guesses range from "one more unhappy" to "about 50%" of the foreign nationals.
- How armies count toward quelling resistance and toward the flip garrison G. alexman says every unit in an army counts for G, but nobody has tested armies for resistance.
- Exactly how the Police Station (−25%?) and Universal Suffrage (−1 per city?) reduce war weariness in C3C. The BIQ has only on/off flags, and the numbers are community measurements.
- How long disorder must last before the government falls, and whether that happens at all outside Democracy's war-weariness revolt. Also how the improvement destroyed by a riot is chosen, and whether "sabotage to ongoing projects" exists.
- Border edge cases: whether civ culture matters for contested tiles, and Crimso's "first to expand keeps it" objection to Law IV.
- What the civ-wide culture levels (Lvl. Multiplier: 1000, 2000 … 32000) do besides the labels.
- How much AI attitude depends on cultural opinion.
- The "less than half the leader's culture" condition for culture victory.
- Flip items: the free defender's exact type, how the AI decides to refuse a flip, and the floating-point flip formula used while `capitalLostTimer` is set (the turn after a civ loses its capital; not recovered from the binary, not implemented). The order of rolls, capitals, integer math and the cooldown are resolved by the binary (§3.15).
- Which nationality a drafted unit or citizen has, and whether foreign nationals are drafted or sacrificed first. Toasty (base game) says only your own nationals can be drafted; this is unconfirmed.
- Whether specialists count as content for the WLTKD "happy vs content" test.
- Whether WW points from different enemies are summed before the level is applied (*C3X-THREAD:* the anarchy test divides total WW by the number of contacts, §3.5; the per-city unhappiness rule is still not shown), or applied per enemy and then summed. The article and Larkin (for war happiness) say per enemy, rounded down, then added. MadScot (61628 #2, Aug 2003) said instead that "the total active WWP for all civs is compared to the threshold values". Third round, code: the 1.22 player record stores war weariness per opponent, `int war_weariness[32];` in `struct Leader` (C3X's header for the exe, https://github.com/maxpetul/C3X/blob/master/Civ3Conquests.h). So the points are kept per enemy. That fits the per-enemy model but does not prove how the level is applied, since the code that reads the array is not published.
- Code-level field names that bear on this section (third round, same header, names given by C3X's reverse engineering, no logic): each city has `turns_of_flip_immunity`, so the stock game keeps a per-city timer during which the city can't flip (this fits "a newly-captured town will _never_ flip on the first interturn"; the binary gives the lengths, 10 and 1, §3.15). Each city has `Total_Cultures[32]`, a per-civ culture total, which is the city's memory of every civ's culture behind the flip formula's Cc term (C3X's culture code adds to `Total_Cultures[civ_id]` of the owner). The city's unhappiness breakdown has separate percentages for no reason, crowding, war weariness, "Agresssion" (the mother-country aggression term), propaganda, draft, oppression (pop rush) and this-city and other-city improvements, plus `UnhappyFacesDueToPropaganda`, `UnhappinessTurnsFromPopRushing`, `DraftCount` and `UnhappyTurnsDueToDrafting`. So mother-country aggression and enemy propaganda are separate unhappiness causes in the engine.
