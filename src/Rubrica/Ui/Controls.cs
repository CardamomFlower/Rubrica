using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Rubrica.Ui
{
    /// The three rubber stamps of the design.
    enum StampStyle
    {
        RedOutline,     // red outline, faint red wash: NEW CONTACT, COPY, DELETE
        DarkOutline,    // ink outline, no fill: EDIT, CANCEL, KEEP
        RedSolid,       // solid red, white letters, a small shadow: SAVE, YES REMOVE, UNDO, ADD
    }

    /// The small interactive pieces the pages are written with.
    static class Controls
    {
        public static readonly Color Ink = Css.Hex("#1b2233");
        public static readonly Color Pencil = Css.Hex("#6b6252");
        public static readonly Color Red = Css.Hex("#b3261e");
        public static readonly Color Rule = Css.Hex("#8a7f6a");    // the line under a field

        /// Gives an element a stable id ("star-17", "tab-3", "page-forward"). It is WPF's standard
        /// accessibility id; the tests find the things they click by it.
        public static T Id<T>(T element, string id) where T : DependencyObject
        {
            System.Windows.Automation.AutomationProperties.SetAutomationId(element, id);
            return element;
        }

        /// A rubber-stamp button (design class "stamp"): a label in a box, slightly crooked.
        public static Canvas Stamp(string label, StampStyle style, double tiltDegrees, Action onClick,
                                   double sidePadding = 14, double contentHeight = 44)
        {
            // The canvas asks for a 1.5 px border; a browser rounds border widths down to whole pixels.
            const double border = 1;
            Color line = style == StampStyle.DarkOutline ? Ink : Red;
            Color letters = style == StampStyle.RedSolid ? Colors.White : line;

            var text = new GlyphText(label, AppFonts.Type, 11, letters, 2);
            double w = text.TextWidth + 2 * sidePadding + 2 * border;
            double h = contentHeight + 2 * border;

            // Half-pixel geometry, as in the browser: WPF must not round it to whole pixels.
            var stamp = new Canvas { Width = w, Height = h, UseLayoutRounding = false, Cursor = Cursors.Hand };
            if (style == StampStyle.RedSolid)
                Css.Place(stamp, Css.SoftRect(w, h, new CornerRadius(4), 4, Css.Rgba(0, 0, 0, 0.3)), 0, 2);   // box-shadow: 0 2px 4px

            Brush fill = style == StampStyle.RedSolid ? Css.Fill(Red)
                       : style == StampStyle.RedOutline ? Css.Fill(Color.FromArgb(15, Red.R, Red.G, Red.B))   // rgba(.., 0.06)
                       : Brushes.Transparent;   // transparent still catches the mouse; no fill at all would not
            stamp.Children.Add(new Rectangle
            {
                Width = w,
                Height = h,
                RadiusX = 4 - border / 2,
                RadiusY = 4 - border / 2,
                Stroke = Css.Fill(line),
                StrokeThickness = border,
                Fill = fill,
            });
            Css.Place(stamp, text, border + sidePadding, border + (contentHeight - text.LineHeight) / 2);

            // WPF: RenderTransform turns the drawing without changing the room it takes in the layout.
            stamp.RenderTransformOrigin = new Point(0.5, 0.5);
            stamp.RenderTransform = new RotateTransform(tiltDegrees);

            Css.BrightenOnHover(stamp, new CornerRadius(4));
            stamp.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (onClick != null) onClick();
            };
            return stamp;
        }

        /// A 16 px line icon in a click target, 40 x 44 unless told otherwise (the arrows and
        /// the cross of the Tabs page, the x of the contact's card).
        public static Grid IconButton(string pathData, Color color, Action onClick, double width = 40, double height = 44)
        {
            var button = new Grid { Width = width, Height = height, Background = Brushes.Transparent, Cursor = Cursors.Hand };
            Path icon = Icons.Make(pathData, 16, Css.Fill(color), 2.2);
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
            button.Children.Add(icon);
            button.MouseLeftButtonUp += delegate { if (onClick != null) onClick(); };
            return button;
        }

        /// The favorite star, in the list and on the card: gold when it is one, an outline when not.
        public static void PaintStar(Path star, bool favorite)
        {
            star.Fill = favorite ? Css.Fill(Css.Hex("#d9a520")) : null;
            star.Stroke = Css.Fill(Css.Hex(favorite ? "#8a6a12" : "#8a7f6a"));
        }

        /// A square of tab colour with the sheen the design gives it.
        public static Canvas Swatch(Color color, double size, double radius)
        {
            var sheen = Css.Linear(90, size, size, Css.At(Css.Rgba(255, 255, 255, 0.25), 0), Css.At(Css.Rgba(0, 0, 0, 0.15), 100));
            return Css.Box(size, size, new CornerRadius(radius), new[] { Css.Fill(color), sheen },
                           Shadow.Outer(0, 1, 2, Css.Rgba(0, 0, 0, 0.4)));
        }

        /// The head of a page: handwritten title, a typed label on its baseline at the right,
        /// an ink rule under both. 32 px in all (Tabs, Setup).
        public static FrameworkElement PageHeader(string title, string label)
        {
            var header = new Grid { Height = 32 };
            header.Children.Add(new GlyphText(title, AppFonts.Hand, 24, Ink, 1, 28) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top });
            header.Children.Add(new GlyphText(label, AppFonts.Type, 11, Pencil, 2)
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 15, 0, 0),
            });
            header.Children.Add(new Rectangle { Height = 2, Fill = Css.Fill(Ink), VerticalAlignment = VerticalAlignment.Bottom });
            return header;
        }

        /// A radio button as a browser draws one at 20 px with accent-color set: a thin ring on
        /// white - red when chosen, grey when not - and a 12 px dot in the chosen one.
        public static Canvas Radio(bool chosen)
        {
            var radio = new Canvas { Width = 20, Height = 20, UseLayoutRounding = false, IsHitTestVisible = false };
            Color ring = chosen ? Red : Css.Hex("#767676");
            radio.Children.Add(new Ellipse { Width = 20, Height = 20, Fill = Brushes.White, Stroke = Css.Fill(ring), StrokeThickness = 1.15 });
            if (chosen) Css.Place(radio, new Ellipse { Width = 11.6, Height = 11.6, Fill = Css.Fill(Red) }, 4.2, 4.2);
            return radio;
        }

        /// A key of the keyboard legend: 30 px high and at least 34 wide, a 1 px frame standing
        /// on a 2 px foot (box-shadow: 0 2px 0, which a browser paints only outside the box).
        public static Canvas KeyCap(string key)
        {
            var label = new GlyphText(key, AppFonts.Type, 12, Ink, 1);
            double w = Math.Max(34, Math.Round(label.TextWidth + 2 * (1 + 8))), h = 30;
            var corners = new CornerRadius(5);

            var cap = new Canvas { Width = w, Height = h, IsHitTestVisible = false };
            Geometry box = Css.RoundedRect(new Rect(0, 0, w, h), corners);
            Geometry foot = Css.RoundedRect(new Rect(0, 2, w, h), corners);
            cap.Children.Add(new Path { Data = new CombinedGeometry(GeometryCombineMode.Exclude, foot, box), Fill = Css.Fill(Pencil) });
            cap.Children.Add(new Border { Width = w, Height = h, CornerRadius = corners, BorderBrush = Css.Fill(Pencil), BorderThickness = new Thickness(1) });
            cap.Children.Add(BinderView.CenteredText(label, w, h));
            return cap;
        }

        /// A small typewriter caption, as above every field: NAME, PHONE 1...
        public static GlyphText Caption(string text)
        {
            return new GlyphText(text, AppFonts.Type, 10, Pencil, 2) { HorizontalAlignment = HorizontalAlignment.Left };
        }

        // WPF: a control's "template" is the tree of elements it is drawn with. The standard
        // text box template brings a border, a background and hover colours of its own; this one
        // is nothing but the part that shows the text, so the page shows through.
        static ControlTemplate bareTextBox;

        public static ControlTemplate BareTextBox()
        {
            if (bareTextBox == null)
            {
                var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");   // the name WPF looks for
                host.SetValue(UIElement.FocusableProperty, false);
                bareTextBox = new ControlTemplate(typeof(TextBox)) { VisualTree = host };
                bareTextBox.Seal();
            }
            return bareTextBox;
        }
    }

    /// A line to write on (the design's text input): handwriting on the page, a rule under it
    /// that turns red while it is being written on. 44 px tall, rule included.
    sealed class PaperLine : Grid
    {
        public readonly TextBox Box;
        readonly Rectangle rule;
        bool missing;

        /// fontSize: 21 on the forms, 20 on the Tabs page. underlined: false for the in-place tab names.
        public PaperLine(double width, string id, double fontSize = 21, bool underlined = true)
        {
            Width = width;
            Height = 44;

            Box = Controls.Id(new TextBox
            {
                Template = Controls.BareTextBox(),
                MaxLength = Constants.MaxFieldLength,
                UndoLimit = 50,
                Width = width,
                Height = underlined ? 43 : 44,   // the rule takes the last pixel
                VerticalAlignment = VerticalAlignment.Top,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(2, 0, 2, 0),      // plus the 2 px WPF keeps for the caret = the 4 the design asks for
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontFamily = AppFonts.Hand.Family,
                FontSize = fontSize,
                Foreground = Css.Fill(Controls.Ink),
                CaretBrush = Css.Fill(Controls.Ink),
                SelectionBrush = Css.Fill(Controls.Red),
                SelectionOpacity = 0.25,
                FocusVisualStyle = null,                  // no dotted focus rectangle: the rule shows the focus
                Cursor = Cursors.IBeam,
            }, id);
            Children.Add(Box);

            rule = new Rectangle { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Fill = Css.Fill(Controls.Rule), IsHitTestVisible = false };
            if (underlined) Children.Add(rule);

            Box.GotKeyboardFocus += delegate { Paint(); };
            Box.LostKeyboardFocus += delegate { Paint(); };
        }

        public string Text
        {
            get { return Box.Text.Trim(); }
            set { Box.Text = value ?? ""; }
        }

        /// A required field left empty: its rule stays red until something is typed.
        public bool Missing
        {
            set
            {
                missing = value;
                Paint();
            }
        }

        void Paint()
        {
            rule.Fill = Css.Fill(missing || Box.IsKeyboardFocused ? Controls.Red : Controls.Rule);
        }
    }

    /// The NOTES box: three ruled lines to write on, scrolling when there is more.
    sealed class PaperNotes : Grid
    {
        public readonly TextBox Box;

        public PaperNotes(double width, string id)
        {
            const double lineHeight = 28, lines = 3;
            Width = width;
            Height = lineHeight * lines;

            // repeating-linear-gradient: a faint blue line along the bottom of every 28 px.
            for (int i = 1; i <= lines; i++)
                Children.Add(new Rectangle
                {
                    Height = 1,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, i * lineHeight - 1, 0, 0),
                    Fill = Css.Fill(i < lines ? Css.Rgba(96, 130, 190, 0.3) : Controls.Rule),
                    IsHitTestVisible = false,
                });

            Box = Controls.Id(new TextBox
            {
                Template = Controls.BareTextBox(),
                MaxLength = Constants.MaxNotesLength,
                UndoLimit = 50,
                Width = width,
                Height = lineHeight * lines - 1,
                VerticalAlignment = VerticalAlignment.Top,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                Padding = new Thickness(2, 0, 2, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontFamily = AppFonts.Hand.Family,
                FontSize = 19,
                Foreground = Css.Fill(Controls.Ink),
                CaretBrush = Css.Fill(Controls.Ink),
                SelectionBrush = Css.Fill(Controls.Red),
                SelectionOpacity = 0.25,
                FocusVisualStyle = null,
                Cursor = Cursors.IBeam,
            }, id);
            // WPF: these two make every line of the box exactly 28 px tall, so the writing sits on the rules.
            TextBlock.SetLineHeight(Box, lineHeight);
            TextBlock.SetLineStackingStrategy(Box, LineStackingStrategy.BlockLineHeight);
            Children.Add(Box);
        }

        public string Text
        {
            get { return Box.Text.Trim(); }
            set { Box.Text = value ?? ""; }
        }
    }

    /// The design's checkbox: 22 x 22, red with a white tick when on.
    sealed class PaperCheck : Canvas
    {
        readonly Rectangle box;
        readonly Path tick;
        bool isChecked;

        public event Action Toggled;

        public PaperCheck(string id)
        {
            Width = 22;
            Height = 22;
            Cursor = Cursors.Hand;
            Controls.Id(this, id);

            box = new Rectangle { Width = 22, Height = 22, RadiusX = 3, RadiusY = 3, StrokeThickness = 1 };
            tick = new Path
            {
                Data = Geometry.Parse("M5 11.5l4 4 8-9"),
                Stroke = Brushes.White,
                StrokeThickness = 2.6,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false,
            };
            Children.Add(box);
            Children.Add(tick);
            Paint();
            MouseLeftButtonUp += delegate { IsChecked = !IsChecked; if (Toggled != null) Toggled(); };
        }

        public bool IsChecked
        {
            get { return isChecked; }
            set
            {
                isChecked = value;
                Paint();
            }
        }

        void Paint()
        {
            box.Fill = isChecked ? Css.Fill(Controls.Red) : Brushes.White;
            box.Stroke = Css.Fill(isChecked ? Controls.Red : Css.Hex("#767676"));
            tick.Visibility = isChecked ? Visibility.Visible : Visibility.Hidden;
        }
    }

    /// The design's select: the chosen value on a ruled line with a small arrow; a click
    /// unrolls the choices on a slip of paper just below (see Overlay).
    sealed class PaperSelect : Grid
    {
        readonly List<string> options;
        readonly Grid valueHost = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        readonly string id;
        int selected;

        public event Action Changed;

        public PaperSelect(double width, string id, List<string> options, int selected)
        {
            this.id = id;
            this.options = options;
            Width = width;
            Height = 44;
            Background = Brushes.Transparent;
            Cursor = Cursors.Hand;
            Controls.Id(this, id);

            Children.Add(valueHost);
            Children.Add(new Rectangle { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Fill = Css.Fill(Controls.Rule), IsHitTestVisible = false });
            Path arrow = Icons.Make("M6 9l6 6 6-6", 16, Css.Fill(Controls.Pencil), 2);
            arrow.HorizontalAlignment = HorizontalAlignment.Right;
            arrow.VerticalAlignment = VerticalAlignment.Top;
            arrow.Margin = new Thickness(0, 14, 6, 0);
            Children.Add(arrow);

            SelectedIndex = selected;
            MouseLeftButtonUp += delegate { Unroll(); };
        }

        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                selected = Math.Max(0, Math.Min(value, options.Count - 1));
                valueHost.Children.Clear();
                // 21 px handwriting centred in the 43 px above the rule, 4 px in: as the browser writes it.
                valueHost.Children.Add(new GlyphText(options.Count == 0 ? "" : options[selected], AppFonts.Hand, 21, Controls.Ink, 0, 43, Width - 36)
                {
                    Margin = new Thickness(4, 0, 0, 0),
                });
            }
        }

        void Unroll()
        {
            var list = new StackPanel();
            for (int i = 0; i < options.Count; i++)
            {
                int index = i;
                var row = Controls.Id(new Grid { Width = Width, Height = 36, Background = Brushes.Transparent, Cursor = Cursors.Hand }, id + "-option-" + i);
                var text = new GlyphText(options[i], AppFonts.Hand, 21, i == selected ? Controls.Red : Controls.Ink, 0, 36, Width - 16) { Margin = new Thickness(8, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
                row.Children.Add(text);
                row.MouseEnter += delegate { text.Foreground = Controls.Red; };
                row.MouseLeave += delegate { text.Foreground = index == selected ? Controls.Red : Controls.Ink; };
                row.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    Overlay.CloseMenu();
                    if (index != selected)
                    {
                        SelectedIndex = index;
                        if (Changed != null) Changed();
                    }
                };
                list.Children.Add(row);
            }

            var paper = Css.Box(Width, 36 * options.Count, new CornerRadius(2),
                Css.Linear(180, Width, 36 * options.Count, Css.At("#fdfaf1", 0), Css.At("#f1e8d3", 100)),
                Shadow.Outer(0, 8, 18, Css.Rgba(0, 0, 0, 0.35)));
            paper.Children.Add(new Rectangle { Width = Width, Height = 36 * options.Count, Stroke = Css.Fill(Css.Hex("#d6cbae")), StrokeThickness = 1, IsHitTestVisible = false });
            paper.Children.Add(list);
            Overlay.OpenMenu(paper, this, new Point(0, Height));
        }
    }

    /// A clickable row whose text and icon turn red under the mouse (design class "ink").
    /// Items sit 8 px apart, each centred in the row's height.
    sealed class InkLink : StackPanel
    {
        readonly Color ink;
        readonly double rowHeight, gap;
        readonly List<GlyphText> texts = new List<GlyphText>();
        readonly List<Path> icons = new List<Path>();

        /// gap: the space between the items, 8 px unless the design says otherwise.
        public InkLink(Color ink, Color hoverInk, double rowHeight, double gap = 8)
        {
            this.ink = ink;
            this.rowHeight = rowHeight;
            this.gap = gap;
            Height = rowHeight;
            Orientation = Orientation.Horizontal;
            Background = Brushes.Transparent;   // makes the gaps between the items clickable too
            Cursor = Cursors.Hand;
            MouseEnter += delegate { Paint(hoverInk); };
            MouseLeave += delegate { Paint(this.ink); };
        }

        public void AddIcon(string data, double size, double strokeWidth = 2)
        {
            var icon = Icons.Make(data, size, Css.Fill(ink), strokeWidth);
            icons.Add(icon);
            Add(icon, size);
        }

        public void AddText(string text, FontSpec font, double size, double lineHeight, double letterSpacing,
                            double maxWidth = double.PositiveInfinity)
        {
            var element = new GlyphText(text, font, size, ink, letterSpacing, lineHeight, maxWidth);
            texts.Add(element);
            Add(element, element.LineHeight);
        }

        /// A label that keeps its own colour on hover.
        public void AddFixed(GlyphText label)
        {
            Add(label, label.LineHeight);
        }

        void Add(FrameworkElement element, double height)
        {
            // Centred by hand: on a half pixel the browser places the item higher, WPF lower.
            double top = Math.Floor((rowHeight - height) / 2);
            element.VerticalAlignment = VerticalAlignment.Top;
            element.Margin = new Thickness(Children.Count > 0 ? gap : 0, top, 0, 0);
            Children.Add(element);
        }

        void Paint(Color color)
        {
            foreach (GlyphText text in texts) text.Foreground = color;
            foreach (Path icon in icons) icon.Stroke = Css.Fill(color);
        }
    }
}
