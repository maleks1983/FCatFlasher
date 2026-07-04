using System.Threading;

namespace FCatFlasher.Services
{
    /// <summary>
    /// Керує командами стенду, режимами UART/ST-LINK і локальним станом живлення.
    /// </summary>
    internal sealed class StandController(SerialService serial)
    {
        public const int CommandSettleDelayMs = 100;
        public const int UartPowerOffDelayMs = 100;
        public const int StlinkPowerOffDelayMs = 200;
        public const int PowerOnSettleDelayMs = 50;
        public const int UartPowerOnSettleDelayMs = 50;
        public const int UartModeSwitchDelayMs = 100;
        public const int StlinkModeSwitchDelayMs = 100;

        private readonly SemaphoreSlim _commandLock = new(1, 1);

        public bool IsPowerOn { get; private set; }

        /// <summary>
        /// Надсилає команду стенду з послідовним виконанням та контрольованою затримкою.
        /// </summary>
        public async Task SendCommand(
            string command,
            int settleDelayMs = CommandSettleDelayMs,
            bool enforceMinimumDelay = true)
        {
            await _commandLock.WaitAsync();

            try
            {
                if (!serial.IsOpen)
                    throw new InvalidOperationException($"COM port is closed. Command not sent: {command}");

                serial.Send(command);
                UpdatePowerState(command);

                int delayMs = enforceMinimumDelay
                    ? Math.Max(settleDelayMs, CommandSettleDelayMs)
                    : settleDelayMs;

                await Task.Delay(delayMs);
            }
            finally
            {
                _commandLock.Release();
            }
        }

        /// <summary>
        /// Перемикає стенд у UART режим.
        /// </summary>
        public async Task SetUartMode(int settleDelayMs = UartModeSwitchDelayMs)
        {
            await SendCommand("UART_ON", settleDelayMs);
        }

        /// <summary>
        /// Перемикає стенд у ST-LINK режим.
        /// </summary>
        public async Task SetStlinkMode(
            int settleDelayMs = StlinkModeSwitchDelayMs,
            bool enforceMinimumDelay = true)
        {
            await SendCommand("STLINK_ON", settleDelayMs, enforceMinimumDelay);
        }

        /// <summary>
        /// Перемикає стенд у UART режим і гарантує подане живлення для тесту.
        /// </summary>
        public async Task SetUartModeAndEnsurePowerOn(
            int powerOffDelayMs = UartPowerOffDelayMs,
            int modeSwitchDelayMs = UartModeSwitchDelayMs,
            int powerOnDelayMs = PowerOnSettleDelayMs)
        {
            await SendCommand("POWER_OFF", powerOffDelayMs);
            await SetUartMode(modeSwitchDelayMs);
            await SendCommand("POWER_ON", powerOnDelayMs, enforceMinimumDelay: false);
        }

        /// <summary>
        /// Перемикає комутацію на UART без вимкнення живлення плати.
        /// </summary>
        public async Task SetUartModeKeepingPowerOn(
            int modeSwitchDelayMs = UartModeSwitchDelayMs,
            int powerOnDelayMs = 0)
        {
            await SetUartMode(modeSwitchDelayMs);
            await SendCommand("POWER_ON", powerOnDelayMs, enforceMinimumDelay: false);
        }

        /// <summary>
        /// Перемикає стенд у ST-LINK режим і подає живлення перед прошивкою.
        /// </summary>
        public async Task SetStlinkModeAndEnsurePowerOn(
            int modeSwitchDelayMs = StlinkModeSwitchDelayMs,
            int powerOnDelayMs = UartPowerOnSettleDelayMs,
            bool enforceModeSwitchMinimumDelay = true)
        {
            bool powerAlreadyOn = IsPowerOn;
            await SetStlinkMode(modeSwitchDelayMs, enforceModeSwitchMinimumDelay);

            if (!powerAlreadyOn)
                await SendCommand("POWER_ON", powerOnDelayMs, enforceMinimumDelay: false);
        }

        /// <summary>
        /// Виконує power-cycle плати у ST-LINK режимі для відновлення зв'язку.
        /// </summary>
        public async Task PowerCycleToStlinkMode()
        {
            await SendCommand("POWER_OFF", StlinkPowerOffDelayMs);
            await SetStlinkModeAndEnsurePowerOn(
                StlinkModeSwitchDelayMs,
                PowerOnSettleDelayMs);
        }

        /// <summary>
        /// Подає живлення у ST-LINK режимі без попереднього power-cycle.
        /// </summary>
        public async Task PowerOnToStlinkMode()
        {
            await SetStlinkModeAndEnsurePowerOn();
        }

        private void UpdatePowerState(string command)
        {
            if (command == "POWER_ON")
                IsPowerOn = true;
            else if (command == "POWER_OFF")
                IsPowerOn = false;
        }
    }
}
