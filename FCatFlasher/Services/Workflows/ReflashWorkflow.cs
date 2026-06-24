namespace FCatFlasher.Services.Workflows
{
    internal sealed class ReflashWorkflow
    {
        private const int PowerOffBeforeStlinkDelayMs = 200;
        private const int StlinkAfterTestModeSwitchDelayMs = 200;
        private const int PowerOnSettleDelayMs = 200;

        private readonly Func<string, int, bool, Task> _sendStandCommand;
        private readonly Func<int, int, Task> _setStlinkModeAndEnsurePowerOn;
        private readonly Func<string, Task<bool>> _ensureStlinkReady;
        private readonly Func<Task> _startFlashing;

        public ReflashWorkflow(
            Func<string, int, bool, Task> sendStandCommand,
            Func<int, int, Task> setStlinkModeAndEnsurePowerOn,
            Func<string, Task<bool>> ensureStlinkReady,
            Func<Task> startFlashing)
        {
            _sendStandCommand = sendStandCommand;
            _setStlinkModeAndEnsurePowerOn = setStlinkModeAndEnsurePowerOn;
            _ensureStlinkReady = ensureStlinkReady;
            _startFlashing = startFlashing;
        }

        public async Task FlashAfterSuccessfulTest()
        {
            await _sendStandCommand("POWER_OFF", PowerOffBeforeStlinkDelayMs, true);
            await _setStlinkModeAndEnsurePowerOn(
                StlinkAfterTestModeSwitchDelayMs,
                PowerOnSettleDelayMs);

            if (!await _ensureStlinkReady("reflash firmware"))
                return;

            await _startFlashing();
        }
    }
}
