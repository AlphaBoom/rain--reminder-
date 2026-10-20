using System.Runtime.InteropServices;
using System.Text;

namespace RainReminder.Tray;

internal static class Desktop
{
    // Query only: never switch desktops or inject UI into the lock screen.
    public static bool IsUnlocked()
    {
        var handle = OpenInputDesktop(0, false, 1); // DESKTOP_READOBJECTS
        if (handle == IntPtr.Zero) return false;
        try
        {
            var name = new StringBuilder(256);
            return GetUserObjectInformation(handle, 2, name, name.Capacity * 2, out _)
                && name.ToString().Equals("Default", StringComparison.OrdinalIgnoreCase);
        }
        finally { CloseDesktop(handle); }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint access);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder info, int length, out int needed);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(IntPtr handle);
}
