using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Rubrica.Model;
using static Rubrica.Ui.Lang;

namespace Rubrica.Ui
{
    /// A list of contacts written across as many pages as it needs: a category, or the
    /// favorites. Each page is 300 px wide and its rows stack top to bottom like the
    /// design's flex columns.
    ///
    /// Two looks, as drawn. A category files its contacts by letter: a red initial
    /// in the margin and an "A - D" range in the header. A collection such as Favorites has
    /// no margin, names the category beside each contact, and has a fixed header label.
    sealed class ContactListSpread
    {
        static readonly Color Ink = Controls.Ink;            // titles and names
        static readonly Color LinkInk = Css.Hex("#2a3352");  // numbers, email, Teams
        static readonly Color Pencil = Controls.Pencil;      // small typewriter labels
        static readonly Color Red = Controls.Red;            // letter markers, hover

        // Heights from the design. An entry: 24 name + 15 role (14 + 1 gap) + 21 per channel (20 + 1).
        const double PageWidth = 300, PageHeight = 540, HeaderHeight = 32, HeaderGap = 14, EntryGap = 10;
        const double NameRowHeight = 24, RoleRowHeight = 15, ChannelRowHeight = 21;

        readonly Book book;
        readonly string title;
        readonly string headerLabel;            // null = the page's letter range
        readonly bool byLetter;
        readonly List<Contact> contacts;
        readonly List<List<Contact>> pages = new List<List<Contact>>();
        readonly Func<FrameworkElement> emptyState;
        readonly Action<Contact> favoriteToggled;

        /// The contact whose card is open: its name is written in red with a ring round it. 0 = nobody.
        public int SelectedId;

        /// A name was clicked.
        public Action<Contact> NameClicked;

        /// A number or an address was clicked: what it is ("NUMBER", "EMAIL") and the text itself.
        public Action<Contact, string, string> ChannelClicked;

        /// The "Teams chat" row was clicked.
        public Action<Contact> ChatClicked;

        /// Search writes its own field and count above the first page instead of the usual
        /// header (FirstPageTopHeight tall), and its later pages have no header at all: their
        /// entries start LaterPagesTop down the page.
        public FrameworkElement FirstPageTop;
        public double FirstPageTopHeight, LaterPagesTop;

        /// Recent writes a red line under the role: what was done with the contact, and when.
        public Func<Contact, string> RedLine;

        /// Search marks what it found in the name: start and length, or null.
        public Func<Contact, int[]> Highlight;

        /// contacts: already in the order they are to be shown.
        /// headerLabel: fixed text at the right of the header; null files the list by letter.
        /// emptyState: builds what the first page says when there is nobody to list.
        public ContactListSpread(Book book, string title, string headerLabel, List<Contact> contacts,
                                 Func<FrameworkElement> emptyState, Action<Contact> favoriteToggled)
        {
            this.book = book;
            this.title = title;
            this.headerLabel = headerLabel;
            this.contacts = contacts;
            this.emptyState = emptyState;
            this.favoriteToggled = favoriteToggled;
            byLetter = headerLabel == null;
        }

        /// Two pages to a spread; an empty list still has its one spread.
        public int SpreadCount
        {
            get { return Math.Max(1, (Pages.Count + 1) / 2); }
        }

        /// The spread a contact is written on (0 when it is not in this list).
        public bool Holds(Contact contact)
        {
            return Pages.Exists(p => p.Contains(contact));
        }

        public int SpreadOf(Contact contact)
        {
            int page = Pages.FindIndex(p => p.Contains(contact));
            return Math.Max(0, page) / 2;
        }

        bool paginated;

        // Paginated on first use, so that the options above can be set after construction.
        List<List<Contact>> Pages
        {
            get
            {
                if (!paginated) Paginate();
                return pages;
            }
        }

        /// How far down the page the entries start: under the usual header, or under Search's own top.
        double EntriesTop(int pageIndex)
        {
            if (FirstPageTop == null) return HeaderHeight + HeaderGap;
            return pageIndex == 0 ? FirstPageTopHeight + HeaderGap : LaterPagesTop;
        }

        /// Entries go on a page until the next one would not fit under the header.
        void Paginate()
        {
            paginated = true;
            pages.Clear();
            double used = 0, room = 0;
            foreach (Contact contact in contacts)
            {
                double height = EntryHeight(contact);
                bool startsPage = pages.Count == 0 || used + EntryGap + height > room;
                if (startsPage)
                {
                    pages.Add(new List<Contact>());
                    room = PageHeight - EntriesTop(pages.Count - 1);
                    used = height;
                }
                else
                {
                    used += EntryGap + height;
                }
                pages[pages.Count - 1].Add(contact);
            }
        }

        double EntryHeight(Contact c)
        {
            int channels = c.Phones.Count + (c.Email.Length > 0 ? 1 : 0) + (c.Teams.Length > 0 ? 1 : 0);
            bool redLine = RedLine != null && !string.IsNullOrEmpty(RedLine(c));
            return NameRowHeight + (c.Role.Length > 0 ? RoleRowHeight : 0) + (redLine ? RoleRowHeight : 0) + ChannelRowHeight * channels;
        }

        // ---- one page ------------------------------------------------------------------

        public FrameworkElement Page(int pageIndex)
        {
            List<Contact> entries = pageIndex < Pages.Count ? Pages[pageIndex] : new List<Contact>();
            var page = new StackPanel { Width = PageWidth };

            double firstGap = HeaderGap;
            if (FirstPageTop == null)
                page.Children.Add(Header(entries));
            else if (pageIndex == 0)
            {
                // The same element is written on every rebuild: take it off the previous page first.
                var previous = FirstPageTop.Parent as Panel;
                if (previous != null) previous.Children.Remove(FirstPageTop);
                page.Children.Add(FirstPageTop);
            }
            else
                firstGap = LaterPagesTop;

            if (contacts.Count == 0 && pageIndex == 0 && emptyState != null)
                page.Children.Add(emptyState());

            for (int i = 0; i < entries.Count; i++)
            {
                FrameworkElement entry = Entry(entries[i]);
                entry.Margin = new Thickness(0, i == 0 ? firstGap : EntryGap, 0, 0);
                page.Children.Add(entry);
            }
            return page;
        }

        FrameworkElement Header(List<Contact> entries)
        {
            string label = headerLabel;
            if (label == null)
                label = entries.Count == 0 ? "-" : book.Initial(entries[0]) + " - " + book.Initial(entries[entries.Count - 1]);

            // WPF: a Grid with no rows or columns simply overlays its children, each placed by its alignment.
            var header = new Grid { Height = HeaderHeight };
            var labelText = new GlyphText(label, AppFonts.Type, 11, Pencil, 2)
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                // On the title's baseline (23 px down): 23 - its own ascent of 8.
                Margin = new Thickness(0, 15, 0, 0),
            };
            header.Children.Add(new GlyphText(title, AppFonts.Hand, 24, Ink, 1, 28, PageWidth - labelText.TextWidth - 12)
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            });
            header.Children.Add(labelText);
            header.Children.Add(new Rectangle { Height = 2, Fill = Css.Fill(Ink), VerticalAlignment = VerticalAlignment.Bottom });
            return header;
        }

        /// A letter in the margin marks the first contact of each initial - first in the whole
        /// list, not on the page, so turning the page does not repeat it.
        bool StartsLetter(Contact contact)
        {
            int index = contacts.IndexOf(contact);
            return index == 0 || book.Initial(contact) != book.Initial(contacts[index - 1]);
        }

        // ---- one entry -----------------------------------------------------------------

        FrameworkElement Entry(Contact contact)
        {
            double indent = byLetter ? 28 : 0;
            double rowWidth = PageWidth - indent;

            var entry = new Grid();
            if (byLetter && StartsLetter(contact))
                entry.Children.Add(new GlyphText(book.Initial(contact), AppFonts.Type, 12, Red)
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 4, 0, 0),
                });

            var rows = new StackPanel { Margin = new Thickness(indent, 0, 0, 0) };
            entry.Children.Add(rows);

            // Name, category when the list mixes them, favourite star at the far right.
            GlyphText categoryLabel = null;
            Category category = book.CategoryOf(contact);
            if (!byLetter && category != null)
                categoryLabel = new GlyphText(category.Name.ToUpperInvariant(), AppFonts.Type, 10, Pencil, 2, double.NaN, 110);

            const double starAndGap = 44 + 8;
            double nameRoom = rowWidth - starAndGap - (categoryLabel == null ? 0 : categoryLabel.TextWidth + 8);

            bool selected = contact.Id == SelectedId;
            var name = Controls.Id(new InkLink(selected ? Red : Ink, Red, NameRowHeight) { HorizontalAlignment = HorizontalAlignment.Left }, "name-" + contact.Id);
            name.AddText(book.DisplayName(contact), AppFonts.Hand, 22, 24, 0.5, nameRoom);
            double nameWidth = ((GlyphText)name.Children[0]).TextWidth;
            int[] found = Highlight == null ? null : Highlight(contact);
            if (found != null) ((GlyphText)name.Children[0]).Mark(found[0], found[1]);
            if (categoryLabel != null) name.AddFixed(categoryLabel);
            name.MouseLeftButtonUp += delegate { if (NameClicked != null) NameClicked(contact); };

            var nameRow = new Grid { Height = NameRowHeight };
            if (selected) nameRow.Children.Add(Ring(nameWidth));
            nameRow.Children.Add(name);
            nameRow.Children.Add(StarButton(contact));
            rows.Children.Add(nameRow);

            if (contact.Role.Length > 0)
                rows.Children.Add(new GlyphText(contact.Role.ToUpperInvariant(), AppFonts.Type, 10, Pencil, 1.5, 14, rowWidth - 12)
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(12, 1, 0, 0),
                });

            string redLine = RedLine == null ? null : RedLine(contact);
            if (!string.IsNullOrEmpty(redLine))
                rows.Children.Add(new GlyphText(redLine, AppFonts.Type, 10, Red, 1.5, 14, rowWidth - 12)
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(12, 1, 0, 0),
                });

            double textRoom = rowWidth - 12 - 14 - 8;   // indent, icon, gap
            for (int i = 0; i < contact.Phones.Count; i++)
            {
                string number = contact.Phones[i].Number;
                rows.Children.Add(ChannelRow("phone-" + contact.Id + "-" + i, Icons.Phone, number, contact.Phones[i].Label.ToUpperInvariant(), textRoom,
                    () => { if (ChannelClicked != null) ChannelClicked(contact, "NUMBER", number); }));
            }
            if (contact.Email.Length > 0)
                rows.Children.Add(ChannelRow("email-" + contact.Id, Icons.Mail, contact.Email, "", textRoom,
                    () => { if (ChannelClicked != null) ChannelClicked(contact, "EMAIL", contact.Email); }));
            if (contact.Teams.Length > 0)
                rows.Children.Add(ChannelRow("chat-" + contact.Id, Icons.Chat, T("Teams chat"), "", textRoom,
                    () => { if (ChatClicked != null) ChatClicked(contact); }));
            return entry;
        }

        /// The red ring drawn round the name whose card is open: border-radius 50% / 45%, 2 px,
        /// a little crooked. It is laid over the row and takes no room, so the page does not move.
        static FrameworkElement Ring(double nameWidth)
        {
            double w = nameWidth + 20, h = NameRowHeight + 4;
            return new Rectangle
            {
                Width = w,
                Height = h,
                RadiusX = w / 2,
                RadiusY = h * 0.45,
                Stroke = Css.Fill(Css.Rgba(179, 38, 30, 0.85)),
                StrokeThickness = 2,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(-10, -2, 0, -2),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(-1.5),
                IsHitTestVisible = false,
            };
        }

        static FrameworkElement ChannelRow(string id, string icon, string text, string label, double textRoom, Action onClick)
        {
            var row = Controls.Id(new InkLink(LinkInk, Red, 20)
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(12, 1, 0, 0),
            }, id);
            row.MouseLeftButtonUp += delegate { onClick(); };
            row.AddIcon(icon, 14);

            GlyphText labelText = label.Length == 0 ? null : new GlyphText(label, AppFonts.Type, 9, Pencil, 1.5, double.NaN, 70);
            row.AddText(text, AppFonts.Hand, 17, 20, 0, textRoom - (labelText == null ? 0 : labelText.TextWidth + 8));
            if (labelText != null) row.AddFixed(labelText);
            return row;
        }

        /// 44 x 44 click target around an 18 px star; the negative margins keep the row 24 px tall.
        FrameworkElement StarButton(Contact contact)
        {
            var star = Icons.Make(Icons.Star, 18, null, 1.8);
            star.HorizontalAlignment = HorizontalAlignment.Center;
            star.VerticalAlignment = VerticalAlignment.Center;

            var button = Controls.Id(new Grid
            {
                Width = 44,
                Height = 44,
                Margin = new Thickness(0, -10, 0, -10),
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = Brushes.Transparent,   // transparent still catches the mouse; null would not
                Cursor = Cursors.Hand,
            }, "star-" + contact.Id);
            button.Children.Add(star);

            Controls.PaintStar(star, contact.Favorite);
            button.MouseLeftButtonUp += delegate
            {
                contact.Favorite = !contact.Favorite;
                Controls.PaintStar(star, contact.Favorite);
                if (favoriteToggled != null) favoriteToggled(contact);
            };
            return button;
        }

        // ---- nobody to list ------------------------------------------------------------

        /// The empty page of a category: a handwritten line, a typed hint
        /// that wraps, and optionally a row of stamps.
        public static FrameworkElement EmptyState(string headline, string hint, params FrameworkElement[] stamps)
        {
            // Header gap 14 + the block's own padding-top 40.
            var block = new StackPanel { Margin = new Thickness(0, HeaderGap + 40, 0, 0) };
            block.Children.Add(GlyphText.Wrap(headline, AppFonts.Hand, 24, Ink, 0, 30, PageWidth));

            StackPanel hintLines = GlyphText.Wrap(hint, AppFonts.Type, 11, Pencil, 2, 18, PageWidth);
            hintLines.Margin = new Thickness(0, 14, 0, 0);
            block.Children.Add(hintLines);

            if (stamps.Length > 0)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
                for (int i = 0; i < stamps.Length; i++)
                {
                    stamps[i].Margin = new Thickness(i == 0 ? 0 : 12, 0, 0, 0);
                    row.Children.Add(stamps[i]);
                }
                block.Children.Add(row);
            }
            return block;
        }
    }
}
