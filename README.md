# Terminus — Sleep Cycle Manager

[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/UI-WPF-blue)](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/)
[![Platform](https://img.shields.io/badge/Platform-Windows-0078D6)](https://www.microsoft.com/windows)

A Windows desktop application that manages your sleep cycle by monitoring your class schedule (iCal/webcal) and enforcing calculated shutdown times. Runs silently in the system tray, warns you when it's time to sleep, and powers off your PC at the hard limit.

## ✨ Features

### 🧠 Core Sleep Management
- **iCal Calendar Sync** — Fetches your class timetable from a `webcal://` or `https://` ICS URL (Google Calendar, iCloud, etc.), refreshes every 6 hours with offline cache fallback.
- **Smart Timing Calculator** — Computes warning & hard-shutdown times based on your first class of the day, with configurable buffers (sleep, wash, breakfast, commute, pre-sleep).
- **Early / Non-Early Classification** — Uses a configurable cutoff hour (default 12:00 noon) to decide whether a day is "early class" (limited delay quota) or "non-early" (unlimited delays). **Any timed calendar event counts as a class** — no special course-code format required.
- **Delay System** — Snooze the warning in 30-minute increments. Early-class days get a configurable quota (default 90 minutes); non-early days are unlimited.
- **Hard Shutdown** — Executes Windows shutdown at the calculated limit, with retry logic (3× every 5 min, then force).
- **Reboot Detection** — Detects if the PC was rebooted after a scheduled shutdown and skips re-triggering.

### 🖥️ Modern UI
- **Dashboard** — Live state display: today's first class, warning time, countdown, delay quota remaining, and shutdown time.
- **Calendar Page** — Monthly and weekly views of your schedule. Click any date to see detailed event list (time, title, location).
- **Settings Page** — Configure calendar URL, buffer times, early-class classification (cutoff hour & delay quota), theme, and startup behavior.
- **AI Overnight Mode** — Optional AI-assisted wake-up flow with configurable endpoint, API key, and model.
- **Logs Window** — In-app log viewer with filtering and live updates.
- **Dark / Light Theme** — Full dark mode support with dynamic resource-based theming.
- **Custom Scrollbars** — Slim, theme-aware scrollbars throughout the app.
- **System Tray** — Minimize to tray with quick actions (Open, Delay, Force Sleep, Exit).

### ⚙️ System Integration
- **Start with Windows** — Auto-launch via registry (`HKCU\SOFTWARE\Terminus`).
- **Windows Notifications** — Toast notifications via `Microsoft.WindowsAppSDK`.
- **Registry-based Settings** — All configuration stored in `HKEY_CURRENT_USER\SOFTWARE\Terminus`.

## 🚀 Getting Started

### Prerequisites
- Windows 10/11
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (for building)

### Build & Run

```bash
# Clone
git clone https://github.com/Harrisng/Terminus.git
cd Terminus

# Build
dotnet build Terminus.sln -c Release

# Run (debug)
dotnet run --project Terminus.UI

# Publish (self-contained, single folder)
dotnet publish Terminus.UI/Terminus.UI.csproj -c Release -r win-x64 --self-contained false -o publish
.\publish\Terminus.exe
```

### First Run
1. Enter your **calendar URL** (`webcal://...` or `https://...ics`) in the Settings page.
2. Adjust **buffer times** if needed (defaults: 6h sleep, 15 min pre-sleep, 30 min wash, 45 min breakfast, 1h45m commute).
3. Enable **Start with Windows** if desired.
4. Click **Save**. The app fetches your calendar and begins monitoring.

## 🏗️ Architecture

```
Terminus/
├── Terminus.Core/              # Business logic & services
│   ├── Logic/                  # Pure functions (timing, classification, cycles)
│   ├── Models/                 # Data models (CalendarEvent, AppState, etc.)
│   └── Services/               # Core services
│       ├── ICalService.cs      # iCal parsing & timezone conversion
│       ├── CalendarDataService # Fetch + cache + offline fallback
│       ├── SleepCycleCalculator
│       ├── Orchestrator.cs     # State machine (Idle → Warning → Shutdown)
│       └── ShutdownService.cs
├── Terminus.UI/                # WPF presentation layer
│   ├── Pages/                  # Dashboard, Calendar, Settings, About
│   ├── Styles/                 # Theme resources & control styles
│   ├── Services/               # SettingsService, WindowsNotificationService, TrayIconService
│   ├── ViewModels/             # MainViewModel
│   └── Windows/                # MainWindow, SettingsWindow, AIModeDialog, LogsWindow
└── Terminus.Tests/             # xUnit + FluentAssertions
```

### Key Design Decisions
- **No MVVM framework** — Lightweight `INotifyPropertyChanged` view models, no external dependency.
- **NodaTime** for timezone handling (Hong Kong `Asia/Hong_Kong`).
- **Ical.Net** for RFC 5545 parsing with RRULE expansion.
- **Microsoft.Extensions.Hosting** for dependency injection.
- **DynamicResource** theming — colors update instantly when switching themes.

## ⚙️ Configuration

All settings are stored in the Windows Registry at `HKCU\SOFTWARE\Terminus`.

| Setting | Default | Description |
|---------|---------|-------------|
| `CalendarUrl` | _(empty)_ | webcal/https ICS URL |
| `SleepHours` | 360 min | Sleep buffer |
| `PreSleepMinutes` | 15 | Pre-sleep wind-down |
| `WashMinutes` | 30 | Wash/shower buffer |
| `BreakfastMinutes` | 45 | Breakfast buffer |
| `CommuteMinutes` | 105 | Commute to school |
| `EarlyClassCutoffHour` | 12 | Hour (0-23) separating early vs non-early class days |
| `EarlyClassDelayQuotaMinutes` | 90 | Delay quota for early-class days (minutes) |
| `MaxShutdownTime` | 03:00 | Latest hard shutdown time |
| `Theme` | Dark | `Dark` or `Light` |

### Timing Example

| First Class | Warning | Hard Shutdown | Delay Quota |
|-------------|---------|---------------|-------------|
| 09:00       | 23:45   | 01:15         | 90 min      |
| 09:30       | 00:15   | 01:45         | 90 min      |
| 10:30       | 01:15   | 02:45         | 90 min      |
| 13:30 only  | 00:30   | 03:00         | Unlimited   |

## 🧪 Testing

```bash
dotnet test Terminus.Tests/Terminus.Tests.csproj
```

Covers: iCal parsing & RRULE expansion, early/non-early classification, timing calculations, sleep-cycle boundaries, cache & offline fallback.

## 🛠️ Tech Stack

- **.NET 8.0** / **C# 12**
- **WPF** (Windows Presentation Foundation)
- **NodaTime** — timezone-safe date/time
- **Ical.Net** — iCalendar parsing
- **Microsoft.Extensions.Hosting** — DI & hosting
- **Microsoft.WindowsAppSDK** — toast notifications
- **xUnit** + **FluentAssertions** — testing
- **Serilog** — structured logging

## 📝 License

This project is for personal educational use.

## 🐛 Troubleshooting

- **Calendar won't load?** Check the URL in Settings → Calendar. Logs are at `%LOCALAPPDATA%\Terminus\logs\terminus.log`.
- **Shutdown not triggering?** Ensure the app is running (check system tray) and the warning window is not stuck behind other windows.
- **Theme not applying fully?** All color resources use `DynamicResource`; restart the app if any control appears stale.

---

## 📦 安裝指南 (Installation)

### 🚀 快速安裝（推薦）

#### 方法 1：使用安裝腳本

1. **右鍵點擊** `terminus.bat`
2. **選擇**「以管理員身分執行」
3. **選擇操作** `1`（安裝）
4. **依照提示操作**：
   - 是否建立桌面捷徑？→ 建議選 Y
   - 是否開機自動啟動？→ 建議選 Y
   - 是否加入 PATH？→ 可選 N
5. **完成！** 程式會安裝到 `C:\Program Files\Terminus`

安裝完成後，可以：
- 從開始功能表啟動：Terminus
- 從桌面捷徑啟動
- 或直接執行 `C:\Program Files\Terminus\Terminus.exe`

#### 📦 安裝內容

安裝腳本會：
- ✅ 複製程式到 `C:\Program Files\Terminus`
- ✅ 建立開始功能表捷徑
- ✅ （可選）建立桌面捷徑
- ✅ （可選）設定開機自動啟動
- ✅ （可選）加入系統 PATH

### 🗑️ 解除安裝

1. **右鍵點擊** `terminus.bat`
2. **選擇**「以管理員身分執行」
3. **選擇操作** `2`（卸載）
4. **確認解除安裝**
5. **選擇是否刪除使用者資料**（設定和快取）

### 🛠️ 方法 2：手動安裝

如果不想用安裝腳本：

1. 複製整個 `publish` 資料夾到任意位置（例如 `D:\Programs\Terminus`）
2. 執行 `Terminus.exe`
3. 手動建立捷徑（可選）

### 📋 系統需求

- **作業系統**：Windows 10/11
- **.NET Runtime**：.NET 8.0 Desktop Runtime
  - 如果缺少，首次執行時會提示下載
  - 或從這裡下載：https://dotnet.microsoft.com/download/dotnet/8.0
- **權限**：需要管理員權限（用於執行關機指令）
- **磁碟空間**：約 50 MB

### 📝 首次使用

安裝後首次啟動：

1. 應用會顯示設定視窗
2. 輸入你的**日曆 URL**（`webcal://` 或 `https://` 地址）
3. 調整緩衝時間（可選，預設值已最佳化）
4. 自訂**早課判定**：設定早課分界時刻（預設 12 時）和早課日延後額度（預設 90 分鐘）
5. 勾選「開機啟動」（推薦）
6. 儲存設定

應用會在系統通知區域運行，右鍵通知區圖示可檢視功能表。

### ❓ 常見問題

**Q：為什麼需要管理員權限？**
A：程式需要執行系統關機指令，這需要管理員權限。

**Q：如何取得日曆 URL？**
A：從你的日曆平台匯出日曆 → 複製 `webcal://` 或 `https://` 地址。

**Q：程式會在背景執行嗎？**
A：是的，程式在系統通知區域執行，不會顯示主視窗。

**Q：如何暫時停用？**
A：右鍵通知區圖示 → Exit

**Q：如何檢視目前狀態？**
A：左鍵點擊通知區圖示，或右鍵 → Show Status

**Q：什麼算「早課」？**
A：在「早課分界時刻」之前開始的課算早課。預設分界是 12 時（中午），你可以到設定頁自行調整。任何有時間的日曆事件都算課程，不需要特定的課程代碼格式。

### 📞 技術支援

- 檢視 `Terminus_Spec_v2.5.md` 了解完整規格

---

**祝你睡個好覺！🌙**
