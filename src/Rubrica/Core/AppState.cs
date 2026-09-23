using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;
using Rubrica.Model;

namespace Rubrica.Core
{
    /// One line of the Recent page: who, what was done ("view", "copy", "chat") and when.
    sealed class RecentEntry
    {
        public int ContactId;
        public string Action;
        public DateTime At;
    }

    /// state.xml: where the window was, how to paint, where the last backup went, and who was
    /// looked up lately. Disposable - if it is
    /// missing or unreadable the defaults apply and nothing of value is lost, so loading never
    /// reports an error and saving never interrupts the operator.
    sealed class AppState
    {
        readonly string folder;

        /// folder: where state.xml lives. Load() reads it; a fresh one starts with the defaults.
        public AppState(string folder)
        {
            this.folder = folder;
        }

        // NaN = never saved.
        public double Left = double.NaN, Top = double.NaN, Width = double.NaN, Height = double.NaN;
        public bool Maximized;

        /// Paint without the graphics card (the "--software" option).
        public bool Software;

        /// Where BACKUP NOW last wrote; "" = never.
        public string BackupFolder = "";

        /// "en" or "it", as picked in Setup; "" = never picked, which follows Windows' own language.
        public string Language = "";

        /// Newest first, one entry per contact, at most Constants.RecentMax.
        public readonly List<RecentEntry> Recents = new List<RecentEntry>();

        public bool HasBounds
        {
            get { return !double.IsNaN(Left) && !double.IsNaN(Top) && !double.IsNaN(Width) && !double.IsNaN(Height); }
        }

        // ---- file ------------------------------------------------------------------------

        public static AppState Load(string folder)
        {
            var state = new AppState(folder);
            try
            {
                XElement root = XDocument.Load(Path.Combine(folder, Xml.File)).Root;
                XElement window = root.Element(Xml.Window);
                if (window != null)
                {
                    state.Left = Number(window, Xml.Left);
                    state.Top = Number(window, Xml.Top);
                    state.Width = Number(window, Xml.Width);
                    state.Height = Number(window, Xml.Height);
                    state.Maximized = (string)window.Attribute(Xml.Maximized) == "1";
                }
                XElement renderer = root.Element(Xml.Renderer);
                state.Software = renderer != null && (string)renderer.Attribute(Xml.Software) == "1";
                XElement language = root.Element(Xml.Language);
                if (language != null) state.Language = (string)language.Attribute(Xml.Code) ?? "";
                XElement backup = root.Element(Xml.Backup);
                if (backup != null) state.BackupFolder = (string)backup.Attribute(Xml.Folder) ?? "";
                foreach (XElement e in root.Elements(Xml.Recent))
                {
                    int id;
                    DateTime at;
                    if (int.TryParse((string)e.Attribute(Xml.Contact), NumberStyles.Integer, CultureInfo.InvariantCulture, out id)
                        && DateTime.TryParseExact((string)e.Attribute(Xml.At), Stamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out at))
                        state.Recents.Add(new RecentEntry { ContactId = id, Action = (string)e.Attribute(Xml.Action) ?? "view", At = at });
                }
            }
            catch (Exception)
            {
                return new AppState(folder);
            }
            return state;
        }

        const string Stamp = "yyyy-MM-ddTHH:mm:ss";

        /// Every element and attribute name of state.xml, in one place.
        static class Xml
        {
            public const string File = "state.xml", Temp = "state.tmp", State = "State";
            public const string Window = "Window", Left = "left", Top = "top", Width = "width", Height = "height", Maximized = "maximized";
            public const string Renderer = "Renderer", Software = "software", Backup = "Backup", Folder = "folder";
            public const string Language = "Language", Code = "code";
            public const string Recent = "Recent", Contact = "contact", Action = "action", At = "at";
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(folder);
                var root = new XElement(Xml.State,
                    new XElement(Xml.Window,
                        new XAttribute(Xml.Left, Left.ToString("R", CultureInfo.InvariantCulture)),
                        new XAttribute(Xml.Top, Top.ToString("R", CultureInfo.InvariantCulture)),
                        new XAttribute(Xml.Width, Width.ToString("R", CultureInfo.InvariantCulture)),
                        new XAttribute(Xml.Height, Height.ToString("R", CultureInfo.InvariantCulture)),
                        new XAttribute(Xml.Maximized, Maximized ? "1" : "0")),
                    new XElement(Xml.Renderer, new XAttribute(Xml.Software, Software ? "1" : "0")),
                    new XElement(Xml.Backup, new XAttribute(Xml.Folder, Clean.Text(BackupFolder))),
                    new XElement(Xml.Language, new XAttribute(Xml.Code, Language == "it" || Language == "en" ? Language : "")));
                foreach (RecentEntry entry in Recents)
                    root.Add(new XElement(Xml.Recent,
                        new XAttribute(Xml.Contact, entry.ContactId),
                        new XAttribute(Xml.Action, entry.Action),
                        new XAttribute(Xml.At, entry.At.ToString(Stamp, CultureInfo.InvariantCulture))));

                // It is written whole every time: complete under another name first, so that a
                // power cut cannot leave half a file - and with it lose the --software setting.
                string file = Path.Combine(folder, Xml.File), fresh = Path.Combine(folder, Xml.Temp);
                using (var stream = new FileStream(fresh, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    using (System.Xml.XmlWriter writer = System.Xml.XmlWriter.Create(stream, new System.Xml.XmlWriterSettings { Indent = true }))
                        new XDocument(root).Save(writer);
                    stream.Flush(true);   // through the OS cache, onto the disk: a power cut then cannot leave it empty
                }
                if (File.Exists(file)) File.Replace(fresh, file, null);
                else File.Move(fresh, file);
            }
            catch (Exception)
            {
                // Not worth interrupting the operator for.
            }
        }

        static double Number(XElement e, string attribute)
        {
            double value;
            return double.TryParse((string)e.Attribute(attribute), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                ? value : double.NaN;
        }

        // ---- recents -----------------------------------------------------------------------

        /// Something was done with a contact: it goes to the top of the Recent page.
        public void Touch(int contactId, string action, DateTime at)
        {
            Recents.RemoveAll(e => e.ContactId == contactId);
            Recents.Insert(0, new RecentEntry { ContactId = contactId, Action = action, At = at });
            if (Recents.Count > Constants.RecentMax) Recents.RemoveRange(Constants.RecentMax, Recents.Count - Constants.RecentMax);
        }

        /// Forgets the contacts that are no longer in the book.
        public void Prune(Book book)
        {
            Recents.RemoveAll(e => !book.Contacts.Exists(c => c.Id == e.ContactId));
        }

        // The words of the Recent page's red line ("COPIED - TODAY 10:42") are in Ui/Lang.
    }
}
