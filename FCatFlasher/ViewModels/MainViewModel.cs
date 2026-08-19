using FCatFlasher.Helpers;
using FCatFlasher.Models;
using FCatFlasher.Services;
using FCatFlasher.Services.Workflows;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;

namespace FCatFlasher.ViewModels
{
/// <summary>
/// Головна ViewModel: координує режими тесту, прошивки, rework, UI-стан, стенд і локальну/API реєстрацію.
/// </summary>
    internal class MainViewModel : INotifyPropertyChanged
    {
        private const int PowerOffBeforeRestartDelayMs = 300;
        private const int BoardInsertPowerOffDelayMs = 300;
        private const int BoardInsertPowerOnDelayMs = 50;
        // private const int BoardRemovalPowerOffDelayMs = 100;
        private const int BuzzerOkDelayMs = 100;
        private const int CycleErrorDelayMs = 2000;
        private const int SerialEntryDebounceDelayMs = 250;
        private const string TestRegistrationType = "TEST";
        private const string ProductionRegistrationType = "PRODUCTION";
        private const string ReworkRegistrationType = "REWORK";

        private readonly FirmwareService _firmwareService = new();
        private readonly TestFirmwareService _testFirmwareService;
        private readonly SerialService _serial = new();
        private readonly StandController _stand;
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
        private readonly CycleTimerService _cycleTimer = new();
        private readonly FileDialogService _fileDialogs = new();
        private readonly SerialPortDiscoveryService _serialPorts = new();
        private readonly LocalDataTableService _localDataTables;
        private IReadOnlyList<ReworkSummary>? _globalReworkSummaries;
        private bool _settingsLoaded;
        private string _currentDeviceType = "";
        private string _lastConnectionError = "";
        private bool _stlinkSelectedForRun;
        private bool _skipReflashDiagnosticsReport;
        private bool _skipFlashRegistration;
        private bool _measureBoardCycleTime;
        private bool _singleProductionFlash;
        private bool _testOnlyMode;
        private bool _startRunTimerOnBoardDetected;
        private bool _startingFlashAfterSerial;
        private bool _retryingProductionBoard;
        private readonly object _backgroundSyncLock = new();
        private bool _backgroundSyncRunning;
        private bool _backgroundSyncRequested;
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
        public ICommand StopCommand { get; }
        public RelayCommand NewProductionBoxCommand { get; }
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
                _testFirmwareService.TestFirmwareDirectory = _testFirmwareDirectory;
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
                FlashCommand.RaiseCanExecuteChanged();
                ReflashCommand.RaiseCanExecuteChanged();
                ReflashTestFirmwareCommand.RaiseCanExecuteChanged();
                NewProductionBoxCommand?.RaiseCanExecuteChanged();
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
            _stand = new StandController(_serial);
            _localDataTables = new LocalDataTableService(_localDatabaseService, _boxTracking);
            _productionFlashWorkflow = new ProductionFlashWorkflow(
                _stand.SendCommand,
                _stand.SetStlinkModeAndEnsurePowerOn,
                StartProductionFlashing);
            _reflashWorkflow = new ReflashWorkflow(
                _stand.SendCommand,
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
                _stand.SendCommand,
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

            FlashCommand = new RelayCommand(
                async () =>
                {
                    if (!RequestProductionStartData())
                        return;

                    if (!EnsureConnected())
                        return;

                    _workMode = WorkMode.Production;
                    _retryingProductionBoard = false;
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
            OpenSettingsCommand = new RelayCommand(OpenSettings);
            SyncPendingCommand = new RelayCommand(SyncPendingData, () => !IsBusy);
            StopCommand = new RelayCommand(async () => await StopAll());
            NewProductionBoxCommand = new RelayCommand(
                CompleteCurrentProductionBoxByOperator,
                () => !IsBusy && _boxTracking.HasCurrentBox);
            OpenProductionReportCommand = new RelayCommand(() => OpenReportWindow(isReworkReport: false));
            OpenReworkReportCommand = new RelayCommand(() => OpenReportWindow(isReworkReport: true));

            _cycleTimer.RunElapsedChanged += value => RunElapsedText = value;
            _serial.DataReceived += OnDataReceived;

            RefreshComPorts();
            LoadSettings();
            LoadApiData();
            LoadLocalDataForCurrentTab();
            _settingsLoaded = true;
            ScheduleBackgroundSync();
        }

        /// <summary>
        /// Відкриває вікно налаштувань із поточним ViewModel як DataContext.
        /// </summary>
        private void OpenSettings()
        {
            var window = new SettingsWindow
            {
                DataContext = this,
                Owner = Application.Current.MainWindow
            };

            window.ShowDialog();
        }

        /// <summary>
        /// Синхронізує локально збережені реєстрації та коробки з API.
        /// </summary>
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

        /// <summary>
        /// Відкриває звіт для виробничих або rework-операцій.
        /// </summary>
        private void ScheduleBackgroundSync()
        {
            if (_currentUser.IsOffline)
                return;

            lock (_backgroundSyncLock)
            {
                _backgroundSyncRequested = true;

                if (_backgroundSyncRunning)
                    return;

                _backgroundSyncRunning = true;
            }

            _ = Task.Run(BackgroundSyncLoop);
        }

        private void BackgroundSyncLoop()
        {
            while (TakeBackgroundSyncRequest())
            {
                try
                {
                    _localDatabaseService.SyncPending(_apiService);
                    IReadOnlyList<ReworkSummary>? globalReworks =
                        _apiService.LoadReworkSummaries(DateTime.Today);
                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        _globalReworkSummaries = globalReworks;
                        LoadLocalDataForCurrentTab();
                    });
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        Log += "SYNC BACKGROUND ERROR: " + ex.Message + "\n";
                    });

                    break;
                }
            }

            bool restart;

            lock (_backgroundSyncLock)
            {
                _backgroundSyncRunning = false;
                restart = _backgroundSyncRequested;
            }

            if (restart)
                ScheduleBackgroundSync();
        }

        private bool TakeBackgroundSyncRequest()
        {
            lock (_backgroundSyncLock)
            {
                if (!_backgroundSyncRequested)
                    return false;

                _backgroundSyncRequested = false;
                return true;
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

        /// <summary>
        /// Перемикає режим лічильника у футері відповідно до активної вкладки.
        /// </summary>
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

        /// <summary>
        /// Запитує номер замовлення для rework-режиму та оновлює пов'язані дані.
        /// </summary>
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

        /// <summary>
        /// Перевіряє стартові дані виробничого циклу: назву прошивки та кількість у коробці.
        /// </summary>
        private bool RequestProductionStartData()
        {
            var window = new ProductionStartWindow(
                FirmwareName,
                BoxQuantity,
                _boxTracking.CurrentBoxNumber,
                _boxTracking.CurrentBoxCount)
            {
                Owner = Application.Current.MainWindow
            };

            if (window.ShowDialog() != true)
                return false;

            if (window.StartMode == global::FCatFlasher.ProductionBoxStartMode.StartNew)
                CompleteCurrentProductionBox(showDialog: false);

            FirmwareName = window.FirmwareName;
            BoxQuantity = window.BoxQuantity;
            return true;
        }

        /// <summary>
        /// Оновлює список доступних COM-портів і підтримує поточний вибір валідним.
        /// </summary>
        private void RefreshComPorts()
        {
            ComPorts.Clear();

            foreach (string port in _serialPorts.GetPortNames())
                ComPorts.Add(port);

            if (ComPorts.Count > 0 && string.IsNullOrWhiteSpace(SelectedComPort))
                SelectedComPort = ComPorts[0];
        }

        private void BrowseFirmware()
        {
            string? path = _fileDialogs.BrowseFirmwareFile();

            if (path != null)
                FirmwarePath = path;
        }

        private void BrowseG0Firmware()
        {
            string? path = _fileDialogs.BrowseFirmwareFile();

            if (path != null)
                G0FirmwarePath = path;
        }

        private void BrowseC0Firmware()
        {
            string? path = _fileDialogs.BrowseFirmwareFile();

            if (path != null)
                C0FirmwarePath = path;
        }

        private void BrowseTestFirmwareDirectory()
        {
            string? path = _fileDialogs.BrowseTestFirmwareDirectory(TestFirmwareDirectory);

            if (path != null)
                TestFirmwareDirectory = path;
        }

        /// <summary>
        /// Гарантує відкрите UART-з'єднання зі стендом перед запуском команд.
        /// </summary>
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

        /// <summary>
        /// Перевіряє, що серійний номер введено у форматі поточного режиму.
        /// </summary>
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

        /// <summary>
        /// Запускає таймер загального часу циклу.
        /// </summary>
        private void StartRunTimer()
        {
            _cycleTimer.StartRunTimer();
        }

        private void ResetRunTimer()
        {
            _cycleTimer.ResetRunTimer();
        }

        private void ResetProductionBoxTimer()
        {
            _cycleTimer.ResetProductionBoxTimer();
        }

        /// <summary>
        /// Стартує вимір часу виробничої коробки один раз для активного циклу.
        /// </summary>
        private void StartProductionBoxTimerIfNeeded()
        {
            if (_cycleTimer.StartProductionBoxTimerIfNeeded(_workMode == WorkMode.Production))
                _startRunTimerOnBoardDetected = false;
        }

        private void StopRunTimer()
        {
            _cycleTimer.StopRunTimer();
        }

        /// <summary>
        /// Скидає дані поточної плати перед новим циклом.
        /// </summary>
        private void ResetCycleData()
        {
            if (!_retryingProductionBoard)
                _currentBoardSerial = "";
            _cycleTimer.ResetBoardCycle();
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

        /// <summary>
        /// Готує стан UI та запускає повний робочий цикл до зупинки або завершення.
        /// </summary>
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

        /// <summary>
        /// У виробничому режимі плата вже встановлена; інші режими очікують її через ST-LINK.
        /// </summary>
        private async Task StartModeCycle()
        {
            if (_workMode == WorkMode.Production)
            {
                Log += "=== Плата вже на стенді, очікування плати пропущено ===\n";
                await PrepareProductionWithDetectedMcu();
                return;
            }

            await StartStlinkBoardDetectionCycle();
        }

        /// <summary>
        /// Перемикає стенд у ST-LINK режим і запускає очікування появи плати.
        /// </summary>
        private async Task StartStlinkBoardDetectionCycle()
        {
            if (!_stlinkSelectedForRun)
            {
                await _stand.PowerCycleToStlinkMode();
                _stlinkSelectedForRun = true;
            }

            await WaitBoardInserted();
        }

        /// <summary>
        /// Чекає плату на стенді, періодично подаючи живлення і перевіряючи зв'язок.
        /// </summary>
        private async Task WaitBoardInserted()
        {
            Status = "Waiting board";

            while (_cycleState == CycleState.WaitingBoard)
            {
                if (!_stand.IsPowerOn)
                    await _stand.SendCommand("POWER_ON", BoardInsertPowerOnDelayMs, enforceMinimumDelay: false);

                if (_cycleState != CycleState.WaitingBoard)
                    return;

                bool present = await _firmwareService.CheckConnection();

                if (present)
                {
                    _lastConnectionError = "";
                    ProgressValue = 20;
                    Log += "Плату знайдено\n";
                    _cycleTimer.MarkBoardStarted();

                    if (_startRunTimerOnBoardDetected)
                    {
                        StartRunTimer();
                        _startRunTimerOnBoardDetected = false;

                        if (_workMode == WorkMode.Production)
                            _cycleTimer.MarkProductionBoxTimerStarted();
                    }

                    if (_measureBoardCycleTime && !_cycleTimer.IsRunning)
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

                    Log += "ERROR: Unknown work mode\n";
                    await StopAll();
                    return;
                }

                LogConnectionErrorIfChanged();
                await CycleBoardPowerWhileWaiting();
            }
        }

        /// <summary>
        /// Готує виробничий сценарій після виявлення плати або після пропуску очікування.
        /// </summary>
        private async Task PrepareProductionWithDetectedMcu()
        {
            try
            {
                _cycleState = CycleState.Flashing;
                _currentDeviceType = string.Empty;

                StartProductionBoxTimerIfNeeded();
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

        /// <summary>
        /// Логує помилку ST-LINK лише коли вона змінилась, щоб не дублювати шум.
        /// </summary>
        private void LogConnectionErrorIfChanged()
        {
            string error = _firmwareService.LastConnectionError;

            if (string.IsNullOrWhiteSpace(error) ||
                error == _lastConnectionError)
                return;

            _lastConnectionError = error;
            Log += "ST-LINK/API: " + error + "\n";
        }

        private async Task FlashOnlyTestFirmware()
        {
            await _testFirmwareWorkflow.FlashOnlyTestFirmware();
        }

        /// <summary>
        /// Готує rework-цикл: визначає MCU, прошиває тестову прошивку і запускає тест.
        /// </summary>
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

            await _stand.PowerCycleToStlinkMode();

            return await _testFirmwareService.FlashAutoTestFirmwareWithUnlockSession();
        }

        private async Task<string> FlashAutoTestFirmwareWithRetry(string deviceType)
        {
            string result = await _testFirmwareService.FlashTestFirmwareForDevice(deviceType);

            if (!TestFirmwareFailed(result))
                return result;

            Log += "\nWARNING: MCU не визначився після RDP unlock. Робимо power cycle і пробуємо ще раз...\n";

            if (_workMode == WorkMode.Production)
                await _stand.PowerOnToStlinkMode();
            else
                await _stand.PowerCycleToStlinkMode();

            return await _testFirmwareService.FlashTestFirmwareForDevice(deviceType);
        }

        /// <summary>
        /// Зчитує тип MCU з повторною спробою після power-cycle у ST-LINK режимі.
        /// </summary>
        private async Task<string> GetDeviceTypeWithRetry(string context)
        {
            string deviceType = await _firmwareService.GetDeviceType();

            if (IsKnownDeviceType(deviceType))
                return deviceType;

            Log += $"WARNING: MCU не визначився ({context}): {deviceType}\n";
            Log += "=== Повторно подаємо живлення в ST-LINK режимі ===\n";

            await _stand.SendCommand("POWER_OFF", BoardInsertPowerOffDelayMs);
            await _stand.SendCommand("POWER_ON", StandController.PowerOnSettleDelayMs, enforceMinimumDelay: false);

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
            await _stand.PowerCycleToStlinkMode();

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
            await _stand.PowerCycleToStlinkMode();

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
        /// <summary>
        /// Перевіряє відповідність виробничої прошивки типу MCU перед записом.
        /// </summary>
        private static string ValidateProductionFlashSessionDevice(string deviceType, string firmwarePath)
        {
            if (!IsKnownDeviceType(deviceType))
                return $"ERROR: MCU not detected: {deviceType}\n";

            if (!System.IO.File.Exists(firmwarePath))
                return $"Firmware not found: {firmwarePath}\n";

            if (deviceType == "C0" && !IsC0FirmwareNameValid(firmwarePath))
                return $"ERROR: C0 firmware filename must contain c0: {firmwarePath}\n";

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

        /// <summary>
        /// Обробляє UART-дані тесту, визначає результат діагностики і переводить цикл далі.
        /// </summary>
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
                            if (!_singleProductionFlash &&
                                !CaptureProductionSerialAfterSuccessfulTest())
                            {
                                return;
                            }

                            IsSerialInputEnabled = true;

                            await StopSuccessfulTestAndFlash();
                            return;
                        }

                        if (_workMode == WorkMode.Reflash)
                        {
                            _cycleState = CycleState.WaitingSerial;
                            IsSerialInputEnabled = true;
                            Log += ">>> Введіть серійний номер D + 6 або 7 цифр для початку прошивки...\n";

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

                    await StopTestAndWaitRemove();

                    return;
                }
            });
        }

        /// <summary>
        /// Після успішного тесту фіксує виробничий серійник або переводить цикл в очікування серійника.
        /// </summary>
        private bool CaptureProductionSerialAfterSuccessfulTest()
        {
            if (_retryingProductionBoard &&
                !string.IsNullOrWhiteSpace(_currentBoardSerial))
            {
                Log += $">>> Повторна спроба для серійника {_currentBoardSerial}\n";
                return true;
            }

            if (IsSerialReadyForCurrentMode(ScannedSerial))
            {
                _currentBoardSerial = ScannedSerial.Trim();
                ScannedSerial = "";
                return true;
            }

            _cycleState = CycleState.WaitingSerial;
            Status = "Waiting serial";
            ProgressValue = 75;
            IsSerialInputEnabled = true;
            Log += ">>> Тест пройдено. Введіть серійний номер D + 7 цифр для початку прошивки...\n";
            return false;
        }

        /// <summary>
        /// Планує запуск наступного кроку після введення валідного серійного номера.
        /// </summary>
        private void ScheduleNextStepAfterSerialEntered()
        {
            _serialEntryCts?.Cancel();
            _serialEntryCts = new CancellationTokenSource();

            _ = StartNextStepAfterSerialEntered(_serialEntryCts.Token);
        }

        /// <summary>
        /// Запускає прошивку або тест після серійника, захищаючись від повторного старту.
        /// </summary>
        private async Task StartNextStepAfterSerialEntered(CancellationToken cancellationToken)
        {
            try
            {
                // A reflash serial may contain either 6 or 7 digits after D.
                // Wait until the scanner finishes sending characters before accepting
                // the shorter valid form.
                await Task.Delay(SerialEntryDebounceDelayMs, cancellationToken);

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
                    _currentBoardSerial = ScannedSerial.Trim();
                    ScannedSerial = "";
                    IsSerialInputEnabled = true;
                    await StopSuccessfulTestAndFlash();
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

        /// <summary>
        /// Перевіряє серійний номер за правилами активного режиму роботи.
        /// </summary>
        private bool IsSerialReadyForCurrentMode(string serial)
        {
            return _workMode == WorkMode.Production
                ? IsProductionSerialValid(serial)
                : IsReflashSerialValid(serial);
        }

        private static bool IsReflashSerialValid(string serial)
        {
            string trimmedSerial = serial.Trim();

            if ((trimmedSerial.Length != 7 && trimmedSerial.Length != 8) ||
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


        private async Task CycleBoardPowerWhileWaiting()
        {
            await _stand.SendCommand("POWER_OFF", BoardInsertPowerOffDelayMs);

            if (_cycleState != CycleState.WaitingBoard)
                return;

            await _stand.SendCommand("POWER_ON", BoardInsertPowerOnDelayMs, enforceMinimumDelay: false);
        }

        /// <summary>
        /// Перевіряє ST-LINK і робить одну спробу відновлення живленням.
        /// </summary>
        private async Task<bool> EnsureStlinkReady(string context)
        {
            if (await _firmwareService.CheckConnection())
                return true;

            Log += $"WARNING: ST-LINK немає зв'язку ({context}). Повторно подаємо живлення...\n";

            await _stand.PowerCycleToStlinkMode();

            if (await _firmwareService.CheckConnection())
            {
                Log += "=== ST-LINK зв'язок відновлено ===\n";
                return true;
            }

            Status = "No ST-LINK";
            Log += "ERROR: ST-LINK не бачить плату після повторного подання живлення\n";
            Log += ">>> Заберіть плату зі стенда...\n";

            return false;
        }

        /// <summary>
        /// Запускає UART-тест у режимі, який відповідає поточному сценарію.
        /// </summary>
        private async Task StartTest()
        {
            _cycleState = CycleState.Testing;
            IsSerialInputEnabled = string.IsNullOrEmpty(ScannedSerial);

            _diagnosticsParser.Reset();

            if (_workMode == WorkMode.Production)
            {
                await _stand.SetUartModeKeepingPowerOn();
            }
            else if (_workMode == WorkMode.Reflash)
            {
                await _stand.SetUartModeAndEnsurePowerOn(
                    powerOffDelayMs: 400,
                    modeSwitchDelayMs: 100,
                    powerOnDelayMs: 0);
            }
            else
            {
                await _stand.SetUartModeAndEnsurePowerOn();
            }

            Status = "Testing";
            ProgressValue = 60;
            Log += "Тест запущено\n";
        }

        private async Task StopTestAndWaitRemove()
        {
            _cycleState = CycleState.WaitingRemove;

            await _stand.SendCommand("POWER_OFF", StandController.UartPowerOffDelayMs);
            await _stand.SetStlinkMode();
            await Task.Delay(100);

            await WaitBoardRemovedAfterError();
        }

        /// <summary>
        /// Зупиняє успішний тест і передає плату в сценарій прошивки активного режиму.
        /// </summary>
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

        /// <summary>
        /// Після тесту готує стенд до виробничої прошивки через ST-LINK.
        /// </summary>
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

        /// <summary>
        /// Завершує режим чистого тестування без прошивки і реєстрації плати.
        /// </summary>
        private async Task CompleteSuccessfulTestOnly()
        {
            await _stand.SendCommand("BUZZ_OK", BuzzerOkDelayMs);
            _testOnlyMode = false;

            Status = "Test OK";
            ProgressValue = 100;
            Log += "Тестування завершено\n";
            StopRunTimer();
            IsBusy = false;
            _cycleState = CycleState.Idle;
        }

        /// <summary>
        /// Виконує виробничу прошивку з перевіркою MCU та recovery-спробою.
        /// </summary>
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

                _cycleState = CycleState.WaitingRemove;

                await WaitBoardRemovedAfterError();
            }
        }

        /// <summary>
        /// Виконує rework-прошивку відповідним файлом для визначеного MCU.
        /// </summary>
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

        private async Task StartFlashing(string firmwareToFlash)
        {
            try
            {
                Status = "Preparing flash";
                ProgressValue = 80;

                Status = "Flashing";
                ProgressValue = 90;
                Log += "Прошивка...\n";
                ClearSerialInputAfterFlashStart();

                if ((_workMode == WorkMode.Production || _workMode == WorkMode.Reflash) &&
                    _currentDeviceType == "C0" &&
                    !IsC0FirmwareNameValid(firmwareToFlash))
                {
                    Log += $"ERROR: C0 firmware filename must contain c0: {firmwareToFlash}\n";
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

        /// <summary>
        /// Фіксує успішну прошивку, реєструє плату або коробку і запускає наступний крок циклу.
        /// </summary>
        private async Task CompleteSuccessfulFlash(string firmwareToFlash)
        {
            await _stand.SendCommand("BUZZ_OK", BuzzerOkDelayMs);

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

            _retryingProductionBoard = false;

            Log += "Прошивку завершено\n";

            Log += ">>> Заберіть плату зі стенда...\n";

            if (_workMode == WorkMode.Production)
            {
                await PowerOffAndRestartProductionCycle();
                return;
            }

            _cycleState = CycleState.WaitingRemove;

            await WaitBoardRemoved();
        }
        private static bool IsC0FirmwareNameValid(string firmwarePath)
        {
            string fileName = System.IO.Path.GetFileName(firmwarePath);

            return fileName.Contains("c0", StringComparison.OrdinalIgnoreCase);
        }

        private void ClearSerialInputAfterFlashStart()
        {
            if (_workMode != WorkMode.Reflash)
                return;

            if (string.IsNullOrEmpty(ScannedSerial))
                return;

            ScannedSerial = "";
        }

        /// <summary>
        /// У виробничому режимі вимикає живлення і відразу запускає наступний цикл.
        /// </summary>
        private async Task PowerOffAndRestartProductionCycle()
        {
            Status = "Power off";

            await _stand.SendCommand("POWER_OFF", PowerOffBeforeRestartDelayMs);

            if (_completedBox != null)
            {
                await ShowBoxCompletedWindow(_completedBox);
                _completedBox = null;

                if (_cycleState == CycleState.Idle)
                    return;
            }

            LogCurrentBoardCycleTime();

            await PowerOffAndRestartCycle();
        }

        /// <summary>
        /// Після помилки у виробничому режимі відразу запускає наступний цикл.
        /// В інших режимах дає оператору паузу і очікує зняття плати.
        /// </summary>
        private async Task WaitBoardRemovedAfterError()
        {
            if (_workMode == WorkMode.Production)
            {
                _retryingProductionBoard = !string.IsNullOrWhiteSpace(_currentBoardSerial);
                Log += "=== Після підтвердження помилки запускаємо наступний цикл ===\n";
                await PowerOffAndRestartProductionCycle();
                return;
            }

            await Task.Delay(CycleErrorDelayMs);
            await WaitBoardRemoved();
        }

        /// <summary>
        /// Очікує зняття плати зі стенда перед наступним циклом.
        /// </summary>
        private async Task WaitBoardRemoved()
        {
            if (_cycleState != CycleState.WaitingRemove)
                return;

            Status = "Power off";

            await _stand.SendCommand("POWER_OFF", PowerOffBeforeRestartDelayMs);

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
            TimeSpan? elapsed = _cycleTimer.FinishBoardCycle();
            if (elapsed == null)
                return;

            Log += $"Час циклу плати: {elapsed:hh\\:mm\\:ss}\n";
        }

        private async Task WaitUntilBoardRemoved()
        {
            Status = "Remove board";

            while (_cycleState == CycleState.WaitingRemove)
            {
                await _stand.SendCommand("POWER_ON", BoardInsertPowerOnDelayMs, enforceMinimumDelay: false);

                bool boardPresent = await _firmwareService.CheckBoardPresence();

                await _stand.SendCommand("POWER_OFF", BoardInsertPowerOffDelayMs);

                if (!boardPresent)
                    return;
            }
        }

        /// <summary>
        /// Вимикає живлення, скидає серійник і стартує наступну ітерацію циклу.
        /// </summary>
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

        /// <summary>
        /// Зупиняє стенд, вимикає живлення і повертає UI в безпечний idle-стан.
        /// </summary>
        private async Task StopAll()
        {
            try
            {
                _cycleState = CycleState.Idle;

                await _stand.SendCommand("PWM_STOP", StandController.CommandSettleDelayMs);
                await _stand.SendCommand("POWER_OFF", 100);

                StopRunTimer();
                _retryingProductionBoard = false;
                ResetCycleData();
                _testOnlyMode = false;
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
        /// <summary>
        /// Додає прошиту плату до поточної коробки і повертає звіт, якщо коробка заповнилась.
        /// </summary>
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

            NotifyProductionBoxChanged();
            LoadLocalDataForCurrentTab();
            OnPropertyChanged(nameof(FlashedToday));

            return report;
        }

        private void CompleteCurrentProductionBoxByOperator()
        {
            MessageBoxResult result = MessageBox.Show(
                "Завершити поточну коробку і почати нову?",
                "Нова коробка",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            CompleteCurrentProductionBox(showDialog: true);
        }

        private void CompleteCurrentProductionBox(bool showDialog)
        {
            BoxReport? report = _boxTracking.CompleteCurrentBox(FirmwareName);

            if (report == null)
                return;

            SaveBoxReport(report);
            _boxTracking.NextBox();
            ResetProductionBoxTimer();
            NotifyProductionBoxChanged();
            LoadLocalDataForCurrentTab();

            if (showDialog)
            {
                Log += $"=== Коробку №{report.BoxNumber} завершено вручну: {report.Quantity} шт. ===\n";
                MessageBox.Show(
                    $"Коробку №{report.BoxNumber} завершено: {report.Quantity} шт.",
                    "Нова коробка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void NotifyProductionBoxChanged()
        {
            NewProductionBoxCommand.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// Завантажує стартову статистику та локальні дані з урахуванням доступності API.
        /// </summary>
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
                _globalReworkSummaries = _apiService.LoadReworkSummaries(DateTime.Today);
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
            _globalReworkSummaries = null;
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

        /// <summary>
        /// Оновлює таблицю локальних даних для активної вкладки.
        /// </summary>
        private void LoadLocalDataForCurrentTab()
        {
            LocalDataRows.Clear();

            try
            {
                LocalDataTableSnapshot table = _footerMode switch
                {
                    WorkMode.TestFirmware => _localDataTables.BuildTestFirmware(),
                    WorkMode.Reflash => _localDataTables.BuildReflash(
                        DateTime.Today,
                        _globalReworkSummaries),
                    _ => _localDataTables.BuildProduction(
                        _productionBoxActive,
                        BoxQuantity,
                        FirmwareName)
                };

                ApplyLocalDataTable(table);
            }
            catch (Exception ex)
            {
                Log += "LOCAL DB ERROR: " + ex.Message + "\n";
            }
        }

        private void ApplyLocalDataTable(LocalDataTableSnapshot table)
        {
            LocalDataHeader1 = table.Header1;
            LocalDataHeader2 = table.Header2;
            LocalDataHeader3 = table.Header3;
            LocalDataHeader4 = table.Header4;
            LocalDataHeader5 = table.Header5;
            LocalDataEmptyText = table.EmptyText;

            foreach (LocalDataRow row in table.Rows)
                LocalDataRows.Add(row);
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


        /// <summary>
        /// Реєструє плату через API або локально, якщо API недоступний.
        /// </summary>
        private void RegisterDeviceInDatabase(string firmwarePath)
        {
            if (_workMode == WorkMode.TestFirmware)
            {
                RegisterTestDeviceInDatabase();
                return;
            }

            if (_workMode == WorkMode.Production)
            {
                RegisterDeviceLocally(firmwarePath);
                ScheduleBackgroundSync();
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
                    GetCurrentMcuId(),
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
                    GetCurrentMcuId(),
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

        /// <summary>
        /// Зберігає реєстрацію плати локально для подальшої синхронізації.
        /// </summary>
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

        /// <summary>
        /// Зберігає звіт коробки через API або локально для синхронізації.
        /// </summary>
        private void SaveBoxReport(BoxReport report)
        {
            SaveBoxReportLocallyAndScheduleSync(report);
        }

        private void SaveBoxReportLocallyAndScheduleSync(BoxReport report)
        {
            try
            {
                _localDatabaseService.AddPendingBoxReport(_currentUser.Id, report);
                LoadLocalDataForCurrentTab();

                if (!_currentUser.IsOffline)
                    ScheduleBackgroundSync();
            }
            catch (Exception ex)
            {
                Log += "LOCAL DB ERROR: " + ex.Message + "\n";
                Log += "=== TEST MODE: box report was not saved locally ===\n";
            }
        }

        /// <summary>
        /// Показує вікно завершеної коробки і обробляє вибір оператора.
        /// </summary>
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

        /// <summary>
        /// Завантажує збережені налаштування у властивості ViewModel.
        /// </summary>
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
            ApiBaseUrl = settings.ApiBaseUrl;
        }

        /// <summary>
        /// Зберігає налаштування після завершення початкового завантаження.
        /// </summary>
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
                ApiBaseUrl = ApiBaseUrl
            });
        }

        private void ResetUniqueFlashedCounter()
        {
            if (_workMode == WorkMode.Reflash)
                _reflashUniqueBoardCounter.Reset();
            else if (_workMode == WorkMode.Production)
                _productionUniqueBoardCounter.Reset();

            OnPropertyChanged(nameof(UniqueFlashedSinceStart));
        }

        /// <summary>
        /// Оновлює лічильник унікально прошитих плат для активного режиму.
        /// </summary>
        private void RegisterUniqueFlashedBoard()
        {
            UniqueBoardCounter counter = _workMode == WorkMode.Reflash
                ? _reflashUniqueBoardCounter
                : _productionUniqueBoardCounter;

            if (counter.Register(GetCurrentBoardSerial(), GetCurrentMcuId()))
                OnPropertyChanged(nameof(UniqueFlashedSinceStart));
        }

    }
}
