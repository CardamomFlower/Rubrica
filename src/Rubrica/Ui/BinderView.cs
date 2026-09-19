using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Rubrica.Model;
using static Rubrica.Ui.Lang;

namespace Rubrica.Ui
{
    /// The open ring binder: desk, cover, spine, the two pages, rings and tabs.
    /// 960 x 720; all coordinates are the design's CSS pixels, and nested canvases mirror
    /// the design's nested boxes. It is built once: moving around the book only swaps what
    /// is written on the pages (SetPages) and which tab sticks out (SetTabs).
    sealed class BinderView : Canvas
    {
        /// The "category id" of the Favorites tab. Real ids start at 1.
        public const int FavoritesTab = 0;

        /// A tab on the right was clicked: a category id, or FavoritesTab.
        public event Action<int> TabClicked;

        /// A folded page corner was clicked: -1 back, +1 forward.
        public event Action<int> PageTurnRequested;

        /// A tab along the top was clicked: "search" or "new".
        public event Action<string> TopTabClicked;

        /// A tab on the left was clicked: "recent", "tabs" or "setup".
        public event Action<string> LeftTabClicked;

        static readonly CornerRadius Square = new CornerRadius(0);

        readonly Canvas topTabs = new Canvas(), leftTabs = new Canvas(), rightTabs = new Canvas();
        readonly PageParts leftPage, rightPage;
        readonly Canvas leftLeaf;       // the left page and the edges of the pages under it
        readonly Canvas insideCover;    // what shows in its place when it is turned away (Setup)

        /// The parts of a page that change while leafing through the book.
        sealed class PageParts
        {
            public Border Content;      // what is written on it
            public Canvas Loose;        // what lies on it: the contact's index card
            public Canvas Number;       // the page number at the bottom
            public Canvas Curl;         // the folded corner
        }

        public BinderView(Color coverColor)
        {
            Width = Constants.DesignWidth;
            Height = Constants.DesignHeight;
            ClipToBounds = true;
            UseLayoutRounding = true;       // WPF: place everything on whole pixels
            SnapsToDevicePixels = true;

            // The top tabs sit behind the cover and show only their upper edge.
            Children.Add(topTabs);
            Css.Place(this, Cover(coverColor, out leftPage, out rightPage, out leftLeaf, out insideCover), 52, 24);
            Children.Add(rightTabs);
            Children.Add(leftTabs);
            SetTopTab(null);
            SetLeftTab(null);

            leftPage.Curl.MouseLeftButtonUp += delegate { if (PageTurnRequested != null) PageTurnRequested(-1); };
            rightPage.Curl.MouseLeftButtonUp += delegate { if (PageTurnRequested != null) PageTurnRequested(+1); };
        }

        /// The desk the book lies on: radial-gradient(ellipse at 50% 35%), its radii reaching the
        /// farthest corner (678.8 x 661.9 px on the 960 x 720 canvas). Painted by the window, not
        /// by the views, so that it carries on behind the bars a non-4:3 window leaves.
        public static Brush Desk()
        {
            return Css.RadialRelative(0.5, 0.35, 678.8 / 960, 661.9 / 720,
                Css.At("#2b2620", 0), Css.At("#17140f", 55), Css.At("#0a0907", 100));
        }

        // ---- what changes --------------------------------------------------------------

        /// Rebuilds the tabs on the right: Favorites, then one per category. The current one
        /// is wider and sticks out further.
        public void SetTabs(IList<Category> categories, int currentTab)
        {
            rightTabs.Children.Clear();
            int count = categories.Count + 1;
            double height = TabHeight(count);

            AddRightTab(rightTabs, 882, T("FAVORITES"), Css.Hex("#8f6d10"), FavoritesTab, currentTab, 0, height, id => Raise(TabClicked, id));
            for (int i = 0; i < categories.Count; i++)
                AddRightTab(rightTabs, 882, categories[i].Name, Css.Hex(categories[i].Colour), categories[i].Id, currentTab, i + 1, height, id => Raise(TabClicked, id));
        }

        public void SetPages(FrameworkElement left, FrameworkElement right, int firstPageNumber, bool canGoBack, bool canGoForward)
        {
            leftLeaf.Visibility = Visibility.Visible;
            insideCover.Children.Clear();
            Fill(leftPage, left, firstPageNumber, canGoBack);
            Fill(rightPage, right, firstPageNumber + 1, canGoForward);
            rightPage.Loose.Children.Clear();
        }

        /// Setup: the left leaf is turned away and the inside of the cover - a 362 x 608 panel -
        /// shows in its place; the page on the right is page 1.
        public void SetInsideCover(FrameworkElement inside, FrameworkElement right, bool canGoForward)
        {
            leftLeaf.Visibility = Visibility.Hidden;
            insideCover.Children.Clear();
            insideCover.Children.Add(inside);
            Fill(rightPage, right, 1, canGoForward);
            rightPage.Loose.Children.Clear();
        }

        /// Lays something loose over the right page - the contact's card - at x, y of the page.
        public void LayOnRightPage(FrameworkElement loose, double x, double y)
        {
            rightPage.Loose.Children.Clear();
            Css.Place(rightPage.Loose, loose, x, y);
        }

        /// Which tab along the top stands up: "search", "new", or null for neither.
        public void SetTopTab(string current)
        {
            topTabs.Children.Clear();
            Css.Place(topTabs, TopTab(T("SEARCH"), "search", current == "search"), 520, current == "search" ? 4 : 8);
            Css.Place(topTabs, TopTab(T("NEW"), "new", current == "new"), 624, current == "new" ? 4 : 8);
        }

        /// Which tab on the left sticks out: "recent", "tabs", "setup", or null for none.
        public void SetLeftTab(string current)
        {
            leftTabs.Children.Clear();
            string[] keys = { "recent", "tabs", "setup" };
            string[] labels = { T("RECENT"), T("TABS"), T("SETUP") };
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                bool isCurrent = key == current;
                Canvas tab = Controls.Id(SideTab(labels[i], Css.Hex("#4a4a48"), false, isCurrent, false, 100), "lefttab-" + key);
                tab.MouseLeftButtonUp += delegate { if (LeftTabClicked != null) LeftTabClicked(key); };
                Css.Place(leftTabs, tab, isCurrent ? 10 : 18, 72 + i * 114);
            }
        }

        static void Fill(PageParts page, FrameworkElement content, int number, bool canTurn)
        {
            page.Content.Child = content;
            page.Number.Children.Clear();
            page.Number.Children.Add(CenteredText(new GlyphText(number.ToString(), AppFonts.Type, 11, Css.Hex("#6b6252"), 2), 362, 11));
            // A corner with nothing behind it stays drawn but does not react.
            page.Curl.IsHitTestVisible = canTurn;
        }

        static void Raise(Action<int> handler, int value)
        {
            if (handler != null) handler(value);
        }

        // ---- tabs on the right (shared with the closed cover) -----------------------------

        /// Five tabs fit at the design's 100 px with 14 px between them; more than five share
        /// the same stretch of edge and get shorter.
        public static double TabHeight(int tabCount)
        {
            const double stretch = 5 * 100 + 4 * 14;
            return Math.Min(100, Math.Floor((stretch - 14 * (tabCount - 1)) / tabCount));
        }

        /// x = where a tab that is not the current one starts.
        public static void AddRightTab(Canvas layer, double x, string label, Color color, int id, int currentTab,
                                       int index, double height, Action<int> onClick)
        {
            bool isCurrent = id == currentTab;
            Canvas tab = Controls.Id(SideTab(label, color, true, isCurrent, id == FavoritesTab, height), "tab-" + id);
            tab.MouseLeftButtonUp += delegate { onClick(id); };
            Css.Place(layer, tab, isCurrent ? x - 8 : x, 72 + index * (height + 14));
        }

        // ---- cover ---------------------------------------------------------------------

        static Canvas Cover(Color coverColor, out PageParts leftPage, out PageParts rightPage, out Canvas leftLeaf, out Canvas insideCover)
        {
            var radius = new CornerRadius(22);
            var cover = Css.Box(856, 672, radius, new[] { Css.Fill(coverColor), Sheen(856, 672) }, CoverShadows());
            cover.Children.Add(Texture(Textures.Leather(856, 672), 856, 672, radius));
            Css.Place(cover, Stitching(836, 652, new CornerRadius(15)), 10, 10);

            // The dark well the pages lie in.
            Css.Place(cover, Css.Box(788, 620, new CornerRadius(6), Css.Fill(Css.Hex("#1a1714")),
                Shadow.Inset(0, 3, 10, Css.Rgba(0, 0, 0, 0.7))), 34, 26);

            Css.Place(cover, SpineBar(), 398, 44);

            var paper = Textures.Paper(362, 608);
            leftLeaf = new Canvas();
            Css.Place(leftLeaf, Css.Box(362, 608, new CornerRadius(3), Css.Fill(Css.Hex("#cfc3a4"))), 39, 38);
            Css.Place(leftLeaf, Css.Box(362, 608, new CornerRadius(3), Css.Fill(Css.Hex("#e0d5b8"))), 40, 35);
            Css.Place(leftLeaf, Page(true, paper, out leftPage), 42, 32);
            cover.Children.Add(leftLeaf);
            insideCover = Css.Place(cover, new Canvas { Width = 362, Height = 608 }, 42, 32);

            Css.Place(cover, Css.Box(362, 608, new CornerRadius(3), Css.Fill(Css.Hex("#cfc3a4"))), 455, 38);
            Css.Place(cover, Css.Box(362, 608, new CornerRadius(3), Css.Fill(Css.Hex("#e0d5b8"))), 454, 35);
            Css.Place(cover, Page(false, paper, out rightPage), 452, 32);

            Css.Place(cover, Rings(), 370, 32);
            return cover;
        }

        // The pieces below dress the open cover here and the closed one in CoverView.

        /// The light falling across the leather: linear-gradient(160deg, white 16% -> clear at 38% -> black 34%).
        public static Brush Sheen(double w, double h)
        {
            return Css.Linear(160, w, h,
                Css.At(Css.Rgba(255, 255, 255, 0.16), 0), Css.At(Css.Rgba(255, 255, 255, 0), 38), Css.At(Css.Rgba(0, 0, 0, 0.34), 100));
        }

        public static Shadow[] CoverShadows()
        {
            return new[]
            {
                Shadow.Outer(0, 30, 60, Css.Rgba(0, 0, 0, 0.65)),
                Shadow.Outer(0, 6, 12, Css.Rgba(0, 0, 0, 0.5)),
                Shadow.Inset(0, 1, 0, Css.Rgba(255, 255, 255, 0.22)),
                Shadow.Inset(0, -2, 0, Css.Rgba(0, 0, 0, 0.35)),
            };
        }

        /// The seam: a dashed line, 3 px on and 2 px off. The canvas says 1.5 px wide, but a
        /// browser rounds border widths down to whole pixels, so it is 1. "radius" is the CSS
        /// border-radius, measured on the outer edge; WPF measures a rounded rectangle on the
        /// middle of its stroke.
        public static FrameworkElement Stitching(double w, double h, CornerRadius radius)
        {
            const double thickness = 1, dash = 3, gap = 2;
            var r = new CornerRadius(radius.TopLeft - thickness / 2, radius.TopRight - thickness / 2,
                                     radius.BottomRight - thickness / 2, radius.BottomLeft - thickness / 2);
            double lineW = w - thickness, lineH = h - thickness;

            // A browser stretches the gaps a hair so that a whole number of stitches goes round
            // the seam: without it the pattern drifts two pixels by the time it comes back.
            double corners = r.TopLeft + r.TopRight + r.BottomRight + r.BottomLeft;
            double length = 2 * (lineW + lineH) - 2 * corners + Math.PI / 2 * corners;
            double stitches = Math.Round(length / (dash + gap));
            double fittedGap = (length - dash * stitches) / stitches;

            return new Path
            {
                Data = Css.RoundedRect(new Rect(thickness / 2, thickness / 2, lineW, lineH), r),
                Stroke = Css.Fill(Css.Rgba(255, 255, 255, 0.3)),
                StrokeThickness = thickness,
                StrokeDashArray = new DoubleCollection { dash, fittedGap },   // in units of the thickness
                IsHitTestVisible = false,
            };
        }

        /// A multiply texture (see Textures), clipped to the shape it covers.
        public static Image Texture(BitmapSource bitmap, double w, double h, CornerRadius radius)
        {
            return new Image
            {
                Source = bitmap,
                Width = w,
                Height = h,
                Stretch = Stretch.Fill,
                Clip = Css.RoundedRect(new Rect(0, 0, w, h), radius),
                IsHitTestVisible = false,
            };
        }

        // ---- pages ---------------------------------------------------------------------

        static Canvas Page(bool isLeft, BitmapSource paperTexture, out PageParts parts)
        {
            // The spine side is the darker, more rounded one.
            var radius = isLeft ? new CornerRadius(3, 6, 6, 3) : new CornerRadius(6, 3, 3, 6);
            var page = Css.Box(362, 608, radius,
                Css.Linear(isLeft ? 270 : 90, 362, 608, Css.At("#dfd3b5", 0), Css.At("#efe7d1", 9), Css.At("#f5efdd", 100)),
                Shadow.Outer(0, 2, 6, Css.Rgba(0, 0, 0, 0.4)));

            // overflow: hidden - applies to what is on the page, not to the page's own shadow.
            var sheet = new Canvas { Width = 362, Height = 608, Clip = Css.RoundedRect(new Rect(0, 0, 362, 608), radius) };
            page.Children.Add(sheet);

            foreach (double top in new double[] { 72, 110, 148, 448, 486, 524 })
                Css.Place(sheet, PunchedHole(), isLeft ? 342 : 8, top);

            parts = new PageParts
            {
                Content = Controls.Id(new Border { Width = 300, Height = 540 }, isLeft ? "page-left" : "page-right"),
                Loose = new Canvas { Width = 362, Height = 608 },
                Number = Controls.Id(new Canvas { Width = 362, Height = 11, IsHitTestVisible = false }, isLeft ? "page-number-left" : "page-number-right"),
                Curl = Controls.Id(PageCurl(isLeft), isLeft ? "page-back" : "page-forward"),
            };
            Css.Place(sheet, parts.Content, isLeft ? 36 : 26, 30);
            sheet.Children.Add(parts.Loose);
            Css.Place(sheet, parts.Curl, isLeft ? 0 : 318, 564);
            Css.Place(sheet, parts.Number, 0, 581);

            sheet.Children.Add(Texture(paperTexture, 362, 608, Square));
            return page;
        }

        static Canvas PunchedHole()
        {
            var hole = new Canvas { Width = 12, Height = 12, IsHitTestVisible = false };
            Css.Place(hole, new Ellipse { Width = 14, Height = 14, Fill = Css.Fill(Css.Rgba(0, 0, 0, 0.08)) }, -1, -1);
            // radial-gradient(circle at 40% 35%, ...): radius to the farthest corner = 10.61 px.
            hole.Children.Add(Css.Box(12, 12, new CornerRadius(6),
                Css.Radial(4.8, 4.2, 10.61, 10.61, Css.At("#4a4034", 0), Css.At("#1e1811", 70), Css.At("#1e1811", 100)),
                Shadow.Inset(0, 1, 2, Css.Hex("#000000"))));
            return hole;
        }

        /// The folded page corner that turns the page.
        static Canvas PageCurl(bool isLeft)
        {
            double angle = isLeft ? 225 : 135;   // "to bottom left" / "to bottom right" on a square
            var clear = Css.Rgba(0, 0, 0, 0);
            var fold = Css.Linear(angle, 44, 44,
                Css.At("#fbf6e8", 0), Css.At("#fbf6e8", 50), Css.At("#d9ceb0", 50), Css.At("#d9ceb0", 100));
            var crease = Css.Linear(angle, 44, 44,
                Css.At(clear, 0), Css.At(clear, 45), Css.At(Css.Rgba(0, 0, 0, 0.25), 50), Css.At(clear, 56), Css.At(clear, 100));
            var curl = Css.Box(44, 44, Square, new[] { fold, crease });
            Css.BrightenOnHover(curl, Square);
            return curl;
        }

        // ---- metal ---------------------------------------------------------------------

        /// The bar the rings are mounted on (design: first inline SVG of the cover).
        static Canvas SpineBar()
        {
            var bar = Css.SvgLinear(false,
                Css.At("#2f2f2d", 0), Css.At("#8a8a87", 12), Css.At("#e0e0dd", 35), Css.At("#c6c6c3", 50),
                Css.At("#7b7b78", 70), Css.At("#3d3d3b", 90), Css.At("#262624", 100));
            var rivet = Css.SvgRadial(0.35, 0.35, 0.7, Css.At("#f0f0ee", 0), Css.At("#a9a9a6", 50), Css.At("#4a4a48", 100));

            // SVG geometry uses half pixels on purpose: no rounding in here.
            var spine = new Canvas { Width = 60, Height = 590, UseLayoutRounding = false, SnapsToDevicePixels = false, IsHitTestVisible = false };
            spine.Children.Add(new Rectangle { Width = 60, Height = 584, RadiusX = 12, RadiusY = 12, Fill = bar });
            Css.Place(spine, new Rectangle { Width = 1, Height = 572, Fill = Css.Fill(Css.Rgba(0, 0, 0, 0.45)) }, 29, 6);
            Css.Place(spine, new Rectangle { Width = 1, Height = 572, Fill = Css.Fill(Css.Rgba(255, 255, 255, 0.35)) }, 30, 6);
            Css.Place(spine, new Ellipse { Width = 10, Height = 10, Fill = rivet }, 25, 13);
            Css.Place(spine, new Ellipse { Width = 10, Height = 10, Fill = rivet }, 25, 561);
            // Latch: 24 x 14 with a 1 px stroke centred on its edge.
            Css.Place(spine, new Rectangle
            {
                Width = 25,
                Height = 15,
                RadiusX = 4,
                RadiusY = 4,
                Fill = bar,
                Stroke = Css.Fill(Css.Rgba(0, 0, 0, 0.5)),
                StrokeThickness = 1,
            }, 17.5, 575.5);
            return spine;
        }

        /// The six rings, drawn over the pages (design: last inline SVG of the cover).
        static Canvas Rings()
        {
            var tube = Css.SvgLinear(true,
                Css.At("#5c5c5a", 0), Css.At("#cfcfcc", 12), Css.At("#fbfbfa", 30), Css.At("#d6d6d3", 50),
                Css.At("#8f8f8c", 68), Css.At("#4b4b49", 88), Css.At("#7a7a78", 100));
            var boss = Css.SvgRadial(0.4, 0.4, 0.7, Css.At("#9a9a97", 0), Css.At("#5a5a58", 70), Css.At("#2e2e2c", 100));

            var rings = new Canvas { Width = 116, Height = 608, UseLayoutRounding = false, SnapsToDevicePixels = false, IsHitTestVisible = false };
            foreach (double y in new double[] { 78, 116, 154, 454, 492, 530 })
            {
                Css.Place(rings, new Ellipse { Width = 14, Height = 14, Fill = boss }, 51, y - 7);
                // The ring's soft shadow on the paper: feGaussianBlur stdDeviation 1.6 = a 3.2 px CSS blur.
                Css.Place(rings, Css.SoftRect(68, 9, new CornerRadius(4.5), 3.2, Css.Rgba(0, 0, 0, 0.45)), 24, y - 1);
                Css.Place(rings, new Rectangle { Width = 76, Height = 9, RadiusX = 4.5, RadiusY = 4.5, Fill = tube }, 20, y - 4.5);
                Css.Place(rings, new Rectangle { Width = 1, Height = 9, Fill = Css.Fill(Css.Rgba(0, 0, 0, 0.5)) }, 57.5, y - 4.5);
            }
            return rings;
        }

        // ---- tabs ----------------------------------------------------------------------

        /// A paper tab along the top edge. The one in use stands 4 px taller, lighter, its label in red.
        Canvas TopTab(string label, string key, bool isCurrent)
        {
            var radius = new CornerRadius(8, 8, 0, 0);
            double h = isCurrent ? 56 : 48;
            var tab = Controls.Id(Css.Box(92, h, radius,
                Css.Linear(180, 92, h, Css.At(isCurrent ? "#fbf6e8" : "#f1e8d3", 0), Css.At(isCurrent ? "#f1e8d3" : "#dfd3b6", 100)),
                isCurrent ? Shadow.Outer(0, -2, 4, Css.Rgba(0, 0, 0, 0.35)) : Shadow.Outer(0, -1, 3, Css.Rgba(0, 0, 0, 0.35))), "toptab-" + key);

            var text = new GlyphText(label, AppFonts.Type, 12, Css.Hex(isCurrent ? "#b3261e" : "#3b3324"), 2);
            Css.Place(tab, CenteredText(text, 92, 12), 0, isCurrent ? 8 : 7);
            Css.BrightenOnHover(tab, radius);
            tab.MouseLeftButtonUp += delegate { if (TopTabClicked != null) TopTabClicked(key); };
            return tab;
        }

        /// A divider tab sticking out of the binder; the label runs along it.
        public static Canvas SideTab(string label, Color color, bool onRight, bool isCurrent, bool withStar, double h)
        {
            double w = isCurrent ? 68 : 60;
            var radius = onRight ? new CornerRadius(0, 10, 10, 0) : new CornerRadius(10, 0, 0, 10);

            var sheen = onRight
                ? Css.Linear(90, w, h,
                    Css.At(Css.Rgba(0, 0, 0, isCurrent ? 0.25 : 0.3), 0), Css.At(Css.Rgba(255, 255, 255, isCurrent ? 0.2 : 0.16), 28),
                    Css.At(Css.Rgba(255, 255, 255, 0), 60), Css.At(Css.Rgba(0, 0, 0, 0.22), 100))
                : Css.Linear(270, w, h,
                    Css.At(Css.Rgba(0, 0, 0, 0.25), 0), Css.At(Css.Rgba(255, 255, 255, 0.18), 28),
                    Css.At(Css.Rgba(255, 255, 255, 0), 60), Css.At(Css.Rgba(0, 0, 0, 0.22), 100));

            var drop = isCurrent
                ? Shadow.Outer(onRight ? 3 : -3, 4, 8, Css.Rgba(0, 0, 0, 0.5))
                : Shadow.Outer(onRight ? 2 : -2, 3, 6, Css.Rgba(0, 0, 0, 0.45));
            double shine = onRight ? (isCurrent ? 0.35 : 0.3) : (isCurrent ? 0.3 : 0.25);
            var topEdge = Shadow.Inset(0, 1, 0, Css.Rgba(255, 255, 255, shine));

            var tab = Css.Box(w, h, radius, new[] { Css.Fill(color), sheen }, drop, topEdge);

            // Written left to right, then turned: top to bottom on the right, bottom to top on the left.
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            // The label may run the whole length of the tab, as FAVORITES does on the canvas
            // (the last 2 px of its width are letter-spacing, not ink).
            double room = h + 2;
            if (withStar)
            {
                var white = Css.Fill(Colors.White);
                var star = Icons.Make(Icons.Star, 12, white, 1.8, white);
                star.Margin = new Thickness(0, 0, 6, 0);
                star.VerticalAlignment = VerticalAlignment.Center;
                star.RenderTransformOrigin = new Point(0.5, 0.5);
                star.RenderTransform = new RotateTransform(-90);   // the star itself stays upright
                line.Children.Add(star);
                room -= 18;
            }
            line.Children.Add(new GlyphText(label.ToUpperInvariant(), AppFonts.Type, 12, Colors.White, 2, double.NaN, room)
            {
                VerticalAlignment = VerticalAlignment.Center,
                // text-shadow: 0 1px 1px - one pixel down on screen is one pixel along the turned line
                ShadowBrush = Css.Fill(Css.Rgba(0, 0, 0, 0.5)),
                ShadowOffset = new Vector(1, 0),
            });
            // WPF: LayoutTransform turns the element and the room it takes, so it can be centred turned.
            line.LayoutTransform = new RotateTransform(onRight ? 90 : -90);

            tab.Children.Add(Centered(line, w, h));
            Css.BrightenOnHover(tab, radius);
            return tab;
        }

        /// A line of text centred in a w x h box the way a browser does it: across, to the
        /// fraction of a pixel (WPF's whole-pixel rounding would shift it); down, on a whole pixel.
        public static Canvas CenteredText(GlyphText text, double w, double h)
        {
            var box = new Canvas { Width = w, Height = h, UseLayoutRounding = false, IsHitTestVisible = false };
            Css.Place(box, text, (w - text.TextWidth) / 2, Math.Floor((h - text.LineHeight) / 2));
            return box;
        }

        public static Grid Centered(FrameworkElement child, double w, double h)
        {
            child.HorizontalAlignment = HorizontalAlignment.Center;
            child.VerticalAlignment = VerticalAlignment.Center;
            var grid = new Grid { Width = w, Height = h, IsHitTestVisible = false };
            grid.Children.Add(child);
            return grid;
        }
    }
}
