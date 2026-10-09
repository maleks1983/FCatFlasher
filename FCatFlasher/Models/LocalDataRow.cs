namespace FCatFlasher.Models
{
/// <summary>
/// Універсальний рядок таблиці локальних даних у головному вікні.
/// </summary>
    public class LocalDataRow
    {
        public string Column1 { get; set; } = string.Empty;

        public string Column2 { get; set; } = string.Empty;

        public string Column3 { get; set; } = string.Empty;

        public string Column4 { get; set; } = string.Empty;

        public string Column5 { get; set; } = string.Empty;

        public string KeyOrderId { get; set; } = string.Empty;

        public string KeyFirmwareName { get; set; } = string.Empty;

        public bool CanDelete { get; set; }

        public int KeyBoxReportId { get; set; }

        public bool CanEditBox { get; set; }
    }
}
