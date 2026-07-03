using System.Globalization;

namespace FCatFlasher.Models
{
/// <summary>
/// Локально збережена реєстрація плати, що очікує синхронізації.
/// </summary>
    public class PendingRegistration
    {
        public string OrderId { get; set; } = string.Empty;

        public string SerialNumber { get; set; } = string.Empty;

        public string FirmwareName { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public string McuName { get; set; } = string.Empty;

        public string CreatedAtText => CreatedAt.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture);
    }
}
