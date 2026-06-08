namespace FCatFlasher.Models
{
    class AppSettings
    {
        public string SelectedComPort { get; set; } = "";
        public string FirmwarePath { get; set; } = "";
        public string G0FirmwarePath { get; set; } = "";
        public string C0FirmwarePath { get; set; } = "";
    }
}
