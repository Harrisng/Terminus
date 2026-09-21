# 多語系支援實作計劃

## Context

目前 Terminus 所有 UI 文字都硬編碼為繁體中文（XAML 屬性值、C# 字串字面值），不論系統語言為何都顯示繁中。使用者希望加入多語系支援，涵蓋：

- **3 種語言**：繁體中文（預設）、英文、簡體中文
- **自動偵測**：啟動時根據系統語言自動選擇
- **手動覆寫**：設定頁可選擇語言
- **即時切換**：切換後立即生效，不需重啟

## 設計原則

沿用現有 [ThemeService.cs](file:///d:/code/own/Terminus/Terminus.UI/Services/ThemeService.cs) 動態字典切換模式（移除舊字典、加入新字典）。所有 XAML 文字改用 `DynamicResource` 綁定，與現有主題色彩綁定方式一致。

## 架構

### 資源字典（3 個語言檔）

建立於 `Terminus.UI/Strings/`：

| 檔案 | 語言 | 用途 |
|------|------|------|
| `Strings.zh-TW.xaml` | 繁體中文 | 預設 fallback |
| `Strings.en-US.xaml` | 英文 | 英文系統使用 |
| `Strings.zh-CN.xaml` | 簡體中文 | 簡中系統使用 |

每個字典包含所有使用者可見字串，key 統一命名（如 `Dashboard_Title`、`Warning_QuotaExceeded`）。含格式占位符的用 `{0}`、`{1:F0}` 標準 .NET 格式。

### 服務層

#### `LanguageService.cs`（UI 專案，鏡像 `ThemeService` 模式）

職責：
- `Initialize()` — 啟動時呼叫，偵測系統語言或讀取登錄設定
- `ApplyLanguage(string code)` — 切換語言字典，即時生效
- `Get(string key, params object[] args)` — 從 `Application.Current.Resources` 查詢字串並格式化（給 UI 層 C# 用）
- `DetectSystemLanguage()` — `CultureInfo.InstalledUICulture` 對照表：zh-TW/zh-HK/zh-MO → zh-TW；zh-CN/zh-SG → zh-CN；en-* → en-US；其餘 → zh-TW

#### `ILocalizationService.cs`（Core 專案，介面）

```csharp
public interface ILocalizationService
{
    string Get(string key, params object[] args);
}
```

Core 專案（`BehaviorOrchestrator` 等只能用此介面拿字串，不直接依賴 WPF）。UI 專案實作此介面，內部呼叫 `LanguageService.Get`。

### DI 接線

`App.xaml.cs` 在 `OnStartup` 建立 `LanguageService`，並傳給 `BehaviorOrchestrator`（透過新加的 `ILocalizationService` 建構參數）。

## 遷移範圍

### XAML（DynamicResource 綁定）

| 檔案 | 大致字串數 | 備註 |
|------|-----------|------|
| [MainWindow.xaml](file:///d:/code/own/Terminus/Terminus.UI/MainWindow.xaml) | ~12 | 主選單、主題切換文字 |
| [DashboardPage.xaml](file:///d:/code/own/Terminus/Terminus.UI/Pages/DashboardPage.xaml) | ~25 | 狀態標籤、預覽標題 |
| [SettingsPage.xaml](file:///d:/code/own/Terminus/Terminus.UI/Pages/SettingsPage.xaml) | ~40 | 卡片標題、說明文字、按鈕 |
| [WarningDialog.xaml](file:///d:/code/own/Terminus/Terminus.UI/Windows/WarningDialog.xaml) | ~10 | 按鈕文字、標題 |
| [LogsWindow.xaml](file:///d:/code/own/Terminus/Terminus.UI/Windows/LogsWindow.xaml) | ~5 | 視窗標題、按鈕 |
| [DelayHistoryWindow.xaml](file:///d:/code/own/Terminus/Terminus.UI/Windows/DelayHistoryWindow.xaml) | ~8 | 表頭、欄位 |
| [SettingsWindow.xaml](file:///d:/code/own/Terminus/Terminus.UI/Windows/SettingsWindow.xaml) | ~3 | 視窗標題 |

範例轉換：
```xml
<!-- 前 -->
<TextBlock Text="儀表板" />
<!-- 後 -->
<TextBlock Text="{DynamicResource Nav_Dashboard}" />
```

### UI C# 字串（用 LanguageService.Get）

| 檔案 | 字串數 | 範例 |
|------|-------|------|
| [TrayIconService.cs](file:///d:/code/own/Terminus/Terminus.UI/Services/TrayIconService.cs) | ~12 | `Header = "📊 開啟主視窗"`、狀態文字 switch |
| [WindowsNotificationService.cs](file:///d:/code/own/Terminus/Terminus.UI/Services/WindowsNotificationService.cs) | ~15 | 通知標題、訊息 |
| [DashboardPage.xaml.cs](file:///d:/code/own/Terminus/Terminus.UI/Pages/DashboardPage.xaml.cs) | ~10 | 狀態顯示文字 switch（`GetStateDisplayInfo`） |
| [App.xaml.cs](file:///d:/code/own/Terminus/Terminus.UI/App.xaml.cs) | ~5 | 啟動錯誤訊息 |
| [WarningDialog.xaml.cs](file:///d:/code/own/Terminus/Terminus.UI/Windows/WarningDialog.xaml.cs) | ~3 | 兜底文字 |

範例轉換：
```csharp
// 前
Title = "配額已用完",
Message = $"延遲配額不足（剩餘 {quota:F0} 分鐘）"
// 後（用 ILocalizationService）
Title = _loc.Get("Warning_QuotaExceeded_Title"),
Message = _loc.Get("Warning_QuotaExceeded_Body", (int)quota.TotalMinutes)
// 字典值: "剩餘 {0} 分鐘，配額已用完" / "Quota exhausted ({0} min remaining)"
```

### Core C# 字串（用 ILocalizationService）

| 檔案 | 字串數 | 備註 |
|------|-------|------|
| [BehaviorOrchestrator.cs](file:///d:/code/own/Terminus/Terminus.Core/Services/BehaviorOrchestrator.cs) | ~5 | 通知標題/訊息、配額不足警告 |

`BehaviorOrchestrator` 建構子加入 `ILocalizationService` 參數。

### Logger 訊息

`LoggerService.cs` 的日訊息維持中文（開發者除錯用，不外顯給使用者）。日誌是診斷用途，與 UI 語言無關。

## 設定 UI

在 SettingsPage.xaml 加入「語言」卡片：

- ComboBox 列出 3 個選項：繁體中文 / English / 简体中文
- 選擇後即時呼叫 `LanguageService.ApplyLanguage(code)` 套用
- 寫入 registry `Language` 機碼（透過 [SettingsService](file:///d:/code/own/Terminus/Terminus.UI/Services/SettingsService.cs) 加 `GetLanguage()` / `SetLanguage(code)`）

## 啟動流程

`App.xaml.cs OnStartup` 新步驟：

1. 建立 `LanguageService`
2. 讀取 `SettingsService.GetLanguage()`：
   - 非空 → 用該值
   - 空 → `LanguageService.DetectSystemLanguage()` 並把結果寫回登錄（首次自動偵測後固化）
3. `LanguageService.ApplyLanguage(code)` 載入對應字典到 `Application.Current.Resources.MergedDictionaries`
4. 後續啟動 orchestrator、顯示主視窗等流程不變

## 驗證

### 自動化

- 在 [BehaviorOrchestratorTests](file:///d:/code/own/Terminus/Terminus.Tests/Services/BehaviorOrchestratorTests.cs) 用 `StubLocalizationService` 注入固定字串，確保測試不依賴資源字典
- 新增 `LanguageServiceTests`：偵測邏輯、ApplyLanguage 後字典切換、Get 格式化

### 手動驗證

1. 切換系統語言為 English (US) → 啟動 Terminus → UI 顯示英文
2. 在設定頁改為简体中文 → 即時生效，所有視窗文字變簡中
3. 重啟程式 → 保持簡中
4. 觸發警告對話框 → 按鈕、標題、訊息都用當前語言
5. 托盤右鍵選單 → 文字用當前語言
6. 查看延遲歷史、日誌視窗 → 表頭用當前語言

## 提交規劃

預計 6 個 commit：

1. 加入 3 個語言字典骨架 + `LanguageService` + `ILocalizationService` 介面 + DI 接線
2. 遷移 MainWindow.xaml + DashboardPage.xaml + DashboardPage.xaml.cs 的 XAML 與 C# 字串
3. 遷移 SettingsPage.xaml + SettingsWindow.xaml + 加入語言選擇 UI
4. 遷移 WarningDialog.xaml + WarningDialog.xaml.cs + LogsWindow.xaml + DelayHistoryWindow.xaml
5. 遷移 TrayIconService.cs + WindowsNotificationService.cs + App.xaml.cs + BehaviorOrchestrator.cs（Core 字串）
6. 更新測試 + 發布 exe

每個 commit 都會 build + test 通過後再推送。

## 已知風險

1. **字串 key 命名衝突**：用前綴分類（`Nav_`、`Dashboard_`、`Settings_`、`Warning_`、`Tray_`、`Notification_`、`Common_`）
2. **格式占位符位置差異**：英文語序可能與中文不同（`"{0} minutes remaining"` vs `"剩餘 {0} 分鐘"`），用編號占位符 `{0}` `{1}` 而非位置順序，避免語序問題
3. **DynamicResource 效能**：每次 FindResource 比直接字串慢，但 DashboardPage 已快取 Brush，文字標籤綁定影響極小
4. **深層日誌字串**：LoggerService 訊息維持中文，避免日誌被誤譯
