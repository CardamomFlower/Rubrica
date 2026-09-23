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
    /// The index card clipped over the right page: who it
    /// is, every channel with a stamp beside it, the notes, and DELETE / EDIT at the bottom.
    /// 322 px wide and normally 500 tall, turned 1.2 degrees; all numbers are the design's.
    sealed class ContactCard : Canvas
    {
        public event Action EditRequested;
        public event Action DeleteRequested;
        public event Action FavoriteToggled;

        /// The x in the corner or the paper clip was clicked: put the card away.
        public event Action CloseRequested;

        /// A COPY stamp was pressed: what it is ("NUMBER", "EMAIL") and the text to copy.
        public event Action<string, string> CopyRequested;

        /// OPEN CHAT was pressed: the Teams account.
        public event Action<string> ChatRequested;

        const double CardWidth = 322, Edge = 1, PadX = 24, PadTop = 26, PadBottom = 20, Gap = 12;
        const double Inner = CardWidth - 2 * (Edge + PadX);   // 272 px to write on
        const double StampHeight = 46, NoteLine = 24;

        public ContactCard(Book book, Contact contact)
        {
            Controls.Id(this, "card");
            var rows = new List<FrameworkElement>();
            rows.Add(Header(book, contact));

            double[] tilts = { -1.5, 1, -1, 1.5 };
            int n = 0;
            for (int i = 0; i < contact.Phones.Count; i++)
            {
                Phone phone = contact.Phones[i];
                string label = phone.Label.Length > 0 ? phone.Label.ToUpperInvariant() : T("PHONE");
                rows.Add(Channel(label, phone.Number, T("COPY"), "card-copy-" + n, tilts[n++ % 4], () => Raise(CopyRequested, "NUMBER", phone.Number)));
            }
            if (contact.Email.Length > 0)
                rows.Add(Channel(T("EMAIL"), contact.Email, T("COPY"), "card-copy-" + n, tilts[n++ % 4], () => Raise(CopyRequested, "EMAIL", contact.Email)));
            if (contact.Teams.Length > 0)
                rows.Add(Channel(T("TEAMS"), contact.Teams, T("OPEN CHAT"), "card-chat", tilts[n++ % 4], () => { if (ChatRequested != null) ChatRequested(contact.Teams); }));

            // Stack the rows, then see what is left for the notes above the two stamps at the bottom.
            double y = Edge + PadTop;
            var placed = new List<KeyValuePair<FrameworkElement, double>>();
            foreach (FrameworkElement row in rows)
            {
                placed.Add(new KeyValuePair<FrameworkElement, double>(row, y));
                y += row.Height + Gap;
            }

            // The card is 500 tall as drawn; a contact with every channel filled in needs a
            // little more, and there is room for 24 px before the page number.
            const double notesCaption = 12;   // 10 px caption + 2
            double bottomBlock = Gap + StampHeight + PadBottom + Edge;
            double height = 500;
            double room = height - bottomBlock - y - notesCaption;
            if (room < 2 * NoteLine) height = Math.Min(524, height + 2 * NoteLine - room);
            room = height - bottomBlock - y - notesCaption;
            int noteLines = (int)Math.Floor(room / NoteLine);

            Width = CardWidth;
            Height = height;
            Children.Add(Css.Box(CardWidth, height, new CornerRadius(0),
                Css.Linear(180, CardWidth, height, Css.At("#fdfaf1", 0), Css.At("#f6f0df", 100)),
                Shadow.OuterWhole(0, 8, 18, Css.Rgba(0, 0, 0, 0.28)), Shadow.OuterWhole(0, 1, 2, Css.Rgba(0, 0, 0, 0.2))));   // whole: the card is turned
            Children.Add(new Rectangle { Width = CardWidth, Height = height, Stroke = Css.Fill(Css.Hex("#d6cbae")), StrokeThickness = 1, IsHitTestVisible = false });

            foreach (KeyValuePair<FrameworkElement, double> row in placed)
                Css.Place(this, row.Key, Edge + PadX, row.Value);
            if (noteLines >= 1)
                Css.Place(this, Notes(contact.Notes, Math.Min(noteLines, 3)), Edge + PadX, y);

            Canvas delete = Controls.Id(Controls.Stamp(T("DELETE"), StampStyle.RedOutline, 1.2, () => { if (DeleteRequested != null) DeleteRequested(); }), "card-delete");
            Canvas edit = Controls.Id(Controls.Stamp(T("EDIT"), StampStyle.DarkOutline, -1.2, () => { if (EditRequested != null) EditRequested(); }), "card-edit");
            double stampsTop = height - Edge - PadBottom - StampHeight;
            Css.Place(this, delete, Edge + PadX, stampsTop);
            Css.Place(this, edit, CardWidth - Edge - PadX - edit.Width, stampsTop);

            // Not in the original drawing: two ways to put the card away with
            // the mouse. The x is pencil grey, because a red cross means "delete" on the Tabs page;
            // its 30 px target stays clear of the star.
            Action close = () => { if (CloseRequested != null) CloseRequested(); };
            Css.Place(this, Controls.Id(Controls.IconButton(Icons.Cross, Controls.Pencil, close, 30, 30), "card-close"), CardWidth - Edge - 30, Edge);

            Canvas clip = Controls.Id(PaperClip(), "card-clip");
            clip.MouseLeftButtonUp += delegate { close(); };
            Css.Place(this, clip, Edge + 22, Edge - 16);

            RenderTransformOrigin = new Point(0.5, 0.5);
            RenderTransform = new RotateTransform(-1.2);
        }

        static void Raise(Action<string, string> handler, string what, string value)
        {
            if (handler != null) handler(what, value);
        }

        /// Name and star, role, category, and the red rule under them. The text starts 30 px in,
        /// clear of the paper clip.
        FrameworkElement Header(Book book, Contact contact)
        {
            const double indent = 30;
            var header = new Canvas { Width = Inner };
            Css.Place(header, new GlyphText(book.DisplayName(contact), AppFonts.Hand, 30, Controls.Ink, 1, 34, Inner - indent - 44 - 8), indent, 0);

            Path star = Icons.Make(Icons.Star, 22, null, 1.8);
            var starButton = Controls.Id(new Grid { Width = 44, Height = 44, Background = Brushes.Transparent, Cursor = Cursors.Hand }, "card-star");
            star.HorizontalAlignment = HorizontalAlignment.Center;
            star.VerticalAlignment = VerticalAlignment.Center;
            starButton.Children.Add(star);
            Controls.PaintStar(star, contact.Favorite);
            starButton.MouseLeftButtonUp += delegate
            {
                contact.Favorite = !contact.Favorite;
                Controls.PaintStar(star, contact.Favorite);
                if (FavoriteToggled != null) FavoriteToggled();
            };
            Css.Place(header, starButton, Inner - 44, -5);   // 44 px centred on the 34 px name line

            double y = 34 + 2;
            if (contact.Role.Length > 0)
            {
                Css.Place(header, new GlyphText(contact.Role.ToUpperInvariant(), AppFonts.Type, 11, Controls.Ink, 2, double.NaN, Inner - indent), indent, y);
                y += 11 + 2;
            }
            Category category = book.CategoryOf(contact);
            Css.Place(header, new GlyphText(category == null ? "" : category.Name.ToUpperInvariant(), AppFonts.Type, 10, Controls.Pencil, 2, double.NaN, Inner - indent), indent, y);
            y += 10 + 6;

            Css.Place(header, new Rectangle { Width = Inner, Height = 2, Fill = Css.Fill(Css.Hex("#c9564a")) }, 0, y);
            header.Height = y + 2;
            return header;
        }

        /// One way to reach the contact: caption, value, and the stamp that acts on it.
        static FrameworkElement Channel(string caption, string value, string stampLabel, string stampId, double tilt, Action onStamp)
        {
            var row = new Canvas { Width = Inner, Height = StampHeight };
            Canvas stamp = Controls.Id(Controls.Stamp(stampLabel, StampStyle.RedOutline, tilt, onStamp, 12), stampId);
            Css.Place(row, stamp, Inner - stamp.Width, 0);

            // Caption (10) over value (24), the pair centred on the stamp's 46 px.
            Css.Place(row, new GlyphText(caption, AppFonts.Type, 10, Controls.Pencil, 2), 0, 6);
            Css.Place(row, new GlyphText(value, AppFonts.Hand, 20, Controls.Ink, 0, 24, Inner - stamp.Width - 12), 0, 16);
            return row;
        }

        /// NOTES over ruled lines. What does not fit is cut with "..."; Edit shows it all.
        static FrameworkElement Notes(string notes, int lines)
        {
            var block = new Canvas { Width = Inner, Height = 12 + lines * NoteLine };
            Css.Place(block, new GlyphText(T("NOTES"), AppFonts.Type, 10, Controls.Pencil, 2), 0, 0);
            for (int i = 1; i <= lines; i++)
                Css.Place(block, new Rectangle { Width = Inner, Height = 1, Fill = Css.Fill(Css.Rgba(96, 130, 190, 0.3)) }, 0, 12 + i * NoteLine - 1);

            var written = new List<string>();
            foreach (string paragraph in notes.Replace("\r", "").Split('\n'))
                foreach (GlyphText line in GlyphText.Wrap(paragraph, AppFonts.Hand, 18, Controls.Ink, 0, NoteLine, Inner).Children)
                    written.Add(line.Text);

            for (int i = 0; i < Math.Min(lines, written.Count); i++)
            {
                bool cut = i == lines - 1 && written.Count > lines;
                Css.Place(block, new GlyphText(cut ? written[i] + " ..." : written[i], AppFonts.Hand, 18, Controls.Ink, 0, NoteLine, Inner), 0, 12 + i * NoteLine);
            }
            return block;
        }

        /// The paper clip: one wire path, drawn twice - a soft dark copy as its shadow, then the metal.
        /// The transparent background makes its whole box take the click, not only the thin wire.
        static Canvas PaperClip()
        {
            const string wire = "M8 16v34a5 5 0 0 0 10 0V14a8 8 0 0 0-16 0v38a11 11 0 0 0 22 0V18";
            var metal = Css.SvgLinear(true, Css.At("#fbfbf9", 0), Css.At("#b5b5b1", 45), Css.At("#6c6c68", 60), Css.At("#e4e4e1", 100));

            var clip = new Canvas { Width = 26, Height = 66, UseLayoutRounding = false, Background = Brushes.Transparent, Cursor = Cursors.Hand };
            Css.Place(clip, Wire(wire, Css.Fill(Css.Rgba(0, 0, 0, 0.35)), 3), 1.5, 2);
            clip.Children.Add(Wire(wire, metal, 2.6));
            return clip;
        }

        static Path Wire(string data, Brush stroke, double thickness)
        {
            return new Path
            {
                Data = Geometry.Parse(data),
                Stroke = stroke,
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
        }
    }
}
