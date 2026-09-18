using NodaTime;
using Terminus.Core.Models;

namespace Terminus.Core.Services;

/// <summary>
/// 週期狀態持久化服務 — 負責以 DPAPI 加密存檔／還原配額、延遲次數、狀態與歷史記錄。
/// </summary>
public interface ICycleStateService
{
    /// <summary>將當前週期狀態加密存檔，可附帶一筆延遲歷史記錄。</summary>
    void SaveCycleState(BehaviorContext context, string? delayType = null, int delayMinutes = 0, string? delayedTo = null);

    /// <summary>若加密檔案存的是同一週期，恢復配額／延遲次數到 SleepCycle。</summary>
    void TryRestoreCycleState(SleepCycle cycle);

    /// <summary>從加密檔案還原上次存檔的狀態和下次動作時間到 BehaviorContext。</summary>
    void RestoreContextState(BehaviorContext context, ZonedDateTime hkNow);

    /// <summary>讀取加密狀態檔案（解密後的 CycleStateData，或 null）。</summary>
    CycleStateData? ReadState();

    /// <summary>直接寫入一筆 CycleStateData（加密）。</summary>
    void WriteState(CycleStateData state);
}
