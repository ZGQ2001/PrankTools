using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace FakeDiskCleanup;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new CleanupForm());
    }
}

class CleanupForm : Form
{
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private static LowLevelKeyboardProc _proc;
    private static IntPtr _hookId = IntPtr.Zero;
    private static CleanupForm? _instance;

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int HWND_TOPMOST = -1;
    private const int SWP_SHOWWINDOW = 0x0040;
    private const int SWP_NOSIZE = 0x0001;
    private const int SWP_NOMOVE = 0x0002;

    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string lpModuleName);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);

    private readonly System.Windows.Forms.Timer _scanTimer;
    private readonly System.Windows.Forms.Timer _topMostTimer;
    private readonly Random _rng = new();

    private enum Phase { Scanning, Cleaning, Done }
    private Phase _phase = Phase.Scanning;
    private int _progress;
    private string _currentFile = "";
    private long _bytesFound;
    private readonly string[] _fakeFiles = {
        "C:\\Windows\\Temp\\cab_1234.tmp",
        "C:\\Windows\\SoftwareDistribution\\Download\\abc123",
        "C:\\Users\\Default\\AppData\\Local\\Temp\\~DF7892.tmp",
        "C:\\Windows\\Prefetch\\CHROME.EXE-ABC123.pf",
        "C:\\Windows\\System32\\winevt\\Logs\\System.evtx",
        "C:\\ProgramData\\Microsoft\\Windows\\WER\\ReportArchive",
        "C:\\Windows\\Logs\\CBS\\CBS.log",
    };

    public CleanupForm()
    {
        _instance = this;
        Text = ""; FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual; TopMost = true;
        ShowInTaskbar = false; DoubleBuffered = true;
        BackColor = Color.FromArgb(32, 32, 32); // dark, like Disk Cleanup
        Cursor = Cursors.Default;

        _scanTimer = new System.Windows.Forms.Timer { Interval = 80 };
        _scanTimer.Tick += (_, _) =>
        {
            if (_phase == Phase.Scanning)
            {
                _progress += _rng.Next(1, 3);
                _currentFile = _fakeFiles[_rng.Next(_fakeFiles.Length)];
                _bytesFound += _rng.Next(100_000, 5_000_000);
                if (_progress >= 100) { _progress = 100; _phase = Phase.Cleaning; }
            }
            else if (_phase == Phase.Cleaning)
            {
                _progress += _rng.Next(0, 2);
                _bytesFound += _rng.Next(50_000, 2_000_000);
                // Occasionally stall
                if (_rng.Next(15) == 0) _progress = Math.Max(0, _progress - _rng.Next(0, 3));
                if (_progress >= 100) { _progress = 100; _phase = Phase.Done; }
            }
            else if (_phase == Phase.Done)
            {
                _progress = 100;
            }
            Invalidate();
        };

        _topMostTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _topMostTimer.Tick += (_, _) =>
        {
            if (IsHandleCreated)
            {
                SetWindowPos(Handle, (IntPtr)HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                SetForegroundWindow(Handle);
            }
        };

        Load += (_, _) =>
        {
            Bounds = Screen.PrimaryScreen!.Bounds;
            SetWindowPos(Handle, (IntPtr)HWND_TOPMOST, Bounds.Left, Bounds.Top, Bounds.Width, Bounds.Height, SWP_SHOWWINDOW);
            SetForegroundWindow(Handle);
            _scanTimer.Start(); _topMostTimer.Start();
            _proc = HookCallback; _hookId = SetHook(_proc);
        };

        FormClosing += (_, _) => { _scanTimer.Stop(); _topMostTimer.Stop(); UnhookWindowsHookEx(_hookId); };
    }

    private static IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        using var cp = Process.GetCurrentProcess();
        using var cm = cp.MainModule;
        return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(cm!.ModuleName), 0);
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int vk = Marshal.ReadInt32(lParam);
            bool isDown = wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN;

            if (isDown && vk == 0x7B)
            {
                if ((GetAsyncKeyState(0x11) & 0x8000) != 0 && (GetAsyncKeyState(0x10) & 0x8000) != 0)
                { _instance?.BeginInvoke(() => _instance.ExitClean()); return (IntPtr)1; }
            }

            if (isDown)
            {
                switch (vk)
                {
                    case 0xA4: case 0xA5: case 0x73: case 0x09: case 0x5B: case 0x5C: case 0x1B:
                        return (IntPtr)1;
                }
                if ((Control.ModifierKeys & Keys.Alt) != 0) return (IntPtr)1;
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void ExitClean()
    {
        UnhookWindowsHookEx(_hookId); _scanTimer.Stop(); _topMostTimer.Stop(); Close();
    }

    protected override CreateParams CreateParams
    { get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; } }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        var w = ClientSize.Width;
        var h = ClientSize.Height;

        using var white = new SolidBrush(Color.White);
        using var gray = new SolidBrush(Color.FromArgb(180, 180, 180));
        using var green = new SolidBrush(Color.FromArgb(0, 200, 100));
        using var accent = new SolidBrush(Color.FromArgb(0, 120, 215));

        // ── Title bar area ──
        using var titleFont = new Font("Segoe UI", 22, FontStyle.Regular);
        string title = "磁盘清理";
        g.DrawString(title, titleFont, white, 60, 50);

        using var subFont = new Font("Segoe UI", 13, FontStyle.Regular);
        string sub = _phase == Phase.Scanning ? "正在扫描可以清理的文件..." :
                     _phase == Phase.Cleaning ? "正在清理 Windows 更新缓存和临时文件..." :
                     "清理完成。";
        g.DrawString(sub, subFont, gray, 60, 90);

        // ── Progress bar ──
        int barX = 60, barY = 140, barW = w - 120, barH = 8;
        using var barBg = new SolidBrush(Color.FromArgb(60, 60, 60));
        g.FillRectangle(barBg, barX, barY, barW, barH);
        int fillW = (int)(barW * _progress / 100.0);
        if (fillW > 0)
        {
            var fillRect = new Rectangle(barX, barY, fillW, barH);
            using var fillBrush = new LinearGradientBrush(fillRect, Color.FromArgb(0, 120, 215), Color.FromArgb(0, 180, 240), LinearGradientMode.Horizontal);
            g.FillRectangle(fillBrush, fillRect);
        }
        using var pctFont = new Font("Segoe UI", 13, FontStyle.Regular);
        g.DrawString($"{_progress}%", pctFont, gray, w - 100, barY + barH + 10);

        // ── Current file being scanned ──
        using var monoFont = new Font("Consolas", 10, FontStyle.Regular);
        string fileLabel = _phase == Phase.Scanning ? "正在分析:" : _phase == Phase.Cleaning ? "正在删除:" : "";
        g.DrawString(fileLabel, monoFont, gray, 60, 180);
        g.DrawString(_currentFile, monoFont, new SolidBrush(Color.FromArgb(140, 140, 140)), 160, 180);

        // ── Categories list ──
        int listY = 220;
        DrawCategory(g, "Windows 更新清理", "2.47 GB", true, 60, ref listY);
        DrawCategory(g, "临时 Internet 文件", "385 MB", true, 60, ref listY);
        DrawCategory(g, "回收站", "1.12 GB", _phase != Phase.Scanning, 60, ref listY);
        DrawCategory(g, "传递优化文件", "876 MB", true, 60, ref listY);
        DrawCategory(g, "缩略图", "42 MB", true, 60, ref listY);
        DrawCategory(g, "临时文件", "1.85 GB", _phase != Phase.Scanning, 60, ref listY);
        DrawCategory(g, "Windows 错误报告", "623 MB", true, 60, ref listY);

        // ── Total found ──
        double gb = _bytesFound / 1_000_000_000.0;
        using var totalFont = new Font("Segoe UI", 16, FontStyle.Bold);
        string total = $"已找到可清理空间: {gb:F2} GB";
        g.DrawString(total, totalFont, green, 60, h - 80);

        // ── Bottom warning ──
        using var warnFont = new Font("Segoe UI", 11, FontStyle.Regular);
        g.DrawString("请勿关闭计算机。此操作可能需要几分钟。", warnFont, gray, 60, h - 50);
    }

    private static void DrawCategory(Graphics g, string name, string size, bool checked_, int x, ref int y)
    {
        int boxSize = 16;
        using var boxPen = new Pen(Color.FromArgb(150, 150, 150), 1);
        g.DrawRectangle(boxPen, x, y, boxSize, boxSize);
        if (checked_)
        {
            using var checkFont = new Font("Segoe UI", 11, FontStyle.Bold);
            using var checkBrush = new SolidBrush(Color.FromArgb(0, 200, 100));
            g.DrawString("✓", checkFont, checkBrush, x + 1, y - 1);
        }
        using var nameFont = new Font("Segoe UI", 13, FontStyle.Regular);
        using var nameBrush = new SolidBrush(Color.White);
        g.DrawString(name, nameFont, nameBrush, x + 28, y - 2);
        using var sizeFont = new Font("Segoe UI", 11, FontStyle.Regular);
        using var sizeBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
        var ss = g.MeasureString(size, sizeFont);
        g.DrawString(size, sizeFont, sizeBrush, x + 500, y);
        y += 28;
    }
}
