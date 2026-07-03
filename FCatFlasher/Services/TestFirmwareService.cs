using System.IO;
using System.Text;

namespace FCatFlasher.Services
{
    internal class TestFirmwareService(FirmwareService firmwareService)
    {
        private const string DefaultTestFirmwareDirectory = @"D:\";
        private const string G0TestFirmwareFileName = "test.hex";
        private const string C0TestFirmwareFileName = "c011_factory_test_02_03_2026.hex";
        // private const string C0TestFirmwareFileName = "c011_factory_no_accel_plus_crc.hex";
        public string TestFirmwareDirectory { get; set; } = DefaultTestFirmwareDirectory;

        public async Task<string> FlashAutoTestFirmware()
        {
            string device = await firmwareService.GetDeviceType();

            return await FlashTestFirmwareForDevice(device);
        }

        public async Task<(string DeviceType, string Output)> FlashAutoTestFirmwareWithUnlockSession()
        {
            var output = new StringBuilder();

            FirmwareService.RdpUnlockSessionResult unlockResult =
                await firmwareService.GetDeviceTypeAndUnlockRdp();

            output.AppendLine(unlockResult.Output);

            if (FlashResultFailed(unlockResult.Output))
                return (unlockResult.DeviceType, output.ToString());

            string firmwarePath;

            if (unlockResult.DeviceType == "G0")
                firmwarePath = GetTestFirmwarePath(G0TestFirmwareFileName);
            else if (unlockResult.DeviceType == "C0")
                firmwarePath = GetTestFirmwarePath(C0TestFirmwareFileName);
            else
            {
                output.AppendLine("ERROR: Unknown MCU");
                return (unlockResult.DeviceType, output.ToString());
            }

            output.AppendLine($"=== FLASH TEST FIRMWARE: {firmwarePath} ===");

            if (!File.Exists(firmwarePath))
            {
                output.AppendLine($"ERROR: Test firmware not found: {firmwarePath}");
                return (unlockResult.DeviceType, output.ToString());
            }

            output.AppendLine(await firmwareService.WriteFirmware(firmwarePath, resetAfterWrite: true));

            return (unlockResult.DeviceType, output.ToString());
        }

        public async Task<string> FlashTestFirmwareForDevice(string device)
        {
            var output = new StringBuilder();

            output.AppendLine("=== UNLOCK RDP 0xAA ===");
            string unlockResult = await firmwareService.UnlockRdp();
            output.AppendLine(unlockResult);

            if (FlashResultFailed(unlockResult))
                return output.ToString();

            output.AppendLine($"MCU: {device}");

            string firmwarePath;

            if (device == "G0")
                firmwarePath = GetTestFirmwarePath(G0TestFirmwareFileName);
            else if (device == "C0")
                firmwarePath = GetTestFirmwarePath(C0TestFirmwareFileName);
            else
            {
                output.AppendLine("ERROR: Unknown MCU");
                return output.ToString();
            }

            output.AppendLine($"=== FLASH TEST FIRMWARE: {firmwarePath} ===");

            if (!File.Exists(firmwarePath))
            {
                output.AppendLine($"ERROR: Test firmware not found: {firmwarePath}");
                return output.ToString();
            }

            output.AppendLine(await firmwareService.WriteFirmware(firmwarePath, resetAfterWrite: true));

            return output.ToString();
        }

        private static bool FlashResultFailed(string result)
        {
            return result.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ||
                   result.Contains("ExitCode", StringComparison.OrdinalIgnoreCase) ||
                   result.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                   result.Contains("failed", StringComparison.OrdinalIgnoreCase);
        }

        private string GetTestFirmwarePath(string fileName)
        {
            string directory = string.IsNullOrWhiteSpace(TestFirmwareDirectory)
                ? DefaultTestFirmwareDirectory
                : TestFirmwareDirectory;

            return Path.Combine(directory, fileName);
        }
    }
}
