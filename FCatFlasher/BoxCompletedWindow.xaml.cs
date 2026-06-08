using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace FCatFlasher
{
    /// <summary>
    /// Логика взаимодействия для BoxCompletedWindow.xaml
    /// </summary>
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
