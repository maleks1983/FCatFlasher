namespace FCatFlasher.Models
{
    class AppSettings
    {
        public string SelectedComPort { get; set; } = "";
        public string FirmwarePath { get; set; } = "";
        public string G0FirmwarePath { get; set; } = "";
        public string C0FirmwarePath { get; set; } = "";
        public string TestFirmwareDirectory { get; set; } = @"D:\";
        public string StmCubeCliPath { get; set; } =
            @"E:\InicaalDG\STM32CubeProgrammer\bin\STM32_Programmer_CLI.exe";
    }
}
