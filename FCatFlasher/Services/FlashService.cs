using System.Diagnostics;
using System.Text;

namespace FCatFlasher.Services
{
    internal class FlashService
    {
        private const string CliPath =
            "E:\\InicaalDG\\STM32CubeProgrammer\\bin\\STM32_Programmer_CLI.exe";

        private const string G0TestFirmwarePath =
            @"D:\test.hex";

        private const string C0TestFirmwarePath =
            @"D:\c011_factory_test_02_03_2026.hex";

        public async Task<string> GetDeviceType()
        {
            string result = await RunCli(
                "-c port=SWD mode=UR",
                5000
            );

            if (result.Contains("STM32G03") ||
                result.Contains("STM32G04"))
                return "G0";

            if (result.Contains("STM32C0"))
                return "C0";

            return "UNKNOWN";
        }

        public async Task<bool> CheckConnection(int timeoutMs = 3000)
        {
            try
            {
                string result = await RunCli(
                    "-c port=SWD mode=UR",
                    timeoutMs
                );

                return !result.Contains("ERROR") &&
                       !result.Contains("timeout") &&
                       result.Contains("Device ID");
            }
            catch
            {
                return false;
            }
        }

        public async Task<string> FlashAutoTestFirmware()
        {
            var output = new StringBuilder();

            output.AppendLine("=== UNLOCK RDP 0xAA ===");

            output.AppendLine(
                await RunCli(
                    "-c port=SWD -ob rdp=0xAA",
                    30000
                )
            );

            string device = await GetDeviceType();

            output.AppendLine($"MCU: {device}");

            string firmwarePath;

            if (device == "G0")
                firmwarePath = G0TestFirmwarePath;
            else if (device == "C0")
                firmwarePath = C0TestFirmwarePath;
            else
            {
                output.AppendLine("ERROR: Unknown MCU");
                return output.ToString();
            }

            output.AppendLine($"=== FLASH TEST FIRMWARE: {firmwarePath} ===");

            output.AppendLine(
                await RunCli(
                    $"-c port=SWD -w \"{firmwarePath}\" -v -rst",
                    30000
                )
            );

            return output.ToString();
        }

        public async Task<string> FlashProductionFirmware(string firmwarePath)
        {
            var output = new StringBuilder();

            output.AppendLine($"=== FLASH PRODUCTION FIRMWARE: {firmwarePath} ===");

            output.AppendLine(
                await RunCli(
                    $"-c port=SWD -w \"{firmwarePath}\" -v",
                    30000
                )
            );

            output.AppendLine("=== SET RDP 0xBB ===");

            output.AppendLine(
                await RunCli(
                    "-c port=SWD -ob rdp=0xBB -rst",
                    30000
                )
            );

            return output.ToString();
        }

        private async Task<string> RunCli(string arguments, int timeoutMs)
        {
            var output = new StringBuilder();

            var psi = new ProcessStartInfo
            {
                FileName = CliPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process();
            process.StartInfo = psi;

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    output.AppendLine(e.Data);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    output.AppendLine(e.Data);
            };

            process.Start();

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            bool finished = await WaitProcess(process, timeoutMs);

            if (!finished)
            {
                output.AppendLine("ERROR: STM32_Programmer_CLI timeout");
                return output.ToString();
            }

            if (process.ExitCode != 0)
                output.AppendLine($"ERROR: ExitCode = {process.ExitCode}");

            return output.ToString();
        }

        private static async Task<bool> WaitProcess(Process process, int timeoutMs)
        {
            var waitTask = process.WaitForExitAsync();
            var delayTask = Task.Delay(timeoutMs);

            var completed = await Task.WhenAny(waitTask, delayTask);

            if (completed == waitTask)
                return true;

            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch
            {
            }

            return false;
        }

    }
}
