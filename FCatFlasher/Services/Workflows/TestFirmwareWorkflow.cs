namespace FCatFlasher.Services.Workflows
{
    internal sealed class TestFirmwareWorkflow
    {
        private const int BuzzerOkDelayMs = 200;

        private readonly Action _setFlashingState;
        private readonly Action _setWaitingRemoveState;
        private readonly Action<string> _setStatus;
        private readonly Action<int> _setProgress;
        private readonly Action<string> _appendLog;
        private readonly Action<string> _setCurrentDeviceType;
        private readonly Func<string, Task<bool>> _ensureStlinkReady;
        private readonly Func<string, Task<string>> _getDeviceTypeWithRetry;
        private readonly Func<string, bool> _isKnownDeviceType;
        private readonly Func<string, Task<string>> _flashAutoTestFirmwareWithRetry;
        private readonly Func<string, bool> _testFirmwareFailed;
        private readonly Func<string, int, bool, Task> _sendStandCommand;
        private readonly Action<string, string> _showTestErrorDialog;
        private readonly Action<string> _registerDeviceInDatabase;
        private readonly Func<Task> _waitBoardRemoved;
        private readonly Func<Task> _waitBoardRemovedAfterError;

        public TestFirmwareWorkflow(
            Action setFlashingState,
            Action setWaitingRemoveState,
            Action<string> setStatus,
            Action<int> setProgress,
            Action<string> appendLog,
            Action<string> setCurrentDeviceType,
            Func<string, Task<bool>> ensureStlinkReady,
            Func<string, Task<string>> getDeviceTypeWithRetry,
            Func<string, bool> isKnownDeviceType,
            Func<string, Task<string>> flashAutoTestFirmwareWithRetry,
            Func<string, bool> testFirmwareFailed,
            Func<string, int, bool, Task> sendStandCommand,
            Action<string, string> showTestErrorDialog,
            Action<string> registerDeviceInDatabase,
            Func<Task> waitBoardRemoved,
            Func<Task> waitBoardRemovedAfterError)
        {
            _setFlashingState = setFlashingState;
            _setWaitingRemoveState = setWaitingRemoveState;
            _setStatus = setStatus;
            _setProgress = setProgress;
            _appendLog = appendLog;
            _setCurrentDeviceType = setCurrentDeviceType;
            _ensureStlinkReady = ensureStlinkReady;
            _getDeviceTypeWithRetry = getDeviceTypeWithRetry;
            _isKnownDeviceType = isKnownDeviceType;
            _flashAutoTestFirmwareWithRetry = flashAutoTestFirmwareWithRetry;
            _testFirmwareFailed = testFirmwareFailed;
            _sendStandCommand = sendStandCommand;
            _showTestErrorDialog = showTestErrorDialog;
            _registerDeviceInDatabase = registerDeviceInDatabase;
            _waitBoardRemoved = waitBoardRemoved;
            _waitBoardRemovedAfterError = waitBoardRemovedAfterError;
        }

        public async Task FlashOnlyTestFirmware()
        {
            try
            {
                _setFlashingState();

                if (!await _ensureStlinkReady("test firmware"))
                {
                    await WaitBoardRemovedAfterFailure();
                    return;
                }

                _setStatus("Flashing test firmware");
                _setProgress(50);
                _appendLog("=== ТЕСТОВА ПРОШИВКА ===\n");

                string deviceType = await _getDeviceTypeWithRetry("test firmware");
                _setCurrentDeviceType(deviceType);

                if (!_isKnownDeviceType(deviceType))
                {
                    _setStatus("MCU Error");
                    _setProgress(100);
                    _appendLog($"ERROR: MCU not detected: {deviceType}\n");
                    _appendLog(">>> Заберіть плату зі стенда...\n");

                    await WaitBoardRemovedAfterFailure();
                    return;
                }

                _appendLog($"MCU: {deviceType}\n");

                string result = await _flashAutoTestFirmwareWithRetry(deviceType);

                if (_testFirmwareFailed(result))
                {
                    _setStatus("Test firmware error");
                    _setProgress(100);
                    _appendLog(result + "\n");
                    _appendLog("=== ТЕСТОВУ ПРОШИВКУ НЕ ЗАЛИТО ===\n");
                    _appendLog(">>> Заберіть плату зі стенда...\n");
                    _showTestErrorDialog("Помилка тестової прошивки", result);

                    await WaitBoardRemovedAfterFailure();
                    return;
                }

                _appendLog(result + "\n");

                await _sendStandCommand("BUZZ_OK", BuzzerOkDelayMs, true);

                _setStatus("Test firmware OK");
                _setProgress(100);

                _registerDeviceInDatabase("");

                _appendLog("\n=== ТЕСТОВУ ПРОШИВКУ ЗАВЕРШЕНО ===\n");
                _appendLog(">>> Заберіть плату зі стенда...\n");

                _setWaitingRemoveState();

                await _waitBoardRemoved();
            }
            catch (Exception ex)
            {
                _setStatus("Error");
                _appendLog("ERROR: " + ex.Message + "\n");

                await WaitBoardRemovedAfterFailure();
            }
        }

        private async Task WaitBoardRemovedAfterFailure()
        {
            _setWaitingRemoveState();
            await _waitBoardRemovedAfterError();
        }
    }
}
