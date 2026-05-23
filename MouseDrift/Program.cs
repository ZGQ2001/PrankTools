using System.Runtime.InteropServices;

namespace MouseDrift;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new DriftContext());
    }
}

class DriftContext : ApplicationContext
{
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private struct POINT { public int X; public int Y; }

    private readonly System.Windows.Forms.Timer _driftTimer;
    private readonly System.Windows.Forms.Timer _dirTimer;
    private readonly Random _rng = new();
    private float _dirX, _dirY;
    private readonly NotifyIcon _tray;

    public DriftContext()
    {
        // Random initial direction
        ChangeDirection();

        _dirTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _dirTimer.Tick += (_, _) => ChangeDirection();
        _dirTimer.Start();

        _driftTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _driftTimer.Tick += (_, _) =>
        {
            // Secret exit: Ctrl+Shift+F11
            if ((GetAsyncKeyState(0x11) & 0x8000) != 0 &&   // Ctrl
                (GetAsyncKeyState(0x10) & 0x8000) != 0 &&    // Shift
                (GetAsyncKeyState(0x7B) & 0x8000) != 0)      // F12
            {
                Exit();
                return;
            }

            if (!GetCursorPos(out var pt)) return;

            // Drift: 1-3 pixels per tick in current direction
            int dx = (int)(_dirX * _rng.Next(1, 4));
            int dy = (int)(_dirY * _rng.Next(1, 4));

            // Add micro-jitter so it doesn't look perfectly smooth
            dx += _rng.Next(-1, 2);
            dy += _rng.Next(-1, 2);

            SetCursorPos(pt.X + dx, pt.Y + dy);
        };
        _driftTimer.Start();

        // Tray icon so user knows it's running (and can exit)
        _tray = new NotifyIcon
        {
            Text = "Mouse Drift — Ctrl+Shift+F11 to exit",
            Visible = true,
            Icon = SystemIcons.Information
        };
        _tray.DoubleClick += (_, _) => Exit();
    }

    private void ChangeDirection()
    {
        // Random angle, bias slightly toward one quadrant for a while
        double angle = _rng.NextDouble() * 2 * Math.PI;
        _dirX = (float)Math.Cos(angle);
        _dirY = (float)Math.Sin(angle);
    }

    private void Exit()
    {
        _driftTimer.Stop();
        _dirTimer.Stop();
        _tray.Visible = false;
        _tray.Dispose();
        Application.Exit();
    }
}
