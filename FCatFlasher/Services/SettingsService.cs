using FCatFlasher.Models;
using System.IO;
using System.Text.Json;

namespace FCatFlasher.Services
{
    internal class SettingsService
    {
        private const string FileName = "settings.json";

        public AppSettings Load()
        {
            try
            {
                if (!File.Exists(FileName))
                    return new AppSettings();

                string json = File.ReadAllText(FileName);

                return JsonSerializer.Deserialize<AppSettings>(json)
                       ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

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

                File.WriteAllText(FileName, json);
            }
            catch
            {
            }
        }
    }
}