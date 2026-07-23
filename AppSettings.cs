using System.Text.Json;
using Microsoft.Win32;

namespace BluetoothAudioRelay;

internal enum ThemePreference
{
    System,
    SunCycle,
    Light,
    Dark
}

internal sealed class UserPreferences
{
    public int SchemaVersion { get; set; } = 3;

    public ThemePreference ThemePreference { get; set; } = ThemePreference.System;

    public string AccentKey { get; set; } = AccentPalettes.Default.Key;

    public string? PreferredDeviceKey { get; set; }

    public string? PreferredDeviceName { get; set; }

    public bool PreferredDeviceNeedsProfileRecovery { get; set; }

    public bool AutoConnectEnabled { get; set; } = true;

    public bool StartWithWindows { get; set; }

    public bool QuietNotifications { get; set; }

    public DateTime? LastUpdateCheckUtc { get; set; }
}

internal static class UserPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string SettingsPath
    {
        get
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BluetoothAudioRelay");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "settings.json");
        }
    }

    public static UserPreferences Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new UserPreferences();
            }

            return JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(SettingsPath)) ?? new UserPreferences();
        }
        catch
        {
            return new UserPreferences();
        }
    }

    public static void Save(UserPreferences preferences)
    {
        try
        {
            var settingsPath = SettingsPath;
            var temporaryPath = settingsPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(preferences, JsonOptions));
            File.Move(temporaryPath, settingsPath, overwrite: true);
        }
        catch
        {
        }
    }
}

internal static class ThemeResolver
{
    public static bool ResolveDarkMode(ThemePreference preference)
    {
        return preference switch
        {
            ThemePreference.Light => false,
            ThemePreference.Dark => true,
            ThemePreference.SunCycle => IsNightByLocalClock(),
            _ => IsWindowsAppThemeDark()
        };
    }

    private static bool IsWindowsAppThemeDark()
    {
        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                1);
            return value is int intValue && intValue == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsNightByLocalClock()
    {
        var now = DateTime.Now.TimeOfDay;
        return now < new TimeSpan(6, 30, 0) || now >= new TimeSpan(18, 30, 0);
    }
}
