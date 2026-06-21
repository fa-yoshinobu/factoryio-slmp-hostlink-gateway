using System.Globalization;
using System.Windows;

namespace GatewayApp.Services;

public enum UiLanguage
{
    English,
    Japanese,
}

public static class Loc
{
    public static event EventHandler? LanguageChanged;

    public static UiLanguage CurrentLanguage { get; private set; } = UiLanguage.English;

    public static void SetLanguage(UiLanguage language)
    {
        CurrentLanguage = language;

        var app = Application.Current;
        if (app is not null)
        {
            var dictionaries = app.Resources.MergedDictionaries;
            for (var index = dictionaries.Count - 1; index >= 0; index--)
            {
                if (dictionaries[index].Source?.OriginalString.Contains("Resources/Strings.", StringComparison.OrdinalIgnoreCase) == true)
                {
                    dictionaries.RemoveAt(index);
                }
            }

            dictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(language == UiLanguage.Japanese
                    ? "/FactoryIOGateway;component/Resources/Strings.ja.xaml"
                    : "/FactoryIOGateway;component/Resources/Strings.en.xaml", UriKind.Relative),
            });
        }

        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string Text(string key)
    {
        EnsureLanguageDictionary();
        return Application.Current?.TryFindResource(key) as string ?? key;
    }

    public static string Format(string key, params object[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, Text(key), args);
    }

    private static void EnsureLanguageDictionary()
    {
        var app = Application.Current;
        if (app is null || app.Resources.MergedDictionaries.Any(IsLocalizationDictionary))
        {
            return;
        }

        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(CurrentLanguage == UiLanguage.Japanese
                ? "/FactoryIOGateway;component/Resources/Strings.ja.xaml"
                : "/FactoryIOGateway;component/Resources/Strings.en.xaml", UriKind.Relative),
        });
    }

    private static bool IsLocalizationDictionary(ResourceDictionary dictionary)
    {
        return dictionary.Source?.OriginalString.Contains("Resources/Strings.", StringComparison.OrdinalIgnoreCase) == true;
    }
}
