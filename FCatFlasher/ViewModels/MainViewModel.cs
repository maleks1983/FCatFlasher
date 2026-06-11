using FCatFlasher.Helpers;
using FCatFlasher.Models;
using FCatFlasher.Services;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO.Ports;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;


namespace FCatFlasher.ViewModels
{
    internal class MainViewModel : INotifyPropertyChanged
    {
        private const int BoardRemovedCheckTimeoutMs = 100;
        private const int BoardRemovedConfirmations = 3;
        private const int StlinkPollDelayMs = 500;
        private const int CapacitorDischargeDelayMs = 500;
        private const int PowerOnSettleDelayMs = 100;
        private const int StlinkConnectionCheckTimeoutMs = 1000;
        private const int StlinkPowerRetryIntervalMs = 3000;
        private const string TestRegistrationType = "TEST";
        private const string ProductionRegistrationType = "PRODUCTION";
        private const string ReworkRegistrationType = "REWORK";

        private readonly FirmwareService _firmwareService = new();
        private readonly TestFirmwareService _testFirmwareService;
        private readonly SerialService _serial = new();
        private readonly SettingsService _settingsService = new();
        private readonly ApiService _apiService;
        private readonly LocalDatabaseService _localDatabaseService;
        private readonly CurrentUser _currentUser;
        private readonly DiagnosticsParser _diagnosticsParser = new();
        private readonly BoxTrackingService _boxTracking = new();
        private readonly UniqueBoardCounter _uniqueBoardCounter = new();
        private readonly DispatcherTimer _runTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private bool _settingsLoaded;
        private string _currentDeviceType = "";
        private DateTime _runStartedAt;

        private enum WorkMode
        {
            TestFirmware,
            Production,
            Reflash
        }

        private enum CycleState
        {
            Idle,
            WaitingBoard,
            Testing,
            WaitingRemove,
            Flashing
        }

        private WorkMode _workMode = WorkMode.Production;
        private WorkMode _footerMode = WorkMode.TestFirmware;
        private CycleState _cycleState = CycleState.Idle;
        private DateTime _lastStlinkPowerRetryUtc = DateTime.MinValue;
        private int _testedToday;
        private int _flashedToday;
        private int _reflashedToday;

        public ICommand OpenSettingsCommand { get; }
        public ICommand ConnectCommand { get; }

        public RelayCommand TestFirmwareCommand { get; }
        public RelayCommand FlashCommand { get; }
        public RelayCommand ReflashCommand { get; }

        public ICommand RefreshComPortsCommand { get; }
        public ICommand BrowseFirmwareCommand { get; }
        public ICommand BrowseG0FirmwareCommand { get; }
        public ICommand BrowseC0FirmwareCommand { get; }
        public ICommand BrowseTestFirmwareDirectoryCommand { get; }
        public ICommand BrowseStmCubeCliCommand { get; }
        public ICommand StopCommand { get; }

        public ObservableCollection<string> ComPorts { get; } = new();

        private BoxReport? _completedBox;

        public ObservableCollection<PendingRegistration> PendingRegistrations { get; } = new();

        public string CurrentUserName
        {
            get
            {
                string name = string.IsNullOrWhiteSpace(_currentUser.FullName)
                    ? _currentUser.Username
                    : GetFirstName(_currentUser.FullName);

                return _currentUser.IsOffline ? $"{name} (offline)" : name;
            }
        }

        private static string GetFirstName(string fullName)
        {
            string[] parts = fullName
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            return parts.Length >= 2 ? parts[1] : fullName.Trim();
        }

        private string _scannedSerial = "";
        public string ScannedSerial
        {
            get => _scannedSerial;
            set
            {
                _scannedSerial = value;
                OnPropertyChanged();
            }
        }

        private string _selectedComPort = "";
        public string SelectedComPort
        {
            get => _selectedComPort;
            set
            {
                _selectedComPort = value;
                OnPropertyChanged();
                SaveSettings();
            }
        }

        private string _firmwarePath = "firmware.hex";
        public string FirmwarePath
        {
            get => _firmwarePath;
            set
            {
                _firmwarePath = value;
                OnPropertyChanged();
                SaveSettings();
            }
        }

        private string _g0FirmwarePath = "";
        public string G0FirmwarePath
        {
            get => _g0FirmwarePath;
            set
            {
                _g0FirmwarePath = value;
                OnPropertyChanged();
                SaveSettings();
            }
        }

        private string _c0FirmwarePath = "";
        public string C0FirmwarePath
        {
            get => _c0FirmwarePath;
            set
            {
                _c0FirmwarePath = value;
                OnPropertyChanged();
                SaveSettings();
            }
        }

        private string _reworkOrderId = "";
        public string ReworkOrderId
        {
            get => _reworkOrderId;
            set
            {
                _reworkOrderId = value;
                OnPropertyChanged();
                SaveSettings();
            }
        }

        private string _testFirmwareDirectory = @"D:\";
        public string TestFirmwareDirectory
        {
            get => _testFirmwareDirectory;
            set
            {
                _testFirmwareDirectory = value;
                OnPropertyChanged();
                ApplyFlashSettings();
                SaveSettings();
            }
        }

        private string _stmCubeCliPath =
            @"E:\InicaalDG\STM32CubeProgrammer\bin\STM32_Programmer_CLI.exe";
        public string StmCubeCliPath
        {
            get => _stmCubeCliPath;
            set
            {
                _stmCubeCliPath = value;
                OnPropertyChanged();
                ApplyFlashSettings();
                SaveSettings();
            }
        }

        private string _apiBaseUrl = "";
        public string ApiBaseUrl
        {
            get => _apiBaseUrl;
            set
            {
                _apiBaseUrl = value;
                OnPropertyChanged();
                SaveSettings();
            }
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                _isBusy = value;
                OnPropertyChanged();

                TestFirmwareCommand.RaiseCanExecuteChanged();
                FlashCommand.RaiseCanExecuteChanged();
                ReflashCommand.RaiseCanExecuteChanged();
            }
        }

        private string _status = "Idle";
        public string Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged();
            }
        }

        private string _log = "";
        public string Log
        {
            get => _log;
            set
            {
                _log = value;
                OnPropertyChanged();
            }
        }

        private int _progressValue;
        public int ProgressValue
        {
            get => _progressValue;
            set
            {
                if (_progressValue == value)
                    return;

                _progressValue = Math.Clamp(value, 0, 100);
                OnPropertyChanged();
            }
        }

        private string _footerTitle = "Протестовано за сьогодні:";

        public string FooterTitle
        {
            get => _footerTitle;
            set
            {
                _footerTitle = value;
                OnPropertyChanged();
            }
        }
        private string _firmwareName = string.Empty;

        public string FirmwareName
        {
            get => _firmwareName;
            set
            {
                _firmwareName = value;
                OnPropertyChanged();
                SaveSettings();
            }
        }
        private int _boxQuantity;

        public int BoxQuantity
        {
            get => _boxQuantity;
            set
            {
                if (_boxQuantity == value)
                    return;

                _boxQuantity = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BoxQuantityText));
                SaveSettings();
            }
        }

        public string BoxQuantityText
        {
            get => BoxQuantity == 0
                ? string.Empty
                : BoxQuantity.ToString(CultureInfo.InvariantCulture);
        }

        public int FlashedToday
        {
            get => _footerMode switch
            {
                WorkMode.TestFirmware => _testedToday,
                WorkMode.Reflash => _reflashedToday,
                _ => _flashedToday
            };
        }

        public int UniqueFlashedSinceStart
        {
            get => _uniqueBoardCounter.Count;
        }

        private string _runElapsedText = "00:00:00";

        public string RunElapsedText
        {
            get => _runElapsedText;
            set
            {
                if (_runElapsedText == value)
                    return;

                _runElapsedText = value;
                OnPropertyChanged();
            }
        }

        public MainViewModel(
            ApiService apiService,
            LocalDatabaseService localDatabaseService,
            CurrentUser currentUser)
        {
            _apiService = apiService;
            _localDatabaseService = localDatabaseService;
            _currentUser = currentUser;
            _testFirmwareService = new TestFirmwareService(_firmwareService);

            ConnectCommand = new RelayCommand(Connect);

            TestFirmwareCommand = new RelayCommand(
                async () =>
                {
                    if (!EnsureConnected())
                        return;

                    _workMode = WorkMode.TestFirmware;
                    StartRunTimer();
                    await StartFullCycle();
                },
                () => !IsBusy
            );

            FlashCommand = new RelayCommand(
                async () =>
                {
                    if (!RequestProductionStartData())
                        return;

                    if (!EnsureConnected())
                        return;

                    _workMode = WorkMode.Production;
                    ResetUniqueFlashedCounter();
                    StartRunTimer();
                    await StartFullCycle();
                },
                () => !IsBusy
            );

            ReflashCommand = new RelayCommand(
                async () =>
                {
                    if (!RequestOrderId())
                        return;

                    if (!EnsureConnected())
                        return;

                    _workMode = WorkMode.Reflash;
                    ResetUniqueFlashedCounter();
                    StartRunTimer();
                    await StartFullCycle();
                },
                () => !IsBusy
            );

            RefreshComPortsCommand = new RelayCommand(RefreshComPorts);
            BrowseFirmwareCommand = new RelayCommand(BrowseFirmware);
            BrowseG0FirmwareCommand = new RelayCommand(BrowseG0Firmware);
            BrowseC0FirmwareCommand = new RelayCommand(BrowseC0Firmware);
            BrowseTestFirmwareDirectoryCommand = new RelayCommand(BrowseTestFirmwareDirectory);
            BrowseStmCubeCliCommand = new RelayCommand(BrowseStmCubeCli);
            OpenSettingsCommand = new RelayCommand(OpenSettings);
            StopCommand = new RelayCommand(async () => await StopAll());

            _runTimer.Tick += (_, _) => UpdateRunElapsed();
            _serial.DataReceived += OnDataReceived;

            RefreshComPorts();
            LoadSettings();
            LoadApiData();
            _settingsLoaded = true;
        }

        private void OpenSettings()
        {
            var window = new SettingsWindow
            {
                DataContext = this,
                Owner = Application.Current.MainWindow
            };

            window.ShowDialog();
        }

        public void SetFooterMode(string? tabHeader)
        {
            switch (tabHeader)
            {
                case "Тестова прошивка":
                    _footerMode = WorkMode.TestFirmware;
                    FooterTitle = "Протестовано за сьогодні:";
                    break;

                case "Перепрошивка":
                    _footerMode = WorkMode.Reflash;
                    FooterTitle = "Перепрошито за сьогодні:";
                    break;

                default:
                    _footerMode = WorkMode.Production;
                    FooterTitle = "Прошито за сьогодні:";
                    break;
            }

            OnPropertyChanged(nameof(FlashedToday));
        }

        private bool RequestOrderId()
        {
            var window = new OrderIdWindow(ReworkOrderId, FirmwareName)
            {
                Owner = Application.Current.MainWindow
            };

            if (window.ShowDialog() != true)
                return false;

            ReworkOrderId = window.OrderId;
            LoadReworkOrderData(window.FirmwareName);
            return true;
        }

        private void LoadReworkOrderData(string fallbackFirmwareName)
        {
            try
            {
                ReworkOrderInfo? order = _apiService.LoadReworkOrder(ReworkOrderId);

                if (order == null)
                {
                    FirmwareName = fallbackFirmwareName;
                    return;
                }

                FirmwareName = order.FirmwareName;
            }
            catch (Exception ex)
            {
                FirmwareName = fallbackFirmwareName;
                Log += "ORDER API ERROR: " + ex.Message + "\n";
            }
        }

        private bool RequestProductionStartData()
        {
            var window = new ProductionStartWindow(FirmwareName, BoxQuantity)
            {
                Owner = Application.Current.MainWindow
            };

            if (window.ShowDialog() != true)
                return false;

            FirmwareName = window.FirmwareName;
            BoxQuantity = window.BoxQuantity;
            return true;
        }

        private void RefreshComPorts()
        {
            ComPorts.Clear();

            foreach (string port in SerialPort.GetPortNames())
                ComPorts.Add(port);

            if (ComPorts.Count > 0 && string.IsNullOrWhiteSpace(SelectedComPort))
                SelectedComPort = ComPorts[0];
        }

        private string? BrowseFirmwareFile()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Оберіть прошивку",
                Filter = "Firmware files (*.hex;*.bin)|*.hex;*.bin|HEX files (*.hex)|*.hex|BIN files (*.bin)|*.bin|All files (*.*)|*.*"
            };

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        private void BrowseFirmware()
        {
            string? path = BrowseFirmwareFile();

            if (path != null)
                FirmwarePath = path;
        }

        private void BrowseG0Firmware()
        {
            string? path = BrowseFirmwareFile();

            if (path != null)
                G0FirmwarePath = path;
        }

        private void BrowseC0Firmware()
        {
            string? path = BrowseFirmwareFile();

            if (path != null)
                C0FirmwarePath = path;
        }

        private void BrowseTestFirmwareDirectory()
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Оберіть папку тестових прошивок",
                Multiselect = false
            };

            if (!string.IsNullOrWhiteSpace(TestFirmwareDirectory))
                dialog.InitialDirectory = TestFirmwareDirectory;

            if (dialog.ShowDialog() == true)
                TestFirmwareDirectory = dialog.FolderName;
        }

        private void BrowseStmCubeCli()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Оберіть STM32CubeProgrammer CLI",
                Filter = "STM32Cube CLI (STM32_Programmer_CLI.exe)|STM32_Programmer_CLI.exe|Executable files (*.exe)|*.exe|All files (*.*)|*.*"
            };

            string? initialDirectory = System.IO.Path.GetDirectoryName(StmCubeCliPath);

            if (!string.IsNullOrWhiteSpace(initialDirectory))
                dialog.InitialDirectory = initialDirectory;

            if (dialog.ShowDialog() == true)
                StmCubeCliPath = dialog.FileName;
        }

        private bool EnsureConnected()
        {
            if (_serial.IsOpen)
                return true;

            if (string.IsNullOrWhiteSpace(SelectedComPort))
            {
                Status = "No COM";
                Log += "COM port not selected\n";
                return false;
            }

            try
            {
                _serial.Open(SelectedComPort, 115200);

                Status = "Connected";
                Log += $"Connected to {SelectedComPort}\n";

                return true;
            }
            catch (Exception ex)
            {
                Status = "COM Error";
                Log += "ERROR: " + ex.Message + "\n";
                return false;
            }
        }

        private bool EnsureSerialEntered()
        {
            if (!string.IsNullOrWhiteSpace(ScannedSerial))
                return true;

            Status = "No serial";
            Log += "ERROR: Serial number is empty\n";
            MessageBox.Show(
                "Введіть серійний номер плати.",
                "Серійний номер",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return false;
        }

        private void Connect()
        {
            EnsureConnected();
        }

        private void StartRunTimer()
        {
            _runStartedAt = DateTime.Now;
            RunElapsedText = "00:00:00";
            _runTimer.Start();
        }

        private void StopRunTimer()
        {
            _runTimer.Stop();
        }

        private void UpdateRunElapsed()
        {
            TimeSpan elapsed = DateTime.Now - _runStartedAt;
            RunElapsedText = elapsed.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        }

        private void ResetCycleData()
        {
            ScannedSerial = "";
            _lastStlinkPowerRetryUtc = DateTime.MinValue;

            _diagnosticsParser.Reset();

            try
            {
                _serial.ClearBuffers();
            }
            catch
            {
            }
        }

        private async Task StartFullCycle()
        {
            try
            {
                Log = "";
                ProgressValue = 0;

                IsBusy = true;
                _cycleState = CycleState.WaitingBoard;

                ResetCycleData();

                Log += "\n--- WAIT BOARD ---\n";

                await PowerCycleToStlinkMode();

                await WaitBoardInserted();
            }
            catch (Exception ex)
            {
                Status = "Error";
                Log += "ERROR: " + ex.Message + "\n";
                StopRunTimer();
                IsBusy = false;
                _cycleState = CycleState.Idle;
            }
        }

        private async Task WaitBoardInserted()
        {
            Status = "Waiting board";

            while (_cycleState == CycleState.WaitingBoard)
            {
                bool present = await _firmwareService.CheckConnection();

                if (present)
                {
                    ProgressValue = 20;
                    Log += "=== ПЛАТУ ЗНАЙДЕНО ===\n";

                    if (_workMode == WorkMode.TestFirmware)
                    {
                        await FlashOnlyTestFirmware();
                        return;
                    }

                    if (_workMode == WorkMode.Reflash)
                    {
                        await PrepareReflashWithTestFirmware();
                        return;
                    }

                    await StartTest();
                    return;
                }

                await RetryStlinkPowerIfDue();
                await Task.Delay(StlinkPollDelayMs);
            }
        }

        private async Task FlashOnlyTestFirmware()
        {
            try
            {
                _cycleState = CycleState.Flashing;

                await PowerCycleToStlinkMode();
                if (!await EnsureStlinkReady("test firmware"))
                {
                    _cycleState = CycleState.WaitingRemove;
                    await WaitBoardRemoved();
                    return;
                }

                Status = "Flashing test firmware";
                ProgressValue = 50;
                Log += "=== ТЕСТОВА ПРОШИВКА ===\n";

                string result = await _testFirmwareService.FlashAutoTestFirmware();

                if (TestFirmwareFailed(result))
                {
                    Status = "Test firmware error";
                    ProgressValue = 100;
                    Log += "=== ТЕСТОВУ ПРОШИВКУ НЕ ЗАЛИТО ===\n";
                    Log += ">>> Заберіть плату зі стенда...\n";
                    ShowTestErrorDialog("Помилка тестової прошивки", result);

                    _cycleState = CycleState.WaitingRemove;

                    await WaitBoardRemoved();
                    return;
                }

                _serial.Send("BUZZ_OK");
                await Task.Delay(100);

                Status = "Test firmware OK";
                ProgressValue = 100;

                RegisterDeviceInDatabase("");

                Log += "\n=== ТЕСТОВУ ПРОШИВКУ ЗАВЕРШЕНО ===\n";
                Log += ">>> Заберіть плату зі стенда...\n";

                _cycleState = CycleState.WaitingRemove;

                await WaitBoardRemoved();
            }
            catch (Exception ex)
            {
                Status = "Error";
                Log += "ERROR: " + ex.Message + "\n";

                _cycleState = CycleState.WaitingRemove;

                await WaitBoardRemoved();
            }
        }

        private async Task PrepareReflashWithTestFirmware()
        {
            try
            {
                _cycleState = CycleState.Flashing;

                await PowerCycleToStlinkMode();
                if (!await EnsureStlinkReady("prepare reflash"))
                {
                    _cycleState = CycleState.WaitingRemove;
                    await WaitBoardRemoved();
                    return;
                }

                Status = "Preparing reflash";
                ProgressValue = 30;
                Log += "=== ПЕРЕПРОШИВКА: СПОЧАТКУ ТЕСТОВА ===\n";

                _currentDeviceType = await _firmwareService.GetDeviceType();

                if (_currentDeviceType == "UNKNOWN")
                {
                    Log += "ERROR: Unknown MCU\n";
                    await StopAll();
                    return;
                }

                Log += $"MCU: {_currentDeviceType}\n";

                string result = await FlashAutoTestFirmwareWithRetry(_currentDeviceType);

                if (TestFirmwareFailed(result))
                {
                    Status = "Test firmware error";
                    ProgressValue = 100;
                    Log += "\n=== ТЕСТОВУ ПРОШИВКУ НЕ ЗАЛИТО ===\n";
                    Log += ">>> Заберіть плату зі стенда...\n";
                    ShowTestErrorDialog("Помилка тестової прошивки", result);

                    _cycleState = CycleState.WaitingRemove;

                    await WaitBoardRemoved();
                    return;
                }

                Log += "\n=== ТЕСТОВУ ПРОШИВКУ ЗАЛИТО ===\n";

                await Task.Delay(100);

                await StartTest();
            }
            catch (Exception ex)
            {
                Status = "Error";
                Log += "ERROR: " + ex.Message + "\n";

                _cycleState = CycleState.WaitingRemove;

                await WaitBoardRemoved();
            }
        }

        private async Task<string> FlashAutoTestFirmwareWithRetry(string deviceType)
        {
            string result = await _testFirmwareService.FlashTestFirmwareForDevice(deviceType);

            if (!TestFirmwareFailed(result))
                return result;

            Log += "\nWARNING: MCU не визначився після RDP unlock. Робимо power cycle і пробуємо ще раз...\n";

            await PowerCycleToStlinkMode();

            return await _testFirmwareService.FlashTestFirmwareForDevice(deviceType);
        }

        private static bool TestFirmwareFailed(string result)
        {
            return FlashResultFailed(result) ||
                   result.Contains("UNKNOWN", StringComparison.OrdinalIgnoreCase);
        }

        private static bool FlashResultFailed(string result)
        {
            return result.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ||
                   result.Contains("ExitCode", StringComparison.OrdinalIgnoreCase) ||
                   result.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                   result.Contains("failed", StringComparison.OrdinalIgnoreCase);
        }

        private async void OnDataReceived(string data)
        {
            await Application.Current.Dispatcher.Invoke(async () =>
            {
                Log += data + "\n";

                if (_cycleState != CycleState.Testing)
                    return;

                string[] lines = data.Replace("\r", "")
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries);

                foreach (string rawLine in lines)
                {
                    string line = rawLine.Trim();

                    bool? diagnosticsPassed = _diagnosticsParser.ProcessLine(line);

                    if (diagnosticsPassed == null)
                        continue;

                    if (diagnosticsPassed == true)
                    {
                        Status = "Test OK";
                        ProgressValue = 70;
                        Log += "=== ТЕСТ ПРОЙДЕНО ===\n";

                        await StopTestAndFlash();
                        return;
                    }

                    Status = "Test Error";
                    ProgressValue = 100;
                    Log += "=== ТЕСТ НЕ ПРОЙДЕНО ===\n";
                    Log += $"SIG: {_diagnosticsParser.LastSig}\n";
                    Log += ">>> Заберіть плату зі стенда...\n";
                    ShowTestErrorDialog(
                        "Помилка тесту",
                        $"SIG: {_diagnosticsParser.LastSig}\n\n{_diagnosticsParser.LastReport}");

                    await StopTestAndWaitRemove();
                    return;
                }
            });
        }

        private static void ShowTestErrorDialog(string title, string details)
        {
            string message = string.IsNullOrWhiteSpace(details)
                ? "Тест завершився з помилкою."
                : details.Trim();

            MessageBox.Show(
                message,
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        private async Task SetUartMode()
        {
            _serial.Send("UART_ON");
            await Task.Delay(100);
        }

        private async Task SetStlinkMode()
        {
            _serial.Send("STLINK_ON");
            await Task.Delay(300);
        }

        private async Task DischargeCapacitor()
        {
            _serial.Send("POWER_OFF");
            await Task.Delay(CapacitorDischargeDelayMs);
        }

        private async Task PowerCycleToStlinkMode()
        {
            await DischargeCapacitor();

            _serial.Send("POWER_ON");
            await Task.Delay(PowerOnSettleDelayMs);

            await SetStlinkMode();
        }

        private async Task PowerCycleToUartMode()
        {
            await DischargeCapacitor();

            await SetUartMode();

            _serial.Send("POWER_ON");
            await Task.Delay(PowerOnSettleDelayMs);
        }

        private async Task RetryStlinkPowerIfDue()
        {
            if ((DateTime.UtcNow - _lastStlinkPowerRetryUtc).TotalMilliseconds < StlinkPowerRetryIntervalMs)
                return;

            _lastStlinkPowerRetryUtc = DateTime.UtcNow;

            await PowerCycleToStlinkMode();
        }

        private async Task<bool> EnsureStlinkReady(string context)
        {
            if (await _firmwareService.CheckConnection(StlinkConnectionCheckTimeoutMs))
                return true;

            Log += $"WARNING: ST-LINK немає зв'язку ({context}). Повторно подаємо живлення...\n";

            await PowerCycleToStlinkMode();

            if (await _firmwareService.CheckConnection(StlinkConnectionCheckTimeoutMs))
            {
                Log += "=== ST-LINK зв'язок відновлено ===\n";
                return true;
            }

            Status = "No ST-LINK";
            Log += "ERROR: ST-LINK не бачить плату після повторного подання живлення\n";
            Log += ">>> Заберіть плату зі стенда...\n";

            return false;
        }

        private async Task StartTest()
        {
            if (!EnsureSerialEntered())
            {
                _cycleState = CycleState.WaitingRemove;
                Log += ">>> Заберіть плату зі стенда...\n";

                await WaitBoardRemoved();
                return;
            }

            _cycleState = CycleState.Testing;

            _diagnosticsParser.Reset();

            await PowerCycleToUartMode();
            _serial.Send("PWM_START");
            await Task.Delay(100);

            Status = "Testing";
            ProgressValue = 60;
            Log += ">>> Тест запущено. Чекаємо DIAGNOSTICS REPORT...\n";
        }

        private async Task StopTestAndWaitRemove()
        {
            _cycleState = CycleState.WaitingRemove;

            _serial.Send("PWM_STOP");
            await Task.Delay(50);

            await SetStlinkMode();
            await Task.Delay(100);

            await WaitBoardRemoved();
        }

        private async Task StopTestAndFlash()
        {
            _cycleState = CycleState.Flashing;

            _serial.Send("PWM_STOP");
            await Task.Delay(50);

            await StartFlashing();
        }

        private async Task StartFlashing()
        {
            try
            {
                Status = "Preparing flash";
                ProgressValue = 80;
                Log += "--- PREPARE FLASH ---\n";

                await PowerCycleToStlinkMode();
                if (!await EnsureStlinkReady("production flash"))
                {
                    _cycleState = CycleState.WaitingRemove;
                    await WaitBoardRemoved();
                    return;
                }

                Status = "Flashing";
                ProgressValue = 90;
                Log += "--- FLASH START ---\n";

                string firmwareToFlash;

                if (_workMode == WorkMode.Production)
                {
                    firmwareToFlash = FirmwarePath;
                }
                else if (_workMode == WorkMode.Reflash)
                {
                    if (_currentDeviceType == "G0")
                        firmwareToFlash = G0FirmwarePath;
                    else if (_currentDeviceType == "C0")
                        firmwareToFlash = C0FirmwarePath;
                    else
                    {
                        Log += "ERROR: Unknown MCU for reflash\n";
                        await StopAll();
                        return;
                    }
                }
                else
                {
                    Log += "ERROR: Wrong mode for StartFlashing\n";
                    await StopAll();
                    return;
                }

                if (_workMode == WorkMode.Reflash &&
                    _currentDeviceType == "C0" &&
                    !IsC0FirmwareNameValid(firmwareToFlash))
                {
                    Log += $"ERROR: C0 firmware filename must contain c011: {firmwareToFlash}\n";
                    Status = "Wrong C0 firmware";
                    ProgressValue = 100;
                    Log += ">>> Заберіть плату зі стенда...\n";

                    _cycleState = CycleState.WaitingRemove;

                    await WaitBoardRemoved();
                    return;
                }

                if (!System.IO.File.Exists(firmwareToFlash))
                {
                    Log += $"Firmware not found: {firmwareToFlash}\n";
                    Status = "Firmware not found";
                    ProgressValue = 100;
                    Log += ">>> Заберіть плату зі стенда...\n";

                    _cycleState = CycleState.WaitingRemove;

                    await WaitBoardRemoved();
                    return;
                }

                string result = await _firmwareService.FlashProductionFirmware(firmwareToFlash);

                if (FlashResultFailed(result))
                {
                    Status = "Flash Error";
                    ProgressValue = 100;
                    Log += "=== ПОМИЛКА ПРОШИВКИ ===\n";
                    Log += ">>> Заберіть плату зі стенда...\n";

                    _cycleState = CycleState.WaitingRemove;

                    await WaitBoardRemoved();
                    return;
                }

                _serial.Send("BUZZ_OK");
                await Task.Delay(100);

                Log += "\n--- FLASH END ---\n";

                Status = "Flash OK";
                ProgressValue = 100;

                RegisterDeviceInDatabase(firmwareToFlash);
                _completedBox = RegisterFlashedBoard();
                RegisterUniqueFlashedBoard();
                ScannedSerial = "";

                Log += "=== ПРОШИВКУ ЗАВЕРШЕНО ===\n";

                Log += ">>> Заберіть плату зі стенда...\n";

                _cycleState = CycleState.WaitingRemove;

                await WaitBoardRemoved();
            }
            catch (Exception ex)
            {
                Status = "Flash Error";
                ProgressValue = 100;
                Log += "ERROR: " + ex.Message + "\n";

                _cycleState = CycleState.WaitingRemove;

                await WaitBoardRemoved();
            }
        }

        private static bool IsC0FirmwareNameValid(string firmwarePath)
        {
            string fileName = System.IO.Path.GetFileName(firmwarePath);

            return fileName.Contains("c011", StringComparison.OrdinalIgnoreCase);
        }

        private async Task WaitBoardRemoved()
        {
            Status = "Waiting remove";
            int absentCount = 0;

            while (_cycleState == CycleState.WaitingRemove)
            {
                bool present = await _firmwareService.CheckConnection(BoardRemovedCheckTimeoutMs);

                if (present)
                {
                    absentCount = 0;
                }
                else
                {
                    absentCount++;
                }

                if (absentCount >= BoardRemovedConfirmations)
                {
                    Log += "=== ПЛАТУ ЗАБРАЛИ ===\n";

                    if (_completedBox != null)
                    {
                        await ShowBoxCompletedWindow(_completedBox);
                        _completedBox = null;
                    }

                    await PowerOffAndRestartCycle();
                    return;
                }

                await Task.Delay(StlinkPollDelayMs);
            }
        }

        private async Task PowerOffAndRestartCycle()
        {
            try
            {
                _serial.Send("POWER_OFF");
                await Task.Delay(100);

                ResetCycleData();

                Log += "=== ЖИВЛЕННЯ ВИМКНЕНО ===\n";

                while (await _firmwareService.CheckConnection(BoardRemovedCheckTimeoutMs))
                {
                    Log += ">>> ST-LINK ще бачить плату. Чекаємо...\n";
                    await Task.Delay(100);
                }

                Log += "=== ПОЧИНАЄМО НОВИЙ ЦИКЛ ===\n";

                await StartFullCycle();
            }
            catch (Exception ex)
            {
                Status = "Error";
                Log += "ERROR: " + ex.Message + "\n";

                StopRunTimer();
                IsBusy = false;
                _cycleState = CycleState.Idle;
            }
        }

        private async Task StopAll()
        {
            try
            {
                _cycleState = CycleState.Idle;

                _serial.Send("PWM_STOP");
                await Task.Delay(50);

                _serial.Send("POWER_OFF");
                await Task.Delay(100);

                StopRunTimer();
                ResetCycleData();

                Status = "Stopped";
                ProgressValue = 100;
                Log += "=== ЗУПИНЕНО ===\n";
                Log += "=== ЖИВЛЕННЯ ВИМКНЕНО ===\n";
            }
            catch (Exception ex)
            {
                Log += "ERROR: " + ex.Message + "\n";
            }
            finally
            {
                IsBusy = false;
                _cycleState = CycleState.Idle;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(name)
            );
        }
        private BoxReport? RegisterFlashedBoard()
        {
            var report = _boxTracking.RegisterFlashedBoard(
                BoxQuantity,
                ScannedSerial,
                FirmwareName);

            if (report != null)
                SaveBoxReport(report);

            OnPropertyChanged(nameof(FlashedToday));

            return report;
        }

        private void LoadApiData()
        {
            if (_currentUser.IsOffline)
            {
                LoadOfflineData("=== TEST MODE: API не використовується ===");
                return;
            }

            try
            {
                _boxTracking.LoadReports(_apiService.LoadBoxReports());
                LoadTodayCountersFromApi();

                OnPropertyChanged(nameof(FlashedToday));
            }
            catch (Exception ex)
            {
                Log += "API ERROR: " + ex.Message + "\n";
                LoadOfflineData("=== OFFLINE: використовуємо локальні незасинхронізовані дані ===");
            }
        }

        private void LoadOfflineData(string message)
        {
            try
            {
                _boxTracking.LoadReports(_localDatabaseService.LoadPendingBoxReports());
                LoadTodayCountersFromLocalDatabase();

                Log += message + "\n";
            }
            catch (Exception localEx)
            {
                _boxTracking.LoadReports(Array.Empty<BoxReport>());
                ResetTodayCounters();
                PendingRegistrations.Clear();

                Log += "LOCAL DB ERROR: " + localEx.Message + "\n";
                Log += "=== TEST MODE: стартуємо без завантаження статистики ===\n";
            }

            LoadPendingRegistrations();
            OnPropertyChanged(nameof(FlashedToday));
        }

        private void LoadTodayCountersFromApi()
        {
            _testedToday = _apiService.CountDeviceRegistrationsFrom(DateTime.Now, TestRegistrationType);
            _flashedToday = _apiService.CountDeviceRegistrationsFrom(DateTime.Now, ProductionRegistrationType);
            _reflashedToday = _apiService.CountDeviceRegistrationsFrom(DateTime.Now, ReworkRegistrationType);
        }

        private void LoadTodayCountersFromLocalDatabase()
        {
            _testedToday = _localDatabaseService.CountPendingDeviceRegistrationsFrom(DateTime.Now, TestRegistrationType);
            _flashedToday = _localDatabaseService.CountPendingDeviceRegistrationsFrom(DateTime.Now, ProductionRegistrationType);
            _reflashedToday = _localDatabaseService.CountPendingDeviceRegistrationsFrom(DateTime.Now, ReworkRegistrationType);
        }

        private void ResetTodayCounters()
        {
            _testedToday = 0;
            _flashedToday = 0;
            _reflashedToday = 0;
        }

        private void LoadPendingRegistrations()
        {
            PendingRegistrations.Clear();

            try
            {
                foreach (PendingRegistration registration in _localDatabaseService.LoadPendingRegistrations())
                    PendingRegistrations.Add(registration);
            }
            catch (Exception ex)
            {
                Log += "LOCAL DB ERROR: " + ex.Message + "\n";
            }
        }

        private void RegisterDeviceInDatabase(string firmwarePath)
        {
            if (_currentUser.IsOffline)
            {
                RegisterDeviceLocally();

                if (_workMode == WorkMode.Reflash)
                    TryRegisterReworkLocally(firmwarePath);

                return;
            }

            try
            {
                _apiService.AddDeviceRegistration(
                    _currentUser.Id,
                    ScannedSerial,
                    _currentDeviceType,
                    FirmwareName,
                    GetCurrentRegistrationType());
            }
            catch (Exception ex)
            {
                _localDatabaseService.AddPendingDeviceRegistration(
                    _currentUser.Id,
                    GetCurrentOrderIdForLocalRegistration(),
                    ScannedSerial,
                    _currentDeviceType,
                    FirmwareName,
                    GetCurrentRegistrationType());

                Log += "API ERROR: " + ex.Message + "\n";
                Log += "=== OFFLINE: плату збережено локально для синхронізації ===\n";
            }

            IncrementTodayCounter(GetCurrentRegistrationType());

            if (_workMode != WorkMode.Reflash)
                return;

            try
            {
                RegisterReworkInDatabase(firmwarePath);
            }
            catch (Exception ex)
            {
                RegisterReworkLocally(firmwarePath);

                Log += "API ERROR: " + ex.Message + "\n";
                Log += "=== OFFLINE: переробку збережено локально для синхронізації ===\n";
            }
        }

        private void RegisterDeviceLocally()
        {
            try
            {
                _localDatabaseService.AddPendingDeviceRegistration(
                    _currentUser.Id,
                    GetCurrentOrderIdForLocalRegistration(),
                    ScannedSerial,
                    _currentDeviceType,
                    FirmwareName,
                    GetCurrentRegistrationType());

                LoadPendingRegistrations();
                IncrementTodayCounter(GetCurrentRegistrationType());
            }
            catch (Exception ex)
            {
                Log += "LOCAL DB ERROR: " + ex.Message + "\n";
                Log += "=== TEST MODE: плату не збережено в локальну базу ===\n";
            }
        }

        private string GetCurrentOrderIdForLocalRegistration()
        {
            return _workMode == WorkMode.Reflash ? ReworkOrderId : string.Empty;
        }

        private string GetCurrentRegistrationType()
        {
            return _workMode switch
            {
                WorkMode.TestFirmware => TestRegistrationType,
                WorkMode.Reflash => ReworkRegistrationType,
                _ => ProductionRegistrationType
            };
        }

        private void IncrementTodayCounter(string registrationType)
        {
            switch (registrationType)
            {
                case TestRegistrationType:
                    _testedToday++;
                    break;

                case ReworkRegistrationType:
                    _reflashedToday++;
                    break;

                default:
                    _flashedToday++;
                    break;
            }

            OnPropertyChanged(nameof(FlashedToday));
        }

        private void RegisterReworkInDatabase(string firmwarePath)
        {
            string firmwareName = string.IsNullOrWhiteSpace(FirmwareName)
                ? System.IO.Path.GetFileNameWithoutExtension(firmwarePath)
                : FirmwareName;

            _apiService.AddReworkRegistration(
                _currentUser.Id,
                ReworkOrderId,
                ScannedSerial,
                firmwareName,
                firmwarePath,
                _currentDeviceType,
                "OK");
        }

        private void RegisterReworkLocally(string firmwarePath)
        {
            string firmwareName = string.IsNullOrWhiteSpace(FirmwareName)
                ? System.IO.Path.GetFileNameWithoutExtension(firmwarePath)
                : FirmwareName;

            _localDatabaseService.AddPendingReworkRegistration(
                _currentUser.Id,
                ReworkOrderId,
                ScannedSerial,
                firmwareName,
                firmwarePath,
                _currentDeviceType,
                "OK");
        }

        private void TryRegisterReworkLocally(string firmwarePath)
        {
            try
            {
                RegisterReworkLocally(firmwarePath);
                LoadPendingRegistrations();
            }
            catch (Exception ex)
            {
                Log += "LOCAL DB ERROR: " + ex.Message + "\n";
                Log += "=== TEST MODE: переробку не збережено в локальну базу ===\n";
            }
        }

        private void SaveBoxReport(BoxReport report)
        {
            if (_currentUser.IsOffline)
            {
                try
                {
                    _localDatabaseService.AddPendingBoxReport(_currentUser.Id, report);
                }
                catch (Exception ex)
                {
                    Log += "LOCAL DB ERROR: " + ex.Message + "\n";
                    Log += "=== TEST MODE: коробку не збережено в локальну базу ===\n";
                }

                return;
            }

            try
            {
                _apiService.AddBoxReport(_currentUser.Id, report);
            }
            catch (Exception ex)
            {
                _localDatabaseService.AddPendingBoxReport(_currentUser.Id, report);

                Log += "API ERROR: " + ex.Message + "\n";
                Log += "=== OFFLINE: коробку збережено локально для синхронізації ===\n";
            }
        }

        private async Task ShowBoxCompletedWindow(BoxReport report)
        {
            var window = new global::FCatFlasher.BoxCompletedWindow
            {
                Owner = Application.Current.MainWindow,
                DataContext = report
            };

            window.ShowDialog();

            if (window.Result == global::FCatFlasher.BoxCompletedResult.Print)
            {
                MessageBox.Show("Тут буде друк етикетки");
            }

            if (window.Result == global::FCatFlasher.BoxCompletedResult.Finish)
            {
                await StopAll();
                return;
            }

            _boxTracking.NextBox();
        }

        private void LoadSettings()
        {
            var settings = _settingsService.Load();

            SelectedComPort = settings.SelectedComPort;
            FirmwarePath = settings.FirmwarePath;
            FirmwareName = settings.FirmwareName;
            BoxQuantity = settings.BoxQuantity;
            G0FirmwarePath = settings.G0FirmwarePath;
            C0FirmwarePath = settings.C0FirmwarePath;
            ReworkOrderId = settings.ReworkOrderId;
            TestFirmwareDirectory = settings.TestFirmwareDirectory;
            StmCubeCliPath = settings.StmCubeCliPath;
            ApiBaseUrl = settings.ApiBaseUrl;

            ApplyFlashSettings();
        }

        private void SaveSettings()
        {
            if (!_settingsLoaded)
                return;

            _settingsService.Save(new Models.AppSettings
            {
                SelectedComPort = SelectedComPort,
                FirmwarePath = FirmwarePath,
                FirmwareName = FirmwareName,
                BoxQuantity = BoxQuantity,
                G0FirmwarePath = G0FirmwarePath,
                C0FirmwarePath = C0FirmwarePath,
                ReworkOrderId = ReworkOrderId,
                TestFirmwareDirectory = TestFirmwareDirectory,
                StmCubeCliPath = StmCubeCliPath,
                ApiBaseUrl = ApiBaseUrl
            });
        }

        private void ApplyFlashSettings()
        {
            _testFirmwareService.TestFirmwareDirectory = TestFirmwareDirectory;
            _firmwareService.CliPath = StmCubeCliPath;
        }

        private void ResetUniqueFlashedCounter()
        {
            _uniqueBoardCounter.Reset();
            OnPropertyChanged(nameof(UniqueFlashedSinceStart));
        }

        private void RegisterUniqueFlashedBoard()
        {
            if (_uniqueBoardCounter.Register(ScannedSerial))
                OnPropertyChanged(nameof(UniqueFlashedSinceStart));
        }

    }
}
