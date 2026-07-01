namespace FCatFlasher.Services.Workflows
{
    internal sealed class ProductionFlashWorkflow
    {
        private const int PowerOffBeforeStlinkDelayMs = 300;
        private const int StlinkAfterTestModeSwitchDelayMs = 100;
        private const int PowerOnSettleDelayMs = 100;

        private readonly Func<string, int, bool, Task> _sendStandCommand;
        private readonly Func<int, int, Task> _setStlinkModeAndEnsurePowerOn;
        private readonly Func<Task> _startFlashing;

        public ProductionFlashWorkflow(
            Func<string, int, bool, Task> sendStandCommand,
            Func<int, int, Task> setStlinkModeAndEnsurePowerOn,
            Func<Task> startFlashing)
        {
            _sendStandCommand = sendStandCommand;
            _setStlinkModeAndEnsurePowerOn = setStlinkModeAndEnsurePowerOn;
            _startFlashing = startFlashing;
        }

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
