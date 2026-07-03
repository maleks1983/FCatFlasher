namespace FCatFlasher.Services.Workflows
{
/// <summary>
/// Описує перехід від успішного тесту до виробничої прошивки.
/// </summary>
    internal sealed class ProductionFlashWorkflow
    {
        private const int PowerOffBeforeStlinkDelayMs = 300;
        private const int StlinkAfterTestModeSwitchDelayMs = 100;
        private const int PowerOnSettleDelayMs = 50;

        private readonly Func<string, int, bool, Task> _sendStandCommand;
        private readonly Func<int, int, Task> _setStlinkModeAndEnsurePowerOn;
        private readonly Func<Task> _startFlashing;

/// <summary>
/// Приймає делегати ViewModel для керування стендом і запуску прошивки.
/// </summary>
        public ProductionFlashWorkflow(
            Func<string, int, bool, Task> sendStandCommand,
            Func<int, int, Task> setStlinkModeAndEnsurePowerOn,
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
            await _setStlinkModeAndEnsurePowerOn(
                StlinkAfterTestModeSwitchDelayMs,
                PowerOnSettleDelayMs);

            await _startFlashing();
        }
    }
}
