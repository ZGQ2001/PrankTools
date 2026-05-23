using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace FakeBSOD;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new BSODForm());
    }
}

class BSODForm : Form
{
    // ── P/Invoke ──
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private static LowLevelKeyboardProc _proc;
    private static IntPtr _hookId = IntPtr.Zero;

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private const int HWND_TOPMOST = -1;
    private const int SWP_SHOWWINDOW = 0x0040;
    private const int SWP_NOSIZE = 0x0001;
    private const int SWP_NOMOVE = 0x0002;

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_MAXIMIZE = 3;

    // ── State ──
    private readonly System.Windows.Forms.Timer _progressTimer;
    private readonly System.Windows.Forms.Timer _topMostTimer;
    private int _progress;
    private readonly string _fakeCode = "CRITICAL_PROCESS_DIED";
    private readonly Random _rng = new();

    public BSODForm()
    {
        Text = "";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(0, 120, 212);
        Cursor = Cursors.Default;

        _progressTimer = new System.Windows.Forms.Timer { Interval = 80 };
        _progressTimer.Tick += (_, _) =>
        {
            _progress += _rng.Next(1, 3);
            if (_progress > 100) _progress = 100;
            Invalidate();
        };

        // Re-assert topmost every 500ms — nothing steals our throne
        _topMostTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _topMostTimer.Tick += (_, _) =>
        {
            if (IsHandleCreated)
            {
                SetWindowPos(Handle, (IntPtr)HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                SetForegroundWindow(Handle);
            }
        };

        Load += (_, _) =>
        {
            // Cover ALL monitors
            int minX = 0, minY = 0, maxX = 0, maxY = 0;
            foreach (var screen in Screen.AllScreens)
            {
                if (screen.Bounds.Left < minX) minX = screen.Bounds.Left;
                if (screen.Bounds.Top < minY) minY = screen.Bounds.Top;
                if (screen.Bounds.Right > maxX) maxX = screen.Bounds.Right;
                if (screen.Bounds.Bottom > maxY) maxY = screen.Bounds.Bottom;
            }
            Bounds = new Rectangle(minX, minY, maxX - minX, maxY - minY);

            // Force topmost + foreground
            SetWindowPos(Handle, (IntPtr)HWND_TOPMOST, minX, minY, maxX - minX, maxY - minY, SWP_SHOWWINDOW);
            SetForegroundWindow(Handle);

            _progressTimer.Start();
            _topMostTimer.Start();

            _proc = HookCallback;
            _hookId = SetHook(_proc);
        };

        FormClosing += (_, e) =>
        {
            _topMostTimer.Stop();
            UnhookWindowsHookEx(_hookId);
        };

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F12 && ModifierKeys == (Keys.Control | Keys.Shift))
            {
                UnhookWindowsHookEx(_hookId);
                _progressTimer.Stop();
                _topMostTimer.Stop();
                Close();
            }
        };
    }

    private static IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        return SetWindowsHookEx(WH_KEYBOARD_LL, proc,
            GetModuleHandle(curModule!.ModuleName), 0);
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            if (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN)
            {
                switch (vkCode)
                {
                    case 0xA4: case 0xA5: // Alt
                    case 0x73:             // F4
                    case 0x09:             // Tab
                    case 0x5B: case 0x5C: // Win
                    case 0x1B:             // Escape
                        return (IntPtr)1;
                }
                if ((Control.ModifierKeys & Keys.Alt) != 0)
                    return (IntPtr)1;
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW — hide from Alt+Tab
            return cp;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        var w = ClientSize.Width;
        var h = ClientSize.Height;

        using var faceFont = new Font("Segoe UI", 96, FontStyle.Regular);
        using var faceBrush = new SolidBrush(Color.White);
        var faceText = ":(";
        var faceSize = g.MeasureString(faceText, faceFont);
        float faceX = (w - faceSize.Width) / 2;
        float faceY = h * 0.15f;
        g.DrawString(faceText, faceFont, faceBrush, faceX, faceY);

        using var msgFont = new Font("Segoe UI", 22, FontStyle.Regular);
        string line1 = "Your PC ran into a problem and needs to restart.";
        var line1Size = g.MeasureString(line1, msgFont);
        g.DrawString(line1, msgFont, faceBrush, (w - line1Size.Width) / 2, faceY + faceSize.Height + 30);

        using var subFont = new Font("Segoe UI", 14, FontStyle.Regular);
        string line2 = "We're just collecting some error info, and then we'll restart for you.";
        var line2Size = g.MeasureString(line2, subFont);
        g.DrawString(line2, subFont, faceBrush, (w - line2Size.Width) / 2, faceY + faceSize.Height + 75);

        using var pctFont = new Font("Segoe UI", 18, FontStyle.Regular);
        string pctText = $"If you'd like to know more, wait for the completion... {_progress}% complete";
        var pctSize = g.MeasureString(pctText, pctFont);
        g.DrawString(pctText, pctFont, faceBrush, (w - pctSize.Width) / 2, faceY + faceSize.Height + 130);

        int qrSize = Math.Min(100, w / 8);
        int qrX = w - qrSize - 60;
        int qrY = h - qrSize - 60;
        DrawFakeQR(g, qrX, qrY, qrSize);

        using var qrFont = new Font("Segoe UI", 9, FontStyle.Regular);
        string qrLabel = "For more information, visit:";
        var qrLabelSize = g.MeasureString(qrLabel, qrFont);
        g.DrawString(qrLabel, qrFont, faceBrush, qrX + (qrSize - qrLabelSize.Width) / 2, qrY - 20);

        string qrUrl = "windows.com/stopcode";
        var qrUrlSize = g.MeasureString(qrUrl, qrFont);
        g.DrawString(qrUrl, qrFont, faceBrush, qrX + (qrSize - qrUrlSize.Width) / 2, qrY + qrSize + 5);

        using var stopFont = new Font("Segoe UI", 13, FontStyle.Regular);
        string stopLabel = "Stop code:";
        var stopLabelSize = g.MeasureString(stopLabel, stopFont);
        float stopY = faceY + faceSize.Height + 200;
        g.DrawString(stopLabel, stopFont, faceBrush, (w - stopLabelSize.Width) / 2 - 140, stopY);

        using var codeFont = new Font("Consolas", 13, FontStyle.Regular);
        g.DrawString(_fakeCode, codeFont, faceBrush, (w - stopLabelSize.Width) / 2 - 140 + stopLabelSize.Width + 10, stopY);
    }

    private static void DrawFakeQR(Graphics g, int x, int y, int size)
    {
        int modules = 21;
        float cellSize = (float)size / modules;
        var rng = new Random(42);
        using var whiteBrush = new SolidBrush(Color.White);
        using var blueBrush = new SolidBrush(Color.FromArgb(0, 120, 212));

        DrawFinder(g, x, y, cellSize);
        DrawFinder(g, x + size - 7 * cellSize, y, cellSize);
        DrawFinder(g, x, y + size - 7 * cellSize, cellSize);

        for (int row = 0; row < modules; row++)
        {
            for (int col = 0; col < modules; col++)
            {
                if ((row < 8 && col < 8) || (row < 8 && col > modules - 9) || (row > modules - 9 && col < 8))
                    continue;
                if (row == 6 || col == 6) continue;
                if (rng.Next(2) == 0)
                    g.FillRectangle(whiteBrush, x + col * cellSize, y + row * cellSize, cellSize, cellSize);
            }
        }
    }

    private static void DrawFinder(Graphics g, float x, float y, float c)
    {
        using var w = new SolidBrush(Color.White);
        using var b = new SolidBrush(Color.FromArgb(0, 120, 212));
        g.FillRectangle(w, x, y, 7 * c, 7 * c);
        g.FillRectangle(b, x + c, y + c, 5 * c, 5 * c);
        g.FillRectangle(w, x + 2 * c, y + 2 * c, 3 * c, 3 * c);
    }
}
