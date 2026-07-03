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
        private void FocusVisibleSerialTextBox()
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
