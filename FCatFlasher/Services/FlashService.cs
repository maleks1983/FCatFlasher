using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FCatFlasher.Services
{
    internal class FlashService
    {
        private readonly CliService _cli = new();

        public async Task<string> Flash(string firmwarePath, string port = "SWD")
        {
            if (string.IsNullOrWhiteSpace(firmwarePath))
                throw new ArgumentException("Firmware path is empty");

            if (!File.Exists(firmwarePath))
                throw new FileNotFoundException("Firmware file not found", firmwarePath);

            // 🔥 шукаємо exe поруч з програмою
            string exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "STM32_Programmer_CLI.exe");

            if (!File.Exists(exe))
                throw new FileNotFoundException("STM32 Programmer CLI not found", exe);

            string args = $"-c port={port} -e all -w \"{firmwarePath}\" -v";

            // 🔥 таймаут (30 сек)
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var result = await _cli.Run(exe, args);

            // 🔥 більш точна перевірка
            if (!IsSuccess(result))
            {
                throw new Exception("Flashing failed:\n" + result);
            }

            return result;
        }

        private bool IsSuccess(string output)
        {
            var text = output.ToLower();

            // ❌ якщо є явні помилки
            if (text.Contains("error") || text.Contains("fail"))
                return false;

            // ✅ шукаємо успішний патерн STM32
            if (text.Contains("download verified successfully") ||
                text.Contains("verification...ok") ||
                text.Contains("file download complete"))
                return true;

            // якщо нічого не зрозуміли — вважаємо помилкою
            return false;
        }
    }
}