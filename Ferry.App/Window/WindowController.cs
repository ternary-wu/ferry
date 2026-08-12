using System.Runtime.InteropServices;
using System.Text.Json;
using System.Drawing;
using Photino.NET;

namespace Ferry.App.Window;

/// <summary>
/// 窗口控制层（Window Shell）：集中窗口级原生操作。
/// 职责：无边框窗口样式、原生拖动、边缘 Resize、最小/最大/关闭、最小尺寸、DPI、状态持久化。
/// 业务 UI 不直接调用 Photino API，统一经 window.js → 命令协议 → 本控制器。
/// </summary>
public sealed class WindowController
{
    private const int DefaultWidth = 1440;
    private const int DefaultHeight = 900;
    private const int MinWidth = 1200;
    private const int MinHeight = 720;

    private const uint WmNcLButtonDown = 0x00A1;
    private const uint WmGetMinMaxInfo = 0x0024;
    private const uint WmNcHitTest = 0x0084;
    private const int HtCaption = 0x0002;
    private const int HtClient = 0x0001;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;

    private const int GwlStyle = -16;
    private const long WsThickFrame = 0x00040000L;
    private const long WsMinimizeBox = 0x00020000L;
    private const long WsMaximizeBox = 0x00010000L;
    private const long WsCaption = 0x00C00000L;

    private const uint SwpFramechanged = 0x0020;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint MonitorDefaultToNearest = 2;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly PhotinoWindow _window;
    private readonly string _statePath;
    private readonly object _saveLock = new();
    private IntPtr _hwnd = IntPtr.Zero;
    private double _scale = 1.0;
    private int _minW;
    private int _minH;
    private bool _maximized;
    private SUBCLASSPROC? _subclassProc;
    private Timer? _saveTimer;

    public WindowController(PhotinoWindow window)
    {
        _window = window;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _statePath = Path.Combine(appData, "Ferry", "window.json");
    }

    public bool IsMaximized => _window.Maximized;

    /// <summary>注册窗口生命周期事件；须在窗口创建（Load）之前调用。</summary>
    public void Initialize()
    {
        _window.RegisterWindowCreatedHandler(OnWindowCreated);
        _window.RegisterLocationChangedHandler((_, _) => ScheduleSave());
        _window.RegisterSizeChangedHandler((_, _) => ScheduleSave());
        _window.RegisterMaximizedHandler((_, _) => { _maximized = true; ScheduleSave(); });
        _window.RegisterRestoredHandler((_, _) => { _maximized = false; ScheduleSave(); });
        _window.RegisterMinimizedHandler((_, _) => ScheduleSave());
        _window.RegisterWindowClosingHandler(OnWindowClosing);
    }

    public void Minimize() => _window.SetMinimized(true);

    public void ToggleMaximize() => _window.SetMaximized(!_window.Maximized);

    public void Close() => _window.Close();

    /// <summary>
    /// 原生窗口拖动：向系统发送 WM_NCLBUTTONDOWN / HTCAPTION，由 Windows 接管拖动，
    /// 鼠标与窗口 1:1 跟随，自动适配 DPI 缩放与多显示器。
    /// </summary>
    public void BeginNativeDrag()
    {
        var hwnd = GetNativeHandle();
        if (hwnd != IntPtr.Zero)
        {
            GetCursorPos(out var pt);
            var lParam = new IntPtr(((pt.Y & 0xFFFF) << 16) | (pt.X & 0xFFFF));
            SendMessage(hwnd, WmNcLButtonDown, new IntPtr(HtCaption), lParam);
        }
    }

    /// <summary>系统 DPI 缩放系数（GetDpiForSystem / 96），用于初始窗口尺寸换算。</summary>
    public static double GetSystemScaleFactor()
    {
        try
        {
            var dpi = GetDpiForSystem();
            return dpi > 0 ? dpi / 96.0 : 1.0;
        }
        catch
        {
            return 1.0;
        }
    }

    // ---------- 窗口生命周期 ----------

    private void OnWindowCreated(object? sender, EventArgs e)
    {
        try
        {
            _hwnd = _window.WindowHandle;
        }
        catch
        {
            _hwnd = IntPtr.Zero;
        }
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        var dpi = GetDpiForWindow(_hwnd);
        _scale = dpi > 0 ? dpi / 96.0 : GetSystemScaleFactor();
        _minW = (int)Math.Round(MinWidth * _scale);
        _minH = (int)Math.Round(MinHeight * _scale);
        _maximized = _window.Maximized;

        EnsureWindowStyles(_hwnd);
        _subclassProc = SubclassProc;
        SetWindowSubclass(_hwnd, _subclassProc, new UIntPtr(1), UIntPtr.Zero);
        ApplyWindowState();
    }

    private bool OnWindowClosing(object? sender, EventArgs e)
    {
        lock (_saveLock)
        {
            _saveTimer?.Dispose();
            _saveTimer = null;
        }
        SaveStateNow();
        return true;
    }

    private void ScheduleSave()
    {
        lock (_saveLock)
        {
            _saveTimer?.Dispose();
            _saveTimer = new Timer(_ => SaveStateNow(), null, 600, Timeout.Infinite);
        }
    }

    // ---------- 原生窗口样式与消息 ----------

    private void EnsureWindowStyles(IntPtr hwnd)
    {
        var style = GetWindowLong(hwnd, GwlStyle).ToInt64();
        var target = (style | WsThickFrame | WsMinimizeBox | WsMaximizeBox) & ~WsCaption;
        if (target == style)
        {
            return;
        }
        SetWindowLong(hwnd, GwlStyle, new IntPtr(target));
        SetWindowPos(
            hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SwpFramechanged | SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    private IntPtr SubclassProc(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData)
    {
        switch (uMsg)
        {
            case WmGetMinMaxInfo:
                var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                mmi.ptMinTrackSize.X = _minW;
                mmi.ptMinTrackSize.Y = _minH;
                Marshal.StructureToPtr(mmi, lParam, false);
                return IntPtr.Zero;
            case WmNcHitTest:
                if (_maximized)
                {
                    return new IntPtr(HtClient);
                }
                var hit = HitTestNc(lParam);
                if (hit != HtClient)
                {
                    return new IntPtr(hit);
                }
                break;
        }
        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private int HitTestNc(IntPtr lParam)
    {
        var x = (short)((long)lParam & 0xFFFF);
        var y = (short)(((long)lParam >> 16) & 0xFFFF);
        if (!GetWindowRect(_hwnd, out var rect))
        {
            return HtClient;
        }
        var border = Math.Max(4, (int)Math.Round(6 * _scale));
        var left = x >= rect.Left && x < rect.Left + border;
        var right = x > rect.Right - border && x <= rect.Right;
        var top = y >= rect.Top && y < rect.Top + border;
        var bottom = y > rect.Bottom - border && y <= rect.Bottom;

        if (top && left) return HtTopLeft;
        if (top && right) return HtTopRight;
        if (bottom && left) return HtBottomLeft;
        if (bottom && right) return HtBottomRight;
        if (left) return HtLeft;
        if (right) return HtRight;
        if (top) return HtTop;
        if (bottom) return HtBottom;
        return HtClient;
    }

    private IntPtr GetNativeHandle()
    {
        if (_hwnd != IntPtr.Zero)
        {
            return _hwnd;
        }
        try
        {
            var h = _window.WindowHandle;
            if (h != IntPtr.Zero)
            {
                _hwnd = h;
                return h;
            }
        }
        catch
        {
            // 窗口尚未创建时忽略
        }
        return FindMainWindow();
    }

    private static IntPtr FindMainWindow()
    {
        var pid = Environment.ProcessId;
        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var wndPid);
            if (wndPid == pid && IsWindowVisible(hwnd))
            {
                found = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    // ---------- 尺寸 / 位置持久化 ----------

    private void ApplyWindowState()
    {
        var state = LoadState();
        if (state is { Width: > 0, Height: > 0 })
        {
            var left = state.Left;
            var top = state.Top;
            var width = Math.Max(_minW, state.Width);
            var height = Math.Max(_minH, state.Height);
            EnsureVisible(ref left, ref top, ref width, ref height);
            _window.SetSize(width, height);
            _window.SetLocation(new Point(left, top));
            if (state.Maximized)
            {
                _window.SetMaximized(true);
            }
            return;
        }

        var work = GetPrimaryWorkArea();
        var w = Math.Max(_minW, Math.Min((int)Math.Round(DefaultWidth * _scale), work.Right - work.Left));
        var h = Math.Max(_minH, Math.Min((int)Math.Round(DefaultHeight * _scale), work.Bottom - work.Top));
        var l = work.Left + (work.Right - work.Left - w) / 2;
        var t = work.Top + (work.Bottom - work.Top - h) / 2;
        _window.SetSize(w, h);
        _window.SetLocation(new Point(l, t));
    }

    private bool EnsureVisible(ref int left, ref int top, ref int width, ref int height)
    {
        var rect = new RECT
        {
            Left = left,
            Top = top,
            Right = left + width,
            Bottom = top + height
        };
        var monitor = MonitorFromRect(ref rect, MonitorDefaultToNearest);
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return false;
        }
        var interW = Math.Min(rect.Right, info.rcWork.Right) - Math.Max(rect.Left, info.rcWork.Left);
        var interH = Math.Min(rect.Bottom, info.rcWork.Bottom) - Math.Max(rect.Top, info.rcWork.Top);
        if (interW >= 200 && interH >= 120)
        {
            return true;
        }

        width = Math.Min(width, info.rcWork.Right - info.rcWork.Left);
        height = Math.Min(height, info.rcWork.Bottom - info.rcWork.Top);
        left = info.rcWork.Left + (info.rcWork.Right - info.rcWork.Left - width) / 2;
        top = info.rcWork.Top + (info.rcWork.Bottom - info.rcWork.Top - height) / 2;
        return false;
    }

    private RECT GetPrimaryWorkArea()
    {
        var pt = new POINT { X = 0, Y = 0 };
        var monitor = MonitorFromPoint(pt, MonitorDefaultToNearest);
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return new RECT { Right = 1920, Bottom = 1080 };
        }
        return info.rcWork;
    }

    private WindowState? LoadState()
    {
        try
        {
            return File.Exists(_statePath)
                ? JsonSerializer.Deserialize<WindowState>(File.ReadAllText(_statePath))
                : null;
        }
        catch
        {
            return null;
        }
    }

    private void SaveStateNow()
    {
        try
        {
            var state = new WindowState { Maximized = _maximized || _window.Maximized };
            var placementOk = false;
            if (_hwnd != IntPtr.Zero)
            {
                var placement = new WINDOWPLACEMENT { length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>() };
                if (GetWindowPlacement(_hwnd, ref placement))
                {
                    state.Left = placement.rcNormalPosition.Left;
                    state.Top = placement.rcNormalPosition.Top;
                    state.Width = placement.rcNormalPosition.Right - placement.rcNormalPosition.Left;
                    state.Height = placement.rcNormalPosition.Bottom - placement.rcNormalPosition.Top;
                    placementOk = true;
                }
            }
            if (!placementOk)
            {
                state.Left = _window.Left;
                state.Top = _window.Top;
                state.Width = _window.Width;
                state.Height = _window.Height;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
            File.WriteAllText(_statePath, JsonSerializer.Serialize(state, JsonOptions));
        }
        catch
        {
            // 状态持久化失败不阻塞窗口
        }
    }

    // ---------- P/Invoke ----------

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    private delegate IntPtr SUBCLASSPROC(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public uint length;
        public uint flags;
        public uint showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;
    }

    private sealed class WindowState
    {
        public int Left { get; set; }
        public int Top { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool Maximized { get; set; }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    private static extern int GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("comctl32.dll")]
    private static extern bool SetWindowSubclass(
        IntPtr hWnd, SUBCLASSPROC pfnSubclass, UIntPtr uIdSubclass, UIntPtr dwRefData);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref RECT lprc, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    private static IntPtr GetWindowLong(IntPtr hWnd, int nIndex)
        => IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);

    private static IntPtr SetWindowLong(IntPtr hWnd, int nIndex, IntPtr value)
        => IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, value) : SetWindowLong32(hWnd, nIndex, value);
}
