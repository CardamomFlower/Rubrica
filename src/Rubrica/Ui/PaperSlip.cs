using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using static Rubrica.Ui.Lang;

namespace Rubrica.Ui
{
    /// The layer above the book. Three things live on it, bottom to top:
    ///   a slip  - a question on a slip of paper; the book behind it is dimmed and out of reach
    ///   a strip - a line of paper along the bottom edge that goes away by itself (UNDO)
    ///   a menu  - the choices of a PaperSelect, unrolled; a click anywhere else rolls it up
    /// There is one layer because there is one window; MainWindow attaches it.
    static class Overlay
    {
        static Canvas layer;
        static Canvas slipGroup, menuGroup;
        static FrameworkElement strip;
        static DispatcherTimer stripTimer;

        public static void Attach(Canvas canvas)
        {
            layer = canvas;
            slipGroup = null;
            menuGroup = null;
            strip = null;
            if (stripTimer != null) stripTimer.Stop();
        }

        // ---- slip ----------------------------------------------------------------------

        public static bool SlipIsOpen { get { return slipGroup != null; } }

        /// Esc closes a slip, which is what its "keep" stamp does too.
        public static void ShowSlip(FrameworkElement slip, double x, double y)
        {
            CloseSlip();
            slipGroup = Controls.Id(new Canvas { Width = Constants.DesignWidth, Height = Constants.DesignHeight }, "slip");
            // The dimmer also swallows every click meant for the book underneath.
            slipGroup.Children.Add(new Rectangle { Width = Constants.DesignWidth, Height = Constants.DesignHeight, Fill = Css.Fill(Css.Rgba(15, 12, 8, 0.42)) });
            Css.Place(slipGroup, Controls.Id(slip, "slip-paper"), x, y);
            // Above the strip, so that its dimmer covers an UNDO too: undoing something under an
            // open question would leave the question about a book that has changed.
            layer.Children.Add(slipGroup);
        }

        public static void CloseSlip()
        {
            if (slipGroup == null) return;
            layer.Children.Remove(slipGroup);
            slipGroup = null;
        }

        // ---- strip ---------------------------------------------------------------------

        public static bool StripIsOpen { get { return strip != null; } }

        public static void ShowStrip(FrameworkElement newStrip, double x, double y, int seconds)
        {
            CloseStrip();
            strip = Controls.Id(newStrip, "strip");
            Css.Place(layer, strip, x, y);
            if (slipGroup != null) layer.Children.Remove(strip);
            if (slipGroup != null) layer.Children.Insert(layer.Children.IndexOf(slipGroup), strip);   // below an open slip

            // WPF: a DispatcherTimer ticks on the window's own thread, so it may touch the window.
            stripTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            stripTimer.Tick += delegate { CloseStrip(); };
            stripTimer.Start();
        }

        public static void CloseStrip()
        {
            if (stripTimer != null) stripTimer.Stop();
            stripTimer = null;
            if (strip == null) return;
            layer.Children.Remove(strip);
            strip = null;
        }

        // ---- menu ----------------------------------------------------------------------

        public static bool MenuIsOpen { get { return menuGroup != null; } }

        /// Unrolls a menu at "offset" from the top-left corner of "anchor".
        public static void OpenMenu(FrameworkElement menu, FrameworkElement anchor, Point offset)
        {
            CloseMenu();
            menuGroup = new Canvas { Width = Constants.DesignWidth, Height = Constants.DesignHeight };
            var catcher = new Rectangle { Width = Constants.DesignWidth, Height = Constants.DesignHeight, Fill = Brushes.Transparent };
            catcher.MouseLeftButtonUp += delegate { CloseMenu(); };
            menuGroup.Children.Add(catcher);

            // WPF: where the anchor's corner is, in the layer's coordinates, whatever panels lie between.
            Point at = anchor.TranslatePoint(offset, layer);
            Css.Place(menuGroup, menu, Math.Round(at.X), Math.Round(at.Y));
            layer.Children.Add(menuGroup);
        }

        public static void CloseMenu()
        {
            if (menuGroup == null) return;
            layer.Children.Remove(menuGroup);
            menuGroup = null;
        }
    }

    /// The slips and strips of paper the book talks with.
    static class PaperSlip
    {
        public const double Width = 336;

        /// The slip of the "Delete - confirm" artboard: a red typed question, a handwritten
        /// explanation, and two stamps - the harmless one first. 336 x 206, a little crooked.
        public static Canvas Question(string title, string body, string keepLabel, string confirmLabel, Action onKeep, Action onConfirm)
        {
            return Sheet(title, body, null, keepLabel, confirmLabel, StampStyle.RedSolid, onKeep, onConfirm);
        }

        /// A slip that only tells something: one stamp, OK.
        public static Canvas Notice(string title, string body, Action onOk)
        {
            return Sheet(title, body, null, null, T("OK"), StampStyle.DarkOutline, null, onOk);
        }

        /// The general slip. extra: a control between the text and the stamps (a PaperSelect), or
        /// null; such a slip lies straight, so that the menu it unrolls lines up with it.
        /// keepLabel: null for a slip with one stamp; confirmLabel null too for one with none
        /// (the installer's "A moment."). The slip is 206 px high, or as much as what is
        /// written on it needs.
        public static Canvas Sheet(string title, string body, FrameworkElement extra, string keepLabel, string confirmLabel,
                                   StampStyle confirmStyle, Action onKeep, Action onConfirm)
        {
            const double w = Width, padX = 25, padY = 23;   // 1 px border + the canvas's 24 / 22 padding
            StackPanel titleLines = GlyphText.Wrap(title, AppFonts.Type, 14, Controls.Red, 2, 14, w - 2 * padX);
            StackPanel bodyLines = GlyphText.Wrap(body, AppFonts.Hand, 19, Controls.Ink, 0, 24, w - 2 * padX);
            Canvas confirm = confirmLabel == null ? null : Controls.Id(Controls.Stamp(confirmLabel, confirmStyle, -1.5, onConfirm), "slip-confirm");
            const double stampHeight = 46;

            double bodyTop = padY + 14 * titleLines.Children.Count + 10;
            double extraTop = bodyTop + 24 * bodyLines.Children.Count + 12;
            double written = extra == null ? extraTop - 12 : extraTop + extra.Height;
            double h = Math.Max(206, written + 18 + stampHeight + padY);

            var slip = Css.Box(w, h, new CornerRadius(0),
                Css.Linear(180, w, h, Css.At("#fdfaf1", 0), Css.At("#f1e8d3", 100)),
                Shadow.Outer(0, 18, 40, Css.Rgba(0, 0, 0, 0.6)));
            slip.Children.Add(new Rectangle { Width = w, Height = h, Stroke = Css.Fill(Css.Hex("#d6cbae")), StrokeThickness = 1, IsHitTestVisible = false });

            Css.Place(slip, titleLines, padX, padY);
            Css.Place(slip, bodyLines, padX, bodyTop);
            if (extra != null) Css.Place(slip, extra, padX, extraTop);

            double top = h - padY - stampHeight;
            if (confirm != null) Css.Place(slip, confirm, w - padX - confirm.Width, top);
            if (confirm != null && keepLabel != null)
            {
                Canvas keep = Controls.Id(Controls.Stamp(keepLabel, StampStyle.DarkOutline, 1, onKeep), "slip-keep");
                Css.Place(slip, keep, w - padX - confirm.Width - 12 - keep.Width, top);
            }

            if (extra == null)
            {
                slip.RenderTransformOrigin = new Point(0.5, 0.5);
                slip.RenderTransform = new RotateTransform(-2);
            }
            return slip;
        }

        /// The strip of the "Deleted - undo" artboard: what happened, and optionally one stamp.
        /// 360 x 46, lying along the bottom of the book.
        public static Canvas Strip(string message, string stampLabel, Action onStamp)
        {
            const double w = 360, h = 46;
            var strip = Css.Box(w, h, new CornerRadius(0),
                Css.Linear(180, w, h, Css.At("#fbf6e8", 0), Css.At("#ece3cc", 100)),
                Shadow.Outer(0, 6, 14, Css.Rgba(0, 0, 0, 0.5)));

            double room = w - 16 - 12;
            if (stampLabel != null)
            {
                Canvas stamp = Controls.Id(Controls.Stamp(stampLabel, StampStyle.RedSolid, 1, onStamp, 12, 36), "strip-stamp");
                Css.Place(strip, stamp, w - 12 - stamp.Width, (h - stamp.Height) / 2);
                room -= stamp.Width + 12;
            }
            var text = new GlyphText(message, AppFonts.Type, 12, Controls.Ink, 2, double.NaN, room);
            Css.Place(strip, text, 16, Math.Floor((h - text.LineHeight) / 2));

            strip.RenderTransformOrigin = new Point(0.5, 0.5);
            strip.RenderTransform = new RotateTransform(-1);
            return strip;
        }
    }
}
