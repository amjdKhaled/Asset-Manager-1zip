// JsonHelpers.cs
// Shared JSON string helpers used by WriteConfigAction and linked into
// Dashboard.SetupHelper.Tests via <Compile Include> source-linking.
//
// WHY A SEPARATE FILE:
//   SetJsonStringField and EscJson are the core logic that port preservation
//   depends on.  Keeping them in a file that the test project compiles directly
//   (rather than copying the code) means any change to either helper is
//   immediately reflected in the tests without a manual sync step.
//
// USAGE IN TESTS:
//   Dashboard.SetupHelper.Tests.csproj links this file with
//     <Compile Include="..\Dashboard.SetupHelper\JsonHelpers.cs" />
//   The test project compiles it as part of its own assembly, so 'internal'
//   access works without InternalsVisibleTo.

using System;
using System.IO;
using System.Text;

namespace Dashboard.SetupHelper
{
    /// <summary>
    /// Minimal JSON string field helpers that work without any external
    /// dependency.  Designed for appsettings.json and laserfiche.config.json
    /// which always contain well-formed, top-level string fields.
    /// </summary>
    internal static class JsonHelpers
    {
        // Sets (or adds) a top-level JSON string field in a JSON document.
        // Uses simple string scanning — avoids any JSON library dependency.
        //
        // If the field already exists its value is replaced in-place.
        // If it does not exist it is inserted before the closing '}' of the
        // outermost object.
        //
        // Limitations: works correctly only for top-level string fields in a
        // well-formed JSON object.  The appsettings.json written by the SDK
        // publish always satisfies this constraint.
        internal static string SetJsonStringField(string json, string fieldName, string value)
        {
            string escapedValue = EscJson(value);
            string fieldKey     = $"\"{fieldName}\"";

            int idx = json.IndexOf(fieldKey, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                // Field exists — find the colon, then the opening quote of the value,
                // then the closing quote, and replace just the value portion.
                int colon = json.IndexOf(':', idx + fieldKey.Length);
                if (colon >= 0)
                {
                    int openQuote = json.IndexOf('"', colon + 1);
                    if (openQuote >= 0)
                    {
                        int closeQuote = json.IndexOf('"', openQuote + 1);
                        if (closeQuote >= 0)
                        {
                            return json.Substring(0, openQuote + 1)
                                 + escapedValue
                                 + json.Substring(closeQuote);
                        }
                    }
                }
            }

            // Field not present — insert before the last closing brace.
            int lastBrace = json.LastIndexOf('}');
            if (lastBrace < 0)
                return json; // malformed; leave unchanged

            // Determine whether a trailing comma is needed (i.e. there is
            // already at least one field in the object).
            string before = json.Substring(0, lastBrace).TrimEnd();
            string insert  = $",\r\n  {fieldKey}: \"{escapedValue}\"\r\n";
            // If the object is empty (just '{' + optional whitespace) use no comma.
            if (before.EndsWith("{"))
                insert = $"\r\n  {fieldKey}: \"{escapedValue}\"\r\n";

            return before + insert + json.Substring(lastBrace);
        }

        // Extracts the value of a JSON string or number field by name.
        // Returns null if the field is not found.
        internal static string? ExtractJsonString(string json, string fieldName)
        {
            string search = $"\"{fieldName}\":";
            int idx = json.IndexOf(search, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            int valueStart = idx + search.Length;
            // skip whitespace
            while (valueStart < json.Length && json[valueStart] == ' ') valueStart++;
            if (valueStart >= json.Length) return null;
            if (json[valueStart] == '"')
            {
                // String value
                int end = json.IndexOf('"', valueStart + 1);
                if (end < 0) return null;
                return json.Substring(valueStart + 1, end - valueStart - 1)
                           .Replace("\\\"", "\"")
                           .Replace("\\\\", "\\")
                           .Replace("\\/", "/");
            }
            else
            {
                // Numeric value: read until comma, newline, or }
                int end = valueStart;
                while (end < json.Length && json[end] != ',' && json[end] != '}' && json[end] != '\r' && json[end] != '\n')
                    end++;
                return json.Substring(valueStart, end - valueStart).Trim();
            }
        }

        // Reads the TCP port from the "Urls" key in <webAppPath>\appsettings.json.
        // Returns the port number if it can be parsed from "http://0.0.0.0:<port>",
        // or 0 if the file is missing, the field is absent, or the value is
        // not in the expected format.
        //
        // Used by WriteConfigAction when --port is not supplied (direct-MSI repair)
        // to preserve the port that was written on the previous install.
        internal static int ReadPortFromAppsettings(string webAppPath)
        {
            if (string.IsNullOrEmpty(webAppPath)) return 0;
            string path = Path.Combine(webAppPath, "appsettings.json");
            if (!File.Exists(path)) return 0;
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                string? urls = ExtractJsonString(json, "Urls");
                if (urls == null) return 0;
                // Format: "http://0.0.0.0:<port>" (or https://* — take the last colon segment)
                int lastColon = urls.LastIndexOf(':');
                if (lastColon < 0) return 0;
                string portPart = urls.Substring(lastColon + 1).TrimEnd('/').Trim();
                if (int.TryParse(portPart, out int p) && p >= 1 && p <= 65535)
                    return p;
            }
            catch { }
            return 0;
        }

        // JSON string escaping (no external dependencies).
        internal static string EscJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\r", "\\r")
                    .Replace("\n", "\\n")
                    .Replace("\t", "\\t");
        }
    }
}
