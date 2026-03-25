using System.IO;

namespace WeChatReminder.Services;

internal static class AppStorage
{
    public static string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WeChatReminder");

    public static string LogsDirectory { get; } = Path.Combine(RootDirectory, "logs");
    public static string LogFilePath { get; } = Path.Combine(LogsDirectory, "app.log");
    public static string DetailedLoggingFlagPath { get; } = PrepareUserFilePath("detailed_logging.txt");
    public static string HotkeyConfigPath { get; } = PrepareUserFilePath("open_wechat_hotkey.txt");

    static AppStorage()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }

    private static string PrepareUserFilePath(string fileName)
    {
        string target = Path.Combine(RootDirectory, fileName);
        string legacy = Path.Combine(AppContext.BaseDirectory, fileName);

        try
        {
            Directory.CreateDirectory(RootDirectory);

            if (!File.Exists(target) && File.Exists(legacy))
                File.Copy(legacy, target, overwrite: false);
        }
        catch
        {
        }

        return target;
    }
}
