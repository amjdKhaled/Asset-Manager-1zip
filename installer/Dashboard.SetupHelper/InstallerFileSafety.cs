using System;
using System.IO;

namespace Dashboard.SetupHelper
{
    internal static class InstallerFileSafety
    {
        internal static string FullPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
                throw new IOException("An absolute path is required.");
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        internal static void RequireSamePath(string actual, string expected)
        {
            if (!string.Equals(FullPath(actual), FullPath(expected), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Refused a path outside the dedicated Dashboard location: " + actual);
            EnsureNoReparsePoints(actual);
        }

        // Check the file AND every ancestor, including ancestors of a missing target.
        // Never follow junctions/symlinks into Laserfiche or another application's tree.
        internal static void EnsureNoReparsePoints(string path)
        {
            string current = Path.GetFullPath(path);
            while (!string.IsNullOrEmpty(current))
            {
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Refused a reparse point: " + current);
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                string? parent = Path.GetDirectoryName(current);
                if (string.Equals(parent, current, StringComparison.OrdinalIgnoreCase)) break;
                current = parent ?? "";
            }
        }

        internal static void WriteBytesAtomic(string path, byte[] bytes)
        {
            EnsureNoReparsePoints(path);
            string temporary = path + ".dashboard-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush();
                }
                EnsureNoReparsePoints(path);
                // Replace the directory entry rather than modifying an existing
                // hard-linked file in place.
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
