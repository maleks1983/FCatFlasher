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

        public async Task<string> FlashTestFirmwareForDevice(string device)
        {
            var output = new StringBuilder();

            output.AppendLine("=== UNLOCK RDP 0xAA ===");
            output.AppendLine(await firmwareService.UnlockRdp());

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

        private string GetTestFirmwarePath(string fileName)
        {
            string directory = string.IsNullOrWhiteSpace(TestFirmwareDirectory)
                ? DefaultTestFirmwareDirectory
                : TestFirmwareDirectory;

            return Path.Combine(directory, fileName);
        }
    }
}
