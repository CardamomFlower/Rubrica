using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Rubrica.Model;

namespace Rubrica.Core
{
    /// Quick search (architecture section 4.7): name, surname, role, phone and email, ignoring
    /// case and accents. A few hundred contacts need no index: every keystroke looks at them all.
    static class Search
    {
        /// The contacts that match, in the book's own order. An empty query lists everybody.
        public static List<Contact> Find(Book book, string query)
        {
            query = (query ?? "").Trim();
            var hits = new List<Contact>();
            foreach (Contact contact in book.Contacts)
                if (Matches(contact, query)) hits.Add(contact);
            return book.Sorted(hits);
        }

        public static bool Matches(Contact contact, string query)
        {
            if (query.Length == 0) return true;
            int start, length;
            if (TryMatch(contact.Name + " " + contact.Surname, query, out start, out length)) return true;
            if (TryMatch(contact.Role, query, out start, out length)) return true;
            if (TryMatch(contact.Email, query, out start, out length)) return true;

            // Numbers are compared digit to digit, so "000 0001" finds "+39 000 000 0001".
            string digits = Digits(query);
            if (digits.Length >= 2)
                foreach (Phone phone in contact.Phones)
                    if (Digits(phone.Number).Contains(digits)) return true;
            return false;
        }

        /// Where the query occurs in a text, ignoring case and accents. start and length are
        /// positions in the text as written, so the match can be marked on the page.
        public static bool TryMatch(string text, string query, out int start, out int length)
        {
            start = 0;
            length = 0;
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query)) return false;

            int[] origin, queryOrigin;
            string plainText = Fold(text, out origin);
            string plainQuery = Fold(query, out queryOrigin);
            if (plainQuery.Length == 0) return false;
            int at = plainText.IndexOf(plainQuery, StringComparison.Ordinal);
            if (at < 0) return false;

            start = origin[at];
            int last = origin[at + plainQuery.Length - 1];
            length = last - start + 1;
            return true;
        }

        /// The text in lower case with the accents taken off its letters, plus, for every
        /// character of the result, the index of the character it came from.
        static string Fold(string text, out int[] origin)
        {
            var folded = new StringBuilder(text.Length);
            var from = new List<int>(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                // Half of a character beyond U+FFFF (an emoji): Normalize refuses it. It has no accent anyway.
                if (char.IsSurrogate(text[i]))
                {
                    folded.Append(text[i]);
                    from.Add(i);
                    continue;
                }

                // Decomposing splits an accented letter into the plain letter plus its accent mark.
                foreach (char c in text[i].ToString().Normalize(NormalizationForm.FormD))
                {
                    if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                    folded.Append(char.ToLowerInvariant(c));
                    from.Add(i);
                }
            }
            origin = from.ToArray();
            return folded.ToString();
        }

        public static string Digits(string text)
        {
            var digits = new StringBuilder();
            foreach (char c in text)
                if (c >= '0' && c <= '9') digits.Append(c);
            return digits.ToString();
        }
    }
}
