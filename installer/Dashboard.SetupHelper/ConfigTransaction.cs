using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Dashboard.SetupHelper
{
    internal static class ConfigTransaction
    {
        private sealed class State
        {
            internal byte[]? Before;
            internal byte[]? Expected;
        }
        private static string? _journal;
        private static State[]? _states;

        private static string[] Paths()
        {
            string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Dashboard");
            string web = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Dashboard", "WebApp");
            return new[] {
                Path.Combine(data, "extension.config.json"),
                Path.Combine(data, "laserfiche.config.json"),
                Path.Combine(data, "laserfiche.runtime.json"),
                Path.Combine(data, "credentials", "37a8eec1ce19687d132fe29051dca629d164e2c4958ba141d5f4133a33f0688f.dpapi"),
                Path.Combine(web, "appsettings.json")
            };
        }

        internal static void Begin(Dictionary<string, string> opts)
        {
            string id;
            // Direct development smoke tests intentionally do not use MSI journals.
            if (!opts.TryGetValue("transaction", out id) || string.IsNullOrEmpty(id)) return;
            string journal = InstallerJournal.PathFor(opts, "config");
            if (File.Exists(journal)) throw new IOException("The configuration transaction already exists.");
            string[] paths = Paths();
            var states = new State[paths.Length];
            for (int i = 0; i < paths.Length; i++)
            {
                InstallerFileSafety.EnsureNoReparsePoints(paths[i]);
                byte[]? before = File.Exists(paths[i]) ? File.ReadAllBytes(paths[i]) : null;
                states[i] = new State { Before = before, Expected = before };
            }
            _journal = journal;
            _states = states;
            Save();
        }

        internal static void Write(string path, byte[] bytes)
        {
            if (_states != null)
            {
                string[] paths = Paths();
                int index = Array.FindIndex(paths, p => string.Equals(Path.GetFullPath(path), p, StringComparison.OrdinalIgnoreCase));
                if (index < 0) throw new IOException("Configuration write is outside the Dashboard transaction.");
                // Persist the expected output BEFORE replacing the target. A crash
                // between these steps leaves rollback able to identify our write.
                _states[index].Expected = bytes;
                Save();
            }
            InstallerFileSafety.WriteBytesAtomic(path, bytes);
        }

        internal static int Rollback(Dictionary<string, string> opts)
        {
            string journal = InstallerJournal.PathFor(opts, "config");
            if (!File.Exists(journal)) return 0;
            string[] paths = Paths();
            using (var reader = new BinaryReader(File.OpenRead(journal)))
            {
                if (reader.ReadString() != "DashboardConfigTransaction-v1" || reader.ReadInt32() != paths.Length)
                    throw new IOException("Invalid configuration rollback journal.");
                for (int i = 0; i < paths.Length; i++)
                {
                    byte[]? before = Read(reader);
                    byte[]? expected = Read(reader);
                    InstallerFileSafety.EnsureNoReparsePoints(paths[i]);
                    byte[]? current = File.Exists(paths[i]) ? File.ReadAllBytes(paths[i]) : null;
                    if (!Equal(current, expected))
                    {
                        SetupLog.Warn("Preserved a concurrent configuration change: " + paths[i]);
                        continue;
                    }
                    if (Equal(current, before)) continue;
                    if (before == null) File.Delete(paths[i]);
                    else InstallerFileSafety.WriteBytesAtomic(paths[i], before);
                }
            }
            File.Delete(journal);
            return 0;
        }

        internal static int Commit(Dictionary<string, string> opts)
        {
            string journal = InstallerJournal.PathFor(opts, "config");
            if (File.Exists(journal)) File.Delete(journal);
            return 0;
        }

        private static void Save()
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
                {
                    writer.Write("DashboardConfigTransaction-v1");
                    writer.Write(_states!.Length);
                    foreach (State state in _states)
                    {
                        Write(writer, state.Before);
                        Write(writer, state.Expected);
                    }
                }
                InstallerFileSafety.WriteBytesAtomic(_journal!, stream.ToArray());
            }
        }

        private static bool Equal(byte[]? left, byte[]? right)
        {
            return left == null ? right == null : right != null && left.SequenceEqual(right);
        }
        private static void Write(BinaryWriter writer, byte[]? bytes)
        {
            writer.Write(bytes == null ? -1 : bytes.Length);
            if (bytes != null) writer.Write(bytes);
        }
        private static byte[]? Read(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length == -1) return null;
            if (length < 0 || length > 32 * 1024 * 1024) throw new IOException("Invalid journal length.");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            return bytes;
        }
    }
}

