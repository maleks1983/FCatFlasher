namespace FCatFlasher.Services.Workflows
{
/// <summary>
/// Описує перехід від rework-тесту до rework-прошивки.
/// </summary>
    internal sealed class ReflashWorkflow
    {
        private const int PowerOffBeforeStlinkDelayMs = 200;
        private const int StlinkAfterTestModeSwitchDelayMs = 100;
        private const int PowerOnSettleDelayMs = 50;

        private readonly Func<string, int, bool, Task> _sendStandCommand;
        private readonly Func<Task> _startFlashing;

/// <summary>
/// Приймає делегати ViewModel для команд стенду і запуску прошивки.
/// </summary>
        public ReflashWorkflow(
            Func<string, int, bool, Task> sendStandCommand,
            Func<Task> startFlashing)
        {
            _sendStandCommand = sendStandCommand;
            _startFlashing = startFlashing;
        }

/// <summary>
/// Перемикає стенд у ST-LINK режим і стартує rework-прошивку.
/// </summary>
        public async Task FlashAfterSuccessfulTest()
        {
            await _sendStandCommand("POWER_OFF", PowerOffBeforeStlinkDelayMs, true);
            await _sendStandCommand("STLINK_ON", StlinkAfterTestModeSwitchDelayMs, true);
            await _sendStandCommand("POWER_ON", PowerOnSettleDelayMs, false);

            await _startFlashing();
        }
    }
}
