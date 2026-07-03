using FCatFlasher.Helpers;
using FCatFlasher.Models;
using FCatFlasher.Services;
using FCatFlasher.Services.Workflows;
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
        private const int PowerOffBeforeRestartDelayMs = 300;
        private const int BoardInsertPowerOffDelayMs = 300;
        private const int BoardInsertPowerOnDelayMs = 50;
        // private const int BoardRemovalPowerOffDelayMs = 100;
        private const int StandCommandSettleDelayMs = 100;
        private const int UartPowerOffDelayMs = 100;
        private const int StlinkPowerOffDelayMs = 200;
        private const int PowerOnSettleDelayMs = 50;
        private const int UartPowerOnSettleDelayMs = 50;
        private const int UartModeSwitchDelayMs = 100;
        private const int StlinkModeSwitchDelayMs = 100;
        private const int BuzzerOkDelayMs = 100;
        private const int CycleErrorDelayMs = 2000;
        private const int StlinkConnectionCheckTimeoutMs = 1000;
        private const int BoardRemovalCheckTimeoutMs = 250;
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
        private readonly UniqueBoardCounter _productionUniqueBoardCounter = new();
        private readonly UniqueBoardCounter _reflashUniqueBoardCounter = new();
        private readonly ProductionFlashWorkflow _productionFlashWorkflow;
        private readonly ReflashWorkflow _reflashWorkflow;
        private readonly TestFirmwareWorkflow _testFirmwareWorkflow;
        private readonly SemaphoreSlim _standCommandLock = new(1, 1);
        private readonly DispatcherTimer _runTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private bool _settingsLoaded;
        private string _currentDeviceType = "";
        private string _lastConnectionError = "";
        private DateTime _runStartedAt;
        private DateTime? _currentBoardStartedAt;
        private bool _stlinkSelectedForRun;
        private bool _standPowerOn;
        private bool _skipReflashDiagnosticsReport;
        private bool _skipFlashRegistration;
        private bool _measureBoardCycleTime;
        private bool _singleProductionFlash;
        private bool _testOnlyMode;
        private bool _optimizeFlashMode;
        private bool _startRunTimerOnBoardDetected;
        private bool _startingFlashAfterSerial;
        private bool _productionBoxTimerStarted;
        private CancellationTokenSource? _serialEntryCts;

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
            WaitingSerial,
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
        public ICommand SyncPendingCommand { get; }

        public RelayCommand TestFirmwareCommand { get; }
        public RelayCommand TestOnlyCommand { get; }
        public RelayCommand OptimizeFlashModeCommand { get; }
        public RelayCommand FlashCommand { get; }
        public RelayCommand SingleFlashCommand { get; }
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

                if (string.IsNullOrEmpty(_scannedSerial) && !IsSerialInputEnabled)
                    IsSerialInputEnabled = true;

                if (_cycleState == CycleState.WaitingSerial &&
                    IsSerialReadyForCurrentMode(_scannedSerial))
                {
                    ScheduleNextStepAfterSerialEntered();
                }
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
                TestOnlyCommand.RaiseCanExecuteChanged();
                OptimizeFlashModeCommand.RaiseCanExecuteChanged();
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
            get => _footerMode switch
            {
                WorkMode.Reflash => _reflashUniqueBoardCounter.Count,
                WorkMode.Production => _productionUniqueBoardCounter.Count,
                _ => 0
            };
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
            _productionFlashWorkflow = new ProductionFlashWorkflow(
                SendStandCommand,
                SetStlinkModeAndEnsurePowerOn,
                StartProductionFlashing);
            _reflashWorkflow = new ReflashWorkflow(
                SendStandCommand,
                StartReflashFlashing);
            _testFirmwareWorkflow = new TestFirmwareWorkflow(
                () => _cycleState = CycleState.Flashing,
                () => _cycleState = CycleState.WaitingRemove,
                value => Status = value,
                value => ProgressValue = value,
                value => Log += value,
                value => _currentDeviceType = value,
                EnsureStlinkReady,
                GetDeviceTypeWithRetry,
                IsKnownDeviceType,
                FlashAutoTestFirmwareWithRetry,
                TestFirmwareFailed,
                SendStandCommand,
                ShowTestErrorDialog,
                RegisterDeviceInDatabase,
                WaitBoardRemoved,
                WaitBoardRemovedAfterError);

            TestFirmwareCommand = new RelayCommand(
                async () =>
                {
                    if (!EnsureConnected())
                        return;

                    _workMode = WorkMode.TestFirmware;
                    _stlinkSelectedForRun = false;
                    _startRunTimerOnBoardDetected = true;
                    ResetRunTimer();
                    await StartFullCycle();
                },
                () => !IsBusy
            );

            TestOnlyCommand = new RelayCommand(
                async () =>
                {
                    if (!EnsureConnected())
                        return;

                    _workMode = WorkMode.TestFirmware;
                    _testOnlyMode = true;
                    _stlinkSelectedForRun = false;
                    ResetRunTimer();
                    Log = "";
                    ProgressValue = 0;
                    IsBusy = true;

                    try
                    {
                        StartRunTimer();
                        await StartTest();
                    }
                    catch (Exception ex)
                    {
                        _testOnlyMode = false;
                        Status = "Error";
                        Log += "ERROR: " + ex.Message + "\n";
                        StopRunTimer();
                        IsBusy = false;
                        _cycleState = CycleState.Idle;
                    }
                },
                () => !IsBusy
            );

            OptimizeFlashModeCommand = new RelayCommand(
                async () =>
                {
                    if (!RequestProductionStartData())
                        return;

                    if (!EnsureConnected())
                        return;

                    _workMode = WorkMode.Production;
                    _optimizeFlashMode = true;
                    _skipFlashRegistration = true;
                    _stlinkSelectedForRun = false;
                    _productionBoxActive = false;
                    ResetUniqueFlashedCounter();
                    LoadLocalDataForCurrentTab();
                    ResetRunTimer();
                    Log = "";
                    ProgressValue = 0;
                    IsBusy = true;

                    try
                    {
                        StartRunTimer();
                        await StartTest();
                    }
                    catch (Exception ex)
                    {
                        _optimizeFlashMode = false;
                        _skipFlashRegistration = false;
                        Status = "Error";
                        Log += "ERROR: " + ex.Message + "\n";
                        StopRunTimer();
                        IsBusy = false;
                        _cycleState = CycleState.Idle;
                    }
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
                    _stlinkSelectedForRun = false;
                    _productionBoxActive = true;
                    ResetProductionBoxTimer();
                    _startRunTimerOnBoardDetected = true;
                    ResetUniqueFlashedCounter();
                    LoadLocalDataForCurrentTab();
                    await StartFullCycle();
                },
                () => !IsBusy
            );

            SingleFlashCommand = new RelayCommand(
                async () =>
                {
                    if (!EnsureConnected())
                        return;

                    _workMode = WorkMode.Production;
                    _singleProductionFlash = true;
                    _skipFlashRegistration = true;
                    _measureBoardCycleTime = true;
                    _startRunTimerOnBoardDetected = true;
                    _stlinkSelectedForRun = false;
                    ResetRunTimer();

                    try
                    {
                        await StartFullCycle();
                    }
                    finally
                    {
                        _singleProductionFlash = false;
                        _skipFlashRegistration = false;
                        _measureBoardCycleTime = false;
                    }
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
                    _startRunTimerOnBoardDetected = true;
                    ResetRunTimer();
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
                    _startRunTimerOnBoardDetected = true;
                    ResetRunTimer();

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
            SyncPendingCommand = new RelayCommand(SyncPendingData, () => !IsBusy);
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

        private void SyncPendingData()
        {
            if (_currentUser.IsOffline)
            {
                Log += "SYNC ERROR: користувач увійшов offline. Увійдіть через API і повторіть синхронізацію.\n";
                return;
            }

            try
            {
                _localDatabaseService.SyncPending(_apiService);
                LoadApiData();
                LoadLocalDataForCurrentTab();
                Log += "=== SYNC: локальні дані синхронізовано з основною базою ===\n";
            }
            catch (Exception ex)
            {
                LoadLocalDataForCurrentTab();
                Log += "SYNC ERROR: " + ex.Message + "\n";
            }
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
            ResetRunTimer();
            _runTimer.Start();
        }

        private void ResetRunTimer()
        {
            _runStartedAt = DateTime.Now;
            RunElapsedText = "00:00:00";
        }

        private void ResetProductionBoxTimer()
        {
            _productionBoxTimerStarted = false;
            _runTimer.Stop();
            ResetRunTimer();
        }

        private void StartProductionBoxTimerIfNeeded()
        {
            if (_workMode != WorkMode.Production || _productionBoxTimerStarted)
                return;

            _productionBoxTimerStarted = true;
            StartRunTimer();
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
            _currentBoardStartedAt = null;
            _lastConnectionError = "";
            _startingFlashAfterSerial = false;
            _serialEntryCts?.Cancel();
            _serialEntryCts = null;
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

                await StartModeCycle();
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

        private async Task StartModeCycle()
        {
            await StartStlinkBoardDetectionCycle();
        }

        private async Task StartStlinkBoardDetectionCycle()
        {
            if (!_stlinkSelectedForRun)
            {
                await PowerCycleToStlinkMode();
                _stlinkSelectedForRun = true;
            }

            await WaitBoardInserted();
        }

        private async Task WaitBoardInserted()
        {
            Status = "Waiting board";

            while (_cycleState == CycleState.WaitingBoard)
            {
                if (!_standPowerOn)
                    await SendStandCommand("POWER_ON", BoardInsertPowerOnDelayMs, enforceMinimumDelay: false);

                if (_cycleState != CycleState.WaitingBoard)
                    return;

                bool present = _workMode == WorkMode.Production
                    ? await _firmwareService.CheckBoardPresence()
                    : await _firmwareService.CheckConnection();

                if (present)
                {
                    _lastConnectionError = "";
                    ProgressValue = 20;
                    Log += "Плату знайдено\n";
                    _currentBoardStartedAt = DateTime.Now;

                    if (_startRunTimerOnBoardDetected)
                    {
                        StartRunTimer();
                        _startRunTimerOnBoardDetected = false;

                        if (_workMode == WorkMode.Production)
                            _productionBoxTimerStarted = true;
                    }

                    if (_measureBoardCycleTime && !_runTimer.IsEnabled)
                        StartRunTimer();

                    if (_workMode == WorkMode.TestFirmware)
                    {
                        if (_testOnlyMode)
                        {
                            await StartTest();
                            return;
                        }

                        await FlashOnlyTestFirmware();
                        return;
                    }

                    if (_workMode == WorkMode.Reflash)
                    {
                        await PrepareReflashWithTestFirmware();
                        return;
                    }

                    if (_workMode == WorkMode.Production)
                    {
                        await PrepareProductionWithDetectedMcu();
                        return;
                    }

                    Log += "ERROR: Unknown work mode\n";
                    await StopAll();
                    return;
                }

                LogConnectionErrorIfChanged();
                await CycleBoardPowerWhileWaiting();
            }
        }

        private async Task PrepareProductionWithDetectedMcu()
        {
            try
            {
                _cycleState = CycleState.Flashing;
                _currentDeviceType = string.Empty;
    
                if (!ValidateProductionFirmwareFileExists())
                {
                    _cycleState = CycleState.WaitingRemove;

                    await WaitBoardRemovedAfterError();
                    return;
                }

                if (_singleProductionFlash)
                {
                    await StartTest();
                    return;
                }

                WaitProductionSerialBeforeTest();
            }
            catch (Exception ex)
            {
                Status = "Error";
                Log += "ERROR: " + ex.Message + "\n";

                _cycleState = CycleState.WaitingRemove;

                await WaitBoardRemovedAfterError();
            }
        }

        private void WaitProductionSerialBeforeTest()
        {
            _cycleState = CycleState.WaitingSerial;
            Status = "Waiting serial";
            ProgressValue = 40;
            IsSerialInputEnabled = true;
            Log += "Очікуємо серійник D + 7 цифр\n";

            if (IsSerialReadyForCurrentMode(ScannedSerial))
                ScheduleNextStepAfterSerialEntered();
        }

        private void LogConnectionErrorIfChanged()
        {
            string error = _firmwareService.LastConnectionError;

            if (string.IsNullOrWhiteSpace(error) ||
                error == "ST-LINK OK, target MCU not connected" ||
                error == _lastConnectionError)
                return;

            _lastConnectionError = error;
            Log += "ST-LINK/API: " + error + "\n";
        }

        private async Task FlashOnlyTestFirmware()
        {
            await _testFirmwareWorkflow.FlashOnlyTestFirmware();
        }

        private async Task PrepareReflashWithTestFirmware()
        {
            try
            {
                _cycleState = CycleState.Flashing;

                Status = "Preparing reflash";
                ProgressValue = 30;
                Log += "=== ПЕРЕПРОШИВКА: СПОЧАТКУ ТЕСТОВА ===\n";

                (string deviceType, string result) = await FlashAutoTestFirmwareWithUnlockSessionAndRetry();
                _currentDeviceType = deviceType;

                if (TestFirmwareFailed(result))
                {
                    Status = "Test firmware error";
                    ProgressValue = 100;
                    Log += result + "\n";
                    Log += "\n=== ТЕСТОВУ ПРОШИВКУ НЕ ЗАЛИТО ===\n";
                    Log += ">>> Заберіть плату зі стенда...\n";
                    ShowTestErrorDialog("Помилка тестової прошивки", result);

                    _cycleState = CycleState.WaitingRemove;

                    await WaitBoardRemovedAfterError();
                    return;
                }

                Log += result + "\n";
                Log += "\n=== ТЕСТОВУ ПРОШИВКУ ЗАЛИТО ===\n";

                await Task.Delay(50);

                if (_skipReflashDiagnosticsReport)
                {
                    Log += "=== REPORT ПРОПУЩЕНО ===\n";
                    await StartReflashFlashing();
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

        private async Task<(string DeviceType, string Output)> FlashAutoTestFirmwareWithUnlockSessionAndRetry()
        {
            (string deviceType, string output) =
                await _testFirmwareService.FlashAutoTestFirmwareWithUnlockSession();

            if (!TestFirmwareFailed(output))
                return (deviceType, output);

            Log += "\nWARNING: MCU/RDP не готові. Робимо power cycle і пробуємо ще раз...\n";

            await PowerCycleToStlinkMode();

            return await _testFirmwareService.FlashAutoTestFirmwareWithUnlockSession();
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
            await SendStandCommand("POWER_ON", PowerOnSettleDelayMs, enforceMinimumDelay: false);

            return await _firmwareService.GetDeviceType();
        }

        private async Task<string> GetProductionDeviceType()
        {
            string cachedDeviceType = GetDeviceTypeFromMcuName(_firmwareService.LastMcuName);

            if (IsKnownDeviceType(cachedDeviceType))
            {
                return cachedDeviceType;
            }

            return await GetDeviceTypeWithRetry("prepare production");
        }

        private static string GetDeviceTypeFromMcuName(string mcuName)
        {
            if (mcuName.Contains("STM32G03") ||
                mcuName.Contains("STM32G04") ||
                mcuName.Contains("STM32G0"))
                return "G0";

            if (mcuName.Contains("STM32C0"))
                return "C0";

            return "UNKNOWN";
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

        private async Task<string> FlashProductionFirmwareWithRecovery(string firmwareToFlash)
        {
            string result = await _firmwareService.FlashProductionFirmware(firmwareToFlash);

            if (!IsRecoverableStlinkFlashFailure(result))
                return result;

            Log += "WARNING: ST-LINK/API lost during flash. Повторно подаємо живлення і пробуємо ще раз...\n";
            await PowerCycleToStlinkMode();

            return await _firmwareService.FlashProductionFirmware(firmwareToFlash);
        }

        private async Task<FirmwareService.ProductionFlashSessionResult> FlashProductionFirmwareSessionWithRecovery(
            string firmwareToFlash)
        {
            FirmwareService.ProductionFlashSessionResult result = await _firmwareService
                .FlashProductionFirmwareSession(
                    firmwareToFlash,
                    deviceType => ValidateProductionFlashSessionDevice(deviceType, firmwareToFlash));

            if (!IsRecoverableStlinkFlashFailure(result.Output))
                return result;

            Log += "WARNING: ST-LINK/API lost during flash. Повторно подаємо живлення і пробуємо ще раз...\n";
            await PowerCycleToStlinkMode();

            return await _firmwareService.FlashProductionFirmwareSession(
                firmwareToFlash,
                deviceType => ValidateProductionFlashSessionDevice(deviceType, firmwareToFlash));
        }

        private static bool ShouldRetryProductionFlashSession(
            FirmwareService.ProductionFlashSessionResult result)
        {
            return !IsKnownDeviceType(result.DeviceType) ||
                   IsRecoverableStlinkFlashFailure(result.Output);
        }
        private static string ValidateProductionFlashSessionDevice(string deviceType, string firmwarePath)
        {
            if (!IsKnownDeviceType(deviceType))
                return $"ERROR: MCU not detected: {deviceType}\n";

            if (!System.IO.File.Exists(firmwarePath))
                return $"Firmware not found: {firmwarePath}\n";

            if (deviceType == "C0" && !IsC0FirmwareNameValid(firmwarePath))
                return $"ERROR: C0 firmware filename must contain c011: {firmwarePath}\n";

            return string.Empty;
        }

        private static bool IsRecoverableStlinkFlashFailure(string result)
        {
            return result.Contains("STM32CubeProgrammer API unavailable", StringComparison.OrdinalIgnoreCase) ||
                   result.Contains("getStLinkList", StringComparison.OrdinalIgnoreCase) ||
                   result.Contains("connect attempts", StringComparison.OrdinalIgnoreCase) ||
                   result.Contains("ST-LINK", StringComparison.OrdinalIgnoreCase) ||
                   result.Contains("timeout", StringComparison.OrdinalIgnoreCase);
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

                        if (_testOnlyMode)
                        {
                            await CompleteSuccessfulTestOnly();
                            return;
                        }

                        if (_workMode == WorkMode.Production)
                        {
                            if (!_singleProductionFlash)
                            {
                                _currentBoardSerial = ScannedSerial.Trim();
                                ScannedSerial = "";
                            }

                            IsSerialInputEnabled = true;

                            await StopSuccessfulTestAndFlash();
                            return;
                        }

                        if (_workMode == WorkMode.Reflash)
                        {
                            _cycleState = CycleState.WaitingSerial;
                            IsSerialInputEnabled = true;
                            Log += ">>> Введіть серійний номер D + від 6 цифр для початку прошивки...\n";

                            if (IsSerialReadyForCurrentMode(ScannedSerial))
                                ScheduleNextStepAfterSerialEntered();

                            return;
                        }

                        await StopSuccessfulTestAndFlash();
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

                    if (_testOnlyMode)
                    {
                        _testOnlyMode = false;
                        StopRunTimer();
                        IsBusy = false;
                        _cycleState = CycleState.Idle;
                        return;
                    }

                    if (_optimizeFlashMode)
                    {
                        _optimizeFlashMode = false;
                        _skipFlashRegistration = false;
                        StopRunTimer();
                        IsBusy = false;
                        _cycleState = CycleState.Idle;
                        return;
                    }

                    await StopTestAndWaitRemove();

                    return;
                }
            });
        }

        private void ScheduleNextStepAfterSerialEntered()
        {
            _serialEntryCts?.Cancel();
            _serialEntryCts = new CancellationTokenSource();

            _ = StartNextStepAfterSerialEntered(_serialEntryCts.Token);
        }

        private async Task StartNextStepAfterSerialEntered(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(50, cancellationToken);

                if (_cycleState != CycleState.WaitingSerial)
                    return;

                cancellationToken.ThrowIfCancellationRequested();

                if (_startingFlashAfterSerial)
                    return;

                if (!IsSerialReadyForCurrentMode(ScannedSerial))
                    return;

                _startingFlashAfterSerial = true;
                _serialEntryCts?.Cancel();
                _serialEntryCts = null;
                IsSerialInputEnabled = false;

                if (_workMode == WorkMode.Production)
                {
                    StartProductionBoxTimerIfNeeded();
                    await StartTest();
                    return;
                }

                _currentBoardSerial = ScannedSerial.Trim();
                await StopSuccessfulTestAndFlash();
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _startingFlashAfterSerial = false;
            }
        }

        private bool IsSerialReadyForCurrentMode(string serial)
        {
            return _workMode == WorkMode.Production
                ? IsProductionSerialValid(serial)
                : IsReflashSerialValid(serial);
        }

        private static bool IsReflashSerialValid(string serial)
        {
            string trimmedSerial = serial.Trim();

            if (trimmedSerial.Length < 7 ||
                char.ToUpperInvariant(trimmedSerial[0]) != 'D')
                return false;

            for (int index = 1; index < trimmedSerial.Length; index++)
            {
                if (!char.IsDigit(trimmedSerial[index]))
                    return false;
            }

            return true;
        }

        private static bool IsProductionSerialValid(string serial)
        {
            string trimmedSerial = serial.Trim();

            if (trimmedSerial.Length != 8 || trimmedSerial[0] != 'D')
                return false;

            for (int index = 1; index < trimmedSerial.Length; index++)
            {
                if (!char.IsDigit(trimmedSerial[index]))
                    return false;
            }

            return true;
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
            int powerOffDelayMs = UartPowerOffDelayMs,
            int modeSwitchDelayMs = UartModeSwitchDelayMs,
            int powerOnDelayMs = PowerOnSettleDelayMs)
        {
            await SendStandCommand("POWER_OFF", powerOffDelayMs);
            await SetUartMode(modeSwitchDelayMs);
            await SendStandCommand("POWER_ON", powerOnDelayMs, enforceMinimumDelay: false);
        }

        private async Task SetUartModeKeepingPowerOn(
            int modeSwitchDelayMs = UartModeSwitchDelayMs,
            int powerOnDelayMs = 0)
        {
            await SetUartMode(modeSwitchDelayMs);
            await SendStandCommand("POWER_ON", powerOnDelayMs, enforceMinimumDelay: false);
        }

        private async Task SetStlinkModeAndEnsurePowerOn(
            int modeSwitchDelayMs = StlinkModeSwitchDelayMs,
            int powerOnDelayMs = UartPowerOnSettleDelayMs)
        {
            bool powerAlreadyOn = _standPowerOn;
            await SetStlinkMode(modeSwitchDelayMs);

            if (!powerAlreadyOn)
                await SendStandCommand("POWER_ON", powerOnDelayMs, enforceMinimumDelay: false);
        }
        private async Task PowerCycleToStlinkMode()
        {
            await SendStandCommand("POWER_OFF", StlinkPowerOffDelayMs);
            await SetStlinkModeAndEnsurePowerOn(
                StlinkModeSwitchDelayMs,
                PowerOnSettleDelayMs);
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

            await SendStandCommand("POWER_ON", BoardInsertPowerOnDelayMs, enforceMinimumDelay: false);
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
            IsSerialInputEnabled = string.IsNullOrEmpty(ScannedSerial);

            _diagnosticsParser.Reset();

            if (_optimizeFlashMode)
            {
                await SetUartModeKeepingPowerOn();
            }
            else if (_workMode == WorkMode.Reflash)
            {
                await SetUartModeAndEnsurePowerOn(
                    powerOffDelayMs: 400,
                    modeSwitchDelayMs: 100,
                    powerOnDelayMs: 0);
            }
            else
            {
                await SetUartModeAndEnsurePowerOn();
            }

            Status = "Testing";
            ProgressValue = 60;
            Log += "Тест запущено\n";
        }

        private async Task StopTestAndWaitRemove()
        {
            _cycleState = CycleState.WaitingRemove;

            await SendStandCommand("POWER_OFF", UartPowerOffDelayMs);
            await SetStlinkMode();
            await Task.Delay(100);

            await WaitBoardRemovedAfterError();
        }

        private async Task StopSuccessfulTestAndFlash()
        {
            if (_workMode == WorkMode.Production)
            {
                await StopProductionTestAndFlash();
                return;
            }

            if (_workMode == WorkMode.Reflash)
            {
                await StopReflashTestAndFlash();
                return;
            }

            Log += "ERROR: Wrong mode after successful test\n";
            await StopAll();
        }

        private async Task StopProductionTestAndFlash()
        {
            _cycleState = CycleState.Flashing;
            await _productionFlashWorkflow.FlashAfterSuccessfulTest();
        }

        private async Task StopReflashTestAndFlash()
        {
            _cycleState = CycleState.Flashing;
            await _reflashWorkflow.FlashAfterSuccessfulTest();
        }

        private async Task CompleteSuccessfulTestOnly()
        {
            await SendStandCommand("BUZZ_OK", BuzzerOkDelayMs);
            _testOnlyMode = false;

            Status = "Test OK";
            ProgressValue = 100;
            Log += "Тестування завершено\n";
            StopRunTimer();
            IsBusy = false;
            _cycleState = CycleState.Idle;
        }

        private async Task StartProductionFlashing()
        {
            try
            {
                Status = "Preparing flash";
                ProgressValue = 80;

                Status = "Flashing";
                ProgressValue = 90;
                Log += "Прошивка...\n";

                FirmwareService.ProductionFlashSessionResult result =
                    await FlashProductionFirmwareSessionWithRecovery(FirmwarePath);

                _currentDeviceType = result.DeviceType;

                if (FlashResultFailed(result.Output))
                {
                    Log += result.Output + "\n";
                    Status = "Flash Error";
                    ProgressValue = 100;
                    Log += "=== ПОМИЛКА ПРОШИВКИ ===\n";

                    if (_optimizeFlashMode)
                    {
                        _optimizeFlashMode = false;
                        _skipFlashRegistration = false;
                        StopRunTimer();
                        IsBusy = false;
                        _cycleState = CycleState.Idle;
                        return;
                    }

                    Log += ">>> Заберіть плату зі стенда...\n";

                    _cycleState = CycleState.WaitingRemove;

                    await WaitBoardRemovedAfterError();
                    return;
                }

                Log += result.Output + "\n";

                await CompleteSuccessfulFlash(FirmwarePath);
            }
            catch (Exception ex)
            {
                Status = "Flash Error";
                ProgressValue = 100;
                Log += "ERROR: " + ex.Message + "\n";

                if (_optimizeFlashMode)
                {
                    _optimizeFlashMode = false;
                    _skipFlashRegistration = false;
                    StopRunTimer();
                    IsBusy = false;
                    _cycleState = CycleState.Idle;
                    return;
                }

                _cycleState = CycleState.WaitingRemove;

                await WaitBoardRemovedAfterError();
            }
        }

        private async Task StartReflashFlashing()
        {
            string firmwareToFlash;

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

            await StartFlashing(firmwareToFlash);
        }

        private async Task StartFlashing(string firmwareToFlash, bool skipFirmwareValidation = false)
        {
            try
            {
                Status = "Preparing flash";
                ProgressValue = 80;

                Status = "Flashing";
                ProgressValue = 90;
                Log += "Прошивка...\n";
                ClearSerialInputAfterFlashStart();

                if (!skipFirmwareValidation &&
                    (_workMode == WorkMode.Production || _workMode == WorkMode.Reflash) &&
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

                if (!skipFirmwareValidation && !System.IO.File.Exists(firmwareToFlash))
                {
                    Log += $"Firmware not found: {firmwareToFlash}\n";
                    Status = "Firmware not found";
                    ProgressValue = 100;
                    Log += ">>> Заберіть плату зі стенда...\n";

                    _cycleState = CycleState.WaitingRemove;

                    await WaitBoardRemovedAfterError();
                    return;
                }

                string result = await FlashProductionFirmwareWithRecovery(firmwareToFlash);

                if (FlashResultFailed(result))
                {
                    Log += result + "\n";
                    Status = "Flash Error";
                    ProgressValue = 100;
                    Log += "=== ПОМИЛКА ПРОШИВКИ ===\n";
                    Log += ">>> Заберіть плату зі стенда...\n";

                    _cycleState = CycleState.WaitingRemove;

                    await WaitBoardRemovedAfterError();
                    return;
                }

                await CompleteSuccessfulFlash(firmwareToFlash);
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

        private async Task CompleteSuccessfulFlash(string firmwareToFlash)
        {
            await SendStandCommand("BUZZ_OK", BuzzerOkDelayMs);

            Status = "Flash OK";
            ProgressValue = 100;

            if (_optimizeFlashMode)
                Log += "=== ПІДБІР РЕЖИМУ: дані не збережено ===\n";
            else if (_skipFlashRegistration)
                Log += "=== ТЕСТОВА ПЕРЕПРОШИВКА: дані не збережено ===\n";
            else
                RegisterDeviceInDatabase(firmwareToFlash);

            if (!_skipFlashRegistration && _workMode == WorkMode.Production)
                _completedBox = RegisterFlashedBoard();

            if (!_skipFlashRegistration)
                RegisterUniqueFlashedBoard();

            Log += "Прошивку завершено\n";

            if (_optimizeFlashMode)
            {
                Log += "=== Повертаємо UART після прошивки ===\n";
                await SetUartModeKeepingPowerOn();
                Log += "=== Очікуємо тестові дані для наступної прошивки ===\n";
                await StartTest();
                return;
            }

            Log += ">>> Заберіть плату зі стенда...\n";

            _cycleState = CycleState.WaitingRemove;

            await WaitBoardRemoved();
        }
        private static bool IsC0FirmwareNameValid(string firmwarePath)
        {
            string fileName = System.IO.Path.GetFileName(firmwarePath);

            return fileName.Contains("c011", StringComparison.OrdinalIgnoreCase);
        }

        private bool ValidateProductionFirmwareFileExists()
        {
            if (System.IO.File.Exists(FirmwarePath))
                return true;

            Log += $"Firmware not found: {FirmwarePath}\n";
            Status = "Firmware not found";
            ProgressValue = 100;
            Log += ">>> Заберіть плату зі стенда...\n";
            return false;
        }

        private bool ValidateProductionFirmwareForDevice()
        {
            if (!System.IO.File.Exists(FirmwarePath))
            {
                Log += $"Firmware not found: {FirmwarePath}\n";
                Status = "Firmware not found";
                ProgressValue = 100;
                Log += ">>> Заберіть плату зі стенда...\n";
                return false;
            }

            if (_currentDeviceType == "C0" && !IsC0FirmwareNameValid(FirmwarePath))
            {
                Log += $"ERROR: C0 firmware filename must contain c011: {FirmwarePath}\n";
                Status = "Wrong C0 firmware";
                ProgressValue = 100;
                Log += ">>> Заберіть плату зі стенда...\n";
                return false;
            }

            Log += "=== MCU підходить для обраної прошивки ===\n";
            return true;
        }

        private void ClearSerialInputAfterFlashStart()
        {
            if (_workMode != WorkMode.Reflash)
                return;

            if (string.IsNullOrEmpty(ScannedSerial))
                return;

            ScannedSerial = "";
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

            await SendStandCommand("POWER_OFF", PowerOffBeforeRestartDelayMs);

            if (_completedBox != null)
            {
                await ShowBoxCompletedWindow(_completedBox);
                _completedBox = null;
            }

            await WaitUntilBoardRemoved();

            LogCurrentBoardCycleTime();

            if (_measureBoardCycleTime)
                StopRunTimer();

            await PowerOffAndRestartCycle();
        }

        private void LogCurrentBoardCycleTime()
        {
            if (_currentBoardStartedAt == null)
                return;

            TimeSpan elapsed = DateTime.Now - _currentBoardStartedAt.Value;
            Log += $"Час циклу плати: {elapsed:hh\\:mm\\:ss}\n";
            _currentBoardStartedAt = null;
        }

        private async Task WaitUntilBoardRemoved()
        {
            Status = "Remove board";

            while (_cycleState == CycleState.WaitingRemove)
            {
                await SendStandCommand("POWER_ON", BoardInsertPowerOnDelayMs, enforceMinimumDelay: false);

                bool boardPresent = await _firmwareService.CheckBoardPresence(BoardRemovalCheckTimeoutMs);

                await SendStandCommand("POWER_OFF", BoardInsertPowerOffDelayMs);

                if (!boardPresent)
                    return;
            }
        }

        private async Task PowerOffAndRestartCycle()
        {
            try
            {
                ResetCycleData();

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
                _testOnlyMode = false;
                _optimizeFlashMode = false;
                _skipFlashRegistration = false;
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
                IsSerialInputEnabled = true;
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
            {
                SaveBoxReport(report);
                _boxTracking.NextBox();
            }

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
                Column2 = string.Empty,
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
                    GetCurrentMcuName(),
                    GetCurrentFirmwareName(firmwarePath),
                    GetCurrentRegistrationType());
            }
            catch (Exception ex)
            {
                _localDatabaseService.AddPendingDeviceRegistration(
                    _currentUser.Id,
                    GetCurrentOrderIdForLocalRegistration(),
                    GetCurrentBoardSerial(),
                    GetCurrentMcuId(),
                    GetCurrentMcuName(),
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
                    GetCurrentMcuName(),
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
                    GetCurrentMcuId(),
                    GetCurrentMcuName(),
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
            return GetCurrentFirmwareName(firmwarePath);
        }

        private string GetCurrentMcuId()
        {
            if (!string.IsNullOrWhiteSpace(_diagnosticsParser.LastMcuSerial))
                return _diagnosticsParser.LastMcuSerial;

            return string.Empty;
        }

        private string GetCurrentMcuName()
        {
            if (!string.IsNullOrWhiteSpace(_firmwareService.LastMcuName))
                return _firmwareService.LastMcuName;

            return GetFallbackMcuName();
        }

        private string GetFallbackMcuName()
        {
            return _currentDeviceType switch
            {
                "G0" => "STM32G0",
                "C0" => "STM32C0",
                _ => _currentDeviceType
            };
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
                GetCurrentMcuId(),
                GetCurrentMcuName(),
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
                GetCurrentMcuId(),
                GetCurrentMcuName(),
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
                ResetProductionBoxTimer();
                await StopAll();
                return;
            }

            ResetProductionBoxTimer();
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
            if (_workMode == WorkMode.Reflash)
                _reflashUniqueBoardCounter.Reset();
            else if (_workMode == WorkMode.Production)
                _productionUniqueBoardCounter.Reset();

            OnPropertyChanged(nameof(UniqueFlashedSinceStart));
        }

        private void RegisterUniqueFlashedBoard()
        {
            UniqueBoardCounter counter = _workMode == WorkMode.Reflash
                ? _reflashUniqueBoardCounter
                : _productionUniqueBoardCounter;

            if (counter.Register(GetCurrentBoardSerial()))
                OnPropertyChanged(nameof(UniqueFlashedSinceStart));
        }

    }
}
