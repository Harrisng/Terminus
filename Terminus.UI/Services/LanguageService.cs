using System.Globalization;
using System.Windows;
using Terminus.Core.Services;

namespace Terminus.UI.Services;

/// <summary>
/// 語言代碼常數。
/// </summary>
public static class LanguageCodes
{
    public const string TraditionalChinese = "zh-TW";
    public const string English = "en-US";
    public const string SimplifiedChinese = "zh-CN";
}

/// <summary>
/// 當地語系化服務。鏡像 ThemeService 的動態字典切換模式：
/// 切換語言時移除舊字典、加入新字典到 Application.Resources.MergedDictionaries。
/// 同時實作 ILocalizationService 供 Core 專案使用。
/// </summary>
public class LanguageService : ILocalizationService
{
    private const string RegistryPath = @"SOFTWARE\Terminus";
    private const string RegistryKey = "Language";

    private string _currentLanguage = LanguageCodes.TraditionalChinese;

    /// <summary>目前使用中的語言代碼。</summary>
    public string CurrentLanguage => _currentLanguage;

    /// <summary>語言切換後觸發。訂閱者可重新整理動態文字。</summary>
    public event EventHandler? LanguageChanged;

    /// <summary>支援的語言清單。</summary>
    public static readonly string[] SupportedLanguages =
    {
        LanguageCodes.TraditionalChinese,
        LanguageCodes.English,
        LanguageCodes.SimplifiedChinese
    };

    /// <summary>
    /// 啟動時呼叫。讀取 registry 設定；若無則偵測系統語言並把結果固化回 registry。
    /// </summary>
    public void Initialize()
    {
        var saved = LoadFromRegistry();
        if (!string.IsNullOrEmpty(saved) && IsSupported(saved))
        {
            ApplyLanguage(saved);
        }
        else
        {
            var detected = DetectSystemLanguage();
            ApplyLanguage(detected);
            SaveToRegistry(detected);  // 首次自動偵測後固化
        }
    }

    /// <summary>
    /// 切換語言。即時生效，無需重啟。
    /// </summary>
    public void ApplyLanguage(string code)
    {
        if (!IsSupported(code))
        {
            LoggerService.Warn($"LanguageService: 不支援的語言代碼 {code}，退回預設");
            code = LanguageCodes.TraditionalChinese;
        }

        if (_currentLanguage == code && Application.Current?.Resources != null)
            return;

        _currentLanguage = code;
        SwapDictionary(code);
        LoggerService.Info($"LanguageService: 套用語言 {code}");
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>切換語言並持久化到 registry。</summary>
    public void SetLanguage(string code)
    {
        ApplyLanguage(code);
        SaveToRegistry(code);
    }

    /// <summary>
    /// 偵測系統語言，對照到支援清單。未匹配者回退到繁體中文。
    /// </summary>
    public static string DetectSystemLanguage()
    {
        try
        {
            var culture = CultureInfo.InstalledUICulture ?? CultureInfo.CurrentUICulture;
            var name = culture.Name;  // e.g. "zh-TW", "en-US", "zh-CN"

            if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                if (name.Contains("CN") || name.Contains("SG"))
                    return LanguageCodes.SimplifiedChinese;
                // zh-TW, zh-HK, zh-MO, zh-*, 都視為繁中
                return LanguageCodes.TraditionalChinese;
            }
            if (name.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                return LanguageCodes.English;

            // 其他語言暫不支援，退回繁中
            return LanguageCodes.TraditionalChinese;
        }
        catch (Exception ex)
        {
            LoggerService.Warn($"LanguageService: 偵測系統語言失敗: {ex.Message}");
            return LanguageCodes.TraditionalChinese;
        }
    }

    /// <inheritdoc/>
    public string Get(string key, params object[] args)
    {
        try
        {
            var app = Application.Current;
            var template = app?.TryFindResource(key) as string;
            if (template == null)
            {
                LoggerService.Warn($"LanguageService: 找不到資源 key {key}");
                return $"[{key}]";
            }
            return args.Length > 0 ? string.Format(template, args) : template;
        }
        catch (Exception ex)
        {
            LoggerService.Warn($"LanguageService: Get({key}) 失敗: {ex.Message}");
            return $"[{key}]";
        }
    }

    // ── 內部輔助 ──

    private static bool IsSupported(string code) =>
        Array.IndexOf(SupportedLanguages, code) >= 0;

    private static void SwapDictionary(string code)
    {
        var app = Application.Current;
        if (app == null) return;

        var dictionaries = app.Resources.MergedDictionaries;

        // 移除舊的語言字典
        var toRemove = dictionaries
            .Where(d => d.Source != null && d.Source.OriginalString.Contains("Strings/Strings."))
            .ToList();
        foreach (var dict in toRemove)
            dictionaries.Remove(dict);

        // 加入新字典
        var uri = new Uri($"Strings/Strings.{code}.xaml", UriKind.Relative);
        dictionaries.Add(new ResourceDictionary { Source = uri });
    }

    private static string LoadFromRegistry()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryPath);
            return key?.GetValue(RegistryKey) as string ?? string.Empty;
        }
        catch (Exception ex)
        {
            LoggerService.Warn($"LanguageService: 讀取 registry 失敗: {ex.Message}");
            return string.Empty;
        }
    }

    private static void SaveToRegistry(string code)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RegistryPath);
            key?.SetValue(RegistryKey, code);
        }
        catch (Exception ex)
        {
            LoggerService.Warn($"LanguageService: 寫入 registry 失敗: {ex.Message}");
        }
    }
}
