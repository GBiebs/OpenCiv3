# 6. Diplomacy, espionage and AI attitude

Scope: the base game (1.29f), Play the World (1.27f) and Conquests (1.22).
Scenarios, mods and the special Conquests game modes are excluded.

How to read this section:

- Every rule has a source and a short verbatim quote. "Local install" means
  the files of a Steam *Civilization III Complete* install (Conquests 1.22),
  read directly: `Conquests/Text/Civilopedia.txt`, `Conquests/Text/script.txt`,
  `Conquests/Text/diplomacy.txt`, `Conquests/Text/labels.txt`, the readmes,
  and `Conquests/conquests.biq` (read with this repo's QueryCiv3 parser).
- Vanilla-only text comes from `Text/Civilopedia.txt` in the same install.
- "Manual" means the Steam manual PDF
  (https://cdn.akamai.steamstatic.com/steam/apps/3910/manuals/manual.pdf), read
  as a text extract. It is the Civ3 Complete edition, but its diplomacy chapter
  is mostly base-game text. Page numbers are the printed folios, approximate.
- "EDHELP" means the Conquests editor help (`Civ3ConquestsEdit.chm`,
  decompiled), cited by topic name.
- "C3X-INI" means the C3X mod's `default.c3x_config.ini`
  (https://github.com/maxpetul/C3X/blob/master/default.c3x_config.ini). The mod
  is out of scope. Only its descriptions of what the unmodded C3C 1.22
  executable does are used.
- "Code-level" marks findings from people reading the C3C 1.22 executable.
- "C3X-THREAD" means the C3X mod's forum thread,
  https://forums.civfanatics.com/threads/c3x-exe-mod-including-bug-fixes-stack-bombard-and-much-more.666881/
  (2021-2026). Posts are cited as "post-NNNNNNNN" (thread URL plus
  `post-NNNNNNNN`) or "page-N #post-number". Flintlock, the C3X author,
  reads the 1.22 executable; other posters report play, sometimes modded.
  Only statements about the unmodded game are used. Quotes were re-checked against the posts; the few that could not be are marked "(paraphrase from a summarised fetch)".
- Community findings are labelled with the tester, version and date where the
  source gives them. CivFanatics studies use a sign convention where a
  negative attitude number is good for you.
- Anything without a source is marked **UNVERIFIED**.

---

## 6.1 Contact and the diplomacy screen

- **The Diplomacy button needs first contact.** Source: vanilla `README.txt`
  (manual errata, local install). Quote: "The Diplomacy button becomes enabled
  after your first contact with another civilization."
- **Foreign Advisor lines need contact with both civs.** Source: vanilla
  `README.txt`, Fixes v1.17f. Quote: "Relationship lines are no longer
  displayed on the Foreign Advisor unless the player has contact with both
  civilizations."
- **There are five attitude states**, which the advisor mouse-over shows.
  Source: vanilla `README.txt` (1.17f). Quote: "The five states an AI can be
  in on the Diplomacy screen, can now be seen on the mouse over advisor
  messages". Their names are in Conquests `labels.txt`: "gracious", "polite",
  "cautious", "annoyed", "furious".
- **Communications (contacts) can be traded.** They are introductions to a
  third civ, and Printing Press unlocks the trade. Sources: Conquests
  Civilopedia `GCON_Agreements`, quote: "{Communications}. These are
  introductions to a third civilization you can grant or receive from a
  rival." Also `conquests.biq` TECH flags, where Printing Press has "Enables
  Communication Trading". Vanilla Civilopedia `TECH_Printing_Press`, quote:
  "Can now trade communications between civilizations."
- **PTW fix: the AI cannot trade communications before it has the tech.**
  Source: `civ3PTW/readme.txt`. Quote: "Fixed bug that allowed the AI to trade
  communications before they had the tech that allowed it."
- **A world map does not give contact.** Source: CivFanatics, "Basic trading
  tips" (https://forums.civfanatics.com/threads/basic-trading-tips.71236/).
  Quote (as surfaced by search): "trading World Maps will show you the world
  known by the other civ but will not give you contact with civs you have".
- **First contact comes from units meeting.** Source: the Civ3 manual (Steam
  `manual.pdf`, Civ3 Complete edition, text extract, printed p.~161, "Negotiations").
  Quote: "You make first contact whenever one of your units crosses paths with
  one of theirs." Also p.~63 ("Making contact"): "You still generally make
  first contact with your opponents by running across their units". The exact
  geometry (adjacent tile or line of sight) is not stated. GordonJ (CFC,
  https://forums.civfanatics.com/threads/first-post-why-cant-i-see-all-the-civs.120770,
  C3C, Jun 2005) says he had to get "within a space of their units to actually
  start negotiating" (one player's account).
- **Contact is permanent.** Same manual page. Quote: "After communications are
  set up, they're never lost."
- **All civs start at peace.** Manual p.~154. Quote: "You are naturally at
  peace with all your rivals at the start of the game". Also p.~158: peace is
  "the natural state when you first meet a rival".
- **First contact gives an automatic peace treaty.** Being at peace and having a peace treaty are the same state. Manual tutorial (printed p.~51-52), meeting the Germans: "Unless we declare war against the Germans, we'll come out of this encounter with an automatic peace treaty with them". Manual p.~163: the "Peace treaty" trade item "is only available if you are at war". Flintlock, reading the stock war-plotting code (https://forums.civfanatics.com/threads/all-about-ai-aggression-levels-and-war-plotting.703785/ #1): "the only way the peace treaty check wouldn't pass is if the target is already at war with every other player in the game". So the code's peace-treaty bit (C3X `Civ3Conquests.h`, `Relation_Treaties[32]`, "& 1 // 1 = peace treaty") is set for every pair that has met and isn't at war. Whether breaking it by declaring war on a newly met civ costs reputation, rather than only attitude, is still disputed (see Disputed).
- **Units in your territory invite contact.** Manual p.~162 lists, among ways
  to open talks: "Move one of your units into the other nation's territory and
  wait for them to contact you (not recommended unless your intention is to
  annoy your rival)."
- **At war, the leader may refuse to meet.** Manual p.~162. Quote: "If you are
  at war with the leader you attempt to contact, he or she might refuse to
  meet with you". `script.txt` `NODIPLOMACY`: "The $CIVNOUN0 refused to
  acknowledge our envoy!"

## 6.2 Embassies

- **Writing enables embassies.** An embassy is built in the rival's capital
  and costs gold. Source: Conquests Civilopedia `GCON_Embassies`. Quotes:
  "When a civilization learns $LINK<Writing=TECH_Writing>, it can begin to
  build its intelligence network. The first step is to build [embassies] in
  rival capital cities." and "Embassies cost gold from your treasury to build,
  and diplomatic missions cost gold to execute." In `conquests.biq`, Writing
  has the flag "Enables Diplomats".
- **You can make agreements when either side has an embassy.** Source:
  Conquests Civilopedia `GCON_Agreements`. Quote: "When your civilization
  builds an $LINK<embassy=GCON_Embassies> in a rival's capital city, or when a
  rival builds one in your capital, you can negotiate [Diplomatic
  Agreements]".
- **An embassy shows the other civ's wars and peace.** Source: CivFanatics
  Civ3 FAQ (https://civfanatics.com/civ3/faq/). Quote: "you can see what
  civilizations they are at war/peace with on the F4 Foreign Advisor screen."
- **Establishing an embassy always succeeds and costs gold by formula** (see
  6.11). The `conquests.biq` ESPN base cost is 20. Source: CivFanatics, "A
  Study of Espionage Missions"
  (https://civfanatics.com/civ3/strategy/game-mechanics/a-study-of-espionage-missions/).
  The study says Build an Embassy and Investigate City always succeed. A
  local `conquests.biq` dump gives "Build an Embassy | diplomat=True base=20".
- **War closes the embassy automatically.** Source: Civ3.com FAQ, quoted on
  Apolyton
  (https://apolyton.net/forum/miscellaneous/archives/civ3-general-archive/41397-espionage-you-can-t-steal-techs-from-civs-you-are-at-war-with).
  Quote: "your embassy in their capitol is automatically closed".
- **An embassy closed by war reopens at peace.** Manual p.~157. Quote: "It is
  only necessary to establish an embassy once with any particular
  civilization. Even if you manage to get it closed down (through war with
  that civilization), it reopens when peace is declared." The same page says
  the embassy menu lists "the civilizations you have contacted, are not at war
  with, and do not already have an embassy with." So you cannot open a new
  embassy with a civ you are at war with. Vanilla `README.txt` (Fixes v1.16f):
  "Incorrect popup concerning placing new embassies with civilizations you're
  at war with no longer displays."
- **The first investigation of the capital is free.** Manual p.~157. Quote:
  "When you first establish the embassy, you can investigate the enemy's
  capital without risk or penalty."
- **What an embassy reveals.** Manual p.~155. Quotes: "With an embassy, you can
  learn about your opponent's diplomatic connections with others" and "The
  Military Advisor gets a complete list of their forces from an embassy." A
  planted spy also gives unit locations (p.~168): "the Spy gives him their
  locations as well."
- **An embassy improves attitude by 2.** See 6.7 (former Disputed item).
- **Wonder pop-ups need an embassy or a spy.** Source: vanilla `README.txt`.
  Quote: ""beginning wonder" pop-ups now only appear if you have an embassy or
  spy with the corresponding civ." Also: "Spaceship parts pop-ups now only
  appear if you have a spy with the corresponding civ."

## 6.3 Diplomatic agreements: kinds and the techs that enable them

- **There are four agreement kinds.** Source: Conquests Civilopedia
  `GCON_Agreements`. Quotes:
  - Military Alliance: "You agree to declare war on a despised third party."
  - Right of Passage: "You agree to allow his or her military units to move
    through your $LINK<territory=GCON_Territory> and use your transportation
    network (roads and railroads)."
  - Trade Embargo: "An agreement that neither of you will
    $LINK<trade=GCON_Trade> with or [allow] trade with a third party."
  - Mutual Protection Pact: "An agreement to declare war upon any
    civilization that attacks your partner."
- **Tech gates in Conquests (`conquests.biq` TECH flags, local dump):**
  - Writing: Diplomats, Military Alliance.
  - Map Making: Right of Passage.
  - Printing Press: Communications.
  - Navigation: Map Trading.
  - Nationalism: Mutual Protection Pact, Trade Embargo.

  The Conquests Civilopedia agrees. `TECH_Writing`: "Can now negotiate
  military alliance." `TECH_Map_Making`: "Can now negotiate right of passage".
  `TECH_Navigation`: "Can now trade $LINK<world maps=GCON_Maps> in
  diplomacy." `TECH_Nationalism`: "Can now negotiate mutual protection pact
  and trade embargo". CivFanatics FAQ, quote: "you cannot trade
  World/Territory Maps until you research Navigation."
- **Vanilla differs (see Disputed).** In vanilla, Writing also gives Right of
  Passage and Map Making gives map trading. Source: vanilla `Text/Civilopedia.txt`.
  `TECH_Writing`: "Can now negotiate military alliance and right of passage".
  `TECH_Map_Making`: "Can now trade $LINK<world maps=GCON_Maps> in
  diplomacy."
- **An MPP needs Nationalism on only one side.** Manual p.~160. Quote: "If a
  peace treaty and embassy are in place between two civilizations and either
  has discovered Nationalism, they can negotiate a mutual protection pact."
  cort (Apolyton,
  https://codehappy.net/apolyton/threads/33796-1.htm, Nov 2001) says the same:
  an MPP needs "only one civ to know Nationalism". (Former UNVERIFIED item.)
- **Every agreement needs an embassy.** Manual pp.~159-160:
  - Military Alliance: "Once you have an embassy with a friendly nation, you
    can sign a military alliance against a common enemy."
  - Right of Passage: "You can sign a right of passage if you have an embassy
    with a friendly nation."
  - Trade Embargo: "If you have discovered Nationalism and have an embassy, you
    can arrange a trade embargo with an ally."
  - MPP: as quoted above (peace treaty plus embassy).

  Manual p.~63: "An embassy also opens the possibility of diplomatic agreements
  beyond a simple peace treaty." Also Konquest02 (Apolyton,
  https://codehappy.net/apolyton/threads/86477-1.htm, Oct 2003) lists the
  options that need an embassy: "Right of Passage, Mutual Protection Pact and
  alliances against other civs." No source names a road or sea link as a
  condition for agreements; that link is a condition for resource trade only
  (6.4).
- **Owner ruling (engine): Right of Passage needs Writing plus an embassy.**
  Sources: Conquests Civilopedia `GCON_Embassies`, "When a civilization learns
  $LINK<Writing=TECH_Writing>, ... The first step is to build [embassies] in
  rival capital cities. That done, you can sign Right of Passage
  $LINK<agreements=GCON_Agreements> and Military Alliances". Vanilla
  Civilopedia `TECH_Writing`: "Can now negotiate military alliance and right of
  passage". Aeson (Apolyton, "1.17f bug: right of passage",
  https://codehappy.net/apolyton/threads/46862-1.htm, Apr 2002): "Writing (and
  establishing an embassy) is what makes ROP agreements possible." The C3C BIQ
  flag puts ROP on Map Making instead (see Disputed).
- **Agreement terms are 20 turns.** Manual pp.~159-160, for each of MA, ROP,
  embargo and MPP. For example: "A right of passage agreement lasts for 20
  turns. At the end of that period, either party can cancel the agreement
  without consequences." and "The pact lasts for 20 turns." To leave without
  a reputation mark, wait for expiry: "To cancel a pact without consequences,
  you must wait for it to expire."
- **Leaving an MA or MPP does not end the war.** Manual p.~159. Quote:
  "Leaving the military alliance won't cancel the state of war with the third
  party, of course."
- **Peace with the common enemy destroys an alliance.** Manual p.~159. Quote:
  "if either party to the alliance makes peace with the third party, it
  effectively destroys the alliance." Breaking an alliance "is remembered as a
  major transgression by all of the other civilizations". Making peace with a
  civ that is still invading an MPP partner also costs reputation (p.~160):
  "If you make peace with a civilization while it is still invading your ally,
  your reputation suffers for quite some time."
- **PTW 1.27: peace ends your alliances against that enemy.** Falconius
  (Apolyton, "1.27 Fixes Military Alliances!",
  https://codehappy.net/apolyton/threads/97851-1.htm, Sep 2003): "when you
  make peace, your alliances against your former foe simply end." He says the
  earlier behaviour that "caused you to get sucked back into a war you just
  ended is now no more". Arrian, in the same thread, thinks the old problem was
  an MPP, not an alliance. Player report, not from a readme.
- **Destroying the common enemy ends an alliance without a reputation hit
  (players).** CFC "How to form a strategic alliance",
  https://forums.civfanatics.com/threads/how-to-form-a-strategic-alliance.154837/
  (Jan 2006): SimpleMonkey, you "can end an alliance by killing off your mutual
  enemy" without a rep hit. TimBentley first said the opposite, then: "I haven't
  seen a way to get a rep hit in this manner." JMK disagreed ("it will be
  considered as breaking agreement"). Not tested.
- **An MPP triggers on an attack on the partner.** `script.txt`
  `MUTUALPROTECTIONOURWAR`: "$CIVNAME0 has attacked our "brothers" in
  $CIVNAME1. Our $LINK<mutual protection pact=GCON_Agreements> requires us to
  declare war against $CIVNAME0." Whether the declaration alone or the first
  attack is the trigger is unclear. Loopy (CFC, Civ3 vanilla, Nov 2001,
  https://forums.civfanatics.com/threads/mutual-proctection-do-you-sign-on-or-not.8546/):
  "I don't think declaring war triggers the MPP, but the first attack" (he
  hedges). WonderPup, same thread: MPP partners "will declare war with your
  enemy on the *next* turn."
- **Peace is made separately with each enemy.** Manual p.~161. Quote: "You
  must make peace separately with each opponent (even those allied with a
  civilization with whom you have already negotiated peace)."
- **An embargo is not a war.** Manual p.~160. Quote: "A trade embargo is not a
  declaration of war, though it's sometimes enough of an affront to inspire
  one."
- **The AI won't embargo a civ it trades resources with (tested).** Spoonwood
  (CFC, "Use The Trade Tab as a Guide to Military Alliances",
  https://forums.civfanatics.com/threads/use-the-trade-tab-as-a-guide-to-military-alliances.692937/,
  C3C, Nov 2024, save tests): "Trade Embargoes don't seem to work when you and
  an AI have peace with a nation if trading a resource or luxury at least."
  Greece refused even for 1000 gpt: "They would never accept this deal." Persia
  accepted the embargo after first signing a military alliance against the
  same civ.
- **AI alliance evaluation uses distance (code-level).** C3X-INI, "DIVISION BY
  ZERO IN AI ALLIANCE EVAL": "When considering whether to agree to a military
  alliance, the AI computes the distance from its capital to the nearest city
  belonging to the target of the alliance and divides by that distance." The
  rest of the formula is not given. Flintlock (C3X-THREAD post-16832498,
  page-140 #2794): "The crash is caused by a division by zero in
  the AI's logic to consider whether to sign a military alliance." He adds
  (post-16833692, page-141 #2810) that the alliance's value to the other
  side likewise "depends on the distance from your capital to the
  nearest city belonging to the target of the alliance". So a nearer target
  is worth more. The crash (target with a city at distance 0) is a bug.
- **Locked alliances are scenario-only, so out of scope.** Members have a
  permanent MPP. Source: Conquests Civilopedia `GCON_Locked_Alliances`.
  Quote: "A pre-designed scenario (only) might begin with some (or all)
  civilizations grouped into several "Locked Alliances"."

## 6.4 Trade routes, resources and embargoes

- **Resource trade needs connected capitals.** Source: Conquests Civilopedia
  `GCON_Trade`. Quote: "Excess resources may be traded to other civilizations
  if the capital cities of both civilizations are connected."
- **Connection rules.** Source: same entry. Quote: "there is a road or
  railroad running between them, there are harbors in both cities and a
  visible water route between them, or both cities contain an airport"
  (paraphrased list; the original is three bullet lines). A road through
  enemy territory or a blockaded harbor does not count. Quote: "two cities are
  not connected if the only road/rail that connects them passes through enemy
  territory, or if a harbor is blockaded".
- **Water routes need techs for every tile type on the route.** Conquests
  Civilopedia `GCON_Trade`: "In order to use a water trade route, your
  civilization must be able to safely traverse every square of the route and
  this depends upon having $LINK<Map Making=TECH_Map_Making>,
  $LINK<Navigation=TECH_Navigation>, and/or $LINK<Magnetism=TECH_Magnetism>."
  The terrain entries name Astronomy for Sea instead: `TERR_Sea`, "[Sea] squares
  are significant barriers to $LINK<trade=GCON_Trade> and
  $LINK<travel=PRTO_Galley> until the discovery of
  $LINK<astronomy=TECH_Astronomy>." and `TERR_Ocean`, the same "until the
  discovery of $LINK<Navigation=TECH_Navigation> or
  $LINK<Magnetism=TECH_Magnetism>." Players (CFC "Pangea Maps and Trading",
  https://forums.civfanatics.com/threads/pangea-maps-and-trading.689998/, C3C,
  Jun 2024, untested): tjs282, "Only one partner needs Astro to make
  Resource-trade deals"; "Barb-Galleys can cut Coastal trade-routes if they
  happen to stop on an inconvenient tile."; Spoonwood and justanick say the
  Great Lighthouse also opens sea routes. The rule file sides with the
  terrain entries: `conquests.biq` TECH flags (QueryCiv3 dump, this pass)
  give "Enables Trade Over Sea" to Astronomy only and "Enables Trade Over
  Ocean" to Navigation and Magnetism. No tech carries a Coast flag, so coast
  routes need none. **Code (third round):** Flintlock's Trade Net X
  (https://github.com/maxpetul/C3X/blob/master/Trade%20Net%20X/TradeNetX.cpp),
  a speed re-implementation of the stock 1.22 trade network, sets a civ's sea
  trade bit if it `Leader_has_tech_with_flag (...,
  ATF_Enables_Trade_Over_Sea_Tiles)` **or** owns a wonder with
  `ITW_Safe_Sea_Travel` (the Great Lighthouse's flag), and its ocean bit from
  `ATF_Enables_Trade_Over_Ocean_Tiles` only. Coast tiles always pass, land
  tiles only if they hold a city, and every tile on the route must have been
  explored by that civ. A water tile in the territory of a civ at war with
  you, or holding one of its combat units, breaks the route. So Spoonwood and
  justanick are right that the Great Lighthouse opens sea routes. The Map Making in `GCON_Trade` matches no flag (Map Making
  carries only Right of Passage). The same dump gives Communication (contact)
  trading to Printing Press, Map trading to Navigation, MPP and Embargo to
  Nationalism, Military Alliance and Diplomats to Writing.
- **An embargo blocks resource and luxury trade.** Source: Conquests
  `script.txt` `DIPLOADVICETRADE_THEIREMBARGO`. Quote: "We cannot trade
  resources or luxuries with the $CIVNOUN0 -- they have a trade embargo
  against us." Also `DIPLOADVICETRADE_OUREMBARGO`: "We have agreed not to
  trade with the $CIVNOUN0 -- we have a trade embargo against them."
- **The embargoed civ is told when an embargo starts and ends.** Source:
  `script.txt` `TRADEEMBARGO` / `TRADEEMBARGOENDS`. Quotes: "$CIVNAME0 and
  $CIVNAME1 have signed a trade embargo against us." and "The trade embargo
  against us between $CIVNAME0 and $CIVNAME1 has ended."
- **Resource and luxury deals last 20 turns or until war.** Manual p.~164.
  Quote: "Like all trade agreements, a luxuries deal lasts for 20 turns or until
  interrupted by war between the parties to the trade." The traded good
  reaches every city connected to the receiver's capital.
- **What can be traded.** Manual pp.~163-165 lists peace treaty, diplomatic
  agreements, embargoes, communications, maps (World Map and Territory Map),
  luxuries, strategic resources, gold (lump sum or per turn), technology,
  cities and Workers. Items the other side has but cannot offer "are grayed
  out", and items the other side does not need do not appear.
- **Only Workers in the capital can be traded, and they keep their
  nationality.** Manual p.~165. Quote: "Any Worker currently in your capital
  city can be offered for trade. The same goes for workers in your rival's
  capital. A traded Worker retains its nationality, just like a captured one."
  Confirmed by players (Apolyton, "How do you trade workers?",
  https://codehappy.net/apolyton/threads/59851-1.htm, Aug 2002). alexman
  (Firaxis), same thread, on foreign workers showing your nationality on the
  trade screen: "It's a bug."
- **Technology needs a tradeable prerequisite.** Manual p.~165. Quote: "Any
  civilization advance that one nation has discovered or acquired but the other
  hasn't and can research is a potential item of trade."
- **Per-turn gold runs for 20 turns; only war stops it.** Manual p.~165.
  Quote: "it commits a leader to pay the specified amount every turn for the
  next 20 turns. Only the outbreak of war between the trading parties
  interrupts the required payments."
- **Cities: the AI never trades them in ordinary diplomacy; it can cede them
  for peace (code reading plus players).** Flintlock (C3X-THREAD
  post-16760037, page-131 #2614): "the reason the devs restricted
  that to begin with is the AI wasn't able to properly value cities".
  Kokuryutei (post-16322889, page-47 #938): "AI never agrees to
  transactions with the transfer/exchange of cities." tjs282 (#939,
  post-16322917): "And a badly damaged AI will often also concede additional
  towns during peace negotiations."; (post-16781598,
  page-133 #2646): "You can't "buy" towns (i.e. offer something in
  exchange), you can only demand them outright." forestar (post-16990905,
  page-191 #3807): city trading is "normally impossible during
  regular diplomacy, but it becomes allowed during dealing with peace
  treaties." Units: Flintlock (post-16291863, page-39 #771): "very
  limited unit trading of only workers in the capital", as the manual says.
  The trade lists stop at 512 cities and 8192 units, a bug C3X fixes
  (post-16883917, page-158 #3141).
- **Cities can be traded, but the AI rarely does so.** Manual p.~165. Quote:
  "under normal circumstances a leader would rather go to war than trade one
  away. The option to trade cities exists, however". The CivFanatics FAQ
  (https://civfanatics.com/civ3/faq/) lists "Cities (as part of a peace deal
  only)" (see Disputed).
- **At war, only a peace treaty can be put on the table first.** Manual
  p.~163. Quote: "This option is only available if you are at war with the
  rival you're dealing with. In fact, it's the only diplomatic agreement that
  appears during a war". ixnay (Apolyton,
  https://apolyton.net/forum/miscellaneous/archives/civ3-general-archive/57646-how-do-i-get-more-than-peace-treaty-for-ending-war,
  Mar 2002): "once you put that on the table you have access to all the other
  options". Zachriel, same thread: "Sometimes, they will give you all their
  cities for peace."
- **Strategic and luxury goods show grey without a route.** Spoonwood (CFC
  692937, C3C, 2024): "All strategic resources or luxuries that an AI has will
  appear in greyed out text" when there is no trade route to your capital.
- **The AI rarely trades its only copy of a luxury.** Source: CivFanatics,
  "Civs and their luxuries", simwiz2 (Civ3 General Discussions)
  (https://forums.civfanatics.com/threads/civs-and-their-luxuries.15068/).
  Quote: "An AI will almost NEVER trade a luxury that it only has one of."
  The "never trades its only resource" rule found elsewhere is a **Civ4** rule
  (https://forums.civfanatics.com/threads/question-about-trading-resources.368926),
  so it is not used here.

## 6.5 Deal durations, renegotiation and peace treaties

- **Ongoing deals run for 20 turns.** Source: Conquests `labels.txt`.
  Quotes: "This deal may be canceled peacefully after 20 turns." and "This
  deal will last for 20 turns."
- **"Always Renegotiate Deals" (1.29f) forces renegotiation every 20 turns.**
  Peace treaties are exempt. Source: vanilla `README.txt`, Additions v1.29f.
  Quote: "Added "Always Renegotiate Deals" prefs which forces a renegotiation
  of every non-peace treaty deal every 20 turns."
- **The AI asks to end or renew at expiry.** Source: Conquests
  `diplomacy.txt` `AIRENEGOTIATE`. Quote: "Friend $PLAYER0, this pact is about
  to expire. We believe that its usefulness is over." `AIRENEGOTIATEPEACE`:
  "$PLAYER0, our peace treaty is about to expire. For the sake of world
  peace, we will extend it if you desire."
- **A peace treaty lasts until someone declares war.** It has a 20-turn term,
  and breaking it inside that term costs reputation. Source: CivFanatics,
  "question about peace treaties" (C3C 1.15)
  (https://forums.civfanatics.com/threads/question-about-peace-treaties.84591).
  marioh, quote: "If you sign a peace-treaty with a civ it will last until you
  or the other declares war." Taé Shala, quote: "there will be a reputation
  penalty if you break the treaty within the remaining turns."
- **A bare treaty renews itself; one with attachments does not.** Source:
  same thread (marioh), as summarised in search. Quote: "If you don't have
  GPT/ROP/MPP/luxuries/resources attached with the peace treaty, then it will
  automatically renew until war is re-declared by either party."
- **Mobilization lasts until peace.** Source: Conquests Civilopedia
  (Mobilization). Quote: "May not return to Normalcy until a Peace Treaty is
  signed."
- **There is no ceasefire in Civ3: relations are peace or war.** Manual
  p.~158 ("The Diplomatic States"). Quote: "the relationship between two
  nations can be expressed as one of two different states: peace or war."
  None of the vanilla, PTW or C3C `diplomacy.txt`, `labels.txt` or `script.txt`
  files has a ceasefire string; the only "cease" lines are flavour text in
  peace replies (C3C `diplomacy.txt` line ~1554: "We will agree to cease
  fighting if only to delay the inevitable."). The "16 turns" figure is from
  the *Civ2* strategy guide
  (https://forums.civfanatics.com/threads/how-long-does-a-cease-fire-last.55033,
  "a cease-fire lasts only 16 turns"), and the CFC thread "How do you get
  ceasefires?" (173866) is in the Civ4 forum. (Former open question 2.)
- **A peace treaty forbids military units in the partner's territory.**
  Manual p.~158. Quote: "You and your rival agree not to attack each other or
  even enter the other's territory with military units." And: "Units that
  violate this agreement may be asked to leave--and their failure to do so
  immediately can be considered a treaty violation." A peace treaty also makes
  trade possible: "A peace treaty, when combined with an embassy, also opens up
  negotiations to several other agreements and, just as importantly, makes
  trade with the other civilization possible."
- **Peace cannot be renegotiated while its 20 turns run.** Vol (CFC, "Trading
  Reputation" thread, page 2,
  https://forums.civfanatics.com/threads/trading-reputation.113376/page-2,
  Sep 2005): "you cannot renegotiate peace (it is grayed out) when the deal
  still has turns left on it." `script.txt` `CONFIRMRENEGOTIATEPEACE`: "Are you
  sure we should renegotiate peace?"
- **1.29f closed a zero-turn-war loophole.** Vanilla `README.txt`, Fixes
  v1.29f: "Fixed diplowin loophole involving renegotiating peace which allowed
  0 turn wars."
- **When the AI will talk peace (Firaxis).** Soren Johnson (Apolyton, "When
  does the AI ask for peace (Soren?)",
  https://codehappy.net/apolyton/threads/41866-1.htm, post #27, Feb 11 2002,
  vanilla): "The AI's willingness to discuss peace terms depends on" "how long
  the war has lasted" and "how much it is winning or losing the war." He
  goes on: "In other words, the longer the war has lasted and the more
  battles/cities/whatever the AI has lost relative to you, the more willing it
  will be to go to the negotiating table." Also: "the length of time factor is
  there to prevent people from war-jacking the AI". So winning and losing is
  judged by what each side has lost in the war, not by the size of the
  armies. The minimum length, and how the two factors combine, are not given.
  Players in the same thread: Harovan (#3), the AI often asks for peace "when
  it has no offensive units left"; solo (#10), "The number of turns since a
  war starts is definitely a factor." (re-read 2026-10-31)
- **The AI accepts peace under threat only if it is losing.** `diplomacy.txt`
  `AIPEACETREATYTHREAT` (reply to "Our troops approach your cities"): "Fool! Do
  you think we are frightened of your puny soldiers? Well, as fate would have
  it, we are. We agree." `AINOPEACETREATY`: "Not likely! $CIVNAME5 is winning!"
  Players (Apolyton, "Does 'Our troops approach your cities' ever work?",
  https://codehappy.net/apolyton/threads/71589-1.htm, Dec 2002) report it
  works only with a real threat. Catt: "it would work for me only in those
  instances in which I had a large force right outside a city or two."
  Anecdotal.

## 6.6 Gold-per-turn deals and reputation

- **What breaks a 20-turn GPT deal.** Source: CivFanatics article "Trading
  Reputation" (https://civfanatics.com/?p=280). Quote: "declare war on that AI
  (including MA/MPP)", "break an MA or MPP", "the trade route between you and
  the AI is cut".
- **No reputation hit if the AI ends the deal.** Source: same article.
  Quote: "Of course you get no rep hit if the payee decides to end the deal by
  itself." Destroying a third party under an MA also ends the deal harmlessly.
  Quote: "Your rep isn't affected."
- **Reputation is per AI, but news of a betrayal spreads.** Source: same
  article. Quote: "Each AI has a record of your reputation separately." An AI
  may also treat you as untrustworthy if it knows you broke a deal with
  another AI it is not at war with. Confirmed by Conquests `diplomacy.txt`
  `AIREJECTINFORMEDOFBROKENDEAL`. Quote: "$CIVNAME5 has informed me that you
  have broken deals with them in the past."
- **A ruined reputation is effectively permanent for GPT deals.** Sources:
  "Trading Reputation", quote: "once you break a gpt deal you'll never be able
  to make such deal again". CivFanatics Civ3 FAQ, quote: "will never again for
  the game accept per-turn payments or deals from you." The article names two
  workarounds: renegotiating peace, or signing an MA against the betrayed AI
  ("B will forgive your betrayal to A").
- **An AI war declaration during a deal does not hurt your reputation.**
  Source: CivFanatics Civ3 FAQ. Quote: "the AI declares war on you when you
  have a 20-turn deal going on" (not counted against you).
- **The AI has specific refusal lines for each kind of bad record.**
  Source: Conquests `diplomacy.txt`.
  - `AIREJECTBROKENDEAL`: "My people remember too well the deals you have
    broken with us in the past."
  - `AIREJECTPASSAGEVIOLATION`: "the last time we had a right of passage
    treaty with $CIVNAME1, you sort of attacked us, didn't you?"
  - `AIREJECTMILITARYRETRACT`: "You have failed to fully honor diplomatic
    agreements with us in the past".

  So the engine tracks at least three kinds of record: broken deals, ROP
  violations, and alliance/treaty retractions. Each also has an "informed of"
  version for third parties.
- **The advisor flags liars and betrayers.** Source: `script.txt`
  `DIPLOADVICEFOREIGN_LIAR` / `_BETRAYAL`. Quotes: "$LEADER0 is a [known liar
  and cheat.]" and "We know that $LEADER0 has [betrayed our friends] the
  $CIVNOUN1."
- **Sneak attacks.** Declaring war with any of your units inside the target's
  territory counts as breaking an implicit Right of Passage. Source:
  CivFanatics, "Reputation for sneak attacks" (Civ3, July 2002)
  (https://forums.civfanatics.com/threads/reputation-for-sneak-attacks.28206).
  Catt, quote: "Reputation hits come not because of war, but because of
  declaring war with troops in enemy territory." Lt. 'Killer' M., quote:
  "having a Scout/worker/any other unit in enemy territory when declaring war
  is enough to be a ROP-breaker".
- **Whoever declares takes the hit, and a lapsed treaty means war.** Source:
  CivFanatics, "How does the AI calculate reputation?" (Civ3, Jun 2002),
  Bamspeedy
  (https://forums.civfanatics.com/threads/how-does-the-ai-calculate-reputation.26122).
  Quotes: "The rep hit goes to whoever actually does the declaration of war."
  and "No peace treaty = war."
- **Razing ruins reputation.** Source: "The Road to Diplomatic Victory"
  (https://civfanatics.com/civ3/strategy/empire-management/the-road-to-diplomatic-victory/).
  Quote: razing an AI city "will destroy your reputation." Also: "if you trash
  your reputation no amount of gifts and bribery can fix it."
- **Failed espionage hurts reputation.** Source: Conquests Civilopedia
  `GCON_Espionage`. Quote: "Remember though, failed missions can seriously
  damage your reputation."
- **The manual's definition of reputation.** Manual p.~156. Quotes: "Your
  reputation is based not on how peaceful or how warlike you are toward your
  neighbors, but on how often you keep your word." Razing and ROP abuse also
  count: "Savagely razing the city of an enemy or using a right of passage
  agreement to set up Cannons to bombard your opponent's cities are acts likely
  to be deplored throughout the known world." And: "Espionage, whether
  successful or not, can also damage your standing."
- **ROP backstabs ruin trust with everyone.** Manual p.~159. Quote: "Using a
  right of passage to infiltrate your troops for a surprise attack is
  remembered as a cold-hearted breach of trust by all of the other
  civilizations."
- **Which deals are "rep-risky" (tested, C3C).** Only deals where you give
  per-turn gold or resources. AlanH (CFC 113376 page 3,
  https://forums.civfanatics.com/threads/trading-reputation.113376/page-3, Nov
  2009): "You only get a rep hit if you are paying per-turn gold or resources
  to the AI and that deal is ended prematurely." Optional, same page: "if
  you're not giving anything from your side, you can safely declare on that AI
  without any fear for your reputation." tmn (2017), same page, disagrees for
  agreements: "Peace treaty, Right of Passage and Trade Embargo are all
  rep-risky." See Disputed for peace treaties.
- **How a resource deal's composition matters (tested, PTW).** Bamspeedy (CFC
  "AI attitude", page 6, https://forums.civfanatics.com/threads/ai-attitude.44999/page-6,
  Jul 29 2003): when you give a resource and its route breaks, getting back a
  resource or GPT costs only "+1" attitude and trading stays possible; getting
  back only techs, a map or a lump sum costs "+4" and blocks later per-turn
  deals. Quote: "It doesn't matter who's fault it is." Page 7 (Jul 30 2003):
  "You can accept lump sum, techs, or maps IF they are also paying you gpt (or
  giving you a resource) in that deal."
- **A broken reputation still allows bad lump-sum-for-GPT deals.** Vol (CFC
  113376 page 2, 2005): "Prior to broken reputation, I could get 16 gold for 1
  gpt." Afterwards "I could get 3 gold for 1 gpt". Arrian (Apolyton,
  "Reputation: What's the point?",
  https://codehappy.net/apolyton/threads/53506-1.htm, Jun 2002): "A bad rep will
  prevent the AI from selling you stuff in exchange for gold per turn" and you
  "will be unable to get RoP agreements."
- **Reputation can be recovered with civs cut off from the victim.** Vol (CFC
  113376 page 2, Jul 31 2005): "Your reputation is _restored_ with
  civilizations that can no longer contact the civilization you offended." And
  (Aug 1 2005): "if that civ is ever eliminated, everyone will go back to
  treating you with broken reputation." Destroying the victim before it meets
  anyone else does work. vmxa (page 3, Dec 2010): "if civ A is eliminated,
  before civ A mets otherss, no one will know of you rep issue." Bamspeedy
  (CFC 44999 page 2, PTW): "If you kill the Aztecs before he meets anyone else,
  you get no attitude or reputation hits." These are player tests in C3C
  (Vol's save was checked in 1.22 by nullspace).
- **Slow recovery by keeping your word (manual).** Manual p.~156. Quote: "Over
  long periods of time, if you mend your ways by keeping your word to other
  rulers, the black marks on your reputation can be partially erased and your
  honor somewhat redeemed." Alliance breaks are "only very slowly erased by
  time" (p.~159). This conflicts with the "never again" community findings
  (see Disputed).
- **Declaring war from outside their territory is safe.** Whomp (CFC 113376
  page 2, Mar 2005): "since you were out of AI territory when you declared you
  would not get the rep hit." The CFC FAQ counts ships: "declared war on them
  when you had units in their territory (this includes ships)".
- **Cancel a deal before declaring war.** Tone (CFC staff, "Cancel right of
  passage?", https://forums.civfanatics.com/threads/cancel-right-of-passage.228499/,
  C3C, Jun 2007): "you cannot cancel any deal in the first 20 turns via this
  screen." After 20 turns, cancel through the Active button first; cancelling
  by declaring war means your reputation "will be trashed".
- **Firaxis view: per-turn deals, not sneak attacks, set reputation.**
  alexman (later a Firaxis engineer; Apolyton, "He had a nasty reputation as
  a cruel dude...", https://codehappy.net/apolyton/threads/64554-1.htm, Oct
  2002, his own play experience): "as long as you don't break any per-turn
  deals, your reputation doesn't take a hit." and "I think sneak attacking must
  hurt their attitude towards you, not your reputation." Opinion.

## 6.7 AI attitude: levels and modifiers

Primary source: CivFanatics "AI Attitude Study"
(https://civfanatics.com/civ3/strategy/game-mechanics/ai-attitude-study/),
tested on PTW 1.01. Quote: "This test was with the 1.01 version of PTW".
In this study a negative number is good.

- **Bands.** Quotes: "-11 and lower = Gracious", "-1 through -10 = Polite",
  "0 = Cautious", "1-10 = Annoyed", "11 through 100 = Furious".
- **Starting value.** The baseline is 0. Sharing a culture group gives -1.
  Aggression level adds a difficulty-dependent amount. For example, on
  Regent-Monarch AL5 gives +3 and AL1 gives -1, and on Emperor-Deity AL5
  gives +3 and AL1 gives 0 (study, summarised). Conquests Civilopedia
  `GCON_Culture` agrees on culture groups. Quote: "nations of the same global
  culture tend to start near each other and are more tolerant in diplomacy."
  C3X-THREAD players say the same: tjs282 (post-16154963, page-21 #405,
  "AFAIK"): (paraphrase from a summarised fetch) "same-Culture Civs are also coded to be more friendly to
  one another initially"; Virote_Considon (post-16428036, page-69 #1369):
  "Civs have slightly less initial hostility to other civs in their
  same culture".
- **Government.** Same government: -1 (study, summarised). Quotes: "-5
  Favorite government (only applies if both you and the AI are in this
  government)" and "+4 Shunned government (only applies if you are in this
  government and the AI is not)".
- **Embassy: -2 (good).** Study, quotes: "-2 If you have an embassy with the
  AI" and, in its worked example, "You have an embassy with that civ (-2)". The
  forum version gives the same line (Bamspeedy,
  https://forums.civfanatics.com/threads/ai-attitude.44999/, page 1; requoted
  by WillJ on page 2 and by Jarek Noschese on page 8). No "+2 embassy" line
  was found on a re-read of the study page. (Former Disputed item.)
- **Agreements.** Quotes: "-5 If you have a Right of Passage with the AI."
  and "-10 If you have a Mutual Protection Pact with the AI." A common enemy
  in war gives -3, an alliance -2, and both together -5 (study, summarised).
  Damage done to a common enemy gives -1 to -3 per act, and razing an enemy
  city -7, capped near -15. This decays by 1 per turn.
- **Trade.** A recent map or tech trade, or an active 20-turn deal, gives -1.
  Trading or giving a resource gives -5. Signing an embargo gives -1 with the
  partner and +10 with the victim (study, summarised).
- **Gifts.** About -1 per 10 gold of gifted gold or techs, capped at -10.
  Quote: "This bonus decays at about 10 gold/turn."
- **Culture lead.** -1 if you lead in culture, +1 if the AI leads (study,
  summarised).
- **Tribute.** Quotes: "-1 You pay tribute to the AI.", "+2 You demand tribute
  from the AI. Whether they actually pay the tribute or not." and "+1 If you
  call the AI's bluff when he demands tribute".
- **Units in territory.** Being asked to leave gives +1. Being told to leave
  or declare war gives +4. Both are temporary and reverse when you comply
  (study, summarised).
- **Permanent penalties** (study). Quotes: "+4 for breaking a peace treaty
  with that civ.", "+1 if you raze the city of another AI.", "+12 if you raze
  a city of that civ.", "Nukes: Victim of the nuke adds +32!", "Other civs
  will add on +16". Summarised from the study: breaking an alliance +12
  (others +2); breaking an ROP +4, or +6 with units inside; selling a worker
  +1; accepting a culture flip +1 and refusing one -1.
- **Failed espionage.** Quotes: "+4 for failing at an espionage mission (any
  mission or attempting to plant a spy)." (temporary) and "+1 for failing at
  an espionage mission" (permanent).
- **Power lead halves the good effects.** Quote: "Power lead: If you have a
  power lead, most of the good effects (negative numbers) are halved." The
  study says ROP is the exception.
- **Things with no measured effect** (study, summarised): owning resources
  the AI lacks, sharing a landmass, and culture pressure on border cities.
- **Follow-up findings from the forum thread** (Bamspeedy and SirPleb, CFC
  "AI attitude", https://forums.civfanatics.com/threads/ai-attitude.44999/,
  8 pages, Feb 2003-2005; PTW 1.0x-1.21 unless noted). SirPleb (page 4, Feb
  28 2003) thinks "most (maybe even all) of Bamspeedy's discoveries apply
  exactly the same way in 1.29."
  - **War declarations** (page 5, Mar 2 2003): the target adds +4 for each war
    you declare. Neutral civs add +1 for the first and +2 for the second;
    "there is a cap of +2 that nuetral civs will add on."
  - **Alliance against a civ** (SirPleb, page 5, Mar 2003): "+10 attitude
    penalty from the target Civ when you enter an alliance against it", removed
    when the alliance ends.
  - **Starving citizens** (SirPleb, page 5): "+1 for each citizen starved",
    at war or not.
  - **War damage recovery is slow and non-linear** (SirPleb, page 5): 16 kills
    gave +17 and were gone after 18 turns of peace. Heavier damage (21 kills,
    2 cities, 6 starved) took about 95 turns. +45 had not recovered after 100+
    turns.
  - **Privateers** (page 4, Mar 2 2003): "+1 when you attack their ship (but
    your privateer dies), and +3 if you do sink their ship." Temporary.
    alexman (Firaxis), replying to the Apolyton repost of the study ("AI
    attitude revealed", https://codehappy.net/apolyton/threads/79615-1.htm,
    6 Mar 2003): the privateer penalty is meant to show that other nations
    suspect you paid for the attacks, and "You get a rep hit when you break
    deals. The use of privateers does not cause war, so it doesn't break
    deals." (So privateers cost attitude, not reputation.)
  - **Disbanding or abandoning a foreign city** (page 6, PTW 1.21, Jun 2003):
    building a worker or settler to disband a city with 50%+ foreigners "is the
    same as if you razed it." Also: "Donations of cities are treated as a
    gift".
  - **Culture flips** (page 5): refusing gives -1, and "This is cumulitive."
  - **No penalty for refusing AI offers** (page 5, Mar 8 2003): "No attitude
    adjustments for not accepting an offer they make." The recent-trade bonus
    lapses: "the 'recent trade' bonus would disappear if you haven't traded
    with a civ lately."
  - **Gifts** (pages 3, 4, 6, 7). The cap is -10 when the AI has the power lead
    and -5 when you have it (page 3). Several gifts on one turn add up. Over
    100 gold extends the -10 before decay starts (page 6, Apr 2003): "A 100
    gold donation gets them to the max of -10, but then this decreases at
    -1/turn (or 10 gold/turn)." A 5 gpt deal counted only -9 (page 4). Page 4
    (Mar 2003) says "-1 is permanent if you make a donation of 100 gold or
    higher", but page 7 (Oct 2003) calls the War Academy's "permanent" wording
    outdated. Treat the permanent -1 as unsettled.
  - **Espionage** (page 2): of the +4 for a failed mission, "+3 seems to be
    temporary, +1 is permanent." He recovered the temporary part after "about
    20 turns".
  - **Nukes** (page 3): the victim's +32 and others' +16 were "permanent (for
    at least the 30-50+ turns that I pushed 'end turn')". Civs that never met
    the victim also add +16.
  - **Asking units to leave** lowers attitude each time. Spoonwood (CFC
    "Trespass Doctrine", https://forums.civfanatics.com/threads/trespass-doctrine.690905/,
    C3C, Jul 2024): "Everytime you tell an AI to get out your territory, that
    decreases their attitude towards you."
- **Attitude starts from the aggression level (code-level).** Flintlock (CFC,
  "All About AI Aggression Levels and War Plotting",
  https://forums.civfanatics.com/threads/all-about-ai-aggression-levels-and-war-plotting.703785/,
  C3C 1.22, Jun 2026, reading the executable): "the game begins with the AI's
  aggression level calculated as above then adds the other factors on top".
  The effective level is: "level = civ.AggressionLevel + GameDifficulty // 2 -
  DifficultyLevelCount // 6" and "return max(-2, min(level, 2))", with
  "GameDifficulty = 0 for Chieftain, 1 for Warlord, and so forth". His post #8
  adds that the game-wide "most" or "least" aggressive setting "overrides all
  the per-civ aggression settings". He also gives the shunned-government term
  as 5 points ("it adds 5 points"), where the study measured +4 (see Disputed).
- **What the attitude code remembers (code-level).** C3X-SRC
  (`Civ3Conquests.h`, https://github.com/maxpetul/C3X/blob/master/Civ3Conquests.h,
  read 2026-10-20) maps the per-civ-pair memory that the stock attitude
  function reads: `struct Reputation` has the counters `cancelled_deal`,
  `caught_spy`, `tribute`, `observed_military_aggression`,
  `diplomatic_threats`, `gift`, `refused_tribute_demands`, `icbm`,
  `icbm_other` and `war_damage_memory` ("Combat damage taken by 'us' (the
  Leader object) from this player ... Decays over time."). The C3X demand code
  comments that the tribute, gift and refused-demand counters "feed into the
  base game's attitude calculation". This matches the study's list of
  effects (tribute, gifts, espionage, nukes, war damage, broken deals). The
  weights are not in the header.
- **Sign of the engine's attitude score (code-level).** In the executable a
  higher attitude score is worse, the same as the study's convention. C3X
  adds a relation penalty for its (modded) foreign-territory colonies with
  `score += penalty` on top of the stock `Leader_get_attitude_toward`
  (`injected_code.c`), and its changelog records fixing a version where the
  penalty was "improving instead of harming relations".
- **BIQ personality fields (`conquests.biq` RACE, local dump).** Each civ has
  `AggressionLevel` (stored -2..2, shown as 1..5 in the editor),
  `FavoriteGovernment`, `ShunnedGovernment` and `CultureGroup`. It also has
  the AI build flags "Never"/"Often" for offensive/defensive/artillery land
  units, settlers, workers, naval, air, growth, production, happiness,
  science, wealth, trade, explore and culture, plus NoWonders/NoSmallWonders
  and the Manage/Emphasize governor flags. Examples from the dump: "Germany
  aggr=2 fav=4 shun=3", "France aggr=-2 fav=4 shun=6", "Mongols aggr=2 fav=1
  shun=5". Government indices: 0 Anarchy, 1 Despotism, 2 Monarchy,
  3 Communism, 4 Republic, 5 Democracy, 6 Fascism, 7 Feudalism.
- **Barbarians are a RACE entry with no personality.** Dump: "A Barbarian
  Chiefdom aggr=0 fav=-1 shun=-1 culture=-1". There is no diplomacy with
  barbarians: the Civilopedia `GCON_Barbarians` describes only villages and
  raiders, and no diplomacy text targets them. UNVERIFIED as an explicit rule.

## 6.8 AI trade valuation

- **Where the AI's markup is applied is not found in the code.** Flintlock
  (C3X-THREAD post-16275461, page-36 #709): "The function that
  calculates the AI's offer is long and I wasn't able to see where it tilts
  the trade in its favor." (post-16892444, page-160 #3198):
  "I wasn't able to find the point where the AI decides how much
  to try to cheat the player." So TheNiceOne's table below has no code
  confirmation. Tech values from the AI's one tech-valuation function are
  "roughly equivalent to the amount of gold the AI would ask"
  (post-16719849, page-123 #2448; section 4 §4.7).

- **The AI-human trade markup is hard-coded.** It depends on difficulty, the
  AI's aggression level and attitude. The editor's AI-AI trade rate does not
  apply to it. Source: TheNiceOne, "AI-human trade revisited" (1.29, Jan 19
  2003) (https://forums.civfanatics.com/threads/ai-human-trade-revisited.42110;
  mirror
  https://apolyton.net/forum/miscellaneous/archives/civ3-general-archive/86979-ai-human-trade-revisited).
  The summarised table:

  | Difficulty | Markup by AL1..AL5 |
  |---|---|
  | Chieftain | 0% at every AL |
  | Warlord | 0, 0, 1, 2, 3% |
  | Regent | 2% from AL2, +2% per AL (8% at AL5) |
  | Monarch | 3% from AL2, +3% per AL (12% at AL5) |
  | Emperor | 4% from AL1, +4% per AL, capped at 16% |
  | Deity | 5% from AL1, +5% per AL, capped at 20% |

  Buying: price = base x (1 + bonus). Selling: price = base / (1 + bonus).
  Quote: "the AI-human trade bonus may be reduced by 1 or 2 %" (at Polite or
  Gracious).
- **Tech price base.** The human's research cost is divided by the AI cost
  factor on Monarch and above. Quote: "Monarch 0.9, emperor 0.8, deity 0.6."
  and "the human player's research cost is divided by the AI's cost factor".
  These match `conquests.biq` DIFF CostFactor (local dump: "Monarch
  costFactor=9", "Emperor costFactor=8", "Deity costFactor=6", "Demigod
  costFactor=7", "Sid costFactor=4", against Regent's 10).
- **The tech price falls with known civs and with civs that have the tech.**
  Source: same post. Quote: "number of known civs and number of civs that
  already has the tech". The exact function is UNVERIFIED. The commonly
  cited "x1.5 to x2 depending on how many civs know it" formula is **Civ4**
  (`CvTeamAI::AI_techTradeVal`;
  https://forums.civfanatics.com/threads/ai-valuation-of-techs.658224), so it
  is not used here.
- **One tech-value function for AI-AI and AI-human trades (Firaxis).** Soren
  Johnson (Apolyton, "Yet another AI CHEAT - Technology",
  https://codehappy.net/apolyton/threads/40548-1.htm, post #25, Jan 23 2002,
  vanilla): "as more civs acquire each tech, its value goes down, which makes
  the AI more willing to trade it" and "the same function is used to determine
  the value of a tech for both AI-to-AI trades and human-to-AI trades". Soren
  (Apolyton, "AI 'Tech Whoring'", https://codehappy.net/apolyton/threads/48219-1.htm,
  post #10, Apr 23 2002): "There is an AI function which estimates how much
  each tech is worth to each player."
- **AI-AI tech deals: full value, half value at worst, only known civs
  count (Firaxis).** Soren Johnson (Apolyton, "v1.17 AI vs AI tech trading",
  https://codehappy.net/apolyton/threads/42905-1.htm, vanilla 1.17f, Feb
  2002). Post #2 (20 Feb 2002): the AIs "always try to meet the _full_ value of
  the tech" and "When necessary, a AI might be willing to sell a tech for down
  to half-value, but only if the other AI has nothing else to give for it".
  Also: "the AI's contact each other more often and ... they are now doing
  per-turn gold offers for techs." Post #16 (21 Feb 2002): "The whole
  techs-are-cheaper-if-known-by-other-civs rule only applies to _known_ civs.
  So even if every civ but one know Republic, it is still full value to that
  last civ if it is still alone." Post #11: a tech the buyer is "close to
  discovering" is "not worth very much via trade", and "if you are playing at a
  level above Regent, techs are worth less to the AI than the human because
  they get research bonuses". Post #21: a buyer one turn from the tech "was
  only willing to give away very little." So the value inputs he names are the
  buyer's own research progress, the number of civs known to the buyer that
  have the tech, and the difficulty. No formula is given.
- **AIs at war do not trade with each other (players).** Same thread, page 4:
  Zachriel (24 Feb 2002): "if the AIs are fighting they don't transfer techs at
  all." ShuShu (25 Feb 2002): "AIs don't trade with each other when they are at
  war." This fits the code: the AI-AI negotiation gate is only reached for
  pairs not at war (C3X-SRC, 6.9).
- **What the AI values in a tech (players, unverified).** TimBentley (CFC
  "how do AIs value techs in market?",
  https://forums.civfanatics.com/threads/how-do-ais-value-techs-in-market.126379/,
  C3C, Aug 2005): "The things that make the AI value techs more include enables
  government, drafting, or mobilization." He adds "a formula to determine the
  trade value has not been computed." Bamspeedy (CFC
  https://forums.civfanatics.com/threads/trading-mainly-for-beginners-but-others-may-find-it-useful.24553/,
  1.29f, Aug 2002): "I believe the AI values the price for techs partially on
  how much it would cost you to research it." ixnay (Apolyton 48219, 1.21,
  Apr 2002): "the AI doesn't seem to value dead-end techs that give a wonder
  which has already been built". SJ Frank (CFC "Trade is difficulty with AI
  civs", https://forums.civfanatics.com/threads/trade-is-difficulty-with-ai-civs.115977/,
  C3C, Apr 2005): "a monopoly tech costs twice as much to buy." Opinion.
- **Competing sellers lower the price.** justanick (CFC "What's a good rule of
  thumb to trade techs with AI?",
  https://forums.civfanatics.com/threads/whats-a-good-rule-of-thumb-to-trade-techs-with-ai.700894/,
  C3C, Dec 2025): "If only one civilization has researched a tech, then the
  trading value is still high." and "The trading value drops a lot if several
  potential sellers need to compete on the price." He also gives the
  research-cost discount per known civ that has the tech, e.g. "In case of 8
  civs it is 14/14, 13/14, 12/14, 11/14, 10/14, 9/14, 8/14 and 7/14 of the full
  price" (a research-cost rule, section 4).
- **How the AI prices maps, ROP, luxuries and contacts (1.29f, tested in
  play).** Bamspeedy (CFC 24553, Jun-Aug 2002):
  - "ROP is based entirely on territory size."
  - "The value of your map is based entirely on how many more tiles you have
    uncovered than the person your trading it to." And: "If you sell your map
    to any AI civ, once you end that turn, your map is worthless" (the AIs
    pass it on).
  - "Luxuries are valued on how many happy faces they add to your civ."
    justanick (CFC "How does the AI price resources and luxuries?",
    https://forums.civfanatics.com/threads/how-does-the-ai-price-resources-and-luxuries.695776/,
    C3C, Feb 2025) agrees: "It is not city count, it is happy faces gained."
    No tested rule was found for strategic resources.
  - Contacts: "As you continue to get contact with more civs the price of
    contact with other civs diminish just like techs do."
  - Workers: "They usually cost 25 to 27 gold each" (Arrian, Apolyton 59851,
    vanilla, Aug 2002, early game).
- **Per-turn gold is worth about 18 gold per 1 gpt.** Civ3 Strategy
  Compilation (CFC, May 2002, vanilla,
  https://www.civfanatics.com/wp-content/uploads/2024/07/Civ3_Strat_Compilation.pdf):
  "Per-Turn Gold amounts are valued by the AI at about 18/20 of their total
  gold value (both ways across the table)." The same 18:1 rate shows up in C3C
  loans (below). The compilation adds that the AI needs you to hold "enough
  gold currently available to make at least 1 payment".
- **Owner ruling (engine): the AI never pays gold per turn beyond its
  surplus.** Sources: Bamspeedy (CFC "AI refusing gold per turn?",
  https://forums.civfanatics.com/threads/ai-refusing-gold-per-turn.26118/,
  vanilla, Jun 29 2002) says a patch made the AI keep cash for its own
  research and pay GPT only from excess: "They usually don't have excess cash
  until they switch out of despositism." Arrian, same thread: when the advisor
  says the AI "will never accept" a tech-for-GPT offer, it "literally cannot
  afford it." joncnunn (Apolyton 71589, Dec 2002): the AI cannot sign a GPT deal
  that puts it into deficit, whatever its treasury (summarised). CFC FAQ: "On
  lower levels, at least early in the game, the AI usually has no extra gold per
  turn." Vanilla `README.txt` (Fixes v1.21f): "Fixed bug in diplo bargaining AI
  for per-turn gold deals." Soren Johnson (Firaxis, Apolyton 42905, posts #4
  and #10, 20 Feb 2002, 1.17f): "They aren't allowed to sign per-turn deals
  they can't pay." and "the AI will only trade gold per-turn directly from
  their income to other civs". JohnM2433 (Apolyton 59508, Aug 2002): "the AI
  won't pay gold/turn that exceeds its income in gold per turn."
- **AI-AI trade rate only covers a shortfall (Firaxis).** Soren Johnson
  (Apolyton 48219, post #4, Apr 23 2002): "When the AI's trade with each other,
  they always ask for full value for their techs, maps, comms, etc." and "the AI
  trading rate determines how much the AI will bend if the other AI does not
  have enough money". WarpStorm, relaying Soren (Apolyton "Can someone explain
  AI to AI Trade Rate?", https://codehappy.net/apolyton/threads/68856-1.htm,
  Nov 2002): "AI to AI trade rate only applies to AI players, not to the human
  players." EDHELP ("AI to AI trade rate (AI Bonuses)") words it as a plain
  multiplier: "which is multiplied by the total value (in gold) of the
  initiating civ's offerings ... with a rate of 120, an AI civ offering 100 gold
  to another AI civ for something would actually be worth 120 gold". The two
  agree on the multiplier and differ on when it applies; follow Soren.
- **The AI-human markup by aggression (code-level).** Flintlock (CFC 703785,
  C3C 1.22) lists "Adjusting the value of trade offers" among the uses of the
  effective aggression level. This fits TheNiceOne's table by aggression level.
- **Things that do not change the price** (TheNiceOne, summarised): relative
  culture, relative military, culture group, and favourite or shunned
  government.
- **Attitude does change the asking price.** Source: Zwingli, "AI Attitude
  and Trade" (Civ3 1.29, Jul 18 2003), Persia at Deity buying monopoly Radio
  (https://forums.civfanatics.com/threads/ai-attitude-and-trade.59079/).
  Quotes: "Gracious** - 681 gpt (620 gpt)", "Cautious - 705 gpt (641 gpt)",
  "Furious (unlimited demands) - 939 (854)", and "For each attitude point
  drop below Cautious, Persia charged about 0.32% more for the tech". This
  conflicts with TheNiceOne (see Disputed).
- **AI-AI trade rate is a BIQ difficulty field.** Source: `conquests.biq`
  DIFF `AIToAITradeRate` (local dump: Chieftain 110 rising by 10 per level to
  Deity 170, Sid 200). Vanilla `README.txt`, quote: "Editor: Exposed AI to AI
  trade rate."
- **Exploits removed by patches.** Source: vanilla `README.txt`. Quotes: "You
  can no longer demand very large amounts of gold in diplomacy.", "Fixed AI
  exploit with regards to giving cities away in diplomacy." and "The AI no
  longer trades during the players turn."
- **The advisor grades deals on five levels.** Source: `script.txt`
  `DIPLOADVICETRADE_DEAL_*`, which match `diplomacy.txt`
  `AIWEAKREJECT`/`AINEUTRALREJECT`/`AISTRONGREJECT`/`AITOTALREJECT`. Quotes:
  "This deal will probably be acceptable.", "We're getting close to a deal
  here.", "I doubt they will accept this proposal.", "They will probably be
  insulted by this deal.", "They would never accept such a deal." Players
  quote the advisor so (wervdon, CFC "When the AI refuses a deal that is too
  good", https://forums.civfanatics.com/threads/when-the-ai-refuses-a-deal-that-is-too-good.17044/:
  "They would never accept such a deal!"). No source gives the value gaps
  between the levels (OpenCiv3's are HEURISTIC, see CounterOfferAI).
- **diplomacy.txt's layout (read from the file).** A label's `#civ 1` gives
  each civ its own group of lines, in the BIQ's RACE order (#AIFIRSTCONTACT
  has 32 lines, one per race; line 21 is the Celts' "Céad Míle Fáilte", line
  29 the Dutch "House of Orange"); `#mood 1` or `#power 1` splits a group in
  three; `#random n` is the lines in each. So the reject labels have 192 = 32
  x 3 x 2 lines and #AICOUNTERPEACE 36 = 3 x 12. The three mood sets read
  friendly, then neutral, then hostile (UNVERIFIED which attitudes map to
  which). Placeholder numbers differ per label; the comments above each label
  say what they stand for.
- **The AI's counter-offers leave it a margin.** Qitai (CFC "A small trick to
  keep AI happy", Civ3, Jun 2003,
  https://forums.civfanatics.com/threads/a-small-trick-to-keep-ai-happy.57086/):
  "Usually they give 10%-15% more of what they offer", and "The limit is near
  when you find they are not giving everything they have". BiggN (Apolyton
  43227, "Won't the AI offer anything?"): asked what it would give for a map,
  the AI offered "the 20-40 gold that they have in stock", sometimes with furs
  or silk. Counters can be inconsistent: Cleo refused a deal she had herself
  offered (Pyrodrew, Apolyton 43582, "AI diplomacy flaws"). The human's asks
  are `#USERASKFORCOUNTER` ("What do you want for $GIVE0?") and
  `#USERASKFOREXCHANGE` ("Would you care to offer something in exchange for
  $GIVE0?"); the answers `#AICOUNTERPEACE` ("How about this?") and
  `#AINOCOUNTERPEACE` ("Regrettably, we don't see how a deal could be worked
  out..."). UNSOURCED: the order the AI fills a counter-offer beyond gold
  first.
- **Selling techs for large GPT can drain the AI economy for 20 turns.**
  Source: "Crippling the AI's Research Capability"
  (https://civfanatics.com/?p=170). Quotes: "There is a limit to how much the
  AI will pay you" and "cripple their economic and scientific growth for 20
  turns".
- **The AI offers loans.** Source: `diplomacy.txt` `AILOANOFFER`. Quote: "We
  are always happy to lend you money, $PLAYER0 - at surprisingly high
  interest rates, that is." You can ask (`USERASKFORLOAN`: "Grant us a loan of
  gold?"), and the AI can refuse (`AINOLOANOFFER`: "Unfortunately, we cannot
  afford to grant you a loan at this time."). The terms of an AI-to-human loan
  are UNVERIFIED.
- **Lending to the AI: about 18 gold per 1 gpt (tested, C3C).** dominatr (CFC
  "Loaning Money", https://forums.civfanatics.com/threads/loaning-money.449848,
  Dec 2011): "The AI doesn't seem to accept less than 18 gold for every 1 gpt."
  Example: "loan 350 gold" for "20 gpt, or 400 gold". Spoonwood, same thread:
  "At minimum aggression, unless I've soiled my reputation, it's always come as
  1 gpt for 18 gold." dexters (CFC "Managing the World: The Machiavellian
  Doctrine", https://forums.civfanatics.com/threads/managing-the-world-the-machiavellian-doctrine.116771/,
  C3C, Apr 2005, tested in play): "The interest rate is affected by attitude
  of the of the AI Civ towards you" and "Relative military strength has no
  effect on the interest rate".

## 6.9 Tribute and demands

- **The AI demands tribute and threatens.** Source: `diplomacy.txt`
  `AIDEMANDTRIBUTE`. Quote: "Think well, $PLAYER0: if you give us tribute, our
  friendship will continue. If not, we will have to seriously re-evaluate our
  relationship." If you refuse ("call the bluff"), the outcome is
  `AIBLUFFISCALLED` or war. Quote: "Very well, then. We shall leave you to
  think about the future".
- **The AI can also give in to your demands.** Sources: `diplomacy.txt`
  `AIGIVETRIBUTE`, quote: "We will give you $GET3 as you request. But we do
  this not out of fear of you and your minions, but out of a love of peace."
  The user side, `USERWILLGIVETRIBUTE`: "We will give you what you want - this
  time."
- **Attitude effects of tribute:** see 6.7 (+2 for demanding, +1 for calling
  a bluff, -1 for paying).
- **The AI demands only from humans, and aggression sets how often
  (code-level).** Flintlock (CFC 703785, C3C 1.22, Jun 2026) lists among the
  uses of the effective aggression level: "Chance to demand things from a human
  player (As far as I know AIs won't demand things from each other.)" and
  "Chance to declare war on someone who's refused a demand or nuked a third
  party". C3X-INI agrees that stock AIs do not demand from each other: its
  option "Allows AI players to demand tribute from each other. The AI uses the
  same demand selection, acceptance, diplomatic penalty, and possible war logic
  that the base game uses for demands involving a human player."
- **The demand gates, in order (code-level).** C3X-SRC `injected_code.c`
  (https://github.com/maxpetul/C3X/blob/master/injected_code.c, read
  2026-10-20), function `try_ai_demand_from_other_ai`, whose comment says:
  "This is the same sequence of demand gates used by Leader::impl_begin_turn
  for demands against human players. demand_items also assembles the offer
  list in exactly the same way." The mod's function calls the stock
  functions (`Leader_demand_items`, `Leader_consider_trade`,
  `Leader_would_yield_to_demand`, `Leader_ai_roll_to_declare_after_provocation`)
  and re-creates only the gates between them. Each check must pass, or no
  demand is made that turn:
  1. Contact with the target, not at war with it, the AI's per-target
     diplomacy cooldown at 0 (offline games), and the stock "would meet with"
     check (`ai_can_demand_from`).
  2. The stock `Leader_demand_items` finds something to ask for. The item kinds
     in its enum are map, contact, strategic resource, luxury resource, gold
     and technology.
  3. Power: `if (32 - this->power_rank > 2 * (32 - recipient->power_rank))`
     the AI does not demand. `power_rank` is 31 for the strongest civ, so
     `32 - power_rank` is the civ's place in the power ranking (1 = first). The
     AI demands only from a civ whose place is at least half its own.
  4. `rand_int (32 - this->power_rank) != 0` stops it: a one-in-(own place)
     roll, so the strongest civ always passes and the fourth passes 1 time in 4.
  5. If the AI has taken more war damage from the target than the target has
     taken from it (`war_damage_memory`), it does not demand.
  6. Attitude roll: the roll size is minus the attitude score, at least 1, and
     the roll must come up 0. Since a higher score is worse (6.7), an AI at
     Cautious or worse always passes, and the friendlier it is the rarer the
     demand.
  7. Aggression roll: `rand_int (2 - aggression) != 0` stops it, with the
     effective aggression level -2..2 (6.7). Read as code, that is always at
     +1 or +2, 1 in 2 at 0, 1 in 3 at -1 and 1 in 4 at -2.
  - After a demand the AI sets `ai_diplomacy_cooldown` for that target to
    `0x20` (32), and the target's `tribute` counter rises. If the target gives
    in, the demander's `gift` counter rises. If not, `refused_tribute_demands`
    rises and the AI may declare war (`Leader_ai_roll_to_declare_after_provocation`),
    but not while the two have a mutual protection pact
    (`Relation_Treaties ... & 4`).
  - Whether the target gives in is the stock `Leader_would_yield_to_demand`,
    fed by the target's valuation of the demanded items
    (`Leader_consider_trade`). Its internals are not shown.
  - This is a 2026 mod's reconstruction of stock gates, not Firaxis text. A
    human target's own "give in" choice is the player's; the gates above decide
    when the AI asks.
- **Demanding tribute angers the AI (manual).** Manual p.~167. Quote: "This is
  one of the more effective ways of making the other leader dislike you. In
  fact, demanding tribute is a good way to incite a declaration of war." Also:
  "If you're in an unassailable position of power, you might want to dispense
  with politeness and just demand what you want."
- **Players' reports of when the AI gives in.** Apolyton, "does 'take this deal
  or suffer' ever work?" (https://codehappy.net/apolyton/threads/68602-1.htm,
  Nov 2002, vanilla): lateralis, "if their millitary is way bigger than yours
  (your advisor says "compared to them, our millitary is weak") don't even
  think about it"; Alex says the AI accepted when "My military advisor said that
  my army was bigger than theirs." way2hot2becool (CFC "Reputations",
  https://forums.civfanatics.com/threads/reputations.160891/, C3C, 2006): strong
  units near their capital mean "they will eventually give in". Anecdotal.
- **Exact rule for when the AI gives in is still UNVERIFIED.** The only formulas found
  ("boldness x 10%", "fear", "dominance") are from Civ1, Civ2 or Civ4 threads
  (https://forums.civfanatics.com/threads/some-notes-about-diplomacy.678722/
  is Civ1; https://forums.civfanatics.com/threads/foreign-relations.76336 is
  Civ2), so they are not used here.

## 6.10 Units in foreign territory

- **The AI warns, then demands withdrawal or war.** Sources: `diplomacy.txt`
  `AIBORDERWARNING`, quote: "it appears that some of your troops have -
  inadvertently, I'm sure - wandered into our territory, near $CITY3. Kindly
  remove them." `USERBORDERWAR` (the human's version of the same threat),
  quote: "Remove your forces from our territory or declare WAR!"
- **A promise to withdraw creates a temporary implicit ROP.** Breaking that
  promise counts as backstabbing. Source: Evie, "Reputation for sneak
  attacks" (Civ3, 2002). Quote (summarised from the page): a "We'll move our
  troops out asap" reply without a withdrawal creates a temporary ROP.
- **Diplomacy removes units from territory at once.** Conquests Civilopedia
  `GCON_Territory`: "You can eject rival military units from your territory
  during diplomatic negotiations." `civ3PTW/readme.txt` (Changes v1.21f):
  "Hidden Nationality units are no longer forcibly removed from enemy territory
  via diplomacy." The same readme (Fixes v1.21f): "Fixed bug where you could
  not ask an AI civ to remove its units from your territory." Manual p.~77:
  "You can contact the owner of the stray unit and demand that it be
  immediately withdrawn. Right-click on the offending unit to do so."
- **Removed units are teleported out.** Civ3 Strategy Compilation (CFC, May
  2002, vanilla): "Units removed from enemy territory for border violations
  are teleported to nearest non-offending spot." vondrack (Apolyton "Remove
  your forces or declare war NOW",
  https://codehappy.net/apolyton/threads/55198-1.htm, Jul 2002, 1.21f) instead
  says they go toward "their original destination", and Coracle, same thread,
  saw a unit that "magically teleports itself to the OTHER side of my civ".
  Ataxerxes (CFC "Right of Passage agreements",
  https://forums.civfanatics.com/threads/right-of-passage-agreements.427477,
  C3C, Jun 2011): "Your units are ejected, sometimes to odd places."
  creamcheese, same thread: "they never boot you if you only move workers."
  The destination rule is disputed; the teleport itself is agreed. (Former open
  question 8.)
- **The AI's own replies when you tell it to leave.** `diplomacy.txt`
  `AIWILLRETREAT` / `AIWILLRETURNHOME` (it complies), for example: "We will
  withdraw immediately. My apologies." When it is the AI that warns you and
  you comply, `AIDONTTEST`: "Very well. Withdraw them and we will ignore this
  violation. But do not let it happen again!" `AIBORDERWAR` treats the
  presence as war unless you leave: "Your recent movement of troops into our
  territory constitutes a declaration of war against $CIVNAME1. Send them home
  immediately or know that YOU will be held responsible for the conflict!"
- **Timing and visibility (community, C3X-THREAD).** Erlend Philip Wood
  (post-16132380, page-16 #313): "you _are always_ asked to leave or
  declare war on the second turn." He adds (post-16823338, page-136 #2722)
  that "the AI will only ask them to leave if it can see them".
  SayHayKid (post-16605848, page-101 #2017): (paraphrase from a summarised fetch) "In standard game you
  can enter, but the other Civ will ask you to remove your forces." These are
  play reports, not tests.
- **The whole exchange, in Civ3's words (C3C `diplomacy.txt`, read
  2026-10-31).** The AI's warning is `AIBORDERWARNING` (lines naming the
  AI's city near the troops, "$CITY3"); its "leave or declare war" is
  `AIBORDERWAR` (three lines; there "$CIVNAME1" and "$CIVADJ2" are the
  AI's own, per the file's comment). The human's answers: `USERWILLRETREAT`
  ("We apologize for the mistake. We will move our troops out of your
  territory."), `USERWILLRETURNHOME` ("Our mistake." "<Your troops will move
  automatically.>") and `USERWAR` ("That's it! Prepare for WAR!"). The AI's
  replies on compliance are `AIDONTTEST` and `AIOVERLOOK` ("Good. $CIVNAME1
  shall overlook your blunder for now."). The human's own demands are
  `USERBORDERWARNING` ("Your forces are within our borders! You must
  withdraw them.") and `USERBORDERWAR`, answered by `AIWILLRETREAT` and
  `AIWILLRETURNHOME`. Which answers go with which prompt is inferred from the
  pairing of the names (WARNING/RETREAT, WAR/RETURNHOME) and the
  "<Your troops will move automatically.>" note: UNVERIFIED against a
  screenshot. `AIBORDERWARNING` and `AIDONTTEST` are marked "#civ 1 #mood 1"
  and have about 93 lines, three per civ, so Civ3 seems to pick a line by
  the AI's civ and mood (UNVERIFIED reading of the header).
- **Engine ruling (owner, 2026-10-31): the AI makes these demands of humans,
  in Civ3's form** (warning, then leave or declare war the next turn; the AI
  never declares war over it itself). This reverses the owner's ruling of
  2026-10-22, under which an AI never told a human to leave.
- **When the "leave or declare war" option appears is unknown.** Apolyton
  55198 (Jul 2002, 1.21f) has guesses only. Hurricane: "The exact number of
  units required is still unresolved, AFAIK." Fitz: "Contacting a
  settler/worker never brings up the war version of this demand."
- **Engine ruling (owner, as in GBiebs/OpenCiv3 9999221c): on peace, each
  side's units in the other's territory are moved out at once,** by the same
  teleport as an ejection. The manual's peace terms (6.5: not to "even enter
  the other's territory with military units") support it, but no Civ3 source
  was found that describes units moving on signing itself. See open
  question 12.
- **A city may be founded inside another civ's borders, and at peace that is
  an act of war (community).** Inverse Icarus (Apolyton "combat settlers",
  https://apolyton.net/forum/civilization-series/civilization-iii/126826-combat-settlers
  #1, Mar 2004): "Building a city in another civilizations land is an act of
  war, but it can be done." CommandoBob (CFC "Anybody seen this?",
  https://forums.civfanatics.com/threads/anybody-seen-this.325883 #3, Jun
  2009): "If you build a city inside someone else's border, and you are at
  peace, the act of building a city is considered an act of war", and
  "you'll get a pop-up from the Foreign Advisor asking you to wimp-out and not
  build the city" (that is `script.txt` `DECLARE_WAR`, "this will cause war
  with the $CIVADJ0 people"). wilbill (CFC "other civs encrouching on my
  borders", https://forums.civfanatics.com/threads/other-civs-encrouching-on-my-borders.113953
  #17): "Actually you can, but it's considered a declaration of war with the
  AI"; Brain (#2): "possible, but only if they are at war with you". Against:
  Grogs (#3): "You can't build a city within another nation's borders";
  Zanzin (Apolyton 126826 #7, C3C) found the option missing in one game.
  The AI doesn't do it: SJ Frank (113953 #8): "they will never settle on your
  land, not even during war"; eldar (#20): except when "reduced to a single
  settler, they will settle in place (if they can)". See open question 13.

## 6.11 Espionage

- **Two tiers.** Diplomatic missions open with Writing. Spy missions need
  Espionage and the Intelligence Agency. Source: Conquests Civilopedia
  `GCON_Espionage_Missions`. Quote: "You can launch certain diplomatic missions
  at any time after the discovery of $LINK<Writing=TECH_Writing>." Also
  "Spy missions become available after you have discovered
  $LINK<Espionage=TECH_Espionage>." `TECH_Espionage`: "Build an
  $LINK<Intelligence Agency=BLDG_Intelligence_Center> and you can conduct
  $LINK<Espionage Missions=GCON_Espionage>." `conquests.biq` BLDG has an
  "AllowsSpyMissions" flag.
- **There are no spy units.** Source: CivFanatics Civ3 FAQ. Quote: "Spies are
  not units in the game." Civ3.com FAQ on Apolyton, quote: "will no longer
  require you to use spy units".
- **The mission list (C3C).** Source: Conquests Civilopedia `GCON_Espionage`.
  Missions: Establish Embassy, Plant Spy, Steal Technology, Steal Military
  Plans ("reveals the locations of all their military units"), Initiate
  Propaganda ("If successful, the people will revolt and
  $LINK<defect=GCON_Conversion> to your side."), Expose Enemy Spy,
  Investigate City, Steal World Map, and Sabotage Production ("destroy all
  the $LINK<shields=GCON_Shields> in the production box of a rival city").
- **Plant Disease was removed in vanilla.** Source: vanilla `README.txt`.
  Quote: "Plant Disease: This espionage mission has been removed."
- **Steal Plans shows units on the Military Advisor for one turn.** Source:
  vanilla `README.txt`. Quote: "On the turn a Steal Plans mission is
  successful, when you select the civilization ... you can mouseover a unit
  and it's location will be marked on that screen's mini-map."
- **Which agent can do what (`conquests.biq` ESPN, local dump).** Each line
  gives the diplomat flag, the spy flag and the base cost:
  - Build an Embassy: diplomat, not spy, base 20.
  - Investigate City: diplomat and spy, base 10.
  - Steal Technology: diplomat and spy, base 10.
  - Steal World Map: spy only, base 1.
  - Plant Spy: spy only, base 60.
  - Steal Plans: spy only, base 10.
  - Initiate Propaganda: spy only, base 100.
  - Sabotage Production: spy only, base 10.
  - Expose Enemy Spy: spy only, base 80.
- **Requirements.** Investigate City and Steal Technology need only an
  embassy. The other five need the Intelligence Agency, an embassy and a
  planted spy. Source: CivFanatics, "Successful Espionage"
  (https://forums.civfanatics.com/threads/successful-espionage.422226). Civ3.com
  FAQ on Apolyton, quote: "only against Civs with whom you've established an
  embassy and planted a spy".
- **War.** Embassy missions stop at war because the embassy closes. A planted
  spy keeps working. Source: Civ3.com FAQ on Apolyton. Quote: "Even if you go
  to war and your embassy is closed, your spy will continue to function as
  long as it is not caught."
- **Cost formulas.** Source: "A Study of Espionage Missions". Here dist is
  the distance to your nearest city, level is 1 for a town, 2 for a city and 3
  for a metropolis, and the leading number is the BIQ base:
  - Embassy: "20 dist*level + base + pop".
  - Investigate: "10 dist*level + base * pop".
  - Steal Tech: "10 dist*level + base * techs*techrate/100".
  - Steal World Map: "1 dist*level + base * mapsize".
  - Plant Spy: "60 dist*level + base".
  - Steal Plans: "10 dist*level + base * unit".
  - Propaganda: "100 dist*level + base * pop * (1 + opp_cult/your_cult)".
  - Sabotage: "10 dist*level + base * shield".
  - Expose: "80 dist*level + base".

  "Carefully" costs 1.5 times "Immediately" and "Safely" costs 2 times. The
  War Academy page says "multiply by 50% for carefully and double for safely",
  which reads as halving, but the forum original
  (https://forums.civfanatics.com/threads/a-study-of-espionage-mission.97042/,
  Oystein, Aug 2004, quoted on page 3) says "3/2 for carefully and 2 for"
  safely. Spoonwood (page 4, Apr 2017, tested): "safe steal attempts cost
  exactly twice of an immediate steal attempt," and careful ones 1.5 times.
  (This corrects the earlier "Carefully halves" reading.) The readme says the
  cost was exposed in the editor. Quote: "Editor: Added mission cost to
  Diplomats and Spies page."
- **The editor help gives the formulas without the distance term.** EDHELP
  "Base Cost (Diplomat / Spy Mission Properties)": "Establish Embassy BaseCost
  + TargetCitySize", "Investigate City BaseCost * TargetCitySize", "Steal Tech
  (BaseCost * Target Player's Number of Techs * Tech Rate) / 100", "Steal World
  Map BaseCost * ( ( Map Width + Map Height ) / 2 )", "Plant Spy BaseCost",
  "Steal Plans BaseCost * Target Player's Number of Units", "Sabotage
  Production BaseCost * Target City's Number of Shields", "Expose Mole
  BaseCost". Propaganda has a gold term the study lacks: "BaseCost * Target
  City Size + ( BaseCost * Target City Size * Culture Ratio) + ( ( BaseCost *
  Target City Size) / 2 * Gold Ratio )". Then: "the cost is then further
  modified, taking into consideration (where applicable) the distance of the
  target, the target city's population level, the ratio of citizens between
  the initiating civ and the target civ, and the safety level modifier". The
  study's "dist*level" term matches the distance and city-level parts.
- **Success chances** (same study):
  - Chance of escaping (Immediate/Carefully/Safely): Embassy 40/55/65,
    Regular spy 60/75/85, Veteran spy 70/85/95.
  - Success is 80% if not caught, giving Embassy 32/44/52, Regular 48/60/68,
    Veteran 56/68/76.
  - Plant Spy: Embassy 50%, Conscript 20%, Regular 50%, Veteran 60%,
    Elite 75%.
  - Embassy and Investigate City always succeed.
  - Expose Enemy Spy always fails if there is no spy.

  The escape table is confirmed by Kool Keith and Jazzmail in "Steal
  Technology"
  (https://forums.civfanatics.com/threads/steal-technology.232487/post-5709659).
  Jazzmail, quote: "You have to multiply the values in the table by 0.8."
- **Binary finding: the 80% roll is for the two thefts only, and a
  successful spy can still be exposed.** Perez's decompile of the Conquests
  executable (`doc/perez-findings/Civ3_Findings.md`, Round 2d "Espionage").
  The owner ruled on 2026-11-12 that this finding is authoritative.
  - Success roll `Rand(100) < successPct`; failing it is being caught.
    "Second roll for Steal Tech and Steal World Map: after a successful first
    roll, `Rand(100) < 80` completes the mission; the remaining 20% give
    "mission failed" without being caught." *Superseded:* the study's "80% if
    not caught" for every caught-able mission. Steal Plans, Sabotage
    Production and Expose Enemy Spy use the success roll alone, so their
    chance is the escape table itself (regular spy 60/75/85).
  - "Exposed afterwards (the spy is lost even though the mission worked):
    Steal Plans 10%, Sabotage Production 20%, Expose Mole 10%." Civ3's own
    messages for it are in `script.txt`
    (#ESPIONAGE_MISSION_STEAL_PLANS_SUCCESS_EXPOSED, "However, our agent was
    caught and executed. $TITLE1 $LEADER2 is not pleased.";
    #ESPIONAGE_MISSION_SABOTAGE_PRODUCTION_SUCCESS_EXPOSED;
    #ESPIONAGE_MISSION_EXPOSE_MOLE_SUCCESS_EXPOSED). The engine counts the
    exposure as an incident because of the "not pleased" line (UNVERIFIED).
  - "The gold is charged first, always, and never refunded."
- **Binary finding: the final price** (same source and ruling):
  `((2*size - nativesOfSpyCiv + 1) * (cost + (sizeClass+1)*DAT_009e5ce4)) /
  (2*size + 1)`, so a city full of the spy civ's own citizens is much cheaper.
  `(sizeClass+1)` is the study's "level". The constant `DAT_009e5ce4` is not
  named by the decompile; the engine takes it to be the study's "dist" (the
  editor help above lists "the distance of the target" and "the ratio of
  citizens between the initiating civ and the target civ" among the
  modifiers). UNVERIFIED.
- **Plant Spy modifiers by agent level** (Oystein, CFC 97042 page 3, Mar
  2005): "Conscript -30, Regular/Embassy 0, Veteran 10, Elite 25" (summarised
  list). An elite embassy had 75% to plant and 65% for the other spy missions.
  He adds that only regular and veteran spies occur in an unmodded game, and
  that an exposed agent is still caught with "(90%)" chance.
- **Discovery is separate from success (manual).** Manual p.~169
  ("International Incidents"). Quotes: "Note that the chance of your treachery
  being discovered is distinct and separate from your odds of success. You can
  succeed and still spark an incident." and "the targeted civilization is
  likely to treat your treachery as an act of war. (A target with which you
  are good friends, though, might sometimes choose to disregard your act.)"
- **Spy experience comes from government.** `conquests.biq` GOVT "SpiesAre":
  Communism and Fascism give veteran (2), the others regular (1).
  "DiplomatsAre" is regular for all. CivFanatics FAQ, quote: "the chances to
  plant a spy depend on your government, but it is around 50-60%."
- **Failure lockout.**
  - Source: `civ3PTW/readme.txt`. Quote: "If you fail a spy mission, you will
    automatically fail any spy mission against that same civilization for
    next 2-4 turns."
  - The study (C3C) differs: a failed Plant Spy sets a flag that clears with a
    1/3 chance each turn, and other failures do not set it (see Disputed).
  - MAS (Steal Technology thread), quote: "its more difficult to succeed a
    second time, as your opponent is weary."
  - The flag is per civ and invisible, and the 1/3 chance is the same every
    turn. Oystein (CFC 97042 page 2, Sep 2004): "The probability that the flag
    will disappear is 1/3, no matter how many turns has past." Oystein (page 3,
    Nov 2005): a failed sabotage does not set the flag. Asked whether the AI is
    flagged too: "there is no difference between human and AI" (page 1).
  - An earlier vanilla report says a failed mission makes the rest of that
    turn's attempts fail. player1 (Apolyton "Cost of spying",
    https://codehappy.net/apolyton/threads/60084-1.htm, Aug 2002); Jaybe
    disagreed, and Catt had succeeded after a failure only against another
    target. Anecdotal.
- **A caught spy can lead to war.** Source: CivFanatics FAQ. Quote: "If it is
  not successful, the AI will become mad at you and might declare war." The
  attitude cost is in 6.7.
- **Propaganda** (study, summarised). Your spy is never caught. Each
  citizen's chance of being persuaded is:

  spy bonus (veteran 10) + government modifier + culture factor
  - 5 x garrison - courthouse (20) - capital (40) + 20 for your own
  nationals

  The city flips when at least 3/4 of its citizens are persuaded. Otherwise
  the persuaded citizens become "confused" for some turns.
  - **Democracy is immune.** `conquests.biq` GOVT Democracy has ImmuneTo=6,
    the Initiate Propaganda mission. `script.txt`, quote: "The $CIVADJ1
    people are immune to propaganda."
  - **Government modifiers** (`conquests.biq` GOVT_GOVT BriberyModifier;
    rows are the sender's government, columns the target's, in the order
    Anarchy, Despotism, Monarchy, Communism, Republic, Democracy, Fascism,
    Feudalism):
    - Despotism 15/0/-10/-15/-20/-30/0/0
    - Monarchy 20/15/0/-5/-10/-25/0/0
    - Communism 25/20/5/0/-10/-20/0/0
    - Republic 30/25/10/8/0/-5/0/0
    - Democracy 35/30/20/10/5/0/0/0
    - Fascism 0/0/-5/-20/-10/-10/0/0
    - Feudalism 20/15/0/-10/-10/-20/-25/0
    - Anarchy 0 across the row.
  - **Culture factors** (`conquests.biq` CULT "chance of successful
    propaganda"): in awe of 30, admirers of 25, impressed with 20,
    unimpressed by 10, dismissive of 5, disdainful of 3.
  - **Courthouse.** Conquests Civilopedia, quote: "The effectiveness of enemy
    spy propaganda missions against the city is reduced."
- **Steal Technology is blocked against civs you are at war with.** Source:
  rysingsun in the "Steal Technology" thread, and the Apolyton thread title
  "espionage: you can't steal techs from civs you are at war with". Combined
  with the war closing the embassy, this follows from 6.2. Jaybe (Apolyton
  "Espionage--worthless?", https://codehappy.net/apolyton/threads/107183-1.htm,
  Jan 2004): an Ambassador "canNOT" investigate a city while at war.
- **A planted spy can steal tech during a war (informal test).** Spoonwood
  (CFC 97042 page 4,
  https://forums.civfanatics.com/threads/a-study-of-espionage-mission.97042/page-4,
  Apr 2017): "if you have a spy planted, then you can attempt to steal tech
  during a war." Without a planted spy he could not. The manual (p.~168)
  agrees in spirit: "Once your Spy is in place, the flow of information is not
  interrupted during a war". (Former open question 9.)
- **Steal Plans lasts the rest of the turn.** Manual p.~168: "For the
  remainder of the turn, you know the positions of all of that rival's
  troops." C3X-INI (`steal_plans_duration`, default 1): "Sets the number of
  turns for which a successful "Steal Plans" espionage mission reveals the
  target civ's units."
- **Sabotage: half or all of the shields?** The manual (p.~168) says "They'll
  destroy half of the shields already accumulated for the current project."
  The C3C Civilopedia says "destroy all the $LINK<shields=GCON_Shields> in the
  production box" (see Disputed).
- **Propaganda is easier against a city in disorder (manual).** Manual
  p.~168: "it is easier to push a city already in civil disorder into open
  revolt than it is to undermine a contented city." Not in the study's
  formula. Oystein (CFC 97042 page 2, Sep 2004): "If the flip chance of a size
  1 city if 30%, then the flip chance of a size 20 city is about 0.004%".
- **When the espionage screen opens.** The vanilla `README.txt` (manual
  errata, 1.0): "The Espionage button becomes enabled after researching the
  Espionage advance." The later manual (p.~169) and the C3C Civilopedia
  `GCON_Espionage` both say the screen is available after Writing ("This
  screen is available for use once a player has discovered
  $LINK<Writing=TECH_Writing>"). The screen arrived with PTW (Apolyton "UGO
  Preview Part 2 - ESPIONAGE Screen", https://codehappy.net/apolyton/threads/63254-1.htm).
  So: vanilla button at Espionage; PTW and C3C screen at Writing.
- **How a mission is rolled (code reading).** Flintlock (C3X-THREAD
  post-16571523, page-91 #1812): "The game computes some sort of
  base chance ahead of time when a spy mission is configured", and "the
  game doesn't account for city improvements or government until the very
  end." (said of the propaganda mission). For stealing a tech the base chance is (paraphrase from a summarised fetch) "the chance not to
  fail completely": losing that roll means the theft fails and the spy is
  caught, and "even if you pass that roll there's another roll to
  determine if you fail to steal the tech but the spy gets away." That is
  the study's two stages (escape chance, then 80% success), in the other
  order of description.
- **Every mission is aimed at a city (code reading).** Flintlock
  (post-16878439, page-156 #3119): "Even the code for the espionage
  screen itself is all written assuming that every mission requires a target
  city." No poison-water mission exists in C3C (post-16848926, page-147
  #2930): "Sadly, I don't see any evidence of this mission in the
  Conquests EXE." vmxa (post-16876177, page-156 #3105), on the capital as the
  target: "No palace, no spy."
- **Steal Plans also shows invisible units** (play report). MARK V
  (C3X-THREAD post-16938325, page-177 #3539): (paraphrase from a summarised fetch) "invisible units
  become visible as a result of an espionage mission to steal plans", and a
  failed mission "almost always" brings a declaration of war.
- **The AI uses espionage too.** jabroni154 (Apolyton "Espionage and the AI in
  1.21", https://codehappy.net/apolyton/threads/49422-1.htm, May 2002) saw an
  AI propaganda attempt and spy plants. TimBentley (CFC 97042 page 4, Apr
  2006): "They use propaganda and sabotage production, for sure." Manual
  p.~157: "Be aware that enemies can use all the same techniques against your
  civilization as you use against theirs."
- **How often the AI spies (binary finding).** Perez's decompile
  (`doc/perez-findings/Civ3_Findings.md`, Round 2d, `AI_ConsiderEspionage`;
  authoritative per the owner's 2026-11-12 ruling): each turn, per civ,
  `Rand(range)` with range 200000, 20000, 2000, 200, 20 for relationship
  level 0..4, halved with an embassy (or "a positive counter"); 0-1 steal
  tech, 2 steal map, 3 steal plans, 4-5 propaganda, 6-7 sabotage, 8-9 expose
  mole; only allowed missions, and never one the target's government is
  immune to. The decompile does not decode which relationship gives which
  level. Engine reading (HEURISTIC): the AI's attitude, Gracious 0 to Furious
  4, so a furious AI with an embassy tries a mission every turn and a
  cautious one about 1 turn in 100.

## 6.12 United Nations (brief)

- **Periodic majority votes.** Source: Conquests Civilopedia (Victory,
  Diplomacy). Quote: "After the $LINK<United Nations=BLDG_United_Nations> is
  built, there are periodic votes by the council to elect a Leader. To be
  elected, a candidate must win a majority of all votes."
- **Candidates.** Quote: "There are always two, and sometimes three,
  candidates." A civ qualifies if it "have built the UN", or controls "at
  least 25% of the world's territory", or "at least 25% of the world's
  population". If only one civ is eligible, "the second candidate is the
  civilization with the largest population."
  - **Binary finding: more than 25%.** Perez's decompile of the Conquests
    executable (`doc/perez-findings/Civ3_Findings.md`, Round 2 "Victory
    conditions"): candidates "own a United-Nations-class wonder ... or more
    than 25% of land or population". The owner ruled on 2026-11-12 that this
    finding is authoritative. *Superseded:* the Civilopedia's "at least 25%".
- **The UN owner decides whether to hold the vote, every 10 turns.** Binary
  finding (same source and ruling): "vote every 10 turns". *Superseded:* the
  CivFanatics FAQ's "comes around every 11 turns", and Turner's "every 11
  turns" below. Also
  `script.txt` `DIPLOVICTORYVOTEOPTION`: "Would you like to hold elections for
  UN Secretary-General?" and "Election was inconclusive; no majority was
  found."
- **One vote per civ; a majority of all civs in the game.** CFC FAQ: "Each
  civilization gets 1 vote, and can vote for one of the candidates." Manual
  p.~175: "you must be elected Secretary-General by a vote of the majority of
  all the civilizations in the world". TimBentley (CFC "the road to Diplomatic
  Victory", https://forums.civfanatics.com/threads/the-road-to-diplomatic-victory.40013/page-2,
  Sep 2005), asked whether the majority is of all civs or of voters: "All civs
  in the game." Candidates vote for themselves (eae, CFC 19457, Dec 2003: "my
  rival voted for himself").
- **Only the UN owner can call the vote.** tao (CFC "Diplomatic victory
  question", https://forums.civfanatics.com/threads/diplomatic-victory-question.112336,
  C3C/PTW, Feb 2005): "Only the owner of the UN can call the vote." Turner,
  same thread: "Actually, it's every 11 turns." Hyronymus: "the non-owners never
  receive a message if the AI owner decided not to run an election."
- **An AI must be Polite or better to vote for you.** Source: AI Attitude
  Study (summarised). SirPleb (CFC 44999 page 5, Mar 31 2003, quoted again on
  page 8): "To get someone's vote at the UN it is necessary to have them
  "polite"." Barely polite (-1) is enough; at 0 (cautious) or worse the AI
  abstains or votes for another candidate. Bamspeedy (page 6): "donating over
  100 gold the turn before the UN vote would be a waste" (the gift cap is -10).
- **TMIT's Guide to UN Victories is Civ4.** The often-cited CFC thread 304815
  (60% of world population, "friendly" voters) is a Civ4 guide, so it is not
  used here.

## 6.13 How attitude and treaties feed AI war plotting (code-level, brief)

The full AI war logic belongs to section 8. This is the part that depends on
diplomacy. Source for every bullet: Flintlock (CFC, "All About AI Aggression
Levels and War Plotting",
https://forums.civfanatics.com/threads/all-about-ai-aggression-levels-and-war-plotting.703785/,
C3C 1.22, Jun-Jul 2026), a pseudocode reconstruction from the executable. His
post #8 corrects the exclusion roll, and the text below uses the corrected
reading.

- **Each turn the AI may start plotting a war.** Quote: "Each AI player has a
  chance at the start of each turn to begin plotting war." The rolls include
  "Roll one in (12 + -2 * EffectiveAggressionLevel)", "If already at war, roll
  one in 16", "If in anarchy, roll one in 8", and "If in a government that
  experiences war weariness, roll one in 2".
- **MPP partners are never targets.** Quote: "Other players with whom the AI
  has a mutual protection pact are excluded". He also believes recent peace
  protects: "I believe this blocks war plotting within 8 turns of signing
  peace." Fergei (page 1, Jul 2026): "The AI will declare war on you, even if
  you have an MPP, if the MPP is agreed AFTER" the plotting began.
- **Peace, common enemies and alliances make exclusion likelier.** The roll is
  "rolling **not** one in X", with "X = 1 to start", "X += 2 if the AI has a
  peace treaty with the target", "X += 2 for each common enemy the AI and the
  target have" and "X += 2 for each military alliance the AI and the target
  have". "The AI will skip this check if the target is a human player who is
  ranked first in power."
- **Attitude weights the target score.** Quote: "DiploEffect = 8 if furious
  toward the target, 4 if annoyed, 2 if cautious, 1 if polite, and 0 if
  gracious." Other diplomatic terms: "If the AI has a resource trade with the
  target, X = 3 * X // 4", "For each common enemy that the AI and the target
  share, X = 2 * X // 3", "If the target has a peace treaty or mutual protection
  pact with any player, X = X // 2." and "X = X // (AllianceCount + 1)".
- **War is declared when the attack starts.** Flintlock (page 2, Jul 14 2026):
  "War is declared only when the attack actually happens." And: "the AI will
  not attack you with its HN units while it's gracious toward you."
- **How the AI's declarations happen in play.** Flintlock (C3X-THREAD
  post-16132702, page-16 #318): AI war declarations usually come from it
  "running its units into its target's cities". Alekseyev_
  (post-16987435, page-190 #3796): (paraphrase from a summarised fetch) "Basically, only surprise
  attacks as war declarations." Both fit "War is declared only when the
  attack actually happens" above.
- **Barbarians: no diplomacy, always enemies (code reading).** Flintlock
  (C3X-THREAD post-16980354, page-188 #3741): "the unit AI considers
  units owned by the barbs to be enemies unconditionally" and
  "Diplomacy with the barbarians is not possible even if they own
  cities."
- **Shared continent means a city on it.** Flintlock (page 2): "Two players are
  considered to share a continent if they both have at least one city on it."

---

## Disputed

- **Right of Passage tech.** Vanilla Civilopedia says Writing ("Can now
  negotiate military alliance and right of passage"). Conquests BIQ and
  Civilopedia say Map Making. The C3C `GCON_Embassies` text still says Writing
  ("That done, you can sign Right of Passage agreements and Military
  Alliances"), a leftover from vanilla. **Map trading:** Map Making in vanilla,
  Navigation in C3C. **Owner ruling (engine): ROP needs Writing plus an
  embassy.** Its sources are listed in 6.3 (C3C `GCON_Embassies`, vanilla
  `TECH_Writing`, the manual's "if you have an embassy", Aeson on Apolyton
  46862). *Code finding next to the ruling (third round, for the lead):* the
  1.22 executable has no Writing rule for this. The tech is a rule-file flag,
  `ATF_Enables_Right_Of_Passage_Treaties = 0x200` in the tech-flag enum of
  C3X's header for the 1.22 exe
  (https://github.com/maxpetul/C3X/blob/master/Civ3Conquests.h), alongside
  `ATF_Enables_Military_Alliances`, `ATF_Enables_Mutual_Protection_Pacts`,
  `ATF_Enables_Trade_Embargos`, `ATF_Enables_Map_Trading` and
  `ATF_Enables_Communication_Trading`. In `conquests.biq` that flag is on Map
  Making (6.3). So stock C3C ties ROP to Map Making, as the BIQ and C3C
  Civilopedia say. The header gives the flag names only; it does not show
  the embassy check.
- **Does attitude change trade prices?** TheNiceOne (1.29) found that
  attitudes below Polite give no extra markup ("no AI will demand more if
  falling below catious"). Zwingli (1.29) measured +0.32% per attitude point
  below Cautious, and Furious cost about 38% more than Gracious when combined
  with demands. Catt (Apolyton 53506, Jun 2002, "a very limited test"): "AI
  attitude towards me had absolutely no affect on trade prices." Bamspeedy (CFC
  44999 page 2): "I did not test how attitudes affect what civs would pay (or
  charge you) for things." The engine follows TheNiceOne's table (owner
  ruling).
- **Spy failure lockout.** The PTW readme says any spy mission fails
  automatically for 2-4 turns after a failure. The espionage study (C3C) says
  only a failed Plant Spy sets a flag, cleared with a 1/3 chance per turn, and
  that a failed sabotage sets no flag. A 1/3 chance per turn gives an expected
  3 turns, which is close to the readme's "2-4", so the two may describe the
  same flag with the readme over-generalising "any spy mission". Not tested
  against the readme. Jaybe (Apolyton "Spies: Useless or Indispensible?",
  https://codehappy.net/apolyton/threads/81355-1.htm, 23 Mar 2003) dates the
  lockout to PTW: "Back in 1.16 I am pretty sure you could try every turn." and
  "On the current Windoze version (PTW??) if you fail then it won't do any good
  to try again for 3-4 turns." Neither post separates a plant-only flag from an
  all-missions lockout.
- **Sabotage Production: all or half the shields.** Manual p.~168: "They'll
  destroy half of the shields already accumulated for the current project."
  Conquests Civilopedia `GCON_Espionage`: "destroy all the
  $LINK<shields=GCON_Shields> in the production box of a rival city". The
  manual's "Losing Improvements" note (p.~143) says sabotage "might scrap the
  item that the city is currently producing or destroy half the shields". One
  vanilla report (DISX, Apolyton "Sabotage Production doesn't work?",
  https://codehappy.net/apolyton/threads/33523-1.htm, Nov 2001): "SABOTAGE
  SUCCESSFUL it tells me, Japanese has to start the production over." No
  controlled test found. For C3C, follow the Civilopedia.
- **Carefully: x0.5 or x1.5 cost.** The War Academy page reads "multiply by 50%
  for carefully", but the forum original says "3/2", and Spoonwood measured
  x1.5 (6.11). Resolved in favour of x1.5; kept here because the War Academy
  page is the commonly cited copy.
- **Shunned government: +4 or +5 attitude.** The study measured "+4 Shunned
  government". Flintlock's code reading (CFC 703785, 2026) gives "if the other
  player is in the AI's shunned government, it adds 5 points." The study's
  numbers are measured at PTW 1.01; the code reading is C3C 1.22.
- **AI reputation.** Players disagree on whether AIs suffer reputation for
  their own treaty breaks. Myartar, quote: "There seems to be NO penelty to
  the AI on reputaion". ahman (CFC 113376 page 3, Dec 2010): "Maybe they don't
  even care about reputation between them?" (unanswered). The manual (p.~156)
  only says "If you habitually break treaties, other leaders will have no
  qualms about doing the same to you."
- **Does breaking a peace treaty hurt reputation?** microbe ("Trading
  Reputation", C3C) says "there is no such a thing" as peace-treaty
  reputation, and that breaking a fresh treaty leaves reputation "intact" unless
  it breaks a GPT deal. Smart (CFC 113376 page 2, Dec 2005): "You cant breake
  peace treaty without a rep hit." nullspace (same page, C3C 1.22 test of Vol's
  save): declaring war "breaks a 20-turn peace treaty, and breaks no other
  deals", yet India's reputation was ruined afterwards. tmn (page 3, 2017):
  "Peace treaty, Right of Passage and Trade Embargo are all rep-risky." The
  attitude study separately gives "+4 for breaking a peace treaty with that
  civ" as an attitude (not reputation) effect.
- **Does reputation ever recover?** The CFC FAQ and microbe say never for GPT
  deals ("will never again for the game accept per-turn payments"). The manual
  (p.~156) says black marks "can be partially erased" over "long periods of
  time". Vol's tests (6.6) show a workaround through contact, not time. No
  timed test was found.
- **Units removed from territory: where to?** The Strategy Compilation says
  "nearest non-offending spot"; vondrack (Apolyton 55198, 1.21f) says toward
  "their original destination" (6.10).
- ~~**Can cities be traded outside a peace deal?**~~ *Resolved (6.4,
  C3X-THREAD):* the AI refuses city transfers outside peace talks; Flintlock
  says the developers restricted it because the AI could not value cities.
  The FAQ's "peace deal only" is right for deals with the AI. Kept for the
  record: The CFC FAQ lists "Cities (as
  part of a peace deal only)". The manual (p.~165) lists cities as an ordinary
  trade item, Bamspeedy (CFC 44999 page 6, PTW 1.21) treats "Donations of
  cities" as gifts, and the Strategy Compilation (vanilla) says "AI trades big
  for cities". The FAQ may mean that the AI only gives up cities to end a war.
  Random Passerby (Apolyton "Trading Absurdity",
  https://codehappy.net/apolyton/threads/49585-1.htm, May 2002, unsourced):
  "the Firaxis team made it so that the AI will only even consider giving
  cities in exchange for peace treaties."
- **UN: second candidate when only one civ qualifies.** Conquests Civilopedia:
  "the second candidate is the civilization with the largest population." The
  CFC FAQ (summarised by the fetch tool) says the highest-scoring civ. EMan (CFC
  40013 page 2, PTW, Nov 2005): "If neither 1 or 2 applies, Civ with highest %
  of World's Population." Follow the Civilopedia.
- **AI-AI trade rate scope.** Soren (via WarpStorm, Apolyton 68856): "the AI to
  AI trade rate is deficit spending only." EDHELP describes a plain multiplier
  on the initiating civ's offer. See 6.8.
- **Submarine collisions.** Vanilla `README.txt` (Fixes v1.16f): "You no
  longer declare war by accidentally passing over a hidden submarine." and "The
  AI no longer reacts to submarines that it cannot see." C3X-INI (C3C 1.22,
  "SUBMARINE BUG"): "After colliding with an invisible unit, the AI initiates
  combat, declaring war if necessary." So in 1.22 an AI that bumps into your
  hidden unit can still start a war. Flintlock (C3X-THREAD post-16055720,
  page-5 #91) lists it among the stock bugs he fixed: "AI
  pathfinding collides with invisible units (called the "submarine bug")".
  Alekseyev_ (post-16310981, page-44 #876): "I think the submarine
  bug still exists on land." It is a bug; the intended rule (vanilla 1.16f)
  is that bumping into an unseen unit starts no war.

## UNVERIFIED / open questions

Resolved this pass (now in the main text): first contact trigger (6.1,
manual), ceasefire (6.5, none in Civ3), MPP before Nationalism (6.3),
embassy attitude sign (6.7), embassy/alliance gate (6.3), units teleported
when told to leave (6.10, destination disputed), planted spy stealing during
war (6.11, informal test).

1. **Exact tech-trade valuation function in Civ3.** Known: one function for
   AI-AI and AI-human trades, value falls as more civs have the tech (Soren),
   and the AI-human markup table (TheNiceOne). Soren (Apolyton 42905, 6.8)
   adds that only civs known to the buyer count, that the buyer's own progress
   lowers the value, and that AIs sell to each other at half value at worst.
   Unknown: the exact scaling by known civs and by civs with the tech, and the
   wonder term (Ironikinit, 42905 page 2, Feb 2002, observed only: "The AI does
   put greater value on techs that yield a wonder, like music theory and
   astronomy.").
2. **The AI's decision to give in to your demands.** The AI's own demand
   gates are now known from code (6.9: power place, attitude and aggression
   rolls, war-damage balance, 32-turn cooldown). Whether it gives in is the
   stock `Leader_would_yield_to_demand`, whose terms are not published. Players
   tie giving in to military superiority near their cities.
3. **When the AI agrees to or refuses ROP, MPP, MA and embargo**
   (`AINORIGHTOFPASSAGE`: "Our bond is not sufficient to allow trust of this
   magnitude.") Known: ROP is priced by territory size (Bamspeedy), the MA
   evaluation divides by the distance to the target (C3X-INI), and the AI
   refuses to embargo a civ it trades resources with (Spoonwood). The attitude
   thresholds are unknown.
4. **Exact decay of temporary attitude effects** other than gifts (10
   gold/turn) and war damage (non-linear, SirPleb's samples in 6.7).
5. **Timed reputation recovery** (see Disputed).
6. **Terms of an AI-to-human loan** (`AILOANOFFER`). Lending to the AI is about
   18 gold per 1 gpt (6.8).
7. **Pricing of cities and strategic resources.** Maps, ROP, luxuries,
   contacts and early workers have player-tested rules (6.8). Cities and
   strategic resources do not. Observed prices only: Hurricane (Apolyton
   49585 page 2, 1.21, May 2002): "Here is an example that the AI indeed will
   pay nice amounts of cash for other resources than Iron" (a screenshot of
   5200 gold for Oil); player1 (page 1): "Iron lacking AI pays a lot of money
   for it."
8. **AI "won't trade techs with someone at war with its friend"** or "won't
   trade while you're in a war you started". No Civ3 source was found. Such
   rules are attested for Civ4 only (for example CFC 217576, a Civ4 thread).
9. **When the "remove your forces or declare war" option appears**, and the
   exact rule for where removed units go. (C3X-THREAD play reports, 6.10: the
   demand comes on the second turn, and only for units the AI can see.)
10. **Whether the MPP fires on the declaration or the first attack.**
11. **Embassy-granting wonder** (Marco Polo's Embassy): its presence and
    effect per version were not checked here. It belongs to the wonders
    section.
12. **Whether signing peace itself moves units out of the other's territory.**
    The engine does (owner's ruling, 6.10). Civ3 sources only describe units
    teleported out when told to leave; none was found for the signing. A test
    in Civ3 (make peace with units inside the AI's borders) would settle it.
13. **Founding a city inside another civ's borders.** The engine follows the
    community reports (6.10): allowed; at peace the Foreign Advisor asks
    (`DECLARE_WAR`) and founding declares war; the AI never does it. Two
    reports say it is impossible; Zanzin (C3C) saw the option missing once,
    possibly for another reason (spacing, terrain). Unverified in the game.
