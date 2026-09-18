using System.Globalization;

namespace FCatFlasher.Models
{
/// <summary>
/// Агрегований підсумок локальних rework-записів за замовленням.
/// </summary>
    public class ReworkSummary
    {
        public string OrderId { get; set; } = string.Empty;

        public string FirmwareName { get; set; } = string.Empty;

        public string ProductName { get; set; } = string.Empty;

        public int Quantity { get; set; }

        public DateTime FirstCreatedAt { get; set; }

        public DateTime LastCreatedAt { get; set; }

        public string DateRangeText
        {
            get
            {
                string first = FirstCreatedAt.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
                string last = LastCreatedAt.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

                return first == last ? first : $"{first} - {last}";
            }
        }
    }
}
