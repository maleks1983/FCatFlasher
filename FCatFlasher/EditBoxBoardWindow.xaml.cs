using System.Windows;

namespace FCatFlasher
{
    public partial class EditBoxBoardWindow : Window
    {
        public string OldSerial => OldSerialTextBox.Text.Trim();
        public string NewSerial => NewSerialTextBox.Text.Trim();

        public EditBoxBoardWindow(int boxNumber, bool replace)
        {
            InitializeComponent();
            Title = replace ? $"Заміна плати — коробка № {boxNumber}" : $"Додавання плати — коробка № {boxNumber}";
            DescriptionText.Text = replace
                ? $"Знайдіть стару плату в коробці № {boxNumber} та вкажіть серійний номер нової."
                : $"Додайте нову плату до завершеної коробки № {boxNumber}.";
            OldSerialPanel.Visibility = replace ? Visibility.Visible : Visibility.Collapsed;

            Loaded += (_, _) =>
            {
                TextBoxToFocus(replace).Focus();
            };
        }

        public void SetReplacementDescription(string text)
        {
            Title = "Заміна бракованої плати";
            DescriptionText.Text = text;
        }

        public void SetMcuReplacementMode()
        {
            Title = "Заміна бракованої плати";
            DescriptionText.Text = "Введіть серійний номер плати. Після успішної тестової прошивки номер плати залишиться без змін, а MCU ID буде записано з нового контролера.";
            OldSerialLabel.Text = "Серійний номер плати";
            NewSerialPanel.Visibility = Visibility.Collapsed;
        }

        private System.Windows.Controls.TextBox TextBoxToFocus(bool replace)
            => replace ? OldSerialTextBox : NewSerialTextBox;

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if ((OldSerialPanel.Visibility == Visibility.Visible && OldSerial.Length == 0) ||
                (NewSerialPanel.Visibility == Visibility.Visible && NewSerial.Length == 0))
            {
                MessageBox.Show("Заповніть серійні номери.", "Редагування коробки",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
