using FCatFlasher.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace FCatFlasher
{
    public partial class MainWindow : Window
    {
        private int _lastSelectedTabIndex = 0;
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
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

            switch (tab.Header?.ToString())
            {
                case "Тестова прошивка":
                    vm.FooterTitle = "Протестовано за сьогодні:";
                    break;

                case "Прошивка":
                    vm.FooterTitle = "Прошито за сьогодні:";
                    break;

                case "Перепрошивка":
                    vm.FooterTitle = "Перепрошито за сьогодні:";
                    break;
            }
        }
    }
}