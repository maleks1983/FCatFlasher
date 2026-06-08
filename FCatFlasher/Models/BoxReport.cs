namespace FCatFlasher.Models
{
    public class BoxReport
    {
        public int BoxNumber { get; set; }

        public int Quantity { get; set; }

        public string FirmwareName { get; set; } = string.Empty;

        public string FirstSerial { get; set; } = string.Empty;

        public string LastSerial { get; set; } = string.Empty;
        public string SerialRangeText => $"{FirstSerial} - {LastSerial}";
    }
}
