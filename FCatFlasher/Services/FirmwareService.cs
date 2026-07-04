using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace FCatFlasher.Services
{
/// <summary>
/// Сервіс низькорівневих операцій STM32CubeProgrammer API: MCU, RDP, flash і reset.
/// </summary>
    internal class FirmwareService
    {
        // private const string DefaultApiDirectory = "E:\\InicaalDG\\Cube\\api";
        private const string DefaultApiDirectory = "C:\\Program Files\\STMicroelectronics\\STM32Cube\\STM32CubeProgrammer\\api";
        private const uint FlashBaseAddress = 0x08000000;
        private const int ApiInitialConnectionSettleDelayMs = 100;
        private const int ApiErrorSessionSettleDelayMs = 150;
        private const int ApiRetryConnectionSettleDelayMs = 150;

        public string LastConnectionError { get; private set; } = string.Empty;
        public string LastMcuName { get; private set; } = string.Empty;

        /// <summary>
        /// Зчитує тип MCU через STM32CubeProgrammer API.
        /// </summary>
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

            return apiResult;
        }

        /// <summary>
        /// Перевіряє, чи ST-LINK бачить MCU і повертає причину помилки в LastConnectionError.
        /// </summary>
        public async Task<bool> CheckConnection()
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

                bool apiConnected = apiResult == "OK";
                LastConnectionError = apiConnected ? string.Empty : FormatConnectionCheckError(apiResult);
                return apiConnected;
            }
            catch (Exception ex)
            {
                LastConnectionError = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Перевіряє присутність плати на стенді через API-підключення ST-LINK.
        /// </summary>
        public async Task<bool> CheckBoardPresence()
        {
            try
            {
                string apiResult = await RunApi(
                    "CHECK BOARD PRESENCE",
                    _ => "OK");

                bool apiConnected = apiResult == "OK";
                LastConnectionError = apiConnected ? string.Empty : FormatConnectionCheckError(apiResult);
                return apiConnected;
            }
            catch (Exception ex)
            {
                LastConnectionError = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Знімає RDP-захист перед тестовою прошивкою.
        /// </summary>
        public async Task<string> UnlockRdp()
        {
            string apiResult = await RunApi(
                "UNLOCK RDP",
                api =>
                {
                    int result = api.SendOptionBytesCommand("-ob rdp=0xaa");
                    return FormatApiResult("RDP 0xAA", result);
                });

            return apiResult;
        }

        /// <summary>
        /// В одному API-сеансі визначає MCU і знімає RDP.
        /// </summary>
        public async Task<RdpUnlockSessionResult> GetDeviceTypeAndUnlockRdp()
        {
            string detectedDeviceType = "UNKNOWN";

            string apiResult = await RunApi(
                "GET DEVICE TYPE AND UNLOCK RDP",
                api =>
                {
                    var output = new StringBuilder();

                    DeviceInfo deviceInfo = api.GetDeviceInfo();
                    StoreMcuInfo(deviceInfo);
                    detectedDeviceType = GetDeviceTypeFromName(deviceInfo.Name);

                    output.AppendLine($"MCU: {detectedDeviceType}");

                    if (detectedDeviceType == "UNKNOWN")
                    {
                        output.AppendLine("ERROR: Unknown MCU");
                        return output.ToString();
                    }

                    output.AppendLine("=== UNLOCK RDP 0xAA ===");

                    int rdpResult = api.SendOptionBytesCommand("-ob rdp=0xaa");
                    output.Append(FormatApiResult("RDP 0xAA", rdpResult));

                    return output.ToString();
                });

            return new RdpUnlockSessionResult(detectedDeviceType, apiResult);
        }

        /// <summary>
        /// Записує HEX-файл у flash-пам'ять і опційно виконує reset.
        /// </summary>
        public async Task<string> WriteFirmware(string firmwarePath, bool resetAfterWrite)
        {
            string apiResult = await RunApi(
                $"WRITE FIRMWARE: {firmwarePath}",
                api =>
                {
                    int downloadResult = api.DownloadFile(firmwarePath, FlashBaseAddress, skipErase: false, verify: false);

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

            return apiResult;
        }

        /// <summary>
        /// Встановлює RDP 0xBB після виробничої прошивки.
        /// </summary>
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

            return apiResult;
        }

        /// <summary>
        /// Виконує повну виробничу прошивку через API.
        /// </summary>
        public async Task<string> FlashProductionFirmware(string firmwarePath)
        {
            var output = new StringBuilder();

            output.AppendLine($"=== FLASH PRODUCTION FIRMWARE: {firmwarePath} ===");

            string apiResult = await RunApi(
                $"FLASH PRODUCTION FIRMWARE: {firmwarePath}",
                api => FlashProductionFirmwareInSession(api, firmwarePath));

            output.AppendLine(apiResult);
            return output.ToString();
        }

        /// <summary>
        /// В одному API-сеансі перевіряє MCU, прошиває файл і повертає тип пристрою.
        /// </summary>
        public async Task<ProductionFlashSessionResult> FlashProductionFirmwareSession(
            string firmwarePath,
            Func<string, string> validateDeviceType)
        {
            var output = new StringBuilder();
            string detectedDeviceType = "UNKNOWN";

            output.AppendLine($"=== FLASH PRODUCTION FIRMWARE: {firmwarePath} ===");

            string apiResult = await RunApi(
                $"FLASH PRODUCTION FIRMWARE SESSION: {firmwarePath}",
                api =>
                {
                    var apiOutput = new StringBuilder();

                    DeviceInfo deviceInfo = api.GetDeviceInfo();
                    StoreMcuInfo(deviceInfo);
                    detectedDeviceType = GetDeviceTypeFromName(deviceInfo.Name);

                    apiOutput.AppendLine($"MCU: {detectedDeviceType}");

                    string validationError = validateDeviceType(detectedDeviceType);

                    if (!string.IsNullOrWhiteSpace(validationError))
                    {
                        apiOutput.Append(validationError);
                        return apiOutput.ToString();
                    }

                    apiOutput.Append(FlashProductionFirmwareInSession(api, firmwarePath));
                    return apiOutput.ToString();
                });

            output.AppendLine(apiResult);

            return new ProductionFlashSessionResult(detectedDeviceType, output.ToString());
        }

        public readonly record struct ProductionFlashSessionResult(
            string DeviceType,
            string Output);

        public readonly record struct RdpUnlockSessionResult(
            string DeviceType,
            string Output);

        private static string FlashProductionFirmwareInSession(
            Stm32CubeProgrammerApi api,
            string firmwarePath)
        {
            var apiOutput = new StringBuilder();

            int downloadResult = api.DownloadFile(
                firmwarePath,
                FlashBaseAddress,
                skipErase: false,
                verify: false);

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

        /// <summary>
        /// Виконує операцію STM32CubeProgrammer API у фоновому потоці та нормалізує помилки.
        /// </summary>
        private async Task<string> RunApi(string title, Func<Stm32CubeProgrammerApi, string> action)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var api = Stm32CubeProgrammerApi.Connect(GetApiLibDirectory());
                    string result = action(api);
                    SettleAfterApiError(result);
                    return result;
                }
                catch (DllNotFoundException ex)
                {
                    return SettleAndReturnApiError(FormatApiUnavailable(ex.Message));
                }
                catch (EntryPointNotFoundException ex)
                {
                    return SettleAndReturnApiError(FormatApiUnavailable(ex.Message));
                }
                catch (BadImageFormatException ex)
                {
                    return SettleAndReturnApiError(FormatApiUnavailable(ex.Message));
                }
                catch (FileNotFoundException ex)
                {
                    return SettleAndReturnApiError(FormatApiUnavailable(ex.Message));
                }
                catch (DirectoryNotFoundException ex)
                {
                    return SettleAndReturnApiError(FormatApiUnavailable(ex.Message));
                }
                catch (InvalidOperationException ex)
                {
                    return SettleAndReturnApiError(FormatApiUnavailable(ex.Message));
                }
                catch (Exception ex)
                {
                    return SettleAndReturnApiError($"ERROR: STM32CubeProgrammer API {title} failed: {ex.Message}");
                }
            });
        }

        private static string SettleAndReturnApiError(string result)
        {
            Thread.Sleep(ApiErrorSessionSettleDelayMs);
            return result;
        }

        private static void SettleAfterApiError(string result)
        {
            if (IsApiErrorResult(result))
                Thread.Sleep(ApiErrorSessionSettleDelayMs);
        }

        private static bool IsApiErrorResult(string result)
        {
            return result.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ||
                   result.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                   result.Contains("timeout", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Повертає папку бібліотек STM32CubeProgrammer API.
        /// </summary>
        private string GetApiLibDirectory()
        {
            string defaultApiLibDirectory = Path.Combine(DefaultApiDirectory, "lib");

            if (Directory.Exists(defaultApiLibDirectory))
                return defaultApiLibDirectory;

            throw new DirectoryNotFoundException($"STM32CubeProgrammer API lib directory was not found: {defaultApiLibDirectory}");
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

        /// <summary>
        /// Перетворює сире повідомлення API у зрозумілу оператору помилку підключення.
        /// </summary>
        private static string FormatConnectionCheckError(string result)
        {
            if (result.Contains("connect attempts:", StringComparison.OrdinalIgnoreCase) &&
                result.Contains("Target voltage:", StringComparison.OrdinalIgnoreCase))
            {
                return "ST-LINK OK, target MCU not connected";
            }

            return result;
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
                            bool loadPathWasConfigured = ConfigureLoadPath(apiLibDirectory);

                            NativeMethods.SetLoadersPath("./.");
                            NativeMethods.SetVerbosityLevel(0);

                            if (loadPathWasConfigured)
                                Thread.Sleep(ApiInitialConnectionSettleDelayMs);

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

                for (int attempt = 1; attempt <= 2; attempt++)
                {
                    if (attempt > 1)
                    {
                        output.Append("retry after settle; ");
                        Thread.Sleep(ApiRetryConnectionSettleDelayMs);
                    }

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

            private static bool ConfigureLoadPath(string apiLibDirectory)
            {
                if (_loadPathConfigured)
                    return false;

                if (!Directory.Exists(apiLibDirectory))
                    throw new DirectoryNotFoundException(apiLibDirectory);

                if (!NativeMethods.SetDllDirectory(apiLibDirectory))
                    throw new InvalidOperationException($"SetDllDirectory failed for {apiLibDirectory}.");

                _loadPathConfigured = true;
                return true;
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
