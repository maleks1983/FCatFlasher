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
        private const int ReflashUartModeSwitchDelayMs = 400;
        private const int ReflashBoardDetectionPowerOffDelayMs = 50;
        private const int ReflashBoardDetectionStlinkDelayMs = 150;
        private const string TestRegistrationType = "TEST";
        private const string ProductionRegistrationType = "PRODUCTION";
        private const string ReworkRegistrationType = "REWORK";

        private readonly FirmwareService _firmwareService;
        private readonly TestFirmwareService _testFirmwareService;
        private readonly SerialService _serial;
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
        private readonly Action<string, string> _showFirmwareErrorDialog;
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
        private PendingBoxReplacement? _pendingBoxReplacement;
        private readonly object _backgroundSyncLock = new();
        private bool _backgroundSyncRunning;
        private bool _backgroundSyncRequested;
        private bool _loadingData;
        private bool _isClosed;
        private bool _initialized;
        private CancellationTokenSource? _serialEntryCts;
        private CancellationTokenSource? _runCts;
        private Task _runTask = Task.CompletedTask;
        private Task _stopTask = Task.CompletedTask;
        private TaskCompletionSource<bool>? _diagnosticsCompletion;
        private TaskCompletionSource<string>? _serialCompletion;
        private bool _restartCycle;
        private CancellationToken RunToken => _runCts?.Token ?? CancellationToken.None;

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
        public RelayCommand TestReplacementCommand { get; }
        public RelayCommand FlashCommand { get; }
        public RelayCommand SingleFlashCommand { get; }
        public RelayCommand ReflashCommand { get; }
        public RelayCommand ReflashTestFirmwareCommand { get; }

        public ICommand DeleteLocalDataRowCommand { get; }
        public ICommand AddBoxBoardCommand { get; }
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

        public event Action<bool>? SerialInputFocusRequested;

        public string ScannedSerial
        {
            get => _scannedSerial;
            set
            {
                _scannedSerial = value;
                OnPropertyChanged();

                if (string.IsNullOrEmpty(_scannedSerial) && !IsSerialInputEnabled)
                    IsSerialInputEnabled = true;

                if (_cycleState == CycleState.WaitingSerial)
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
                OnPropertyChanged(nameof(G0FirmwareFileName));
                SaveSettings();
            }
        }

        public string G0FirmwareFileName => System.IO.Path.GetFileName(G0FirmwarePath);

        private string _c0FirmwarePath = "";
        public string C0FirmwarePath
        {
            get => _c0FirmwarePath;
            set
            {
                _c0FirmwarePath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(C0FirmwareFileName));
                SaveSettings();
            }
        }

        public string C0FirmwareFileName => System.IO.Path.GetFileName(C0FirmwarePath);

        private string _reworkOrderId = "";
        private string _reworkProductName = "";
        private int _reworkQuantity;
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

        private bool _useNoAccelC0TestFirmware;
        public bool UseNoAccelC0TestFirmware
        {
            get => _useNoAccelC0TestFirmware;
            set
            {
                _useNoAccelC0TestFirmware = value;
                OnPropertyChanged();
                _testFirmwareService.UseNoAccelC0TestFirmware = value;
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
                TestReplacementCommand.RaiseCanExecuteChanged();
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
                const int maxLogLength = 131072;
                _log = value.Length <= maxLogLength ? value : value[^maxLogLength..];
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
            CurrentUser currentUser,
            FirmwareService? firmwareService = null,
            SerialService? serialService = null,
            Action<string, string>? showFirmwareErrorDialog = null)
        {
            _showFirmwareErrorDialog = showFirmwareErrorDialog ?? ShowTestErrorDialog;
            _firmwareService = firmwareService ?? new FirmwareService();
            _serial = serialService ?? new SerialService();
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
                    StartRunTimer();
                    await StartFullCycle();
                },
                () => !IsBusy
            );

            TestReplacementCommand = new RelayCommand(
                async () => await StartBoxReplacement(),
                () => !IsBusy);

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
                    if (!await RequestOrderId())
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
            AddBoxBoardCommand = new RelayCommand<LocalDataRow>(row => EditCompletedBox(row, replace: false));
            RefreshComPortsCommand = new RelayCommand(RefreshComPorts);
            BrowseFirmwareCommand = new RelayCommand(BrowseFirmware);
            BrowseG0FirmwareCommand = new RelayCommand(BrowseG0Firmware);
            BrowseC0FirmwareCommand = new RelayCommand(BrowseC0Firmware);
            BrowseTestFirmwareDirectoryCommand = new RelayCommand(BrowseTestFirmwareDirectory);
            OpenSettingsCommand = new RelayCommand(OpenSettings);
            SyncPendingCommand = new RelayCommand(async () => await SyncPendingData(), () => !IsBusy);
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
            LoadOfflineData("");
            var savedBox = _localDatabaseService.LoadProductionBox(_currentUser.Id);
            if (savedBox != null)
            {
                _boxTracking.Restore(savedBox);
                if (_boxTracking.HasCurrentBox)
                {
                    FirmwareName = savedBox.FirmwareName;
                    BoxQuantity = savedBox.BoxQuantity;
                    _productionBoxActive = true;
                    Log += $"Відновлено коробку №{savedBox.BoxNumber}: {savedBox.SerialNumbers.Count} шт.\n";
                }
            }
            LoadLocalDataForCurrentTab();
            _settingsLoaded = true;
        }

        public async Task InitializeAsync()
        {
            if (_currentUser.IsOffline || _isClosed || _initialized)
                return;
            _initialized = true;
            _loadingData = true;
            IsBusy = true;
            try
            {
                await RefreshApiDataAsync();
                if (!_isClosed)
                {
                    LoadLocalDataForCurrentTab();
                    ScheduleBackgroundSync();
                }
            }
            catch (Exception ex)
            {
                if (!_isClosed)
                    LoadOfflineData("API ERROR: " + ex.Message);
            }
            finally
            {
                _loadingData = false;
                IsBusy = false;
            }
        }

        public string ReworkProductName
        {
            get => _reworkProductName;
            private set
            {
                _reworkProductName = value;
                OnPropertyChanged();
            }
        }

        public int ReworkQuantity
        {
            get => _reworkQuantity;
            private set
            {
                _reworkQuantity = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ReworkQuantityText));
            }
        }

        public string ReworkQuantityText => ReworkQuantity > 0
            ? ReworkQuantity.ToString(CultureInfo.InvariantCulture)
            : string.Empty;

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
        private async Task SyncPendingData()
        {
            if (_loadingData || IsBusy)
                return;
            if (_currentUser.IsOffline)
            {
                Log += "SYNC ERROR: користувач увійшов offline. Увійдіть через API і повторіть синхронізацію.\n";
                return;
            }

            _loadingData = true;
            IsBusy = true;
            try
            {
                await Task.Run(() => _localDatabaseService.SyncPending(_apiService));
                await RefreshApiDataAsync();
                LoadLocalDataForCurrentTab();
                Log += "=== SYNC: локальні дані синхронізовано з основною базою ===\n";
            }
            catch (Exception ex)
            {
                LoadLocalDataForCurrentTab();
                Log += "SYNC ERROR: " + ex.Message + "\n";
            }
            finally
            {
                _loadingData = false;
                IsBusy = false;
            }
        }

        /// <summary>
        /// Відкриває звіт для виробничих або rework-операцій.
        /// </summary>
        private void ScheduleBackgroundSync()
        {
            if (_currentUser.IsOffline || _isClosed)
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
        private async Task<bool> RequestOrderId()
        {
            if (IsBusy || _isClosed)
                return false;
            _loadingData = true;
            IsBusy = true;
            try
            {
                return await RequestOrderIdCore();
            }
            finally
            {
                _loadingData = false;
                IsBusy = false;
            }
        }

        private async Task<bool> RequestOrderIdCore()
        {
            IReadOnlyList<ReworkOrderInfo> orders;
            try
            {
                orders = await Task.Run(_apiService.LoadActiveReworkTasks);
                if (_isClosed)
                    return false;
            }
            catch (Exception ex)
            {
                if (_isClosed)
                    return false;
                Log += "ORDER LIST API ERROR: " + ex.Message + "\n";
                MessageBox.Show("Не вдалося завантажити отримані задачі на перепрошивку.",
                    "Перепрошивка", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            if (orders.Count == 0)
            {
                MessageBox.Show("У вас немає активних отриманих задач на перепрошивку.",
                    "Перепрошивка", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            var window = new OrderIdWindow(orders, ReworkOrderId, ReworkFirmwareName)
            {
                Owner = Application.Current.MainWindow
            };

            if (window.ShowDialog() != true)
                return false;

            ReworkOrderId = window.OrderId;
            ReworkProductName = window.ProductName;
            ReworkQuantity = window.Quantity;
            ReworkFirmwareName = window.FirmwareName;
            try
            {
                long? c0Id = window.C0FirmwareId;
                long? g0Id = window.G0FirmwareId;
                string c0Name = window.C0FirmwareName;
                string g0Name = window.G0FirmwareName;
                var paths = await Task.Run(() => (
                    C0: c0Id.HasValue ? _apiService.DownloadFirmware(c0Id.Value, c0Name) : string.Empty,
                    G0: g0Id.HasValue ? _apiService.DownloadFirmware(g0Id.Value, g0Name) : string.Empty));
                if (_isClosed)
                    return false;
                C0FirmwarePath = paths.C0;
                G0FirmwarePath = paths.G0;
            }
            catch (Exception ex)
            {
                if (_isClosed)
                    return false;
                Log += "FIRMWARE DOWNLOAD ERROR: " + ex.Message + "\n";
                MessageBox.Show("Не вдалося завантажити файл прошивки з бази.",
                    "Перепрошивка", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            return true;
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

            if (window.StartMode == global::FCatFlasher.ProductionBoxStartMode.StartNew &&
                !CompleteCurrentProductionBox(showDialog: false))
                return false;

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
            string serial = !string.IsNullOrWhiteSpace(_currentBoardSerial)
                ? _currentBoardSerial
                : _pendingBoxReplacement?.SerialNumber ?? ScannedSerial.Trim();

            return _workMode == WorkMode.Reflash
                ? ResolveReflashSerial(serial, GetCurrentMcuId())
                : serial;
        }

        internal static string ResolveReflashSerial(string serial, string mcuId)
        {
            const string missingSerialPlaceholder = "D111111";
            string normalizedSerial = serial.Trim();

            if (!normalizedSerial.Equals(missingSerialPlaceholder, StringComparison.OrdinalIgnoreCase))
                return normalizedSerial;

            string mcuDigits = new(mcuId.Where(char.IsDigit).ToArray());
            if (mcuDigits.Length < 7)
                return missingSerialPlaceholder;

            return "D" + mcuDigits[^7..];
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
            _serialEntryCts?.Dispose();
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
            if (IsBusy || _isClosed)
                return;

            _runCts = new CancellationTokenSource();
            _stand.RunCancellationToken = _runCts.Token;
            _firmwareService.RunCancellationToken = _runCts.Token;
            IsBusy = true;
            _runTask = RunCyclesAsync(_runCts.Token);
            await _runTask;
        }

        private async Task RunCyclesAsync(CancellationToken cancellationToken)
        {
            try
            {
                do
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _restartCycle = false;
                    Log = "";
                    ProgressValue = 0;
                    _cycleState = CycleState.WaitingBoard;
                    ResetCycleData();

                    if (_workMode == WorkMode.Production || _workMode == WorkMode.Reflash)
                        SerialInputFocusRequested?.Invoke(false);

                    if (_testOnlyMode)
                        await StartTest();
                    else
                        await StartModeCycle();
                    cancellationToken.ThrowIfCancellationRequested();
                }
                while (_restartCycle);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Status = "Stopped";
                Log += "=== ЗУПИНЕНО ===\n";
            }
            catch (RegistrationException ex)
            {
                Status = "Registration error";
                Log += "ERROR: " + ex.Message + "\n" + ex.InnerException?.Message + "\n";
            }
            catch (Exception ex)
            {
                Status = "Error";
                Log += "ERROR: " + ex.Message + "\n";
            }
            finally
            {
                // Keep start commands disabled until the native operation and shutdown finish.
                _cycleState = CycleState.Idle;
                _serialEntryCts?.Cancel();
                _diagnosticsCompletion = null;
                _serialCompletion = null;
                try
                {
                    await _stand.StopAsync();
                }
                catch (Exception ex)
                {
                    Log += "STOP ERROR: " + ex.Message + "\n";
                }
                StopRunTimer();
                _testOnlyMode = false;
                _stlinkSelectedForRun = false;
                _retryingProductionBoard = false;
                _runCts?.Dispose();
                _runCts = null;
                IsSerialInputEnabled = true;
                IsBusy = false;
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
                if (_workMode == WorkMode.Reflash)
                    await PrepareReflashBoardDetectionAttempt();
                else
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
                RunToken.ThrowIfCancellationRequested();

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
                    RequestStop();
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
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not RegistrationException)
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

                if (_skipReflashDiagnosticsReport)
                {
                    Log += "=== REPORT ПРОПУЩЕНО ===\n";
                    await StartReflashFlashing();
                    return;
                }

                await StartTest();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not RegistrationException)
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
            => FirmwareCompatibility.Validate(deviceType, firmwarePath);
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
        private void OnDataReceived(string data)
        {
            // Queued data must not complete a later board's test.
            var completion = _diagnosticsCompletion;
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    Log += data + "\n";
                    if (_cycleState != CycleState.Testing || completion == null ||
                        completion != _diagnosticsCompletion || completion.Task.IsCompleted)
                        return;

                    foreach (string line in data.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        bool? passed = _diagnosticsParser.ProcessLine(line.Trim());
                        if (passed.HasValue)
                        {
                            completion.TrySetResult(passed.Value);
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    completion?.TrySetException(ex);
                    Log += "UART ERROR: " + ex.Message + "\n";
                }
            });
        }

        private async Task HandleDiagnosticsResult(bool passed)
        {
            RunToken.ThrowIfCancellationRequested();
            if (passed)
            {
                Status = "Test OK";
                ProgressValue = 70;
                Log += "=== ТЕСТ ПРОЙДЕНО ===\n";
                if (_testOnlyMode)
                {
                    await CompleteSuccessfulTestOnly();
                    return;
                }

                if (!_singleProductionFlash)
                    await WaitForSerialAsync();
                await StopSuccessfulTestAndFlash();
                return;
            }

            Status = "Test Error";
            ProgressValue = 100;
            Log += "=== ТЕСТ НЕ ПРОЙДЕНО ===\n";
            Log += $"SIG: {_diagnosticsParser.LastSig}\n";
            Log += ">>> Заберіть плату зі стенда...\n";
            ShowTestErrorDialog("Помилка тесту",
                $"SIG: {_diagnosticsParser.LastSig}\n\n{_diagnosticsParser.LastReport}");
            RunToken.ThrowIfCancellationRequested();
            if (_testOnlyMode)
            {
                _cycleState = CycleState.Idle;
                return;
            }
            await StopTestAndWaitRemove();
        }

        private async Task WaitForSerialAsync()
        {
            _serialCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                if (_workMode == WorkMode.Production && CaptureProductionSerialAfterSuccessfulTest())
                    return;

                _cycleState = CycleState.WaitingSerial;
                IsSerialInputEnabled = true;
                if (_workMode == WorkMode.Reflash)
                    Log += ">>> Введіть серійний номер D + 6 або 7 цифр для початку прошивки...\n";
                if (IsSerialReadyForCurrentMode(ScannedSerial))
                    ScheduleNextStepAfterSerialEntered();

                _currentBoardSerial = await _serialCompletion.Task.WaitAsync(RunToken);
                if (_workMode == WorkMode.Production)
                {
                    ScannedSerial = "";
                    IsSerialInputEnabled = true;
                }
            }
            finally
            {
                _serialCompletion = null;
            }
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
            SerialInputFocusRequested?.Invoke(true);
            Log += ">>> Тест пройдено. Введіть серійний номер D + 7 цифр для початку прошивки...\n";
            return false;
        }

        /// <summary>
        /// Планує запуск наступного кроку після введення валідного серійного номера.
        /// </summary>
        private void ScheduleNextStepAfterSerialEntered()
        {
            _serialEntryCts?.Cancel();
            _serialEntryCts?.Dispose();
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
                {
                    SerialInputFocusRequested?.Invoke(true);
                    return;
                }

                _startingFlashAfterSerial = true;
                IsSerialInputEnabled = false;
                _serialCompletion?.TrySetResult(ScannedSerial.Trim());
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _serialCompletion?.TrySetException(ex);
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
            if (_workMode == WorkMode.Reflash)
            {
                await PrepareReflashBoardDetectionAttempt();
                return;
            }

            await _stand.SendCommand("POWER_OFF", BoardInsertPowerOffDelayMs);

            if (_cycleState != CycleState.WaitingBoard)
                return;

            await _stand.SendCommand("POWER_ON", BoardInsertPowerOnDelayMs, enforceMinimumDelay: false);
        }

        private async Task PrepareReflashBoardDetectionAttempt()
        {
            await _stand.SendCommand(
                "POWER_OFF",
                ReflashBoardDetectionPowerOffDelayMs,
                enforceMinimumDelay: false);

            if (_cycleState != CycleState.WaitingBoard)
                return;

            await _stand.SetStlinkMode(
                ReflashBoardDetectionStlinkDelayMs,
                enforceMinimumDelay: false);
            await _stand.SendCommand("POWER_ON", 0, enforceMinimumDelay: false);
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
            _diagnosticsCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

            if (_workMode == WorkMode.Production)
            {
                await _stand.SetUartModeKeepingPowerOn();
            }
            else if (_workMode == WorkMode.Reflash)
            {
                await _stand.SendCommand("POWER_OFF", 0, enforceMinimumDelay: false);
                await _stand.SetUartMode(
                    ReflashUartModeSwitchDelayMs,
                    enforceMinimumDelay: false);
                await _stand.SendCommand("POWER_ON", 0, enforceMinimumDelay: false);
            }
            else
            {
                await _stand.SetUartModeAndEnsurePowerOn();
            }

            Status = "Testing";
            ProgressValue = 60;
            Log += "Тест запущено\n";
            bool passed = await _diagnosticsCompletion.Task.WaitAsync(RunToken);
            _diagnosticsCompletion = null;
            await HandleDiagnosticsResult(passed);
        }

        private async Task StopTestAndWaitRemove()
        {
            _cycleState = CycleState.WaitingRemove;

            await _stand.SendCommand("POWER_OFF", StandController.UartPowerOffDelayMs);
            await _stand.SetStlinkMode();
            await Task.Delay(100, RunToken);

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
            RequestStop();
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
                    if (IsKnownDeviceType(result.DeviceType) &&
                        FirmwareCompatibility.Validate(result.DeviceType, FirmwarePath).Length > 0)
                    {
                        await HandleFirmwareValidationFailure(result.Output);
                        return;
                    }
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
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not RegistrationException)
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
                RequestStop();
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

                string validationError = FirmwareCompatibility.Validate(_currentDeviceType, firmwareToFlash);
                if (!string.IsNullOrEmpty(validationError))
                {
                    await HandleFirmwareValidationFailure(validationError);
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
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not RegistrationException)
            {
                Status = "Flash Error";
                ProgressValue = 100;
                Log += "ERROR: " + ex.Message + "\n";

                _cycleState = CycleState.WaitingRemove;

                await WaitBoardRemovedAfterError();
            }
        }

        /// <summary>
        /// Показує помилку відповідності та продовжує цикл після підтвердження оператора.
        /// </summary>
        private async Task HandleFirmwareValidationFailure(string details)
        {
            Status = "Wrong firmware";
            ProgressValue = 100;
            _cycleState = CycleState.WaitingRemove;
            Log += details + "\n";
            _showFirmwareErrorDialog(
                "Помилка відповідності прошивки",
                details.Trim() + "\n\nПрошивку не записано. Натисніть ОК, щоб продовжити цикл.");
            RunToken.ThrowIfCancellationRequested();
            await WaitBoardRemovedAfterError();
        }

        /// <summary>
        /// Фіксує успішну прошивку, реєструє плату або коробку і запускає наступний крок циклу.
        /// </summary>
        private async Task CompleteSuccessfulFlash(string firmwareToFlash)
        {
            if (_skipFlashRegistration)
                Log += "=== ТЕСТОВА ПЕРЕПРОШИВКА: дані не збережено ===\n";
            else
                await RegisterDeviceInDatabase(firmwareToFlash);

            if (!_skipFlashRegistration)
                RegisterUniqueFlashedBoard();

            Status = "Flash OK";
            ProgressValue = 100;
            await _stand.SendCommand("BUZZ_OK", BuzzerOkDelayMs);

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
        /// Після помилки у виробничому або rework-режимі відразу запускає наступний цикл.
        /// У режимі тестової прошивки дає оператору паузу і очікує зняття плати.
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

            if (_workMode == WorkMode.Reflash)
            {
                Log += "=== Помилку підтверджено. Запускаємо наступний цикл без очікування зняття плати ===\n";
                Status = "Power off";
                await _stand.SendCommand("POWER_OFF", PowerOffBeforeRestartDelayMs);
                LogCurrentBoardCycleTime();
                await PowerOffAndRestartCycle();
                return;
            }

            await Task.Delay(CycleErrorDelayMs, RunToken);
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
            int consecutiveAbsentChecks = 0;

            while (_cycleState == CycleState.WaitingRemove)
            {
                await _stand.SendCommand("POWER_ON", BoardInsertPowerOnDelayMs, enforceMinimumDelay: false);

                bool boardPresent = await _firmwareService.CheckBoardPresence();

                await _stand.SendCommand("POWER_OFF", BoardInsertPowerOffDelayMs);

                // Confirm removal so a transient ST-LINK failure cannot restart flashing.
                consecutiveAbsentChecks = boardPresent ? 0 : consecutiveAbsentChecks + 1;
                if (consecutiveAbsentChecks >= 2)
                    return;
            }
        }

        /// <summary>
        /// Вимикає живлення, скидає серійник і стартує наступну ітерацію циклу.
        /// </summary>
        private Task PowerOffAndRestartCycle()
        {
            RunToken.ThrowIfCancellationRequested();
            _restartCycle = true;
            return Task.CompletedTask;
        }

        private void RequestStop()
        {
            _runCts?.Cancel();
            _serialEntryCts?.Cancel();
            _cycleState = CycleState.Idle;
        }

        public Task StopAll()
        {
            if (_loadingData)
                return Task.CompletedTask;
            if (!_stopTask.IsCompleted)
                return _stopTask;
            _stopTask = StopCoreAsync();
            return _stopTask;
        }

        private async Task StopCoreAsync()
        {
            RequestStop();
            if (!_runTask.IsCompleted)
            {
                Status = "Stopping";
                await _runTask;
                return;
            }

            try
            {
                IsBusy = true;
                if (_serial.IsOpen)
                    await _stand.StopAsync();
                Status = "Stopped";
            }
            catch (Exception ex)
            {
                Log += "STOP ERROR: " + ex.Message + "\n";
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task ShutdownAsync()
        {
            _isClosed = true;
            await StopAll();
            _serial.DataReceived -= OnDataReceived;
            _serial.Close();
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
        /// Завершує поточну коробку за підтвердженням оператора.
        /// </summary>

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

        private bool CompleteCurrentProductionBox(bool showDialog)
        {
            BoxReport? report = _boxTracking.HasCurrentBox
                ? _boxTracking.GetCurrentBoxReport(BoxQuantity, FirmwareName)
                : null;

            if (report == null)
                return true;

            var nextBox = _boxTracking.GetState(BoxQuantity, FirmwareName);
            nextBox.BoxNumber++;
            nextBox.SerialNumbers.Clear();
            try
            {
                _localDatabaseService.CompleteProductionBox(_currentUser.Id, report, nextBox);
            }
            catch (Exception ex)
            {
                Log += "LOCAL DB ERROR: " + ex.Message + "\n";
                MessageBox.Show("Не вдалося зберегти коробку. Спробуйте ще раз.", "Коробка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            _boxTracking.NextBox();
            if (!_currentUser.IsOffline)
                ScheduleBackgroundSync();
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
            return true;
        }

        private void NotifyProductionBoxChanged()
        {
            NewProductionBoxCommand.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// Завантажує стартову статистику та локальні дані з урахуванням доступності API.
        /// </summary>
        private async Task RefreshApiDataAsync()
        {
            var snapshot = await Task.Run(() => (
                Boxes: _apiService.LoadBoxReports(),
                Reworks: _apiService.LoadReworkSummaries(DateTime.Today),
                Tested: _apiService.CountDeviceRegistrationsFrom(DateTime.Today, TestRegistrationType),
                Flashed: _apiService.CountDeviceRegistrationsFrom(DateTime.Today, ProductionRegistrationType),
                Reflashed: _apiService.CountDeviceRegistrationsFrom(DateTime.Today, ReworkRegistrationType)));
            if (_isClosed)
                return;
            _boxTracking.LoadReports(snapshot.Boxes);
            _globalReworkSummaries = snapshot.Reworks;
            _testedToday = snapshot.Tested;
            _flashedToday = snapshot.Flashed;
            _reflashedToday = snapshot.Reflashed;
            OnPropertyChanged(nameof(FlashedToday));
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
                        DateTime.Today,
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

        private void EditCompletedBox(LocalDataRow? row, bool replace)
        {
            if (row == null || !row.CanEditBox || row.KeyBoxReportId <= 0)
                return;

            BoxReport? report = _localDatabaseService.LoadPendingBoxReports()
                .FirstOrDefault(item => item.LocalId == row.KeyBoxReportId && !item.IsSynced);
            if (report == null)
            {
                MessageBox.Show(
                    "Коробку вже синхронізовано або її локальні дані змінилися. Оновіть таблицю.",
                    "Редагування коробки", MessageBoxButton.OK, MessageBoxImage.Information);
                LoadLocalDataForCurrentTab();
                return;
            }

            var window = new global::FCatFlasher.EditBoxBoardWindow(report.BoxNumber, replace)
            {
                Owner = Application.Current.MainWindow
            };
            if (window.ShowDialog() != true)
                return;

            bool valid = replace
                ? BoxReportEditor.TryReplace(report, window.OldSerial, window.NewSerial, out BoxReport updated, out string error)
                : BoxReportEditor.TryAdd(report, window.NewSerial, out updated, out error);

            if (!valid)
            {
                MessageBox.Show(error, "Редагування коробки", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (!_localDatabaseService.UpdatePendingBoxReport(report.LocalId, updated))
                    throw new InvalidOperationException("Коробку вже синхронізовано або змінено.");

                _boxTracking.LoadReports(_localDatabaseService.LoadPendingBoxReports());
                LoadLocalDataForCurrentTab();
                string action = replace ? $"{window.OldSerial} замінено на {window.NewSerial}" : $"додано {window.NewSerial}";
                Log += $"=== КОРОБКА № {report.BoxNumber}: {action} ===\n";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Не вдалося оновити коробку",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task StartBoxReplacement()
        {
            if (_currentUser.IsOffline)
            {
                MessageBox.Show("Для запису нового MCU ID потрібне з’єднання із сервером.",
                    "Заміна плати", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var window = new global::FCatFlasher.EditBoxBoardWindow(0, replace: true)
            {
                Owner = Application.Current.MainWindow
            };
            window.SetMcuReplacementMode();
            if (window.ShowDialog() != true)
                return;

            List<BoxReport> localReports = _localDatabaseService.LoadPendingBoxReports().ToList();
            BoxReport? source = localReports.FirstOrDefault(report => BoxReportEditor.Contains(report, window.OldSerial));
            if (source == null)
            {
                source = _apiService.LoadBoxReports()
                    .FirstOrDefault(report => BoxReportEditor.Contains(report, window.OldSerial));
            }

            if (source == null)
            {
                MessageBox.Show($"Плату {window.OldSerial} не знайдено в завершених коробках.",
                    "Заміна плати", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!EnsureConnected())
                return;

            _pendingBoxReplacement = new PendingBoxReplacement(window.OldSerial, source.BoxNumber);
            _workMode = WorkMode.TestFirmware;
            _stlinkSelectedForRun = false;
            _startRunTimerOnBoardDetected = true;
            ResetRunTimer();
            Log += $"=== ЗАМІНА ПЛАТИ {window.OldSerial}, коробка № {source.BoxNumber}: очікується новий MCU ===\n";

            try
            {
                await StartFullCycle();
            }
            finally
            {
                _pendingBoxReplacement = null;
            }
        }

        /// <summary>
        /// Реєструє плату через API або локально, якщо API недоступний.
        /// </summary>
        private sealed class RegistrationException(string message, Exception inner)
            : Exception(message, inner);

        private async Task RegisterDeviceInDatabase(string firmwarePath)
        {
            if (_workMode == WorkMode.TestFirmware)
            {
                await RegisterTestDeviceInDatabase();
                return;
            }

            ProductionBoxState? boxState = null;
            BoxReport? completedBox = null;
            if (_workMode == WorkMode.Production)
            {
                var pendingBox = new BoxTrackingService();
                pendingBox.Restore(_boxTracking.GetState(BoxQuantity, FirmwareName));
                completedBox = pendingBox.RegisterFlashedBoard(BoxQuantity, GetCurrentBoardSerial(), FirmwareName);
                if (completedBox != null)
                    pendingBox.NextBox();
                boxState = pendingBox.GetState(BoxQuantity, FirmwareName);
            }

            try
            {
                _localDatabaseService.SaveFlashedBoard(
                    _currentUser.Id, GetCurrentOrderIdForLocalRegistration(),
                    GetCurrentBoardSerial(), GetCurrentMcuId(), GetCurrentMcuName(),
                    GetCurrentLocalFirmwareName(firmwarePath), GetCurrentRegistrationType(),
                    _reworkProductName, firmwarePath, boxState, completedBox);
            }
            catch (Exception ex)
            {
                throw new RegistrationException(
                    $"Плату {GetCurrentBoardSerial()} прошито, але не зареєстровано. " +
                    "Цикл зупинено; перевірте локальну базу перед продовженням.", ex);
            }

            if (boxState != null)
            {
                _boxTracking.Restore(boxState);
                _completedBox = completedBox;
                NotifyProductionBoxChanged();
            }
            IncrementTodayCounter(GetCurrentRegistrationType());
            LoadLocalDataForCurrentTab();
            if (!_currentUser.IsOffline)
                ScheduleBackgroundSync();
        }

        private async Task RegisterTestDeviceInDatabase()
        {
            if (_currentUser.IsOffline)
            {
                IncrementTodayCounter(TestRegistrationType);
                return;
            }

            string serial = GetCurrentBoardSerial();
            string mcuId = GetCurrentMcuId();
            string mcuName = GetCurrentMcuName();
            try
            {
                await Task.Run(() => _apiService.AddDeviceRegistration(
                    _currentUser.Id, serial, mcuId, mcuName,
                    string.Empty, string.Empty, string.Empty, TestRegistrationType));
            }
            catch (Exception ex)
            {
                Log += "API ERROR: " + ex.Message + "\n";
                if (_pendingBoxReplacement != null)
                {
                    throw new RegistrationException(
                        $"Новий MCU ID для плати {_pendingBoxReplacement.SerialNumber} не записано.", ex);
                }
                Log += "=== OFFLINE: тестову плату не збережено в локальну базу ===\n";
            }

            IncrementTodayCounter(TestRegistrationType);
            if (_pendingBoxReplacement != null)
            {
                Log += $"=== ПЛАТА {_pendingBoxReplacement.SerialNumber}: записано новий MCU ID {mcuId}, коробка № {_pendingBoxReplacement.BoxNumber} ===\n";
                _pendingBoxReplacement = null;
            }
        }

        private sealed record PendingBoxReplacement(string SerialNumber, int BoxNumber);

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

        /// <summary>
        /// Показує звіт завершеної коробки та обробляє вибір оператора.
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
                RequestStop();
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
