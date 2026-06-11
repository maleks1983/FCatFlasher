using System.Diagnostics;
using System.IO;
using System.Text;

namespace FCatFlasher.Services
{
    internal class FirmwareService
    {
        private const string DefaultCliPath =
            "E:\\InicaalDG\\STM32CubeProgrammer\\bin\\STM32_Programmer_CLI.exe";

        public string CliPath { get; set; } = DefaultCliPath;

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

        public async Task<string> UnlockRdp()
        {
            return await RunCli(
                "-c port=SWD -ob rdp=0xAA",
                30000
            );
        }

        public async Task<string> WriteFirmware(string firmwarePath, bool resetAfterWrite)
        {
            string resetArgument = resetAfterWrite ? " -rst" : "";

            return await RunCli(
                $"-c port=SWD -w \"{firmwarePath}\" -v{resetArgument}",
                30000
            );
        }

        public async Task<string> SetRdpProtected()
        {
            return await RunCli(
                "-c port=SWD -ob rdp=0xBB -rst",
                30000
            );
        }

        public async Task<string> FlashProductionFirmware(string firmwarePath)
        {
            var output = new StringBuilder();

            output.AppendLine($"=== FLASH PRODUCTION FIRMWARE: {firmwarePath} ===");
            output.AppendLine(await WriteFirmware(firmwarePath, resetAfterWrite: false));

            output.AppendLine("=== SET RDP 0xBB ===");
            output.AppendLine(await SetRdpProtected());

            return output.ToString();
        }

        private async Task<string> RunCli(string arguments, int timeoutMs)
        {
            var output = new StringBuilder();

            if (string.IsNullOrWhiteSpace(CliPath) || !File.Exists(CliPath))
            {
                output.AppendLine($"ERROR: STM32Cube CLI not found: {CliPath}");
                return output.ToString();
            }

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
