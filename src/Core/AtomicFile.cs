using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace WildWasteland.Core
{
    internal static class AtomicFile
    {
        [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool MoveFileEx(string source, string target, uint flags);

        internal static void Replace(string source, string target)
        {
            source = Path.GetFullPath(source); target = Path.GetFullPath(target);
            bool windows = Environment.OSVersion.Platform == PlatformID.Win32NT;
            if (string.Equals(source, target, windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                || !string.Equals(Path.GetDirectoryName(source), Path.GetDirectoryName(target), windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new IOException("Atomic save publication requires distinct files in the same directory");
            if (windows)
            {
                // Avoid Mono's ReplaceFile path (1175). No COPY_ALLOWED: publication stays a same-volume rename.
                if (!MoveFileEx(source, target, 1 | 8))
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new IOException("Atomic save replacement failed: " + Path.GetFileName(target), new Win32Exception(error));
                }
            }
            else if (File.Exists(target)) File.Replace(source, target, null);
            else File.Move(source, target);
        }
    }
}
