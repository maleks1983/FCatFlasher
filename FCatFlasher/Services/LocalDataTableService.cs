using FCatFlasher.Models;
using System.Globalization;

namespace FCatFlasher.Services
{
    /// <summary>
    /// Формує рядки та заголовки таблиці локальних даних для головного вікна.
    /// </summary>
    internal sealed class LocalDataTableService(
        LocalDatabaseService localDatabaseService,
        BoxTrackingService boxTracking)
    {
        public LocalDataTableSnapshot BuildTestFirmware()
        {
            return new LocalDataTableSnapshot(
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                "У тестовій прошивці локальні дані не записуються.",
                Array.Empty<LocalDataRow>());
        }

        public LocalDataTableSnapshot BuildReflash(
            DateTime from,
            IReadOnlyList<ReworkSummary>? globalSummaries)
        {
            var rows = new List<LocalDataRow>();

            IReadOnlyList<ReworkSummary> localSummaries =
                localDatabaseService.LoadPendingReworkSummariesFrom(from);

            List<ReworkSummary> summaries = globalSummaries?.ToList() ?? new();
            var globalKeys = summaries
                .Select(summary => BuildSummaryKey(summary.OrderId, summary.ProductName))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            summaries.AddRange(localSummaries.Where(summary =>
                !globalKeys.Contains(BuildSummaryKey(summary.OrderId, summary.ProductName))));

            summaries = summaries
                .OrderByDescending(summary => summary.LastCreatedAt)
                .ToList();

            foreach (ReworkSummary summary in summaries)
            {
                rows.Add(new LocalDataRow
                {
                    Column1 = GetLastOrderDigits(summary.OrderId),
                    Column2 = summary.ProductName,
                    Column3 = summary.Quantity.ToString(CultureInfo.InvariantCulture),
                    Column4 = summary.DateRangeText,
                    KeyOrderId = summary.OrderId,
                    KeyFirmwareName = summary.ProductName,
                    CanDelete = true
                });
            }

            return new LocalDataTableSnapshot(
                "Зам.",
                "Продукт",
                "К-сть",
                "Дати",
                string.Empty,
                rows.Count > 0 ? string.Empty : "Немає локальних даних перепрошивки.",
                rows);
        }

        public LocalDataTableSnapshot BuildProduction(
            DateTime from,
            bool productionBoxActive,
            int boxQuantity,
            string firmwareName)
        {
            var rows = new List<LocalDataRow>();

            if (productionBoxActive)
                AddCurrentProductionBoxRow(rows, boxQuantity, firmwareName);

            foreach (BoxReport report in localDatabaseService.LoadPendingBoxReportsFrom(from))
            {
                rows.Add(new LocalDataRow
                {
                    Column1 = report.BoxNumber.ToString(CultureInfo.InvariantCulture),
                    Column2 = FormatSerialRangesForDisplay(report.SerialRangeText),
                    Column3 = report.Quantity.ToString(CultureInfo.InvariantCulture),
                    Column4 = report.FirmwareName
                });
            }

            return new LocalDataTableSnapshot(
                "ID коробки",
                "Діапазон",
                "К-сть",
                "Прошивка",
                string.Empty,
                rows.Count > 0 ? string.Empty : "Немає локальних коробок.",
                rows);
        }

        private void AddCurrentProductionBoxRow(
            List<LocalDataRow> rows,
            int boxQuantity,
            string firmwareName)
        {
            BoxReport? currentBox = boxTracking.GetCurrentBoxReport(boxQuantity, firmwareName);

            if (currentBox == null || currentBox.Quantity >= boxQuantity)
                return;

            rows.Add(new LocalDataRow
            {
                Column1 = currentBox.BoxNumber.ToString(CultureInfo.InvariantCulture),
                Column2 = string.Empty,
                Column3 = currentBox.Quantity.ToString(CultureInfo.InvariantCulture),
                Column4 = currentBox.FirmwareName
            });
        }

        private static string FormatSerialRangesForDisplay(string serialRanges)
        {
            return serialRanges.Replace(", ", Environment.NewLine);
        }

        private static string GetLastOrderDigits(string orderId)
        {
            string digits = new(orderId.Where(char.IsDigit).ToArray());

            if (digits.Length <= 4)
                return digits;

            return digits[^4..];
        }

        private static string BuildSummaryKey(string orderId, string firmwareName)
        {
            return $"{orderId.Trim()}\n{firmwareName.Trim()}";
        }
    }

    internal sealed record LocalDataTableSnapshot(
        string Header1,
        string Header2,
        string Header3,
        string Header4,
        string Header5,
        string EmptyText,
        IReadOnlyList<LocalDataRow> Rows);
}
