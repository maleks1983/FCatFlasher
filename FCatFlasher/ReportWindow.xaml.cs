using FCatFlasher.Models;
using FCatFlasher.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace FCatFlasher
{
/// <summary>
/// Вікно локального звіту по виробничих або rework-прошивках.
/// </summary>
    public partial class ReportWindow : Window, INotifyPropertyChanged
    {
        private readonly LocalDatabaseService _localDatabaseService;
        private readonly bool _isReworkReport;
        private DateTime _reportFromDate = DateTime.Today;
        private DateTime _reportToDate = DateTime.Today;
        private string _reportStatus = "";

/// <summary>
/// Створює звітне вікно для вибраного типу звіту.
/// </summary>
        internal ReportWindow(LocalDatabaseService localDatabaseService, bool isReworkReport)
        {
            _localDatabaseService = localDatabaseService;
            _isReworkReport = isReworkReport;

            InitializeComponent();
            DataContext = this;
            OrderColumn.Visibility = OrderColumnVisibility;
            LoadReport();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string WindowTitle => _isReworkReport
            ? "Звіт перепрошивка"
            : "Звіт прошивка";

        public Visibility OrderColumnVisibility => _isReworkReport
            ? Visibility.Visible
            : Visibility.Collapsed;

        public ObservableCollection<FirmwareReportRow> Rows { get; } = new();

        public DateTime ReportFromDate
        {
            get => _reportFromDate;
            set
            {
                _reportFromDate = value;
                OnPropertyChanged();
            }
        }

        public DateTime ReportToDate
        {
            get => _reportToDate;
            set
            {
                _reportToDate = value;
                OnPropertyChanged();
            }
        }

        public string ReportStatus
        {
            get => _reportStatus;
            set
            {
                _reportStatus = value;
                OnPropertyChanged();
            }
        }

/// <summary>
/// Оновлює звіт за вибраний період.
/// </summary>
        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadReport();
        }

        private void LoadReport()
        {
            Rows.Clear();

            DateTime from = ReportFromDate.Date;
            DateTime to = ReportToDate.Date;

            if (to < from)
            {
                ReportStatus = "Дата 'до' менша за дату 'від'.";
                return;
            }

            try
            {
                IReadOnlyList<FirmwareReportRow> rows = _isReworkReport
                    ? _localDatabaseService.LoadReworkFirmwareReport(from, to)
                    : _localDatabaseService.LoadProductionFirmwareReport(from, to);

                foreach (FirmwareReportRow row in rows)
                    Rows.Add(row);

                int total = Rows.Sum(row => row.Quantity);
                ReportStatus = $"Всього: {total}";
            }
            catch (Exception ex)
            {
                ReportStatus = "Помилка звіту: " + ex.Message;
            }
        }

/// <summary>
/// Сповіщає WPF про зміну властивості вікна.
/// </summary>
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
