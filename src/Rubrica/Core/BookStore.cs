using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using Rubrica.Model;

namespace Rubrica.Core
{
    /// A book written by a newer Rubrica: it must not be opened, because saving would
    /// drop whatever this version does not know about.
    sealed class NewerBookException : Exception
    {
        public NewerBookException(string message) : base(message) { }
    }

    /// The book is there but could not be opened - another program holds it, or Windows
    /// denies it. That is not damage: nothing is set aside, nothing is replaced, and the
    /// program stops rather than show an older or an empty book.
    sealed class BookUnreadableException : Exception
    {
        public BookUnreadableException(string message) : base(message) { }
    }

    /// What Load had to do instead of simply reading the book - for the window to tell the
    /// operator, in their language. (null when the book was simply read.)
    sealed class LoadNotice
    {
        /// Where the unreadable book.xml was set aside; null when there was no book.xml at all.
        public string SetAsideAs;

        /// Where an unreadable book.bak was set aside (so that no later save can overwrite it); or null.
        public string BackupSetAsideAs;

        /// True when book.bak was loaded in its place; false when that failed too and the book is empty.
        public bool FromBackup;

        /// True when book.xml was missing and the save that was being written (book.tmp) was
        /// complete: it is the newest book there is, and it was loaded.
        public bool FromUnfinishedSave;
    }

    /// Reads and writes book.xml (architecture section 3).
    ///   book.xml  the book
    ///   book.bak  the save before the last one, left by the atomic replace
    ///   book.tmp  exists only while a save is in progress
    sealed class BookStore
    {
        readonly string folder;

        /// What the one category of a brand-new book is called (the window gives it in the
        /// operator's language).
        public string FirstCategoryName = "CONTACTS";

        bool startedWithoutBook;    // Load found no book at all...
        bool wroteBook;             // ...and this session has not written one yet

        public BookStore(string folder)
        {
            this.folder = folder;
        }

        string BookPath { get { return Path.Combine(folder, "book.xml"); } }
        string BakPath { get { return Path.Combine(folder, "book.bak"); } }
        string TmpPath { get { return Path.Combine(folder, "book.tmp"); } }

        /// %APPDATA%\CardamomTools\Rubrica
        public static string DefaultFolder()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            // An empty answer would quietly turn the book's folder into one relative to wherever Rubrica was started from.
            if (string.IsNullOrEmpty(appData)) throw new InvalidOperationException("Windows did not say where this user's AppData folder is.");
            return Path.Combine(appData, Constants.DataFolder);
        }

        // ---- loading ---------------------------------------------------------------------

        /// Never fails for a damaged file: it falls back to book.bak, then to an empty book.
        /// "notice" is null, or what the operator has to be told.
        public Book Load(out LoadNotice notice)
        {
            notice = null;
            if (!File.Exists(BookPath) && !File.Exists(BakPath))
            {
                // The very first save may have been cut short between writing and renaming.
                Book first = TryReadQuietly(TmpPath);
                if (first != null)
                {
                    notice = new LoadNotice { FromUnfinishedSave = true };
                    return first;
                }
                startedWithoutBook = true;
                return Repaired(new Book());   // first launch
            }

            Book book = TryRead(BookPath);
            if (book != null) return book;

            // book.xml is missing or damaged. Everything that could stop the program (a backup that
            // cannot be opened right now, or one from a newer Rubrica) is found out BEFORE anything
            // is renamed, so that "nothing was changed" is true when it is said.
            bool missing = !File.Exists(BookPath);
            Book unfinished = missing ? TryReadQuietly(TmpPath) : null;
            Book fromBak = unfinished == null ? TryRead(BakPath) : null;

            notice = new LoadNotice();
            if (!missing) notice.SetAsideAs = SetAside(BookPath, "");   // a file that failed to load is never overwritten
            if (unfinished != null)
            {
                // A save got as far as book.tmp and no further (a power cut inside File.Replace).
                notice.FromUnfinishedSave = true;
                return unfinished;
            }
            if (fromBak == null && File.Exists(BakPath))
                notice.BackupSetAsideAs = SetAside(BakPath, "-bak");   // else the second save would replace it
            notice.FromBackup = fromBak != null;
            return fromBak ?? Repaired(new Book());
        }

        /// book-damaged-20260918-104200[-bak][-2].xml, next to the book.
        string SetAside(string path, string suffix)
        {
            string stamp = "book-damaged-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + suffix;
            string aside = Path.Combine(folder, stamp + ".xml");
            for (int n = 2; File.Exists(aside); n++)
                aside = Path.Combine(folder, stamp + "-" + n + ".xml");
            File.Move(path, aside);
            return aside;
        }

        /// book.tmp is only a candidate: whatever is wrong with it, it is simply not used.
        Book TryReadQuietly(string path)
        {
            try
            {
                return TryRead(path);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// null when the file is missing or damaged. Two things are not "damaged" and go
        /// through as exceptions, so that the program stops without touching the file: a book
        /// from a newer Rubrica, and a book that cannot be opened right now (held by an
        /// antivirus or a backup program, access denied) - tried a few times first.
        Book TryRead(string path)
        {
            if (!File.Exists(path)) return null;
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    return Read(path);
                }
                catch (NewerBookException)
                {
                    throw;
                }
                catch (Exception error)
                {
                    bool cannotOpen = error is IOException || error is UnauthorizedAccessException;
                    if (!cannotOpen) return null;   // it opened, and what is inside is not a book
                    if (attempt == 4)
                        throw new BookUnreadableException("The book is there but could not be opened: another program may be using it, or Windows denies access to it.");
                    Thread.Sleep(300);
                }
            }
        }

        Book Read(string path)
        {
            XElement root = XDocument.Load(path).Root;
            if (root == null || root.Name != Xml.Book) throw new InvalidDataException("Not a Rubrica book.");

            int version = Int(root, Xml.SchemaVersion, 1);
            if (version > Constants.SchemaVersion)
                throw new NewerBookException("This book was written by a newer version of Rubrica.");
            // Migrations from older schemas go here, oldest first. Schema 1 is the first.

            var book = new Book
            {
                Name = Text(root, Xml.Name),
                Cover = Text(root, Xml.Cover, "#2450a0"),
                SortBy = Text(root, Xml.SortBy) == "surname" ? SortBy.Surname : SortBy.Name,
                NextId = Int(root, Xml.NextId, 1),
            };

            foreach (XElement e in root.Elements(Xml.Category))
                book.Categories.Add(new Category { Id = Int(e, Xml.Id, 0), Name = Text(e, Xml.Name), Colour = Text(e, Xml.Colour) });

            foreach (XElement e in root.Elements(Xml.Contact))
            {
                var contact = new Contact
                {
                    Id = Int(e, Xml.Id, 0),
                    CategoryId = Int(e, Xml.CategoryRef, 0),
                    Name = Text(e, Xml.Name),
                    Surname = Text(e, Xml.Surname),
                    Role = Text(e, Xml.Role),
                    Email = Text(e, Xml.Email),
                    Teams = Text(e, Xml.Teams),
                    Favorite = Text(e, Xml.Favorite) == "1",
                    Notes = (string)e.Element(Xml.Notes) ?? "",
                };
                foreach (XElement p in e.Elements(Xml.Phone))
                    contact.Phones.Add(new Phone(p.Value, Text(p, Xml.Label)));
                book.Contacts.Add(contact);
            }
            return Repaired(book);
        }

        /// Whatever the file said, the rest of the program may rely on these: at least one
        /// category, every contact in an existing one, every id positive and below NextId.
        Book Repaired(Book book)
        {
            if (book.Categories.Count == 0)
                book.Categories.Add(new Category { Id = 0, Name = FirstCategoryName, Colour = Category.Palette[0] });

            // A colour typed wrong in a hand-edited file would stop the program at every start.
            if (!IsColour(book.Cover)) book.Cover = "#2450a0";
            for (int i = 0; i < book.Categories.Count; i++)
                if (!IsColour(book.Categories[i].Colour)) book.Categories[i].Colour = Category.Palette[i % Category.Palette.Length];

            int highest = 0;
            foreach (Category c in book.Categories) highest = Math.Max(highest, c.Id);
            foreach (Contact c in book.Contacts) highest = Math.Max(highest, c.Id);
            book.NextId = Math.Max(book.NextId, highest + 1);

            foreach (Category c in book.Categories)
                if (c.Id <= 0) c.Id = book.NewId();

            foreach (Contact c in book.Contacts)
            {
                if (c.Id <= 0) c.Id = book.NewId();
                if (book.CategoryOf(c) == null) c.CategoryId = book.Categories[0].Id;
            }
            return book;
        }

        /// "#rrggbb"
        static bool IsColour(string text)
        {
            if (text == null || text.Length != 7 || text[0] != '#') return false;
            for (int i = 1; i < text.Length; i++)
                if (!Uri.IsHexDigit(text[i])) return false;
            return true;
        }

        // ---- saving ----------------------------------------------------------------------

        /// Atomic: the new file is complete on disk before it takes the old one's place, so a
        /// crash or a power cut leaves either the old book or the new one, never half of each.
        public void Save(Book book)
        {
            // No book at start, and now there is one that this session did not write: the folder
            // was out of reach a moment ago (a redirected profile, a network hiccup) and the real
            // book has come back. Replacing it would rotate it out in two saves.
            if (startedWithoutBook && !wroteBook && File.Exists(BookPath))
                throw new InvalidOperationException("A book that was not there when Rubrica started has appeared in its folder. Nothing was overwritten: close Rubrica and start it again.");

            Directory.CreateDirectory(folder);
            WriteFile(book, TmpPath);

            // An antivirus or an indexer may hold the book for a moment after it was written:
            // the same few tries as when reading, before the operator is told.
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    if (File.Exists(BookPath)) File.Replace(TmpPath, BookPath, BakPath);
                    else File.Move(TmpPath, BookPath);
                    break;
                }
                catch (Exception error)
                {
                    if (!(error is IOException || error is UnauthorizedAccessException) || attempt == 4) throw;
                    Thread.Sleep(300);
                }
            }
            wroteBook = true;
        }

        /// BACKUP NOW: the book as it is on screen, written where the operator says - anywhere
        /// but over the files Rubrica itself works on. Restoring is by hand: close Rubrica and
        /// copy the backup over book.xml.
        public void Backup(Book book, string path)
        {
            string target = Path.GetFullPath(path);
            foreach (string own in new[] { BookPath, BakPath, TmpPath })
                if (string.Equals(target, Path.GetFullPath(own), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("That is the book Rubrica is working on. Choose another name or folder.");

            // Complete under another name first: an older backup of the same name is only
            // replaced by one that was written to the end.
            string fresh = target + ".tmp";
            WriteFile(book, fresh);
            if (File.Exists(target)) File.Delete(target);
            File.Move(fresh, target);
        }

        static void WriteFile(Book book, string path)
        {
            var settings = new XmlWriterSettings { Indent = true };
            using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using (XmlWriter writer = XmlWriter.Create(file, settings))
                    new XDocument(Write(book)).Save(writer);
                file.Flush(true);   // true = through the OS cache, onto the disk
            }
        }

        static XElement Write(Book book)
        {
            // Clean.Text once more on the way out: whatever slipped into the book, a character
            // the XML writer refuses must never be what stops a save.
            var root = new XElement(Xml.Book,
                new XAttribute(Xml.SchemaVersion, Constants.SchemaVersion),
                new XAttribute(Xml.Name, Clean.Text(book.Name)),
                new XAttribute(Xml.Cover, book.Cover),
                new XAttribute(Xml.SortBy, book.SortBy == SortBy.Surname ? "surname" : "name"),
                new XAttribute(Xml.NextId, book.NextId));

            foreach (Category c in book.Categories)
                root.Add(new XElement(Xml.Category,
                    new XAttribute(Xml.Id, c.Id),
                    new XAttribute(Xml.Name, Clean.Text(c.Name)),
                    new XAttribute(Xml.Colour, c.Colour)));

            foreach (Contact c in book.Contacts)
            {
                var e = new XElement(Xml.Contact,
                    new XAttribute(Xml.Id, c.Id),
                    new XAttribute(Xml.CategoryRef, c.CategoryId),
                    new XAttribute(Xml.Name, Clean.Text(c.Name)),
                    new XAttribute(Xml.Surname, Clean.Text(c.Surname)),
                    new XAttribute(Xml.Role, Clean.Text(c.Role)),
                    new XAttribute(Xml.Email, Clean.Text(c.Email)),
                    new XAttribute(Xml.Teams, Clean.Text(c.Teams)),
                    new XAttribute(Xml.Favorite, c.Favorite ? "1" : "0"));
                foreach (Phone p in c.Phones)
                    e.Add(new XElement(Xml.Phone, new XAttribute(Xml.Label, Clean.Text(p.Label)), Clean.Text(p.Number)));
                if (c.Notes.Length > 0)
                    e.Add(new XElement(Xml.Notes, Clean.Text(c.Notes)));
                root.Add(e);
            }
            return root;
        }

        // ---- xml helpers -----------------------------------------------------------------

        /// Every element and attribute name of book.xml, in one place.
        static class Xml
        {
            public const string Book = "Book", SchemaVersion = "schemaVersion", Cover = "cover",
                SortBy = "sortBy", NextId = "nextId";
            public const string Category = "Category", Colour = "colour";
            public const string Contact = "Contact", CategoryRef = "category", Surname = "surname",
                Role = "role", Email = "email", Teams = "teams", Favorite = "favorite";
            public const string Phone = "Phone", Label = "label", Notes = "Notes";
            public const string Id = "id", Name = "name";
        }

        static string Text(XElement e, string attribute, string fallback = "")
        {
            return (string)e.Attribute(attribute) ?? fallback;
        }

        static int Int(XElement e, string attribute, int fallback)
        {
            int value;
            return int.TryParse((string)e.Attribute(attribute), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                ? value : fallback;
        }
    }
}
