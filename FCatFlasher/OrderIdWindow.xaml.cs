using FCatFlasher.Services;
using System.Windows;
using System.Windows.Controls;

namespace FCatFlasher
{
    public partial class OrderIdWindow : Window
    {
        internal OrderIdWindow(IReadOnlyList<ReworkOrderInfo> orders, string orderId = "", string firmwareName = "")
        {
            InitializeComponent();
            OrderComboBox.ItemsSource = orders;
            OrderComboBox.SelectedItem = orders.FirstOrDefault(order =>
                    order.RegistrationOrderId == orderId || order.OrderId == orderId)
                ?? orders.FirstOrDefault();

            OrderComboBox.Focus();
        }

        public string OrderId { get; private set; } = string.Empty;
        public string FirmwareName { get; private set; } = string.Empty;
        public string ProductName { get; private set; } = string.Empty;
        public int Quantity { get; private set; }
        public string C0FirmwareName { get; private set; } = string.Empty;
        public string G0FirmwareName { get; private set; } = string.Empty;
        public long? C0FirmwareId { get; private set; }
        public long? G0FirmwareId { get; private set; }

        private void OrderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (OrderComboBox.SelectedItem is not ReworkOrderInfo order)
            {
                ProductNameTextBox.Text = string.Empty;
                C0FirmwareTextBox.Text = string.Empty;
                G0FirmwareTextBox.Text = string.Empty;
                return;
            }

            ProductNameTextBox.Text = order.ProductName;
            C0FirmwareTextBox.Text = order.C0FirmwareName;
            G0FirmwareTextBox.Text = order.G0FirmwareName;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (OrderComboBox.SelectedItem is not ReworkOrderInfo order)
            {
                ErrorTextBlock.Text = "Оберіть задачу на перепрошивку.";
                OrderComboBox.Focus();
                return;
            }

            OrderId = order.RegistrationOrderId;
            ProductName = order.ProductName;
            Quantity = order.Quantity;
            FirmwareName = order.FirmwareName;
            C0FirmwareName = order.C0FirmwareName;
            G0FirmwareName = order.G0FirmwareName;
            C0FirmwareId = order.C0FirmwareId;
            G0FirmwareId = order.G0FirmwareId;
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
