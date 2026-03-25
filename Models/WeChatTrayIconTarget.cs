using System.Drawing;

namespace WeChatReminder.Models;

public class WeChatTrayIconTarget
{
    public Rectangle FullRect { get; set; }
    public Rectangle SampleRect { get; set; }
    public Point ClickPoint { get; set; }
    public string Name { get; set; } = string.Empty;
}