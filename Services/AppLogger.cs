using System.IO;
using System.Text;
using System.Threading.Channels;

namespace WeChatReminder.Services;

public static class AppLogger
{
    private static readonly object SyncRoot = new();
    private static readonly Channel<string> PendingLines = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    private static readonly Task WriterTask = Task.Run(ProcessPendingLinesAsync);

    private static string? _lastMessage;
    private static DateTime _lastMessageTime;
    private static int _repeatCount;
    private static bool _isDetailedEnabled = LoadDetailedMode();
    private static int _shutdownRequested;

    public static bool IsDetailedEnabled => _isDetailedEnabled;

    public static void SetDetailedMode(bool enabled)
    {
        lock (SyncRoot)
        {
            _isDetailedEnabled = enabled;
            PersistDetailedMode(enabled);
        }

        LogInfo("logger", enabled ? "Detailed logging enabled." : "Detailed logging disabled.");
    }

    public static void LogInfo(string category, string message)
    {
        Write("INFO", category, message);
    }

    public static void LogDebug(string category, string message)
    {
        if (!_isDetailedEnabled)
            return;

        Write("DEBUG", category, message);
    }

    public static void LogError(string category, string message, Exception ex)
    {
        string details = $"{message} {ex.GetType().Name}: {ex.Message}";
        Write("ERROR", category, details);
    }

    public static void Shutdown()
    {
        if (Interlocked.Exchange(ref _shutdownRequested, 1) == 1)
            return;

        try
        {
            lock (SyncRoot)
            {
                FlushCollapsedMessageCore(DateTime.Now);
            }

            PendingLines.Writer.TryComplete();
            WriterTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }
    }

    private static void Write(string level, string category, string message)
    {
        if (Volatile.Read(ref _shutdownRequested) == 1)
            return;

        try
        {
            lock (SyncRoot)
            {
                DateTime now = DateTime.Now;
                string key = $"[{level}] [{category}] {message}";

                if (_lastMessage == key && now - _lastMessageTime < AppSettings.Logging.RepeatCollapseWindow)
                {
                    _repeatCount++;
                    _lastMessageTime = now;
                    return;
                }

                FlushCollapsedMessageCore(now);

                EnqueueLine($"[{now:yyyy-MM-dd HH:mm:ss.fff}] {key}");
                _lastMessage = key;
                _lastMessageTime = now;
            }
        }
        catch
        {
        }
    }

    private static void FlushCollapsedMessageCore(DateTime now)
    {
        if (_repeatCount <= 0 || _lastMessage == null)
            return;

        EnqueueLine(
            $"[{now:yyyy-MM-dd HH:mm:ss.fff}] [INFO] [logger] previous message repeated {_repeatCount} times");
        _repeatCount = 0;
    }

    private static void EnqueueLine(string line)
    {
        PendingLines.Writer.TryWrite(line);
    }

    private static async Task ProcessPendingLinesAsync()
    {
        try
        {
            await foreach (string line in PendingLines.Reader.ReadAllAsync())
            {
                WriteLineToDisk(line);
            }
        }
        catch
        {
        }
    }

    private static void WriteLineToDisk(string line)
    {
        try
        {
            Directory.CreateDirectory(AppStorage.LogsDirectory);
            RotateIfNeeded(Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length);
            File.AppendAllText(AppStorage.LogFilePath, line + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
        }
    }

    private static void RotateIfNeeded(int upcomingBytes)
    {
        if (!File.Exists(AppStorage.LogFilePath))
            return;

        var fileInfo = new FileInfo(AppStorage.LogFilePath);
        if (fileInfo.Length + upcomingBytes < AppSettings.Logging.MaxLogFileBytes)
            return;

        string archivePath = Path.Combine(
            AppStorage.LogsDirectory,
            $"app_{DateTime.Now:yyyyMMdd_HHmmss}.log");

        File.Move(AppStorage.LogFilePath, archivePath, true);

        var archives = new DirectoryInfo(AppStorage.LogsDirectory)
            .GetFiles("app_*.log")
            .OrderByDescending(x => x.CreationTimeUtc)
            .Skip(AppSettings.Logging.MaxArchiveFiles);

        foreach (var archive in archives)
        {
            archive.Delete();
        }
    }

    private static bool LoadDetailedMode()
    {
        try
        {
            if (!File.Exists(AppStorage.DetailedLoggingFlagPath))
                return false;

            string text = File.ReadAllText(AppStorage.DetailedLoggingFlagPath).Trim();
            return bool.TryParse(text, out bool enabled) && enabled;
        }
        catch
        {
            return false;
        }
    }

    private static void PersistDetailedMode(bool enabled)
    {
        try
        {
            File.WriteAllText(AppStorage.DetailedLoggingFlagPath, enabled.ToString());
        }
        catch
        {
        }
    }
}
