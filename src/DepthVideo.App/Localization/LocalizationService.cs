using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace DepthVideo.App.Localization;

public sealed record LanguageOption(string Code, string DisplayName);

public static class LocalizationService
{
    private const string DefaultLanguage = "zh-CN";
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DepthVideoConverter",
        "settings.json");

    public static IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new("zh-CN", "简体中文"),
        new("en-US", "English"),
    ];

    public static string CurrentLanguage { get; private set; } = DefaultLanguage;

    public static void Initialize() => SetLanguage(LoadLanguage(), save: false);

    public static void SetLanguage(string languageCode, bool save = true)
    {
        if (!Languages.Any(language => language.Code == languageCode)) languageCode = DefaultLanguage;
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.Contains("Strings.", StringComparison.OrdinalIgnoreCase) == true);
        if (existing is not null) dictionaries.Remove(existing);

        dictionaries.Insert(0, new ResourceDictionary
        {
            Source = new Uri($"Resources/Strings.{languageCode}.xaml", UriKind.Relative),
        });
        CurrentLanguage = languageCode;
        var culture = CultureInfo.GetCultureInfo(languageCode);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        if (save) SaveLanguage(languageCode);
    }

    public static string Text(string key) =>
        Application.Current.TryFindResource(key) as string ?? key;

    public static string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Text(key), arguments);

    private static string LoadLanguage()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return DefaultLanguage;
            using var document = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            return document.RootElement.TryGetProperty("language", out var value)
                ? value.GetString() ?? DefaultLanguage
                : DefaultLanguage;
        }
        catch (JsonException)
        {
            return DefaultLanguage;
        }
    }

    private static void SaveLanguage(string languageCode)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new { language = languageCode }));
    }
}
