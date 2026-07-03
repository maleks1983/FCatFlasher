namespace FCatFlasher.Models
{
/// <summary>
/// Набір користувацьких налаштувань, які зберігаються в settings.json.
/// </summary>
    class AppSettings
    {
        public string SelectedComPort { get; set; } = "";
        public string FirmwarePath { get; set; } = "";
        public string FirmwareName { get; set; } = "";
        public string ReworkFirmwareName { get; set; } = "";
        public int BoxQuantity { get; set; }
        public string G0FirmwarePath { get; set; } = "";
        public string C0FirmwarePath { get; set; } = "";
        public string ReworkOrderId { get; set; } = "";
        public string TestFirmwareDirectory { get; set; } = @"D:\";
        public string ApiBaseUrl { get; set; } = "http://localhost:8080";
        public bool UseOptimizedProductionFlashMode { get; set; } = true;
    }
}
