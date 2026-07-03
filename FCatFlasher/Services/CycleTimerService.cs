using System.Globalization;
using System.Windows.Threading;

namespace FCatFlasher.Services
{
    /// <summary>
    /// Веде таймер виконання режиму і час обробки поточної плати.
    /// </summary>
    internal sealed class CycleTimerService
    {
        private readonly DispatcherTimer _runTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private DateTime _runStartedAt;
        private DateTime? _currentBoardStartedAt;
        private bool _productionBoxTimerStarted;

        public CycleTimerService()
        {
            _runTimer.Tick += (_, _) => NotifyElapsed();
        }

        public event Action<string>? RunElapsedChanged;

        public bool IsRunning => _runTimer.IsEnabled;

        public void MarkBoardStarted()
        {
            _currentBoardStartedAt = DateTime.Now;
        }

        public void MarkProductionBoxTimerStarted()
        {
            _productionBoxTimerStarted = true;
        }

        public void ResetBoardCycle()
        {
            _currentBoardStartedAt = null;
        }

        public void StartRunTimer()
        {
            ResetRunTimer();
            _runTimer.Start();
        }

        public void StopRunTimer()
        {
            _runTimer.Stop();
        }

        public void ResetRunTimer()
        {
            _runStartedAt = DateTime.Now;
            RunElapsedChanged?.Invoke("00:00:00");
        }

        public void ResetProductionBoxTimer()
        {
            _productionBoxTimerStarted = false;
            StopRunTimer();
            ResetRunTimer();
        }

        public bool StartProductionBoxTimerIfNeeded(bool isProductionMode)
        {
            if (!isProductionMode || _productionBoxTimerStarted)
                return false;

            _productionBoxTimerStarted = true;
            _currentBoardStartedAt ??= DateTime.Now;
            StartRunTimer();
            return true;
        }

        public TimeSpan? FinishBoardCycle()
        {
            if (_currentBoardStartedAt == null)
                return null;

            TimeSpan elapsed = DateTime.Now - _currentBoardStartedAt.Value;
            _currentBoardStartedAt = null;
            return elapsed;
        }

        private void NotifyElapsed()
        {
            TimeSpan elapsed = DateTime.Now - _runStartedAt;
            RunElapsedChanged?.Invoke(elapsed.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture));
        }
    }
}
