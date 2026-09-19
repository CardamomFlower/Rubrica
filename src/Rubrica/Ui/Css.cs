using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Rubrica.Ui
{
    /// One colour stop of a gradient: colour plus position in percent, as written in CSS.
    struct Stop
    {
        public readonly Color Color;
        public readonly double Percent;

        public Stop(Color color, double percent)
        {
            Color = color;
            Percent = percent;
        }
    }

    /// One CSS box-shadow: "dx dy blur colour", optionally inset.
    struct Shadow
    {
        public readonly bool IsInset;
        public readonly double Dx, Dy, Blur;
        public readonly Color Color;

        /// Bake it in one piece even when it is large (see Css.Sliced): for a box that is drawn
        /// turned, like the contact's card, where the joins between pieces would show as steps.
        public bool Whole;

        Shadow(bool isInset, double dx, double dy, double blur, Color color)
        {
            IsInset = isInset;
            Dx = dx;
            Dy = dy;
            Blur = blur;
            Color = color;
            Whole = false;
        }

        public static Shadow Outer(double dx, double dy, double blur, Color color) { return new Shadow(false, dx, dy, blur, color); }
        public static Shadow OuterWhole(double dx, double dy, double blur, Color color) { return new Shadow(false, dx, dy, blur, color) { Whole = true }; }
        public static Shadow Inset(double dx, double dy, double blur, Color color) { return new Shadow(true, dx, dy, blur, color); }
    }

    /// Translates the CSS vocabulary of the design canvas (colours, gradients, box shadows)
    /// into WPF objects, so the screens can be written with the design's own numbers.
    static class Css
    {
        // A CSS blur of B px is a Gaussian with sigma = B / 2. WPF's BlurEffect takes the
        // kernel radius, roughly 3 sigma.
        const double WpfBlurRadiusPerCssBlur = 1.5;

        // ---- colours -------------------------------------------------------------------

        public static Color Hex(string hex)
        {
            int rgb = int.Parse(hex.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }

        public static Color Rgba(int r, int g, int b, double alpha)
        {
            return Color.FromArgb((byte)Math.Round(alpha * 255), (byte)r, (byte)g, (byte)b);
        }

        public static Brush Fill(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        public static Stop At(Color color, double percent) { return new Stop(color, percent); }
        public static Stop At(string hex, double percent) { return new Stop(Hex(hex), percent); }

        // ---- gradients -----------------------------------------------------------------

        /// linear-gradient(angle, stops) painted on a w x h box. 0deg points up, 90deg right.
        public static Brush Linear(double angleDeg, double w, double h, params Stop[] stops)
        {
            double angle = angleDeg * Math.PI / 180;
            double dx = Math.Sin(angle), dy = -Math.Cos(angle);
            double half = (Math.Abs(w * dx) + Math.Abs(h * dy)) / 2;   // CSS gradient line length / 2
            var brush = new LinearGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                StartPoint = new Point(w / 2 - dx * half, h / 2 - dy * half),
                EndPoint = new Point(w / 2 + dx * half, h / 2 + dy * half),
                GradientStops = ToWpfStops(stops),
            };
            brush.Freeze();
            return brush;
        }

        /// radial-gradient(... at cx cy) with explicit radii, all in the box's own pixels.
        public static Brush Radial(double cx, double cy, double rx, double ry, params Stop[] stops)
        {
            var brush = new RadialGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                Center = new Point(cx, cy),
                GradientOrigin = new Point(cx, cy),
                RadiusX = rx,
                RadiusY = ry,
                GradientStops = ToWpfStops(stops),
            };
            brush.Freeze();
            return brush;
        }

        /// The same, with centre and radii as fractions of whatever it paints (0 to 1), so it
        /// stretches with its surface.
        public static Brush RadialRelative(double cx, double cy, double rx, double ry, params Stop[] stops)
        {
            var brush = new RadialGradientBrush
            {
                Center = new Point(cx, cy),
                GradientOrigin = new Point(cx, cy),
                RadiusX = rx,
                RadiusY = ry,
                GradientStops = ToWpfStops(stops),
            };
            brush.Freeze();
            return brush;
        }

        /// SVG gradients use the painted shape's bounding box as the unit square.
        public static Brush SvgLinear(bool vertical, params Stop[] stops)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = vertical ? new Point(0, 1) : new Point(1, 0),
                GradientStops = ToWpfStops(stops),
            };
            brush.Freeze();
            return brush;
        }

        public static Brush SvgRadial(double cx, double cy, double r, params Stop[] stops)
        {
            var brush = new RadialGradientBrush
            {
                Center = new Point(cx, cy),
                GradientOrigin = new Point(cx, cy),
                RadiusX = r,
                RadiusY = r,
                GradientStops = ToWpfStops(stops),
            };
            brush.Freeze();
            return brush;
        }

        /// CSS blends gradient colours "premultiplied" (each colour weighted by its own alpha),
        /// WPF blends them plain. The two agree when only the alpha or only the colour changes
        /// between two stops; when both change, WPF's fade drifts towards the more transparent
        /// colour. So a fully transparent stop borrows its neighbour's colour (exact), and a
        /// stretch where colour and alpha both change is cut into short steps computed the CSS way.
        static GradientStopCollection ToWpfStops(Stop[] stops)
        {
            var result = new GradientStopCollection();
            for (int i = 0; i < stops.Length; i++)
            {
                Color here = stops[i].Color;
                double offset = stops[i].Percent / 100;

                if (here.A != 0)
                    result.Add(new GradientStop(here, offset));
                else
                {
                    if (i > 0) result.Add(new GradientStop(Transparent(stops[i - 1].Color), offset));
                    if (i < stops.Length - 1) result.Add(new GradientStop(Transparent(stops[i + 1].Color), offset));
                }

                if (i == stops.Length - 1) continue;
                Color next = stops[i + 1].Color;
                bool colourChanges = here.R != next.R || here.G != next.G || here.B != next.B;
                if (!colourChanges || here.A == next.A || here.A == 0 || next.A == 0) continue;

                const int steps = 8;
                double nextOffset = stops[i + 1].Percent / 100;
                for (int s = 1; s < steps; s++)
                {
                    double t = (double)s / steps;
                    double alpha = here.A + t * (next.A - here.A);
                    result.Add(new GradientStop(Color.FromArgb(
                        (byte)Math.Round(alpha),
                        Premultiplied(here.R, here.A, next.R, next.A, t, alpha),
                        Premultiplied(here.G, here.A, next.G, next.A, t, alpha),
                        Premultiplied(here.B, here.A, next.B, next.A, t, alpha)), offset + t * (nextOffset - offset)));
                }
            }
            return result;
        }

        /// One colour channel, blended with each end weighted by its alpha.
        static byte Premultiplied(byte from, byte fromAlpha, byte to, byte toAlpha, double t, double alpha)
        {
            double weighted = from * (double)fromAlpha + t * (to * (double)toAlpha - from * (double)fromAlpha);
            return (byte)Math.Round(weighted / alpha);
        }

        static Color Transparent(Color c) { return Color.FromArgb(0, c.R, c.G, c.B); }

        // ---- boxes and shadows ---------------------------------------------------------

        /// A CSS box: backgrounds (bottom to top) with border-radius and box-shadows.
        /// Returns a canvas of the box's size; the caller adds the content on top.
        public static Canvas Box(double w, double h, CornerRadius radius, Brush[] backgrounds, params Shadow[] shadows)
        {
            var box = new Canvas { Width = w, Height = h };
            for (int i = shadows.Length - 1; i >= 0; i--)
                if (!shadows[i].IsInset) box.Children.Add(OuterShadow(w, h, radius, shadows[i]));
            foreach (var background in backgrounds)
                box.Children.Add(new Border { Width = w, Height = h, CornerRadius = radius, Background = background });
            for (int i = shadows.Length - 1; i >= 0; i--)
                if (shadows[i].IsInset) box.Children.Add(InsetShadow(w, h, radius, shadows[i]));
            return box;
        }

        public static Canvas Box(double w, double h, CornerRadius radius, Brush background, params Shadow[] shadows)
        {
            return Box(w, h, radius, new[] { background }, shadows);
        }

        /// The box's own shape, blurred and offset, painted behind it.
        static UIElement OuterShadow(double w, double h, CornerRadius radius, Shadow s)
        {
            var shadow = SoftRect(w, h, radius, s.Blur, s.Color, s.Whole);
            Canvas.SetLeft(shadow, s.Dx);
            Canvas.SetTop(shadow, s.Dy);
            return shadow;
        }

        /// A filled rounded rectangle with a blurred edge. (0, 0) of the returned element is
        /// the rectangle's own corner; the blur spills outside it.
        public static FrameworkElement SoftRect(double w, double h, CornerRadius radius, double cssBlur, Color color, bool whole = false)
        {
            if (cssBlur <= 0)
                return new Border { Width = w, Height = h, CornerRadius = radius, Background = Fill(color), IsHitTestVisible = false };

            double scale = BakeScale(cssBlur);
            double spill = Math.Ceiling((cssBlur * WpfBlurRadiusPerCssBlur + 2) / 4) * 4;

            // How far in from an edge a rounded corner or the blur can still be felt. Beyond that, a
            // big box is flat colour in the middle and the same strip all along each edge.
            double reach = Math.Ceiling(Math.Max(Math.Max(radius.TopLeft, radius.TopRight), Math.Max(radius.BottomLeft, radius.BottomRight))) + spill;
            bool wholePixels = scale == 1 && w == Math.Floor(w) && h == Math.Floor(h);
            if (!whole && wholePixels && w * h >= SliceFromArea && w >= 2 * reach + 8 && h >= 2 * reach + 8)
                return Sliced(w, h, radius, cssBlur, color, spill, reach);

            var area = new Rect(-spill, -spill, Math.Ceiling(w / 4) * 4 + 2 * spill, Math.Ceiling(h / 4) * 4 + 2 * spill);

            string key = "soft|" + w + "|" + h + "|" + radius + "|" + cssBlur + "|" + color;
            var image = Baked(key, area, scale, delegate
            {
                return new Border { Width = w, Height = h, CornerRadius = radius, Background = Fill(color), Effect = Blur(cssBlur) };
            });

            var holder = new Canvas { Width = w, Height = h, IsHitTestVisible = false };
            Place(holder, image, area.X, area.Y);
            return holder;
        }

        const double SliceFromArea = 40000;   // smaller shadows are baked whole: slicing them would save nothing

        /// The shadow of a big box, from a small bitmap. A page's shadow baked whole is a megabyte
        /// of pixels, nearly all of them one flat colour hidden under the page. Here a small box
        /// with the same corners and the same blur is baked once, and the big one is laid out from
        /// its pieces: four corners as they are, one pixel of each edge stretched along it, a plain
        /// rectangle in the middle. Same pixels, a few KB.
        static FrameworkElement Sliced(double w, double h, CornerRadius radius, double cssBlur, Color color, double spill, double reach)
        {
            double box = 2 * reach + 4;   // two corner zones and 4 px of plain edge between them
            var area = new Rect(-spill, -spill, box + 2 * spill, box + 2 * spill);
            string key = "rim|" + radius + "|" + cssBlur + "|" + color;
            BitmapSource tile = BakedBitmap(key, area, 1, delegate
            {
                return new Border { Width = box, Height = box, CornerRadius = radius, Background = Fill(color), Effect = Blur(cssBlur) };
            });

            // In pixels of the tile, which at scale 1 are the design's: c = a corner piece, side = the whole tile.
            int c = (int)(spill + reach), side = (int)(box + 2 * spill);
            double farX = w - reach, farY = h - reach, midW = w - 2 * reach, midH = h - 2 * reach;

            var holder = new Canvas { Width = w, Height = h, IsHitTestVisible = false };
            // WPF: when the window is not at its design size the pieces land on fractions of a pixel,
            // and softened edges would let a hairline show between them. Hard edges tile exactly.
            RenderOptions.SetEdgeMode(holder, EdgeMode.Aliased);

            Piece(holder, tile, key, 0, 0, c, c, -spill, -spill, c, c);
            Piece(holder, tile, key, side - c, 0, c, c, farX, -spill, c, c);
            Piece(holder, tile, key, 0, side - c, c, c, -spill, farY, c, c);
            Piece(holder, tile, key, side - c, side - c, c, c, farX, farY, c, c);
            Piece(holder, tile, key, c, 0, 1, c, reach, -spill, midW, c);            // top and bottom edges
            Piece(holder, tile, key, c, side - c, 1, c, reach, farY, midW, c);
            Piece(holder, tile, key, 0, c, c, 1, -spill, reach, c, midH);            // left and right edges
            Piece(holder, tile, key, side - c, c, c, 1, farX, reach, c, midH);
            Place(holder, new Rectangle { Width = midW, Height = midH, Fill = Fill(color) }, reach, reach);
            return holder;
        }

        static readonly Dictionary<string, BitmapSource> tilePieces = new Dictionary<string, BitmapSource>();

        /// One rectangle of a tile (x, y, w, h in its pixels), drawn at (left, top) as wide and high as asked.
        static void Piece(Canvas holder, BitmapSource tile, string tileKey, int x, int y, int w, int h, double left, double top, double width, double height)
        {
            string key = tileKey + "|" + x + "," + y + "," + w + "," + h;
            BitmapSource piece;
            if (!tilePieces.TryGetValue(key, out piece))
            {
                piece = new CroppedBitmap(tile, new Int32Rect(x, y, w, h));
                piece.Freeze();
                tilePieces[key] = piece;
            }
            var image = new Image { Source = piece, Width = width, Height = height, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);   // an edge strip is repeated, not faded at its ends
            Place(holder, image, left, top);
        }

        /// Everything outside the (offset) box shape, blurred, and clipped to the box: what
        /// is left is the shadow that falls inside the edges.
        static UIElement InsetShadow(double w, double h, CornerRadius radius, Shadow s)
        {
            if (s.Blur <= 0) return InsetShadowShape(w, h, radius, s);

            string key = "inset|" + w + "|" + h + "|" + radius + "|" + s.Dx + "|" + s.Dy + "|" + s.Blur + "|" + s.Color;
            return Baked(key, new Rect(0, 0, w, h), BakeScale(s.Blur), delegate { return InsetShadowShape(w, h, radius, s); });
        }

        /// A wide blur has no fine detail, so it can be drawn smaller and stretched back:
        /// half the resolution costs a quarter, a quarter of it a sixteenth.
        static double BakeScale(double cssBlur)
        {
            return cssBlur >= 24 ? 0.25 : cssBlur >= 10 ? 0.5 : 1;
        }

        static FrameworkElement InsetShadowShape(double w, double h, CornerRadius radius, Shadow s)
        {
            var shape = RoundedRect(new Rect(0, 0, w, h), radius);
            var moved = shape.Clone();
            moved.Transform = new TranslateTransform(s.Dx, s.Dy);

            double margin = s.Blur * 3 + Math.Abs(s.Dx) + Math.Abs(s.Dy) + 2;
            var outside = new CombinedGeometry(GeometryCombineMode.Exclude,
                new RectangleGeometry(new Rect(-margin, -margin, w + 2 * margin, h + 2 * margin)), moved);

            var paint = new Path { Data = outside, Fill = Fill(s.Color) };
            if (s.Blur > 0) paint.Effect = Blur(s.Blur);

            var clip = new Canvas { Width = w, Height = h, Clip = shape, IsHitTestVisible = false };
            clip.Children.Add(paint);
            return clip;
        }

        static Effect Blur(double cssBlur)
        {
            var blur = new BlurEffect
            {
                Radius = cssBlur * WpfBlurRadiusPerCssBlur,
                KernelType = KernelType.Gaussian,
                RenderingBias = RenderingBias.Quality,
            };
            blur.Freeze();
            return blur;
        }

        // ---- baking --------------------------------------------------------------------

        static readonly Dictionary<string, BitmapSource> bakedBitmaps = new Dictionary<string, BitmapSource>();

        /// Draws a blurred shape once, at startup, and returns it as a plain image.
        /// WPF re-runs an Effect every time its element is redrawn, which is costly on an old
        /// GPU and worse without one; an image costs nothing. Equal shapes share one bitmap.
        /// "area" is the part of the element to capture, in the element's own coordinates.
        static Image Baked(string key, Rect area, double scale, Func<FrameworkElement> build)
        {
            return new Image
            {
                Source = BakedBitmap(key, area, scale, build),
                Width = area.Width,
                Height = area.Height,
                Stretch = Stretch.Fill,
                IsHitTestVisible = false,
            };
        }

        static BitmapSource BakedBitmap(string key, Rect area, double scale, Func<FrameworkElement> build)
        {
            BitmapSource bitmap;
            if (!bakedBitmaps.TryGetValue(key, out bitmap))
            {
                var host = new Canvas { Width = area.Width, Height = area.Height };
                Place(host, build(), -area.X, -area.Y);
                host.Measure(new Size(area.Width, area.Height));
                host.Arrange(new Rect(0, 0, area.Width, area.Height));
                host.UpdateLayout();

                var target = new RenderTargetBitmap((int)Math.Ceiling(area.Width * scale), (int)Math.Ceiling(area.Height * scale),
                                                    96 * scale, 96 * scale, PixelFormats.Pbgra32);
                target.Render(host);
                target.Freeze();
                bakedBitmaps[key] = bitmap = target;
            }
            return bitmap;
        }

        /// Rounded rectangle with a different radius per corner (WPF's own takes one radius).
        public static Geometry RoundedRect(Rect r, CornerRadius c)
        {
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(new Point(r.Left + c.TopLeft, r.Top), true, true);
                g.LineTo(new Point(r.Right - c.TopRight, r.Top), true, false);
                Corner(g, new Point(r.Right, r.Top + c.TopRight), c.TopRight);
                g.LineTo(new Point(r.Right, r.Bottom - c.BottomRight), true, false);
                Corner(g, new Point(r.Right - c.BottomRight, r.Bottom), c.BottomRight);
                g.LineTo(new Point(r.Left + c.BottomLeft, r.Bottom), true, false);
                Corner(g, new Point(r.Left, r.Bottom - c.BottomLeft), c.BottomLeft);
                g.LineTo(new Point(r.Left, r.Top + c.TopLeft), true, false);
                Corner(g, new Point(r.Left + c.TopLeft, r.Top), c.TopLeft);
            }
            geometry.Freeze();
            return geometry;
        }

        static void Corner(StreamGeometryContext g, Point end, double radius)
        {
            if (radius > 0)
                g.ArcTo(end, new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
        }

        // ---- interaction ---------------------------------------------------------------

        /// The design's "filter: brightness(1.08)" on hover, as a faint white veil.
        /// Call it last, so the veil sits above the box's content.
        public static void BrightenOnHover(Canvas box, CornerRadius radius, double strength = 0.08)
        {
            var veil = new Border
            {
                Width = box.Width,
                Height = box.Height,
                CornerRadius = radius,
                Background = Fill(Rgba(255, 255, 255, strength)),
                Opacity = 0,
                IsHitTestVisible = false,
            };
            box.Children.Add(veil);
            box.Cursor = System.Windows.Input.Cursors.Hand;
            box.MouseEnter += delegate { veil.Opacity = 1; };
            box.MouseLeave += delegate { veil.Opacity = 0; };
        }

        /// Adds a child at the given position; returns it so calls can be nested.
        public static T Place<T>(Canvas parent, T child, double x, double y) where T : UIElement
        {
            Canvas.SetLeft(child, x);
            Canvas.SetTop(child, y);
            parent.Children.Add(child);
            return child;
        }
    }
}
