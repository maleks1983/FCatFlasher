namespace FCatFlasher.Services.Workflows
{
    internal sealed class ReflashWorkflow
    {
        private const int PowerOffBeforeStlinkDelayMs = 200;
        private const int StlinkAfterTestModeSwitchDelayMs = 100;
        private const int PowerOnSettleDelayMs = 50;

        private readonly Func<string, int, bool, Task> _sendStandCommand;
        private readonly Func<Task> _startFlashing;

        public ReflashWorkflow(
            Func<string, int, bool, Task> sendStandCommand,
            Func<Task> startFlashing)
        {
            _sendStandCommand = sendStandCommand;
            _startFlashing = startFlashing;
        }

        public async Task FlashAfterSuccessfulTest()
        {
            await _sendStandCommand("POWER_OFF", PowerOffBeforeStlinkDelayMs, true);
            await _sendStandCommand("STLINK_ON", StlinkAfterTestModeSwitchDelayMs, true);
            await _sendStandCommand("POWER_ON", PowerOnSettleDelayMs, false);

            await _startFlashing();
        }
    }
}
