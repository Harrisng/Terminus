namespace Terminus.Core.Services;

/// <summary>
/// 當地語系化服務介面。Core 專案用此介面取得使用者可見字串，避免直接依賴 WPF。
/// UI 專案負責實作（從 ResourceDictionary 查詢 + 格式化）。
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// 依資源 key 取得當前語言字串。可傳入格式化參數（與 string.Format 相同語法）。
    /// 找不到 key 時回傳 "[key]" 標記，避免拋例外。
    /// </summary>
    string Get(string key, params object[] args);
}

/// <summary>
/// 預設實作：不依賴 WPF，直接回傳 key。給測試或未初始化的環境使用。
/// </summary>
public sealed class NullLocalizationService : ILocalizationService
{
    public string Get(string key, params object[] args) =>
        args.Length > 0 ? $"[{key}]{string.Join(",", args)}" : $"[{key}]";
}
