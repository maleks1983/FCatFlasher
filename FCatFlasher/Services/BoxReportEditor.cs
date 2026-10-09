using FCatFlasher.Models;
using System.Globalization;

namespace FCatFlasher.Services
{
    /// <summary>
    /// Редагує серійні номери завершеної локальної коробки.
    /// </summary>
    internal static class BoxReportEditor
    {
        public static bool Contains(BoxReport report, string serial)
            => Expand(report.SerialNumbersText).Contains(serial.Trim(), StringComparer.OrdinalIgnoreCase);

        public static bool TryAdd(
            BoxReport source,
            string newSerial,
            out BoxReport updated,
            out string error)
        {
            List<string> serials = Expand(source.SerialNumbersText);
            string normalized = newSerial.Trim();

            if (normalized.Length == 0)
                return Fail(source, "Вкажіть серійний номер нової плати.", out updated, out error);

            if (serials.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                return Fail(source, $"Плата {normalized} вже є в коробці № {source.BoxNumber}.", out updated, out error);

            serials.Add(normalized);
            updated = Build(source, serials);
            error = string.Empty;
            return true;
        }

        public static bool TryReplace(
            BoxReport source,
            string oldSerial,
            string newSerial,
            out BoxReport updated,
            out string error)
        {
            List<string> serials = Expand(source.SerialNumbersText);
            string oldValue = oldSerial.Trim();
            string newValue = newSerial.Trim();

            if (oldValue.Length == 0 || newValue.Length == 0)
                return Fail(source, "Вкажіть старий і новий серійні номери.", out updated, out error);

            int index = serials.FindIndex(value =>
                string.Equals(value, oldValue, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return Fail(source, $"Плату {oldValue} не знайдено в коробці № {source.BoxNumber}.", out updated, out error);

            if (!string.Equals(oldValue, newValue, StringComparison.OrdinalIgnoreCase) &&
                serials.Contains(newValue, StringComparer.OrdinalIgnoreCase))
            {
                return Fail(source, $"Плата {newValue} вже є в коробці № {source.BoxNumber}.", out updated, out error);
            }

            serials[index] = newValue;
            updated = Build(source, serials);
            error = string.Empty;
            return true;
        }

        internal static List<string> Expand(string serialNumbersText)
        {
            var result = new List<string>();
            foreach (string rawPart in serialNumbersText.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                string part = rawPart.Trim();
                if (!TryExpandRange(part, result))
                    result.Add(part);
            }
            return result;
        }

        private static bool TryExpandRange(string value, List<string> output)
        {
            int separator = value.LastIndexOf('-');
            if (separator <= 0 || separator >= value.Length - 1)
                return false;

            if (!TryParse(value[..separator], out string startPrefix, out long start, out int digits) ||
                !TryParse(value[(separator + 1)..], out string endPrefix, out long end, out int endDigits) ||
                !string.Equals(startPrefix, endPrefix, StringComparison.OrdinalIgnoreCase) ||
                end < start || end - start > 10000)
            {
                return false;
            }

            int width = Math.Max(digits, endDigits);
            for (long number = start; number <= end; number++)
                output.Add(startPrefix + number.ToString($"D{width}", CultureInfo.InvariantCulture));
            return true;
        }

        private static bool TryParse(string value, out string prefix, out long number, out int digits)
        {
            int index = value.Length - 1;
            while (index >= 0 && char.IsDigit(value[index]))
                index--;

            prefix = value[..(index + 1)];
            string numberText = value[(index + 1)..];
            digits = numberText.Length;
            number = 0;
            return digits > 0 && long.TryParse(numberText, NumberStyles.None, CultureInfo.InvariantCulture, out number);
        }

        private static BoxReport Build(BoxReport source, List<string> serials)
        {
            var session = new BoxSession();
            session.Restore(new ProductionBoxState
            {
                BoxNumber = source.BoxNumber,
                BoxQuantity = serials.Count,
                FirmwareName = source.FirmwareName,
                SerialNumbers = serials
            });
            BoxReport result = session.CreateCurrentReport(source.FirmwareName);
            result.LocalId = source.LocalId;
            result.IsSynced = false;
            result.CreatedAt = source.CreatedAt;
            return result;
        }

        private static bool Fail(BoxReport source, string message, out BoxReport updated, out string error)
        {
            updated = source;
            error = message;
            return false;
        }
    }
}
