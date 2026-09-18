# Terminus — Sleep Cycle Manager

[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/UI-WPF-blue)](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/)
[![Platform](https://img.shields.io/badge/Platform-Windows-0078D6)](https://www.microsoft.com/windows)

A Windows desktop application that manages your sleep cycle by monitoring your class schedule (iCal/webcal) and enforcing calculated shutdown times. Runs silently in the system tray, warns you when it's time to sleep, and powers off your PC at the hard limit.

## ✨ Features

### 🧠 Core Sleep Management
- **iCal Calendar Sync** — Fetches your class timetable from a `webcal://` or `https://` ICS URL (Moodle, Google Calendar, etc.), refreshes every 6 hours with offline cache fallback.
- **Smart Timing Calculator** — Computes warning & hard-shutdown times based on your first class of the day, with configurable buffers (sleep, wash, breakfast, commute, pre-sleep).
- **Early / Non-Early Classification** — Uses a 12:00 noon boundary to decide whether a day is "early class" (limited delay quota) or "non-early" (unlimited delays).
- **Delay System** — Snooze the warning in 30-minute increments. Early-class days get a 90-minute quota; non-early days are unlimited.
- **Hard Shutdown** — Executes Windows shutdown at the calculated limit, with retry logic (3× every 5 min, then force).
- **Reboot Detection** — Detects if the PC was rebooted after a scheduled shutdown and skips re-triggering.

### 🖥️ Modern UI
- **Dashboard** — Live state display: today's first class, warning time, countdown, delay quota remaining, and shutdown time.
- **Calendar Page** — Monthly and weekly views of your schedule. Click any date to see detailed event list (time, title, location).
- **Settings Page** — Configure calendar URL, buffer times, theme, and startup behavior.
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
| `EarlyClassCutoff` | 12:00 | Noon boundary for early classification |
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
