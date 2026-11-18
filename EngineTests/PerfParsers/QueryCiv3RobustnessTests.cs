using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using EngineTests.Utils;
using QueryCiv3;
using QueryCiv3.Biq;
using Xunit;

namespace EngineTests.PerfParsers;

// Malformed, truncated and unsupported BIQ and SAV files must fail with a clear exception, without reading out of bounds
public class QueryCiv3RobustnessTests {
	private const int SECTION_HEADERS_START = 736;

	// A BIQ file header, followed by the given sections
	private static byte[] Biq(string type, int majorVersion, int minorVersion, params byte[][] sections) {
		byte[] header = new byte[SECTION_HEADERS_START];
		for (int i = 0; i < 4; i++) header[i] = (byte)type[i];
		BitConverter.GetBytes(majorVersion).CopyTo(header, 24);
		BitConverter.GetBytes(minorVersion).CopyTo(header, 28);
		List<byte> bytes = new(header);
		foreach (byte[] section in sections) bytes.AddRange(section);
		return bytes.ToArray();
	}

	// A section header followed by records, each of which is prefixed with its length
	private static byte[] Section(string name, params byte[][] records) {
		List<byte> bytes = new();
		bytes.AddRange(name.Select(c => (byte)c));
		bytes.AddRange(BitConverter.GetBytes(records.Length));
		foreach (byte[] record in records) {
			bytes.AddRange(BitConverter.GetBytes(record.Length));
			bytes.AddRange(record);
		}
		return bytes.ToArray();
	}

	private static byte[] Filled(int length, byte value) => Enumerable.Repeat(value, length).ToArray();

	[Theory]
	[InlineData("BIC ", 2, FileVersion.Vanilla)]
	[InlineData("BICX", 11, FileVersion.PlayTheWorld)]
	[InlineData("BICX", 12, FileVersion.Conquests)]
	[InlineData("BICQ", 12, FileVersion.Conquests)]
	[InlineData("BICZ", 12, FileVersion.Unknown)]
	public void BiqVersionsAreRecognized(string type, int majorVersion, FileVersion expected) {
		Civ3File file = new(Biq(type, majorVersion, 0));
		Assert.Equal(expected, file.Civ3Version.FileVersion);
		Assert.Equal(majorVersion, file.Civ3Version.MajorVersion);
	}

	[Fact]
	public void SavAndOtherFilesHaveAnUnknownBiqVersion() {
		byte[] sav = new byte[100];
		"CIV3"u8.CopyTo(sav);
		Assert.Equal(FileVersion.Unknown, new Civ3File(sav).Civ3Version.FileVersion);
		Assert.Equal(FileVersion.Unknown, new Civ3File(new byte[100]).Civ3Version.FileVersion);
		// Too short to have a version
		Civ3File shortFile = new("BICX12"u8.ToArray());
		Assert.Equal(-1, shortFile.Civ3Version.MajorVersion);
	}

	[Theory]
	[InlineData("BIC ", 1)]
	[InlineData("BICX", 1)]
	[InlineData("CIV3", 12)]
	[InlineData("ABCD", 12)]
	public void UnsupportedBiqVersionsAreRejected(string type, int majorVersion) {
		byte[] biq = Biq(type, majorVersion, 0, Section("GOOD", new byte[88]));
		Assert.Throws<NotSupportedException>(() => new BiqData(biq));
	}

	// Civ3's and Play the World's files (.bic, .bix) are read as Conquests
	// records with the newer fields zeroed (see BiqData.MIN_LEGACY_MAJOR_VERSION).
	[Theory]
	[InlineData("BIC ", 2, true)]
	[InlineData("BIC ", 4, true)]
	[InlineData("BICX", 11, true)]
	[InlineData("BICQ", 12, false)]
	public void OlderBiqVersionsAreReadAsLegacy(string type, int majorVersion, bool legacy) {
		byte[] biq = Biq(type, majorVersion, 0, Section("GOOD", new byte[88]));
		BiqData data = new(biq);
		Assert.Equal(legacy, data.IsLegacy);
		Assert.Single(data.Good);
	}

	[Fact]
	public void StaticRecordsUseTheirOwnLength() {
		// A short record, one of exactly the struct size, and a long one; each is read up to its own length
		int size = Unsafe.SizeOf<GOOD>() - 4;
		byte[] good = Biq("BICQ", 12, 8, Section("GOOD", Filled(size - 20, 1), Filled(size, 2), Filled(size + 30, 3)), Section("GOOD", Filled(size, 4)));
		BiqData data = new(good);
		// The second GOOD section replaces the first, which shows the records were stepped over correctly
		Assert.Single(data.Good);
		Assert.Equal(size, data.Good[0].Length);

		good = Biq("BICQ", 12, 8, Section("GOOD", Filled(size - 20, 1), Filled(size, 2), Filled(size + 30, 3)));
		data = new(good);
		Assert.Equal(3, data.Good.Length);
		byte[][] records = data.Good.Select(g => MemoryMarshal.AsBytes(new ReadOnlySpan<GOOD>(ref g)).ToArray()).ToArray();
		Assert.Equal(Filled(size - 20, 1).Concat(new byte[20]), records[0].Skip(4));
		Assert.Equal(Filled(size, 2), records[1].Skip(4));
		Assert.Equal(Filled(size, 3), records[2].Skip(4));
	}

	[Fact]
	public void MalformedBiqsAreRejected() {
		int size = Unsafe.SizeOf<GOOD>() - 4;
		byte[] valid = Biq("BICQ", 12, 8, Section("GOOD", Filled(size, 2), Filled(size, 3)));
		new BiqData(valid);

		// Truncated anywhere (except right after the header, which leaves a valid BIQ without sections)
		for (int length = 4; length < valid.Length; length++) {
			if (length == SECTION_HEADERS_START) continue;
			byte[] truncated = valid.AsSpan(0, length).ToArray();
			Exception e = Record.Exception(() => new BiqData(truncated));
			Assert.True(e is InvalidDataException || e is NotSupportedException, $"length {length}: {e}");
		}

		// Record lengths and counts that don't fit
		foreach (int badValue in new[] { -1, -100, int.MinValue, int.MaxValue, int.MaxValue - 3, 100_000 }) {
			byte[] badLength = (byte[])valid.Clone();
			BitConverter.GetBytes(badValue).CopyTo(badLength, SECTION_HEADERS_START + 8);
			Assert.Throws<InvalidDataException>(() => new BiqData(badLength));
			byte[] badCount = (byte[])valid.Clone();
			BitConverter.GetBytes(badValue).CopyTo(badCount, SECTION_HEADERS_START + 4);
			Assert.Throws<InvalidDataException>(() => new BiqData(badCount));
		}

		// An unknown section
		Assert.Throws<InvalidDataException>(() => new BiqData(Biq("BICQ", 12, 8, Section("ABCD"))));
		// RACE needs ERAS first
		Assert.Throws<InvalidDataException>(() => new BiqData(Biq("BICQ", 12, 8, Section("RACE", new byte[400]))));
	}

	[Fact]
	public void DynamicRecordsAreBoundedByTheirLength() {
		// A WMAP record with room for two resources
		byte[] record = new byte[BiqData.WMAP_LEN_1 - 4 + 2 * 4 + BiqData.WMAP_LEN_2];
		BitConverter.GetBytes(2).CopyTo(record, 0);
		Assert.Equal(2, new BiqData(Biq("BICQ", 12, 8, Section("WMAP", record))).WmapResource[0].Length);
		// More resources reach into the last fixed-size part, which is then short: that's allowed, the rest is zeroed
		BitConverter.GetBytes(3).CopyTo(record, 0);
		Assert.Equal(3, new BiqData(Biq("BICQ", 12, 8, Section("WMAP", record))).WmapResource[0].Length);
		// But the resources themselves must fit in the record
		BitConverter.GetBytes(1000).CopyTo(record, 0);
		Assert.Throws<InvalidDataException>(() => new BiqData(Biq("BICQ", 12, 8, Section("WMAP", record))));
		BitConverter.GetBytes(-1).CopyTo(record, 0);
		Assert.Throws<InvalidDataException>(() => new BiqData(Biq("BICQ", 12, 8, Section("WMAP", record))));
	}

	[Fact]
	public void CityNameIsLimitedToItsField() {
		byte[] bytes = new byte[Unsafe.SizeOf<QueryCiv3.Biq.CITY>()];
		"ABCDEFGHIJKLMNOPQRSTUVWX"u8.CopyTo(bytes.AsSpan(6)); // 24 characters, no terminator
		BitConverter.GetBytes(0x41414141).CopyTo(bytes, 30); // OwnerType, right after the name
		QueryCiv3.Biq.CITY city = MemoryMarshal.Read<QueryCiv3.Biq.CITY>(bytes);
		Assert.Equal("ABCDEFGHIJKLMNOPQRSTUVWX", city.Name);
	}

	[Fact]
	public void ReadsOutsideTheFileAreRejected() {
		byte[] bytes = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();
		Civ3File file = new(bytes);
		Civ3File embedded = new(bytes, 8, 40);
		Assert.Equal(BitConverter.ToInt32(bytes, 60), file.ReadInt32(60));
		Assert.Equal(BitConverter.ToInt16(bytes, 62), file.ReadInt16(62));
		Assert.Equal(BitConverter.ToInt32(bytes, 44), embedded.ReadInt32(36));
		foreach (Civ3File f in new[] { file, embedded }) {
			Assert.Throws<ArgumentOutOfRangeException>(() => f.ReadInt32(f.Length - 3));
			Assert.Throws<ArgumentOutOfRangeException>(() => f.ReadInt16(f.Length - 1));
			Assert.Throws<ArgumentOutOfRangeException>(() => f.ReadInt32(-1));
			Assert.Throws<ArgumentOutOfRangeException>(() => f.ReadInt32(int.MaxValue));
			Assert.Throws<ArgumentOutOfRangeException>(() => f.ReadInt32(int.MinValue));
			// Lengths that would overflow are clamped to the end of the file like any others
			Assert.Equal(f.Length - 10, f.GetBytes(10, int.MaxValue).Length);
			Assert.Empty(f.GetBytes(int.MaxValue, int.MaxValue));
			Assert.Empty(f.GetBytes(f.Length, 10));
		}
	}

	private static string TempFile(string extension) => Path.Combine(Path.GetTempPath(), "c7-fix-parsers-" + Guid.NewGuid() + extension);

	[Fact]
	public void ReadFileRejectsTinyFiles() {
		string path = TempFile(".biq");
		try {
			foreach (byte[] contents in new[] { new byte[0], new byte[] { 0 } }) {
				File.WriteAllBytes(path, contents);
				Assert.Throws<InvalidDataException>(() => Util.ReadFile(path));
			}
		} finally {
			File.Delete(path);
		}
	}

	[Fact]
	public void ReadFileOnlyCachesSettledFiles() {
		Random random = new(5);
		BlastTestEncoder encoder = new();
		byte[] expected = encoder.RandomStream(random, false, 4, 20_000);
		string path = TempFile(".biq");
		try {
			File.WriteAllBytes(path, encoder.ToArray());

			// Just written, so not cached
			Assert.NotSame(Util.ReadFileShared(path), Util.ReadFileShared(path));

			File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-5));
			byte[] shared = Util.ReadFileShared(path);
			Assert.Equal(expected, shared);
			Assert.Same(shared, Util.ReadFileShared(path));
			Assert.True(Util.ReadFileReadOnly(path).Span.SequenceEqual(expected));
			// ReadFile always returns a copy the caller owns
			byte[] copy = Util.ReadFile(path);
			Assert.Equal(expected, copy);
			Assert.NotSame(shared, copy);
			Array.Clear(copy);
			Assert.Equal(expected, Util.ReadFileShared(path));

			// A replaced file is read again, even through a cache hit's FileInfo
			encoder = new BlastTestEncoder();
			byte[] changed = encoder.RandomStream(random, false, 5, 1000);
			File.WriteAllBytes(path, encoder.ToArray());
			File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-4));
			Assert.Equal(changed, Util.ReadFile(path));
			Assert.Equal(changed, Util.ReadFileShared(path));
		} finally {
			File.Delete(path);
		}
	}

	private static IEnumerable<string> Civ3Files(params string[] extensions) {
		return Directory.EnumerateFiles(Civ3Location.GetCiv3Path(), "*", SearchOption.AllDirectories)
			.Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
			.OrderBy(f => f, StringComparer.Ordinal);
	}

	[SkippableFact]
	public void Civ3BiqsLoad() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), "No Civ3 install found.");

		int loaded = 0;
		foreach (string path in Civ3Files(".biq", ".bic", ".bix")) {
			byte[] bytes = Util.ReadFile(path);
			// Every Civ3, Play the World and Conquests file is read (some
			// have only a map, or only a few of the rules).
			Assert.NotNull(new BiqData(bytes));
			loaded++;
		}
		Assert.True(loaded > 0);
	}

	[SkippableFact]
	public void TruncatedConquestsBiqIsRejected() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), "No Civ3 install found.");

		byte[] biq = Util.ReadFile(PathUtils.defaultBicPath);
		Random random = new(6);
		for (int i = 0; i < 200; i++) {
			int length = i < 50 ? SECTION_HEADERS_START + 1 + i * 7 : random.Next(SECTION_HEADERS_START, biq.Length);
			Assert.Throws<InvalidDataException>(() => new BiqData(biq, 0, length));
		}
	}

	private static string SavPath() {
		return Civ3Files(".sav").FirstOrDefault();
	}

	private static byte[] Bytes<T>(T[] array) where T : unmanaged => array == null ? null : MemoryMarshal.AsBytes(array.AsSpan()).ToArray();

	[SkippableFact]
	public void SavCanBeLoadedAgain() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), "No Civ3 install found.");
		string[] savs = Civ3Files(".sav").Take(2).ToArray();
		Skip.If(savs.Length < 2, "No Civ3 SAV files found.");

		byte[] biq = Util.ReadFile(PathUtils.defaultBicPath);
		byte[] first = Util.ReadFile(savs[0]);
		byte[] second = Util.ReadFile(savs[1]);
		SavData expected = new(second, biq);

		SavData data = new(first, biq);
		data.Load(second);
		Assert.Equal(Bytes(expected.City), Bytes(data.City));
		Assert.Equal(Bytes(expected.Date), Bytes(data.Date));
		Assert.Equal(Bytes(expected.Unit), Bytes(data.Unit));
		Assert.Equal(Bytes(expected.Lead), Bytes(data.Lead));
		Assert.Equal(Bytes(expected.Bic.Bldg), Bytes(data.Bic.Bldg));
		Assert.Equal(expected.Rplt?.Length, data.Rplt?.Length);

		// and the same file twice
		data.Load(second);
		Assert.Equal(Bytes(expected.City), Bytes(data.City));
		Assert.Equal(Bytes(expected.Date), Bytes(data.Date));
	}

	[SkippableFact]
	public void MalformedSavsAreRejected() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), "No Civ3 install found.");
		string path = SavPath();
		Skip.If(path == null, "No Civ3 SAV files found.");

		byte[] biq = Util.ReadFile(PathUtils.defaultBicPath);
		byte[] sav = Util.ReadFile(path);
		new SavData(sav, biq);

		Random random = new(8);
		for (int i = 0; i < 60; i++) {
			byte[] truncated = sav.AsSpan(0, random.Next(4, sav.Length)).ToArray();
			Exception e = Record.Exception(() => new SavData(truncated, biq));
			// (A file cut off right after a section is valid)
			Assert.True(e == null || e is InvalidDataException, $"length {truncated.Length}: {e}");
		}

		// The length of the embedded BIQ data must fit in the file
		foreach (int badLength in new[] { -1, int.MinValue, int.MaxValue, sav.Length - 561 }) {
			byte[] bad = (byte[])sav.Clone();
			BitConverter.GetBytes(badLength).CopyTo(bad, 38);
			Assert.Throws<InvalidDataException>(() => new SavData(bad, biq));
		}

		// Randomly corrupted files load or fail cleanly
		for (int i = 0; i < 60; i++) {
			byte[] corrupt = (byte[])sav.Clone();
			for (int j = 0; j < 4; j++) corrupt[random.Next(562, corrupt.Length)] = (byte)random.Next(256);
			Exception e = Record.Exception(() => new SavData(corrupt, biq));
			Assert.True(e == null || e is InvalidDataException || e is NotSupportedException, e?.ToString());
		}
	}
}
