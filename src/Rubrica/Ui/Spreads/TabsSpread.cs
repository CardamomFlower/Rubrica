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
    /// The page that manages the categories (design artboard "Manage tabs"): rename in place,
    /// reorder with the arrows, add one with a name and a colour, delete one - which asks, on
    /// the right page, where its contacts should go. It changes the book itself and says so
    /// through the two events; saving and redrawing are MainWindow's business.
    sealed class TabsSpread
    {
        /// A name changed: save, and redraw the tabs on the edge. The pages are already right.
        public event Action Renamed;

        /// Tabs were added, moved or deleted: save, redraw the tabs, and ask for the pages again.
        public event Action Changed;

        const double PageWidth = 300;

        readonly Book book;
        Category pendingDelete;         // the tab the slip on the right page is asking about
        int moveTo;                     // index into Others(pendingDelete)
        int colour;                     // index into Category.Palette, for the next tab
        string newName = "";
        readonly List<Action> pendingRenames = new List<Action>();   // one per name field of the page as last built
        bool focusNewName;              // a tab was just added: the caret goes back where the next name is typed

        public TabsSpread(Book book)
        {
            this.book = book;
            colour = Math.Min(book.Categories.Count, Category.Palette.Length - 1);
        }

        // ---- left page -----------------------------------------------------------------

        public FrameworkElement Left()
        {
            CommitRenames();   // whatever made the page be drawn again, a name being typed is not lost
            pendingRenames.Clear();
            var page = new StackPanel { Width = PageWidth };
            page.Children.Add(Controls.PageHeader(T("TABS"), T("MANAGE CATEGORIES")));

            StackPanel hint = GlyphText.Wrap(T("FAVORITES IS FIXED. RENAME IN PLACE, REORDER WITH THE ARROWS."), AppFonts.Type, 10, Controls.Pencil, 2, 10, PageWidth);
            hint.Margin = new Thickness(0, 12, 0, 0);
            page.Children.Add(hint);

            var rows = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            foreach (Category category in book.Categories) rows.Children.Add(Row(category));
            page.Children.Add(rows);

            page.Children.Add(AddSection());
            return page;
        }

        /// Swatch, name to write over, how many contacts, up, down, delete: 48 px, everything
        /// centred on it, and the dashed rule below makes 49.
        FrameworkElement Row(Category category)
        {
            var row = new Canvas { Width = PageWidth, Height = 49 };
            Css.Place(row, Controls.Swatch(Css.Hex(category.Colour), 22, 3), 0, 13);

            var name = new PaperLine(96, "tabs-name-" + category.Id, 20, false) { Text = category.Name };
            Action commit = delegate
            {
                if (name.Text.Length == 0 || name.Text == category.Name)
                {
                    name.Text = category.Name;   // a blank name is refused: the old one comes back
                    return;
                }
                book.RenameCategory(category, name.Text);
                if (Renamed != null) Renamed();
            };
            name.Box.LostKeyboardFocus += delegate { commit(); };
            pendingRenames.Add(commit);
            name.Box.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Enter) return;
                commit();
                e.Handled = true;
            };
            Css.Place(row, name, 30, 2);

            var count = new GlyphText(book.CountIn(category).ToString(), AppFonts.Type, 10, Controls.Pencil, 1);
            Css.Place(row, count, 134 + 22 - count.TextWidth, 19);

            int index = book.Categories.IndexOf(category);
            Css.Place(row, ArrowOrCross("M18 15l-6-6-6 6", Controls.Ink, "tabs-up-" + category.Id, index > 0, () => Move(category, -1)), 164, 2);
            Css.Place(row, ArrowOrCross("M6 9l6 6 6-6", Controls.Ink, "tabs-down-" + category.Id, index < book.Categories.Count - 1, () => Move(category, +1)), 212, 2);
            Css.Place(row, ArrowOrCross(Icons.Cross, Controls.Red, "tabs-delete-" + category.Id, book.Categories.Count > 1, () => AskDelete(category)), 260, 2);

            Css.Place(row, DashedRule(PageWidth), 0, 48);
            return row;
        }

        /// An action that cannot be taken is still drawn, fainter, and does not react.
        static FrameworkElement ArrowOrCross(string icon, Color color, string id, bool enabled, Action onClick)
        {
            Grid button = Controls.Id(Controls.IconButton(icon, color, onClick), id);
            if (!enabled)
            {
                button.Opacity = 0.25;
                button.IsHitTestVisible = false;
            }
            return button;
        }

        /// border-bottom: 1px dashed - 3 px on, 2 off, the gaps stretched a hair so that the
        /// line begins and ends on a dash, as a browser draws it.
        static FrameworkElement DashedRule(double width)
        {
            double dashes = Math.Floor((width + 2) / 5);
            double gap = (width - 3 * dashes) / (dashes - 1);
            return new Line
            {
                X1 = 0,
                X2 = width,
                Y1 = 0.5,
                Y2 = 0.5,
                Stroke = Css.Fill(Css.Rgba(27, 34, 51, 0.3)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, gap },
                UseLayoutRounding = false,
                IsHitTestVisible = false,
            };
        }

        /// A name still being typed when an arrow, the cross or ADD is clicked: the click does not
        /// take the focus away, so the rename would arrive after the page was drawn again, too late
        /// to show. It is taken first.
        public void CommitRenames()
        {
            foreach (Action commit in pendingRenames.ToArray()) commit();
        }

        void Move(Category category, int step)
        {
            CommitRenames();
            book.MoveCategory(category, step);
            if (Changed != null) Changed();
        }

        /// ADD A TAB: six colours to pick from, a name, the ADD stamp. Dimmed when the binder is full.
        FrameworkElement AddSection()
        {
            var section = new StackPanel { Margin = new Thickness(0, 12 + 8, 0, 0) };
            section.Children.Add(Controls.Caption(book.CanAddCategory ? T("ADD A TAB") : T("THE BINDER IS FULL: {0} TABS", Constants.MaxCategories)));

            var swatches = new StackPanel { Orientation = Orientation.Horizontal, Height = 44, Margin = new Thickness(0, 6, 0, 0) };
            for (int i = 0; i < Category.Palette.Length; i++)
            {
                int index = i;
                var swatch = Controls.Id(Controls.Swatch(Css.Hex(Category.Palette[i]), 26, 4), "tabs-swatch-" + i);
                // The canvas frames every swatch in translucent white; the chosen one is framed in ink.
                swatch.Children.Add(new Rectangle
                {
                    Width = 26,
                    Height = 26,
                    RadiusX = 3,
                    RadiusY = 3,
                    StrokeThickness = 2,
                    Stroke = Css.Fill(i == colour ? Controls.Ink : Css.Rgba(255, 255, 255, 0.6)),
                    IsHitTestVisible = false,
                });
                swatch.Margin = new Thickness(i == 0 ? 0 : 6, 9, 0, 9);
                swatch.Cursor = Cursors.Hand;
                swatch.MouseLeftButtonUp += delegate
                {
                    colour = index;
                    if (Changed != null) Changed();
                };
                swatches.Children.Add(swatch);
            }
            section.Children.Add(swatches);

            // The stamp needs the action and the action needs the field, whose width depends on
            // the stamp: so the stamp is given a call to a variable that is filled in just below.
            // (A stamp marks its click as handled, so a handler added to it afterwards never runs.)
            Action addTab = null;
            Canvas add = Controls.Id(Controls.Stamp(T("ADD"), StampStyle.RedSolid, -1.5, () => addTab(), 12), "tabs-add");
            var name = new PaperLine(PageWidth - 10 - add.Width, "tabs-new-name") { Text = newName };
            name.Box.TextChanged += delegate { newName = name.Box.Text; };
            addTab = delegate
            {
                CommitRenames();
                if (book.AddCategory(name.Text, Category.Palette[colour]) == null) return;
                newName = "";
                focusNewName = true;
                colour = Math.Min(book.Categories.Count, Category.Palette.Length - 1);
                if (Changed != null) Changed();
            };
            name.Box.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Enter) return;
                addTab();
                e.Handled = true;
            };

            if (focusNewName)
            {
                // The page was drawn again and the field that had the caret is gone: without this the
                // next name typed would go to the window, where "n" and "/" are shortcuts.
                focusNewName = false;
                name.Box.Dispatcher.BeginInvoke(new Action(() => name.Box.Focus()), System.Windows.Threading.DispatcherPriority.Input);
            }

            var line = new Canvas { Width = PageWidth, Height = add.Height, Margin = new Thickness(0, 6, 0, 0) };
            Css.Place(line, name, 0, 1);
            Css.Place(line, add, PageWidth - add.Width, 0);
            section.Children.Add(line);

            if (!book.CanAddCategory)
            {
                swatches.Opacity = 0.35;
                line.Opacity = 0.35;
                swatches.IsHitTestVisible = false;
                line.IsHitTestVisible = false;
            }
            return section;
        }

        // ---- right page: the question before a delete -------------------------------------------

        public FrameworkElement Right()
        {
            var page = new StackPanel { Width = PageWidth };
            if (pendingDelete == null || !book.Categories.Contains(pendingDelete)) return page;

            page.Children.Add(Controls.PageHeader(T("DELETE TAB"), ""));
            FrameworkElement slip = DeleteSlip(pendingDelete);
            slip.Margin = new Thickness(0, 14 + 24, 0, 0);
            page.Children.Add(slip);
            return page;
        }

        void AskDelete(Category category)
        {
            CommitRenames();
            pendingDelete = category;
            moveTo = 0;
            if (Changed != null) Changed();
        }

        List<Category> Others(Category category)
        {
            return book.Categories.FindAll(c => c != category);
        }

        FrameworkElement DeleteSlip(Category category)
        {
            const double pad = 23;   // 1 px border + the canvas's 22
            double inner = PageWidth - 2 * pad;
            int holds = book.CountIn(category);
            List<Category> others = Others(category);

            var lines = new StackPanel { Width = inner };
            lines.Children.Add(GlyphText.Wrap(T("DELETE TAB {0}?", category.Name.ToUpperInvariant()), AppFonts.Type, 14, Controls.Red, 2, 14, inner));
            string body = holds == 0 ? T("It is empty.") : N(holds, "It holds {0} contact. Move it to:", "It holds {0} contacts. Move them to:");
            StackPanel bodyLines = GlyphText.Wrap(body, AppFonts.Hand, 19, Controls.Ink, 0, 24, inner);
            bodyLines.Margin = new Thickness(0, 12, 0, 0);
            lines.Children.Add(bodyLines);

            if (holds > 0)
            {
                var names = new List<string>();
                foreach (Category other in others) names.Add(other.Name);
                var select = new PaperSelect(inner, "tabs-move-to", names, moveTo) { Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
                select.Changed += delegate { moveTo = select.SelectedIndex; };
                lines.Children.Add(select);
            }

            Canvas delete = Controls.Id(Controls.Stamp(T("DELETE TAB"), StampStyle.RedSolid, -1.5, delegate
            {
                book.DeleteCategory(category, others[Math.Min(moveTo, others.Count - 1)]);
                pendingDelete = null;
                if (Changed != null) Changed();
            }), "tabs-slip-delete");
            Canvas keep = Controls.Id(Controls.Stamp(T("KEEP"), StampStyle.DarkOutline, 1, delegate
            {
                pendingDelete = null;
                if (Changed != null) Changed();
            }), "tabs-slip-keep");
            var stamps = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12 + 6, 0, 0) };
            keep.Margin = new Thickness(0, 0, 12, 0);
            stamps.Children.Add(keep);
            stamps.Children.Add(delete);
            lines.Children.Add(stamps);

            // The paper is as tall as what is written on it: measure that first.
            lines.Measure(new Size(inner, double.PositiveInfinity));
            double height = pad + lines.DesiredSize.Height + 18 + 1;

            var slip = Controls.Id(Css.Box(PageWidth, height, new CornerRadius(0),
                Css.Linear(180, PageWidth, height, Css.At("#fdfaf1", 0), Css.At("#f1e8d3", 100)),
                Shadow.Outer(0, 8, 18, Css.Rgba(0, 0, 0, 0.28))), "tabs-slip");
            slip.Children.Add(new Rectangle { Width = PageWidth, Height = height, Stroke = Css.Fill(Css.Hex("#d6cbae")), StrokeThickness = 1, IsHitTestVisible = false });
            Css.Place(slip, lines, pad, pad);
            slip.RenderTransformOrigin = new Point(0.5, 0.5);
            slip.RenderTransform = new RotateTransform(1.2);
            return slip;
        }
    }
}
