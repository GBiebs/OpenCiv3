using System;
using System.Security.Cryptography;
using System.Text;

namespace C7GameData {
	// Hotseat and play by email: the players' and the administrator's
	// passwords are kept as a slow salted hash (PBKDF2-SHA256, a random salt),
	// so a save sent by email doesn't give them away. "" means none.
	public static class Passwords {
		private const int Iterations = 100_000;

		public static string Hash(string password) {
			if (string.IsNullOrEmpty(password)) {
				return "";
			}
			byte[] salt = RandomNumberGenerator.GetBytes(16);
			return $"pbkdf2:{Iterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(Pbkdf2(password, salt, Iterations))}";
		}

		// Whether `password` matches `hash` (anything does when there's no
		// hash). `legacySalt`: hotseat saves from before play by email hashed
		// "{salt}:{password}" with SHA-256, the player's id as salt.
		public static bool Check(string hash, string password, string legacySalt = null) {
			if (string.IsNullOrEmpty(hash)) {
				return true;
			}
			password ??= "";
			string[] parts = hash.Split(':');
			if (parts.Length == 4 && parts[0] == "pbkdf2" && int.TryParse(parts[1], out int iterations)) {
				byte[] expected = Convert.FromBase64String(parts[3]);
				return CryptographicOperations.FixedTimeEquals(Pbkdf2(password, Convert.FromBase64String(parts[2]), iterations), expected);
			}
			if (legacySalt == null) {
				return false;
			}
			byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{legacySalt}:{password}"));
			return Convert.ToHexString(bytes) == hash;
		}

		private static byte[] Pbkdf2(string password, byte[] salt, int iterations) {
			return Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
		}
	}
}
