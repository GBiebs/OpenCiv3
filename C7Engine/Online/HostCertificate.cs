using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace C7Engine.Online {
	// The host's certificate for the encrypted connection, made each time
	// hosting starts. No certificate authority vouches for it: the invite
	// carries its fingerprint, and a joining player's game checks that.
	public static class HostCertificate {
		// 20 bytes of its SHA-256 (160 bits), in base32.
		public const int FingerprintLength = 32;
		private const string Base32 = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

		public static X509Certificate2 Create() {
			using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
			CertificateRequest request = new("CN=OpenCiv3 host", key, HashAlgorithmName.SHA256);
			using X509Certificate2 made = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
			// Windows' TLS needs the key in a form it can use, which a
			// certificate read back from its own export has (the key goes
			// when the certificate is disposed).
			return new X509Certificate2(made.Export(X509ContentType.Pkcs12));
		}

		public static string Fingerprint(X509Certificate certificate) {
			byte[] hash = SHA256.HashData(certificate.GetRawCertData());
			StringBuilder text = new();
			// 5 bits a character.
			for (int bit = 0; bit < FingerprintLength * 5; bit += 5) {
				int value = 0;
				for (int i = 0; i < 5; i++) {
					int b = bit + i;
					value = (value << 1) | ((hash[b / 8] >> (7 - b % 8)) & 1);
				}
				text.Append(Base32[value]);
			}
			return text.ToString();
		}

		public static bool IsFingerprintCharacter(char c) => Base32.Contains(c);
	}
}
