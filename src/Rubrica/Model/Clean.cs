using System.Text;

namespace Rubrica.Model
{
    /// What a field of the book may hold. Text reaches the book from the keyboard, the
    /// clipboard and imported files, and any of them can carry characters that XML 1.0
    /// cannot store: the writer refuses them, and from then on EVERY save would fail. So
    /// they are taken out at the door, and once more on the way to the disk.
    static class Clean
    {
        /// The text without control characters (tab and line ends stay), without halves of
        /// a surrogate pair left on their own, and no longer than max characters.
        public static string Text(string text, int max = int.MaxValue)
        {
            if (string.IsNullOrEmpty(text)) return "";

            var clean = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length && clean.Length < max; i++)
            {
                char c = text[i];
                if (char.IsHighSurrogate(c))
                {
                    // A character beyond U+FFFF is written as two: both, or neither.
                    bool paired = i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]);
                    if (paired && clean.Length + 2 <= max) clean.Append(c).Append(text[i + 1]);
                    if (paired) i++;
                }
                else if (char.IsLowSurrogate(c) || c == '\ufffe' || c == '\uffff')
                {
                    continue;
                }
                else if (c >= ' ' || c == '\t' || c == '\n' || c == '\r')
                {
                    clean.Append(c);
                }
            }
            return clean.ToString();
        }

        /// A one-line field: cleaned, line ends and tabs turned into blanks, trimmed.
        public static string Line(string text, int max = Constants.MaxFieldLength)
        {
            return Text(text, max).Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
        }
    }
}
