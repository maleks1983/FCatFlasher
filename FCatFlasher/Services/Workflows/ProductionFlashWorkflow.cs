namespace FCatFlasher.Services.Workflows
{
/// <summary>
/// Описує перехід від успішного тесту до виробничої прошивки.
/// </summary>
    internal sealed class ProductionFlashWorkflow
    {
        private const int PowerOffBeforeStlinkDelayMs = 100;
        private const int SerialAfterPowerOffDelayMs = 50;
        private const int StlinkAfterTestModeSwitchDelayMs = 50;
        private const int PowerOnSettleDelayMs = 0;

        private readonly Func<string, int, bool, Task> _sendStandCommand;
        private readonly Func<int, int, bool, Task> _setStlinkModeAndEnsurePowerOn;
        private readonly Func<Task> _startFlashing;

/// <summary>
/// Приймає делегати ViewModel для керування стендом і запуску прошивки.
/// </summary>
        public ProductionFlashWorkflow(
            Func<string, int, bool, Task> sendStandCommand,
            Func<int, int, bool, Task> setStlinkModeAndEnsurePowerOn,
            Func<Task> startFlashing)
        {
            _sendStandCommand = sendStandCommand;
            _setStlinkModeAndEnsurePowerOn = setStlinkModeAndEnsurePowerOn;
            _startFlashing = startFlashing;
        }

/// <summary>
/// Вимикає живлення, перемикає ST-LINK, подає живлення і стартує прошивку.
/// </summary>
        public async Task FlashAfterSuccessfulTest()
        {
            await _sendStandCommand("POWER_OFF", PowerOffBeforeStlinkDelayMs, true);
            await Task.Delay(SerialAfterPowerOffDelayMs);
            await _setStlinkModeAndEnsurePowerOn(
                StlinkAfterTestModeSwitchDelayMs,
                PowerOnSettleDelayMs,
                false);

            await _startFlashing();
        }
    }
}
