namespace Terminus.Core.Services;

public static class LoggerService
{
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Terminus", "logs");

    private static readonly string LogFilePath = Path.Combine(LogDirectory, "terminus.log");

    /// <summary>輪替上限：5 MB。超過後將舊日誌改名為 .bak，並建立新檔。</summary>
    private const long MaxLogSizeBytes = 5 * 1024 * 1024;

    private static readonly object _lock = new();

    static LoggerService()
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
        }
        catch { /* 建立目錄失敗時忽略，Log() 會再嘗試 */ }
    }

    public static void Log(string category, string message)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var line = $"[{timestamp}] [{category}] {message}{Environment.NewLine}";

            lock (_lock)
            {
                // 日誌輪替：超過上限時備份舊檔並建立新檔
                try
                {
                    if (File.Exists(LogFilePath))
                    {
                        var fi = new FileInfo(LogFilePath);
                        if (fi.Length > MaxLogSizeBytes)
                        {
                            var bakPath = LogFilePath + ".bak";
                            if (File.Exists(bakPath))
                                File.Delete(bakPath);
                            File.Move(LogFilePath, bakPath);
                        }
                    }
                }
                catch { /* 輪替失敗不影響寫入 */ }

                File.AppendAllText(LogFilePath, line);
            }
        }
        catch { /* 寫入失敗時靜默，避免日誌服務本身拋出例外導致應用程式崩潰 */ }
    }

    public static void Info(string message) => Log("INFO", message);
    public static void Warn(string message) => Log("WARN", message);
    public static void Error(string message) => Log("ERROR", message);
    public static void Error(string message, Exception ex) => Log("ERROR", $"{message}\n  Exception: {ex.GetType().Name}: {ex.Message}\n  StackTrace: {ex.StackTrace}");

    public static string GetLogFilePath() => LogFilePath;

    public static string ReadRecentLogs(int maxLines = 500)
    {
        try
        {
            if (!File.Exists(LogFilePath))
                return "（無日誌記錄）";

            var lines = File.ReadAllLines(LogFilePath);
            if (lines.Length == 0)
                return "（日誌為空）";

            var startIdx = Math.Max(0, lines.Length - maxLines);
            var recent = new string[lines.Length - startIdx];
            Array.Copy(lines, recent, recent.Length);
            return string.Join(Environment.NewLine, recent);
        }
        catch (Exception ex)
        {
            return $"讀取日誌失敗: {ex.Message}";
        }
    }

    public static void Clear()
    {
        try
        {
            lock (_lock)
            {
                if (File.Exists(LogFilePath))
                    File.WriteAllText(LogFilePath, "");
            }
        }
        catch { }
    }
}
