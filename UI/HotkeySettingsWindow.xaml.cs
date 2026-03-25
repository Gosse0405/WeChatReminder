using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using FormsKeys = System.Windows.Forms.Keys;

namespace WeChatReminder.UI;

public partial class HotkeySettingsWindow : Window
{
    private bool _isRecording;

    public string ShortcutText => ShortcutTextBox.Text.Trim();

    public HotkeySettingsWindow(string currentShortcut)
    {
        InitializeComponent();
        ShortcutTextBox.Text = currentShortcut;
        Loaded += (_, _) =>
        {
            ShortcutTextBox.Focus();
            ShortcutTextBox.SelectAll();
        };
    }

    public void ShowValidationError(string message)
    {
        HintText.Text = message;
        HintText.Foreground = System.Windows.Media.Brushes.IndianRed;
    }

    private void Record_Click(object sender, RoutedEventArgs e)
    {
        _isRecording = !_isRecording;
        RecordButton.Content = _isRecording ? "录制中..." : "开始录制";
        HintText.Text = _isRecording
            ? "请按下组合键，例如 Ctrl+Alt+W。"
            : "支持 Ctrl、Alt、Shift 与单个主键组合。";
        HintText.Foreground = System.Windows.Media.Brushes.DimGray;
        ShortcutTextBox.Focus();
        ShortcutTextBox.SelectAll();
    }

    private void ShortcutTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_isRecording)
            return;

        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            _isRecording = false;
            RecordButton.Content = "开始录制";
            HintText.Text = "已取消录制。";
            HintText.Foreground = System.Windows.Media.Brushes.DimGray;
            return;
        }

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift)
            return;

        FormsKeys shortcut = FormsKeys.None;
        ModifierKeys modifiers = Keyboard.Modifiers;

        if (modifiers.HasFlag(ModifierKeys.Control))
            shortcut |= FormsKeys.Control;
        if (modifiers.HasFlag(ModifierKeys.Alt))
            shortcut |= FormsKeys.Alt;
        if (modifiers.HasFlag(ModifierKeys.Shift))
            shortcut |= FormsKeys.Shift;

        shortcut |= (FormsKeys)KeyInterop.VirtualKeyFromKey(key);
        ShortcutTextBox.Text = FormatShortcut(shortcut);

        _isRecording = false;
        RecordButton.Content = "开始录制";
        HintText.Text = "已录制快捷键，可以直接保存。";
        HintText.Foreground = System.Windows.Media.Brushes.DimGray;
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _isRecording = false;
        RecordButton.Content = "开始录制";
        ShortcutTextBox.Text = "Ctrl+Alt+W";
        HintText.Text = "已恢复默认快捷键。";
        HintText.Foreground = System.Windows.Media.Brushes.DimGray;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private static string FormatShortcut(FormsKeys shortcut)
    {
        var parts = new List<string>();

        if (shortcut.HasFlag(FormsKeys.Control))
            parts.Add("Ctrl");
        if (shortcut.HasFlag(FormsKeys.Alt))
            parts.Add("Alt");
        if (shortcut.HasFlag(FormsKeys.Shift))
            parts.Add("Shift");

        parts.Add((shortcut & FormsKeys.KeyCode).ToString());
        return string.Join("+", parts);
    }
}
