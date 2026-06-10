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


namespace FCatFlasher.ViewModels
{
    internal class MainViewModel : INotifyPropertyChanged
    {
        private const int BoardRemovedCheckTimeoutMs = 100;
        private const int BoardRemovedConfirmations = 3;
        private const int CapacitorDischargeDelayMs = 500;
        private const int PowerOnSettleDelayMs = 100;
        private const int StlinkConnectionCheckTimeoutMs = 1000;
        private const int StlinkPowerRetryIntervalMs = 100;

        private readonly FlashService _flashService = new();
        private readonly SerialService _serial = new();
        private readonly SettingsService _settingsService = new();
        private readonly DiagnosticsParser _diagnosticsParser = new();
        private readonly BoxTrackingService _boxTracking = new();
        private readonly UniqueBoardCounter _uniqueBoardCounter = new();
        private bool _settingsLoaded;
        private string _currentDeviceType = "";

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
        private CycleState _cycleState = CycleState.Idle;
        private DateTime _lastStlinkPowerRetryUtc = DateTime.MinValue;

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

        public ObservableCollection<BoxReport> BoxReports => _boxTracking.BoxReports;

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
            }
        }

        public string BoxQuantityText
        {
            get => BoxQuantity == 0
                ? string.Empty
                : BoxQuantity.ToString(CultureInfo.InvariantCulture);
            set
            {
                BoxQuantity = ParsePositiveInt(value);
                OnPropertyChanged();
            }
        }

        private int _plannedQuantity;

        public int PlannedQuantity
        {
            get => _plannedQuantity;
            set
            {
                if (_plannedQuantity == value)
                    return;

                _plannedQuantity = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PlannedQuantityText));
            }
        }

        public string PlannedQuantityText
        {
            get => PlannedQuantity == 0
                ? string.Empty
                : PlannedQuantity.ToString(CultureInfo.InvariantCulture);
            set
            {
                PlannedQuantity = ParsePositiveInt(value);
                OnPropertyChanged();
            }
        }

        public int FlashedToday
        {
            get => _boxTracking.FlashedToday;
        }

        public int UniqueFlashedSinceStart
        {
            get => _uniqueBoardCounter.Count;
        }

        public MainViewModel()
        {
            ConnectCommand = new RelayCommand(Connect);

            TestFirmwareCommand = new RelayCommand(
                async () =>
                {
                    if (!EnsureConnected())
                        return;

                    _workMode = WorkMode.TestFirmware;
                    await StartFullCycle();
                },
                () => !IsBusy
            );

            FlashCommand = new RelayCommand(
                async () =>
                {
                    if (!EnsureConnected())
                        return;

                    _workMode = WorkMode.Production;
                    ResetUniqueFlashedCounter();
                    await StartFullCycle();
                },
                () => !IsBusy
            );

            ReflashCommand = new RelayCommand(
                async () =>
                {
                    if (!EnsureConnected())
                        return;

                    _workMode = WorkMode.Reflash;
                    ResetUniqueFlashedCounter();
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

            _serial.DataReceived += OnDataReceived;

            RefreshComPorts();
            LoadSettings();
            _settingsLoaded = true;
        }

        private static int ParsePositiveInt(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0;

            return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int result) && result > 0
                ? result
                : 0;
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
                IsBusy = false;
                _cycleState = CycleState.Idle;
            }
        }

        private async Task WaitBoardInserted()
        {
            Status = "Waiting board";

            while (_cycleState == CycleState.WaitingBoard)
            {
                bool present = await _flashService.CheckConnection();

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
                await Task.Delay(100);
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

                string result = await _flashService.FlashAutoTestFirmware();

                if (TestFirmwareFailed(result))
                {
                    Status = "Test firmware error";
                    ProgressValue = 100;
                    Log += "=== ТЕСТОВУ ПРОШИВКУ НЕ ЗАЛИТО ===\n";
                    Log += ">>> Заберіть плату зі стенда...\n";

                    _cycleState = CycleState.WaitingRemove;

                    await WaitBoardRemoved();
                    return;
                }

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

                _currentDeviceType = await _flashService.GetDeviceType();

                if (_currentDeviceType == "UNKNOWN")
                {
                    Log += "ERROR: Unknown MCU\n";
                    await StopAll();
                    return;
                }

                Log += $"MCU: {_currentDeviceType}\n";

                string result = await FlashAutoTestFirmwareWithRetry();

                if (TestFirmwareFailed(result))
                {
                    Status = "Test firmware error";
                    ProgressValue = 100;
                    Log += "\n=== ТЕСТОВУ ПРОШИВКУ НЕ ЗАЛИТО ===\n";
                    Log += ">>> Заберіть плату зі стенда...\n";

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

        private async Task<string> FlashAutoTestFirmwareWithRetry()
        {
            string result = await _flashService.FlashAutoTestFirmware();

            if (!TestFirmwareFailed(result))
                return result;

            Log += "\nWARNING: MCU не визначився після RDP unlock. Робимо power cycle і пробуємо ще раз...\n";

            await PowerCycleToStlinkMode();

            return await _flashService.FlashAutoTestFirmware();
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
                    Log += ">>> Заберіть плату зі стенда...\n";

                    await StopTestAndWaitRemove();
                    return;
                }
            });
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
            if (await _flashService.CheckConnection(StlinkConnectionCheckTimeoutMs))
                return true;

            Log += $"WARNING: ST-LINK немає зв'язку ({context}). Повторно подаємо живлення...\n";

            await PowerCycleToStlinkMode();

            if (await _flashService.CheckConnection(StlinkConnectionCheckTimeoutMs))
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
            
            _serial.Send("PWM_START");
            await PowerCycleToUartMode();
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

            _serial.Send("POWER_OFF");
            await Task.Delay(200);

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

                string result = await _flashService.FlashProductionFirmware(firmwareToFlash);

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

        private async Task WaitBoardRemoved()
        {
            Status = "Waiting remove";
            int absentCount = 0;

            while (_cycleState == CycleState.WaitingRemove)
            {
                bool present = await _flashService.CheckConnection(BoardRemovedCheckTimeoutMs);

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

                await Task.Delay(100);
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

                while (await _flashService.CheckConnection(BoardRemovedCheckTimeoutMs))
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

            OnPropertyChanged(nameof(FlashedToday));

            return report;
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
            G0FirmwarePath = settings.G0FirmwarePath;
            C0FirmwarePath = settings.C0FirmwarePath;
            TestFirmwareDirectory = settings.TestFirmwareDirectory;
            StmCubeCliPath = settings.StmCubeCliPath;

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
                G0FirmwarePath = G0FirmwarePath,
                C0FirmwarePath = C0FirmwarePath,
                TestFirmwareDirectory = TestFirmwareDirectory,
                StmCubeCliPath = StmCubeCliPath
            });
        }

        private void ApplyFlashSettings()
        {
            _flashService.TestFirmwareDirectory = TestFirmwareDirectory;
            _flashService.CliPath = StmCubeCliPath;
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
