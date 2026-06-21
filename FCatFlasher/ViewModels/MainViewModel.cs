using FCatFlasher.Helpers;
using FCatFlasher.Models;
using FCatFlasher.Services;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO.Ports;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;


namespace FCatFlasher.ViewModels
{
    internal class MainViewModel : INotifyPropertyChanged
    {
        private const int PowerOffBeforeRestartDelayMs = 1000;
        private const int BoardInsertPowerOffDelayMs = 300;
        private const int StandCommandSettleDelayMs = 200;
        private const int UartPowerOffDelayMs = 200;
        private const int PowerOnSettleDelayMs = 200;
        private const int UartPowerOnSettleDelayMs = 100;
        private const int UartModeSwitchDelayMs = 200;
        private const int StlinkModeSwitchDelayMs = 300;
        private const int StlinkAfterTestModeSwitchDelayMs = 200;
        private const int BuzzerOkDelayMs = 200;
        private const int CycleErrorDelayMs = 2000;
        private const int StlinkConnectionCheckTimeoutMs = 1000;
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
        private readonly SemaphoreSlim _standCommandLock = new(1, 1);
        private readonly DispatcherTimer _runTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private bool _settingsLoaded;
        private string _currentDeviceType = "";
        private string _lastConnectionError = "";
        private DateTime _runStartedAt;
        private bool _stlinkSelectedForRun;
        private bool _standPowerOn;
        private bool _skipReflashDiagnosticsReport;
        private bool _skipFlashRegistration;

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
        private bool _productionBoxActive;
        private int _testedToday;
        private int _flashedToday;
        private int _reflashedToday;

        public ICommand OpenSettingsCommand { get; }

        public RelayCommand TestFirmwareCommand { get; }
        public RelayCommand FlashCommand { get; }
        public RelayCommand ReflashCommand { get; }
        public RelayCommand ReflashTestFirmwareCommand { get; }

        public ICommand DeleteLocalDataRowCommand { get; }
        public ICommand RefreshComPortsCommand { get; }
        public ICommand BrowseFirmwareCommand { get; }
        public ICommand BrowseG0FirmwareCommand { get; }
        public ICommand BrowseC0FirmwareCommand { get; }
        public ICommand BrowseTestFirmwareDirectoryCommand { get; }
        public ICommand BrowseStmCubeCliCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand OpenProductionReportCommand { get; }
        public ICommand OpenReworkReportCommand { get; }

        public ObservableCollection<string> ComPorts { get; } = new();

        private BoxReport? _completedBox;

        public ObservableCollection<LocalDataRow> LocalDataRows { get; } = new();

        private string _localDataHeader1 = "";
        public string LocalDataHeader1
        {
            get => _localDataHeader1;
            set
            {
                _localDataHeader1 = value;
                OnPropertyChanged();
            }
        }

        private string _localDataHeader2 = "";
        public string LocalDataHeader2
        {
            get => _localDataHeader2;
            set
            {
                _localDataHeader2 = value;
                OnPropertyChanged();
            }
        }

        private string _localDataHeader3 = "";
        public string LocalDataHeader3
        {
            get => _localDataHeader3;
            set
            {
                _localDataHeader3 = value;
                OnPropertyChanged();
            }
        }

        private string _localDataHeader4 = "";
        public string LocalDataHeader4
        {
            get => _localDataHeader4;
            set
            {
                _localDataHeader4 = value;
                OnPropertyChanged();
            }
        }

        private string _localDataHeader5 = "";
        public string LocalDataHeader5
        {
            get => _localDataHeader5;
            set
            {
                _localDataHeader5 = value;
                OnPropertyChanged();
            }
        }

        private string _localDataEmptyText = "";
        public string LocalDataEmptyText
        {
            get => _localDataEmptyText;
            set
            {
                _localDataEmptyText = value;
                OnPropertyChanged();
            }
        }

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
        private string _currentBoardSerial = "";

        public string ScannedSerial
        {
            get => _scannedSerial;
            set
            {
                _scannedSerial = value;
                OnPropertyChanged();
            }
        }

        private bool _isSerialInputEnabled = true;
        public bool IsSerialInputEnabled
        {
            get => _isSerialInputEnabled;
            set
            {
                _isSerialInputEnabled = value;
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
                ReflashTestFirmwareCommand.RaiseCanExecuteChanged();
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

        private string _reworkFirmwareName = string.Empty;

        public string ReworkFirmwareName
        {
            get => _reworkFirmwareName;
            set
            {
                _reworkFirmwareName = value;
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

            TestFirmwareCommand = new RelayCommand(
                async () =>
                {
                    if (!EnsureConnected())
                        return;

                    _workMode = WorkMode.TestFirmware;
                    _stlinkSelectedForRun = false;
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
                    _productionBoxActive = true;
                    ResetUniqueFlashedCounter();
                    LoadLocalDataForCurrentTab();
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
                    _stlinkSelectedForRun = false;
                    ResetUniqueFlashedCounter();
                    StartRunTimer();
                    await StartFullCycle();
                },
                () => !IsBusy
            );

            ReflashTestFirmwareCommand = new RelayCommand(
                async () =>
                {
                    if (!EnsureConnected())
                        return;

                    _workMode = WorkMode.Reflash;
                    _skipReflashDiagnosticsReport = true;
                    _skipFlashRegistration = true;
                    _stlinkSelectedForRun = false;
                    StartRunTimer();

                    try
                    {
                        await StartFullCycle();
                    }
                    finally
                    {
                        _skipReflashDiagnosticsReport = false;
                        _skipFlashRegistration = false;
                    }
                },
                () => !IsBusy
            );

            DeleteLocalDataRowCommand = new RelayCommand<LocalDataRow>(DeleteLocalDataRow);
            RefreshComPortsCommand = new RelayCommand(RefreshComPorts);
            BrowseFirmwareCommand = new RelayCommand(BrowseFirmware);
            BrowseG0FirmwareCommand = new RelayCommand(BrowseG0Firmware);
            BrowseC0FirmwareCommand = new RelayCommand(BrowseC0Firmware);
            BrowseTestFirmwareDirectoryCommand = new RelayCommand(BrowseTestFirmwareDirectory);
            BrowseStmCubeCliCommand = new RelayCommand(BrowseStmCubeCli);
            OpenSettingsCommand = new RelayCommand(OpenSettings);
            StopCommand = new RelayCommand(async () => await StopAll());
            OpenProductionReportCommand = new RelayCommand(() => OpenReportWindow(isReworkReport: false));
            OpenReworkReportCommand = new RelayCommand(() => OpenReportWindow(isReworkReport: true));

            _runTimer.Tick += (_, _) => UpdateRunElapsed();
            _serial.DataReceived += OnDataReceived;

            RefreshComPorts();
            LoadSettings();
            LoadApiData();
            LoadLocalDataForCurrentTab();
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

        private void OpenReportWindow(bool isReworkReport)
        {
            var window = new global::FCatFlasher.ReportWindow(_localDatabaseService, isReworkReport)
            {
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
            LoadLocalDataForCurrentTab();
        }

        private bool RequestOrderId()
        {
            var window = new OrderIdWindow(ReworkOrderId, ReworkFirmwareName)
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
            if (_currentUser.IsOffline)
            {
                LoadReworkOrderDataFromLocalDatabase(fallbackFirmwareName);
                return;
            }

            try
            {
                ReworkOrderInfo? order = _apiService.LoadReworkOrder(ReworkOrderId);

                if (order == null)
                {
                    LoadReworkOrderDataFromLocalDatabase(fallbackFirmwareName);
                    return;
                }

                ReworkFirmwareName = order.FirmwareName;
            }
            catch (Exception ex)
            {
                Log += "ORDER API ERROR: " + ex.Message + "\n";
                LoadReworkOrderDataFromLocalDatabase(fallbackFirmwareName);
            }
        }

        private void LoadReworkOrderDataFromLocalDatabase(string fallbackFirmwareName)
        {
            ReworkOrderInfo? localOrder = _localDatabaseService.LoadPendingReworkOrder(ReworkOrderId);

            if (localOrder == null)
            {
                ReworkFirmwareName = fallbackFirmwareName;
                return;
            }

            ReworkOrderId = localOrder.OrderId;
            ReworkFirmwareName = localOrder.FirmwareName;
            Log += "=== ORDER: дані перепрошивки взято з локальної бази ===\n";
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

        private string GetCurrentBoardSerial()
        {
            if (!string.IsNullOrWhiteSpace(_currentBoardSerial))
                return _currentBoardSerial;

            return ScannedSerial.Trim();
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
            _currentBoardSerial = "";
            _lastConnectionError = "";
            IsSerialInputEnabled = true;

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

                if (_workMode == WorkMode.Production)
                {
                    Log += "=== PRODUCTION: стартуємо з UART без попереднього ST-LINK ===\n";
                    await StartTest();
                    return;
                }

                if (_stlinkSelectedForRun)
                    await SendStandCommand("POWER_ON", PowerOnSettleDelayMs);
                else
                {
                    await PowerCycleToStlinkMode();
                    _stlinkSelectedForRun = true;
                }

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
                    _lastConnectionError = "";
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

                LogConnectionErrorIfChanged();

                await CycleBoardPowerWhileWaiting();
            }
        }

        private void LogConnectionErrorIfChanged()
        {
            string error = _firmwareService.LastConnectionError;

            if (string.IsNullOrWhiteSpace(error) || error == _lastConnectionError)
                return;

            _lastConnectionError = error;
            Log += "ST-LINK/API: " + error + "\n";
        }

        private async Task FlashOnlyTestFirmware()
        {
            try
            {
                _cycleState = CycleState.Flashing;

                if (!await EnsureStlinkReady("test firmware"))
                {
                    _cycleState = CycleState.WaitingRemove;
                    await WaitBoardRemovedAfterError();
                    return;
                }

                Status = "Flashing test firmware";
                ProgressValue = 50;
                Log += "=== ТЕСТОВА ПРОШИВКА ===\n";

                _currentDeviceType = await GetDeviceTypeWithRetry("test firmware");

                if (!IsKnownDeviceType(_currentDeviceType))
                {
                    Status = "MCU Error";
                    ProgressValue = 100;
                    Log += $"ERROR: MCU not detected: {_currentDeviceType}\n";
                    Log += ">>> Заберіть плату зі стенда...\n";

                    _cycleState = CycleState.WaitingRemove;

                    await WaitBoardRemovedAfterError();
                    return;
                }

                Log += $"MCU: {_currentDeviceType}\n";

                string result = await FlashAutoTestFirmwareWithRetry(_currentDeviceType);

                if (TestFirmwareFailed(result))
                {
                    Status = "Test firmware error";
                    ProgressValue = 100;
                    Log += "=== ТЕСТОВУ ПРОШИВКУ НЕ ЗАЛИТО ===\n";
                    Log += ">>> Заберіть плату зі стенда...\n";
                    ShowTestErrorDialog("Помилка тестової прошивки", result);

                    _cycleState = CycleState.WaitingRemove;

                    await WaitBoardRemovedAfterError();
                    return;
                }

                await SendStandCommand("BUZZ_OK", BuzzerOkDelayMs);

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

                await WaitBoardRemovedAfterError();
            }
        }

        private async Task PrepareReflashWithTestFirmware()
        {
            try
            {
                _cycleState = CycleState.Flashing;

                Status = "Preparing reflash";
                ProgressValue = 30;
                Log += "=== ПЕРЕПРОШИВКА: СПОЧАТКУ ТЕСТОВА ===\n";

                _currentDeviceType = await GetDeviceTypeWithRetry("prepare reflash");

                if (!IsKnownDeviceType(_currentDeviceType))
                {
                    Status = "MCU Error";
                    ProgressValue = 100;
                    Log += $"ERROR: MCU not detected: {_currentDeviceType}\n";
                    Log += ">>> Заберіть плату зі стенда...\n";

                    _cycleState = CycleState.WaitingRemove;

                    await WaitBoardRemovedAfterError();
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

                    await WaitBoardRemovedAfterError();
                    return;
                }

                Log += "\n=== ТЕСТОВУ ПРОШИВКУ ЗАЛИТО ===\n";

                await Task.Delay(100);

                if (_skipReflashDiagnosticsReport)
                {
                    Log += "=== REPORT ПРОПУЩЕНО ===\n";
                    await StartFlashing();
                    return;
                }

                await StartTest();
            }
            catch (Exception ex)
            {
                Status = "Error";
                Log += "ERROR: " + ex.Message + "\n";

                _cycleState = CycleState.WaitingRemove;

                await WaitBoardRemovedAfterError();
            }
        }

        private async Task<string> FlashAutoTestFirmwareWithRetry(string deviceType)
        {
            string result = await _testFirmwareService.FlashTestFirmwareForDevice(deviceType);

            if (!TestFirmwareFailed(result))
                return result;

            Log += "\nWARNING: MCU не визначився після RDP unlock. Робимо power cycle і пробуємо ще раз...\n";

            if (_workMode == WorkMode.Production)
                await PowerOnToStlinkMode();
            else
                await PowerCycleToStlinkMode();

            return await _testFirmwareService.FlashTestFirmwareForDevice(deviceType);
        }

        private async Task<string> GetDeviceTypeWithRetry(string context)
        {
            string deviceType = await _firmwareService.GetDeviceType();

            if (IsKnownDeviceType(deviceType))
                return deviceType;

            Log += $"WARNING: MCU не визначився ({context}): {deviceType}\n";
            Log += "=== Повторно подаємо живлення в ST-LINK режимі ===\n";

            await SendStandCommand("POWER_OFF", BoardInsertPowerOffDelayMs);
            await SendStandCommand("POWER_ON", PowerOnSettleDelayMs);

            return await _firmwareService.GetDeviceType();
        }

        private static bool IsKnownDeviceType(string deviceType)
        {
            return deviceType is "G0" or "C0";
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
                        if (!EnsureSerialEntered())
                        {
                            Log += ">>> Заберіть плату зі стенда...\n";
                            await StopTestAndWaitRemove();
                            return;
                        }

                        _currentBoardSerial = ScannedSerial.Trim();
                        IsSerialInputEnabled = false;
                        ScannedSerial = "";

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

        private async Task SendStandCommand(
            string command,
            int settleDelayMs = StandCommandSettleDelayMs,
            bool enforceMinimumDelay = true)
        {
            await _standCommandLock.WaitAsync();

            try
            {
                if (!_serial.IsOpen)
                    throw new InvalidOperationException($"COM port is closed. Command not sent: {command}");

                if (!IsPowerCommand(command))
                    Log += $">>> {command}\n";

                _serial.Send(command);
                UpdateStandPowerState(command);

                int delayMs = enforceMinimumDelay
                    ? Math.Max(settleDelayMs, StandCommandSettleDelayMs)
                    : settleDelayMs;

                await Task.Delay(delayMs);
            }
            finally
            {
                _standCommandLock.Release();
            }
        }

        private static bool IsPowerCommand(string command)
        {
            return command is "POWER_ON" or "POWER_OFF";
        }

        private void UpdateStandPowerState(string command)
        {
            if (command == "POWER_ON")
                _standPowerOn = true;
            else if (command == "POWER_OFF")
                _standPowerOn = false;
        }

        private async Task SetUartMode(int settleDelayMs = UartModeSwitchDelayMs)
        {
            await SendStandCommand("UART_ON", settleDelayMs);
        }

        private async Task SetStlinkMode(int settleDelayMs = StlinkModeSwitchDelayMs)
        {
            await SendStandCommand("STLINK_ON", settleDelayMs);
        }

        private async Task SetUartModeAndEnsurePowerOn(
            int modeSwitchDelayMs = UartModeSwitchDelayMs,
            int powerOnDelayMs = PowerOnSettleDelayMs)
        {
            bool powerAlreadyOn = _standPowerOn;
            await SetUartMode(modeSwitchDelayMs);

            if (!powerAlreadyOn)
                await SendStandCommand("POWER_ON", powerOnDelayMs);
        }

        private async Task SetStlinkModeAndEnsurePowerOn(
            int modeSwitchDelayMs = StlinkModeSwitchDelayMs,
            int powerOnDelayMs = UartPowerOnSettleDelayMs)
        {
            bool powerAlreadyOn = _standPowerOn;
            await SetStlinkMode(modeSwitchDelayMs);

            if (!powerAlreadyOn)
                await SendStandCommand("POWER_ON", powerOnDelayMs);
        }
        private async Task PowerCycleToStlinkMode()
        {
            await SetStlinkModeAndEnsurePowerOn();
        }

        private async Task PowerOnToStlinkMode()
        {
            await SetStlinkModeAndEnsurePowerOn();
        }

        private async Task CycleBoardPowerWhileWaiting()
        {
            await SendStandCommand("POWER_OFF", BoardInsertPowerOffDelayMs);

            if (_cycleState != CycleState.WaitingBoard)
                return;

            await SendStandCommand("POWER_ON", PowerOnSettleDelayMs);
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
            _cycleState = CycleState.Testing;
            IsSerialInputEnabled = true;

            _diagnosticsParser.Reset();
            await SetUartModeAndEnsurePowerOn();

            Status = "Testing";
            ProgressValue = 60;
            Log += ">>> Тест запущено. Чекаємо DIAGNOSTICS REPORT...\n";
        }

        private async Task StopTestAndWaitRemove()
        {
            _cycleState = CycleState.WaitingRemove;

            await SendStandCommand("POWER_OFF", UartPowerOffDelayMs);
            await SetStlinkMode();
            await Task.Delay(100);

            await WaitBoardRemovedAfterError();
        }

        private async Task StopTestAndFlash()
        {
            _cycleState = CycleState.Flashing;
            await SetStlinkModeAndEnsurePowerOn(
                StlinkAfterTestModeSwitchDelayMs,
                PowerOnSettleDelayMs);

            await StartFlashing();
        }

        private async Task StartFlashing()
        {
            try
            {
                Status = "Preparing flash";
                ProgressValue = 80;
                Log += "--- PREPARE FLASH ---\n";

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

                if ((_workMode == WorkMode.Production || _workMode == WorkMode.Reflash) &&
                    _currentDeviceType == "C0" &&
                    !IsC0FirmwareNameValid(firmwareToFlash))
                {
                    Log += $"ERROR: C0 firmware filename must contain c011: {firmwareToFlash}\n";
                    Status = "Wrong C0 firmware";
                    ProgressValue = 100;
                    Log += ">>> Заберіть плату зі стенда...\n";

                    _cycleState = CycleState.WaitingRemove;

                    await WaitBoardRemovedAfterError();
                    return;
                }

                if (!System.IO.File.Exists(firmwareToFlash))
                {
                    Log += $"Firmware not found: {firmwareToFlash}\n";
                    Status = "Firmware not found";
                    ProgressValue = 100;
                    Log += ">>> Заберіть плату зі стенда...\n";

                    _cycleState = CycleState.WaitingRemove;

                    await WaitBoardRemovedAfterError();
                    return;
                }

                string result = await _firmwareService.FlashProductionFirmware(firmwareToFlash);
                Log += result + "\n";

                if (FlashResultFailed(result))
                {
                    Status = "Flash Error";
                    ProgressValue = 100;
                    Log += "=== ПОМИЛКА ПРОШИВКИ ===\n";
                    Log += ">>> Заберіть плату зі стенда...\n";

                    _cycleState = CycleState.WaitingRemove;

                    await WaitBoardRemovedAfterError();
                    return;
                }

                await SendStandCommand("BUZZ_OK", BuzzerOkDelayMs);

                Log += "\n--- FLASH END ---\n";

                Status = "Flash OK";
                ProgressValue = 100;

                if (_skipFlashRegistration)
                    Log += "=== ТЕСТОВА ПЕРЕПРОШИВКА: дані не збережено ===\n";
                else
                    RegisterDeviceInDatabase(firmwareToFlash);

                if (!_skipFlashRegistration && _workMode == WorkMode.Production)
                    _completedBox = RegisterFlashedBoard();

                if (!_skipFlashRegistration)
                    RegisterUniqueFlashedBoard();

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

                await WaitBoardRemovedAfterError();
            }
        }

        private static bool IsC0FirmwareNameValid(string firmwarePath)
        {
            string fileName = System.IO.Path.GetFileName(firmwarePath);

            return fileName.Contains("c011", StringComparison.OrdinalIgnoreCase);
        }

        private async Task WaitBoardRemovedAfterError()
        {
            await Task.Delay(CycleErrorDelayMs);
            await WaitBoardRemoved();
        }

        private async Task WaitBoardRemoved()
        {
            if (_cycleState != CycleState.WaitingRemove)
                return;

            Status = "Power off";
            Log += "=== ВИМИКАЄМО ЖИВЛЕННЯ НА 500 МС ===\n";

            await SendStandCommand("POWER_OFF", PowerOffBeforeRestartDelayMs);

            if (_completedBox != null)
            {
                await ShowBoxCompletedWindow(_completedBox);
                _completedBox = null;
            }

            await PowerOffAndRestartCycle();
        }

        private async Task PowerOffAndRestartCycle()
        {
            try
            {
                ResetCycleData();

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

                await SendStandCommand("PWM_STOP", StandCommandSettleDelayMs);
                await SendStandCommand("POWER_OFF", 100);

                StopRunTimer();
                ResetCycleData();
                _stlinkSelectedForRun = false;

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
                GetCurrentBoardSerial(),
                FirmwareName);

            if (report != null)
                SaveBoxReport(report);

            LoadLocalDataForCurrentTab();
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
                LocalDataRows.Clear();

                Log += "LOCAL DB ERROR: " + localEx.Message + "\n";
                Log += "=== TEST MODE: стартуємо без завантаження статистики ===\n";
            }

            LoadLocalDataForCurrentTab();
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
            _reflashedToday = _localDatabaseService.CountPendingReworksFrom(DateTime.Now);
        }

        private void ResetTodayCounters()
        {
            _testedToday = 0;
            _flashedToday = 0;
            _reflashedToday = 0;
        }

        private void LoadLocalDataForCurrentTab()
        {
            LocalDataRows.Clear();

            try
            {
                switch (_footerMode)
                {
                    case WorkMode.TestFirmware:
                        SetLocalDataHeaders("", "", "", "", "");
                        LocalDataEmptyText = "У тестовій прошивці локальні дані не записуються.";
                        break;

                    case WorkMode.Reflash:
                        SetLocalDataHeaders("Зам.", "Прошивка", "К-сть", "Дати", "");
                        LocalDataEmptyText = "Немає локальних даних перепрошивки.";

                        foreach (ReworkSummary summary in _localDatabaseService.LoadPendingReworkSummariesFrom(DateTime.Today))
                        {
                            LocalDataRows.Add(new LocalDataRow
                            {
                                Column1 = GetLastOrderDigits(summary.OrderId),
                                Column2 = summary.FirmwareName,
                                Column3 = summary.Quantity.ToString(CultureInfo.InvariantCulture),
                                Column4 = summary.DateRangeText,
                                KeyOrderId = summary.OrderId,
                                KeyFirmwareName = summary.FirmwareName,
                                CanDelete = true
                            });
                        }

                        break;

                    default:
                        SetLocalDataHeaders("ID коробки", "Діапазон", "К-сть", "Прошивка", "");
                        LocalDataEmptyText = "Немає локальних коробок.";

                        AddCurrentProductionBoxRow();

                        foreach (BoxReport report in _localDatabaseService.LoadPendingBoxReportsFrom(DateTime.Today))
                        {
                            LocalDataRows.Add(new LocalDataRow
                            {
                                Column1 = report.BoxNumber.ToString(CultureInfo.InvariantCulture),
                                Column2 = FormatSerialRangesForDisplay(report.SerialRangeText),
                                Column3 = report.Quantity.ToString(CultureInfo.InvariantCulture),
                                Column4 = report.FirmwareName
                            });
                        }

                        break;
                }

                if (LocalDataRows.Count > 0)
                    LocalDataEmptyText = string.Empty;
            }
            catch (Exception ex)
            {
                Log += "LOCAL DB ERROR: " + ex.Message + "\n";
            }
        }

        private void AddCurrentProductionBoxRow()
        {
            if (!_productionBoxActive)
                return;

            BoxReport? currentBox = _boxTracking.GetCurrentBoxReport(BoxQuantity, FirmwareName);

            if (currentBox == null || currentBox.Quantity >= BoxQuantity)
                return;

            LocalDataRows.Add(new LocalDataRow
            {
                Column1 = currentBox.BoxNumber.ToString(CultureInfo.InvariantCulture),
                Column2 = currentBox.Quantity == 0
                    ? string.Empty
                    : FormatSerialRangesForDisplay(currentBox.SerialRangeText),
                Column3 = currentBox.Quantity.ToString(CultureInfo.InvariantCulture),
                Column4 = currentBox.FirmwareName
            });
        }

        private static string FormatSerialRangesForDisplay(string serialRanges)
        {
            return serialRanges.Replace(", ", Environment.NewLine);
        }

        private void SetLocalDataHeaders(
            string header1,
            string header2,
            string header3,
            string header4,
            string header5)
        {
            LocalDataHeader1 = header1;
            LocalDataHeader2 = header2;
            LocalDataHeader3 = header3;
            LocalDataHeader4 = header4;
            LocalDataHeader5 = header5;
        }

        private void DeleteLocalDataRow(LocalDataRow? row)
        {
            if (row == null || !row.CanDelete)
                return;

            MessageBoxResult result = MessageBox.Show(
                $"Видалити локальні переробки замовлення {row.KeyOrderId}?",
                "Видалення переробки",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                _localDatabaseService.DeletePendingReworks(row.KeyOrderId, row.KeyFirmwareName);
                LoadTodayCountersFromLocalDatabase();
                LoadLocalDataForCurrentTab();
                OnPropertyChanged(nameof(FlashedToday));

                Log += $"=== LOCAL DB: переробки замовлення {row.KeyOrderId} видалено ===\n";
            }
            catch (Exception ex)
            {
                Log += "LOCAL DB ERROR: " + ex.Message + "\n";
            }
        }

        private static string GetLastOrderDigits(string orderId)
        {
            string digits = new(orderId.Where(char.IsDigit).ToArray());

            if (digits.Length <= 4)
                return digits;

            return digits[^4..];
        }

        private void RegisterDeviceInDatabase(string firmwarePath)
        {
            if (_workMode == WorkMode.TestFirmware)
            {
                RegisterTestDeviceInDatabase();
                return;
            }

            if (_currentUser.IsOffline)
            {
                RegisterDeviceLocally(firmwarePath);

                if (_workMode == WorkMode.Reflash)
                    TryRegisterReworkLocally(firmwarePath);

                return;
            }

            try
            {
                _apiService.AddDeviceRegistration(
                    _currentUser.Id,
                    GetCurrentBoardSerial(),
                    _currentDeviceType,
                    GetCurrentFirmwareName(firmwarePath),
                    GetCurrentRegistrationType());
            }
            catch (Exception ex)
            {
                _localDatabaseService.AddPendingDeviceRegistration(
                    _currentUser.Id,
                    GetCurrentOrderIdForLocalRegistration(),
                    GetCurrentBoardSerial(),
                    _currentDeviceType,
                    GetCurrentLocalFirmwareName(firmwarePath),
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

        private void RegisterTestDeviceInDatabase()
        {
            if (_currentUser.IsOffline)
            {
                IncrementTodayCounter(TestRegistrationType);
                return;
            }

            try
            {
                _apiService.AddDeviceRegistration(
                    _currentUser.Id,
                    GetCurrentBoardSerial(),
                    _currentDeviceType,
                    string.Empty,
                    TestRegistrationType);
            }
            catch (Exception ex)
            {
                Log += "API ERROR: " + ex.Message + "\n";
                Log += "=== OFFLINE: тестову плату не збережено в локальну базу ===\n";
            }

            IncrementTodayCounter(TestRegistrationType);
        }

        private void RegisterDeviceLocally(string firmwarePath)
        {
            try
            {
                _localDatabaseService.AddPendingDeviceRegistration(
                    _currentUser.Id,
                    GetCurrentOrderIdForLocalRegistration(),
                    GetCurrentBoardSerial(),
                    _currentDeviceType,
                    GetCurrentLocalFirmwareName(firmwarePath),
                    GetCurrentRegistrationType());

                LoadLocalDataForCurrentTab();
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

        private string GetCurrentFirmwareName(string firmwarePath)
        {
            string firmwareName = _workMode == WorkMode.Reflash
                ? ReworkFirmwareName
                : FirmwareName;

            return string.IsNullOrWhiteSpace(firmwareName) && !string.IsNullOrWhiteSpace(firmwarePath)
                ? System.IO.Path.GetFileNameWithoutExtension(firmwarePath)
                : firmwareName;
        }

        private string GetCurrentLocalFirmwareName(string firmwarePath)
        {
            if (_workMode == WorkMode.Production && !string.IsNullOrWhiteSpace(firmwarePath))
                return GetFileNameFromPath(firmwarePath);

            return GetCurrentFirmwareName(firmwarePath);
        }

        private static string GetFileNameFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            string trimmedPath = path.Trim();
            int slashIndex = trimmedPath.LastIndexOf('/');
            int backslashIndex = trimmedPath.LastIndexOf('\\');
            int separatorIndex = Math.Max(slashIndex, backslashIndex);

            return separatorIndex >= 0 && separatorIndex < trimmedPath.Length - 1
                ? trimmedPath[(separatorIndex + 1)..]
                : trimmedPath;
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
            string firmwareName = GetCurrentFirmwareName(firmwarePath);

            _apiService.AddReworkRegistration(
                _currentUser.Id,
                ReworkOrderId,
                GetCurrentBoardSerial(),
                firmwareName,
                firmwarePath,
                _currentDeviceType,
                "OK");
        }

        private void RegisterReworkLocally(string firmwarePath)
        {
            string firmwareName = GetCurrentFirmwareName(firmwarePath);

            _localDatabaseService.AddPendingReworkRegistration(
                _currentUser.Id,
                ReworkOrderId,
                GetCurrentBoardSerial(),
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
                LoadLocalDataForCurrentTab();
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
                    LoadLocalDataForCurrentTab();
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
                LoadLocalDataForCurrentTab();

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
            LoadLocalDataForCurrentTab();
        }

        private void LoadSettings()
        {
            var settings = _settingsService.Load();

            SelectedComPort = settings.SelectedComPort;
            FirmwarePath = settings.FirmwarePath;
            FirmwareName = settings.FirmwareName;
            ReworkFirmwareName = settings.ReworkFirmwareName;
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
                ReworkFirmwareName = ReworkFirmwareName,
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
            if (_uniqueBoardCounter.Register(GetCurrentBoardSerial()))
                OnPropertyChanged(nameof(UniqueFlashedSinceStart));
        }

    }
}
