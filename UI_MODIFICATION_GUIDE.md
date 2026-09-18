# Terminus UI 修改指南

這份文檔幫助其他 AI 或開發者快速修改 Terminus 的界面設計。

## 📁 項目結構

```
D:\code\own\Terminus\
├── Terminus.UI\              # WPF 用戶界面項目
│   ├── MainWindow.xaml       # 主窗口界面（狀態顯示）
│   ├── MainWindow.xaml.cs    # 主窗口邏輯
│   ├── SettingsWindow.xaml   # 設置窗口界面
│   ├── SettingsWindow.xaml.cs# 設置窗口邏輯
│   ├── AIModeDialog.xaml     # AI 模式對話框
│   ├── App.xaml              # 應用程序入口和全局樣式
│   └── Services\
│       ├── TrayIconService.cs # 系統托盤圖標服務
│       └── WindowsNotificationService.cs # 通知服務
├── Terminus.Core\            # 核心業務邏輯（不涉及 UI）
└── Terminus.Tests\           # 單元測試
```

## 🎨 界面文件說明

### 1. MainWindow.xaml - 主窗口
**位置**: `D:\code\own\Terminus\Terminus.UI\MainWindow.xaml`

**功能**: 顯示當前狀態、週期信息、時間安排等

**主要組件**:
- 標題區：顯示 "🌙 睡眠週期管理" 和當前時間
- 狀態卡片：白色圓角卡片，顯示 8 項信息
  - 運行狀態、當前週期、目標日期、課程分類
  - 警告時間、強制關機、剩餘配額、延後次數
- 操作按鈕：設置按鈕、關閉按鈕

**設計元素**:
```xml
<!-- 配色方案 -->
背景色: #F5F7FA (淺灰藍)
卡片背景: White
主文字: #2C3E50 (深灰藍)
次要文字: #7F8C8D (灰色)
按鈕: #3498DB (藍色) → #2980B9 (hover)

<!-- 字體大小 -->
標題: 28px
副標題: 16px
標籤: 14px
內容: 14px

<!-- 間距 -->
外邊距: 30px
卡片內邊距: 25px
陰影: 0px 0px 20px rgba(0,0,0,0.1)
圓角: 10px
```

### 2. SettingsWindow.xaml - 設置窗口
**位置**: `D:\code\own\Terminus\Terminus.UI\SettingsWindow.xaml`

**功能**: 配置日曆 URL 和時間緩衝參數

**主要組件**:
- 日曆設置卡片：輸入 webcal:// 地址
- 時間緩衝設置卡片：5 個時間參數
  - 睡眠時間 (默認 360 分鐘 = 6 小時)
  - 睡前準備 (默認 30 分鐘)
  - 梳洗時間 (默認 60 分鐘)
  - 早餐時間 (默認 20 分鐘)
  - 通勤時間 (默認 105 分鐘 = 1小時45分)
- 啟動設置：開機自啟選項

**輸入控件命名**:
```
CalendarUrlTextBox  - 日曆 URL
SleepTimeTextBox    - 睡眠時間
PreSleepTextBox     - 睡前準備
WashTextBox         - 梳洗時間
BreakfastTextBox    - 早餐時間
CommuteTextBox      - 通勤時間
StartWithWindowsCheckBox - 開機自啟
```

### 3. TrayIconService.cs - 托盤圖標
**位置**: `D:\code\own\Terminus\Terminus.UI\Services\TrayIconService.cs`

**功能**: 系統托盤圖標和右鍵菜單

**圖標生成**: 第 88-106 行 `CreateDefaultIcon()` 方法
- 使用 GDI+ 繪製月亮圖標
- 可以替換為嵌入的 .ico 資源

## 🔧 如何修改界面

### 方法 1: 修改現有 XAML（推薦）

1. **修改顏色**:
```xml
<!-- 在 Window.Resources 中找到 Style 定義 -->
<Style TargetType="Button" x:Key="ModernButton">
    <Setter Property="Background" Value="#3498DB"/> <!-- 改這裡 -->
</Style>
```

2. **修改字體大小**:
```xml
<TextBlock Text="🌙 睡眠週期管理" FontSize="28"/> <!-- 改 FontSize -->
```

3. **修改佈局間距**:
```xml
<Grid Margin="30"> <!-- 外邊距 -->
<Border Padding="25"> <!-- 內邊距 -->
```

4. **修改圓角和陰影**:
```xml
<Border CornerRadius="10"> <!-- 圓角 -->
    <Border.Effect>
        <DropShadowEffect BlurRadius="20" Opacity="0.1"/> <!-- 陰影 -->
    </Border.Effect>
</Border>
```

### 方法 2: 使用全局樣式

在 `App.xaml` 中添加全局樣式：
```xml
<Application.Resources>
    <SolidColorBrush x:Key="PrimaryColor" Color="#3498DB"/>
    <SolidColorBrush x:Key="BackgroundColor" Color="#F5F7FA"/>
    <!-- 然後在各窗口中引用 -->
</Application.Resources>
```

### 方法 3: 替換圖標

**使用自定義 .ico 文件**:
1. 將 .ico 文件添加到項目 `Resources` 文件夾
2. 在項目文件中設置為嵌入資源
3. 修改 `TrayIconService.cs`:
```csharp
// 替換 CreateDefaultIcon() 為：
var iconStream = Application.GetResourceStream(
    new Uri("pack://application:,,,/Resources/icon.ico")).Stream;
return new System.Drawing.Icon(iconStream);
```

## 🏗️ 構建流程

### 開發構建
```bash
cd D:\code\own\Terminus
dotnet build -c Release
```

### 發布獨立 EXE
```bash
cd D:\code\own\Terminus
dotnet publish Terminus.UI/Terminus.UI.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o Output_Folder_Name
```

輸出文件：`Output_Folder_Name\Terminus.exe` (約 150 MB)

### 常見構建問題

1. **文件被佔用**: 關閉正在運行的 Terminus.exe
2. **發布失敗**: 換個輸出目錄名稱
3. **缺少依賴**: 確保安裝了 .NET 8 SDK

## 📝 UI 組件清單

### 當前使用的 WPF 控件
- `Window` - 窗口容器
- `Grid` - 佈局網格
- `StackPanel` - 垂直/水平堆疊
- `Border` - 邊框和背景容器
- `TextBlock` - 只讀文本
- `TextBox` - 輸入框
- `Button` - 按鈕
- `CheckBox` - 複選框
- `ScrollViewer` - 滾動容器
- `DropShadowEffect` - 陰影效果

### 可用但未使用的控件（可擴展）
- `ComboBox` - 下拉選擇
- `Slider` - 滑塊
- `ProgressBar` - 進度條
- `DatePicker` - 日期選擇器
- `TabControl` - 標籤頁
- `ListView` - 列表視圖

## 🎯 快速改色方案

### 藍色主題（當前）
```
主色: #3498DB (藍)
深色: #2C3E50 (深藍灰)
背景: #F5F7FA (淺藍灰)
```

### 綠色主題建議
```
主色: #27AE60 (綠)
深色: #2C3E50 (深藍灰)
背景: #F5FAF7 (淺綠灰)
```

### 紫色主題建議
```
主色: #9B59B6 (紫)
深色: #2C3E50 (深藍灰)
背景: #F9F7FA (淺紫灰)
```

### 深色主題建議
```
主色: #3498DB (藍)
深色: #FFFFFF (白)
背景: #2C3E50 (深藍灰)
卡片: #34495E (深灰)
```

## 🔍 關鍵代碼位置

### 數據綁定更新
**文件**: `MainWindow.xaml.cs`
- 第 34-73 行：`UpdateDisplay()` 方法更新所有顯示文本
- 如果添加新的 UI 元素，在這裡綁定數據

### 驗證邏輯
**文件**: `SettingsWindow.xaml.cs`
- 第 37-74 行：輸入驗證和錯誤提示
- 修改提示文本在這裡

### 狀態文本映射
**文件**: `MainWindow.xaml.cs` 第 43-52 行
```csharp
StateText.Text = context.State switch
{
    BehaviorState.Idle => "閒置中",
    BehaviorState.Warning => "警告中",
    // ... 在這裡修改狀態顯示文本
};
```

## 📞 給 AI 助手的提示

如果你是其他 AI 正在幫助修改這個項目：

1. **界面文件都在**: `D:\code\own\Terminus\Terminus.UI\*.xaml`
2. **只改界面，不要動**: `Terminus.Core\` 文件夾（業務邏輯）
3. **構建命令**: 見上方"發布獨立 EXE"部分
4. **配色統一**: 在 `Window.Resources` 中使用 `Style` 定義
5. **測試**: 修改後記得用 `dotnet build` 檢查語法錯誤

## ✅ 當前狀態

- ✅ 全中文界面
- ✅ 現代化設計（卡片、陰影、圓角）
- ✅ 響應式佈局
- ✅ 獨立 EXE 可運行
- ⚠️ 圖標為程序生成（可替換為自定義 .ico）

---

**最後更新**: 2026年9月16日  
**當前版本**: Terminus v2.5.0  
**UI 框架**: WPF (.NET 8.0)
