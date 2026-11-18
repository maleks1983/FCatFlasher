namespace FCatFlasher.Services;

internal sealed class DiagnosticsParser
{
    private const int BatteryMinMv = 3000, BatteryMaxMv = 3400;
    private static readonly DiagnosticsProfile Fcat = new("SER", "SIG", ["OUT_IO", "OUT_HS", "OUT_LS", "ADC", "ACC", "BUZ"], [("BAT", 3000, 3400), ("LED_ACT", 1600, 2000), ("LED_STA", 1800, 2200), ("LED_ERR", 1800, 2300)], false);
    private static readonly DiagnosticsProfile Fcat2 = new("SER", "CRC", ["I2C_IO", "ACC_IC", "SW", "BUZ", "OUT_HS", "OUT_LS", "OUT_TV", "EXT"], [("BAT", BatteryMinMv, BatteryMaxMv), ("LED_R", 1600, 2000), ("LED_G", 1400, 2200), ("LED_Y", 1400, 2200), ("PWM", 1000, 2200)], true);
    private bool _receiving; private string _report = string.Empty;
    public int LastSig { get; private set; }
    public string LastReport { get; private set; } = string.Empty;
    public string LastMcuSerial { get; private set; } = string.Empty;
    public string LastInvalidField { get; private set; } = string.Empty;
    public string BoardType { get; set; } = "FCAT";
    public void Reset() { _receiving = false; _report = string.Empty; LastSig = 0; LastReport = LastMcuSerial = LastInvalidField = string.Empty; }
    public bool? ProcessLine(string line)
    {
        if (line.Contains("DIAGNOSTICS REPORT BEGIN", StringComparison.OrdinalIgnoreCase)) { _receiving = true; _report = string.Empty; return null; }
        if (_receiving) _report += line + "\n";
        if (!_receiving || !line.Contains("DIAGNOSTICS REPORT END", StringComparison.OrdinalIgnoreCase)) return null;
        _receiving = false; var values = Parse(_report); var profile = Profile(_report); bool ok = Validate(values, profile); LastReport = Mark(_report, values, profile); return ok;
    }
    private DiagnosticsProfile Profile(string report) => BoardType.Equals("FCAT2", StringComparison.OrdinalIgnoreCase) || (report.Contains("SER:", StringComparison.OrdinalIgnoreCase) && report.Contains("CRC:", StringComparison.OrdinalIgnoreCase)) ? Fcat2 : Fcat;
    private bool Validate(IReadOnlyDictionary<string, string> v, DiagnosticsProfile p)
    {
        LastInvalidField = string.Empty; LastMcuSerial = Text(v, p.Serial).Trim().ToUpperInvariant(); LastSig = Number(v, p.Signature); if (LastSig <= 0 && p.OkSignature && IsOk(v, p.Signature)) LastSig = 1;
        if (LastSig <= 0) { LastInvalidField = p.Signature; return false; } if (string.IsNullOrWhiteSpace(LastMcuSerial)) { LastInvalidField = p.Serial; return false; }
        foreach (string f in p.Status) if (!IsOk(v, f)) { LastInvalidField = f; return false; }
        foreach (var f in p.Measurements) if (!Range(v, f.Key, f.Min, f.Max)) { LastInvalidField = f.Key; return false; } return true;
    }
    private string Mark(string report, IReadOnlyDictionary<string, string> v, DiagnosticsProfile p)
    {
        var result = new List<string>(); foreach (string line in report.Trim().Split('\n')) { string text = line.TrimEnd('\r'); string key = text.Split(':', 2)[0].Trim(); bool known = key.Equals(p.Serial, StringComparison.OrdinalIgnoreCase) || key.Equals(p.Signature, StringComparison.OrdinalIgnoreCase) || p.Status.Contains(key, StringComparer.OrdinalIgnoreCase) || p.Measurements.Any(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase)); bool bad = known && !ValidField(v, p, key); if (bad && string.IsNullOrEmpty(LastInvalidField)) LastInvalidField = key; result.Add(bad ? text + " - ERR" : text); } return string.Join('\n', result);
    }
    private static bool ValidField(IReadOnlyDictionary<string, string> v, DiagnosticsProfile p, string k) { if (k.Equals(p.Serial, StringComparison.OrdinalIgnoreCase)) return !string.IsNullOrWhiteSpace(Text(v, k)); if (k.Equals(p.Signature, StringComparison.OrdinalIgnoreCase)) return Number(v, k) > 0 || (p.OkSignature && IsOk(v, k)); if (p.Status.Contains(k, StringComparer.OrdinalIgnoreCase)) return IsOk(v, k); var f = p.Measurements.First(x => x.Key.Equals(k, StringComparison.OrdinalIgnoreCase)); return Range(v, k, f.Min, f.Max); }
    private static Dictionary<string, string> Parse(string report) { var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); foreach (string line in report.Split('\n')) { string t = line.Trim(); int i = t.IndexOf(':'); if (i > 0) d[t[..i].Trim()] = t[(i + 1)..].Trim(); } return d; }
    private static string Text(IReadOnlyDictionary<string, string> v, string k) => v.TryGetValue(k, out string? x) ? x : string.Empty;
    private static int Number(IReadOnlyDictionary<string, string> v, string k) => int.TryParse(Text(v, k), out int x) ? x : 0;
    private static bool IsOk(IReadOnlyDictionary<string, string> v, string k) => Text(v, k).Equals("OK", StringComparison.OrdinalIgnoreCase);
    private static bool Range(IReadOnlyDictionary<string, string> v, string k, int min, int max) => Number(v, k) >= min && Number(v, k) <= max;
    private sealed record DiagnosticsProfile(string Serial, string Signature, string[] Status, (string Key, int Min, int Max)[] Measurements, bool OkSignature);
}
