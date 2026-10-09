namespace FCatFlasher.Models
{
/// <summary>
/// Звіт виробничої коробки із діапазонами та списком серійників.
/// </summary>
    public class BoxReport
    {
        public int LocalId { get; set; }

        public bool IsSynced { get; set; }

        public int BoxNumber { get; set; }

        public int Quantity { get; set; }

        public string FirmwareName { get; set; } = string.Empty;

        public string SerialNumbersText { get; set; } = string.Empty;

        public string FirstSerial { get; set; } = string.Empty;

        public string LastSerial { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public string SerialRangeText => string.IsNullOrWhiteSpace(SerialNumbersText)
            ? $"{FirstSerial} - {LastSerial}"
            : SerialNumbersText;

        public string CreatedAtText => CreatedAt == default
            ? string.Empty
            : CreatedAt.ToString("dd.MM.yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
    }
}
