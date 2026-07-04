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
            : this(firmwareName, boxQuantity, 0, 0)
        {
        }

        public ProductionStartWindow(
            string firmwareName,
            int boxQuantity,
            int currentBoxNumber,
            int currentBoxCount)
        {
            InitializeComponent();

            FirmwareNameTextBox.Text = firmwareName;
            BoxQuantityTextBox.Text = boxQuantity > 0
                ? boxQuantity.ToString(CultureInfo.InvariantCulture)
                : string.Empty;

            if (currentBoxCount > 0)
            {
                ExistingBoxTextBlock.Text =
                    $"Є незавершена коробка №{currentBoxNumber}: {currentBoxCount} шт.";
                ExistingBoxPanel.Visibility = Visibility.Visible;
            }

            FirmwareNameTextBox.SelectAll();
            FirmwareNameTextBox.Focus();
        }

        public string FirmwareName { get; private set; } = string.Empty;

        public int BoxQuantity { get; private set; }

        public ProductionBoxStartMode StartMode { get; private set; }
            = ProductionBoxStartMode.ContinueExisting;

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
            StartMode = StartNewBoxRadioButton.IsChecked == true
                ? ProductionBoxStartMode.StartNew
                : ProductionBoxStartMode.ContinueExisting;
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

    public enum ProductionBoxStartMode
    {
        ContinueExisting,
        StartNew
    }
}
