namespace FCatFlasher.Services
{
    internal class DiagnosticsParser
    {
        private bool _isReceiving;
        private string _report = string.Empty;

        public int LastSig { get; private set; }
        public string LastReport { get; private set; } = string.Empty;
        public string LastMcuSerial { get; private set; } = string.Empty;

        public void Reset()
        {
            _isReceiving = false;
            _report = string.Empty;
            LastSig = 0;
            LastReport = string.Empty;
            LastMcuSerial = string.Empty;
        }

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

            if (line != "DIAGNOSTICS REPORT END")
                return null;

            _isReceiving = false;
            LastReport = _report.Trim();
            return IsDiagnosticsOk(_report);
        }

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
                   IsReportOk(values, "BUZ");
        }

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

        private static bool IsReportOk(IReadOnlyDictionary<string, string> values, string key)
        {
            return values.TryGetValue(key, out string? value) &&
                   value.Equals("OK", StringComparison.OrdinalIgnoreCase);
        }

        private static int GetReportInt(IReadOnlyDictionary<string, string> values, string key)
        {
            if (!values.TryGetValue(key, out string? value))
                return 0;

            return int.TryParse(value, out int result) ? result : 0;
        }

        private static string GetReportText(IReadOnlyDictionary<string, string> values, string key)
        {
            if (!values.TryGetValue(key, out string? value))
                return string.Empty;

            return value;
        }

        private static string NormalizeMcuSerial(string serial)
        {
            return new string(
                serial.Trim().Where(char.IsLetterOrDigit).ToArray())
                .ToUpperInvariant();
        }
    }
}
