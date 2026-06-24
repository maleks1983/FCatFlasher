namespace FCatFlasher.Services.Workflows
{
    internal sealed class ProductionFlashWorkflow
    {
        private const int PowerOffBeforeStlinkDelayMs = 300;
        private const int StlinkModeSwitchDelayMs = 500;

        private readonly Func<string, int, bool, Task> _sendStandCommand;
        private readonly Func<int, Task> _setStlinkMode;
        private readonly Func<Task> _startFlashing;

        public ProductionFlashWorkflow(
            Func<string, int, bool, Task> sendStandCommand,
            Func<int, Task> setStlinkMode,
            Func<Task> startFlashing)
        {
            _sendStandCommand = sendStandCommand;
            _setStlinkMode = setStlinkMode;
            _startFlashing = startFlashing;
        }

        public async Task FlashAfterSuccessfulTest()
        {
            await _sendStandCommand("POWER_OFF", PowerOffBeforeStlinkDelayMs, true);
            await _setStlinkMode(StlinkModeSwitchDelayMs);

            await _startFlashing();
        }
    }
}
