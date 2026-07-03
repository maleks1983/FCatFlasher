using System.Windows;

namespace FCatFlasher
{
/// <summary>
/// Діалог дії оператора після заповнення коробки.
/// </summary>
    public partial class BoxCompletedWindow : Window
    {
        public BoxCompletedResult Result { get; private set; }
            = BoxCompletedResult.Continue;

/// <summary>
/// Створює вікно завершеної коробки.
/// </summary>
        public BoxCompletedWindow()
        {
            InitializeComponent();
        }

        private void Print_Click(object sender, RoutedEventArgs e)
        {
            Result = BoxCompletedResult.Print;
            DialogResult = true;
        }

/// <summary>
/// Повертає команду продовжити наступну коробку.
/// </summary>
        private void Continue_Click(object sender, RoutedEventArgs e)
        {
            Result = BoxCompletedResult.Continue;
            DialogResult = true;
        }

/// <summary>
/// Повертає команду завершити виробничий цикл.
/// </summary>
        private void Finish_Click(object sender, RoutedEventArgs e)
        {
            Result = BoxCompletedResult.Finish;
            DialogResult = true;
        }
    }

/// <summary>
/// Результат вибору оператора у вікні завершеної коробки.
/// </summary>
    public enum BoxCompletedResult
    {
        Print,
        Continue,
        Finish
    }
}
