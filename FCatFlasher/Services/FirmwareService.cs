using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace FCatFlasher.Services
{
    internal class FirmwareService
    {
        private const string DefaultCliPath =
            "E:\\InicaalDG\\STM32CubeProgrammer\\bin\\STM32_Programmer_CLI.exe";
        private const string DefaultApiDirectory = "E:\\InicaalDG\\Cube\\api";
        private const uint FlashBaseAddress = 0x08000000;
        private const int ApiConnectionSettleDelayMs = 400;
        private const bool UseCliFallback = false;

        public string CliPath { get; set; } = DefaultCliPath;
        public string LastConnectionError { get; private set; } = string.Empty;
        public string LastMcuName { get; private set; } = string.Empty;

        public async Task<string> GetDeviceType()
        {
            string apiResult = await RunApi(
                "GET DEVICE TYPE",
                api =>
                {
                    DeviceInfo deviceInfo = api.GetDeviceInfo();
                    StoreMcuInfo(deviceInfo);
                    return GetDeviceTypeFromName(deviceInfo.Name);
                });

            if (!ShouldFallbackToCli(apiResult))
                return apiResult;

            string result = await RunCli(
                "-c port=SWD mode=UR",
                5000
            );

            if (result.Contains("STM32G03") ||
                result.Contains("STM32G04"))
            {
                StoreMcuInfo(new DeviceInfo("STM32G0"));
                return "G0";
            }

            if (result.Contains("STM32C0"))
            {
                StoreMcuInfo(new DeviceInfo("STM32C0"));
                return "C0";
            }

            return "UNKNOWN";
        }

        public async Task<bool> CheckConnection(int timeoutMs = 3000)
        {
            try
            {
                string apiResult = await RunApi(
                    "CHECK CONNECTION",
                    api =>
                    {
                        DeviceInfo deviceInfo = api.GetDeviceInfo();

                        if (deviceInfo.Name.Length == 0)
                            return "ERROR: STM32CubeProgrammer API connected, but device info is empty";

                        StoreMcuInfo(deviceInfo);
                        return "OK";
                    });

                if (!ShouldFallbackToCli(apiResult))
                {
                    bool apiConnected = apiResult == "OK";
                    LastConnectionError = apiConnected ? string.Empty : FormatConnectionCheckError(apiResult);
                    return apiConnected;
                }

                string result = await RunCli(
                    "-c port=SWD mode=UR",
                    timeoutMs
                );

                bool connected = !result.Contains("ERROR") &&
                                 !result.Contains("timeout") &&
                                 result.Contains("Device ID");

                LastConnectionError = connected ? string.Empty : result.Trim();
                return connected;
            }
            catch (Exception ex)
            {
                LastConnectionError = ex.Message;
                return false;
            }
        }

        public async Task<string> UnlockRdp()
        {
            string apiResult = await RunApi(
                "UNLOCK RDP",
                api =>
                {
                    int result = api.SendOptionBytesCommand("-ob rdp=0xaa");
                    return FormatApiResult("RDP 0xAA", result);
                });

            if (!ShouldFallbackToCli(apiResult))
                return apiResult;

            return await RunCli(
                "-c port=SWD -ob rdp=0xAA",
                30000
            );
        }

        public async Task<string> WriteFirmware(string firmwarePath, bool resetAfterWrite)
        {
            string apiResult = await RunApi(
                $"WRITE FIRMWARE: {firmwarePath}",
                api =>
                {
                    int downloadResult = api.DownloadFile(firmwarePath, FlashBaseAddress, skipErase: false, verify: true);

                    if (downloadResult != 0)
                        return FormatApiResult("DOWNLOAD", downloadResult);

                    if (!resetAfterWrite)
                        return FormatApiResult("DOWNLOAD", downloadResult);

                    int resetResult = api.Reset();
                    return new StringBuilder()
                        .AppendLine(FormatApiResult("DOWNLOAD", downloadResult))
                        .Append(FormatApiResult("RESET", resetResult))
                        .ToString();
                });

            if (!ShouldFallbackToCli(apiResult))
                return apiResult;

            string resetArgument = resetAfterWrite ? " -rst" : "";

            return await RunCli(
                $"-c port=SWD -w \"{firmwarePath}\" -v{resetArgument}",
                30000
            );
        }

        public async Task<string> SetRdpProtected()
        {
            string apiResult = await RunApi(
                "SET RDP 0xBB",
                api =>
                {
                    int rdpResult = api.SendOptionBytesCommand("-ob rdp=0xbb");
                    int resetResult = api.Reset();

                    return new StringBuilder()
                        .AppendLine(FormatApiResult("RDP 0xBB", rdpResult))
                        .Append(FormatApiResult("RESET", resetResult))
                        .ToString();
                });

            if (!ShouldFallbackToCli(apiResult))
                return apiResult;

            return await RunCli(
                "-c port=SWD -ob rdp=0xBB -rst",
                30000
            );
        }

        public async Task<string> FlashProductionFirmware(string firmwarePath)
        {
            var output = new StringBuilder();

            output.AppendLine($"=== FLASH PRODUCTION FIRMWARE: {firmwarePath} ===");

            string apiResult = await RunApi(
                $"FLASH PRODUCTION FIRMWARE: {firmwarePath}",
                api =>
                {
                    var apiOutput = new StringBuilder();

                    int downloadResult = api.DownloadFile(
                        firmwarePath,
                        FlashBaseAddress,
                        skipErase: false,
                        verify: true);

                    apiOutput.AppendLine(FormatApiResult("DOWNLOAD", downloadResult));

                    if (downloadResult != 0)
                        return apiOutput.ToString();

                    apiOutput.AppendLine("=== SET RDP 0xBB ===");

                    int rdpResult = api.SendOptionBytesCommand("-ob rdp=0xbb");
                    apiOutput.AppendLine(FormatApiResult("RDP 0xBB", rdpResult));

                    if (rdpResult != 0)
                        return apiOutput.ToString();

                    int resetResult = api.Reset();
                    apiOutput.Append(FormatApiResult("RESET", resetResult));

                    return apiOutput.ToString();
                });

            if (!ShouldFallbackToCli(apiResult))
            {
                output.AppendLine(apiResult);
                return output.ToString();
            }

            output.AppendLine(apiResult);
            output.AppendLine(await RunCli(
                $"-c port=SWD -w \"{firmwarePath}\" -v",
                30000));

            output.AppendLine("=== SET RDP 0xBB ===");
            output.AppendLine(await RunCli(
                "-c port=SWD -ob rdp=0xBB -rst",
                30000));

            return output.ToString();
        }

        private void StoreMcuInfo(DeviceInfo deviceInfo)
        {
            if (!string.IsNullOrWhiteSpace(deviceInfo.Name))
                LastMcuName = deviceInfo.Name;
        }

        private static string GetDeviceTypeFromName(string deviceName)
        {
            if (deviceName.Contains("STM32G03") ||
                deviceName.Contains("STM32G04") ||
                deviceName.Contains("STM32G0"))
                return "G0";

            if (deviceName.Contains("STM32C0"))
                return "C0";

            return "UNKNOWN";
        }

        private readonly record struct DeviceInfo(string Name);

        private async Task<string> RunApi(string title, Func<Stm32CubeProgrammerApi, string> action)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var api = Stm32CubeProgrammerApi.Connect(GetApiLibDirectory());
                    return action(api);
                }
                catch (DllNotFoundException ex)
                {
                    return FormatApiUnavailable(ex.Message);
                }
                catch (EntryPointNotFoundException ex)
                {
                    return FormatApiUnavailable(ex.Message);
                }
                catch (BadImageFormatException ex)
                {
                    return FormatApiUnavailable(ex.Message);
                }
                catch (FileNotFoundException ex)
                {
                    return FormatApiUnavailable(ex.Message);
                }
                catch (DirectoryNotFoundException ex)
                {
                    return FormatApiUnavailable(ex.Message);
                }
                catch (InvalidOperationException ex)
                {
                    return FormatApiUnavailable(ex.Message);
                }
                catch (Exception ex)
                {
                    return $"ERROR: STM32CubeProgrammer API {title} failed: {ex.Message}";
                }
            });
        }

        private string GetApiLibDirectory()
        {
            string? binDirectory = Path.GetDirectoryName(CliPath);

            if (string.IsNullOrWhiteSpace(binDirectory))
                throw new DirectoryNotFoundException("STM32CubeProgrammer bin directory was not found.");

            string programmerDirectory = Directory.GetParent(binDirectory)?.FullName
                ?? throw new DirectoryNotFoundException("STM32CubeProgrammer directory was not found.");

            string defaultApiLibDirectory = Path.Combine(DefaultApiDirectory, "lib");

            if (Directory.Exists(defaultApiLibDirectory))
                return defaultApiLibDirectory;

            return Path.Combine(programmerDirectory, "api", "lib");
        }

        private static bool IsApiUnavailable(string result)
        {
            return result.StartsWith("ERROR: STM32CubeProgrammer API unavailable:", StringComparison.Ordinal);
        }

        private static bool ShouldFallbackToCli(string result)
        {
            return UseCliFallback &&
                   (IsApiUnavailable(result) ||
                    result.StartsWith("ERROR: STM32CubeProgrammer API", StringComparison.Ordinal));
        }

        private static string FormatApiUnavailable(string message)
        {
            return $"ERROR: STM32CubeProgrammer API unavailable: {message}";
        }

        private static string FormatApiResult(string operation, int result)
        {
            return result == 0
                ? $"{operation}: OK"
                : $"ERROR: {operation} failed with code {result}";
        }

        private static string FormatConnectionCheckError(string result)
        {
            if (result.Contains("connect attempts:", StringComparison.OrdinalIgnoreCase) &&
                result.Contains("Target voltage:", StringComparison.OrdinalIgnoreCase))
            {
                return "ST-LINK OK, target MCU not connected";
            }

            return result;
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

        private sealed class Stm32CubeProgrammerApi : IDisposable
        {
            private const int StlinkInterface = 0;
            private const int NormalMode = 0;
            private const int HotplugMode = 1;
            private const int UnderResetMode = 2;
            private const int SwdPort = 1;
            private const int SoftwareReset = 0;
            private static readonly object NativeLock = new();
            private static bool _loadPathConfigured;
            private string _apiLibDirectory = string.Empty;
            private bool _connected;

            private Stm32CubeProgrammerApi()
            {
            }

            public static Stm32CubeProgrammerApi Connect(string apiLibDirectory)
            {
                lock (NativeLock)
                {
                    return WithApiWorkingDirectory(
                        apiLibDirectory,
                        () =>
                        {
                            ConfigureLoadPath(apiLibDirectory);

                            NativeMethods.SetLoadersPath("./.");
                            NativeMethods.SetVerbosityLevel(0);
                            Thread.Sleep(ApiConnectionSettleDelayMs);

                            string apiDllPath = Path.Combine(apiLibDirectory, "CubeProgrammer_API.dll");
                            string pathInfo = $"API path: {apiLibDirectory}; CubeProgrammer_API.dll exists: {File.Exists(apiDllPath)}";

                            int count = NativeMethods.GetStLinkList(out IntPtr stLinkList, shared: 0);

                            if (count <= 0 || stLinkList == IntPtr.Zero)
                            {
                                int sharedCount = NativeMethods.GetStLinkList(out stLinkList, shared: 1);

                                if (sharedCount > count)
                                    count = sharedCount;
                            }

                            if (count <= 0 || stLinkList == IntPtr.Zero)
                                throw new InvalidOperationException($"{pathInfo}; getStLinkList returned {count}.");

                            DebugConnectParameters parameters =
                                Marshal.PtrToStructure<DebugConnectParameters>(stLinkList);

                            parameters.DbgPort = SwdPort;

                            int connectResult = TryConnectStLink(parameters, out string attempts);

                            if (connectResult != 0)
                                throw new InvalidOperationException(
                                    $"{pathInfo}; getStLinkList returned {count}; " +
                                    $"ST-LINK SN: {parameters.GetSerialNumber()}; " +
                                    $"FW: {parameters.GetFirmwareVersion()}; " +
                                    $"Target voltage: {parameters.GetTargetVoltage()}; " +
                                    $"AP: {parameters.AccessPort}/{parameters.AccessPortNumber}; " +
                                    $"Frequency: {parameters.Frequency}; " +
                                    $"connect attempts: {attempts}.");

                            return new Stm32CubeProgrammerApi
                            {
                                _apiLibDirectory = apiLibDirectory,
                                _connected = true
                            };
                        });
                }
            }

            private static int TryConnectStLink(DebugConnectParameters baseParameters, out string attempts)
            {
                var output = new StringBuilder();

                int[] modes =
                {
                    UnderResetMode,
                    HotplugMode,
                    NormalMode
                };

                int[] sharedModes =
                {
                    baseParameters.Shared,
                    0,
                    1
                };

                foreach (int shared in sharedModes.Distinct())
                {
                    foreach (int mode in modes)
                    {
                        DebugConnectParameters parameters = baseParameters;
                        parameters.ConnectionMode = mode;
                        parameters.Shared = shared;

                        int result = NativeMethods.ConnectStLink(parameters);
                        output.Append($"{GetConnectModeName(mode)}/shared={shared}:{result}; ");

                        if (result == 0)
                        {
                            attempts = output.ToString();
                            return 0;
                        }

                        NativeMethods.Disconnect();
                    }
                }

                attempts = output.ToString();
                return -1;
            }

            private static string GetConnectModeName(int mode)
            {
                return mode switch
                {
                    UnderResetMode => "UR",
                    HotplugMode => "HOTPLUG",
                    NormalMode => "NORMAL",
                    _ => mode.ToString(CultureInfo.InvariantCulture)
                };
            }

            public DeviceInfo GetDeviceInfo()
            {
                lock (NativeLock)
                {
                    return WithApiWorkingDirectory(
                        _apiLibDirectory,
                        () =>
                        {
                            IntPtr generalInfoPtr = NativeMethods.GetDeviceGeneralInf();

                            if (generalInfoPtr == IntPtr.Zero)
                                return new DeviceInfo(string.Empty);

                            GeneralInf generalInfo = Marshal.PtrToStructure<GeneralInf>(generalInfoPtr);
                            string deviceName = generalInfo.GetName();

                            return new DeviceInfo(deviceName);
                        });
                }
            }

            public int DownloadFile(string firmwarePath, uint address, bool skipErase, bool verify)
            {
                lock (NativeLock)
                {
                    return WithApiWorkingDirectory(
                        _apiLibDirectory,
                        () => NativeMethods.DownloadFile(
                            firmwarePath,
                            address,
                            skipErase ? 1u : 0u,
                            verify ? 1u : 0u,
                            string.Empty));
                }
            }

            public int SendOptionBytesCommand(string command)
            {
                lock (NativeLock)
                {
                    return WithApiWorkingDirectory(
                        _apiLibDirectory,
                        () =>
                        {
                            byte[] bytes = Encoding.ASCII.GetBytes(command + '\0');
                            return NativeMethods.SendOptionBytesCmd(bytes);
                        });
                }
            }

            public int Reset()
            {
                lock (NativeLock)
                {
                    return WithApiWorkingDirectory(
                        _apiLibDirectory,
                        () => NativeMethods.Reset(SoftwareReset));
                }
            }

            public void Dispose()
            {
                if (_connected)
                {
                    lock (NativeLock)
                    {
                        WithApiWorkingDirectory(
                            _apiLibDirectory,
                            () =>
                            {
                                NativeMethods.Disconnect();
                                return 0;
                            });
                    }

                    _connected = false;
                }

                lock (NativeLock)
                {
                    WithApiWorkingDirectory(
                        _apiLibDirectory,
                        () =>
                        {
                            NativeMethods.DeleteInterfaceList();
                            return 0;
                        });
                }
            }

            private static void ConfigureLoadPath(string apiLibDirectory)
            {
                if (_loadPathConfigured)
                    return;

                if (!Directory.Exists(apiLibDirectory))
                    throw new DirectoryNotFoundException(apiLibDirectory);

                if (!NativeMethods.SetDllDirectory(apiLibDirectory))
                    throw new InvalidOperationException($"SetDllDirectory failed for {apiLibDirectory}.");

                _loadPathConfigured = true;
            }

            private static T WithApiWorkingDirectory<T>(string apiLibDirectory, Func<T> action)
            {
                string previousDirectory = Directory.GetCurrentDirectory();

                try
                {
                    Directory.SetCurrentDirectory(apiLibDirectory);
                    return action();
                }
                finally
                {
                    Directory.SetCurrentDirectory(previousDirectory);
                }
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
            private unsafe struct Frequencies
            {
                public fixed uint JtagFreq[12];
                public uint JtagFreqNumber;
                public fixed uint SwdFreq[12];
                public uint SwdFreqNumber;
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
            private unsafe struct DebugConnectParameters
            {
                public int DbgPort;
                public int Index;
                public fixed byte SerialNumber[33];
                public fixed byte FirmwareVersion[20];
                public fixed byte TargetVoltage[5];
                public int AccessPortNumber;
                public int AccessPort;
                public int ConnectionMode;
                public int ResetMode;
                public int IsOldFirmware;
                public Frequencies Freq;
                public int Frequency;
                public int IsBridge;
                public int Shared;
                public fixed byte Board[100];
                public int DbgSleep;
                public int Speed;

                public string GetSerialNumber()
                {
                    fixed (byte* ptr = SerialNumber)
                        return ReadFixedString(ptr, 33);
                }

                public string GetFirmwareVersion()
                {
                    fixed (byte* ptr = FirmwareVersion)
                        return ReadFixedString(ptr, 20);
                }

                public string GetTargetVoltage()
                {
                    fixed (byte* ptr = TargetVoltage)
                        return ReadFixedString(ptr, 5);
                }
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
            private unsafe struct GeneralInf
            {
                public ushort DeviceId;
                public int FlashSize;
                public int BootloaderVersion;
                public fixed byte Type[4];
                public fixed byte Cpu[20];
                public fixed byte Name[100];
                public fixed byte Series[100];
                public fixed byte Description[150];
                public fixed byte RevisionId[8];
                public fixed byte Board[100];

                public string GetName()
                {
                    fixed (byte* ptr = Name)
                        return ReadFixedString(ptr, 100);
                }
            }

            private static unsafe string ReadFixedString(byte* value, int length)
            {
                int actualLength = 0;

                while (actualLength < length && value[actualLength] != 0)
                    actualLength++;

                return Encoding.ASCII.GetString(value, actualLength);
            }

            private static class NativeMethods
            {
                private const string DllName = "CubeProgrammer_API.dll";

                [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
                [return: MarshalAs(UnmanagedType.Bool)]
                public static extern bool SetDllDirectory(string lpPathName);

                [DllImport(DllName, EntryPoint = "getStLinkList", CallingConvention = CallingConvention.Cdecl)]
                public static extern int GetStLinkList(out IntPtr stLinkList, int shared);

                [DllImport(DllName, EntryPoint = "connectStLink", CallingConvention = CallingConvention.Cdecl)]
                public static extern int ConnectStLink(DebugConnectParameters debugParameters);

                [DllImport(DllName, EntryPoint = "getDeviceGeneralInf", CallingConvention = CallingConvention.Cdecl)]
                public static extern IntPtr GetDeviceGeneralInf();

                [DllImport(DllName, EntryPoint = "downloadFile", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
                public static extern int DownloadFile(
                    [MarshalAs(UnmanagedType.LPWStr)] string filePath,
                    uint address,
                    uint skipErase,
                    uint verify,
                    [MarshalAs(UnmanagedType.LPWStr)] string binPath);

                [DllImport(DllName, EntryPoint = "sendOptionBytesCmd", CallingConvention = CallingConvention.Cdecl)]
                public static extern int SendOptionBytesCmd(byte[] command);

                [DllImport(DllName, EntryPoint = "reset", CallingConvention = CallingConvention.Cdecl)]
                public static extern int Reset(int resetMode);

                [DllImport(DllName, EntryPoint = "disconnect", CallingConvention = CallingConvention.Cdecl)]
                public static extern void Disconnect();

                [DllImport(DllName, EntryPoint = "deleteInterfaceList", CallingConvention = CallingConvention.Cdecl)]
                public static extern void DeleteInterfaceList();

                [DllImport(DllName, EntryPoint = "setVerbosityLevel", CallingConvention = CallingConvention.Cdecl)]
                public static extern void SetVerbosityLevel(int level);

                [DllImport(DllName, EntryPoint = "setLoadersPath", CallingConvention = CallingConvention.Cdecl)]
                public static extern void SetLoadersPath(
                    [MarshalAs(UnmanagedType.LPStr)] string path);
            }
        }
    }
}
