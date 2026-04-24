using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace FCatFlasher.Services
{
    internal class CliService
    {
        public async Task<string> Run(string file, string args)
        {
            var process = new Process();

            process.StartInfo = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var output = new StringBuilder();

            process.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                    output.AppendLine(e.Data);
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                    output.AppendLine("ERR: " + e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            return output.ToString();
        }
    }
}
