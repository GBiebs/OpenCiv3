namespace C7Engine {
	using System;
	using System.Collections.Generic;
	using System.Linq;

	// Civilization III's own random map generator (Civ3Conquests.exe 1.22,
	// Map::generate, Civ3 0x5fadd0), step by step, so that a seed and the
	// world settings give the same map as in Civ3, tile for tile. It works
	// in Civ3's own terms: tiles by index (y * width/2 + x/2, in the doubled
	// x coordinates OpenCiv3 also uses), Civ3's terrain numbers, and Civ3's
	// tile flags. MapGenerator turns the result into a GameMap. Over all
	// 1184 maps of a corpus of Civ3's saves it makes the same map, tile for
	// tile and start for start (MapGenExactTest).
	//
	// The notes this follows, with every function's address:
	// rea_civ3/notes/mapgen.md.
	public class Civ3MapGenerator {
		// Civ3's terrain numbers (the tile's terrain type, Civ3 0x5fa370).
		public const int Desert = 0, Plains = 1, Grassland = 2, Tundra = 3, FloodPlain = 4, Hills = 5, Mountains = 6,
			Forest = 7, Jungle = 8, Marsh = 9, Volcano = 10, Coast = 11, Sea = 12, Ocean = 13;

		// The tile flags Civ3 keeps at tile +0x30.
		public const uint BonusGrasslandFlag = 0x10000, StartFlag = 0x80000, SnowCappedFlag = 0x100000, ColdFlag = 0x200000,
			KeepFlag = 0x400000;

		// The world settings, as Civ3 numbers them (map +0x04..+0x30).
		public class Settings {
			public int width, height;
			public bool wrapX = true, wrapY;
			public int seed;
			// 0 archipelago, 1 continents, 2 pangaea.
			public int landform;
			// 0 = 80% water, 1 = 70%, 2 = 60% (4 = 40%, which Civ3's setup
			// screen doesn't offer).
			public int oceanCoverage;
			// 0 arid, 1 normal, 2 wet.
			public int climate;
			// 0 cool, 1 temperate, 2 warm.
			public int temperature;
			// 0 = 3 billion years, 1 = 4, 2 = 5.
			public int age;
			// -1 no barbarians .. 3.
			public int barbarians;
			public int numberOfCivs;
			// The world size's distance between civs (WSIZ).
			public int civDistance;
			// How many of the starts must be by the sea (> 20 tiles of it):
			// in Civ3 the number of seafaring civs in the game (Civ3
			// 0x5590c0 counts them for 0x5fe9c0).
			public int coastalStarts;
			// Single player: the civs' order is shuffled once the starts are
			// found, the human (slot 1) taking the start two thirds of the
			// way down the ranking, and civs on one landmass put together.
			public bool shuffleStarts = true;
			// OpenCiv3's own: every landmass but the largest is sunk, leaving
			// one landmass and no islands (Rising Tide, Eternal War).
			public bool singleLandmass;
		}

		// What the generator takes from the rules (the BIQ's TERR and GOOD),
		// by Civ3's terrain numbers.
		public class Rules {
			public int[] irrigation = new int[14], mining = new int[14], road = new int[14];
			public int[] food = new int[14], shields = new int[14], commerce = new int[14];
			// TERR +0x78, and whether TERR +0x70 (the worker job) is set: a
			// city site looks at the base terrain of a tile a worker can
			// clear.
			public bool[] allowCities = new bool[14], workerJob = new bool[14];
			public List<Good> goods = new();
		}

		// A resource, in the GOOD list's order.
		public class Good {
			// 0 bonus, 1 luxury, 2 strategic (GOOD +0x3c).
			public int type;
			public int appearanceRatio;
			public int food, shields, commerce;
			// Whether it needs an advance to be seen (GOOD +0x4c).
			public bool needsAdvance;
			// OpenCiv3's own: a strategic resource a unit or building needs
			// is placed before those nothing needs (MDJ's rules make nearly
			// every resource strategic; in Civ3's rules a unit needs each).
			public bool placedFirst;
			// The terrains (Civ3 numbers) it may be on (TERR +8).
			public bool[] allowedOn = new bool[14];
		}

		public readonly Settings settings;
		public readonly Rules rules;
		public readonly int width, height, tileCount;

		// Per tile: Civ3's terrain type, base terrain, flags (+0x30), river
		// bits (+0x4), body of land or water (+0x1e).
		public readonly int[] terrain, baseTerrain;
		public readonly uint[] flags;
		public readonly byte[] river;
		public readonly short[] body;
		// The resource on each tile (an index into rules.goods, or -1), and
		// whether it has a goody hut.
		public readonly int[] resource;
		public readonly bool[] hut;

		// The bodies of land and water, land first and largest first
		// (Civ3 0x5fb490).
		public readonly List<(bool isLand, int size)> bodies = new();

		public Civ3MapGenerator(Settings settings, Rules rules) {
			this.settings = settings;
			this.rules = rules;
			width = settings.width;
			height = settings.height;
			tileCount = width * height / 2;
			terrain = new int[tileCount];
			baseTerrain = new int[tileCount];
			flags = new uint[tileCount];
			river = new byte[tileCount];
			body = new short[tileCount];
			resource = new int[tileCount];
			Array.Fill(resource, -1);
			hut = new bool[tileCount];
		}

		// Map::generate, Civ3 0x5fadd0: every step in Civ3's order. (Its settings
		// come resolved: a random landform and so on are left to the caller.)
		public void Generate() {
			// Every tile starts as ocean (Civ3 0x5fac90).
			for (int i = 0; i < tileCount; ++i) {
				SetTerrain(i, Ocean);
			}
			Shape();
			if (settings.landform == 1) {
				CutContinentsChannel();
			}
			if (settings.singleLandmass) {
				SinkAllButLargestLandmass();
			}
			SetWaterDepth();
			AddHillsAndMountains();
			region = new short[tileCount];
			FindRegions();
			AssignClimate();
			AddLakes();
			AddRivers();
			// Deserts on a river are flood plains (Civ3 0x5fadd0).
			for (int i = 0; i < tileCount; ++i) {
				if (river[i] != 0 && terrain[i] == Desert) {
					SetTerrain(i, FloodPlain);
				}
			}
			FixTundraAndShallows();
			FixVertices();
			AddResources();
			AddGoodyHuts();
			AddBonusGrassland();
			FindStartingLocations();
		}

		// The scenario editor's Map > Redistribute (MapGenerator.Redistribute):
		// a placement step again on a map loaded into the arrays, its old
		// placements taken off first.
		public void RedoResources() {
			Array.Fill(resource, -1);
			AddResources();
		}

		public void RedoGoodyHuts() {
			Array.Fill(hut, false);
			AddGoodyHuts();
		}

		public void RedoBonusGrassland() {
			for (int i = 0; i < tileCount; ++i) {
				flags[i] &= ~BonusGrasslandFlag;
			}
			AddBonusGrassland();
		}

		public void RedoStartingLocations() {
			for (int i = 0; i < tileCount; ++i) {
				flags[i] &= ~StartFlag;
			}
			FindStartingLocations();
		}

		// --- Tiles and neighbors ---

		public int X(int i) {
			int y = i / (width / 2);
			return (y & 1) + (i % (width / 2)) * 2;
		}

		public int Y(int i) {
			return i / (width / 2);
		}

		public int Index(int x, int y) {
			return (width / 2) * y + (x >> 1);
		}

		// The spiral of neighbors around a tile (Civ3 0x5f66a0): k = 0 is
		// the tile itself, 1..8 the ring around it starting north-east and
		// going clockwise, 9..24 the next ring, and so on; k < (2r+1)^2 covers
		// the rings up to r.
		public static (int dx, int dy) Spiral(int k) {
			if (k <= 0) {
				return (0, 0);
			}
			int r = 0;
			for (int s = 1; ; ++r) {
				s += 2;
				if (s * s > k) {
					++r;
					break;
				}
			}
			int m = k - (2 * r - 1) * (2 * r - 1);
			int n = m + 1;
			if (k < 9) {
				if (n <= r * 2) {
					return (n, n - 2 * r);
				}
				if (n <= r * 4) {
					return (4 * r - n, n - 2 * r);
				}
				if (n <= r * 6) {
					return (4 * r - n, 6 * r - n);
				}
				return (n - 8 * r, 6 * r - n);
			}
			if (n > 8 * r - 4) {
				if (n == 8 * r - 3) {
					return (0, -2 * r);
				}
				if (n == 8 * r - 2) {
					return (2 * r, 0);
				}
				if (n == 8 * r - 1) {
					return (0, 2 * r);
				}
				return (-2 * r, 0);
			}
			if (n < 2 * r) {
				return (n, n - 2 * r);
			}
			if (n < 4 * r - 1) {
				return (4 * r - (m + 2), m + 2 - 2 * r);
			}
			if (n < 6 * r - 2) {
				return (4 * r - (m + 3), 6 * r - (m + 3));
			}
			return (m + 4 - 8 * r, 6 * r - (m + 4));
		}

		private static readonly (int dx, int dy)[] spiral = BuildSpiral(441);

		private static (int, int)[] BuildSpiral(int n) {
			var s = new (int, int)[n];
			for (int k = 0; k < n; ++k) {
				s[k] = Spiral(k);
			}
			return s;
		}

		// The k'th neighbor of (x, y) in the spiral, with the map's wrapping,
		// or false if it is off the map.
		public bool Neighbor(int x, int y, int k, out int nx, out int ny) {
			(int dx, int dy) = k < spiral.Length ? spiral[k] : Spiral(k);
			return Offset(x, y, dx, dy, out nx, out ny);
		}

		public bool Offset(int x, int y, int dx, int dy, out int nx, out int ny) {
			nx = x + dx;
			if (settings.wrapX) {
				if (nx < 0) {
					nx += width;
				} else if (nx >= width) {
					nx -= width;
				}
			}
			ny = y + dy;
			if (settings.wrapY) {
				if (ny < 0) {
					ny += height;
				} else if (ny >= height) {
					ny -= height;
				}
			}
			return nx >= 0 && nx < width && ny >= 0 && ny < height;
		}

		// The four tiles sharing an edge with a tile (Civ3 0x68d600/0x68d610).
		public static readonly int[] EdgeDx = { 1, 1, -1, -1 }, EdgeDy = { -1, 1, 1, -1 };

		public static bool IsWater(int t) => t >= Coast;

		public bool IsWaterAt(int i) => terrain[i] >= Coast;

		// Tile::setTerrain, Civ3 0x5f9190: the base terrain follows the type.
		public void SetTerrain(int i, int t) {
			if (t == Desert && river[i] != 0) {
				terrain[i] = FloodPlain;
				baseTerrain[i] = Desert;
				return;
			}
			terrain[i] = t;
			if (t < 4) {
				baseTerrain[i] = t;
			} else if (t < Coast) {
				if (t == Forest) {
					if (baseTerrain[i] < Coast && baseTerrain[i] != Desert) {
						return;
					}
					baseTerrain[i] = Grassland;
				} else if (t == FloodPlain) {
					baseTerrain[i] = Desert;
				} else {
					baseTerrain[i] = Grassland;
				}
			} else {
				baseTerrain[i] = t;
			}
		}

		// --- The random number generator, Civ3 0x6263f0 and 0x626440 ---

		public struct Rng {
			public uint state;

			public Rng(uint seed) {
				state = seed;
			}

			public Rng(int seed) {
				state = (uint)seed;
			}

			// A number in [0, 1): 15 bits of the state.
			public double Next() {
				state = state * 0x41C64E6Du + 0x3039u;
				return ((state >> 16) & 0x7fff) / 32768.0;
			}

			// 0..n-1: n is cut to 16 bits, and the product of the 15-bit draw
			// and n, which Civ3 takes as a double, is exact, so it is done in
			// integers.
			public int Rand(int n) {
				state = state * 0x41C64E6Du + 0x3039u;
				return (int)((((state >> 16) & 0x7fff) * (uint)(n & 0xffff)) >> 15);
			}

			public void Skip(int n) {
				for (int i = 0; i < n; ++i) {
					Next();
				}
			}
		}

		// The order Civ3 visits things in: a Fisher-Yates shuffle of 0..n-1.
		public static int[] Shuffle(ref Rng rng, int n) {
			int[] perm = new int[n];
			for (int i = 0; i < n; ++i) {
				perm[i] = i;
			}
			for (int i = 0; i < n; ++i) {
				int j = i + rng.Rand(n - i);
				if (j != i) {
					(perm[i], perm[j]) = (perm[j], perm[i]);
				}
			}
			return perm;
		}

		// --- Fractal heightmaps, Civ3 0x5f13a0 ---

		public class Fractal {
			public const int GW = 129, GH = 65;
			public readonly byte[] g = new byte[GW * GH];
			private readonly int width, height, flags;
			private readonly double sx, sy;
			private Rng rng;

			// flags: 1 copies column 0 into column 128 (x wraps), 2 copies row
			// 0 into row 64 (y wraps), 8 zeroes rows 0 and 64 (the poles are
			// water), 0x10 cuts a second seam (Continents). L sets the
			// coarsest step: 2^(6-L) cells.
			public Fractal(int width, int height, int L, int flags, int seed, Fractal seam = null) {
				this.width = width;
				this.height = height;
				this.flags = flags;
				// When the seed is 0 Civ3 takes the time; never with the seeds
				// the generator passes in practice.
				rng = new Rng(seed);
				sx = 128.0 / width;
				sy = 64.0 / height;
				int s0 = Math.Clamp(6 - L, 0, 6);
				rng.Next();
				for (int s = s0; s >= 0; --s) {
					Edges();
					int nx = (128 >> s) + ((flags & 1) == 0 ? 1 : 0);
					int ny = (64 >> s) + ((flags & 2) == 0 ? 1 : 0);
					int noise = 1 << (8 + s - s0), half = 1 << (7 + s - s0);
					for (int i = 0; i < nx; ++i) {
						for (int j = 0; j < ny; ++j) {
							int at = (i << s) * GH + (j << s);
							if (s == s0) {
								g[at] = (byte)rng.Rand(256);
								continue;
							}
							bool iOdd = (i & 1) != 0, jOdd = (j & 1) != 0;
							int v;
							if (iOdd && jOdd) {
								v = (G(i - 1, j - 1, s) + G(i - 1, j + 1, s) + G(i + 1, j - 1, s) + G(i + 1, j + 1, s)) >> 2;
							} else if (iOdd) {
								v = (G(i - 1, j, s) + G(i + 1, j, s)) >> 1;
							} else if (jOdd) {
								v = (G(i, j - 1, s) + G(i, j + 1, s)) >> 1;
							} else {
								continue;
							}
							v += rng.Rand(noise) - half;
							g[at] = (byte)Math.Clamp(v, 0, 255);
						}
					}
				}
				Edges();
				if (seam != null) {
					CutSeams(seam);
				}
			}

			private int G(int i, int j, int s) => g[(i << s) * GH + (j << s)];

			private void Edges() {
				if ((flags & 8) != 0) {
					for (int ix = 0; ix < GW; ++ix) {
						g[ix * GH] = 0;
						g[ix * GH + 64] = 0;
					}
				} else if ((flags & 2) != 0) {
					for (int ix = 0; ix < GW; ++ix) {
						g[ix * GH + 64] = g[ix * GH];
					}
				}
				if ((flags & 1) != 0) {
					for (int iy = 0; iy < GH; ++iy) {
						g[128 * GH + iy] = g[iy];
					}
				}
			}

			// Civ3 0x5f17e0: an ocean seam about 32 cells wide at x = 0 that
			// wanders with the seam map, and with flag 0x10 a second one at
			// x = 64.
			private void CutSeams(Fractal b) {
				for (int iy = 0; iy < GH; ++iy) {
					int d = (b.g[96 * GH + iy] - 128) / 8;
					int d2 = (b.g[32 * GH + iy] - 128) / 4;
					for (int i = 0; i < 16; ++i) {
						int a = Wrap128(d + i), c = Wrap128(d - i);
						g[a * GH + iy] = (byte)(g[a * GH + iy] * i / 16);
						g[c * GH + iy] = (byte)(g[c * GH + iy] * i / 16);
						if ((flags & 0x10) != 0) {
							a = Wrap128(d2 + i + 64);
							c = Wrap128(d2 - i + 64);
							g[a * GH + iy] = (byte)((g[a * GH + iy] * i + 16 - i) / 16);
							g[c * GH + iy] = (byte)((g[c * GH + iy] * i + 16 - i) / 16);
						}
					}
				}
				for (int iy = 0; iy < GH; ++iy) {
					g[128 * GH + iy] = g[iy];
				}
			}

			private static int Wrap128(int v) => v < 0 ? v + 128 : v >= 128 ? v - 128 : v;

			// Civ3 0x5f19a0: the height at tile (x, y), interpolated between the
			// four grid points around it.
			public int At(int x, int y) {
				double fx = x * sx;
				int ix = (int)fx;
				double fy = y * sy;
				int iy = (int)fy;
				double ax = fx - ix;
				double ay = fy - iy;
				double ay1 = 1.0 - ay;
				double ax1 = 1.0 - ax;
				int b = ix * GH + iy;
				double v = g[b] * ax1 * ay1;
				v += g[b + GH] * ay1 * ax;
				v += g[b + 1] * ax1 * ay;
				v += g[b + GH + 1] * ay * ax;
				return Math.Clamp((int)v, 0, 255);
			}

			// Civ3 0x5f1a90: the height below which p percent of the 128 x 64
			// cells lie, by binary search.
			public int Percentile(int p) {
				p = Math.Clamp(p, 0, 100);
				int guess = p * 255 / 100;
				if (guess == 0) {
					return 0;
				}
				int lo = 0, hi = 255;
				while (true) {
					int count = 0;
					for (int iy = 0; iy < 64; ++iy) {
						for (int ix = 0; ix < 128; ++ix) {
							if (g[ix * GH + iy] < guess) {
								++count;
							}
						}
					}
					int share = (count * 100) >> 13;
					int next;
					if (share > p) {
						hi = guess;
						next = (lo + guess) / 2;
					} else {
						lo = guess;
						next = (hi + guess) / 2;
					}
					guess = next;
					if (guess == lo) {
						return guess;
					}
				}
			}
		}

		// --- Land and water, Civ3 0x5fc7e0 ---

		// How many tries the land and water took (Civ3 keeps its tenth).
		public int shapeAttempts;

		private void Shape() {
			int S = settings.seed;
			(int pA, int pB) = settings.oceanCoverage switch {
				0 => (82, 67),
				1 => (72, 57),
				4 => (42, 27),
				_ => (62, 47),
			};
			Rng lakes = new(S + 0xCC98);
			lakes.Skip(2);
			int yWrap = settings.wrapY ? 2 : 0;
			int F = (settings.wrapY ? 2 : 8) | 1;
			if (settings.landform == 1) {
				F |= 0x10;
			}
			int c = 0;
			for (int attempt = 1; ; ++attempt) {
				Fractal A;
				int tA, tB, t96;
				while (true) {
					int s = S + 113 * c;
					if (settings.landform == 0) {
						A = new Fractal(width, height, 3, F, ~s);
					} else {
						Fractal B = new(width, height, 2, yWrap, s + 0x3039);
						A = new Fractal(width, height, 2, F, ~s, B);
						if (settings.landform == 1) {
							Fractal C = new(width, height, 3, F, s, B);
							for (int k = 0; k < A.g.Length; ++k) {
								A.g[k] = (byte)((A.g[k] + C.g[k]) / 2);
							}
						}
					}
					t96 = A.Percentile(96);
					tA = A.Percentile(pA);
					tB = A.Percentile(pB);
					int tried = 113 * c;
					++c;
					// Civ3 tries again while the water line is in the middle of
					// the heights, and takes whatever the 15th seed gives.
					bool done = tried < 1130 ? tA < 70 || tA > 130 : tried >= 1695 || tA < 80 || tA > 120;
					if (done) {
						break;
					}
				}
				for (int i = 0; i < tileCount; ++i) {
					int x = X(i), y = Y(i);
					int h = A.At(x, y);
					if (h > t96 && lakes.Rand(50) == 0) {
						SetTerrain(i, Sea);
					} else if (h > tA) {
						// Land only where a neighbor is land too.
						for (int k = 1; k < 9; ++k) {
							if (Neighbor(x, y, k, out int nx, out int ny) && A.At(nx, ny) > tA) {
								SetTerrain(i, Grassland);
								break;
							}
						}
					} else {
						SetTerrain(i, h > tB ? Sea : Ocean);
					}
				}
				FindBodies();
				shapeAttempts = attempt;
				if (ShapeAccepted() || attempt >= 10) {
					return;
				}
			}
		}

		// Whether the landmasses have the landform's shape (Civ3 0x5fc7e0).
		private bool ShapeAccepted() {
			switch (settings.landform) {
				case 0:
					return bodies.Count > 2 && bodies[2].isLand && bodies[0].size < bodies[2].size * 2;
				case 1:
					if (bodies.Count <= 2 || !bodies[1].isLand || bodies[0].size >= bodies[1].size * 3 / 2) {
						return false;
					}
					return !bodies[2].isLand || bodies[0].size > bodies[2].size * 4;
				default:
					if (bodies.Count <= 1) {
						return false;
					}
					return !bodies[1].isLand || bodies[0].size > bodies[1].size * 8;
			}
		}

		// Map vt+0x7c, Civ3 0x5fb010: flood fills the bodies of land (joined
		// through all eight neighbors) and water (joined through edges only),
		// then sorts them land first and largest first (Civ3 0x5fb490).
		public void FindBodies() {
			Array.Fill(body, (short)-1);
			List<(bool isLand, int size)> found = new();
			int[] queue = new int[tileCount];
			for (int i = 0; i < tileCount; ++i) {
				if (body[i] != -1) {
					continue;
				}
				bool water = IsWaterAt(i);
				short id = (short)found.Count;
				body[i] = id;
				int head = 0, tail = 0;
				queue[tail++] = i;
				int size = 1;
				while (head < tail) {
					int t = queue[head++];
					int x = X(t), y = Y(t);
					if (!water) {
						for (int k = 1; k < 9; ++k) {
							if (Neighbor(x, y, k, out int nx, out int ny)) {
								int n = Index(nx, ny);
								if (!IsWaterAt(n) && body[n] != id) {
									body[n] = id;
									queue[tail++] = n;
									++size;
								}
							}
						}
					} else {
						for (int e = 0; e < 4; ++e) {
							if (Offset(x, y, EdgeDx[e], EdgeDy[e], out int nx, out int ny)) {
								int n = Index(nx, ny);
								if (IsWaterAt(n) && body[n] != id) {
									body[n] = id;
									queue[tail++] = n;
									++size;
								}
							}
						}
					}
				}
				found.Add((!water, size));
			}
			int[] order = new int[found.Count];
			for (int k = 0; k < order.Length; ++k) {
				order[k] = k;
			}
			MsvcQsort(order, (a, b) => {
				var ba = found[a];
				var bb = found[b];
				if (bb.isLand != ba.isLand) {
					return ba.isLand ? -1 : 1;
				}
				return bb.size < ba.size ? -1 : bb.size > ba.size ? 1 : 0;
			});
			int[] newId = new int[found.Count];
			bodies.Clear();
			for (int k = 0; k < order.Length; ++k) {
				newId[order[k]] = k;
				bodies.Add(found[order[k]]);
			}
			for (int i = 0; i < tileCount; ++i) {
				body[i] = (short)newId[body[i]];
			}
		}

		// The C runtime's qsort that Civ3 was built with (Civ3 0x66a17f, with
		// its insertion of the largest for 8 or fewer, 0x66a2d3): it is not
		// stable, so ties come out in its own order, which Civ3's maps depend
		// on.
		public static void MsvcQsort(int[] a, Comparison<int> comp) {
			if (a.Length < 2) {
				return;
			}
			int[] loStack = new int[30], hiStack = new int[30];
			int sp = 0;
			int lo = 0, hi = a.Length - 1;
			while (true) {
				int size = hi - lo + 1;
				if (size <= 8) {
					ShortSort(a, lo, hi, comp);
				} else {
					int mid = lo + size / 2;
					(a[mid], a[lo]) = (a[lo], a[mid]);
					int loguy = lo, higuy = hi + 1;
					while (true) {
						do {
							++loguy;
						} while (loguy <= hi && comp(a[loguy], a[lo]) <= 0);
						do {
							--higuy;
						} while (higuy > lo && comp(a[higuy], a[lo]) >= 0);
						if (higuy < loguy) {
							break;
						}
						(a[loguy], a[higuy]) = (a[higuy], a[loguy]);
					}
					(a[lo], a[higuy]) = (a[higuy], a[lo]);
					if (higuy - 1 - lo >= hi - loguy) {
						if (lo + 1 < higuy) {
							loStack[sp] = lo;
							hiStack[sp] = higuy - 1;
							++sp;
						}
						if (loguy < hi) {
							lo = loguy;
							continue;
						}
					} else {
						if (loguy < hi) {
							loStack[sp] = loguy;
							hiStack[sp] = hi;
							++sp;
						}
						if (lo + 1 < higuy) {
							hi = higuy - 1;
							continue;
						}
					}
				}
				--sp;
				if (sp < 0) {
					return;
				}
				lo = loStack[sp];
				hi = hiStack[sp];
			}
		}

		private static void ShortSort(int[] a, int lo, int hi, Comparison<int> comp) {
			while (hi > lo) {
				int max = lo;
				for (int p = lo + 1; p <= hi; ++p) {
					if (comp(a[p], a[max]) > 0) {
						max = p;
					}
				}
				(a[max], a[hi]) = (a[hi], a[max]);
				--hi;
			}
		}

		// OpenCiv3's own (settings.singleLandmass): the land of every body but
		// the largest landmass becomes sea, so the coast step rings what is
		// left.
		private void SinkAllButLargestLandmass() {
			for (int i = 0; i < tileCount; ++i) {
				if (!IsWaterAt(i) && body[i] != 0) {
					SetTerrain(i, Sea);
				}
			}
			FindBodies();
		}

		// --- Continents' channel, Civ3 0x5fcd90 ---

		// Every tile of the second landmass near the first becomes sea, so the
		// two are apart.
		private void CutContinentsChannel() {
			int r = Math.Min(4, (width + height) / 100);
			int K = (2 * r + 5) * (2 * r + 5);
			for (int i = 0; i < tileCount; ++i) {
				if (body[i] != 1) {
					continue;
				}
				int x = X(i), y = Y(i);
				for (int k = 1; k < K; ++k) {
					if (Neighbor(x, y, k, out int nx, out int ny) && body[Index(nx, ny)] == 0) {
						SetTerrain(i, Sea);
					}
				}
			}
			FindBodies();
		}

		// --- Hills, mountains and volcanoes, Civ3 0x5fd560 ---

		private void AddHillsAndMountains() {
			int S = settings.seed;
			Fractal H = new(width, height, 2, 1, S + 0x9A2112);
			Rng rng = new(S + 0xD431);
			for (int i = 0; i < tileCount; ++i) {
				rng.Rand(tileCount - i);
			}
			// The bands of the heights by the world's age: low hills, hills,
			// mountains, and nothing above the top. Younger worlds have more
			// of each.
			int lowHillsFrom, lowHillsTo, hillsFrom, mountainsFrom, top, mountainChance;
			switch (settings.age) {
				case 0:
					(lowHillsFrom, lowHillsTo, hillsFrom, mountainsFrom, top, mountainChance) =
						(H.Percentile(18), H.Percentile(28), H.Percentile(55), H.Percentile(65), H.Percentile(77), 94);
					break;
				case 1:
					(lowHillsFrom, lowHillsTo, hillsFrom, mountainsFrom, top, mountainChance) =
						(H.Percentile(20), H.Percentile(25), H.Percentile(63), H.Percentile(70), H.Percentile(77), 96);
					break;
				default:
					(lowHillsFrom, lowHillsTo, hillsFrom, mountainsFrom, top, mountainChance) =
						(H.Percentile(21), H.Percentile(24), H.Percentile(66), H.Percentile(70), H.Percentile(74), 98);
					break;
			}
			for (int i = 0; i < tileCount; ++i) {
				if (terrain[i] != Grassland) {
					continue;
				}
				int h = H.At(X(i), Y(i));
				if (h > top) {
					continue;
				}
				if (h >= mountainsFrom) {
					SetTerrain(i, rng.Rand(100) < mountainChance ? Mountains : Volcano);
				} else if (h >= hillsFrom || (h <= lowHillsTo && h >= lowHillsFrom)) {
					SetTerrain(i, Hills);
				}
			}
		}

		// --- Regions, Civ3 0x5fd7e0 and 0x5fdf10 ---

		// Each tile's region: a patch of up to 16 tiles of a landmass that
		// later gets one terrain. Regions are numbered within each landmass.
		public short[] region;

		private bool IsHillsOrMountains(int i) => terrain[i] == Hills || terrain[i] == Mountains;

		private void FindRegions() {
			Array.Fill(region, (short)-1);
			int[] regionsOfBody = new int[bodies.Count];
			Rng rng = new(settings.seed + 0xD431);
			int[] order = Shuffle(ref rng, tileCount);
			foreach (int t in order) {
				if (region[t] != -1 || IsWaterAt(t)) {
					continue;
				}
				// Civ3 has another way when a landmass has 65535 regions,
				// which no map comes near.
				int b = body[t];
				if (RegionWalk(X(t), Y(t), (short)regionsOfBody[b])) {
					++regionsOfBody[b];
				}
			}
		}

		// Civ3 0x5fdf10: grows a region from a tile by 16 steps, each a random
		// walk from the tile through the region's own tiles (at most 3
		// moves) to an unclaimed tile on the same landmass, hills and
		// mountains apart from the rest. Returns whether the region kept any
		// tile.
		private bool RegionWalk(int x0, int y0, short id) {
			Rng rng = new(settings.seed + 29 * id);
			rng.Skip(4);
			int start = Index(x0, y0);
			bool seedHM = IsHillsOrMountains(start);
			int claimed = 0;
			for (int step = 0; step < 16; ++step) {
				int cx = x0, cy = y0;
				int at = start;
				int moves = 0;
				bool claim = region[at] == -1;
				while (!claim) {
					int d = rng.Rand(4) & 3;
					bool moved = false;
					for (int tries = 0; tries < 4; ++tries, d = (d + 1) & 3) {
						if (!Offset(cx, cy, EdgeDx[d], EdgeDy[d], out int nx, out int ny)) {
							continue;
						}
						int n = Index(nx, ny);
						if (body[n] == body[at] && IsHillsOrMountains(n) == seedHM && (region[n] == -1 || region[n] == id)) {
							cx = nx;
							cy = ny;
							at = n;
							moved = true;
							break;
						}
					}
					if (!moved) {
						step = 16;
						break;
					}
					if (++moves >= 3) {
						break;
					}
					claim = region[at] == -1;
				}
				if (claim) {
					region[at] = id;
					++claimed;
				}
			}
			// A region of one to three tiles gives its first tile and the one
			// north-east of it to the neighboring region most of their edges
			// touch.
			short[] nb = { -1, -1, -1, -1 };
			int prev = 0;
			while (claimed < 4 && claimed != prev) {
				prev = claimed;
				for (int k = 1; k >= 0; --k) {
					if (!Neighbor(x0, y0, k, out int tx, out int ty)) {
						continue;
					}
					int t = Index(tx, ty);
					if (body[t] != body[start] || region[t] != id) {
						continue;
					}
					for (int e = 0; e < 4; ++e) {
						if (Offset(tx, ty, EdgeDx[e], EdgeDy[e], out int ex, out int ey)) {
							nb[e] = -1;
							int n = Index(ex, ey);
							if (body[n] == body[start] && region[n] != id) {
								nb[e] = region[n];
							}
						}
					}
					short a = nb[0], b = nb[1], c = nb[2], d = nb[3];
					short to;
					if (a != -1 && (a == b || a == c || a == d)) {
						to = a;
					} else if (b != -1 && (b == c || b == d)) {
						to = b;
					} else if (c != -1 && c == d) {
						to = c;
					} else if (d != -1) {
						to = d;
					} else if (c != -1) {
						to = c;
					} else if (b != -1) {
						to = b;
					} else if (a != -1) {
						to = a;
					} else {
						continue;
					}
					region[t] = to;
					--claimed;
				}
			}
			return claimed != 0;
		}

		// --- Climate, Civ3 0x6011a0 ---

		private void AssignClimate() {
			int S = settings.seed;
			int J, TV, TL, M, K;
			switch (settings.temperature) {
				case 0:
					(J, TV, TL, M, K) = (3, 35, 45, 10, 3);
					break;
				case 1:
					(J, TV, TL, M, K) = (4, 50, 60, 12, 5);
					break;
				default:
					(J, TV, TL, M, K) = (5, 55, 65, 16, 7);
					break;
			}
			int plainsLo, plainsHi, desertLo, desertHi, marshGrass, marshJungle;
			switch (settings.climate) {
				case 0:
					J -= 1;
					(plainsLo, plainsHi, desertLo, desertHi, marshGrass, marshJungle) = (12, 44, 13, 34, 2, 7);
					break;
				case 1:
					(plainsLo, plainsHi, desertLo, desertHi, marshGrass, marshJungle) = (14, 42, 17, 30, 5, 10);
					break;
				default:
					J += 2;
					(plainsLo, plainsHi, desertLo, desertHi, marshGrass, marshJungle) = (16, 40, 20, 27, 8, 15);
					break;
			}
			int maybeCold = (TV + J) / 2;
			int F = (settings.wrapY ? 2 : 8) | 1;
			Fractal H = new(width, height, 3, F, S + 0x1E1735);
			Rng rng = new(S + 0xF0FF3);
			int[] order = Shuffle(ref rng, tileCount);
			bool[] visited = new bool[tileCount];
			foreach (int t in order) {
				int x = X(t), y = Y(t);
				int lat = Math.Abs(height / 2 - y) * 180 / height;
				int v = (H.At(x, y) - 128) * 30 / 256 + lat;
				if (IsWaterAt(t)) {
					continue;
				}
				int wd = 0;
				for (int k = 1; k < 25; ++k) {
					if (Neighbor(x, y, k, out int nx, out int ny) && IsWaterAt(Index(nx, ny))) {
						wd = k < 9 ? 2 : 1;
						break;
					}
				}
				if (v >= 23 && v < 48) {
					v += 2 * wd;
				} else {
					v -= wd;
				}
				if (v > TV || (v > maybeCold && rng.Rand(100) < 50)) {
					flags[t] |= ColdFlag;
				}
				if (visited[t]) {
					continue;
				}
				int type = Grassland;
				if (v > plainsLo && v < plainsHi) {
					type = Plains;
				}
				if (v > desertLo && v < desertHi) {
					type = Desert;
				}
				if (v < J) {
					type = Jungle;
				} else if (v > TV && lat > TL) {
					type = Tundra;
				}
				if (v < M) {
					if (type == Grassland) {
						if (rng.Rand(100) < wd * K + marshGrass) {
							type = Marsh;
						}
					} else if (type == Jungle) {
						if (rng.Rand(100) < wd * K + marshJungle) {
							type = Marsh;
						}
					}
				}
				FillRegion(x, y, type, visited);
			}

			// Forests, on the flat land but deserts, jungles and marshes.
			Fractal H4 = new(width, height, 5, F, S + 0x34B59E);
			int forestFrom = H4.Percentile(70), forestTo = H4.Percentile(100);
			for (int i = 0; i < tileCount; ++i) {
				int t = terrain[i];
				if (baseTerrain[i] == Desert || t == Jungle || t == Hills || t == Mountains || t == Marsh || t == Volcano || IsWater(t)) {
					continue;
				}
				int h = H4.At(X(i), Y(i));
				if (t == Plains) {
					h -= 10;
				}
				if (h >= forestFrom && h <= forestTo) {
					SetTerrain(i, Forest);
				}
			}

			// Snow on mountains near tundra or near other snow.
			for (int i = 0; i < tileCount; ++i) {
				if (terrain[i] != Mountains) {
					continue;
				}
				int x = X(i), y = Y(i);
				int chance = 50;
				for (int k = 0; k < 49; ++k) {
					if (!Neighbor(x, y, k, out int nx, out int ny)) {
						continue;
					}
					if (k == 9 || k == 25) {
						chance >>= 1;
					}
					int n = Index(nx, ny);
					if (baseTerrain[n] == Tundra || (k < 25 && terrain[n] == Mountains && (flags[n] & SnowCappedFlag) != 0)) {
						if (rng.Rand(100) < chance) {
							flags[i] |= SnowCappedFlag;
							break;
						}
					}
				}
			}
		}

		// Civ3 0x601ac0: gives a region its terrain, from one of its tiles,
		// leaving its hills, mountains and volcanoes be.
		private void FillRegion(int x, int y, int type, bool[] visited) {
			int i = Index(x, y);
			visited[i] = true;
			int t = terrain[i];
			if (t == Hills || t == Mountains || t == Volcano) {
				return;
			}
			if (type != Grassland) {
				SetTerrain(i, type);
			}
			for (int k = 1; k < 9; ++k) {
				if (!Neighbor(x, y, k, out int nx, out int ny)) {
					continue;
				}
				int n = Index(nx, ny);
				int nt = terrain[n];
				if (body[n] == body[i] && region[n] == region[i] && nt != Hills && nt != Mountains && nt != Volcano && !visited[n]) {
					FillRegion(nx, ny, type, visited);
				}
			}
		}

		// --- Lakes, Civ3 0x5fcf50 ---

		// The food irrigation adds to a tile's terrain (TERR +0x4c, Civ3
		// 0x5eb3c0).
		private int Irrigation(int i) => rules.irrigation[terrain[i]];

		// Map vt+0x54, Civ3 0x603750: whether one of the first k tiles of the
		// spiral is water of a body of 20 tiles or fewer.
		public bool NearSmallWater(int x, int y, int k) {
			for (int j = 0; j < k; ++j) {
				if (Neighbor(x, y, j, out int nx, out int ny)) {
					int n = Index(nx, ny);
					if (IsWaterAt(n) && bodies[body[n]].size <= 20) {
						return true;
					}
				}
			}
			return false;
		}

		// Map vt+0x60, Civ3 0x603850.
		public bool HasFreshWater(int x, int y) {
			return NearSmallWater(x, y, 9) || river[Index(x, y)] != 0;
		}

		// Every landmass of 75 tiles or more gets fresh water: if none of its
		// tiles with food has a river or a lake by it, a tile in the middle
		// of food-bearing land, or failing that of any land, becomes a lake.
		private void AddLakes() {
			Rng rng = new(settings.seed + 0x7C0);
			rng.Skip(3);
			int[] order = Shuffle(ref rng, tileCount);
			for (int b = 0; b < bodies.Count; ++b) {
				if (bodies[b].size < 75) {
					continue;
				}
				bool watered = false;
				for (int i = 0; i < tileCount && !watered; ++i) {
					watered = body[i] == b && HasFreshWater(X(i), Y(i)) && Irrigation(i) > 0;
				}
				if (watered) {
					continue;
				}
				bool done = false;
				for (int pass = 0; pass < 2 && !done; ++pass) {
					foreach (int t in order) {
						if (body[t] != b || river[t] != 0) {
							continue;
						}
						int x = X(t), y = Y(t);
						bool ok = true;
						for (int k = 0; k < 9 && ok; ++k) {
							if (Neighbor(x, y, k, out int nx, out int ny)) {
								int n = Index(nx, ny);
								ok = !IsWaterAt(n) && (pass == 1 || Irrigation(n) != 0);
							}
						}
						if (ok) {
							SetTerrain(t, Coast);
							done = true;
							break;
						}
					}
				}
			}
			FindBodies();
		}

		// --- Rivers, Civ3 0x6003d0 ---

		// Rivers run along the edges between tiles. An edge is named by the
		// sum of the coordinates of the two tiles either side of it, and a
		// river's way by one of the four diagonal steps (EdgeDx, EdgeDy).
		// Each tile's river byte has a bit for each of its edges (1 NE, 3
		// SE, 5 SW, 7 NW) and, once the rivers are drawn, each of its
		// corners a river passes through (0 N, 2 E, 4 S, 6 W).

		private static readonly int[] RiverDx = { 1, 1, -1, -1, 1, 1 }, RiverDy = { -1, 1, 1, -1, -1, 1 };

		private bool InBounds(int x, int y) => x >= 0 && x < width && y >= 0 && y < height;

		private void AddRivers() {
			int S = settings.seed;
			int budget = Math.Clamp(tileCount * 75 / 5000, 0, 256);
			Rng rng = new(S + 0x87B01);
			int[] order = Shuffle(ref rng, tileCount);

			// Each body's share of the river mouths, as the end of its range
			// of slots in the table of mouths.
			int n = bodies.Count;
			byte[] quota = new byte[n];
			int total = 0;
			for (int b = 0; b < n; ++b) {
				quota[b] = (byte)(bodies[b].size >= 37 ? 1 : 0);
				total += bodies[b].size;
				budget -= quota[b];
			}
			for (int b = n - 1; b >= 1; --b) {
				int share = bodies[b].size * budget / (total & 0xffff);
				quota[b] = (byte)(quota[b] + share);
				budget -= share;
				total -= bodies[b].size;
			}
			quota[0] = (byte)(quota[0] + budget);
			for (int b = 1; b < n && quota[b] != 0; ++b) {
				quota[b] = (byte)(quota[b - 1] + quota[b]);
			}

			// The mouths: where a straight piece of coast meets a corner.
			ushort[] score = new ushort[256], at = new ushort[256];
			bool[] water = new bool[4];
			foreach (int t in order) {
				int b = body[t];
				if (quota[b] == 0) {
					continue;
				}
				int x0 = X(t), y0 = Y(t);
				int wet = 0;
				bool outside = false;
				for (int u = 0; u < 4; ++u) {
					if (!Offset(x0, y0, (u >> 1) - (u & 1), (u + 1) / 2, out int ux, out int uy)) {
						outside = true;
						break;
					}
					water[u] = IsWaterAt(Index(ux, uy));
					if (water[u]) {
						++wet;
					}
				}
				if (outside || wet != 2 || water[0] == water[3]) {
					continue;
				}
				int s = 0;
				for (int k = 0; k < 121; ++k) {
					if (!Neighbor(x0, y0, k, out int nx, out int ny)) {
						continue;
					}
					int m = Index(nx, ny);
					if (body[m] != b) {
						continue;
					}
					s += 8;
					switch (terrain[m]) {
						case Jungle:
							s += (k < 25 ? 16 : 0) + (k < 9 ? 32 : 0) + (k < 1 ? 64 : 0);
							break;
						case Marsh:
							s += (k < 25 ? 32 : 0) + (k < 9 ? 64 : 0) + (k < 1 ? 64 : 0);
							break;
						case FloodPlain:
							break;
						default:
							if (terrain[m] <= Volcano) {
								s += (k < 25 ? 8 : 0) + (k < 9 ? 16 : 0) + (k < 1 ? 32 : 0);
							}
							break;
					}
				}
				int start = b > 0 ? quota[b - 1] : 0, end = quota[b];
				int j = start;
				while (j < end && (ushort)s <= score[j]) {
					++j;
				}
				if (j >= end) {
					continue;
				}
				// Not within 3 tiles of a mouth already in the body's slots
				// (an empty slot is tile 0).
				bool near = false;
				for (int e = start; e < end && !near; ++e) {
					int ex = X(at[e]), ey = Y(at[e]);
					int dx = Math.Abs(x0 - ex), dy = Math.Abs(y0 - ey);
					if (settings.wrapX && dx > width / 2) {
						dx = width - dx;
					}
					if (settings.wrapY && dy > height / 2) {
						dy = height - dy;
					}
					near = (dx + dy) / 2 <= 3;
				}
				if (near) {
					continue;
				}
				// Civ3 puts the mouth in the slot after the first one it
				// beats, moving the rest down: so a body's first slot stays
				// empty.
				ushort carryScore = (ushort)s, carryAt = (ushort)Index(x0, y0);
				for (int e = j + 1; e < end; ++e) {
					(score[e], carryScore) = (carryScore, score[e]);
					(at[e], carryAt) = (carryAt, at[e]);
				}
			}

			// Each river is traced upstream from its mouth.
			for (int slot = 0; slot < 256; ++slot) {
				if (at[slot] == 0) {
					continue;
				}
				int x0 = X(at[slot]), y0 = Y(at[slot]);
				int vx = 0, vy = 0;
				bool topWet = false, leftWet = false;
				for (int u = 0; u < 4; ++u) {
					if (!Offset(x0, y0, (u >> 1) - (u & 1), (u + 1) / 2, out int ux, out int uy)) {
						continue;
					}
					if (!IsWaterAt(Index(ux, uy))) {
						vx += ux;
						vy += uy;
					} else if (u == 0) {
						topWet = true;
					} else if (u == 1) {
						leftWet = true;
					}
				}
				int dir = topWet ? (leftWet ? 1 : 2) : (leftWet ? 0 : 3);
				TraceRiver(vx, vy, dir, 0, 0, 0, 0);
			}

			// The corners each river passes through.
			for (int i = 0; i < tileCount; ++i) {
				int x = X(i), y = Y(i);
				for (int bit = 0; bit < 8; bit += 2) {
					if (Neighbor(x, y, bit == 0 ? 8 : bit, out int nx, out int ny) && RiverThroughCorner(x, y, nx, ny)) {
						SetRiverBit(i, 1 << bit, true);
					}
				}
			}
		}

		// The tile at half an edge's coordinates.
		private int EdgeTile(int ex, int ey) {
			int x = ex / 2, y = ey / 2;
			return InBounds(x, y) ? Index(x, y) : -1;
		}

		// Civ3 0x5fff40 and 0x600020: marks (or clears) a river along an edge
		// on the tiles either side of it.
		private void MarkRiverEdge(int vx, int vy, int o, bool on) {
			int a = EdgeTile(vx + RiverDx[o], vy + RiverDy[o]);
			if (a >= 0) {
				SetRiverBit(a, 1 << (o != 0 ? 7 : 5), on);
			}
			int b = EdgeTile(vx + RiverDx[o + 2], vy + RiverDy[o + 2]);
			if (b >= 0) {
				SetRiverBit(b, 1 << (o != 0 ? 3 : 1), on);
			}
		}

		// Tile vt+0xf4 (Civ3 0x5fa510) and vt+0xd0 (0x5fa3b0): a desert with
		// a river is a flood plain at once, and is a desert again when its
		// last river bit goes (0x5fa3d0). So the rivers still to be traced
		// see the flood plains of those before.
		private void SetRiverBit(int i, int bit, bool on) {
			if (on) {
				river[i] |= (byte)bit;
				if (terrain[i] == Desert) {
					terrain[i] = FloodPlain;
				}
			} else {
				river[i] &= (byte)~bit;
				if (river[i] == 0 && terrain[i] == FloodPlain) {
					terrain[i] = Desert;
				}
			}
		}

		// Civ3 0x5ffc30: takes a river one edge further upstream, and
		// returns whether it went on. Where it can't, a long enough river
		// starts on a mountain; a short one is taken back.
		private bool TraceRiver(int vx, int vy, int dir, int length, int sinceFork, int turns, int straight) {
			if (length > 20) {
				RiverSource(vx, vy, dir);
				return true;
			}
			int o = ~dir & 1;
			MarkRiverEdge(vx, vy, o, true);
			int ex = vx + RiverDx[dir], ey = vy + RiverDy[dir];
			int[] scores = new int[3];
			if (turns < -1) {
				scores[0] = -1;
			} else {
				int d = (dir + 3) & 3;
				scores[0] = RiverOption(ex + RiverDx[d], ey + RiverDy[d], d, length, turns >= 0 ? 0 : 2);
			}
			scores[1] = RiverOption(ex + RiverDx[dir], ey + RiverDy[dir], dir, length, straight >= 2 ? 1 << (straight - 1) : 0);
			if (turns > 1) {
				scores[2] = -1;
			} else {
				int d = (dir + 1) & 3;
				scores[2] = RiverOption(ex + RiverDx[d], ey + RiverDy[d], d, length, turns <= 0 ? 0 : 2);
			}
			if (scores[0] >= 0 || scores[1] >= 0 || scores[2] >= 0) {
				if (BranchRiver(ex, ey, dir, length, sinceFork, turns, straight, scores)) {
					return true;
				}
				MarkRiverEdge(vx, vy, o, false);
				return false;
			}
			if (length < 4 || sinceFork < 2) {
				MarkRiverEdge(vx, vy, o, false);
				return false;
			}
			RiverSource(vx, vy, (dir + 2) & 3);
			return true;
		}

		// The mountain a river starts on.
		private void RiverSource(int vx, int vy, int dir) {
			switch (dir) {
				case 0:
					--vx;
					++vy;
					break;
				case 1:
					--vx;
					--vy;
					break;
				case 2:
					++vx;
					--vy;
					break;
				default:
					++vx;
					++vy;
					break;
			}
			int x = vx / 2, y = vy / 2 - 1;
			if (settings.wrapX) {
				x = x < 0 ? x + width : x >= width ? x - width : x;
			}
			if (settings.wrapY) {
				y = y < 0 ? y + height : y >= height ? y - height : y;
			}
			if (InBounds(x, y)) {
				SetTerrain(Index(x, y), Mountains);
			}
		}

		// Civ3 0x5ff610: how good the edge at (vx, vy), going dir, is for the
		// river: -1 (or below 0) if it can't go there, which is unless both
		// tiles beside it are land and the tiles beside the next two edges
		// have no river; otherwise the weights of the tiles around two
		// points three edges ahead, divided by the given divisor.
		private int RiverOption(int vx, int vy, int dir, int length, int divisor) {
			int o = ~dir & 1;
			int ax = RiverDx[o], ay = RiverDy[o];
			int Tile(int px, int py) => EdgeTile(px, py);
			bool Land(int t) => t >= 0 && !IsWaterAt(t);
			bool Dry(int t) => t >= 0 && river[t] == 0;
			if (!Land(Tile(vx + ax, vy + ay)) || !Land(Tile(vx - ax, vy - ay))) {
				return -1;
			}
			int dx = RiverDx[dir], dy = RiverDy[dir];
			int px = vx + 2 * dx, py = vy + 2 * dy;
			int t3 = Tile(px + ax, py + ay);
			if (!Land(t3) || !Dry(t3) || !Dry(Tile(px + 3 * ax, py + 3 * ay))) {
				return -1;
			}
			int t5 = Tile(px - ax, py - ay);
			if (!Land(t5) || !Dry(t5) || !Dry(Tile(px - 3 * ax, py - 3 * ay))) {
				return -1;
			}
			int qx = vx + 4 * dx, qy = vy + 4 * dy;
			if (!Dry(Tile(qx + ax, qy + ay)) || !Dry(Tile(qx + 3 * ax, qy + 3 * ay))
				|| !Dry(Tile(qx - ax, qy - ay)) || !Dry(Tile(qx - 3 * ax, qy - 3 * ay))) {
				return -1;
			}
			int rx = vx + 6 * dx, ry = vy + 6 * dy;
			int c1x = (rx + ax) / 2, c1y = (ry + ay) / 2;
			int c2x = (rx - ax) / 2, c2y = (ry - ay) / 2;
			if (!InBounds(c1x, c1y) || !InBounds(c2x, c2y)) {
				return -1;
			}
			int sum = 0;
			for (int k = 0; k < 49; ++k) {
				(int sx, int sy) = spiral[k];
				if (InBounds(c1x + sx, c1y + sy)) {
					sum += RiverWeight(c1x + sx, c1y + sy, length, k + 1);
				}
				if (InBounds(c2x + sx, c2y + sy)) {
					sum += RiverWeight(c2x + sx, c2y + sy, length, k + 1);
				}
			}
			return divisor > 0 ? sum / divisor : sum;
		}

		// Civ3 0x5ff400: how much a river wants to go by a tile: lowland near
		// its mouth, hills and mountains further up. The noise is the tile's
		// own, the same every time.
		private int RiverWeight(int x, int y, int length, int closeness) {
			Rng rng = new(settings.seed + 666 * (((width / 2) * y + (x >> 1)) & 0xffff));
			rng.Skip(4);
			int i = Index(x, y);
			if (IsWaterAt(i)) {
				return -10;
			}
			int w = terrain[i] switch {
				Desert => length < 4 ? 2 : length < 8 ? 0 : -1,
				Plains or Forest or Marsh or Jungle => length < 4 ? 4 : length < 8 ? 2 : -1,
				Grassland => length < 4 ? 5 : length < 8 ? 2 : -1,
				Hills => length < 4 ? -3 : 5,
				Mountains => length < 4 ? -3 : length < 8 ? 10 : 15,
				Volcano => -5,
				_ => length < 8 ? 0 : -1,
			};
			w += rng.Rand(11) - 5;
			if (closeness <= 2) {
				w *= 2;
			}
			if (closeness <= 1) {
				w *= 2;
			}
			return w + rng.Rand(11) - 5;
		}

		// Civ3 0x600100: follows the best of the ways a river can go on, or
		// two or three of them where they are about as good: a fork.
		private bool BranchRiver(int ex, int ey, int dir, int length, int sinceFork, int turns, int straight, int[] s) {
			Rng rng = new(settings.seed + 42 * (((width / 2) * ey + (ex >> 1)) & 0xffff));
			rng.Skip(4);
			int best, second, third;
			if (s[0] >= s[1] && s[1] >= s[2]) {
				(best, second, third) = (0, 1, 2);
			} else if (s[0] >= s[2] && s[2] >= s[1]) {
				(best, second, third) = (0, 2, 1);
			} else if (s[1] >= s[0] && s[0] >= s[2]) {
				(best, second, third) = (1, 0, 2);
			} else if (s[1] >= s[2] && s[2] >= s[0]) {
				(best, second, third) = (1, 2, 0);
			} else if (s[2] >= s[0] && s[0] >= s[1]) {
				(best, second, third) = (2, 0, 1);
			} else {
				(best, second, third) = (2, 1, 0);
			}
			int count = 1;
			if (sinceFork >= 2) {
				if (s[best] - s[second] > s[best] >> 3) {
					if (s[second] >= 0) {
						count = rng.Rand(2) + 1;
					}
				} else {
					count = 2;
					if (s[third] >= 0) {
						count = rng.Rand(2) + 2;
					}
				}
			}
			bool any = false;
			int[] ways = { best, second, third };
			for (int j = 0; j < count; ++j) {
				int d, t, st;
				switch (ways[j]) {
					case 0:
						(d, t, st) = ((dir + 3) & 3, turns - 1, 0);
						break;
					case 1:
						(d, t, st) = (dir, turns, straight + 1);
						break;
					default:
						(d, t, st) = ((dir + 1) & 3, turns + 1, 0);
						break;
				}
				if (TraceRiver(ex + RiverDx[d], ey + RiverDy[d], d, length + 1, count == 2 ? 0 : sinceFork + 1, t, st)) {
					any = true;
				}
			}
			return any;
		}

		// Civ3 0x600f60: whether a river goes through the corner of tile (x,
		// y) towards (nx, ny), along edges on both sides of it.
		private bool RiverThroughCorner(int x, int y, int nx, int ny) {
			int ddx = nx - x, ddy = ny - y;
			if (ddx > width / 2) {
				ddx -= width;
			} else if (ddx < -(width / 2)) {
				ddx += width;
			}
			if (ddy > height / 2) {
				ddy -= height;
			} else if (ddy < -(height / 2)) {
				ddy += height;
			}
			int k = -1;
			for (int j = 0; j < 9; ++j) {
				if (spiral[j] == (ddx, ddy)) {
					k = j;
					break;
				}
			}
			int b1 = (k + 7) & 7, b2 = (k + 1) & 7;
			int self = Index(x, y);
			bool one = false, two = false;
			if (Neighbor(x, y, b1, out int ax, out int ay)) {
				one = (river[self] & (1 << b1)) != 0 || (river[Index(ax, ay)] & (1 << b2)) != 0;
			}
			if (Neighbor(x, y, b2, out int bx, out int by)) {
				two = (river[self] & (1 << b2)) != 0 || (river[Index(bx, by)] & (1 << b1)) != 0;
			}
			return one && two;
		}

		// --- Fixes for the terrain art, Civ3 0x5fb7a0 and 0x5fbb90 ---

		// Tundra has no art beside plains or desert, so such tundra becomes
		// grassland (a forest staying a forest); and sea or ocean beside
		// land becomes coast.
		private void FixTundraAndShallows() {
			for (int i = 0; i < tileCount; ++i) {
				int x = X(i), y = Y(i);
				if (baseTerrain[i] == Tundra) {
					for (int k = 1; k < 9; ++k) {
						if (!Neighbor(x, y, k, out int nx, out int ny)) {
							continue;
						}
						int n = Index(nx, ny);
						if (IsWaterAt(n) || baseTerrain[n] == Tundra || baseTerrain[n] == Grassland) {
							continue;
						}
						bool forest = terrain[i] == Forest;
						SetTerrain(i, Grassland);
						if (forest) {
							SetTerrain(i, Forest);
						}
						break;
					}
				} else if (baseTerrain[i] == Ocean || baseTerrain[i] == Sea) {
					for (int k = 1; k < 9; ++k) {
						if (Neighbor(x, y, k, out int nx, out int ny) && !IsWaterAt(Index(nx, ny))) {
							SetTerrain(i, Coast);
							break;
						}
					}
				}
			}
		}

		// Civ3 0x5fbb90: the art has no corner where four different base
		// terrains meet, so where four meet at a tile's top corner the tile
		// takes the base terrain of the tile west or east of that corner
		// (whichever is land or water as it is), or else of the one north
		// of it. (The rest of 0x5fbb90 picks each tile's piece of terrain
		// art, which OpenCiv3 does in TerrainTextureFiles.)
		private void FixVertices() {
			bool changedWater = false;
			int[] counts = new int[14];
			for (int i = 0; i < tileCount; ++i) {
				int x = X(i), y = Y(i);
				Array.Clear(counts);
				for (int u = 0; u < 4; ++u) {
					counts[Offset(x, y, u / 2 - (u & 1), (u + 1) / 2 - 2, out int ux, out int uy) ? baseTerrain[Index(ux, uy)] : Coast]++;
				}
				int distinct = 0;
				foreach (int c in counts) {
					if (c != 0) {
						++distinct;
					}
				}
				if (distinct < 4) {
					continue;
				}
				int side = (y & 1) != 0 ? 1 : -1;
				if (Offset(x, y, side, -1, out int wx, out int wy) && IsWaterAt(Index(wx, wy)) == IsWaterAt(i)) {
					SetTerrain(i, baseTerrain[Index(wx, wy)]);
				} else if (Offset(x, y, -side, -1, out int ex, out int ey) && IsWaterAt(Index(ex, ey)) == IsWaterAt(i)) {
					SetTerrain(i, baseTerrain[Index(ex, ey)]);
				} else if (Offset(x, y, 0, -2, out int nx, out int ny)) {
					SetTerrain(i, baseTerrain[Index(nx, ny)]);
					changedWater = true;
					if (Offset(x, y, 0, 2, out int sx, out int sy) && IsWaterAt(Index(sx, sy))) {
						SetTerrain(Index(sx, sy), Coast);
					}
				}
			}
			if (changedWater) {
				FindBodies();
			}
		}

		// --- Resources, Civ3 0x6020d0 ---

		// The landmass each luxury was put on, by good (-1 if none).
		public int[] luxuryLandmass;

		// How many of a good a map has: the number of civs times its
		// appearance ratio (random 50..100 if 0), less if few terrains may
		// have it; a water terrain counts as five.
		private int GoodTarget(ref Rng rng, int g) {
			Good good = rules.goods[g];
			int ratio = good.appearanceRatio;
			if (ratio == 0) {
				int a = rng.Rand(26);
				ratio = a + rng.Rand(26) + 50;
			}
			int target = settings.numberOfCivs * ratio / 100;
			int w = TerrainWeight(g);
			if (w == 0) {
				return 0;
			}
			int least = 1;
			if (w < 2) {
				target = (int)(target * 0.5f);
			} else if (w < 4) {
				target = (int)(target * 0.75f);
			} else {
				least = 2;
			}
			return Math.Max(least, target);
		}

		private int TerrainWeight(int g) {
			int w = 0;
			for (int t = 0; t < 14; ++t) {
				if (rules.goods[g].allowedOn[t]) {
					w += t >= Coast ? 5 : 1;
				}
			}
			return w;
		}

		private void AddResources() {
			int n = rules.goods.Count;
			luxuryLandmass = new int[n];
			Array.Fill(luxuryLandmass, -1);
			Rng rng = new(settings.seed + 0x180E3);
			int[] order = Shuffle(ref rng, tileCount);
			int early = 2 * tileCount / 3;

			// Luxuries, each on one landmass, in clusters.
			for (int g = 0; g < n; ++g) {
				if (rules.goods[g].type != 1) {
					continue;
				}
				int target = GoodTarget(ref rng, g);
				for (int copies = 0; copies < target; ++copies) {
					int c = 0;
					while (c < tileCount) {
						int t = order[c++];
						if (luxuryLandmass[g] != -1 && body[t] != luxuryLandmass[g]) {
							continue;
						}
						if (!CanPlaceResource(X(t), Y(t), g, c < early)) {
							continue;
						}
						luxuryLandmass[g] = body[t];
						resource[t] = g;
						int x0 = X(t), y0 = Y(t);
						while (copies < target && rng.Rand(2) == 0) {
							for (int k = 1; k < 9; ++k) {
								if (!Neighbor(x0, y0, k, out int nx, out int ny)) {
									continue;
								}
								int same = 0;
								for (int k2 = 1; k2 < 9; ++k2) {
									if (Neighbor(nx, ny, k2, out int mx, out int my) && resource[Index(mx, my)] == g) {
										++same;
									}
								}
								if (same < 3 && CanPlaceResource(nx, ny, g, c < early)) {
									resource[Index(nx, ny)] = g;
									++copies;
									break;
								}
							}
						}
						break;
					}
				}
			}

			// Strategic resources, each copy on the first tile that takes it.
			int placed = 0;
			IEnumerable<int> strategic = Enumerable.Range(0, n).Where(g => rules.goods[g].type == 2);
			foreach (int g in strategic.Where(g => rules.goods[g].placedFirst).Concat(strategic.Where(g => !rules.goods[g].placedFirst))) {
				int target = GoodTarget(ref rng, g);
				for (int copies = 0; copies < target; ++copies) {
					if (PlaceOnFirst(order, g)) {
						++placed;
					}
				}
			}

			// Bonus resources, a round at a time: each has a chance in each
			// round, the better the more terrains may have it, until a round
			// places none or there are a resource for every 32 tiles.
			while (n > 0) {
				int before = placed;
				for (int g = 0; g < n; ++g) {
					if (rules.goods[g].type == 1 || rules.goods[g].type == 2) {
						continue;
					}
					int w = TerrainWeight(g);
					if (w == 0) {
						continue;
					}
					if (rng.Rand(w < 2 ? 6 : w < 4 ? 4 : 2) <= 1 && PlaceOnFirst(order, g)) {
						++placed;
					}
				}
				if (placed == before || placed >= tileCount >> 5) {
					break;
				}
			}
		}

		private bool PlaceOnFirst(int[] order, int g) {
			foreach (int t in order) {
				if (CanPlaceResource(X(t), Y(t), g, true)) {
					resource[t] = g;
					return true;
				}
			}
			return false;
		}

		// Map vt+0x44, Civ3 0x6031b0: whether good g may go on (x, y). A
		// luxury needs a landmass of 75 tiles (37 late in the search). The
		// terrain must allow it, the tile be free and not bonus grassland,
		// and on the same body: next to it no other resource (nor the same
		// strategic one); further out, to r+2 rings, no other luxury near a
		// luxury and no copy of a strategic one. A water tile needs land
		// within two tiles.
		public bool CanPlaceResource(int x, int y, int g, bool early) {
			int i = Index(x, y);
			Good good = rules.goods[g];
			if (good.type == 1 && bodies[body[i]].size < (early ? 75 : 37)) {
				return false;
			}
			if (!good.allowedOn[terrain[i]] || resource[i] != -1 || (flags[i] & BonusGrasslandFlag) != 0) {
				return false;
			}
			int K = 9;
			if (good.type == 1 || good.type == 2) {
				int r = Math.Min(4, (width + height) / 2 / 50);
				K = (2 * r + 5) * (2 * r + 5);
			}
			for (int k = 1; k < K; ++k) {
				if (!Neighbor(x, y, k, out int nx, out int ny)) {
					continue;
				}
				int m = Index(nx, ny);
				int other = resource[m];
				if (body[m] != body[i] || other == -1) {
					continue;
				}
				bool blocks;
				if (k < 9) {
					blocks = other != g || good.type == 2;
				} else if (good.type == 2) {
					blocks = other == g;
				} else {
					blocks = other != g && rules.goods[other].type == 1;
				}
				if (blocks) {
					return false;
				}
			}
			if (!IsWaterAt(i)) {
				return true;
			}
			for (int k = 1; k < 21; ++k) {
				if (Neighbor(x, y, k, out int nx, out int ny) && !IsWaterAt(Index(nx, ny))) {
					return true;
				}
			}
			return false;
		}

		// --- Goody huts, Civ3 0x601fd0 ---

		// One try for every 32 tiles at a random tile, which gets a hut if it
		// is land with no resource and no hut within three tiles (Civ3
		// 0x602b30). None without barbarians.
		private void AddGoodyHuts() {
			if (settings.barbarians == -1) {
				return;
			}
			Rng rng = new(settings.seed + 0x8ACE);
			for (int c = 0; c < tileCount >> 5; ++c) {
				int t = rng.Rand(tileCount);
				if (CanPlaceHut(X(t), Y(t))) {
					hut[t] = true;
				}
			}
		}

		public bool CanPlaceHut(int x, int y) {
			int i = Index(x, y);
			if (IsWaterAt(i) || (flags[i] & StartFlag) != 0 || resource[i] != -1) {
				return false;
			}
			for (int k = 0; k < 49; ++k) {
				if (Neighbor(x, y, k, out int nx, out int ny) && hut[Index(nx, ny)]) {
					return false;
				}
			}
			return true;
		}

		// --- Bonus grassland, Civ3 0x601ea0 ---

		// A third of the grassland (by base, so under forests and hills too)
		// with no resource gets a bonus shield.
		private void AddBonusGrassland() {
			Rng rng = new(settings.seed + 0x8CF78);
			int[] order = Shuffle(ref rng, tileCount);
			foreach (int t in order) {
				if (baseTerrain[t] == Grassland && resource[t] == -1 && rng.Rand(3) == 0) {
					flags[t] |= BonusGrasslandFlag;
				}
			}
		}

		// --- Starting locations, Civ3 0x5fe9c0 ---

		// Each player slot's start (a tile index, or -1); slot 0 is the
		// barbarians' (map +0x16c).
		public readonly int[] startSlots = new int[32];

		// The number of starts found.
		public int startCount;

		private void FindStartingLocations() {
			int civs = settings.numberOfCivs;
			int[] luxuries = new int[bodies.Count], starts = new int[bodies.Count];
			int luxuryTypes = 0;
			for (int g = 0; g < rules.goods.Count; ++g) {
				if (rules.goods[g].type != 1) {
					continue;
				}
				for (int i = 0; i < tileCount; ++i) {
					if (resource[i] == g) {
						++luxuries[body[i]];
						++luxuryTypes;
						break;
					}
				}
			}
			Rng rng = new(settings.seed + 0x16062);
			for (int s = 0; s < 32; ++s) {
				startSlots[s] = -1;
				rng.Next();
			}
			int[] order = Shuffle(ref rng, tileCount);
			int[] score = new int[tileCount];
			for (int k = 0; k < tileCount; ++k) {
				score[k] = CitySiteScore(X(order[k]), Y(order[k]));
			}
			SortByScore(order, score);

			int next = 1, slot = 1, placed = 0;
			for (int pass = 0; pass < 8; ++pass) {
				for (int k = 0; next <= civs && k < tileCount; ++k) {
					int t = order[k];
					int x = X(t), y = Y(t);
					if (pass < 4 && placed < settings.coastalStarts) {
						int sea = LargestWaterBeside(x, y);
						if (sea == -1 || bodies[sea].size <= 20) {
							continue;
						}
					}
					if (y == 0 || y >= height - 1) {
						continue;
					}
					if ((flags[t] & StartFlag) != 0 || IsWaterAt(t) || hut[t]) {
						continue;
					}
					int b = body[t];
					if (pass < 1 && next <= luxuryTypes && luxuries[b] <= starts[b]) {
						continue;
					}
					int size = bodies[b].size;
					if (size < 75 && !(pass >= 2 && size >= 37) && pass < 5) {
						continue;
					}
					if (pass < 7 && score[k] <= 0) {
						continue;
					}
					bool apart = true;
					int distance = pass < 3 ? settings.civDistance : settings.civDistance / 2;
					for (int s = 1; s <= civs && apart; ++s) {
						int o = startSlots[s];
						if (o == -1 || body[o] != b) {
							continue;
						}
						int dx = Math.Abs(x - X(o));
						if (settings.wrapX && dx > width / 2) {
							dx = width - dx;
						}
						int dy = Math.Abs(y - Y(o));
						if (settings.wrapY && dy > height / 2) {
							dy = height - dy;
						}
						int far = Math.Max(dx, dy) - ((dx + dy) / 2 - Math.Min(dx, dy) + 1) / 2;
						apart = far > distance;
					}
					if (pass < 6 && !apart) {
						continue;
					}
					startSlots[slot] = t;
					flags[t] |= StartFlag;
					++starts[b];
					++next;
					++placed;
					while (slot <= civs && startSlots[slot] != -1) {
						++slot;
					}
				}
			}
			startCount = next - 1;

			if (settings.shuffleStarts && next > 1) {
				// The human player (slot 1) gets a start two thirds of the way
				// down the ranking; the others are shuffled.
				for (int i = 1; i < next; ++i) {
					int j = i == 1 ? (2 * next - 2) / 3 + 1 : rng.Rand(next - i) + i;
					(startSlots[i], startSlots[j]) = (startSlots[j], startSlots[i]);
				}
				// Civs on one landmass come one after another.
				for (int a = 2; a - 1 < next; ++a) {
					int reference = body[startSlots[a - 1]];
					for (int b = a; b < next; ++b) {
						if (body[startSlots[b]] == reference) {
							continue;
						}
						for (int c = b + 1; c < next; ++c) {
							if (body[startSlots[c]] == reference) {
								(startSlots[b], startSlots[c]) = (startSlots[c], startSlots[b]);
								break;
							}
						}
					}
				}
			}
		}

		// What Civ3's single-player setup does next (Civ3 0x5aec10, in the
		// game start): each seafaring civ, in slot order, whose start is not
		// by a sea of more than 20 tiles takes the first slot's start that is
		// and that no seafaring civ has taken yet.
		public void GiveSeafaringCivsCoastalStarts(Func<int, bool> seafaring, int players) {
			bool[] coastal = new bool[32];
			for (int s = 1; s < 32; ++s) {
				int t = startSlots[s];
				if (t >= 0) {
					int sea = LargestWaterBeside(X(t), Y(t));
					coastal[s] = sea != -1 && bodies[sea].size > 20;
				}
			}
			for (int s = 1; s <= players; ++s) {
				if (!seafaring(s) || startSlots[s] < 0) {
					continue;
				}
				if (coastal[s]) {
					coastal[s] = false;
					continue;
				}
				for (int q = 1; q <= players; ++q) {
					if (coastal[q]) {
						(startSlots[s], startSlots[q]) = (startSlots[q], startSlots[s]);
						coastal[q] = false;
						break;
					}
				}
			}
		}

		// Civ3 0x5fe8a0: the largest body of water beside a tile, or -1.
		public int LargestWaterBeside(int x, int y) {
			int best = -1;
			for (int k = 1; k < 9; ++k) {
				if (!Neighbor(x, y, k, out int nx, out int ny)) {
					continue;
				}
				int n = Index(nx, ny);
				if (!IsWaterAt(n) || body[n] == best) {
					continue;
				}
				if (best == -1 || bodies[body[n]].size > bodies[best].size) {
					best = body[n];
				}
			}
			return best;
		}

		// Civ3 0x5dcda0: sorts the tiles by score, highest first, the way Civ3
		// does: for each score from the lowest up, its tiles are swapped to
		// the end of what is left.
		private static void SortByScore(int[] keys, int[] values) {
			SortedSet<int> distinct = new(values);
			int last = keys.Length - 1;
			foreach (int v in distinct) {
				int i = 0;
				while (i <= last) {
					if (values[i] == v) {
						if (i != last) {
							(values[i], values[last]) = (values[last], values[i]);
							(keys[i], keys[last]) = (keys[last], keys[i]);
						}
						--last;
					} else {
						++i;
					}
				}
			}
		}

		// Map vt+0x58, Civ3 0x603680: a river within the first k tiles of the
		// spiral.
		public bool RiverWithin(int x, int y, int k) {
			for (int j = 0; j < k; ++j) {
				if (Neighbor(x, y, j, out int nx, out int ny) && river[Index(nx, ny)] != 0) {
					return true;
				}
			}
			return false;
		}

		// Civ3 0x444250 (the AI's city site value) as the map generator asks
		// it, for a start (Map vt+8, 0x5e2bd0): with no cities, borders or
		// players yet. 0 where a city can't be; otherwise a million plus the
		// tiles and resources of the 21 tiles around, divided down for a
		// start by the sea, without a luxury, among jungle, without two good
		// tiles beside it, without fresh water, and on jungle, desert or
		// tundra.
		public int CitySiteScore(int x, int y) {
			int center = Index(x, y);
			if (!rules.allowCities[terrain[center]]) {
				return 0;
			}
			int freshWater = HasFreshWater(x, y) ? 4 : 0;
			int resources = 0, luxuries = 0, rivers = 0;
			int best = 0, foodAndTrade = 0, good = 0, fair = 0, barren = 0;
			for (int k = 0; k < 21; ++k) {
				if (!Neighbor(x, y, k, out int nx, out int ny)) {
					continue;
				}
				int n = Index(nx, ny);
				int r = resource[n];
				Good goodHere = r == -1 ? null : rules.goods[r];
				if (goodHere != null) {
					if (goodHere.type == 0) {
						resources += 1;
					} else {
						resources += k < 9 ? 4 : 2;
						if (goodHere.type == 1) {
							++luxuries;
						}
					}
				}
				if (river[n] != 0) {
					++rivers;
				}
				int t = k == 0 && rules.workerJob[terrain[n]] ? baseTerrain[n] : terrain[n];
				int food;
				if (k < 1) {
					food = 2;
				} else {
					food = rules.food[t] + (goodHere?.food ?? 0) + rules.irrigation[t] + (IsWaterAt(n) ? 1 : 0);
				}
				int shields = rules.shields[t] + (goodHere?.shields ?? 0) + rules.mining[t];
				if (IsWaterAt(n) && bodies[body[n]].size > 20) {
					++shields;
				}
				if (terrain[n] == Grassland && (flags[n] & BonusGrasslandFlag) != 0) {
					++shields;
				}
				int trade = rules.commerce[t] + (goodHere?.commerce ?? 0) + rules.road[t];
				if (food > 2 && shields > 0) {
					++best;
				} else if (food > 2 && trade > 0) {
					++foodAndTrade;
				} else if (food > 1 && (shields > 0 || trade > 0)) {
					++good;
				} else if (food > 0 && (shields > 0 || trade > 0)) {
					++fair;
				} else if (food == 0) {
					++barren;
				}
			}
			int score = fair / 3 + 1000000 + best * 2 + good / 2 - barren + foodAndTrade + luxuries + resources + freshWater + rivers;

			if (rules.irrigation[terrain[center]] == 0) {
				return 0;
			}
			int land = 0;
			for (int k = 1; k < 9; ++k) {
				if (Neighbor(x, y, k, out int nx, out int ny) && !IsWaterAt(Index(nx, ny))) {
					++land;
				}
			}
			score /= 9 - land;
			bool luxuryNear = false;
			for (int k = 0; k < 45 && !luxuryNear; ++k) {
				if (Neighbor(x, y, k, out int nx, out int ny)) {
					int r = resource[Index(nx, ny)];
					luxuryNear = r != -1 && rules.goods[r].type == 1;
				}
			}
			if (!luxuryNear) {
				score /= 16;
			}
			int jungles = 0;
			for (int k = 1; k < 21; ++k) {
				if (Neighbor(x, y, k, out int nx, out int ny) && terrain[Index(nx, ny)] == Jungle) {
					++jungles;
				}
			}
			score /= jungles * 32 / 20 + 1;
			int fed = 0, worked = 0;
			for (int k = 1; k < 9; ++k) {
				if (!Neighbor(x, y, k, out int nx, out int ny)) {
					continue;
				}
				int n = Index(nx, ny);
				int r = resource[n];
				Good known = r != -1 && !rules.goods[r].needsAdvance ? rules.goods[r] : null;
				int food = rules.food[terrain[n]] + (IsWaterAt(n) && bodies[body[n]].size <= 20 ? 1 : 0) + (known?.food ?? 0);
				if (food <= 1) {
					continue;
				}
				++fed;
				if (rules.shields[terrain[n]] > 0 || (terrain[n] == Grassland && (flags[n] & BonusGrasslandFlag) != 0) || (known != null && known.shields > 0)) {
					++worked;
				}
			}
			if (fed == 0) {
				return 0;
			}
			if (fed == 1) {
				score /= 64;
			}
			if (worked < 2) {
				score /= 128;
			}
			if (!HasFreshWater(x, y)) {
				score /= RiverWithin(x, y, 45) || NearSmallWater(x, y, 77) ? 256 : 512;
			}
			switch (terrain[center]) {
				case Jungle:
					score /= 1024;
					break;
				case Desert:
					score /= 2048;
					break;
				case Tundra:
					score /= 4096;
					break;
			}
			return Math.Max(1, score);
		}

		// --- Coast, sea and ocean, Civ3 0x5fe5d0 ---

		private void SetWaterDepth() {
			for (int i = 0; i < tileCount; ++i) {
				if (!IsWaterAt(i)) {
					continue;
				}
				int x = X(i), y = Y(i);
				for (int k = 1; k < 9; ++k) {
					if (Neighbor(x, y, k, out int nx, out int ny) && !IsWaterAt(Index(nx, ny))) {
						SetTerrain(i, Coast);
						break;
					}
				}
			}
			for (int i = 0; i < tileCount; ++i) {
				if (terrain[i] != Ocean) {
					continue;
				}
				int x = X(i), y = Y(i);
				for (int k = 1; k < 9; ++k) {
					if (Neighbor(x, y, k, out int nx, out int ny) && terrain[Index(nx, ny)] == Coast) {
						if ((flags[i] & KeepFlag) == 0) {
							SetTerrain(i, Sea);
						}
						break;
					}
				}
			}
		}
	}
}
