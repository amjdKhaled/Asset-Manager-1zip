// PathUtil.cs
// Defensive sanitization of path arguments received from MSI ExeCommand.
//
// WHY THIS EXISTS (the 0x80070643 / Error 1722 root cause):
//   MSI directory properties like [WEBAPPFOLDER] always end with a backslash.
//   When the WXS author writes:
//       --webapp-path "[WEBAPPFOLDER]"
//   the expanded command line becomes:
//       --webapp-path "C:\Program Files\Dashboard\WebApp\"
//   Windows command-line parsing (CommandLineToArgvW) treats the trailing \"
//   as an ESCAPED LITERAL QUOTE, so the argv value the helper receives is:
//       C:\Program Files\Dashboard\WebApp"
//   On .NET Framework 4.8, Path.Combine() then throws
//   ArgumentException("Illegal characters in path.") which previously bubbled
//   to Program.Main's catch and returned exit code 1 -- rolling back the
//   entire installation.
//
//   The WXS has been fixed to pass "[WEBAPPFOLDER]." (trailing dot neutralizes
//   the backslash-quote), but this sanitizer keeps the helper robust against
//   any caller that still passes a trailing-backslash-quoted path.

using System;
using System.IO;
using System.Linq;

namespace Dashboard.SetupHelper
{
    internal static class PathUtil
    {
        // Sanitizes a directory path argument:
        //   1. Strips stray double-quote characters injected by \" escaping.
        //   2. Strips all other invalid path characters.
        //   3. Trims whitespace and trailing directory separators.
        //   4. Removes a trailing "\." left by the WXS "[DIR]." pattern.
        // Returns "" for null/empty input.
        public static string SanitizeDir(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";

            char[] invalid = Path.GetInvalidPathChars();
            string cleaned = new string(
                raw!.Where(c => c != '"' && !invalid.Contains(c)).ToArray());

            cleaned = cleaned.Trim();

            // "[DIR]." expands to "C:\...\Dir\." -- normalize away the "\.".
            while (cleaned.EndsWith("\\.") || cleaned.EndsWith("/."))
                cleaned = cleaned.Substring(0, cleaned.Length - 2);

            return cleaned.TrimEnd('\\', '/');
        }
    }
}
