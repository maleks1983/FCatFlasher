namespace FCatFlasher.Services
{
    internal class DiagnosticsParser
    {
        private bool _isReceiving;
        private string _report = string.Empty;

        public void Reset()
        {
            _isReceiving = false;
            _report = string.Empty;
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
            return IsDiagnosticsOk(_report);
        }

        private static bool IsDiagnosticsOk(string report)
        {
            return report.Contains("OUT_IO: OK") &&
                   report.Contains("OUT_HS: OK") &&
                   report.Contains("OUT_LS: OK") &&
                   report.Contains("ADC: OK") &&
                   report.Contains("ACC: OK") &&
                   report.Contains("BUZ: OK") &&
                   GetReportInt(report, "SIG") > 0;
        }

        private static int GetReportInt(string report, string key)
        {
            foreach (string line in report.Split('\n'))
            {
                string trimmed = line.Trim();

                if (!trimmed.StartsWith(key + ":"))
                    continue;

                string value = trimmed.Substring((key + ":").Length).Trim();

                if (int.TryParse(value, out int result))
                    return result;
            }

            return 0;
        }
    }
}
