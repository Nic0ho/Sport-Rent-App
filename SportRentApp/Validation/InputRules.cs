using System;
using System.Globalization;
using System.Linq;

namespace SportRentApp
{
    public static class InputRules
    {
        public static bool StoreNumber(string value, out int number) => int.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out number) && number > 0;
        public static string LoginError(string value)
        {
            value = (value ?? "").Trim();
            return value.Length < 5 || value.Length > 80 || value.StartsWith("__", StringComparison.Ordinal) ? "Логін: 5–80 символів, без початкового __." : null;
        }

        public static string NameError(string value)
        {
            int length = (value ?? "").Trim().Length;
            return length < 3 || length > 160 ? "Ім’я: 3–160 символів." : null;
        }

        public static string NormalizePhone(string value)
        {
            value = (value ?? "").Trim();
            if (value.Any(c => !(c >= '0' && c <= '9') && c != '+' && c != '-' && c != '(' && c != ')' && !char.IsWhiteSpace(c)) || value.Count(c => c == '+') > 1 || (value.Contains('+') && !value.StartsWith("+")))
                throw new ArgumentException("Телефон містить некоректні символи.");
            string digits = new string (value.Where(c => c >= '0' && c <= '9').ToArray());
            if (digits.Length == 10 && digits.StartsWith("0"))
                digits = "38" + digits;
            if (digits.Length < 10 || digits.Length > 15 || digits[0] == '0')
                throw new ArgumentException("Вкажіть номер телефону з кодом країни.");
            return "+" + digits;
        }

        public static bool IsMoney(decimal value, bool positive = false) => value >= (positive ? 0.01m : 0m) && value <= 1000000m && decimal.Round(value, 2) == value;
        public static bool IsMoney(double value, bool positive = false) => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0 && value <= 1000000 && IsMoney((decimal)value, positive);
        public static bool TryMoney(string value, out decimal amount, bool positive = false) => decimal.TryParse((value ?? "").Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount) && IsMoney(amount, positive);
        public static decimal Money(string value, bool positive = false)
        {
            decimal amount;
            if (!TryMoney(value, out amount, positive))
                throw new ArgumentException("Сума: до 1 000 000 грн, максимум 2 знаки після коми" + (positive ? ", більша за нуль." : "."));
            return amount;
        }
    }
}
