using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Rubrica.Model;

namespace Rubrica.Core
{
    /// One contact an import would add, and the tab its row names ("" = none).
    sealed class ImportRow
    {
        public Contact Contact;
        public string Category = "";
    }

    /// What an import would do, worked out before the book is touched: the operator reads
    /// the figures on a slip and only then says yes (architecture section 5).
    sealed class ImportPlan
    {
        /// True for a file with no header row: "name surname number", one contact per line.
        public bool PlainList;

        public readonly List<ImportRow> Rows = new List<ImportRow>();

        /// Skipped because the book - or the file itself, further up - already has them.
        public int AlreadyThere;

        /// Skipped because there is nothing to file them under: a row with no name, a line with no number.
        public int Unusable;

        /// Tabs that would be created, in the order the file names them.
        public readonly List<string> NewCategories = new List<string>();

        /// Rows whose tab cannot be created because the binder is full: they go under the first tab.
        public int ToFirstCategory;
    }

    /// What an import did, so that UNDO can take it back.
    sealed class ImportResult
    {
        public readonly List<Contact> Added = new List<Contact>();
        public readonly List<Category> Created = new List<Category>();
    }

    /// CSV in and out (architecture section 5). One format for both, so a round trip loses
    /// nothing. Import also reads a plain "name surname number" list.
    static class Csv
    {
        const char ExportSeparator = ';';   // what Excel expects where the decimal mark is a comma
        const int MinDigits = 3;            // in a plain list, fewer digits than an extension has are not a number

        // ---- columns ---------------------------------------------------------------------

        const string ColName = "Name", ColSurname = "Surname", ColRole = "Role", ColCategory = "Category",
            ColPhone = "Phone", ColLabel = "Label", ColEmail = "Email", ColTeams = "Teams",
            ColFavorite = "Favorite", ColNotes = "Notes";

        /// Name; Surname; Role; Category; Phone1; Label1; ... ; Email; Teams; Favorite; Notes
        public static List<string> Columns()
        {
            var columns = new List<string> { ColName, ColSurname, ColRole, ColCategory };
            for (int i = 1; i <= Constants.MaxPhones; i++)
            {
                columns.Add(ColPhone + i);
                columns.Add(ColLabel + i);
            }
            columns.AddRange(new[] { ColEmail, ColTeams, ColFavorite, ColNotes });
            return columns;
        }

        // ---- export ----------------------------------------------------------------------

        /// The whole book as CSV text: a header row, then one row per contact, tab by tab.
        public static string Write(Book book)
        {
            var text = new StringBuilder();
            WriteRow(text, Columns());

            foreach (Category category in book.Categories)
                foreach (Contact c in book.InCategory(category.Id))
                {
                    var row = new List<string> { c.Name, c.Surname, c.Role, category.Name };
                    for (int i = 0; i < Constants.MaxPhones; i++)
                    {
                        row.Add(i < c.Phones.Count ? c.Phones[i].Number : "");
                        row.Add(i < c.Phones.Count ? c.Phones[i].Label : "");
                    }
                    row.AddRange(new[] { c.Email, c.Teams, c.Favorite ? "yes" : "", c.Notes });
                    WriteRow(text, row);
                }
            return text.ToString();
        }

        /// UTF-8 with a byte order mark: without it Excel reads accents wrong.
        public static void Export(Book book, string path)
        {
            File.WriteAllText(path, Write(book), new UTF8Encoding(true));
        }

        static void WriteRow(StringBuilder text, List<string> cells)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                if (i > 0) text.Append(ExportSeparator);
                text.Append(Quoted(cells[i] ?? ""));
            }
            text.Append("\r\n");
        }

        /// In quotes when the cell holds a separator, a quote, a line break, or blanks at its
        /// ends; a quote inside is written twice.
        static string Quoted(string cell)
        {
            bool needed = cell.IndexOfAny(new[] { ';', ',', '\t', '"', '\r', '\n' }) >= 0
                          || (cell.Length > 0 && (char.IsWhiteSpace(cell[0]) || char.IsWhiteSpace(cell[cell.Length - 1])));
            return needed ? "\"" + cell.Replace("\"", "\"\"") + "\"" : cell;
        }

        // ---- reading a file ----------------------------------------------------------------

        /// The text of a file, whatever wrote it: a byte order mark is believed; without one
        /// the bytes are UTF-8 if they are valid UTF-8, and otherwise the Western European
        /// code page that an old Notepad and Excel's plain "CSV" still write.
        public static string ReadText(string path)
        {
            var info = new FileInfo(path);
            if (info.Length > Constants.MaxImportBytes)
                throw new InvalidDataException("The file is too large to be a contacts list.");

            byte[] bytes = File.ReadAllBytes(path);
            string text;
            if (StartsWith(bytes, 0xEF, 0xBB, 0xBF)) text = new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            else if (StartsWith(bytes, 0xFF, 0xFE)) text = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            else if (StartsWith(bytes, 0xFE, 0xFF)) text = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            else
            {
                try
                {
                    text = new UTF8Encoding(false, true).GetString(bytes);   // true = refuse bytes that are not UTF-8
                }
                catch (DecoderFallbackException)
                {
                    text = Encoding.GetEncoding(1252).GetString(bytes);
                }
            }
            if (text.IndexOf('\0') >= 0) throw new InvalidDataException("This is not a text file.");
            return text;
        }

        static bool StartsWith(byte[] bytes, params byte[] mark)
        {
            if (bytes.Length < mark.Length) return false;
            for (int i = 0; i < mark.Length; i++)
                if (bytes[i] != mark[i]) return false;
            return true;
        }

        // ---- import: the plan --------------------------------------------------------------

        /// Reads the text and works out what importing it would do. The book is not changed.
        public static ImportPlan Plan(Book book, string text)
        {
            var plan = new ImportPlan();
            char separator = Separator(text);
            List<List<string>> table = Parse(text, separator);

            // Who is in the book already. Rows that pass are added to it too, so that a list
            // that names somebody twice adds them once.
            var known = new HashSet<string>();
            foreach (Contact c in book.Contacts) known.Add(Key(c));

            Dictionary<string, int> header = table.Count > 0 ? Header(table[0]) : null;
            if (header != null)
            {
                for (int r = 1; r < table.Count; r++)
                    Consider(plan, known, FromRow(table[r], header));
            }
            else
            {
                plan.PlainList = true;
                foreach (string line in text.Replace("\r", "\n").Split('\n'))
                    if (line.Trim().Length > 0)
                        Consider(plan, known, FromLine(line));
            }

            if (!plan.PlainList) PlanCategories(book, plan);
            return plan;
        }

        static void Consider(ImportPlan plan, HashSet<string> known, ImportRow row)
        {
            if (row == null) plan.Unusable++;
            else if (!known.Add(Key(row.Contact))) plan.AlreadyThere++;
            else plan.Rows.Add(row);
        }

        /// "Already there" means the same name and surname and the same first number. The two
        /// names are compared joined, because a plain list may have split them differently, and
        /// the number digit by digit, because the same number is written in many ways.
        static string Key(Contact c)
        {
            string name = string.Join(" ", (c.Name + " " + c.Surname).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            string number = c.Phones.Count > 0 ? Search.Digits(c.Phones[0].Number) : "";
            return name.ToUpperInvariant() + "|" + number;
        }

        /// Which new tabs the rows ask for, while the binder has room; the rows left over go
        /// under the first tab.
        static void PlanCategories(Book book, ImportPlan plan)
        {
            int room = Constants.MaxCategories - book.Categories.Count;
            foreach (ImportRow row in plan.Rows)
            {
                if (row.Category.Length == 0 || Find(book, row.Category) != null) continue;
                if (plan.NewCategories.Exists(n => Same(n, row.Category))) continue;
                if (plan.NewCategories.Count < room) plan.NewCategories.Add(row.Category);
                else plan.ToFirstCategory++;
            }
        }

        static Category Find(Book book, string name)
        {
            return book.Categories.Find(c => Same(c.Name, name));
        }

        static bool Same(string a, string b)
        {
            return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        // ---- import: doing it --------------------------------------------------------------

        /// Adds what the plan lists. plainListTarget: the tab a plain list goes under (it
        /// names none itself); ignored for a CSV. Nothing is replaced, ever.
        public static ImportResult Apply(Book book, ImportPlan plan, Category plainListTarget)
        {
            var result = new ImportResult();
            Category first = book.Categories[0];

            foreach (string name in plan.NewCategories)
            {
                Category created = book.AddCategory(name, FreeColour(book));
                if (created != null) result.Created.Add(created);
            }

            foreach (ImportRow row in plan.Rows)
            {
                Category category = plan.PlainList ? plainListTarget : row.Category.Length > 0 ? Find(book, row.Category) : null;
                if (category == null || !book.Categories.Contains(category)) category = first;

                row.Contact.Id = book.NewId();
                row.Contact.CategoryId = category.Id;
                book.Contacts.Add(row.Contact);
                result.Added.Add(row.Contact);
            }
            return result;
        }

        /// Takes an import back: the contacts it added, and the tabs it made if they are empty again.
        public static void Undo(Book book, ImportResult result)
        {
            foreach (Contact contact in result.Added) book.Contacts.Remove(contact);
            foreach (Category category in result.Created)
                if (book.CountIn(category) == 0 && book.Categories.Count > 1) book.Categories.Remove(category);
        }

        /// The first colour of the palette that no tab wears yet.
        static string FreeColour(Book book)
        {
            foreach (string colour in Category.Palette)
                if (!book.Categories.Exists(c => string.Equals(c.Colour, colour, StringComparison.OrdinalIgnoreCase))) return colour;
            return Category.Palette[book.Categories.Count % Category.Palette.Length];
        }

        // ---- a row of the CSV ----------------------------------------------------------------

        /// Column name -> position, or null when the row is not a header: a header has a Name column.
        static Dictionary<string, int> Header(List<string> cells)
        {
            var header = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < cells.Count; i++)
            {
                string name = cells[i].Replace(" ", "");   // "Telefono 1", as the form writes it, is Telefono1
                string english;
                if (ItalianColumns.TryGetValue(name, out english)) name = english;
                if (name.Length > 0 && !header.ContainsKey(name)) header[name] = i;
            }
            return header.ContainsKey(ColName) ? header : null;
        }

        /// EXPORT writes the English names, and IMPORT reads those - and these, which is what an
        /// Italian operator would type at the top of a sheet of their own.
        static readonly Dictionary<string, string> ItalianColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Nome", ColName }, { "Cognome", ColSurname }, { "Ruolo", ColRole }, { "Categoria", ColCategory },
            { "Telefono", ColPhone + 1 }, { "Telefono1", ColPhone + 1 }, { "Telefono2", ColPhone + 2 }, { "Telefono3", ColPhone + 3 },
            { "Etichetta", ColLabel + 1 }, { "Etichetta1", ColLabel + 1 }, { "Etichetta2", ColLabel + 2 }, { "Etichetta3", ColLabel + 3 },
            { "E-mail", ColEmail }, { "Mail", ColEmail }, { "Preferito", ColFavorite }, { "Note", ColNotes },
        };

        /// null when the row has no name to file it under.
        static ImportRow FromRow(List<string> cells, Dictionary<string, int> header)
        {
            Func<string, string> cell = column =>
            {
                int at;
                return header.TryGetValue(column, out at) && at < cells.Count ? Clean.Line(cells[at]) : "";
            };

            var contact = new Contact
            {
                Name = cell(ColName),
                Surname = cell(ColSurname),
                Role = cell(ColRole),
                Email = cell(ColEmail),
                Teams = cell(ColTeams),
                Favorite = IsYes(cell(ColFavorite)),
            };
            int notesAt;
            if (header.TryGetValue(ColNotes, out notesAt) && notesAt < cells.Count)   // the one cell that may hold line breaks
                contact.Notes = Clean.Text(cells[notesAt].Replace("\r\n", "\n").Replace('\r', '\n'), Constants.MaxNotesLength).Trim();
            if (contact.Name.Length == 0)
            {
                // A surname alone is an organisation written in the wrong column.
                contact.Name = contact.Surname;
                contact.Surname = "";
            }
            if (contact.Name.Length == 0) return null;

            for (int i = 1; i <= Constants.MaxPhones; i++)
                if (cell(ColPhone + i).Length > 0)
                    contact.Phones.Add(new Phone(cell(ColPhone + i), cell(ColLabel + i)));

            return new ImportRow { Contact = contact, Category = cell(ColCategory) };
        }

        static bool IsYes(string cell)
        {
            string plain = Plain(cell);
            return plain == "1" || plain == "yes" || plain == "y" || plain == "true" || plain == "x" || plain == "si" || plain == "s";
        }

        /// Lower case, accents off: the Italian for "yes" has one.
        static string Plain(string text)
        {
            var plain = new StringBuilder();
            foreach (char c in text.Trim().Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    plain.Append(char.ToLowerInvariant(c));
            return plain.ToString();
        }

        // ---- a line of a plain list ------------------------------------------------------------

        /// "Mario Rossi 333 1234567": the number starts at the first digit or "+" that begins a
        /// word ("Studio2" keeps its 2), what comes before it is the name - first word NAME, the
        /// rest SURNAME. A line that starts with the number is read the other way round. null
        /// when there is no number or no name: in a list of numbers, such a line is a title.
        static ImportRow FromLine(string line)
        {
            int at = -1;
            for (int i = 0; i < line.Length && at < 0; i++)
                if ("+0123456789".IndexOf(line[i]) >= 0 && (i == 0 || " \t;,:(".IndexOf(line[i - 1]) >= 0)) at = i;
            if (at < 0) return null;

            string name = line.Substring(0, at);
            string number = line.Substring(at);
            if (Words(name).Length == 0)
            {
                // Number first: it runs as long as the characters are ones a number is written with.
                int end = 0;
                while (end < number.Length && "+0123456789 -/.()".IndexOf(number[end]) >= 0) end++;
                name = number.Substring(end);
                number = number.Substring(0, end);
            }
            else
            {
                // Anything after the number's own cell belongs to columns this list does not name.
                int cut = number.IndexOfAny(new[] { ';', '\t' });
                if (cut >= 0) number = number.Substring(0, cut);
            }

            string[] words = Words(Clean.Line(name));
            number = Clean.Line(number).Trim(' ', ',', ';', ':', '-', '\t');
            if (words.Length == 0 || Search.Digits(number).Length < MinDigits) return null;

            var contact = new Contact { Name = words[0], Surname = string.Join(" ", words, 1, words.Length - 1) };
            contact.Phones.Add(new Phone(number, ""));
            return new ImportRow { Contact = contact };
        }

        /// The words of a name. Separators and the punctuation that ends a name ("Rossi:",
        /// "Rossi -") count as blanks.
        static string[] Words(string name)
        {
            var words = new List<string>();
            foreach (string word in name.Split(new[] { ' ', '\t', ';', ',', ':' }, StringSplitOptions.RemoveEmptyEntries))
                if (word.Trim('-', '"').Length > 0) words.Add(word.Trim('"'));
            return words.ToArray();
        }

        // ---- the CSV grammar -------------------------------------------------------------------

        /// Whichever of ; , and tab the first line uses most, outside quotes.
        static char Separator(string text)
        {
            int semicolons = 0, commas = 0, tabs = 0;
            bool quoted = false;
            foreach (char c in text)
            {
                if (c == '"') quoted = !quoted;
                else if (quoted) continue;
                else if (c == '\n' || c == '\r') break;
                else if (c == ';') semicolons++;
                else if (c == ',') commas++;
                else if (c == '\t') tabs++;
            }
            if (tabs > semicolons && tabs > commas) return '\t';
            return commas > semicolons ? ',' : ';';
        }

        /// Rows of cells. A cell in quotes may hold separators and line breaks; a quote inside
        /// it is written twice. Rows with nothing in them are dropped.
        static List<List<string>> Parse(string text, char separator)
        {
            var table = new List<List<string>>();
            var row = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;

            Action endCell = () =>
            {
                row.Add(cell.ToString());
                cell.Clear();
            };
            Action endRow = () =>
            {
                endCell();
                if (row.Exists(c => c.Trim().Length > 0)) table.Add(row);
                row = new List<string>();
            };

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c != '"') cell.Append(c);
                    else if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else quoted = false;
                }
                else if (c == '"' && cell.ToString().Trim().Length == 0)
                {
                    cell.Clear();
                    quoted = true;
                }
                else if (c == separator) endCell();
                else if (c == '\n') endRow();
                else if (c != '\r') cell.Append(c);
            }
            endRow();
            return table;
        }
    }
}
