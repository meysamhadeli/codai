using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Codai.Cli;

internal sealed class WindowsConsoleMouseInput
{
    private const uint EnableMouseInput = 0x0010;
    private const uint EnableExtendedFlags = 0x0080;
    private const uint EnableQuickEditMode = 0x0040;
    private readonly nint _handle;
    private readonly uint _originalMode;

    [SupportedOSPlatform("windows")]
    public WindowsConsoleMouseInput()
    {
        _handle = GetStdHandle(-10);
        if (!GetConsoleMode(_handle, out _originalMode))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not read terminal mouse input mode.");
        }
        // Quick Edit captures clicks instead of delivering them to the application.
        if (!SetConsoleMode(_handle, (_originalMode | EnableMouseInput | EnableExtendedFlags) & ~EnableQuickEditMode))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not enable terminal mouse input.");
        }
    }

    [SupportedOSPlatform("windows")]
    public bool TryRead(out TerminalMouseEvent mouse)
    {
        mouse = default;
        while (true)
        {
            if (!PeekConsoleInput(_handle, out var record, 1, out var count))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not inspect terminal input.");
            }
            if (count == 0 || record.Type == 1 && record.KeyDown != 0 && record.VirtualKey is not (16 or 17 or 18))
            {
                return false;
            }
            if (!ReadConsoleInput(_handle, out record, 1, out _))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not read terminal mouse input.");
            }
            if (record.Type != 2)
            {
                continue;
            }
            var action = DecodeAction(record.Buttons, record.Flags);
            if (action is { } value)
            {
                mouse = new TerminalMouseEvent(
                    record.Column - Console.WindowLeft + 1, record.Row - Console.WindowTop + 1, value);
                return true;
            }
        }
    }

    internal static TerminalMouseAction? DecodeAction(uint buttons, uint flags) => flags switch
    {
        4 when (short)(buttons >> 16) > 0 => TerminalMouseAction.ScrollUp,
        4 when (short)(buttons >> 16) < 0 => TerminalMouseAction.ScrollDown,
        0 or 2 when (buttons & 1) != 0 => TerminalMouseAction.Press,
        1 when (buttons & 1) != 0 => TerminalMouseAction.Drag,
        0 when buttons == 0 => TerminalMouseAction.Release,
        _ => null,
    };

    [SupportedOSPlatform("windows")]
    public void Restore()
    {
        if (!SetConsoleMode(_handle, _originalMode))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not restore terminal mouse input mode.");
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 20)]
    private struct InputRecord
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(4)] public int KeyDown;
        [FieldOffset(10)] public ushort VirtualKey;
        [FieldOffset(4)] public short Column;
        [FieldOffset(6)] public short Row;
        [FieldOffset(8)] public uint Buttons;
        [FieldOffset(16)] public uint Flags;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint GetStdHandle(int handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetConsoleMode(nint handle, out uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleMode(nint handle, uint mode);

    [DllImport("kernel32.dll", EntryPoint = "PeekConsoleInputW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekConsoleInput(nint handle, out InputRecord record, uint length, out uint count);

    [DllImport("kernel32.dll", EntryPoint = "ReadConsoleInputW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadConsoleInput(nint handle, out InputRecord record, uint length, out uint count);
}
