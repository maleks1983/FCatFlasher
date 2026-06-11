namespace FCatFlasher.Services
{
    internal class DiagnosticsParser
    {
        private bool _isReceiving;
        private string _report = string.Empty;

        public int LastSig { get; private set; }
        public string LastReport { get; private set; } = string.Empty;

        public void Reset()
        {
            _isReceiving = false;
            _report = string.Empty;
            LastSig = 0;
            LastReport = string.Empty;
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
            int sig = GetReportInt(report, "SIG");
            LastSig = sig;

            return report.Contains("OUT_IO: OK") &&
                   report.Contains("OUT_HS: OK") &&
                   report.Contains("OUT_LS: OK") &&
                   report.Contains("ADC: OK") &&
                   report.Contains("ACC: OK") &&
                   report.Contains("BUZ: OK") &&
                   sig > 0;
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
