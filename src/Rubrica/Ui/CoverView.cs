using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Rubrica.Model;
using static Rubrica.Ui.Lang;

namespace Rubrica.Ui
{
    /// The closed book (design artboard "Cover"): leather cover with its spine, the brass
    /// name plate, the OPEN tag, the elastic band and the divider tabs. Clicking the book
    /// opens it; clicking a tab opens it there.
    sealed class CoverView : Canvas
    {
        /// The book itself was clicked.
        public event Action Opened;

        /// A tab was clicked: a category id, or BinderView.FavoritesTab.
        public event Action<int> TabClicked;

        public CoverView(Book book)
        {
            Width = Constants.DesignWidth;
            Height = Constants.DesignHeight;
            ClipToBounds = true;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            Canvas closedBook = Controls.Id(ClosedBook(Css.Hex(book.Cover), book.Name), "cover-book");
            closedBook.MouseLeftButtonUp += delegate { if (Opened != null) Opened(); };
            Css.Place(this, closedBook, 236, 30);

            // The tabs come after the book, so they lie over its edge.
            int count = book.Categories.Count + 1;
            double height = BinderView.TabHeight(count);
            BinderView.AddRightTab(this, 714, T("FAVORITES"), Css.Hex("#8f6d10"), BinderView.FavoritesTab, -1, 0, height, RaiseTab);
            for (int i = 0; i < book.Categories.Count; i++)
                BinderView.AddRightTab(this, 714, book.Categories[i].Name, Css.Hex(book.Categories[i].Colour),
                                       book.Categories[i].Id, -1, i + 1, height, RaiseTab);
        }

        void RaiseTab(int id)
        {
            if (TabClicked != null) TabClicked(id);
        }

        static Canvas ClosedBook(Color coverColor, string radioName)
        {
            const double w = 488, h = 660;
            var radius = new CornerRadius(8, 24, 24, 8);

            var cover = Css.Box(w, h, radius, new[] { Css.Fill(coverColor), BinderView.Sheen(w, h) }, BinderView.CoverShadows());
            cover.Children.Add(BinderView.Texture(Textures.Leather((int)w, (int)h), w, h, radius));

            // The rounded spine: shading only, 54 px along the left edge.
            cover.Children.Add(new Border
            {
                Width = 54,
                Height = h,
                CornerRadius = new CornerRadius(8, 0, 0, 8),
                IsHitTestVisible = false,
                Background = Css.Linear(90, 54, h,
                    Css.At(Css.Rgba(0, 0, 0, 0.4), 0), Css.At(Css.Rgba(0, 0, 0, 0.05), 35), Css.At(Css.Rgba(0, 0, 0, 0.28), 70),
                    Css.At(Css.Rgba(255, 255, 255, 0.1), 88), Css.At(Css.Rgba(0, 0, 0, 0.35), 100)),
            });

            Css.Place(cover, BinderView.Stitching(w - 74, h - 20, new CornerRadius(4, 15, 15, 4)), 64, 10);
            Css.Place(cover, NamePlate(radioName), 141, 168);
            Css.Place(cover, OpenTag(), 380, 330);

            // The elastic band, a little longer than the book.
            Css.Place(cover, Css.Box(16, 676, new CornerRadius(0),
                Css.Linear(90, 16, 676, Css.At("#2a2622", 0), Css.At("#0f0d0b", 60), Css.At("#2a2622", 100)),
                Shadow.Outer(2, 0, 4, Css.Rgba(0, 0, 0, 0.5))), 404, -8);

            Css.BrightenOnHover(cover, radius, 0.04);
            return cover;
        }

        /// 260 x 104 paper label in a 7 px brass frame. CSS paints a border whose sides have
        /// different colours as four pieces mitred at 45 degrees, which is what makes it look bevelled.
        static Canvas NamePlate(string radioName)
        {
            const double w = 260, h = 104, b = 7;
            var plate = Css.Box(w, h, new CornerRadius(4),
                Css.Linear(180, w, h, Css.At("#f3ecd9", 0), Css.At("#e6dcc1", 100)),
                Shadow.Outer(0, 3, 6, Css.Rgba(0, 0, 0, 0.55)));
            plate.IsHitTestVisible = false;

            var frame = new Canvas { Width = w, Height = h, Clip = Css.RoundedRect(new Rect(0, 0, w, h), new CornerRadius(4)) };
            frame.Children.Add(Side("#d8b95f", new Point(0, 0), new Point(w, 0), new Point(w - b, b), new Point(b, b)));           // top
            frame.Children.Add(Side("#8c6a20", new Point(w, 0), new Point(w, h), new Point(w - b, h - b), new Point(w - b, b)));   // right
            frame.Children.Add(Side("#6f5316", new Point(w, h), new Point(0, h), new Point(b, h - b), new Point(w - b, h - b)));   // bottom
            frame.Children.Add(Side("#caa84c", new Point(0, h), new Point(0, 0), new Point(b, b), new Point(b, h - b)));           // left
            plate.Children.Add(frame);

            // inset 0 0 0 1px: a dark line just inside the frame.
            Css.Place(plate, new Rectangle { Width = w - 2 * b, Height = h - 2 * b, Stroke = Css.Fill(Css.Hex("#5a4310")), StrokeThickness = 1 }, b, b);

            // Two centred lines, 6 px apart: 24 + 6 + 12 = 42 px of text in the 90 px opening.
            var title = new GlyphText(T("CONTACTS"), AppFonts.Type, 24, Css.Hex("#1b2233"), 6);
            // The program's own name, unless book.xml names a radio (owner, 2026-09-18: one name
            // fits every radio the book is used at; nothing in the program edits it).
            var name = new GlyphText(radioName.Length > 0 ? radioName.ToUpperInvariant() : "RUBRICA", AppFonts.Type, 12, Css.Hex("#6b6252"), 3,
                                     double.NaN, w - 2 * b - 16);
            Css.Place(plate, BinderView.CenteredText(title, w, 24), 0, b + 24);
            Css.Place(plate, BinderView.CenteredText(name, w, 12), 0, b + 24 + 24 + 6);
            return plate;
        }

        static Polygon Side(string colorHex, params Point[] corners)
        {
            return new Polygon { Points = new PointCollection(corners), Fill = Css.Fill(Css.Hex(colorHex)) };
        }

        /// The paper tag tucked under the elastic band.
        static Canvas OpenTag()
        {
            var tag = Css.Box(70, 36, new CornerRadius(0),
                Css.Linear(180, 70, 36, Css.At("#f3ecd9", 0), Css.At("#e6dcc1", 100)),
                Shadow.Outer(0, 2, 4, Css.Rgba(0, 0, 0, 0.5)));
            tag.IsHitTestVisible = false;
            tag.Children.Add(BinderView.CenteredText(new GlyphText(T("OPEN"), AppFonts.Type, 12, Css.Hex("#b3261e"), 3), 70, 36));
            tag.RenderTransformOrigin = new Point(0.5, 0.5);
            tag.RenderTransform = new RotateTransform(-5);
            return tag;
        }
    }
}
