using FCatFlasher.Models;
using FCatFlasher.Services;
using FCatFlasher.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace FCatFlasher
{
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

        private void LogTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            LogTextBox.ScrollToEnd();
        }

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
