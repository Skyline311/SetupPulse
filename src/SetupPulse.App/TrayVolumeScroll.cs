using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SetupPulse.App;

/// <summary>Optional wheel input scoped to this NotifyIcon's current shell rectangle.</summary>
internal sealed class TrayVolumeScroll : IDisposable
{
    const int WhMouseLl = 14, WmMouseWheel = 0x020A, WheelDelta = 120;
    readonly NotifyIcon icon;
    readonly Action<int> onSteps;
    readonly FieldInfo windowField;
    readonly uint iconId;
    readonly HookProc hookProc;
    IntPtr hook;
    int remainder;
    bool disposed;

    public TrayVolumeScroll(NotifyIcon icon, Action<int> onSteps)
    {
        ArgumentNullException.ThrowIfNull(icon);
        ArgumentNullException.ThrowIfNull(onSteps);
        this.icon = icon;
        this.onSteps = onSteps;
        hookProc = HandleMouse;

        // .NET 10 NotifyIcon uses these exact fields for NOTIFYICONDATAW.hWnd/uID:
        // https://github.com/dotnet/winforms/blob/v10.0.0/src/System.Windows.Forms/System/Windows/Forms/NotifyIcon.cs
        // Reject unknown layouts instead of searching other tray icons or windows.
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var idField = typeof(NotifyIcon).GetField("_id", flags);
        windowField = typeof(NotifyIcon).GetField("_window", flags)
            ?? throw UnsupportedLayout();
        if (typeof(NotifyIcon).Assembly.GetName().Version?.Major != 10 ||
            idField?.FieldType != typeof(uint) || !idField.IsInitOnly ||
            !typeof(NativeWindow).IsAssignableFrom(windowField.FieldType))
            throw UnsupportedLayout();

        try
        {
            if (idField.GetValue(icon) is not uint id || id == 0 ||
                windowField.GetValue(icon) is not NativeWindow window || window.Handle == IntPtr.Zero)
                throw UnsupportedLayout();
            iconId = id;
            uint ownerThread = GetWindowThreadProcessId(window.Handle, out uint ownerProcess);
            if (!Application.MessageLoop || ownerThread != GetCurrentThreadId() || ownerProcess != Environment.ProcessId)
                throw new InvalidOperationException("Enable tray mouse-wheel control on the tray icon's Windows Forms UI thread after its message loop has started.");
        }
        catch (MemberAccessException ex)
        {
            throw new NotSupportedException("The Windows Forms tray icon identity is unavailable. Disable tray mouse-wheel control for this runtime.", ex);
        }

        hook = SetWindowsHookExW(WhMouseLl, hookProc, GetModuleHandleW(null), 0);
        if (hook == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not enable tray mouse-wheel control. Disable the option or restart SetupPulse and try again.");
    }

    static NotSupportedException UnsupportedLayout() => new("Tray mouse-wheel control requires the verified .NET 10 Windows Forms NotifyIcon layout and a created tray icon. Disable this option for the current runtime.");

    IntPtr HandleMouse(int code, IntPtr message, IntPtr data)
    {
        if (code < 0 || message != WmMouseWheel || disposed)
            return CallNextHookEx(hook, code, message, data);

        try
        {
            // The native window can be recreated, so never retain an old HWND.
            if (!icon.Visible || windowField.GetValue(icon) is not NativeWindow window ||
                window.Handle == IntPtr.Zero || !GetCursorPos(out var cursor))
                return PassThrough();

            var identifier = new NotifyIconIdentifier
            {
                Size = (uint)Marshal.SizeOf<NotifyIconIdentifier>(),
                Window = window.Handle,
                Id = iconId
            };
            if (Shell_NotifyIconGetRect(in identifier, out var rectangle) < 0 ||
                cursor.X < rectangle.Left || cursor.X >= rectangle.Right ||
                cursor.Y < rectangle.Top || cursor.Y >= rectangle.Bottom)
                return PassThrough();

            var mouse = Marshal.PtrToStructure<MouseHookData>(data);
            int delta = unchecked((short)(mouse.MouseData >> 16));
            remainder += delta;
            int steps = remainder / WheelDelta;
            remainder %= WheelDelta;
            // The supplied callback runs on the hook's UI thread; it should queue any slow work.
            if (steps != 0) onSteps(steps);
            return new IntPtr(1);
        }
        catch
        {
            // Managed exceptions must never cross the native hook boundary.
            return PassThrough();
        }

        IntPtr PassThrough()
        {
            remainder = 0;
            return CallNextHookEx(hook, code, message, data);
        }
    }

    public void Dispose()
    {
        disposed = true;
        remainder = 0;
        if (hook != IntPtr.Zero)
        {
            if (!UnhookWindowsHookEx(hook))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not release tray mouse-wheel control. Restart SetupPulse to release the hook.");
            hook = IntPtr.Zero;
        }
        GC.KeepAlive(hookProc);
        GC.SuppressFinalize(this);
    }

    ~TrayVolumeScroll()
    {
        if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    struct MouseHookData
    {
        public Point Point;
        public uint MouseData, Flags, Time;
        public UIntPtr ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct NotifyIconIdentifier
    {
        public uint Size;
        public IntPtr Window;
        public uint Id;
        public Guid Guid;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookExW(int hookKind, HookProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetCursorPos(out Point point);
    [DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr GetModuleHandleW(string? moduleName);
    [DllImport("shell32.dll")]
    static extern int Shell_NotifyIconGetRect(in NotifyIconIdentifier identifier, out Rect rectangle);
}
