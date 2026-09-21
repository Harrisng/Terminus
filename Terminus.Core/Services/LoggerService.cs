using System.Text;

namespace Terminus.Core.Services;

public static class LoggerService
{
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Terminus", "logs");

    private static readonly string LogFilePath = Path.Combine(LogDirectory, "terminus.log");

    /// <summary>輪替上限：5 MB。超過後將舊日誌改名為 .bak，並建立新檔。</summary>
    private const long MaxLogSizeBytes = 5 * 1024 * 1024;

    /// <summary>每次寫入後強制 flush 的間隔（寫入次數）。降低磁碟 I/O 卻保持當機可恢復。</summary>
    private const int FlushEveryWrites = 10;

    private static readonly object _lock = new();
    private static StreamWriter? _writer;
    private static int _writesSinceFlush = 0;

    static LoggerService()
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
        }
        catch { /* 建立目錄失敗時忽略，Log() 會再嘗試 */ }
    }

    /// <summary>
    /// 取得或建立目前日誌檔的 StreamWriter（共用單例，避免每次寫入都開關檔案）。
    /// 失敗時擲回，Log() 會吞掉。
    /// </summary>
    private static StreamWriter EnsureWriter()
    {
        if (_writer != null)
            return _writer;

        // 輪替檢查
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

        var stream = new FileStream(LogFilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream, new UTF8Encoding(false))
        {
            AutoFlush = false
        };
        return _writer;
    }

    public static void Log(string category, string message)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var line = $"[{timestamp}] [{category}] {message}{Environment.NewLine}";

            lock (_lock)
            {
                var writer = EnsureWriter();
                writer.Write(line);
                _writesSinceFlush++;
                if (_writesSinceFlush >= FlushEveryWrites)
                {
                    writer.Flush();
                    _writesSinceFlush = 0;
                }
            }
        }
        catch { /* 寫入失敗時靜默，避免日誌服務本身拋出例外導致應用程式崩潰 */ }
    }

    public static void Info(string message) => Log("INFO", message);
    public static void Warn(string message) => Log("WARN", message);
    public static void Error(string message) => Log("ERROR", message);
    public static void Error(string message, Exception ex) => Log("ERROR", $"{message}\n  Exception: {ex.GetType().Name}: {ex.Message}\n  StackTrace: {ex.StackTrace}");

    public static string GetLogFilePath() => LogFilePath;

    /// <summary>
    /// 從檔尾往前讀取最近 maxLines 行，避免一次載入整個 5MB 檔案。
    /// </summary>
    public static string ReadRecentLogs(int maxLines = 500)
    {
        try
        {
            if (!File.Exists(LogFilePath))
                return "（無日誌記錄）";

            // 先 flush 暫存寫入，確保讀到最新內容
            lock (_lock)
            {
                try { _writer?.Flush(); } catch { }
            }

            var lines = new List<string>(maxLines);
            // 用 FileStream 反向讀：每次讀一塊緩衝區，從後往前找換行
            using var fs = new FileStream(LogFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length == 0)
                return "（日誌為空）";

            const int BufferSize = 8192;
            var buffer = new byte[BufferSize];
            long pos = fs.Length;
            var tail = new List<byte>();   // 已讀的位元組（反向累積）
            var newlines = 0;

            while (pos > 0 && newlines <= maxLines)
            {
                var read = (int)Math.Min(BufferSize, pos);
                pos -= read;
                fs.Position = pos;
                fs.Read(buffer, 0, read);

                // 由後往前掃換行
                for (var i = read - 1; i >= 0; i--)
                {
                    if (buffer[i] == (byte)'\n')
                    {
                        newlines++;
                        if (newlines > maxLines)
                        {
                            // 這個 \n 之後（不含）的位元組才是要保留的
                            tail.AddRange(ReverseRange(buffer, i + 1, read - 1));
                            goto Done;
                        }
                    }
                }
                // 整個緩衝區都屬於目前這段
                tail.AddRange(ReverseRange(buffer, 0, read - 1));
            }

            Done:
            // tail 是「最後一個位元組 → 開頭」的反向順序，反轉回來
            tail.Reverse();
            var text = Encoding.UTF8.GetString(tail.ToArray());
            // 切掉開頭可能殘留的不完整行
            var firstNl = text.IndexOf('\n');
            if (firstNl >= 0 && firstNl > 0)
                text = text.Substring(firstNl + 1);
            var finalLines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (finalLines.Length == 0)
                return "（日誌為空）";
            return string.Join(Environment.NewLine, finalLines);
        }
        catch (Exception ex)
        {
            return $"讀取日誌失敗: {ex.Message}";
        }
    }

    /// <summary>反向複製 buffer[start..end]（含兩端），作為 List<byte> 的元素加入。</summary>
    private static IEnumerable<byte> ReverseRange(byte[] buffer, int start, int end)
    {
        for (var i = end; i >= start; i--)
            yield return buffer[i];
    }

    public static void Clear()
    {
        try
        {
            lock (_lock)
            {
                try { _writer?.Flush(); } catch { }
                _writer?.Dispose();
                _writer = null;
                _writesSinceFlush = 0;

                if (File.Exists(LogFilePath))
                    File.WriteAllText(LogFilePath, "");
            }
        }
        catch (Exception ex)
        {
            // Clear 失敗不該靜默，但日誌服務本身不能拋出
            try { System.Diagnostics.Debug.WriteLine($"LoggerService.Clear 失敗: {ex.Message}"); } catch { }
        }
    }
}
