using FCatFlasher.Models;
using FCatFlasher.Services;
using FCatFlasher.ViewModels;
using Microsoft.Data.Sqlite;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;

internal static class Program
{
    private static readonly string TestRoot = Path.Combine(Path.GetTempPath(), "FCatFlasher-tests-" + Guid.NewGuid());
    private static int _passed;

    [STAThread]
    private static int Main()
    {
        Directory.CreateDirectory(TestRoot);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        int exitCode = 0;
        app.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                await Test("diagnostics framing and duplicate END", TestDiagnostics);
                await Test("firmware filename compatibility in both directions", TestCompatibility);
                await Test("revision migration preserves pending data", TestMigration);
                await Test("device update during HTTP remains pending", () => TestRevision(false));
                await Test("rework update during HTTP remains pending", () => TestRevision(true));
                await Test("concurrent sync sends a pending row once", TestConcurrentSync);
                await Test("rework save is atomic", TestAtomicSave);
                await Test("production box survives restart and completes atomically", TestProductionBoxRecovery);
                await Test("cancelled native operation is not started", TestCancelledFirmware);
                await Test("initial API loading leaves the dispatcher responsive", TestAsyncInitialization);
                await Test("idle window closes after asynchronous shutdown", TestWindowClose);
                await Test("Stop waits for detection and prevents flashing", TestStopDuringDetection);
                await Test("multiple test-firmware boards stop and restart cleanly", TestRepeatedBoards);
                await Test("duplicate UART report completes test only once", TestUartCompletion);
                await Test("Stop cancels serial-number wait", TestStopWaitingSerial);
                await Test("Stop cancels diagnostics wait", TestStopDuringDiagnostics);
                await Test("Stop waits for a production flash", TestStopDuringFlash);
                await Test("production success records once and starts next board", () => TestSuccessfulFlash(false));
                await Test("rework success records both rows and starts next board", () => TestSuccessfulFlash(true));
                await Test("firmware mismatch shows a dialog and resumes after OK", TestWrongFirmware);
                await Test("registration failure stops without counting a board", TestRegistrationFailure);
                Console.WriteLine($"PASS: {_passed} regression checks");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                exitCode = 1;
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                // This directory is created exclusively by this test process.
                Directory.Delete(TestRoot, recursive: true);
                app.Shutdown();
            }
        });
        app.Run();
        return exitCode;
    }

    private static async Task Test(string name, Func<Task> test)
    {
        await test().WaitAsync(TimeSpan.FromSeconds(20));
        _passed++;
        Console.WriteLine("PASS " + name);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static readonly string[] Report =
    [
        "DIAGNOSTICS REPORT BEGIN", "SIG: 1", "SER: ABC123",
        "OUT_IO: OK", "OUT_HS: OK", "OUT_LS: OK", "ADC: OK",
        "ACC: OK", "BUZ: OK", "DIAGNOSTICS REPORT END"
    ];

    private static Task TestDiagnostics()
    {
        var parser = new DiagnosticsParser();
        Check(parser.ProcessLine("DIAGNOSTICS REPORT END") == null, "END without BEGIN was accepted");
        foreach (string line in Report[..^1]) Check(parser.ProcessLine(line) == null, "Premature result");
        Check(parser.ProcessLine(Report[^1]) == true, "Valid report rejected");
        Check(parser.ProcessLine(Report[^1]) == null, "Old report reused");
        parser.ProcessLine(Report[0]);
        Check(parser.ProcessLine(Report[^1]) == false, "Incomplete report accepted");
        return Task.CompletedTask;
    }

    private static Task TestCompatibility()
    {
        string c0 = Path.Combine(TestRoot, "firmware-C0.hex");
        string g0 = Path.Combine(TestRoot, "firmware.hex");
        File.WriteAllText(c0, "test");
        File.WriteAllText(g0, "test");
        Check(FirmwareCompatibility.Validate("C0", c0) == "", "C0 file rejected");
        Check(FirmwareCompatibility.Validate("G0", g0) == "", "Legacy G0 file rejected");
        Check(FirmwareCompatibility.Validate("G0", c0).Contains("ERROR"), "C0 file accepted for G0");
        Check(FirmwareCompatibility.Validate("C0", g0).Contains("ERROR"), "G0 file accepted for C0");
        Check(FirmwareCompatibility.Validate("UNKNOWN", g0).Contains("ERROR"), "Unknown MCU accepted");
        Check(FirmwareCompatibility.Validate("G0", g0 + ".missing").Contains("ERROR"), "Missing file accepted");
        return Task.CompletedTask;
    }

    private static (LocalDatabaseService Db, string Path) Database()
    {
        string path = Path.Combine(TestRoot, Guid.NewGuid() + ".db");
        var db = new LocalDatabaseService(path);
        db.Initialize();
        return (db, path);
    }

    private static Task TestProductionBoxRecovery()
    {
        var (db, path) = Database();
        var tracker = new BoxTrackingService();
        tracker.RegisterFlashedBoard(2, "D001", "firmware");
        db.SaveFlashedBoard("1", "", "D001", "mcu1", "G0", "firmware", "PRODUCTION", "", "",
            tracker.GetState(2, "firmware"));
        db = new LocalDatabaseService(path);
        db.Initialize();
        var saved = db.LoadProductionBox("1")!;
        Check(saved.SerialNumbers.SequenceEqual(new[] { "D001" }) && saved.BoxQuantity == 2 &&
            saved.FirmwareName == "firmware", "Partial box was lost");
        Check(db.LoadProductionBox("2") == null, "Another user's box was restored");
        tracker = new BoxTrackingService();
        tracker.Restore(saved);
        tracker.LoadReports(new[] { new BoxReport { BoxNumber = 99 } });
        Check(tracker.CurrentBoxNumber == 1, "API refresh renumbered the active box");
        var completed = tracker.RegisterFlashedBoard(2, "D002", "firmware")!;
        tracker.NextBox();
        Sql(path, "CREATE TRIGGER fail_box BEFORE INSERT ON pending_box_reports BEGIN SELECT RAISE(ABORT, 'test'); END;");
        bool failed = false;
        try
        {
            db.SaveFlashedBoard("1", "", "D002", "mcu2", "G0", "firmware", "PRODUCTION", "", "",
                tracker.GetState(2, "firmware"), completed);
        }
        catch (SqliteException) { failed = true; }
        Check(failed && db.LoadProductionBox("1")!.SerialNumbers.Count == 1 &&
            Sql(path, "SELECT COUNT(*) FROM pending_device_registrations") == 1, "Failed completion was not rolled back");
        Sql(path, "DROP TRIGGER fail_box;");
        db.SaveFlashedBoard("1", "", "D002", "mcu2", "G0", "firmware", "PRODUCTION", "", "",
            tracker.GetState(2, "firmware"), completed);
        saved = new LocalDatabaseService(path).LoadProductionBox("1")!;
        Check(saved.BoxNumber == 2 && saved.SerialNumbers.Count == 0, "Completed box reopened");
        Check(db.LoadPendingBoxReports().Single().SerialNumbersText == "D001-D002", "Serial numbers were lost");
        tracker.RegisterFlashedBoard(2, "D003", "firmware");
        var partial = tracker.CompleteCurrentBox("firmware")!;
        tracker.NextBox();
        db.CompleteProductionBox("1", partial, tracker.GetState(2, "firmware"));
        Check(db.LoadProductionBox("1")!.BoxNumber == 3 && db.LoadPendingBoxReports().Count == 2,
            "Manual completion was not persisted");
        return Task.CompletedTask;
    }

    private static long Sql(string path, string sql)
    {
        using var connection = new SqliteConnection("Data Source=" + path);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar() ?? 0);
    }

    private static void Add(LocalDatabaseService db, bool rework, string firmware)
    {
        if (rework)
            db.AddPendingReworkRegistration("1", "order", "D1234567", "product", firmware, firmware, "ABC123", "STM32G0", "OK");
        else
            db.AddPendingDeviceRegistration("1", "order", "D1234567", "ABC123", "STM32G0", firmware, "REWORK");
    }

    private static Task TestMigration()
    {
        var (db, path) = Database();
        Add(db, false, "old");
        foreach (string table in new[] { "pending_device_registrations", "pending_rework_registrations", "pending_box_reports" })
            Sql(path, $"ALTER TABLE {table} DROP COLUMN revision;");
        db.Initialize();
        Check(Sql(path, "SELECT revision FROM pending_device_registrations") == 1, "Migration lost the row or revision");
        return Task.CompletedTask;
    }

    private static async Task TestRevision(bool rework)
    {
        var (db, path) = Database();
        Add(db, rework, "old");
        int requests = 0;
        var handler = new Handler(_ =>
        {
            if (Interlocked.Increment(ref requests) == 1) Add(db, rework, "new");
            return Task.CompletedTask;
        });
        var api = new ApiService("http://test.invalid/", handler);
        await Task.Run(() => db.SyncPending(api));
        string table = rework ? "pending_rework_registrations" : "pending_device_registrations";
        Check(Sql(path, $"SELECT is_synced FROM {table}") == 0, "An unsent update was marked synced");
        Check(Sql(path, $"SELECT revision FROM {table}") == 2, "Revision was not incremented");
        await Task.Run(() => db.SyncPending(api));
        Check(Sql(path, $"SELECT is_synced FROM {table}") == 1, "New version was not synced");
        Check(requests == 2, "Unexpected request count");
    }

    private static async Task TestConcurrentSync()
    {
        var (db, _) = Database();
        Add(db, false, "firmware");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int requests = 0;
        var api = new ApiService("http://test.invalid/", new Handler(async _ =>
        {
            Interlocked.Increment(ref requests);
            entered.TrySetResult();
            await release.Task;
        }));
        Task first = Task.Run(() => db.SyncPending(api));
        await entered.Task;
        Task second = Task.Run(() => db.SyncPending(api));
        release.SetResult();
        await Task.WhenAll(first, second);
        Check(requests == 1, "Concurrent sync duplicated a POST");
    }

    private static Task TestAtomicSave()
    {
        var (db, path) = Database();
        Sql(path, "CREATE TRIGGER reject_rework BEFORE INSERT ON pending_rework_registrations BEGIN SELECT RAISE(ABORT, 'test failure'); END;");
        bool failed = false;
        try { db.SaveFlashedBoard("1", "order", "D1234567", "ABC123", "STM32G0", "fw", "REWORK", "product", "fw.hex"); }
        catch (SqliteException) { failed = true; }
        Check(failed, "Expected save failure");
        Check(Sql(path, "SELECT COUNT(*) FROM pending_device_registrations") == 0, "Half of the rework registration was committed");
        return Task.CompletedTask;
    }

    private static async Task TestCancelledFirmware()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var firmware = new FirmwareService { RunCancellationToken = cts.Token };
        bool cancelled = false;
        try { await firmware.GetDeviceType(); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "Cancellation was converted into a native API error");
    }

    private static async Task TestAsyncInitialization()
    {
        var (db, _) = Database();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new ApiService("http://test.invalid/", new Handler(async _ =>
        {
            entered.TrySetResult();
            await release.Task;
        }, request => request.RequestUri!.AbsolutePath.EndsWith("/count") ? "{\"count\":3}" : "[]"));
        var vm = new MainViewModel(api, db, new CurrentUser { Id = "1", Username = "test" },
            new FakeFirmware(), new FakeSerial());
        Task initialization = vm.InitializeAsync();
        await entered.Task;
        Check(vm.IsBusy && !initialization.IsCompleted, "Initialization did not yield to the dispatcher");
        await vm.StopAll();
        Check(vm.IsBusy, "Stop incorrectly enabled a run during data loading");
        release.SetResult();
        await initialization;
        Check(!vm.IsBusy && vm.FlashedToday == 3, "API snapshot was not applied");
        await Until(() => !(bool)Field(vm, "_backgroundSyncRunning").GetValue(vm)!);
        await vm.ShutdownAsync();
    }

    private static async Task TestWindowClose()
    {
        var (db, _) = Database();
        var window = new FCatFlasher.MainWindow(
            new ApiService("http://test.invalid/", new Handler(_ => throw new Exception("Unexpected HTTP"))),
            db, new CurrentUser { Id = "1", Username = "test", IsOffline = true });
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        window.Close();
        await Until(() => closed);
    }

    private static (MainViewModel Vm, FakeFirmware Firmware, FakeSerial Serial, string DbPath) ViewModel(Action<string, string>? showFirmwareErrorDialog = null)
    {
        var (db, path) = Database();
        var firmware = new FakeFirmware();
        var serial = new FakeSerial();
        var vm = new MainViewModel(new ApiService("http://test.invalid/", new Handler(_ => throw new Exception("Unexpected HTTP"))),
            db, new CurrentUser { Id = "1", Username = "test", IsOffline = true }, firmware, serial,
            showFirmwareErrorDialog ?? ((_, message) => throw new Exception("Unexpected dialog: " + message)));
        Set(vm, "_settingsLoaded", false);
        string directory = Path.Combine(TestRoot, Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "test.hex"), "fake");
        vm.TestFirmwareDirectory = directory;
        vm.FirmwarePath = Path.Combine(directory, "production.hex");
        File.WriteAllText(vm.FirmwarePath, "fake");
        vm.G0FirmwarePath = vm.FirmwarePath;
        vm.BoxQuantity = 2;
        return (vm, firmware, serial, path);
    }

    private static async Task Until(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }

    private static async Task TestStopDuringDetection()
    {
        var (vm, firmware, serial, _) = ViewModel();
        firmware.ConnectionGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.TestFirmwareCommand.Execute(null);
        await Until(() => firmware.ConnectionCalls > 0);
        Task stop = vm.StopAll();
        Check(vm.IsBusy && !stop.IsCompleted, "New run was enabled before the native operation finished");
        firmware.ConnectionGate.SetResult(true);
        await stop;
        Check(firmware.Writes == 0 && firmware.Unlocks == 0, "Stop allowed flashing to start");
        Check(!vm.IsBusy && serial.Commands[^1] == "POWER_OFF", "Stop did not finish safely");
        await vm.ShutdownAsync();
    }

    private static async Task TestRepeatedBoards()
    {
        var (vm, firmware, serial, _) = ViewModel();
        firmware.PauseWriteAt = 3;
        vm.TestFirmwareCommand.Execute(null);
        await Until(() => firmware.Writes == 3);
        Task stop = vm.StopAll();
        firmware.WriteGate.SetResult("OK");
        await stop;
        Check(!vm.IsBusy && firmware.Writes == 3, "Board loop did not stop");
        firmware.PauseWriteAt = 4;
        firmware.WriteGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.TestFirmwareCommand.Execute(null);
        await Until(() => firmware.Writes == 4);
        stop = vm.StopAll();
        firmware.WriteGate.SetResult("OK");
        await stop;
        Check(serial.Commands[^1] == "POWER_OFF", "Restart retained stale run state");
        await vm.ShutdownAsync();
    }

    private static void EmitReport(MainViewModel vm)
    {
        foreach (string line in Report) Invoke(vm, "OnDataReceived", line);
        Invoke(vm, "OnDataReceived", Report[^1]);
    }

    private static async Task TestUartCompletion()
    {
        var (vm, _, serial, _) = ViewModel();
        vm.TestOnlyCommand.Execute(null);
        await Until(() => vm.Status == "Testing");
        EmitReport(vm);
        await Until(() => !vm.IsBusy);
        Check(serial.Commands.Count(x => x == "BUZZ_OK") == 1, "Duplicate UART result repeated completion");
        await vm.ShutdownAsync();
    }

    private static async Task TestStopWaitingSerial()
    {
        var (vm, firmware, _, _) = ViewModel();
        Set(vm, "_workMode", Enum.Parse(Field(vm, "_workMode").FieldType, "Production"));
        Task run = (Task)Invoke(vm, "StartFullCycle")!;
        await Until(() => vm.Status == "Testing");
        EmitReport(vm);
        await Until(() => vm.Status == "Waiting serial");
        await vm.StopAll();
        await run;
        vm.ScannedSerial = "D1234567";
        await Task.Delay(300);
        Check(firmware.ProductionWrites == 0 && !vm.IsBusy, "Serial input revived a cancelled run");
        await vm.ShutdownAsync();
    }

    private static async Task TestRegistrationFailure()
    {
        var (vm, firmware, _, path) = ViewModel();
        Sql(path, "DROP TABLE pending_device_registrations;");
        Set(vm, "_workMode", Enum.Parse(Field(vm, "_workMode").FieldType, "Production"));
        Task run = (Task)Invoke(vm, "StartFullCycle")!;
        await Until(() => vm.Status == "Testing");
        EmitReport(vm);
        await Until(() => vm.Status == "Waiting serial");
        vm.ScannedSerial = "D1234567";
        await run;
        var box = (BoxTrackingService)Field(vm, "_boxTracking").GetValue(vm)!;
        Check(firmware.ProductionWrites == 1 && box.CurrentBoxCount == 0, "Unregistered board was counted or flashed again");
        Check(vm.Log.Contains("не зареєстровано"), "Missing registration failure message");
        await vm.ShutdownAsync();
    }

    private static async Task TestStopDuringDiagnostics()
    {
        var (vm, _, serial, _) = ViewModel();
        vm.TestOnlyCommand.Execute(null);
        await Until(() => vm.Status == "Testing");
        Task first = vm.StopAll();
        Task second = vm.StopAll();
        Check(ReferenceEquals(first, second), "Repeated Stop started a second shutdown");
        await first;
        EmitReport(vm);
        await Task.Delay(50);
        Check(!vm.IsBusy && !serial.Commands.Contains("BUZZ_OK"), "Cancelled diagnostics still completed");
        await vm.ShutdownAsync();
    }

    private static async Task TestStopDuringFlash()
    {
        var (vm, firmware, serial, path) = ViewModel();
        firmware.PauseProduction = true;
        Set(vm, "_workMode", Enum.Parse(Field(vm, "_workMode").FieldType, "Production"));
        Task run = (Task)Invoke(vm, "StartFullCycle")!;
        await Until(() => vm.Status == "Testing");
        EmitReport(vm);
        await Until(() => vm.Status == "Waiting serial");
        vm.ScannedSerial = "D1234567";
        await Until(() => firmware.ProductionWrites == 1);
        int commandsBeforeStop = serial.Commands.Count;
        Task stop = vm.StopAll();
        Check(vm.IsBusy && !stop.IsCompleted, "Stop abandoned the active native call");
        Check(serial.Commands.Count == commandsBeforeStop, "Power was switched during the native call");
        firmware.ProductionGate.SetResult();
        await stop;
        await run;
        Check(Sql(path, "SELECT COUNT(*) FROM pending_device_registrations") == 0, "Cancelled operation was registered");
        Check(serial.Commands[^1] == "POWER_OFF", "Stop did not power off after the native call");
        await vm.ShutdownAsync();
    }

    private static async Task TestSuccessfulFlash(bool rework)
    {
        var (vm, firmware, _, path) = ViewModel();
        Set(vm, "_workMode", Enum.Parse(Field(vm, "_workMode").FieldType, rework ? "Reflash" : "Production"));
        Task run = (Task)Invoke(vm, "StartFullCycle")!;
        await Until(() => vm.Status == "Testing");
        EmitReport(vm);
        await Until(() => Field(vm, "_cycleState").GetValue(vm)!.ToString() == "WaitingSerial");
        vm.ScannedSerial = "D1234567";
        await Until(() => firmware.ProductionWrites == 1 && vm.Status == "Testing");
        await vm.StopAll();
        await run;
        Check(Sql(path, "SELECT COUNT(*) FROM pending_device_registrations") == 1, "Wrong device registration count");
        Check(Sql(path, "SELECT COUNT(*) FROM pending_rework_registrations") == (rework ? 1 : 0), "Wrong rework registration count");
        var restarted = new MainViewModel(
            new ApiService("http://test.invalid/", new Handler(_ => throw new Exception("Unexpected HTTP"))),
            new LocalDatabaseService(path), new CurrentUser { Id = "1", Username = "test", IsOffline = true },
            new FakeFirmware(), new FakeSerial());
        var restoredBox = (BoxTrackingService)Field(restarted, "_boxTracking").GetValue(restarted)!;
        Check(restoredBox.CurrentBoxCount == (rework ? 0 : 1), "Restart restored an incorrect board count");
        if (!rework)
        {
            Check(restarted.BoxQuantity == 2 &&
                restoredBox.GetState(2, "").SerialNumbers.Single() == "D1234567", "Restart lost box details");
        }
        await restarted.ShutdownAsync();
        await vm.ShutdownAsync();
    }

    private static async Task TestWrongFirmware()
    {
        int dialogs = 0;
        string message = "";
        var (vm, firmware, _, path) = ViewModel((_, details) =>
        {
            dialogs++;
            message = details;
        });
        vm.FirmwarePath = Path.Combine(TestRoot, "firmware-C0.hex");
        Set(vm, "_workMode", Enum.Parse(Field(vm, "_workMode").FieldType, "Production"));
        Task run = (Task)Invoke(vm, "StartFullCycle")!;
        await Until(() => vm.Status == "Testing");
        EmitReport(vm);
        await Until(() => vm.Status == "Waiting serial");
        vm.ScannedSerial = "D1234567";
        await Until(() => dialogs == 1 && vm.Status == "Testing");
        Check(vm.IsBusy && !run.IsCompleted, "Cycle stopped after acknowledging the dialog");
        Check(message.Contains("C0 firmware cannot be written to G0") && message.Contains("ОК"),
            "Dialog is missing the mismatch details or continuation instruction");
        Check(firmware.ProductionWrites == 0, "Incompatible firmware was written");
        Check(Sql(path, "SELECT COUNT(*) FROM pending_device_registrations") == 0, "Rejected board was registered");
        await vm.StopAll();
        await run;
        await vm.ShutdownAsync();
    }

    private static FieldInfo Field(object target, string name) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static void Set(object target, string name, object value) => Field(target, name).SetValue(target, value);
    private static object? Invoke(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);

    private sealed class Handler(Func<HttpRequestMessage, Task> onRequest, Func<HttpRequestMessage, string>? body = null) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await onRequest(request);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body?.Invoke(request) ?? "{}") };
        }
    }

    private sealed class FakeSerial : SerialService
    {
        public List<string> Commands { get; } = [];
        public override bool IsOpen => true;
        public override void Send(string command) => Commands.Add(command);
        public override void Open(string portName, int baud = 115200) { }
        public override void ClearBuffers() { }
        public override void Close() { }
    }

    private sealed class FakeFirmware : FirmwareService
    {
        public int ConnectionCalls, Writes, Unlocks, ProductionWrites, PauseWriteAt;
        public bool PauseProduction;
        public TaskCompletionSource ProductionGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool>? ConnectionGate;
        public TaskCompletionSource<string> WriteGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task<bool> CheckConnection()
        {
            ConnectionCalls++;
            return ConnectionGate == null || await ConnectionGate.Task;
        }
        public override Task<bool> CheckBoardPresence() => Task.FromResult(false);
        public override Task<string> GetDeviceType() => Task.FromResult("G0");
        public override Task<RdpUnlockSessionResult> GetDeviceTypeAndUnlockRdp() =>
            Task.FromResult(new RdpUnlockSessionResult("G0", "OK"));
        public override Task<string> UnlockRdp()
        {
            Unlocks++;
            return Task.FromResult("OK");
        }
        public override async Task<string> WriteFirmware(string firmwarePath, bool resetAfterWrite)
        {
            Writes++;
            if (Writes == PauseWriteAt) await WriteGate.Task;
            RunCancellationToken.ThrowIfCancellationRequested();
            return "OK";
        }
        public override async Task<string> FlashProductionFirmware(string firmwarePath)
        {
            ProductionWrites++;
            if (PauseProduction) await ProductionGate.Task;
            RunCancellationToken.ThrowIfCancellationRequested();
            return "OK";
        }
        public override async Task<ProductionFlashSessionResult> FlashProductionFirmwareSession(string firmwarePath, Func<string, string> validateDeviceType)
        {
            string error = validateDeviceType("G0");
            if (error.Length > 0)
                return new ProductionFlashSessionResult("G0", error);
            await FlashProductionFirmware(firmwarePath);
            return new ProductionFlashSessionResult("G0", "OK");
        }
    }
}
