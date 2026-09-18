using FCatFlasher.Models;
using FCatFlasher.Services;
using FCatFlasher.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace FCatFlasher
{
/// <summary>
/// Головне WPF-вікно flasher-додатку.
/// </summary>
    public partial class MainWindow : Window
    {
        private int _lastSelectedTabIndex = 0;
        private bool _closePending;
        private bool _shutdownComplete;

        protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
            if (_shutdownComplete || e.Cancel)
                return;
            e.Cancel = true;
            if (_closePending)
                return;
            _closePending = true;
            IsEnabled = false;
            try
            {
                if (DataContext is MainViewModel vm)
                {
                    await vm.ShutdownAsync();
                    vm.SerialInputFocusRequested -= FocusVisibleSerialTextBox;
                }
                _shutdownComplete = true;
                _ = Dispatcher.BeginInvoke(new Action(Close));
            }
            catch (Exception ex)
            {
                _closePending = false;
                IsEnabled = true;
                MessageBox.Show(this, ex.Message, "Помилка зупинки");
            }
        }

        internal MainWindow(
            ApiService apiService,
            LocalDatabaseService localDatabaseService,
            CurrentUser currentUser)
        {
            InitializeComponent();
            DataContext = new MainViewModel(
                apiService,
                localDatabaseService,
                currentUser);
            ((MainViewModel)DataContext).SerialInputFocusRequested += FocusVisibleSerialTextBox;
            Loaded += async (_, _) => await ((MainViewModel)DataContext).InitializeAsync();
        }

/// <summary>
/// Автоматично прокручує лог до останнього рядка.
/// </summary>
        private void LogTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            LogTextBox.ScrollToEnd();
        }

/// <summary>
/// Повертає фокус у поле серійника після зміни тексту.
/// </summary>
        private void SerialTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox textBox ||
                !string.IsNullOrEmpty(textBox.Text))
                return;

            FocusVisibleSerialTextBox();
        }

/// <summary>
/// Фокусує поле серійника, коли воно знову стає доступним.
/// </summary>
        private void SerialTextBox_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is true)
                FocusVisibleSerialTextBox();
        }

/// <summary>
/// Фокусує видиме і доступне поле вводу серійного номера.
/// </summary>
        private void FocusVisibleSerialTextBox(bool selectAll = false)
        {
            Dispatcher.BeginInvoke(() =>
            {
                TextBox? serialTextBox = ProductionSerialTextBox.IsVisible
                    ? ProductionSerialTextBox
                    : ReflashSerialTextBox.IsVisible
                        ? ReflashSerialTextBox
                        : null;

                if (serialTextBox == null || !serialTextBox.IsEnabled)
                    return;

                serialTextBox.Focus();
                if (selectAll)
                    serialTextBox.SelectAll();
                else
                    serialTextBox.CaretIndex = serialTextBox.Text.Length;
            });
        }

/// <summary>
/// Передає ViewModel активну вкладку для перемикання футера і локальної таблиці.
/// </summary>
        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not TabControl tabControl)
                return;

            if (DataContext is not MainViewModel vm)
                return;

            // Забороняємо перемикання під час роботи
            if (vm.IsBusy)
            {
                tabControl.SelectedIndex = _lastSelectedTabIndex;
                return;
            }

            _lastSelectedTabIndex = tabControl.SelectedIndex;

            if (tabControl.SelectedItem is not TabItem tab)
                return;

            vm.SetFooterMode(tab.Header?.ToString());
        }
    }
}
