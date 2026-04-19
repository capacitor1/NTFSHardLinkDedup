using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace NTFSHardLinkDedup.Src
{
    internal static class FileAssociationHelper
    {
        private const string Extension = ".hlf";
        private const string ProgId = "NTFSHardLinkDedup.HashListFormat";
        private const string Description = "HashList Format";

        internal static void RegisterFileAssociation()
        {
            string exePath = Environment.ProcessPath
                             ?? Process.GetCurrentProcess().MainModule?.FileName
                             ?? throw new InvalidOperationException("Cannot get current process path.");

            if (!File.Exists(exePath))
                return;

            string expectedCommand = $"\"{exePath}\" \"%1\"";
            string expectedIcon = $"{exePath},0";

            using RegistryKey classesRoot = Registry.CurrentUser.CreateSubKey(@"Software\Classes");
            if (classesRoot == null)
                return;

            bool changed = false;

            string? currentProgId = ReadDefault(classesRoot.OpenSubKey(Extension));
            string? currentDescription = ReadDefault(classesRoot.OpenSubKey(ProgId));
            string? currentCommand = ReadDefault(classesRoot.OpenSubKey($@"{ProgId}\shell\open\command"));
            string? currentIcon = ReadDefault(classesRoot.OpenSubKey($@"{ProgId}\DefaultIcon"));

            bool rebuild = false;

            if (!string.Equals(currentProgId, ProgId, StringComparison.Ordinal))
            {
                rebuild = true;
            }
            else
            {
                string? oldExePath = ExtractExePath(currentCommand);
                if (string.IsNullOrWhiteSpace(oldExePath) ||
                    !File.Exists(oldExePath) ||
                    !string.Equals(Path.GetFullPath(oldExePath), Path.GetFullPath(exePath), StringComparison.OrdinalIgnoreCase))
                {
                    rebuild = true;
                }
            }

            if (rebuild)
            {
                changed |= TryDelete(classesRoot, Extension);
                changed |= TryDelete(classesRoot, ProgId);

                using (RegistryKey? extKey = classesRoot.CreateSubKey(Extension))
                {
                    extKey?.SetValue("", ProgId);
                    changed = true;
                }

                using (RegistryKey? progKey = classesRoot.CreateSubKey(ProgId))
                {
                    progKey?.SetValue("", Description);

                    using (RegistryKey? iconKey = progKey?.CreateSubKey("DefaultIcon"))
                        iconKey?.SetValue("", expectedIcon);

                    using (RegistryKey? cmdKey = progKey?.CreateSubKey(@"shell\open\command"))
                        cmdKey?.SetValue("", expectedCommand);

                    changed = true;
                }
            }
            else
            {
                if (!string.Equals(currentDescription, Description, StringComparison.Ordinal))
                {
                    using RegistryKey? progKey = classesRoot.CreateSubKey(ProgId);
                    progKey?.SetValue("", Description);
                    changed = true;
                }

                if (!string.Equals(currentIcon, expectedIcon, StringComparison.OrdinalIgnoreCase))
                {
                    using RegistryKey? iconKey = classesRoot.CreateSubKey($@"{ProgId}\DefaultIcon");
                    iconKey?.SetValue("", expectedIcon);
                    changed = true;
                }

                if (!string.Equals(currentCommand, expectedCommand, StringComparison.OrdinalIgnoreCase))
                {
                    using RegistryKey? cmdKey = classesRoot.CreateSubKey($@"{ProgId}\shell\open\command");
                    cmdKey?.SetValue("", expectedCommand);
                    changed = true;
                }
            }

            if (changed)
            {
                NotifyShell();
            }
        }

        private static string? ReadDefault(RegistryKey? key)
        {
            return key?.GetValue("")?.ToString();
        }

        private static bool TryDelete(RegistryKey parent, string subKey)
        {
            try
            {
                parent.DeleteSubKeyTree(subKey, false);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string? ExtractExePath(string? command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return null;

            command = command.Trim();

            if (command.StartsWith('"'))
            {
                int end = command.IndexOf('"', 1);
                if (end > 1)
                    return command[1..end];
            }

            int exeIndex = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeIndex > 0)
                return command[..(exeIndex + 4)];

            return null;
        }

        private static void NotifyShell()
        {
            const uint SHCNE_ASSOCCHANGED = 0x08000000;
            SHChangeNotify(SHCNE_ASSOCCHANGED, 0, IntPtr.Zero, IntPtr.Zero);
        }

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
    }
}
