using System.Windows;

namespace FCatFlasher
{
    public partial class BoxCompletedWindow : Window
    {
        public BoxCompletedResult Result { get; private set; }
            = BoxCompletedResult.Continue;

        public BoxCompletedWindow()
        {
            InitializeComponent();
        }

        private void Print_Click(object sender, RoutedEventArgs e)
        {
            Result = BoxCompletedResult.Print;
            DialogResult = true;
        }

        private void Continue_Click(object sender, RoutedEventArgs e)
        {
            Result = BoxCompletedResult.Continue;
            DialogResult = true;
        }

        private void Finish_Click(object sender, RoutedEventArgs e)
        {
            Result = BoxCompletedResult.Finish;
            DialogResult = true;
        }
    }

    public enum BoxCompletedResult
    {
        Print,
        Continue,
        Finish
    }
}
