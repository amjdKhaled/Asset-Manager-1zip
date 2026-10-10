using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Dashboard.SetupHelper
{
    internal static class WebClientText
    {
        internal const string Tag = "<script src=\"assets/custom/lf-dashboard-button.js\"></script>";

        // Preserve the original encoding, BOM, newline style and every existing
        // character. In particular, never drop a whole line containing the tag.
        internal static byte[] InsertTag(byte[] original)
        {
            Encoding encoding = new UTF8Encoding(false, true);
            int bomLength = 0;
            if (original.Length >= 3 && original[0] == 0xEF && original[1] == 0xBB && original[2] == 0xBF)
                bomLength = 3;
            else if (original.Length >= 2 && original[0] == 0xFF && original[1] == 0xFE)
            {
                encoding = new UnicodeEncoding(false, false, true);
                bomLength = 2;
            }
            else if (original.Length >= 2 && original[0] == 0xFE && original[1] == 0xFF)
            {
                encoding = new UnicodeEncoding(true, false, true);
                bomLength = 2;
            }
            string text = encoding.GetString(original, bomLength, original.Length - bomLength);
            if (text.IndexOf("lf-dashboard-button.js", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Do not normalize or rewrite an existing Laserfiche page.
                if (!text.Contains(Tag))
                    throw new IOException("An unfamiliar Dashboard script tag already exists; manual review is required.");
                return original;
            }
            Match head = Regex.Match(text, "</head\\s*>", RegexOptions.IgnoreCase);
            if (!head.Success) throw new IOException("Browse.aspx has no head closing tag; no files were changed.");
            string updated = text.Insert(head.Index, Tag);
            byte[] body = encoding.GetBytes(updated);
            byte[] result = new byte[bomLength + body.Length];
            Array.Copy(original, result, bomLength);
            Array.Copy(body, 0, result, bomLength, body.Length);
            return result;
        }
    }
}
