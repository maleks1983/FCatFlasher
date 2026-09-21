using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace FCatFlasher.Services
{
/// <summary>
/// РЎРµСЂРІС–СЃ РЅРёР·СЊРєРѕСЂС–РІРЅРµРІРёС… РѕРїРµСЂР°С†С–Р№ STM32CubeProgrammer API: MCU, RDP, flash С– reset.
/// </summary>
    internal class FirmwareService
    {
        private const uint FlashBaseAddress = 0x08000000;
        private const string RdpUnlockCommand = "-ob rdp=0xaa";
        private const string RdpProtectCommand = "-ob rdp=0xbb";
        private const int ApiInitialConnectionSettleDelayMs = 100;
        private const int ApiErrorSessionSettleDelayMs = 150;
        private const int ApiRetryConnectionSettleDelayMs = 150;
        private string? _validatedApiLibDirectory;

        public string LastConnectionError { get; private set; } = string.Empty;
        public string LastMcuName { get; private set; } = string.Empty;
        public CancellationToken RunCancellationToken { get; set; }

        /// <summary>
        /// Р—С‡РёС‚СѓС” С‚РёРї MCU С‡РµСЂРµР· STM32CubeProgrammer API.
        /// </summary>
        public virtual async Task<string> GetDeviceType()
        {
            string apiResult = await RunApi(
                "GET DEVICE TYPE",
                DetectDeviceType);

            return apiResult;
        }

        /// <summary>
        /// РџРµСЂРµРІС–СЂСЏС”, С‡Рё ST-LINK Р±Р°С‡РёС‚СЊ MCU С– РїРѕРІРµСЂС‚Р°С” РїСЂРёС‡РёРЅСѓ РїРѕРјРёР»РєРё РІ LastConnectionError.
        /// </summary>
        public virtual async Task<bool> CheckConnection()
        {
            return await CheckConnectionCore(
                "CHECK CONNECTION",
                api =>
                {
                    DeviceInfo deviceInfo = api.GetDeviceInfo();

                    if (deviceInfo.Name.Length == 0)
                        return "ERROR: STM32CubeProgrammer API connected, but device info is empty";

                    StoreMcuInfo(deviceInfo);
                    return "OK";
                });
        }

        /// <summary>
        /// РџРµСЂРµРІС–СЂСЏС” РїСЂРёСЃСѓС‚РЅС–СЃС‚СЊ РїР»Р°С‚Рё РЅР° СЃС‚РµРЅРґС– С‡РµСЂРµР· API-РїС–РґРєР»СЋС‡РµРЅРЅСЏ ST-LINK.
        /// </summary>
        public virtual async Task<bool> CheckBoardPresence()
        {
            return await CheckConnectionCore(
                "CHECK BOARD PRESENCE",
                _ => "OK",
                retryConnection: false);
        }

        private async Task<bool> CheckConnectionCore(
            string title,
            Func<Stm32CubeProgrammerApi, string> check,
            bool retryConnection = true)
        {
            try
            {
                string apiResult = await RunApi(title, check, retryConnection);

                bool apiConnected = apiResult == "OK";
                LastConnectionError = apiConnected ? string.Empty : FormatConnectionCheckError(apiResult);
                return apiConnected;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LastConnectionError = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Р—РЅС–РјР°С” RDP-Р·Р°С…РёСЃС‚ РїРµСЂРµРґ С‚РµСЃС‚РѕРІРѕСЋ РїСЂРѕС€РёРІРєРѕСЋ.
        /// </summary>
        public virtual async Task<string> UnlockRdp()
        {
            string apiResult = await RunApi(
                "UNLOCK RDP",
                api =>
                {
                    int result = api.SendOptionBytesCommand(RdpUnlockCommand);
                    return FormatApiResult("RDP 0xAA", result);
                });

            return apiResult;
        }

        /// <summary>
        /// Р’ РѕРґРЅРѕРјСѓ API-СЃРµР°РЅСЃС– РІРёР·РЅР°С‡Р°С” MCU С– Р·РЅС–РјР°С” RDP.
        /// </summary>
        public virtual async Task<RdpUnlockSessionResult> GetDeviceTypeAndUnlockRdp()
        {
            string detectedDeviceType = "UNKNOWN";

            string apiResult = await RunApi(
                "GET DEVICE TYPE AND UNLOCK RDP",
                api =>
                {
                    var output = new StringBuilder();

                    detectedDeviceType = DetectDeviceType(api);

                    output.AppendLine($"MCU: {detectedDeviceType}");

                    if (detectedDeviceType == "UNKNOWN")
                    {
                        output.AppendLine("ERROR: Unknown MCU");
                        return output.ToString();
                    }

                    output.AppendLine("=== UNLOCK RDP 0xAA ===");

                    int rdpResult = api.SendOptionBytesCommand(RdpUnlockCommand);
                    output.Append(FormatApiResult("RDP 0xAA", rdpResult));

                    return output.ToString();
                });

            return new RdpUnlockSessionResult(detectedDeviceType, apiResult);
        }

        /// <summary>
        /// Р—Р°РїРёСЃСѓС” HEX-С„Р°Р№Р» Сѓ flash-РїР°Рј'СЏС‚СЊ С– РѕРїС†С–Р№РЅРѕ РІРёРєРѕРЅСѓС” reset.
        /// </summary>
        public virtual async Task<string> WriteFirmware(string firmwarePath, bool resetAfterWrite)
        {
            string apiResult = await RunApi(
                $"WRITE FIRMWARE: {firmwarePath}",
                api =>
                {
                    int downloadResult = DownloadFirmware(api, firmwarePath);

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
        /// Р’РёРєРѕРЅСѓС” РїРѕРІРЅСѓ РІРёСЂРѕР±РЅРёС‡Сѓ РїСЂРѕС€РёРІРєСѓ С‡РµСЂРµР· API.
        /// </summary>
        public virtual async Task<string> FlashProductionFirmware(string firmwarePath)
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
        /// Р’ РѕРґРЅРѕРјСѓ API-СЃРµР°РЅСЃС– РїРµСЂРµРІС–СЂСЏС” MCU, РїСЂРѕС€РёРІР°С” С„Р°Р№Р» С– РїРѕРІРµСЂС‚Р°С” С‚РёРї РїСЂРёСЃС‚СЂРѕСЋ.
        /// </summary>
        public virtual async Task<ProductionFlashSessionResult> FlashProductionFirmwareSession(
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

                    detectedDeviceType = DetectDeviceType(api);

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

            int downloadResult = DownloadFirmware(api, firmwarePath);

            apiOutput.AppendLine(FormatApiResult("DOWNLOAD", downloadResult));

            if (downloadResult != 0)
                return apiOutput.ToString();

            apiOutput.AppendLine("=== SET RDP 0xBB ===");

            int rdpResult = api.SendOptionBytesCommand(RdpProtectCommand);
            apiOutput.AppendLine(FormatApiResult("RDP 0xBB", rdpResult));

            if (rdpResult != 0)
                return apiOutput.ToString();

            int resetResult = api.Reset();
            apiOutput.Append(FormatApiResult("RESET", resetResult));

            return apiOutput.ToString();
        }

        private static int DownloadFirmware(
            Stm32CubeProgrammerApi api,
            string firmwarePath)
        {
            return api.DownloadFile(
                firmwarePath,
                FlashBaseAddress,
                skipErase: false,
                verify: false);
        }

        private void StoreMcuInfo(DeviceInfo deviceInfo)
        {
            if (!string.IsNullOrWhiteSpace(deviceInfo.Name))
                LastMcuName = deviceInfo.Name;
        }

        private string DetectDeviceType(Stm32CubeProgrammerApi api)
        {
            DeviceInfo deviceInfo = api.GetDeviceInfo();
            StoreMcuInfo(deviceInfo);
            return GetDeviceTypeFromName(deviceInfo.Name);
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
        /// Р’РёРєРѕРЅСѓС” РѕРїРµСЂР°С†С–СЋ STM32CubeProgrammer API Сѓ С„РѕРЅРѕРІРѕРјСѓ РїРѕС‚РѕС†С– С‚Р° РЅРѕСЂРјР°Р»С–Р·СѓС” РїРѕРјРёР»РєРё.
        /// </summary>
        private async Task<string> RunApi(string title, Func<Stm32CubeProgrammerApi, string> action,
            bool retryConnection = true)
        {
            CancellationToken cancellationToken = RunCancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            string output = await Task.Run(() =>
            {
                try
                {
                    using var api = Stm32CubeProgrammerApi.Connect(GetApiLibDirectory(), retryConnection);
                    cancellationToken.ThrowIfCancellationRequested();
                    string result = action(api);
                    SettleAfterApiError(result);
                    return result;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (
                    ex is DllNotFoundException or
                    EntryPointNotFoundException or
                    BadImageFormatException or
                    FileNotFoundException or
                    DirectoryNotFoundException or
                    InvalidOperationException)
                {
                    return SettleAndReturnApiError(FormatApiUnavailable(ex.Message));
                }
                catch (Exception ex)
                {
                    return SettleAndReturnApiError($"ERROR: STM32CubeProgrammer API {title} failed: {ex.Message}");
                }
            }, cancellationToken);
            // Native calls are allowed to finish before cancellation is observed.
            cancellationToken.ThrowIfCancellationRequested();
            return output;
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
        /// РџРѕРІРµСЂС‚Р°С” РїР°РїРєСѓ Р±С–Р±Р»С–РѕС‚РµРє STM32CubeProgrammer API.
        /// </summary>
        private string GetApiLibDirectory()
        {
            if (_validatedApiLibDirectory != null)
                return _validatedApiLibDirectory;

            string localApiDirectory = Path.Combine(AppContext.BaseDirectory, "api", "lib");
            string localApiDirectoryRoot = Path.GetDirectoryName(localApiDirectory)!;

            if (HasCompleteApiRuntime(localApiDirectory) &&
                HasDeviceDatabase(localApiDirectoryRoot))
            {
                _validatedApiLibDirectory = localApiDirectory;
                return _validatedApiLibDirectory;
            }

            throw new DirectoryNotFoundException(
                $"Complete STM32CubeProgrammer API runtime was not found: {localApiDirectoryRoot}");
        }

        private static bool HasCompleteApiRuntime(string directory)
        {
            return File.Exists(Path.Combine(directory, "CubeProgrammer_API.dll")) &&
                   Directory.Exists(Path.Combine(directory, "FlashLoader")) &&
                   Directory.Exists(Path.Combine(directory, "ExternalLoader"));
        }

        private static bool HasDeviceDatabase(string apiDirectory)
        {
            string databaseDirectory = Path.Combine(apiDirectory, "Data_Base");

            return Directory.Exists(databaseDirectory) &&
                   Directory.EnumerateFiles(databaseDirectory, "*.xml").Any();
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
        /// РџРµСЂРµС‚РІРѕСЂСЋС” СЃРёСЂРµ РїРѕРІС–РґРѕРјР»РµРЅРЅСЏ API Сѓ Р·СЂРѕР·СѓРјС–Р»Сѓ РѕРїРµСЂР°С‚РѕСЂСѓ РїРѕРјРёР»РєСѓ РїС–РґРєР»СЋС‡РµРЅРЅСЏ.
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
            private const int HardwareReset = 1;
            private static readonly object NativeLock = new();
            private static readonly InitProgressBarCallback InitProgressBar = static () => { };
            private static readonly LogMessageCallback LogMessage = static (_, _) => { };
            private static readonly LoadBarCallback LoadBar = static (_, _) => { };
            private static bool _loadPathConfigured;
            private static bool _callbacksConfigured;
            private string _apiLibDirectory = string.Empty;
            private bool _connected;

            private Stm32CubeProgrammerApi()
            {
            }

            private static int _lastConnectionMode = NormalMode;
            private static int _lastSharedMode;

            public static Stm32CubeProgrammerApi Connect(string apiLibDirectory, bool retryConnection = true)
            {
                lock (NativeLock)
                {
                    return WithApiWorkingDirectory(
                        apiLibDirectory,
                        () =>
                        {
                            bool loadPathWasConfigured = ConfigureLoadPath(apiLibDirectory);

                            ConfigureDisplayCallbacks();
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

                            int connectResult = TryConnectStLink(parameters, out string attempts, retryConnection);

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

            private static int TryConnectStLink(DebugConnectParameters baseParameters, out string attempts,
                bool retryConnection)
            {
                var output = new StringBuilder();

                int[] modes =
                {
                    _lastConnectionMode,
                    NormalMode,
                    HotplugMode,
                    UnderResetMode
                };

                int[] sharedModes =
                {
                    _lastSharedMode,
                    baseParameters.Shared,
                    0,
                    1
                };

                // Removal polling uses the last working connection settings once.
                // An absent board must not trigger the full connection recovery sequence.
                if (!retryConnection)
                {
                    modes = new[] { _lastConnectionMode };
                    sharedModes = new[] { _lastSharedMode };
                }

                for (int attempt = 1; attempt <= (retryConnection ? 2 : 1); attempt++)
                {
                    if (attempt > 1)
                    {
                        output.Append("retry after settle; ");
                        Thread.Sleep(ApiRetryConnectionSettleDelayMs);
                    }

                    foreach (int shared in sharedModes.Distinct())
                    {
                        foreach (int mode in modes.Distinct())
                        {
                            DebugConnectParameters parameters = baseParameters;
                            parameters.ConnectionMode = mode;
                            parameters.ResetMode =
                                mode == UnderResetMode ? HardwareReset : SoftwareReset;
                            parameters.Shared = shared;

                             int result = NativeMethods.ConnectStLink(parameters);
                            output.Append($"{GetConnectModeName(mode)}/shared={shared}:{result}; ");

                            if (result == 0)
                            {
                                _lastConnectionMode = mode;
                                _lastSharedMode = shared;
                                attempts = output.ToString();
                                return 0;
                            }
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

            private static void ConfigureDisplayCallbacks()
            {
                if (_callbacksConfigured)
                    return;

                NativeMethods.SetDisplayCallbacks(
                    new DisplayCallbacks
                    {
                        InitProgressBar = InitProgressBar,
                        LogMessage = LogMessage,
                        LoadBar = LoadBar
                    });

                _callbacksConfigured = true;
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

            [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
            private delegate void InitProgressBarCallback();

            [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
            private delegate void LogMessageCallback(int messageType, IntPtr message);

            [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
            private delegate void LoadBarCallback(int current, int total);

            [StructLayout(LayoutKind.Sequential)]
            private struct DisplayCallbacks
            {
                public InitProgressBarCallback InitProgressBar;
                public LogMessageCallback LogMessage;
                public LoadBarCallback LoadBar;
            }

            private static class NativeMethods
            {
                private const string DllName = "CubeProgrammer_API.dll";

                [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
                [return: MarshalAs(UnmanagedType.Bool)]
                public static extern bool SetDllDirectory(string lpPathName);

                [DllImport(DllName, EntryPoint = "setDisplayCallbacks", CallingConvention = CallingConvention.Cdecl)]
                public static extern void SetDisplayCallbacks(DisplayCallbacks callbacks);

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
