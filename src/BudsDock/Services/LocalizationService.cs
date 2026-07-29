using System.Globalization;
using System.Windows;
using BudsDock.Models;

namespace BudsDock.Services;

public sealed class LocalizationService
{
    private const string DictionaryMarker = "Strings.";
    private readonly AppLanguage _systemLanguage = CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
        ? AppLanguage.ChineseSimplified
        : AppLanguage.English;

    public AppLanguage CurrentLanguage { get; private set; } = AppLanguage.ChineseSimplified;

    public void Apply(AppLanguage requestedLanguage)
    {
        CurrentLanguage = Resolve(requestedLanguage);
        var fileName = CurrentLanguage == AppLanguage.English ? "Strings.en-US.xaml" : "Strings.zh-CN.xaml";
        ReplaceDictionary(DictionaryMarker, $"Resources/{fileName}");
        CultureInfo.CurrentUICulture = CurrentLanguage == AppLanguage.English
            ? CultureInfo.GetCultureInfo("en-US")
            : CultureInfo.GetCultureInfo("zh-CN");
    }

    public string Translate(string key)
        => Application.Current.TryFindResource(key) as string ?? key;

    public string DisplayName(DockItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.BuiltInNameKey))
        {
            return Translate(item.BuiltInNameKey);
        }

        if (CurrentLanguage == AppLanguage.English && !string.IsNullOrWhiteSpace(item.NameEn))
        {
            return item.NameEn;
        }

        return string.IsNullOrWhiteSpace(item.Name) ? Path.GetFileNameWithoutExtension(item.TargetPath) : item.Name;
    }

    private AppLanguage Resolve(AppLanguage language)
    {
        return language == AppLanguage.System ? _systemLanguage : language;
    }

    private static void ReplaceDictionary(string marker, string source)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(dictionary => dictionary.Source?.OriginalString.Contains(marker, StringComparison.OrdinalIgnoreCase) == true);
        if (existing is not null)
        {
            dictionaries.Remove(existing);
        }
        dictionaries.Add(new ResourceDictionary { Source = new Uri(source, UriKind.Relative) });
    }
}
