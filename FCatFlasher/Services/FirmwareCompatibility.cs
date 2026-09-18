using System.IO;

namespace FCatFlasher.Services;

internal static class FirmwareCompatibility
{
    // Legacy HEX files have no trusted target metadata; enforce the existing C0 filename convention.
    public static string Validate(string deviceType, string firmwarePath)
    {
        if (deviceType is not ("C0" or "G0"))
            return $"ERROR: MCU not detected: {deviceType}\n";
        if (!File.Exists(firmwarePath))
            return $"ERROR: Firmware not found: {firmwarePath}\n";

        bool markedC0 = Path.GetFileName(firmwarePath).Contains("c0", StringComparison.OrdinalIgnoreCase);
        if (deviceType == "C0" && !markedC0)
            return $"ERROR: C0 firmware filename must contain c0: {firmwarePath}\n";
        if (deviceType == "G0" && markedC0)
            return $"ERROR: C0 firmware cannot be written to G0: {firmwarePath}\n";
        return string.Empty;
    }
}
