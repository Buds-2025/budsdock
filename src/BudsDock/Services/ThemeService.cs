using System.ComponentModel;
using System.Windows;
using BudsDock.Models;

namespace BudsDock.Services;

public sealed class ThemeService : INotifyPropertyChanged
{
    private const string DictionaryMarker = "Theme.";
    private int _revision;

    public bool IsDark { get; private set; } = true;
    public int Revision => _revision;
    public event PropertyChangedEventHandler? PropertyChanged;

    public void Apply(ThemeMode requestedMode)
    {
        IsDark = requestedMode switch
        {
            ThemeMode.Light => false,
            _ => true
        };

        var fileName = IsDark ? "Theme.Dark.xaml" : "Theme.Light.xaml";
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(dictionary => dictionary.Source?.OriginalString.Contains(DictionaryMarker, StringComparison.OrdinalIgnoreCase) == true);
        if (existing is not null)
        {
            dictionaries.Remove(existing);
        }
        dictionaries.Add(new ResourceDictionary { Source = new Uri($"Resources/{fileName}", UriKind.Relative) });
        _revision++;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Revision)));
    }

}
