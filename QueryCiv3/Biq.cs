using System;
using System.IO;
using QueryCiv3.Biq;

namespace QueryCiv3 {
	public class BiqData {
		public Civ3File FileData;
		public bool HasCustomRules => FileData.SectionExists("BLDG");
		public bool HasCustomMap => FileData.SectionExists("WCHR");

		// RULE, WCHR, WMAP, and GAME all seem to only ever have a maximum of 1 header in the biq files, but I'm not certain on that
		// If that's confirmed to be the case, they can be demoted from arrays to singular variables and the code simplified accordingly
		public BLDG[] Bldg;
		public CITY[] City;
		public CLNY[] Clny;
		public CONT[] Cont;
		public CTZN[] Ctzn;
		public CULT[] Cult;
		public DIFF[] Diff;
		public ERAS[] Eras;
		public ESPN[] Espn;
		public EXPR[] Expr;
		public FLAV[] Flav;
		public GAME[] Game;
		public GOOD[] Good;
		public GOVT[] Govt;
		public LEAD[] Lead;
		public PRTO[] Prto;
		public RACE[] Race;
		public RULE[] Rule;
		public SLOC[] Sloc;
		public TECH[] Tech;
		public TERR[] Terr;
		public TFRM[] Tfrm;
		public TILE[] Tile;
		public UNIT[] Unit;
		public WCHR[] Wchr;
		public WMAP[] Wmap;
		public WSIZ[] Wsiz;

		/*
			Note which of the following are rectangular 2d arrays [,] vs which are jagged 2d arrays [][]
			A rectangular array means that the second dimension for all components is the same eg. for RaceEra, every civ shares the same # of Eras
			A jagged array means that the second dimension can vary between components eg. for RaceCityName, each civ can have a different # of Cities
		*/
		public bool[,] TerrGood; // which resources are allowed on which types of terrain
		public GOVT_GOVT[,] GovtGovt; // relationships between governments
		public RACE_City[][] RaceCityName; // the list of city names for each civ
		public RACE_ERAS[,] RaceEra; // the file names for each era for each civ
		public RACE_LeaderName[][] RaceGreatLeaderName; // the great leaders for each civ
		public RACE_LeaderName[][] RaceScientificLeaderName; // the scientific leaders for each civ
		public int[][] CityBuilding; // Building IDs in each city
		public int[][] WmapResource;
		public int[][] PrtoPrto; // Stealth unit targets per unit
		public LEAD_Unit[][] LeadPrto; // starting unit data for each leader
		public int[][] LeadTech; // starting tech data for each leader
		public RULE_CULT[][] RuleCult; // culture level names per rule
		public int[][] RuleSpaceship; // spaceship quantity requirements per rule
		public int[][] GameCiv; // Playable civs for game
		public int[][] GameAlliance; // Civ alliances for game

		private const int SECTION_HEADERS_START = 736;
		// Dynamic sections need to have their static subcomponents read in as discrete chunks, which these length constants help with
		// The sum of the LEN constants for each section equals the total size of that section's struct
		// eg. GOVT_LEN_1 + GOVT_LEN_2 == sizeof(GOVT)
		// This invariant is enforced by the BiqSectionSizeTests test in EngineTests; a mismatch means a
		// section's Buffer.MemoryCopy writes past the end of its struct's backing memory.
		internal const int GOVT_LEN_1 =  400;
		internal const int GOVT_LEN_2 =   76;
		internal const int TERR_LEN_1 =    8;
		internal const int TERR_LEN_2 =  225;
		internal const int RACE_LEN_1 =    8;
		internal const int RACE_LEN_2 =    4;
		internal const int RACE_LEN_3 =  208;
		internal const int RACE_LEN_4 =   92;
		internal const int CITY_LEN_1 =   38;
		internal const int CITY_LEN_2 =   32;
		internal const int WMAP_LEN_1 =    8;
		internal const int WMAP_LEN_2 =  164;
		internal const int PRTO_LEN_1 =  238;
		internal const int PRTO_LEN_2 =   21;
		internal const int LEAD_LEN_1 =   56;
		internal const int LEAD_LEN_2 =    8;
		internal const int LEAD_LEN_3 =   33;
		internal const int RULE_LEN_1 =  104;
		internal const int RULE_LEN_2 =  164;
		internal const int RULE_LEN_3 =   32;
		internal const int GAME_LEN_1 =   16;
		internal const int GAME_LEN_2 = 5304;
		internal const int GAME_LEN_3 = 2017;

		public string Title;
		public string Description;

		public static unsafe BiqData LoadFile(string biqFilePath) {
			// BiqData only reads the bytes, so it can share the cached copy of a decompressed BIQ instead of cloning it
			byte[] biqBytes = Util.ReadFileShared(biqFilePath);
			return new BiqData(biqBytes);
		}

		public unsafe BiqData(byte[] biqBytes) {
			Load(biqBytes);
		}

		public unsafe BiqData(byte[] data, int start, int length) {
			Load(data, start, length);
		}

		public unsafe void Load(byte[] biqBytes) {
			ArgumentNullException.ThrowIfNull(biqBytes);
			Load(biqBytes, 0, biqBytes.Length);
		}

		// The minimum BIQ major version this reader understands. Earlier versions (vanilla "BIC " files and
		// Play the World "BICX" files before version 12) have shorter records: compared with conquests.biq word by
		// word, Play the World's civ3X.bix and vanilla's civ3mod.bic records are the start of Conquests' (where a
		// record's parts depend on a count, as GOVT's table of governments, the counts place them), so the reader takes
		// them as Conquests records with the newer fields zeroed. Their tiles keep terrain, overlays and bonuses only
		// in the older fields (see TILE.FillFromLegacy). Vanilla files before version 2 aren't read.
		public const int MIN_SUPPORTED_MAJOR_VERSION = 12;
		public const int MIN_LEGACY_MAJOR_VERSION = 2;
		public const int LEGACY_TERRAIN_COUNT = 12;

		// Whether the file is from Civ3 or Play the World (a .bic or .bix) rather than Conquests.
		public bool IsLegacy { get; private set; }

		/// <summary>
		/// Throws a <see cref="NotSupportedException"/> unless the file is a Conquests BIQ (a BICX or BICQ file with
		/// major version 12 or later), the only layout this reader supports.
		/// </summary>
		public static void EnsureSupportedVersion(Civ3File file) {
			ArgumentNullException.ThrowIfNull(file);
			Civ3Version version = file.Civ3Version;
			string type = version.FileTypeName?.Trim() ?? "";
			if (!file.IsBicFile || (type != "BICX" && type != "BICQ" && type != "BIC")) {
				throw new NotSupportedException($"Unsupported BIQ file: its type is '{type}', but only Civ3 rules and scenario files (BIC, BICX or BICQ) are supported.");
			}
			if (version.MajorVersion < MIN_LEGACY_MAJOR_VERSION) {
				throw new NotSupportedException($"Unsupported BIQ file: {type} version {version.MajorVersion}.{version.MinorVersion} is older than this reader understands (version {MIN_LEGACY_MAJOR_VERSION} or later).");
			}
		}

		// Load BIQ data stored at data[start .. start + length), e.g. the BIQ sections embedded in a SAV file, without copying it out.
		// Throws NotSupportedException for anything but a Conquests BIQ, and InvalidDataException for malformed data: every read
		// is checked against the end of the data, and against the length of the record it is part of.
		public unsafe void Load(byte[] data, int start, int length) {
			try {
				LoadRecords(data, start, length);
				if (IsLegacy && Terr != null && Terr.Length == LEGACY_TERRAIN_COUNT) {
					// Civ3's and Play the World's 12 terrains lack Conquests' Marsh and Volcano (between Jungle and
					// Coast); the importer reads terrain by its place in Conquests' list, so their own terrain list
					// gives way to the default rules' (its changes are lost) and their tiles are renumbered
					// (TILE.FillFromLegacy).
					Terr = null;
					TerrGood = null;
				}
			} finally {
				readingLegacy = false;
			}
		}

		private unsafe void LoadRecords(byte[] data, int start, int length) {
			ArgumentNullException.ThrowIfNull(data);
			FileData = new Civ3File(data, start, length);
			EnsureSupportedVersion(FileData);
			IsLegacy = FileData.Civ3Version.MajorVersion < MIN_SUPPORTED_MAJOR_VERSION;
			readingLegacy = IsLegacy;
			if (length < SECTION_HEADERS_START) {
				throw new InvalidDataException($"Malformed BIQ file: it is {length} bytes long, too short for its {SECTION_HEADERS_START} byte header.");
			}
			Description = FileData.GetString(32, 640);
			Title = FileData.GetString(672, 64);

			fixed (byte* basePtr = data) {
				byte* bytePtr = basePtr + start;
				// For now, we're skipping over the VER# and BIQ file header information to get right to the structs
				// The first section is likely to be BLDG in BIQ files, but the current approach supports any ordering of the sections
				long offset = SECTION_HEADERS_START;

				while (offset < length) { // Don't read past the end
										  // We don't know what orders the headers come in or which headers will be set, so get the next header and switch off it:
					CheckRange(offset, 8, length, "header");
					// Every header is exactly 4 chars long, so like in SavData it can be switched on as a 32-bit integer
					int header = *(int*)(bytePtr + offset);
					int count = *(int*)(bytePtr + offset + 4);
					string section = FileData.GetString((int)offset, 4);
					if (count < 0) {
						throw Malformed(section, $"negative record count {count}");
					}
					offset += 8;

					// Section data structures are stored in the BiqSections/ folder
					// We can divide the BIQ sections into two types: static and dynamic
					// Static have a fixed length always, which means they can be read directly memory-copied into our structs with no special logic
					//   The static sections are: BLDG, CTZN, CULT, DIFF, ERAS, ESPN, EXPR, FLAV(??), GOOD, TECH, TFRM, WSIZ, WCHR, TILE, CONT, SLOC, UNIT, CLNY
					// Dynamic sections have at least one component with varying length, and so require multiple structs and special logic
					//   The dynamic sections are: GOVT, RULE, PRTO, RACE, TERR, WMAP, CITY, GAME, LEAD
					// Every record (except FLAV's) starts with its length, not counting the length value itself, and the next record
					// always starts after that length. A record shorter than our struct leaves the rest of the struct zeroed, and the
					// extra bytes of a longer one are skipped.
					switch (header) {
						case 0x47444c42: // BLDG
							Bldg = ReadStaticSection<BLDG>(bytePtr, ref offset, length, count, section);
							break;
						case 0x59544943: // CITY
							City = new CITY[CheckCount(count, 4, offset, length, section)];
							CityBuilding = new int[count][];

							fixed (CITY* ptr = City) {
								for (int i = 0; i < count; i++) {
									byte* cityPtr = (byte*)(ptr + i);
									long recordEnd = BeginRecord(bytePtr, offset, length, section);
									CopyChunk(bytePtr, ref offset, recordEnd, cityPtr, CITY_LEN_1, section);
									CityBuilding[i] = ReadArray<int>(bytePtr, ref offset, recordEnd, City[i].NumberOfBuildings, section);
									CopyTail(bytePtr, ref offset, recordEnd, cityPtr + CITY_LEN_1, CITY_LEN_2);
									offset = recordEnd;
								}
							}
							break;
						case 0x594e4c43: // CLNY
							Clny = ReadStaticSection<CLNY>(bytePtr, ref offset, length, count, section);
							break;
						case 0x544e4f43: // CONT
							Cont = ReadStaticSection<CONT>(bytePtr, ref offset, length, count, section);
							break;
						case 0x4e5a5443: // CTZN
							Ctzn = ReadStaticSection<CTZN>(bytePtr, ref offset, length, count, section);
							break;
						case 0x544c5543: // CULT
							Cult = ReadStaticSection<CULT>(bytePtr, ref offset, length, count, section);
							break;
						case 0x46464944: // DIFF
							Diff = ReadStaticSection<DIFF>(bytePtr, ref offset, length, count, section);
							break;
						case 0x53415245: // ERAS
							Eras = ReadStaticSection<ERAS>(bytePtr, ref offset, length, count, section);
							break;
						case 0x4e505345: // ESPN
							Espn = ReadStaticSection<ESPN>(bytePtr, ref offset, length, count, section);
							break;
						case 0x52505845: // EXPR
							Expr = ReadStaticSection<EXPR>(bytePtr, ref offset, length, count, section);
							break;
						case 0x56414c46: // FLAV
										 // FLAV has two oddities compared with other sections:
										 // 1. FLAV is the only section which is divided into section groups. The number of section groups (the
										 //   header's count) is always 1, and is followed by the number of flavors in the group
							if (count != 1) {
								throw new NotSupportedException($"Unsupported BIQ file: its FLAV section has {count} groups of flavors, but only 1 is supported.");
							}
							CheckRange(offset, 4, length, section);
							int flavorCount = *(int*)(bytePtr + offset);
							offset += 4;
							// 2. FLAV records have no length. Each is an unknown 4 byte value, the 256 byte name, the number of flavors,
							//   and that many relationship values. The number of flavors is practically always 7 (see FLAV.cs)
							Flav = new FLAV[CheckCount(flavorCount, FLAV_FIXED_LEN, offset, length, section)];
							fixed (FLAV* ptr = Flav) {
								for (int i = 0; i < flavorCount; i++) {
									CheckRange(offset, FLAV_FIXED_LEN, length, section);
									int relationships = *(int*)(bytePtr + offset + FLAV_FIXED_LEN - 4);
									if (relationships < 0) {
										throw Malformed(section, $"negative number of flavor relationships {relationships}");
									}
									long recordLength = FLAV_FIXED_LEN + 4L * relationships;
									CheckRange(offset, recordLength, length, section);
									Buffer.MemoryCopy(bytePtr + offset, ptr + i, sizeof(FLAV), Math.Min(recordLength, sizeof(FLAV)));
									offset += recordLength;
								}
							}
							break;
						case 0x454d4147: // GAME
							Game = new GAME[CheckCount(count, 4, offset, length, section)];
							GameCiv = new int[count][];
							GameAlliance = new int[count][];

							fixed (GAME* ptr = Game) {
								for (int i = 0; i < count; i++) {
									byte* gamePtr = (byte*)(ptr + i);
									long recordEnd = BeginRecord(bytePtr, offset, length, section);
									CopyChunk(bytePtr, ref offset, recordEnd, gamePtr, GAME_LEN_1, section);
									int playableCivs = Game[i].NumberOfPlayableCivs;
									GameCiv[i] = ReadArray<int>(bytePtr, ref offset, recordEnd, playableCivs, section);
									CopyChunk(bytePtr, ref offset, recordEnd, gamePtr + GAME_LEN_1, GAME_LEN_2, section);
									GameAlliance[i] = ReadArray<int>(bytePtr, ref offset, recordEnd, playableCivs, section);
									// The GAME records of Conquests BIQs before version 12.7 are 12 bytes shorter; the values they lack are left zeroed
									CopyTail(bytePtr, ref offset, recordEnd, gamePtr + GAME_LEN_1 + GAME_LEN_2, GAME_LEN_3);
									offset = recordEnd;
								}
							}
							break;
						case 0x444f4f47: // GOOD
							Good = ReadStaticSection<GOOD>(bytePtr, ref offset, length, count, section);
							break;
						case 0x54564f47: // GOVT
							Govt = new GOVT[CheckCount(count, 4, offset, length, section)];
							// Each GOVT record has a GOVT_GOVT relationship with every government
							CheckCount((long)count * count, sizeof(GOVT_GOVT), offset, length, section);
							GovtGovt = new GOVT_GOVT[count, count];
							int govtgovtRowLength = count * sizeof(GOVT_GOVT);

							fixed (GOVT* ptr = Govt) fixed (GOVT_GOVT* ptr2 = GovtGovt) {
								for (int i = 0; i < count; i++) {
									byte* govtPtr = (byte*)(ptr + i);
									long recordEnd = BeginRecord(bytePtr, offset, length, section);
									CopyChunk(bytePtr, ref offset, recordEnd, govtPtr, GOVT_LEN_1, section);
									CopyChunk(bytePtr, ref offset, recordEnd, (byte*)ptr2 + (long)i * govtgovtRowLength, govtgovtRowLength, section);
									CopyTail(bytePtr, ref offset, recordEnd, govtPtr + GOVT_LEN_1, GOVT_LEN_2);
									offset = recordEnd;
								}
							}
							break;
						case 0x4441454c: // LEAD
							Lead = new LEAD[CheckCount(count, 4, offset, length, section)];
							LeadPrto = new LEAD_Unit[count][];
							LeadTech = new int[count][];

							fixed (LEAD* ptr = Lead) {
								for (int i = 0; i < count; i++) {
									byte* leadPtr = (byte*)(ptr + i);
									long recordEnd = BeginRecord(bytePtr, offset, length, section);
									CopyChunk(bytePtr, ref offset, recordEnd, leadPtr, LEAD_LEN_1, section);
									LeadPrto[i] = ReadArray<LEAD_Unit>(bytePtr, ref offset, recordEnd, Lead[i].NumberOfStartUnitTypes, section);
									CopyChunk(bytePtr, ref offset, recordEnd, leadPtr + LEAD_LEN_1, LEAD_LEN_2, section);
									LeadTech[i] = ReadArray<int>(bytePtr, ref offset, recordEnd, Lead[i].NumberOfStartingTechnologies, section);
									CopyTail(bytePtr, ref offset, recordEnd, leadPtr + LEAD_LEN_1 + LEAD_LEN_2, LEAD_LEN_3);
									offset = recordEnd;
								}
							}
							break;
						case 0x4f545250: // PRTO
							Prto = new PRTO[CheckCount(count, 4, offset, length, section)];
							PrtoPrto = new int[count][];

							fixed (PRTO* ptr = Prto) {
								for (int i = 0; i < count; i++) {
									byte* prtoPtr = (byte*)(ptr + i);
									long recordEnd = BeginRecord(bytePtr, offset, length, section);
									CopyChunk(bytePtr, ref offset, recordEnd, prtoPtr, PRTO_LEN_1, section);
									PrtoPrto[i] = ReadArray<int>(bytePtr, ref offset, recordEnd, Prto[i].NumberOfStealthTargets, section);
									CopyTail(bytePtr, ref offset, recordEnd, prtoPtr + PRTO_LEN_1, PRTO_LEN_2);
									offset = recordEnd;
								}
							}
							break;
						case 0x45434152: // RACE
										 // For getting dynamic race data, we need to know the number of eras as defined earlier, so the ERAS section
										 // of a BIQ must appear before its RACE section
							if (Eras == null) {
								throw Malformed(section, "it comes before the ERAS section it depends on");
							}
							int eras = Eras.Length;
							Race = new RACE[CheckCount(count, 4, offset, length, section)];
							RaceCityName = new RACE_City[count][];
							RaceScientificLeaderName = new RACE_LeaderName[count][];
							RaceGreatLeaderName = new RACE_LeaderName[count][];
							CheckCount((long)count * eras, sizeof(RACE_ERAS), offset, length, section);
							RaceEra = new RACE_ERAS[count, eras];
							int raceeraRowLength = eras * sizeof(RACE_ERAS);

							fixed (RACE* ptr = Race) fixed (RACE_ERAS* ptr2 = RaceEra) {
								for (int i = 0; i < count; i++) {
									byte* racePtr = (byte*)(ptr + i);
									long recordEnd = BeginRecord(bytePtr, offset, length, section);
									CopyChunk(bytePtr, ref offset, recordEnd, racePtr, RACE_LEN_1, section);
									racePtr += RACE_LEN_1;
									RaceCityName[i] = ReadArray<RACE_City>(bytePtr, ref offset, recordEnd, Race[i].NumberOfCities, section);

									CopyChunk(bytePtr, ref offset, recordEnd, racePtr, RACE_LEN_2, section);
									racePtr += RACE_LEN_2;
									RaceGreatLeaderName[i] = ReadArray<RACE_LeaderName>(bytePtr, ref offset, recordEnd, Race[i].NumberOfGreatLeaders, section);

									CopyChunk(bytePtr, ref offset, recordEnd, racePtr, RACE_LEN_3, section);
									racePtr += RACE_LEN_3;
									CopyChunk(bytePtr, ref offset, recordEnd, (byte*)ptr2 + (long)i * raceeraRowLength, raceeraRowLength, section);

									CopyChunk(bytePtr, ref offset, recordEnd, racePtr, RACE_LEN_4, section);
									RaceScientificLeaderName[i] = ReadArray<RACE_LeaderName>(bytePtr, ref offset, recordEnd, Race[i].NumberOfScientificLeaders, section);
									offset = recordEnd;
								}
							}
							break;
						case 0x454c5552: // RULE
							Rule = new RULE[CheckCount(count, 4, offset, length, section)];
							RuleCult = new RULE_CULT[count][];
							RuleSpaceship = new int[count][];

							fixed (RULE* ptr = Rule) {
								for (int i = 0; i < count; i++) {
									byte* rulePtr = (byte*)(ptr + i);
									long recordEnd = BeginRecord(bytePtr, offset, length, section);
									CopyChunk(bytePtr, ref offset, recordEnd, rulePtr, RULE_LEN_1, section);
									RuleSpaceship[i] = ReadArray<int>(bytePtr, ref offset, recordEnd, Rule[i].NumberOfSpaceshipParts, section);
									CopyChunk(bytePtr, ref offset, recordEnd, rulePtr + RULE_LEN_1, RULE_LEN_2, section);
									RuleCult[i] = ReadArray<RULE_CULT>(bytePtr, ref offset, recordEnd, Rule[i].NumberOfCultureLevels, section);
									CopyTail(bytePtr, ref offset, recordEnd, rulePtr + RULE_LEN_1 + RULE_LEN_2, RULE_LEN_3);
									offset = recordEnd;
								}
							}
							break;
						case 0x434f4c53: // SLOC
							Sloc = ReadStaticSection<SLOC>(bytePtr, ref offset, length, count, section);
							break;
						case 0x48434554: // TECH
							Tech = ReadStaticSection<TECH>(bytePtr, ref offset, length, count, section);
							break;
						case 0x52524554: // TERR
							Terr = new TERR[CheckCount(count, 4, offset, length, section)];
							// The number of resources, which each terrain has a flag for, is taken from the first record
							int goodCount = 0;
							if (count > 0) {
								CheckRange(offset, 8, length, section);
								goodCount = *(int*)(bytePtr + offset + 4);
								if (goodCount < 0) {
									throw Malformed(section, $"negative number of resources {goodCount}");
								}
								// at least one bit for each flag
								CheckCount((long)count * goodCount, 0.125, offset, length, section);
							}
							TerrGood = new bool[count, goodCount];

							fixed (TERR* ptr = Terr) {
								// TERR contains dynamic data, so it can't be read in as a block. Instead, read in data for each TERR
								for (int i = 0; i < count; i++) {
									byte* terrBytePtr = (byte*)(ptr + i);
									long recordEnd = BeginRecord(bytePtr, offset, length, section);
									CopyChunk(bytePtr, ref offset, recordEnd, terrBytePtr, TERR_LEN_1, section);

									// Get TerrGood flags, dynamic data which determines which resources are allowed on which terrain types
									// They are a bit array, one bit for each resource, rounded up to whole bytes
									int recordGoods = Terr[i].NumPossibleResources;
									if (recordGoods < 0) {
										throw Malformed(section, $"negative number of resources {recordGoods}");
									}
									long flagBytes = ((long)recordGoods + 7) / 8;
									CheckRange(offset, flagBytes, recordEnd, section);
									byte* flags = bytePtr + offset;
									for (int j = 0, n = Math.Min(goodCount, recordGoods); j < n; j++) {
										TerrGood[i, j] = Util.GetFlag(flags[j / 8], j % 8);
									}
									offset += flagBytes;

									CopyTail(bytePtr, ref offset, recordEnd, terrBytePtr + TERR_LEN_1, TERR_LEN_2);
									offset = recordEnd;
								}
							}
							break;
						case 0x4d524654: // TFRM
							Tfrm = ReadStaticSection<TFRM>(bytePtr, ref offset, length, count, section);
							break;
						case 0x454c4954: // TILE
							Tile = ReadStaticSection<TILE>(bytePtr, ref offset, length, count, section);
							if (IsLegacy) {
								for (int i = 0; i < Tile.Length; i++) {
									Tile[i].FillFromLegacy();
								}
							}
							break;
						case 0x54494e55: // UNIT
							Unit = ReadStaticSection<UNIT>(bytePtr, ref offset, length, count, section);
							break;
						case 0x52484357: // WCHR
							Wchr = ReadStaticSection<WCHR>(bytePtr, ref offset, length, count, section);
							break;
						case 0x50414d57: // WMAP
							Wmap = new WMAP[CheckCount(count, 4, offset, length, section)];
							WmapResource = new int[count][];

							fixed (WMAP* ptr = Wmap) {
								for (int i = 0; i < count; i++) {
									byte* wmapPtr = (byte*)(ptr + i);
									long recordEnd = BeginRecord(bytePtr, offset, length, section);
									CopyChunk(bytePtr, ref offset, recordEnd, wmapPtr, WMAP_LEN_1, section);
									WmapResource[i] = ReadArray<int>(bytePtr, ref offset, recordEnd, Wmap[i].NumberOfResources, section);
									CopyTail(bytePtr, ref offset, recordEnd, wmapPtr + WMAP_LEN_1, WMAP_LEN_2);
									offset = recordEnd;
								}
							}
							break;
						case 0x5a495357: // WSIZ
							Wsiz = ReadStaticSection<WSIZ>(bytePtr, ref offset, length, count, section);
							break;
						default:
							throw new InvalidDataException("An error occured while parsing the BIQ file because a header was not found where expected.  Instead, found " + section);
					}
				}
			}
		}

		// The size of a FLAV record without its relationship values
		private const int FLAV_FIXED_LEN = 264;

		private static InvalidDataException Malformed(string section, string problem) {
			return new InvalidDataException($"Malformed BIQ file: in section {section}, {problem}.");
		}

		// Throws unless data[offset .. offset + size) lies within data[0 .. limit)
		private static void CheckRange(long offset, long size, long limit, string section) {
			if (size < 0 || offset < 0 || offset > limit - size) {
				throw Malformed(section, $"reading {size} bytes at offset {offset} would go past the end of the data at {limit}");
			}
		}

		// Checks that count records of at least minSize bytes each fit in data[offset .. limit), so that a corrupt count
		// can't cause a huge allocation, and returns the count
		private static int CheckCount(long count, double minSize, long offset, long limit, string section) {
			if (count < 0 || count > int.MaxValue || count * minSize > limit - offset) {
				throw Malformed(section, $"{count} records can't fit in the {limit - offset} bytes left");
			}
			return (int)count;
		}

		// Checks the record at offset, which starts with its length (not counting the length value itself), and returns where it ends
		private static unsafe long BeginRecord(byte* data, long offset, long limit, string section) {
			CheckRange(offset, 4, limit, section);
			int recordLength = *(int*)(data + offset);
			if (recordLength < 0) {
				throw Malformed(section, $"negative record length {recordLength}");
			}
			CheckRange(offset, 4L + recordLength, limit, section);
			return offset + 4 + recordLength;
		}

		// Copies the size bytes at offset, which must be inside the record ending at recordEnd, to dest
		// In a legacy file (a .bic or .bix) a record may end before a part Conquests added: as much as it has is copied,
		// the rest left zeroed (as CopyTail).
		[ThreadStatic] private static bool readingLegacy;

		private static unsafe void CopyChunk(byte* data, ref long offset, long recordEnd, void* dest, long size, string section) {
			if (readingLegacy && offset + size > recordEnd) {
				CopyTail(data, ref offset, recordEnd, dest, size);
				return;
			}
			CheckRange(offset, size, recordEnd, section);
			Buffer.MemoryCopy(data + offset, dest, size, size);
			offset += size;
		}

		// Copies the last fixed-size part of a record to dest: as much of it as the record has, leaving the rest of dest
		// (part of a freshly allocated array, so zeroed) as it is. Older or unusual records can be shorter than our structs.
		private static unsafe void CopyTail(byte* data, ref long offset, long recordEnd, void* dest, long size) {
			long available = Math.Clamp(recordEnd - offset, 0, size);
			Buffer.MemoryCopy(data + offset, dest, size, available);
			offset += available;
		}

		// Reads count values of T at offset, which must all be inside the record ending at recordEnd
		private static unsafe T[] ReadArray<T>(byte* data, ref long offset, long recordEnd, int count, string section) where T : unmanaged {
			if (count < 0) {
				throw Malformed(section, $"negative count {count}");
			}
			if (readingLegacy && offset + (long)count * sizeof(T) > recordEnd) {
				count = (int)Math.Max(0, (recordEnd - offset) / sizeof(T));
			}
			long size = (long)count * sizeof(T);
			CheckRange(offset, size, recordEnd, section);
			T[] result = new T[count];
			fixed (T* dest = result) {
				Buffer.MemoryCopy(data + offset, dest, size, size);
			}
			offset += size;
			return result;
		}

		// Reads a section of count fixed-size records, each of which starts with its length (not counting the length value itself)
		private static unsafe T[] ReadStaticSection<T>(byte* data, ref long offset, long limit, int count, string section) where T : unmanaged {
			T[] result = new T[CheckCount(count, 4, offset, limit, section)];
			fixed (T* dest = result) {
				for (int i = 0; i < count; i++) {
					long recordEnd = BeginRecord(data, offset, limit, section);
					Buffer.MemoryCopy(data + offset, dest + i, sizeof(T), Math.Min(recordEnd - offset, sizeof(T)));
					offset = recordEnd;
				}
			}
			return result;
		}
	}
}
