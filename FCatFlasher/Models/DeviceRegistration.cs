namespace FCatFlasher.Models
{
    class DeviceRegistration
    {
        public int Id { get; set; }
        public string SerialNumber { get; set; } = string.Empty;
        public string McuId { get; set; } = string.Empty;
        public string McuName { get; set; } = string.Empty;
        public string FirmwareName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
