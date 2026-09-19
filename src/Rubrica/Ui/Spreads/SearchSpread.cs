using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using Rubrica.Core;
using Rubrica.Model;
using static Rubrica.Ui.Lang;

namespace Rubrica.Ui
{
    /// The quick search (design artboards "Quick search" and "Search - no results"): a line
    /// to write in at the top of the left page, the count of what was found, and the results
    /// flowing over the pages like any list, the match marked in each name. The field stays
    /// put while the results are rewritten at every keystroke.
    sealed class SearchSpread
    {
        /// The results changed: ask for the pages again.
        public event Action ResultsChanged;

        /// The stamp on the "no results" page.
        public Action NewContact;

        // What the list needs from the window. Every list that is built calls back through
        // these fields when something is clicked - not through copies taken while building,
        // because the first list is built in the constructor, before the window has set them.
        public Action<Contact> NameClicked, ChatClicked, FavoriteToggled;
        public Action<Contact, string, string> ChannelClicked;

        const double PageWidth = 300;

        readonly Book book;
        readonly Canvas top = new Canvas { Width = PageWidth, Height = 73 };   // the field's row (48) + 14 + the count line (11)
        readonly PaperLine field;
        GlyphText count;

        public ContactListSpread List { get; private set; }

        public SearchSpread(Book book)
        {
            this.book = book;

            // The row: magnifier, SEARCH, the line to write on; a 2 px rule under all three. The
            // canvas draws the magnifier 20 px wide, but its flex row is too full and the browser
            // squeezes the icon to 16: the reference render is what is followed here.
            Path glass = Icons.Make("M11 3a8 8 0 1 1 0 16 8 8 0 0 1 0-16z M21 21l-4.35-4.35", 16, Css.Fill(Controls.Ink), 2.2);
            Css.Place(top, glass, 0, 14);
            var caption = new GlyphText(T("SEARCH"), AppFonts.Type, 11, Controls.Pencil, 2);
            Css.Place(top, caption, 26, 16);
            double fieldLeft = 26 + caption.TextWidth + 10;
            field = new PaperLine(PageWidth - fieldLeft, "search-field", 24, false);
            Css.Place(top, field, fieldLeft, 0);
            Css.Place(top, new Rectangle { Width = PageWidth, Height = 2, Fill = Css.Fill(Controls.Ink) }, 0, 46);

            field.Box.TextChanged += delegate { Rebuild(); };
            Rebuild();
        }

        public string Query
        {
            get { return field.Text; }
            set { field.Text = value; }
        }

        public void Focus()
        {
            field.Box.Focus();
            field.Box.SelectAll();
        }

        /// The book changed under the search (an edit, a delete, an undo): look again.
        public void Refresh()
        {
            Rebuild();
        }

        void Rebuild()
        {
            string query = Query;
            List<Contact> hits = Search.Find(book, query);

            if (count != null) top.Children.Remove(count);
            string line = query.Length == 0 ? N(hits.Count, "{0} CONTACT IN ALL CATEGORIES", "{0} CONTACTS IN ALL CATEGORIES")
                                            : N(hits.Count, "{0} RESULT IN ALL CATEGORIES", "{0} RESULTS IN ALL CATEGORIES");
            count = new GlyphText(line, AppFonts.Type, 11, Controls.Pencil, 2);
            Css.Place(top, count, 0, 62);

            List = new ContactListSpread(book, "", "", hits,
                () => ContactListSpread.EmptyState(T("No match for \"{0}\".", query), T("CHECK THE SPELLING, OR ADD IT AS A NEW CONTACT."),
                    Controls.Id(Controls.Stamp(T("NEW CONTACT"), StampStyle.RedOutline, -1.5, () => { if (NewContact != null) NewContact(); }), "search-new")),
                contact => { if (FavoriteToggled != null) FavoriteToggled(contact); })
            {
                FirstPageTop = top,
                FirstPageTopHeight = 73,
                LaterPagesTop = 90,
                NameClicked = contact => { if (NameClicked != null) NameClicked(contact); },
                ChatClicked = contact => { if (ChatClicked != null) ChatClicked(contact); },
                ChannelClicked = (contact, kind, text) => { if (ChannelClicked != null) ChannelClicked(contact, kind, text); },
                Highlight = contact =>
                {
                    int start, length;
                    return query.Length > 0 && Search.TryMatch(book.DisplayName(contact), query, out start, out length)
                        ? new[] { start, length } : null;
                },
            };
            if (ResultsChanged != null) ResultsChanged();
        }
    }
}
