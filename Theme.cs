using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace TraceBoard;

public static class Theme
{
    public static readonly Color Background = Color.FromArgb(14,14,14);
    public static readonly Color Panel = Color.FromArgb(20,20,20);
    public static readonly Color Card = Color.FromArgb(28,28,28);
    public static readonly Color Border = Color.FromArgb(49,49,49);
    public static readonly Color Text = Color.FromArgb(237,237,237);
    public static readonly Color Muted = Color.FromArgb(142,142,142);
    private static readonly PrivateFontCollection Collection = new();
    private static readonly Dictionary<(float, bool), Font> Fonts = [];
    private static readonly List<string> Registered = [];
    public static void Initialize()
    {
        foreach (var name in new[] { "Montserrat-Regular.ttf", "Montserrat-Bold.ttf" })
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", name);
            Collection.AddFontFile(path); AddFontResourceEx(path, 0x10, IntPtr.Zero); Registered.Add(path);
        }
    }
    public static Font Font(float size = 13, bool bold = false)
    {
        if (!Fonts.TryGetValue((size, bold), out var font))
            Fonts[(size, bold)] = font = new Font(Collection.Families[0], size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        return font;
    }
    public static void Release()
    {
        foreach (var font in Fonts.Values) font.Dispose();
        Collection.Dispose(); foreach (var path in Registered) RemoveFontResourceEx(path, 0x10, IntPtr.Zero);
    }
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern int AddFontResourceEx(string name, uint flags, IntPtr reserved);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern bool RemoveFontResourceEx(string name, uint flags, IntPtr reserved);
    public static string Kind(CardKind kind) => kind switch { CardKind.Person => "ПЕРСОНА", CardKind.Evidence => "УЛИКА", CardKind.Place => "МЕСТО", _ => "ЗАМЕТКА" };
    public static Button Button(string text, Action action, bool primary = false, int width = 108)
    {
        var button = new Button { Text = text, Size = new Size(width,34), FlatStyle = FlatStyle.Flat, BackColor = primary ? Text : Card, ForeColor = primary ? Background : Text, Font = Font(12, true), Cursor = Cursors.Hand, Margin = new Padding(0,0,8,0), UseCompatibleTextRendering = true };
        button.FlatAppearance.BorderSize = 0; button.FlatAppearance.MouseOverBackColor = primary ? Color.White : Color.FromArgb(44,44,44);
        button.Click += (_, _) => action(); return button;
    }
    public static Label Label(string text, float size = 13, bool bold = false) => new() { Text = text, ForeColor = Text, BackColor = Color.Transparent, Font = Font(size,bold), AutoSize = false, UseCompatibleTextRendering = true };
    public static TextBox Input(bool multiline = false) => new() { BackColor = Card, ForeColor = Text, BorderStyle = BorderStyle.FixedSingle, Font = Font(14), Multiline = multiline, ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None };
    public static void RoundFill(Graphics g, RectangleF rect, Color color, float radius = 7)
    {
        using var brush = new SolidBrush(color);
        using var path = Rounded(rect,radius); g.FillPath(brush,path);
    }
    public static GraphicsPath Rounded(RectangleF r,float radius)
    {
        var path = new GraphicsPath(); float d = radius * 2;
        path.AddArc(r.Left,r.Top,d,d,180,90); path.AddArc(r.Right-d,r.Top,d,d,270,90);
        path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); path.AddArc(r.Left,r.Bottom-d,d,d,90,90); path.CloseFigure(); return path;
    }
    public static void DrawText(Graphics g,string text,RectangleF bounds,float size,Color color,bool bold=false)
    {
        using var brush = new SolidBrush(color);
        using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.LineLimit };
        g.DrawString(text,Font(size,bold),brush,bounds,format);
    }
}
