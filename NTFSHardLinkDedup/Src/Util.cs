using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace NTFSHardLinkDedup.Src
{
    internal class Util
    {
        public static bool IsRunAsAdministrator()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }
        public static void RestartAsAdministrator()
        {
            try
            {
                string exePath = Environment.ProcessPath!;

                string[] args = Environment.GetCommandLineArgs().Skip(1).ToArray();
                string arguments = string.Join(" ", args.Select(a => $"\"{a}\""));

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = arguments,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Runas admin error: " + ex.Message);
            }

            Environment.Exit(0);
        }
        public static bool TryParseSha256(string? input, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();

            if (input is null || input.Length != 64)
                return false;

            try
            {
                bytes = Convert.FromHexString(input);
                return bytes.Length == 32;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        /// <summary>
        /// 将字节/秒转换为固定单位 MB/s 的字符串。
        /// 例如：15728640 -> "15.00 MB/s"
        /// </summary>
        public static string ToMBpsString(long bytesPerSecond)
        {
            double mbps = bytesPerSecond / 1024d / 1024d;
            return $"{mbps:F2} MB/s";
        }
        public static string FormatBytes(decimal bytes,bool isIEC = false)
        {
            string[] suffixes = isIEC ? ["B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB","ZiB"] : ["B", "KB", "MB", "GB", "TB", "PB", "EB","ZB"];
            int i;
            for (i = 0; i < suffixes.Length && bytes >= (isIEC ? 1024 : 1000); i++)
            {
                bytes /= (isIEC ? 1024 : 1000);
            }
            return $"{bytes:F3} {suffixes[i]}";
        }

        public static string FormatBytesKB(ulong bytes)
        {
            if (bytes == 0)
                return "0 KB";

            ulong kb = (bytes + 1023) / 1024; // 向上取整
            return $"{kb:N0} KB";
        }
        public static void EnsureNormalForReplace(string path)
        {
            FileAttributes attrs = File.GetAttributes(path);

            if ((attrs & FileAttributes.System) != 0)
            {
                throw new IOException("PROTECTED Src because has SYSTEM attr.");
            }

            FileAttributes newAttrs = attrs;

            if ((newAttrs & FileAttributes.ReadOnly) != 0)
                newAttrs &= ~FileAttributes.ReadOnly;

            if ((newAttrs & FileAttributes.Hidden) != 0)
                newAttrs &= ~FileAttributes.Hidden;

            if (newAttrs != attrs)
                File.SetAttributes(path, newAttrs);
        }
    }
}
