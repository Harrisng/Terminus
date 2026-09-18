using System.Security.Cryptography;
using NodaTime;
using Terminus.Core.Models;

namespace Terminus.Core.Services;

/// <summary>
/// DPAPI 加密週期狀態服務實作。金鑰綁定 Windows 用戶帳號，原始碼公開也無法解密。
/// </summary>
public class CycleStateService : ICycleStateService
{
    private static readonly string StateDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Terminus");
    private static readonly string StateFile = Path.Combine(StateDir, "cycle_state.dat");

    /// <inheritdoc/>
    public void SaveCycleState(BehaviorContext context, string? delayType = null, int delayMinutes = 0, string? delayedTo = null)
    {
        try
        {
            var c = context.CurrentCycle;
            var existing = ReadState();
            var history = (existing != null && existing.CycleId == c.CycleId)
                ? existing.History : new List<DelayRecord>();

            if (delayType != null)
            {
                history.Add(new DelayRecord
                {
                    Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    DurationMinutes = delayMinutes,
                    DelayedTo = delayedTo ?? "",
                    Type = delayType
                });
            }

            var state = new CycleStateData
            {
                CycleId = c.CycleId,
                QuotaRemainingMinutes = (int)c.QuotaRemaining.TotalMinutes,
                DelayCount = c.DelayCount,
                AutoDelayCount = c.AutoDelayCount,
                LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                History = history,
                State = context.State.ToString(),
                NextActionTime = context.NextActionTime?.ToDateTimeUnspecified().ToString("yyyy-MM-ddTHH:mm:ss")
            };

            WriteState(state);
            LoggerService.Info($"CycleStateService: 週期狀態已存檔, DelayCount={c.DelayCount}, AutoDelayCount={c.AutoDelayCount}");
        }
        catch (Exception ex)
        {
            LoggerService.Error($"CycleStateService: SaveCycleState 失敗: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public void TryRestoreCycleState(SleepCycle cycle)
    {
        try
        {
            var state = ReadState();
            if (state == null || state.CycleId != cycle.CycleId) return;

            cycle.QuotaRemaining = TimeSpan.FromMinutes(state.QuotaRemainingMinutes);
            cycle.DelayCount = state.DelayCount;
            cycle.AutoDelayCount = state.AutoDelayCount;

            LoggerService.Info($"CycleStateService: 恢復週期狀態, Quota={cycle.QuotaRemaining.TotalMinutes:F0}min, DelayCount={cycle.DelayCount}, AutoDelayCount={cycle.AutoDelayCount}, History={state.History.Count}筆");
        }
        catch (Exception ex)
        {
            LoggerService.Error($"CycleStateService: TryRestoreCycleState 失敗: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public void RestoreContextState(BehaviorContext context, ZonedDateTime hkNow)
    {
        try
        {
            var saved = ReadState();
            if (saved == null || saved.CycleId != context.CurrentCycle.CycleId) return;

            // 還原狀態
            if (!string.IsNullOrEmpty(saved.State) && Enum.TryParse<BehaviorState>(saved.State, out var savedState))
            {
                context.State = savedState;
            }

            // 還原 NextActionTime
            if (!string.IsNullOrEmpty(saved.NextActionTime) && DateTime.TryParse(saved.NextActionTime, out var dt))
            {
                var instant = Instant.FromDateTimeOffset(new DateTimeOffset(dt, TimeSpan.FromHours(8)));
                context.NextActionTime = instant.InZone(DateTimeZoneProviders.Tzdb["Asia/Hong_Kong"]);
            }

            // 如果延遲時間已過，回到 Warning
            if ((context.State == BehaviorState.Delayed || context.State == BehaviorState.AutoDelaying)
                && context.NextActionTime.HasValue
                && hkNow.ToInstant() >= context.NextActionTime.Value.ToInstant())
            {
                context.State = BehaviorState.Warning;
                context.NextActionTime = null;
                LoggerService.Info("CycleStateService: 延遲時間已過, 回到 Warning");
            }

            LoggerService.Info($"CycleStateService: 還原狀態={context.State}, NextActionTime={context.NextActionTime?.ToDateTimeUnspecified():yyyy-MM-dd HH:mm}");
        }
        catch (Exception ex)
        {
            LoggerService.Error($"CycleStateService: RestoreContextState 失敗: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public CycleStateData? ReadState()
    {
        if (!File.Exists(StateFile)) return null;

        try
        {
            var bytes = File.ReadAllBytes(StateFile);
            if (bytes.Length == 0) return null;

            // DPAPI 解密（綁定當前 Windows 用戶）
            var json = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
            return System.Text.Json.JsonSerializer.Deserialize<CycleStateData>(json);
        }
        catch (Exception ex)
        {
            // 解密失敗（舊格式或檔案損壞），刪除舊檔案，下次存檔會建立新的
            LoggerService.Error($"CycleStateService: ReadState 解密失敗，刪除舊檔: {ex.Message}");
            try { File.Delete(StateFile); } catch { /* 忽略刪除失敗 */ }
            return null;
        }
    }

    /// <inheritdoc/>
    public void WriteState(CycleStateData state)
    {
        Directory.CreateDirectory(StateDir);
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(state);
        // DPAPI 加密（綁定當前 Windows 用戶，換帳號/換機器都無法解密）
        var encrypted = ProtectedData.Protect(json, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(StateFile, encrypted);
    }
}
