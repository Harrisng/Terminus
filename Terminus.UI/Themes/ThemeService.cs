using System.Windows;
using Microsoft.Win32;

namespace Terminus.UI.Themes;

public enum AppTheme
{
    Dark,
    Light
}

public class ThemeService
{
    private readonly string _registryPath = @"SOFTWARE\Terminus";
    private AppTheme _currentTheme;

    public AppTheme CurrentTheme => _currentTheme;

    public event EventHandler? ThemeChanged;

    public ThemeService()
    {
        _currentTheme = LoadTheme();
        // Don't call ApplyTheme in constructor - DarkTheme is already loaded by App.xaml
        // ApplyTheme will be called when user toggles theme
    }

    public void SetTheme(AppTheme theme)
    {
        if (_currentTheme == theme)
            return;

        _currentTheme = theme;
        SaveTheme(theme);
        ApplyTheme(theme);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleTheme()
    {
        SetTheme(_currentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);
    }

    private AppTheme LoadTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_registryPath);
            var value = key?.GetValue("Theme") as string;
            return value switch
            {
                "Light" => AppTheme.Light,
                "Dark" => AppTheme.Dark,
                _ => AppTheme.Dark // Default to dark
            };
        }
        catch
        {
            return AppTheme.Dark;
        }
    }

    private void SaveTheme(AppTheme theme)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(_registryPath);
            key.SetValue("Theme", theme.ToString());
        }
        catch { }
    }

    private void ApplyTheme(AppTheme theme)
    {
        var app = Application.Current;
        if (app == null) return;

        var dictionaries = app.Resources.MergedDictionaries;

        // Remove existing theme dictionaries
        var toRemove = dictionaries
            .Where(d => d.Source != null && (
                d.Source.OriginalString.Contains("DarkTheme.xaml") ||
                d.Source.OriginalString.Contains("LightTheme.xaml")))
            .ToList();

        foreach (var dict in toRemove)
        {
            dictionaries.Remove(dict);
        }

        // Add new theme dictionary
        var themeUri = theme == AppTheme.Dark
            ? new Uri("Themes/DarkTheme.xaml", UriKind.Relative)
            : new Uri("Themes/LightTheme.xaml", UriKind.Relative);

        dictionaries.Add(new ResourceDictionary { Source = themeUri });
    }
}
