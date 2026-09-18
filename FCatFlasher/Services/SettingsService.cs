using FCatFlasher.Models;
using System.IO;
using System.Text.Json;

namespace FCatFlasher.Services
{
/// <summary>
/// Читає і записує користувацькі налаштування у профілі поточного користувача.
/// </summary>
    internal class SettingsService
    {
        private const string FileName = "settings.json";
        private static readonly string SettingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FCatFlasher");
        private static readonly string SettingsPath = Path.Combine(SettingsDirectory, FileName);

        public AppSettings Load()
        {
            try
            {
                MigrateLegacySettings();

                if (!File.Exists(SettingsPath))
                    return new AppSettings();

                string json = File.ReadAllText(SettingsPath);

                return JsonSerializer.Deserialize<AppSettings>(json)
                       ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

/// <summary>
/// Зберігає налаштування у JSON-файл у локальному профілі користувача.
/// </summary>
        public void Save(AppSettings settings)
        {
            try
            {
                string json = JsonSerializer.Serialize(
                    settings,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                Directory.CreateDirectory(SettingsDirectory);
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
            }
        }

        private static void MigrateLegacySettings()
        {
            if (File.Exists(SettingsPath) || !File.Exists(FileName))
                return;

            Directory.CreateDirectory(SettingsDirectory);
            File.Copy(FileName, SettingsPath, overwrite: false);
        }
    }
}
