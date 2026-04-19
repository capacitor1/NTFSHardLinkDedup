using System;
using System.Collections.Generic;
using System.Text;

namespace NTFSHardLinkDedup.Src
{
    internal class CheckNTFS
    {
        public static bool TryGetDriveLetter(string path, out char driveLetter)
        {
            driveLetter = default;

            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                string fullPath = Path.GetFullPath(path);
                string? root = Path.GetPathRoot(fullPath);

                if (!string.IsNullOrEmpty(root) &&
                    root.Length >= 2 &&
                    root[1] == ':' &&
                    char.IsLetter(root[0]))
                {
                    driveLetter = char.ToUpperInvariant(root[0]);
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
        public static (bool IsNTFS,string Message) IsNtfs(char driveLetter)
        {
            if (!char.IsLetter(driveLetter))
            {
                return (false, $"Invalid DrvLetter '{driveLetter}'");
            }

            try
            {
                string root = char.ToUpperInvariant(driveLetter) + @":\";
                DriveInfo drive = new DriveInfo(root);

                if (!drive.IsReady)
                {
                    return (false, $"Disk {root} is not ready");
                }

                if (!string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
                {
                    return (false,$"Disk {root} is not NTFS but {drive.DriveFormat}");
                }

                return (true,string.Empty);
            }
            catch (Exception ex)
            {
                return (false,$"Exception at checking disk {driveLetter}: {ex.Message}");
            }
        }
    }
}
