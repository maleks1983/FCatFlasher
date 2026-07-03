using System.Windows;

namespace FCatFlasher
{
/// <summary>
/// Діалог введення номера rework-замовлення та назви прошивки.
/// </summary>
    public partial class OrderIdWindow : Window
    {
        public OrderIdWindow(string orderId = "", string firmwareName = "")
        {
            InitializeComponent();

            OrderIdTextBox.Text = orderId;
            FirmwareNameTextBox.Text = firmwareName;
            OrderIdTextBox.SelectAll();
            OrderIdTextBox.Focus();
        }

        public string OrderId { get; private set; } = string.Empty;

        public string FirmwareName { get; private set; } = string.Empty;

/// <summary>
/// Валідує номер замовлення і повертає введені значення.
/// </summary>
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            string orderId = OrderIdTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(orderId))
            {
                ErrorTextBlock.Text = "Введіть ID замовлення.";
                OrderIdTextBox.Focus();
                return;
            }

            OrderId = orderId;
            FirmwareName = FirmwareNameTextBox.Text.Trim();
            DialogResult = true;
        }

/// <summary>
/// Закриває діалог без зміни даних.
/// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
