using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace Dashboard.BA
{
    /// <summary>
    /// Creates a short-lived, machine-DPAPI encrypted credential package for
    /// the elevated MSI helper. Plain-text credentials never enter Burn/MSI
    /// variables, command lines, configuration files, or setup logs.
    /// </summary>
    internal static class CredentialStager
    {
        internal static string Create(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentException("A Laserfiche username is required.", nameof(username));
            byte[] plain = Encoding.UTF8.GetBytes(username.Trim() + "\n" + password);
            byte[] encrypted = Array.Empty<byte>();
            try
            {
                encrypted = ProtectedData.Protect(
                    plain,
                    optionalEntropy: null,
                    scope: DataProtectionScope.LocalMachine);

                string directory = Path.Combine(
                    Path.GetTempPath(),
                    "LaserficheDashboardSetup",
                    Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                RestrictDirectory(directory);

                string path = Path.Combine(directory, "credentials.dpapi.pending");
                File.WriteAllBytes(path, encrypted);
                RestrictFile(path);
                return path;
            }
            finally
            {
                Array.Clear(plain, 0, plain.Length);
                if (encrypted.Length > 0)
                    Array.Clear(encrypted, 0, encrypted.Length);
            }
        }

        internal static void TryDelete(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                if (File.Exists(path)) File.Delete(path);
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // The elevated helper may still own the file. It also deletes
                // the package immediately after importing it successfully.
            }
        }

        private static void RestrictDirectory(string directory)
        {
            try
            {
                var security = new DirectorySecurity();
                security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                AddAllowedPrincipals(security);
                Directory.SetAccessControl(directory, security);
            }
            catch
            {
                // DPAPI encryption remains the security boundary if ACL
                // hardening is unavailable on an unusual Windows filesystem.
            }
        }

        private static void RestrictFile(string path)
        {
            try
            {
                var security = new FileSecurity();
                security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                AddAllowedPrincipals(security);
                File.SetAccessControl(path, security);
            }
            catch
            {
                // See RestrictDirectory.
            }
        }

        private static void AddAllowedPrincipals(FileSystemSecurity security)
        {
            var current = WindowsIdentity.GetCurrent().User;
            if (current != null)
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    current, FileSystemRights.FullControl, AccessControlType.Allow));
            }

            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl,
                AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.FullControl,
                AccessControlType.Allow));
        }
    }
}
