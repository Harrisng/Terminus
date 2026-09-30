# Terminus 三項功能改進計劃

## Context

用戶提出三項新需求：

1. **立即關機加取消按鈕** — `WarningDialog.ShutdownButton_Click` 點擊後直接執行 `_onShutdown()` 沒有確認步驟，用戶無法反悔。需加確認流程。
   - 注意：`DashboardPage.ShutdownNowButton_Click`（儀表板的「立即關機」按鈕）**已有**確認流程（`SetConfirmMode(isDanger:true)`）。這次要修的是**警告對話框內**那個。
2. **延後時間加自訂分鐘輸入** — 警告對話框和儀表板目前只有 30/60/90 三個固定按鈕。底層 `OnDelayRequestedAsync(TimeSpan? duration)` 已支援自訂時長，只是 UI 沒接上。
3. **假期檢測功能** — `ICalService.FetchHolidaysAsync` 和 `CalendarDataService.GetHolidaysAsync` 都已實作完成能抓香港公眾假期，但 `BehaviorOrchestrator` 完全沒呼叫，`ScheduleClassifier.ClassifyDay` 也沒接收 holidays 參數。等於底層都寫好了，只是接線沒接上。

假期行為決策（用戶已確認）：假期當天 = 非早課日（warning=00:30、hard shutdown=03:00、延遲無限），但頁面要顯示「假期」狀態而非僅「非早課日」。

---

## 實作方案

### 任務 1：警告對話框立即關機確認

**檔案**：[WarningDialog.xaml.cs](file:///d:/code/own/Terminus/Terminus.UI/Windows/WarningDialog.xaml.cs)

**現狀**：`ShutdownButton_Click` 直接 `await _onShutdown()` 然後 `Close()`。

**改法**：點擊後進入確認模式，不直接執行 `_onShutdown`。需要保存原本的按鈕可見狀態（讓取消後能還原）。

新增方法 `SetShutdownConfirmMode()`：
- 隱藏所有 Delay 按鈕和原本的 ShutdownButton
- 顯示 ConfirmButton（紅色危險樣式）和 CancelButton
- ConfirmButton 點擊：執行 `_onShutdown()` 然後 `Close()`
- CancelButton 點擊：還原原本按鈕狀態（不關閉對話框，回到警告模式）

實作細節：
- 在 `ShutdownButton_Click` 改為調用 `SetShutdownConfirmMode()` 而不是直接執行 `_onShutdown`
- 新增 `ConfirmButton_Click` 邏輯：若是確認關機模式則執行 `_onShutdown`，否則返回 true 關閉（保持原本 SetConfirmMode 用途）
- 用一個 `_isShutdownConfirmMode` 布林欄位區分
- 取消後恢復：需在進入確認模式前快取各 Delay 按鈕和 ShutdownButton 的 Visibility，取消時還原

### 任務 2：自訂分鐘延後輸入

**檔案**：
- [WarningDialog.xaml](file:///d:/code/own/Terminus/Terminus.UI/Windows/WarningDialog.xaml)
- [WarningDialog.xaml.cs](file:///d:/code/own/Terminus/Terminus.UI/Windows/WarningDialog.xaml.cs)
- [DashboardPage.xaml](file:///d:/code/own/Terminus/Terminus.UI/Pages/DashboardPage.xaml)（儀表板延後按鈕改為開啟警告對話框）
- [Strings.zh-TW.xaml](file:///d:/code/own/Terminus/Terminus.UI/Strings/Strings.zh-TW.xaml)、[Strings.zh-CN.xaml](file:///d:/code/own/Terminus/Terminus.UI/Strings/Strings.zh-CN.xaml)、[Strings.en-US.xaml](file:///d:/code/own/Terminus/Terminus.UI/Strings/Strings.en-US.xaml)

**改法**：
1. 在 `WarningDialog.xaml` 的延遲按鈕 Grid 下方加一個「自訂」按鈕（`DelayCustomButton`），用次要按鈕樣式（非主色調）
2. 點擊後切換到「自訂分鐘輸入模式」：
   - 隱藏所有固定按鈕
   - 顯示一個 TextBox + 確認/取消按鈕
   - TextBox 預設值 30、最小 1、最大 480（8 小時）
3. 用戶輸入分鐘數後確認，調用 `_onDelay(TimeSpan.FromMinutes(input))`
4. 同樣保存原本按鈕狀態，取消可還原

**XAML 變更**：
- 在延遲按鈕 Grid 下方加 `DelayCustomButton`
- 在 ConfirmButton/CancelButton 附近加一個隱藏的「自訂輸入區」（TextBox + 上下微調或 Slider）：
  ```xaml
  <StackPanel x:Name="CustomDelayPanel" Visibility="Collapsed" Margin="0,0,0,8">
      <TextBox x:Name="CustomDelayInput" ... />
      <StackPanel Orientation="Horizontal">
          <Button x:Name="CustomDelayConfirmButton" Content="{DynamicResource Common_Confirm}" .../>
          <Button x:Name="CustomDelayCancelButton" Content="{DynamicResource Common_Cancel}" .../>
      </StackPanel>
  </StackPanel>
  ```

**Code-behind**：
- 新增 `DelayCustomButton_Click`：切換到自訂模式
- 新增 `CustomDelayConfirmButton_Click`：解析 TextBox 數值，驗證範圍，調用 `_onDelay`
- 新增 `CustomDelayCancelButton_Click`：還原按鈕狀態
- TextBox PreviewTextInput 加數字驗證

**儀表板延後按鈕**：目前 `DashboardPage.DelayButton_Click` 直接調用 `_orchestrator.OnDelayRequestedAsync()` 用預設 30 分鐘。改為：點擊後開啟一個輕量警告對話框，只顯示延遲選項（不顯示立即關機），讓用戶選擇 30/60/90/自訂。
- 用新的 `WarningDialog` 建構子參數 `delayOnlyMode: true` 或複用現有參數 `showShutdownButton: false`

### 任務 3：假期檢測與顯示

**檔案**：
- [ScheduleClassifier.cs](file:///d:/code/own/Terminus/Terminus.Core/Logic/ScheduleClassifier.cs)
- [BehaviorOrchestrator.cs](file:///d:/code/own/Terminus/Terminus.Core/Services/BehaviorOrchestrator.cs)
- [TimingCalculator.cs](file:///d:/code/own/Terminus/Terminus.Core/Logic/TimingCalculator.cs)
- [ScheduleTiming.cs](file:///d:/code/own/Terminus/Terminus.Core/Models/ScheduleTiming.cs)（需加 IsHoliday 欄位）
- [DashboardPage.xaml.cs](file:///d:/code/own/Terminus/Terminus.UI/Pages/DashboardPage.xaml.cs)
- [Strings.*.xaml](file:///d:/code/own/Terminus/Terminus.UI/Strings/)（加 Classification_Holiday）

**改法**：

1. **ScheduleTiming 加欄位**：
   ```csharp
   public bool IsHoliday { get; set; }
   ```

2. **ScheduleClassifier.ClassifyDay 加 holidays 參數**：
   ```csharp
   public static DayClassification ClassifyDay(
       LocalDate targetDate,
       List<CalendarEvent> events,
       bool hasScheduleData = true,
       int cutoffHour = 12,
       List<PublicHoliday>? holidays = null)  // 新增
   ```
   邏輯：在原本分類前先檢查 holidays，若該日期是假期，直接返回 `NonEarlyClass`。
   
   另新增一個檢查假期的方法：
   ```csharp
   public static bool IsHoliday(LocalDate targetDate, List<PublicHoliday>? holidays)
       => holidays?.Any(h => h.Date == targetDate) ?? false;
   ```

3. **TimingCalculator.CalculateTiming 加 isHoliday 參數**：
   ```csharp
   public static ScheduleTiming CalculateTiming(
       DayClassification classification,
       LocalTime? firstClassTime,
       ...,
       bool isHoliday = false)  // 新增
   ```
   將 `isHoliday` 傳到 `ScheduleTiming.IsHoliday` 屬性。非早課日路徑才會進來（假期被 ClassifyDay 分類為 NonEarlyClass）。

4. **BehaviorOrchestrator.InitializeContextAsync 抓取假期**：
   - 在 `GetScheduleAsync` 之後呼叫 `_calendarService.GetHolidaysAsync("tc")` 抓取假期
   - 把 holidays 傳給 `ScheduleClassifier.ClassifyDay`
   - 把 `IsHoliday` 傳給 `TimingCalculator.CalculateTiming`
   - 同時把 holidays 存到 `BehaviorContext.Holidays`（新欄位）供 `LoadSchedulePreview` 重用

5. **BehaviorContext 加欄位**：
   ```csharp
   public List<PublicHoliday>? Holidays { get; set; }
   ```

6. **DashboardPage 顯示假期狀態**：
   - 在 `GetClassificationDisplayInfo` 檢查 `context.Timing.IsHoliday`：
     - 若 IsHoliday=true，顯示「🌅 假期」（用資源 `Classification_Holiday`）+ `StatusSuccess` 顏色（或新增 `AccentHoliday` 資源）
   - `LoadSchedulePreview` 中呼叫 `ScheduleClassifier.ClassifyDay` 時也要傳 holidays，並在 Preview 中假期當天顯示「假期」而非「非早課日」

7. **字串資源**（三個語言檔）：
   - `Classification_Holiday` = 「假期」/「假日」/「Holiday」
   - `Warning_CustomDelay` = 「自訂分鐘」/「自定义分钟」/「Custom minutes」
   - `Warning_CustomDelayPrompt` = 「請輸入延後分鐘數（1-480）」
   - `Warning_CustomDelayInvalid` = 「請輸入 1-480 之間的有效數字」

---

## 實作順序

1. 任務 3 先做假期檢測（底層接線，影響面最大但最獨立）
2. 任務 1 警告對話框立即關機確認
3. 任務 2 自訂分鐘延後輸入
4. 每個任務一個 commit（用戶偏好刷 commit 數）
5. 全部完成後 build → test → 重新發布 publish\Terminus.exe

## 驗證方式

- Build：`dotnet build Terminus.sln -c Release`
- Test：`dotnet test`（確認 ScheduleClassifier / TimingCalculator 測試通過；可能需要更新測試加上 holidays 參數）
- 手動驗證：
  1. 啟動應用，在警告對話框點擊「立即關機」→ 應出現紅色確認按鈕 + 取消按鈕，取消後返回原本警告模式
  2. 警告對話框點擊「自訂」→ 出現輸入框，輸入 45 → 點確認 → 應延後 45 分鐘
  3. 檢查今天/明天是否為香港公眾假期，若是則儀表板 Classification 顯示「假期」
- 測試假期抓取：可手動設系統日期到最近公眾假期（如 10/1 國慶日）驗證
