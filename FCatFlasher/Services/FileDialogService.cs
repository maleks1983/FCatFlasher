using Microsoft.Win32;

namespace FCatFlasher.Services
{
    /// <summary>
    /// Інкапсулює системні діалоги вибору файлів і папок.
    /// </summary>
    internal sealed class FileDialogService
    {
        public string? BrowseFirmwareFile()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Оберіть прошивку",
                Filter = "Firmware files (*.hex;*.bin)|*.hex;*.bin|HEX files (*.hex)|*.hex|BIN files (*.bin)|*.bin|All files (*.*)|*.*"
            };

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        public string? BrowseTestFirmwareDirectory(string currentDirectory)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Оберіть папку тестових прошивок",
                Multiselect = false
            };

            if (!string.IsNullOrWhiteSpace(currentDirectory))
                dialog.InitialDirectory = currentDirectory;

            return dialog.ShowDialog() == true ? dialog.FolderName : null;
        }
    }
}
