namespace FCatFlasher.Services
{
/// <summary>
/// Парсить UART-звіт автотесту та визначає результат діагностики.
/// </summary>
    internal class DiagnosticsParser
    {
        private const int BatteryMinMv = 3000;
        private const int BatteryMaxMv = 3400;
        private const int RedLedMinMv = 1600;
        private const int RedLedMaxMv = 2000;
        private const int GreenLedMinMv = 1800;
        private const int GreenLedMaxMv = 2600;
        private const int YellowLedMinMv = 1800;
        private const int YellowLedMaxMv = 2300;

        private bool _isReceiving;
        private string _report = string.Empty;

        public int LastSig { get; private set; }
        public string LastReport { get; private set; } = string.Empty;
        public string LastMcuSerial { get; private set; } = string.Empty;
        public string LastInvalidField { get; private set; } = string.Empty;

/// <summary>
/// Очищає накопичений звіт і останні діагностичні значення.
/// </summary>
        public void Reset()
        {
            _isReceiving = false;
            _report = string.Empty;
            LastSig = 0;
            LastReport = string.Empty;
            LastMcuSerial = string.Empty;
            LastInvalidField = string.Empty;
        }

/// <summary>
/// Обробляє один рядок UART-звіту; повертає null, поки звіт не завершився.
/// </summary>
        public bool? ProcessLine(string line)
        {
            if (line == "DIAGNOSTICS REPORT BEGIN")
            {
                _isReceiving = true;
                _report = string.Empty;
                return null;
            }

            if (_isReceiving)
                _report += line + "\n";

            if (line != "DIAGNOSTICS REPORT END" || !_isReceiving)
                return null;

            _isReceiving = false;
            LastReport = MarkInvalidMeasurements(_report);
            return IsDiagnosticsOk(_report);
        }

        private string MarkInvalidMeasurements(string report)
        {
            var values = ParseReportValues(report);
            var result = new List<string>();

            foreach (string line in report.Trim().Split('\n'))
            {
                string trimmed = line.TrimEnd('\r');
                string key = trimmed.Split(':', 2)[0].Trim();

                bool invalid = key switch
                {
                    "BAT" => !IsReportMillivoltsInRange(values, key, BatteryMinMv, BatteryMaxMv),
                    "LED_ACT" => !IsReportMillivoltsInRange(values, key, RedLedMinMv, RedLedMaxMv),
                    "LED_STA" => !IsReportMillivoltsInRange(values, key, GreenLedMinMv, GreenLedMaxMv),
                    "LED_ERR" => !IsReportMillivoltsInRange(values, key, YellowLedMinMv, YellowLedMaxMv),
                    _ => false
                };

                if (invalid && string.IsNullOrEmpty(LastInvalidField))
                    LastInvalidField = key;

                result.Add(invalid ? $"{trimmed} - ERR" : trimmed);
            }

            return string.Join('\n', result);
        }

/// <summary>
/// Перевіряє, чи всі контрольні поля звіту мають очікувані значення.
/// </summary>
        private bool IsDiagnosticsOk(string report)
        {
            Dictionary<string, string> values = ParseReportValues(report);

            LastSig = GetReportInt(values, "SIG");
            LastMcuSerial = NormalizeMcuSerial(GetReportText(values, "SER"));

            return LastSig > 0 &&
                   !string.IsNullOrWhiteSpace(LastMcuSerial) &&
                   IsReportOk(values, "OUT_IO") &&
                   IsReportOk(values, "OUT_HS") &&
                   IsReportOk(values, "OUT_LS") &&
                   IsReportOk(values, "ADC") &&
                   IsReportOk(values, "ACC") &&
                   IsReportOk(values, "BUZ") &&
                   IsReportMillivoltsInRange(values, "BAT", BatteryMinMv, BatteryMaxMv) &&
                   IsReportMillivoltsInRange(values, "LED_ACT", RedLedMinMv, RedLedMaxMv) &&
                   IsReportMillivoltsInRange(values, "LED_STA", GreenLedMinMv, GreenLedMaxMv) &&
                   IsReportMillivoltsInRange(values, "LED_ERR", YellowLedMinMv, YellowLedMaxMv);
        }

/// <summary>
/// Розбирає текстовий звіт у словник ключ-значення.
/// </summary>
        private static Dictionary<string, string> ParseReportValues(string report)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string line in report.Split('\n'))
            {
                string trimmed = line.Trim();
                int separatorIndex = trimmed.IndexOf(':');

                if (separatorIndex <= 0)
                    continue;

                string key = trimmed[..separatorIndex].Trim();
                string value = trimmed[(separatorIndex + 1)..].Trim();
                values[key] = value;
            }

            return values;
        }

/// <summary>
/// Перевіряє булеве поле звіту у форматі OK.
/// </summary>
        private static bool IsReportOk(IReadOnlyDictionary<string, string> values, string key)
        {
            return values.TryGetValue(key, out string? value) &&
                   value.Equals("OK", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsReportMillivoltsInRange(
            IReadOnlyDictionary<string, string> values,
            string key,
            int minimum,
            int maximum)
        {
            int value = GetReportInt(values, key);
            return value >= minimum && value <= maximum;
        }

/// <summary>
/// Безпечно зчитує числове поле звіту.
/// </summary>
        private static int GetReportInt(IReadOnlyDictionary<string, string> values, string key)
        {
            if (!values.TryGetValue(key, out string? value))
                return 0;

            return int.TryParse(value, out int result) ? result : 0;
        }

/// <summary>
/// Безпечно зчитує текстове поле звіту.
/// </summary>
        private static string GetReportText(IReadOnlyDictionary<string, string> values, string key)
        {
            if (!values.TryGetValue(key, out string? value))
                return string.Empty;

            return value;
        }

/// <summary>
/// Нормалізує серійний номер MCU з тестового звіту.
/// </summary>
        private static string NormalizeMcuSerial(string serial)
        {
            return new string(
                serial.Trim().Where(char.IsLetterOrDigit).ToArray())
                .ToUpperInvariant();
        }
    }
}
