using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Rubrica.Model;
using static Rubrica.Ui.Lang;

namespace Rubrica.Ui
{
    /// The Setup page. On the left the inside of the cover shows instead of a page: a
    /// pocket holding three slips - IMPORT CSV, EXPORT CSV, BACKUP NOW. On the right, one
    /// page: the sort order and the keyboard legend. All numbers are the design's.
    sealed class SetupSpread
    {
        public event Action ImportRequested;
        public event Action ExportRequested;
        public event Action BackupRequested;

        /// The other sort order was picked - the book already says so: save, and draw the page again.
        public event Action SortChanged;

        /// The other language was picked: the window remembers it and draws everything again.
        public event Action<Language> LanguageChanged;

        const double PanelWidth = 362, PanelHeight = 608, PageWidth = 300;
        const double SlipWidth = 214, SlipHeight = 124;

        readonly Book book;
        FrameworkElement insideCover;   // two leather textures: built once per visit (the window drops the spread on leaving)

        public SetupSpread(Book book)
        {
            this.book = book;
        }

        // ---- the inside of the cover -------------------------------------------------------

        /// 362 x 608, to lie where the left page usually does.
        public FrameworkElement InsideCover()
        {
            if (insideCover != null) return insideCover;
            var radius = new CornerRadius(4);
            Color leather = Css.Hex(book.Cover);

            var panel = Css.Box(PanelWidth, PanelHeight, radius,
                new[] { Css.Fill(leather), Css.Linear(160, PanelWidth, PanelHeight, Css.At(Css.Rgba(255, 255, 255, 0.08), 0), Css.At(Css.Rgba(0, 0, 0, 0.3), 100)) },
                Shadow.Inset(0, 0, 30, Css.Rgba(0, 0, 0, 0.5)));
            panel.Children.Add(BinderView.Texture(Textures.Leather((int)PanelWidth, (int)PanelHeight), PanelWidth, PanelHeight, radius));
            Css.Place(panel, BinderView.Stitching(PanelWidth - 16, PanelHeight - 16, new CornerRadius(3)), 8, 8);

            Css.Place(panel, new GlyphText(T("DATA"), AppFonts.Type, 12, Css.Rgba(255, 255, 255, 0.55), 3)
            {
                ShadowBrush = Css.Fill(Css.Rgba(0, 0, 0, 0.6)),
                ShadowOffset = new Vector(0, -1),
                IsHitTestVisible = false,
            }, 24, 26);

            // Bottom to top, as they lie in the pocket.
            Css.Place(panel, Slip(T("IMPORT CSV"), T("NOTEPAD LIST -> BOOK"), -4, "setup-import", () => Raise(ImportRequested)), 40, 128);
            Css.Place(panel, Slip(T("EXPORT CSV"), T("BOOK -> FILE"), 2, "setup-export", () => Raise(ExportRequested)), 100, 168);
            Css.Place(panel, Slip(T("BACKUP NOW"), T("SAFETY COPY OF THE BOOK"), -1, "setup-backup", () => Raise(BackupRequested)), 62, 214);

            Css.Place(panel, Pocket(leather), 0, PanelHeight - 300);
            insideCover = panel;
            return panel;
        }

        static void Raise(Action handler)
        {
            if (handler != null) handler();
        }

        /// A slip of paper sticking out of the pocket: a typed title and what it does.
        static Canvas Slip(string title, string hint, double tilt, string id, Action onClick)
        {
            var slip = Css.Box(SlipWidth, SlipHeight, new CornerRadius(0),
                Css.Linear(180, SlipWidth, SlipHeight, Css.At("#fdfaf1", 0), Css.At("#efe6cf", 100)),
                Shadow.Outer(0, 6, 14, Css.Rgba(0, 0, 0, 0.45)));
            slip.Children.Add(new Rectangle { Width = SlipWidth, Height = SlipHeight, Stroke = Css.Fill(Css.Hex("#d6cbae")), StrokeThickness = 1, IsHitTestVisible = false });

            // 1 px border + 18 px padding; the hint 6 px under the title's line.
            var titleText = new GlyphText(title, AppFonts.Type, 16, Controls.Ink, 3) { IsHitTestVisible = false };
            Css.Place(slip, titleText, 19, 19);
            Css.Place(slip, new GlyphText(hint, AppFonts.Type, 10, Controls.Pencil, 2) { IsHitTestVisible = false }, 19, 19 + titleText.LineHeight + 6);

            Css.BrightenOnHover(slip, new CornerRadius(0));
            slip.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                e.Handled = true;
                onClick();
            };
            slip.RenderTransformOrigin = new Point(0.5, 0.5);
            slip.RenderTransform = new RotateTransform(tilt);
            return Controls.Id(slip, id);
        }

        /// The pocket: the lower 300 px of the panel, its upper edge cut slantwise
        /// (clip-path: polygon(0 70px, 55% 0, 100% 44px, 100% 100%, 0 100%)), stitched along
        /// the inside. It lets clicks through to the slips it half covers.
        static Canvas Pocket(Color leather)
        {
            const double w = PanelWidth, h = 300;
            var square = new CornerRadius(0);
            var pocket = Css.Box(w, h, square,
                new[] { Css.Fill(leather), Css.Linear(180, w, h, Css.At(Css.Rgba(0, 0, 0, 0.12), 0), Css.At(Css.Rgba(0, 0, 0, 0.38), 100)) });
            pocket.Children.Add(BinderView.Texture(Textures.Leather((int)w, (int)h), w, h, square));
            Css.Place(pocket, BinderView.Stitching(w - 20, h - 70, square), 10, 60);

            var edge = new StreamGeometry();
            using (StreamGeometryContext path = edge.Open())
            {
                path.BeginFigure(new Point(0, 70), true, true);
                path.PolyLineTo(new[] { new Point(0.55 * w, 0), new Point(w, 44), new Point(w, h), new Point(0, h) }, false, false);
            }
            edge.Freeze();   // WPF: a frozen object can no longer change, which makes it cheaper to draw
            pocket.Clip = edge;
            pocket.IsHitTestVisible = false;
            return pocket;
        }

        // ---- the page on the right ---------------------------------------------------------

        public FrameworkElement Right()
        {
            var page = new StackPanel { Width = PageWidth };
            page.Children.Add(Controls.PageHeader(T("SETUP"), T("INSIDE COVER")));

            var sort = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            sort.Children.Add(Controls.Caption(T("SORT A-Z BY")));
            sort.Children.Add(SortChoice(T("FIRST NAME"), SortBy.Name, "setup-sort-name"));
            sort.Children.Add(SortChoice(T("SURNAME"), SortBy.Surname, "setup-sort-surname"));
            page.Children.Add(sort);

            var keys = new StackPanel { Margin = new Thickness(0, 14 + 8, 0, 0) };
            keys.Children.Add(Controls.Caption(T("KEYBOARD SHORTCUTS")));
            keys.Children.Add(Shortcut("/", T("FOCUS SEARCH")));
            keys.Children.Add(Shortcut("N", T("NEW CONTACT")));
            keys.Children.Add(Shortcut("ESC", T("CLOSE / BACK")));
            page.Children.Add(keys);

            // Not in the original drawing: the language, below everything else on the page.
            // Each name is written in its own language.
            var language = new StackPanel { Margin = new Thickness(0, 14 + 8, 0, 0) };
            language.Children.Add(Controls.Caption(T("LANGUAGE")));
            var choices = new StackPanel { Orientation = Orientation.Horizontal };
            choices.Children.Add(LanguageChoice("ENGLISH", Language.English, "setup-language-en"));
            choices.Children.Add(LanguageChoice("ITALIANO", Language.Italian, "setup-language-it"));
            language.Children.Add(choices);
            page.Children.Add(language);
            return page;
        }

        FrameworkElement LanguageChoice(string label, Language which, string id)
        {
            var row = Controls.Id(new Canvas { Width = PageWidth / 2, Height = 44, Margin = new Thickness(0, 4, 0, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand }, id);
            Css.Place(row, Controls.Radio(Lang.Current == which), 0, 12);

            var text = new GlyphText(label, AppFonts.Hand, 20, Controls.Ink) { IsHitTestVisible = false };
            Css.Place(row, text, 30, HalfUp((44 - text.LineHeight) / 2));

            row.MouseLeftButtonUp += delegate
            {
                if (Lang.Current == which) return;
                if (LanguageChanged != null) LanguageChanged(which);
            };
            return row;
        }

        /// A radio button and its label, 44 px high; the whole row takes the click, as a
        /// label does in a browser.
        FrameworkElement SortChoice(string label, SortBy order, string id)
        {
            var row = Controls.Id(new Canvas { Width = PageWidth, Height = 44, Margin = new Thickness(0, 4, 0, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand }, id);
            Css.Place(row, Controls.Radio(book.SortBy == order), 0, 12);

            var text = new GlyphText(label, AppFonts.Hand, 20, Controls.Ink) { IsHitTestVisible = false };
            Css.Place(row, text, 30, HalfUp((44 - text.LineHeight) / 2));

            row.MouseLeftButtonUp += delegate
            {
                if (book.SortBy == order) return;
                book.SortBy = order;
                Raise(SortChanged);
            };
            return row;
        }

        /// A key cap and what the key does, 40 px high.
        static FrameworkElement Shortcut(string key, string meaning)
        {
            var row = new Canvas { Width = PageWidth, Height = 40, Margin = new Thickness(0, 4, 0, 0), IsHitTestVisible = false };
            Canvas cap = Controls.KeyCap(key);
            Css.Place(row, cap, 0, 5);

            var text = new GlyphText(meaning, AppFonts.Type, 11, Controls.Ink, 2);
            Css.Place(row, text, cap.Width + 14, HalfUp((40 - text.LineHeight) / 2));
            return row;
        }

        /// A line of text centred in a flex row lands on a half pixel here, and the browser
        /// settles it on the pixel below (boxes, elsewhere, go to the one above).
        static double HalfUp(double y)
        {
            return Math.Floor(y + 0.5);
        }
    }
}
