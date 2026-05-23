using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ScreenFlicker;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new FlickerForm());
    }
}

class FlickerForm : Form
{
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private static LowLevelKeyboardProc _proc;
    private static IntPtr _hookId = IntPtr.Zero;
    private static FlickerForm? _instance;

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
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);

    private readonly System.Windows.Forms.Timer _flickerTimer;
    private readonly Random _rng = new();
    private int _flickerState; // 0=normal, 1=black, 2=white, 3=static lines

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    public FlickerForm()
    {
        _instance = this;
        Text = ""; FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual; TopMost = true;
        ShowInTaskbar = false; DoubleBuffered = true;
        BackColor = Color.Black;
        Opacity = 0; // start transparent
        Cursor = Cursors.Default;

        _flickerTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _flickerTimer.Tick += (_, _) =>
        {
            // Random flicker behavior
            int r = _rng.Next(100);
            if (r < 30)
            {
                // Brief black flash
                Opacity = 0.6 + _rng.NextDouble() * 0.4;
                BackColor = Color.Black;
                _flickerState = 1;
            }
            else if (r < 35)
            {
                // White flash (like a glitch)
                Opacity = 0.3 + _rng.NextDouble() * 0.3;
                BackColor = Color.White;
                _flickerState = 2;
            }
            else if (r < 38)
            {
                // Colored static lines
                Opacity = 0.4;
                _flickerState = 3;
                Invalidate();
            }
            else
            {
                // Normal — briefly visible or fade out
                Opacity = Math.Max(0, Opacity - 0.05);
                _flickerState = 0;
                Invalidate();
            }

            // Randomly change interval for organic feel
            _flickerTimer.Interval = _rng.Next(30, 200);
        };

        _flickerTimer.Start();

        Load += (_, _) =>
        {
            Bounds = SystemInformation.VirtualScreen;
            SetWindowPos(Handle, (IntPtr)HWND_TOPMOST, Bounds.Left, Bounds.Top, Bounds.Width, Bounds.Height, SWP_SHOWWINDOW);
            _proc = HookCallback; _hookId = SetHook(_proc);
        };

        FormClosing += (_, _) => { _flickerTimer.Stop(); UnhookWindowsHookEx(_hookId); };
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
        UnhookWindowsHookEx(_hookId); _flickerTimer.Stop(); Close();
    }

    protected override CreateParams CreateParams
    { get { var cp = base.CreateParams; cp.ExStyle |= 0x80 | 0x20; return cp; } } // WS_EX_TRANSPARENT for click-through

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_flickerState == 3)
        {
            // Draw random horizontal static lines
            var g = e.Graphics;
            var w = ClientSize.Width;
            var h = ClientSize.Height;
            var colors = new[] { Color.FromArgb(200, 0, 255, 0), Color.FromArgb(200, 255, 0, 255), Color.FromArgb(200, 0, 255, 255) };
            for (int i = 0; i < 20; i++)
            {
                int y = _rng.Next(h);
                int lineH = _rng.Next(1, 5);
                using var pen = new Pen(colors[_rng.Next(colors.Length)], 1);
                g.DrawLine(pen, 0, y, w, y + lineH);
            }
        }
    }
}
