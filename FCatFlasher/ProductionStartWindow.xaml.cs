using System.Globalization;
using System.Windows;

namespace FCatFlasher
{
/// <summary>
/// Діалог введення виробничої назви прошивки та кількості у коробці.
/// </summary>
    public partial class ProductionStartWindow : Window
    {
        public ProductionStartWindow(string firmwareName = "", int boxQuantity = 0)
        {
            InitializeComponent();

            FirmwareNameTextBox.Text = firmwareName;
            BoxQuantityTextBox.Text = boxQuantity > 0
                ? boxQuantity.ToString(CultureInfo.InvariantCulture)
                : string.Empty;

            FirmwareNameTextBox.SelectAll();
            FirmwareNameTextBox.Focus();
        }

        public string FirmwareName { get; private set; } = string.Empty;

        public int BoxQuantity { get; private set; }

/// <summary>
/// Валідує стартові параметри виробництва і закриває діалог при успіху.
/// </summary>
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            string firmwareName = FirmwareNameTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(firmwareName))
            {
                ErrorTextBlock.Text = "Введіть назву прошивки.";
                FirmwareNameTextBox.Focus();
                return;
            }

            if (!int.TryParse(BoxQuantityTextBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int boxQuantity) ||
                boxQuantity <= 0)
            {
                ErrorTextBlock.Text = "Введіть кількість в коробці.";
                BoxQuantityTextBox.Focus();
                return;
            }

            FirmwareName = firmwareName;
            BoxQuantity = boxQuantity;
            DialogResult = true;
        }

/// <summary>
/// Закриває діалог без старту виробництва.
/// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
