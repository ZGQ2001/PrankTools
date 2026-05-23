using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace FakeUpdate;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new UpdateForm());
    }
}

class BlackScreen : Form
{
    public BlackScreen(Rectangle bounds)
    {
        Text = ""; FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual; TopMost = true;
        ShowInTaskbar = false; BackColor = Color.Black; Bounds = bounds;
    }
    protected override CreateParams CreateParams
    { get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; } }
}

class UpdateForm : Form
{
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private static LowLevelKeyboardProc _proc;
    private static IntPtr _hookId = IntPtr.Zero;
    private static UpdateForm? _instance;

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

    private readonly System.Windows.Forms.Timer _progressTimer;
    private readonly System.Windows.Forms.Timer _topMostTimer;
    private readonly List<BlackScreen> _blackScreens = new();
    private readonly Random _rng = new();
    private int _progress;
    private int _dotAngle;

    public UpdateForm()
    {
        _instance = this;
        Text = ""; FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual; TopMost = true;
        ShowInTaskbar = false; DoubleBuffered = true;
        BackColor = Color.Black; // Win11 = black
        Cursor = Cursors.Default;

        _progressTimer = new System.Windows.Forms.Timer { Interval = 300 };
        _progressTimer.Tick += (_, _) =>
        {
            _dotAngle = (_dotAngle + 8) % 360;
            _progress += _rng.Next(0, 2); // much slower
            if (_progress > 99) _progress = 99;
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
            foreach (var bs in _blackScreens)
                if (bs.IsHandleCreated)
                    SetWindowPos(bs.Handle, (IntPtr)HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
        };

        Load += (_, _) =>
        {
            var primary = Screen.PrimaryScreen;
            Bounds = primary!.Bounds;
            SetWindowPos(Handle, (IntPtr)HWND_TOPMOST, Bounds.Left, Bounds.Top, Bounds.Width, Bounds.Height, SWP_SHOWWINDOW);
            SetForegroundWindow(Handle);

            foreach (var s in Screen.AllScreens)
            {
                if (s.Primary) continue;
                var black = new BlackScreen(s.Bounds); black.Show(); _blackScreens.Add(black);
            }

            _progressTimer.Start(); _topMostTimer.Start();
            _proc = HookCallback; _hookId = SetHook(_proc);
        };

        FormClosing += (_, _) =>
        {
            _topMostTimer.Stop(); _progressTimer.Stop();
            foreach (var bs in _blackScreens) { bs.Close(); bs.Dispose(); }
            UnhookWindowsHookEx(_hookId);
        };
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
        UnhookWindowsHookEx(_hookId);
        _progressTimer.Stop(); _topMostTimer.Stop();
        foreach (var bs in _blackScreens) { bs.Close(); bs.Dispose(); }
        Close();
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
        int cx = w / 2;

        // ── Spinning dots circle ──
        int r = 28, dotR = 4, numDots = 7;
        float circleY = (float)(h * 0.32);
        for (int i = 0; i < numDots; i++)
        {
            double angle = (i * 360.0 / numDots + _dotAngle) * Math.PI / 180;
            int dx = cx + (int)(r * Math.Cos(angle));
            int dy = (int)(circleY + r * Math.Sin(angle));
            float alpha = 0.2f + 0.8f * (float)i / numDots;
            using var dotBrush = new SolidBrush(Color.FromArgb((int)(255 * alpha), 255, 255, 255));
            g.FillEllipse(dotBrush, dx - dotR, dy - dotR, dotR * 2, dotR * 2);
        }

        // ── "Working on updates" — centered below circle ──
        float y = circleY + r + 50;
        using var titleFont = new Font("Segoe UI", 24, FontStyle.Regular);
        string title = "Working on updates";
        var ts = g.MeasureString(title, titleFont);
        g.DrawString(title, titleFont, white, (w - ts.Width) / 2, y);

        // ── Bottom area: small percentage + subtext ──
        float botY = h - 80;
        using var pctFont = new Font("Segoe UI", 16, FontStyle.Regular);
        string pct = $"{_progress}% complete";
        var ps = g.MeasureString(pct, pctFont);
        g.DrawString(pct, pctFont, white, (w - ps.Width) / 2, botY);

        using var subFont = new Font("Segoe UI", 13, FontStyle.Regular);
        string sub = "Don't turn off your PC. This will take a while.";
        var ss = g.MeasureString(sub, subFont);
        g.DrawString(sub, subFont, white, (w - ss.Width) / 2, botY + ps.Height + 6);
    }
}
