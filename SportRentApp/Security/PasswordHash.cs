using System;
using System.Security.Cryptography;

namespace SportRentApp
{
    internal static class PasswordHash
    {
        private const int Iterations = 600000;
        public static string Create(string password)
        {
            if (password == null || password.Length < 8 || password.Length > 256)
                throw new ArgumentException("Пароль: від 8 до 256 символів.");
            return CreateForMigration(password);
        }

        public static string CreateForMigration(string password)
        {
            var salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(salt);
            using (var kdf = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256))
                return "pbkdf2-sha256$" + Iterations + "$" + Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(kdf.GetBytes(32));
        }

        public static bool Verify(string password, string stored)
        {
            if (password == null || password.Length > 256 || stored == null)
                return false;
            if (!stored.StartsWith("pbkdf2-sha256$", StringComparison.Ordinal))
                return password == stored;
            try
            {
                var parts = stored.Split('$');
                int rounds;
                if (parts.Length != 4 || !int.TryParse(parts[1], out rounds) || rounds < 100000 || rounds > 2000000)
                    return false;
                var salt = Convert.FromBase64String(parts[2]);
                var expected = Convert.FromBase64String(parts[3]);
                if (salt.Length != 16 || expected.Length != 32)
                    return false;
                using (var kdf = new Rfc2898DeriveBytes(password, salt, rounds, HashAlgorithmName.SHA256))
                {
                    var actual = kdf.GetBytes(32);
                    int difference = 0;
                    for (int i = 0; i < actual.Length; i++)
                        difference |= actual[i] ^ expected[i];
                    return difference == 0;
                }
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public static bool IsLegacy(string value) => !value.StartsWith("pbkdf2-sha256$", StringComparison.Ordinal);
    }
}
