using System.Runtime.InteropServices;

namespace SetupPulse.App;

// Active only while a shortcut input is focused, and only in this app's foreground window.
internal sealed class ShortcutCapture(IntPtr window, Action<string> captured) : IDisposable
{
    delegate IntPtr Hook(int code, IntPtr message, IntPtr data);
    IntPtr handle;
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int type, Hook callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
    Hook? retained;
    public void Start()
    {
        retained = OnKey;
        handle = SetWindowsHookEx(13, retained, GetModuleHandle(null), 0);
        if (handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Shortcut-Erfassung konnte nicht gestartet werden.");
    }
    IntPtr OnKey(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && GetForegroundWindow() == window && (message == 0x100 || message == 0x104))
        {
            int key = Marshal.ReadInt32(data);
            bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
            bool ctrl = Down(0x11), alt = Down(0x12), shift = Down(0x10), win = Down(0x5b) || Down(0x5c);
            string? name = key is >= 0x60 and <= 0x69 ? "Num" + (key - 0x60)
                : key is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5a ? ((char)key).ToString()
                : key is >= 0x70 and <= 0x87 ? "F" + (key - 0x6f) : null;
            if (name != null && (ctrl || alt || win || shift))
            {
                var parts = new List<string>(); if (ctrl) parts.Add("Ctrl"); if (alt) parts.Add("Alt"); if (shift) parts.Add("Shift"); if (win) parts.Add("Win"); parts.Add(name);
                captured(string.Join('+', parts)); return new IntPtr(1);
            }
        }
        return CallNextHookEx(handle, code, message, data);
    }
    public void Dispose() { if (handle != IntPtr.Zero) { UnhookWindowsHookEx(handle); handle = IntPtr.Zero; } retained = null; }
}
