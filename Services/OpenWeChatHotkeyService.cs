using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace WeChatReminder.Services;

public class OpenWeChatHotkeyService
{
    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    private const uint InputKeyboard = 1;
    private const uint KeyEventFKeyUp = 0x0002;

    private static readonly string ConfigPath = AppStorage.HotkeyConfigPath;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    public Keys Shortcut { get; private set; } = Keys.Control | Keys.Alt | Keys.W;

    public OpenWeChatHotkeyService()
    {
        Load();
    }

    public string ShortcutText => FormatShortcut(Shortcut);

    public bool TrySetShortcut(string text, out string normalizedText)
    {
        if (!TryParseShortcut(text, out Keys shortcut))
        {
            normalizedText = ShortcutText;
            return false;
        }

        Shortcut = shortcut;
        normalizedText = ShortcutText;
        Save();
        return true;
    }

    public void Execute()
    {
        AppLogger.LogDebug("hotkey", $"Execute shortcut: {ShortcutText}");

        var modifiers = new List<Keys>();
        if (Shortcut.HasFlag(Keys.Control))
            modifiers.Add(Keys.ControlKey);
        if (Shortcut.HasFlag(Keys.Alt))
            modifiers.Add(Keys.Menu);
        if (Shortcut.HasFlag(Keys.Shift))
            modifiers.Add(Keys.ShiftKey);

        Keys mainKey = Shortcut & Keys.KeyCode;

        foreach (var modifier in modifiers)
        {
            SendSingleInput((ushort)modifier, 0);
            Thread.Sleep(4);
        }

        SendSingleInput((ushort)mainKey, 0);
        Thread.Sleep(12);
        SendSingleInput((ushort)mainKey, KeyEventFKeyUp);
        Thread.Sleep(4);

        for (int i = modifiers.Count - 1; i >= 0; i--)
        {
            SendSingleInput((ushort)modifiers[i], KeyEventFKeyUp);
            Thread.Sleep(4);
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                Save();
                return;
            }

            string text = File.ReadAllText(ConfigPath).Trim();
            if (TryParseShortcut(text, out Keys shortcut))
                Shortcut = shortcut;
        }
        catch (Exception ex)
        {
            AppLogger.LogError("hotkey", "Load failed.", ex);
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(ConfigPath, ShortcutText);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("hotkey", "Save failed.", ex);
        }
    }

    private static bool TryParseShortcut(string? text, out Keys shortcut)
    {
        shortcut = Keys.None;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        Keys result = Keys.None;
        string[] parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return false;

        foreach (string rawPart in parts)
        {
            string part = rawPart.Trim();
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                result |= Keys.Control;
                continue;
            }

            if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                result |= Keys.Alt;
                continue;
            }

            if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                result |= Keys.Shift;
                continue;
            }

            if (!Enum.TryParse(part, true, out Keys key))
                return false;

            result |= key & Keys.KeyCode;
        }

        Keys keyCode = result & Keys.KeyCode;
        if (keyCode == Keys.None)
            return false;

        shortcut = result;
        return true;
    }

    private static string FormatShortcut(Keys shortcut)
    {
        var parts = new List<string>();

        if (shortcut.HasFlag(Keys.Control))
            parts.Add("Ctrl");
        if (shortcut.HasFlag(Keys.Alt))
            parts.Add("Alt");
        if (shortcut.HasFlag(Keys.Shift))
            parts.Add("Shift");

        parts.Add((shortcut & Keys.KeyCode).ToString());
        return string.Join("+", parts);
    }

    private static INPUT CreateKeyInput(ushort virtualKey, uint flags)
    {
        return new INPUT
        {
            type = InputKeyboard,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKey,
                    wScan = 0,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = 0
                }
            }
        };
    }

    private static void SendSingleInput(ushort virtualKey, uint flags)
    {
        var input = CreateKeyInput(virtualKey, flags);
        _ = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }
}
