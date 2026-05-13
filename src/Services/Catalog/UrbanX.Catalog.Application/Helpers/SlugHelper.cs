using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace UrbanX.Catalog.Application.Helpers
{
    public static class SlugHelper
    {
        private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
        private static readonly Regex NonSlugChars = new(@"[^a-z0-9-]", RegexOptions.Compiled);

        /// <summary>Builds a URL-safe slug from a display name (kebab-case).</summary>
        public static string ToSlug(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "item";

            var s = name.Trim().ToLowerInvariant();
            s = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                var uc = CharUnicodeInfo.GetUnicodeCategory(c);
                if (uc == UnicodeCategory.NonSpacingMark) continue;
                if (c >= 128) continue; // drop extended chars for a simple ASCII slug
                sb.Append(c);
            }

            s = Whitespace.Replace(sb.ToString().Trim(), "-");
            s = NonSlugChars.Replace(s, string.Empty);
            s = s.Trim('-');
            s = Regex.Replace(s, @"-{2,}", "-");

            return s.Length == 0 ? "item" : s;
        }
    }
}
