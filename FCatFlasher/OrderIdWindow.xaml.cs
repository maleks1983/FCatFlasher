using System.Windows;

namespace FCatFlasher
{
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

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
