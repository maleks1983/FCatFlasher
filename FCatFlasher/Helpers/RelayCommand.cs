using System.Windows.Input;

namespace FCatFlasher.Helpers
{
/// <summary>
/// ICommand-обгортка для кнопок без параметрів.
/// </summary>
    internal class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

/// <summary>
/// Повертає доступність команди для WPF binding.
/// </summary>
        public bool CanExecute(object? parameter)
            => canExecute == null || canExecute();

        public void Execute(object? parameter)
            => execute();

/// <summary>
/// Повідомляє WPF, що доступність команди змінилась.
/// </summary>
        public void RaiseCanExecuteChanged()
            => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// ICommand-обгортка для кнопок або меню з параметром.
    /// </summary>
    internal class RelayCommand<T>(Action<T?> execute, Func<T?, bool>? canExecute = null) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
            => canExecute == null || canExecute((T?)parameter);

        public void Execute(object? parameter)
            => execute((T?)parameter);

        public void RaiseCanExecuteChanged()
            => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
