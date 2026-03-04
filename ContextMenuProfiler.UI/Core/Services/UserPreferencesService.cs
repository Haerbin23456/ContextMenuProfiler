using System.Text.Json;
using System.IO;

namespace ContextMenuProfiler.UI.Core.Services
{
    public class UserPreferences
    {
        public string LanguageCode { get; set; } = "auto";
    }

    public static class UserPreferencesService
    {
        private static readonly string PreferencesDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ContextMenuProfiler");
        private static readonly string PreferencesPath = Path.Combine(PreferencesDirectory, "preferences.json");

        public static UserPreferences Load()
        {
            try
            {
                if (!File.Exists(PreferencesPath))
                {
                    return new UserPreferences();
                }

                string json = File.ReadAllText(PreferencesPath);
                var prefs = JsonSerializer.Deserialize<UserPreferences>(json);
                return prefs ?? new UserPreferences();
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning("Failed to load user preferences", ex);
                return new UserPreferences();
            }
        }

        public static void Save(UserPreferences preferences)
        {
            try
            {
                Directory.CreateDirectory(PreferencesDirectory);
                string json = JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(PreferencesPath, json);
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning("Failed to save user preferences", ex);
            }
        }
    }
}
