using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using Rubrica.Model;
using static Rubrica.Ui.Lang;

namespace Rubrica.Ui
{
    /// The two-page form (design artboard "New / edit contact"): who they are on the left,
    /// how to reach them on the right. It edits a copy of the values; nothing touches the
    /// contact until SAVE, and CANCEL leaves no trace.
    sealed class EditSpread
    {
        /// SAVE passed the checks: the contact now holds what was typed. True = it is a new one.
        public event Action<Contact, bool> Saved;

        /// CANCEL was pressed (Esc asks for the same through RequestCancel).
        public event Action CancelRequested;

        public readonly FrameworkElement Left, Right;

        const double PageWidth = 300, PageHeight = 540, FieldGap = 14;

        readonly Book book;
        readonly Contact contact;
        readonly bool isNew;

        readonly PaperLine name, surname, role, email, teams;
        readonly PaperSelect category;
        readonly PaperCheck favorite;
        readonly PaperNotes notes;
        readonly List<PaperLine> numbers = new List<PaperLine>(), labels = new List<PaperLine>();
        readonly StackPanel phoneRows = new StackPanel();
        FrameworkElement addNumber;
        readonly string before;

        /// existing: the contact to edit, or null for a new one, which starts in startCategoryId.
        /// asDrawn: only for comparing with the artboard, which predates SURNAME - leaves that field out.
        public EditSpread(Book book, Contact existing, int startCategoryId, bool asDrawn = false)
        {
            this.book = book;
            isNew = existing == null;
            contact = existing ?? new Contact { CategoryId = startCategoryId };

            // ---- left page: who ----
            var left = new StackPanel { Width = PageWidth };
            left.Children.Add(Header(isNew ? T("NEW CONTACT") : T("EDIT CONTACT"), "1 / 2"));
            name = AddField(left, T("NAME"), "edit-name", contact.Name);
            surname = new PaperLine(PageWidth, "edit-surname") { Text = contact.Surname };
            if (!asDrawn) left.Children.Add(Group(T("SURNAME"), surname));
            role = AddField(left, T("ROLE / ORG"), "edit-role", contact.Role);

            var categoryNames = new List<string>();
            foreach (Category c in book.Categories) categoryNames.Add(c.Name);
            category = new PaperSelect(PageWidth, "edit-category", categoryNames, Math.Max(0, book.Categories.FindIndex(c => c.Id == contact.CategoryId)));
            left.Children.Add(Group(T("CATEGORY"), category));

            favorite = new PaperCheck("edit-favorite") { IsChecked = contact.Favorite };
            left.Children.Add(FavoriteRow());

            notes = new PaperNotes(PageWidth, "edit-notes") { Text = contact.Notes };
            left.Children.Add(Group(T("NOTES"), notes));
            Left = left;

            // ---- right page: how to reach them ----
            var top = new StackPanel { Width = PageWidth, VerticalAlignment = VerticalAlignment.Top };
            top.Children.Add(Header(T("CHANNELS"), "2 / 2"));
            top.Children.Add(phoneRows);
            int rows = Math.Max(isNew ? 2 : 1, contact.Phones.Count);   // the canvas opens a new contact on two numbers
            for (int i = 0; i < Math.Min(rows, Constants.MaxPhones); i++)
                AddPhoneRow(i < contact.Phones.Count ? contact.Phones[i] : null);
            addNumber = AddNumberLink();
            top.Children.Add(addNumber);
            ShowOrHideAddNumber();
            email = AddField(top, T("EMAIL"), "edit-email", contact.Email);
            teams = AddField(top, T("TEAMS (ACCOUNT EMAIL)"), "edit-teams", contact.Teams);

            Canvas save = Controls.Id(Controls.Stamp(T("SAVE"), StampStyle.RedSolid, -1.5, () => Save()), "edit-save");
            Canvas cancel = Controls.Id(Controls.Stamp(T("CANCEL"), StampStyle.DarkOutline, 1, RequestCancel), "edit-cancel");
            var stamps = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
            cancel.Margin = new Thickness(0, 0, 12, 0);
            stamps.Children.Add(cancel);
            stamps.Children.Add(save);

            // WPF: a Grid with no rows or columns overlays its children; the stamps sit at the bottom of the page.
            var right = new Grid { Width = PageWidth, Height = PageHeight };
            right.Children.Add(top);
            right.Children.Add(stamps);
            Right = right;

            before = Snapshot();
            name.Box.TextChanged += delegate { name.Missing = false; };
        }

        public void FocusFirst()
        {
            name.Box.Focus();
            name.Box.SelectAll();
        }

        /// Something was typed or changed since the form opened.
        public bool IsDirty
        {
            get { return Snapshot() != before; }
        }

        public void RequestCancel()
        {
            if (CancelRequested != null) CancelRequested();
        }

        /// NAME is the only required field. False = the form stays open, NAME marked in red.
        public bool Save()
        {
            // Clean.Line / Clean.Text: what a paste can bring (control characters, line ends in a
            // one-line field) never reaches the book.
            if (Clean.Line(name.Text).Length == 0)
            {
                name.Missing = true;
                name.Box.Focus();
                return false;
            }

            contact.Name = Clean.Line(name.Text);
            contact.Surname = Clean.Line(surname.Text);
            contact.Role = Clean.Line(role.Text);
            contact.CategoryId = book.Categories[category.SelectedIndex].Id;
            contact.Favorite = favorite.IsChecked;
            contact.Notes = Clean.Text(notes.Text, Constants.MaxNotesLength);
            contact.Email = Clean.Line(email.Text);
            contact.Teams = Clean.Line(teams.Text);

            // A hand-edited book may hold more numbers than the form has rows: they stay.
            List<Phone> beyondTheForm = contact.Phones.Count > Constants.MaxPhones
                ? contact.Phones.GetRange(Constants.MaxPhones, contact.Phones.Count - Constants.MaxPhones) : new List<Phone>();
            contact.Phones.Clear();
            for (int i = 0; i < numbers.Count; i++)
                if (Clean.Line(numbers[i].Text).Length > 0)   // a row left empty is simply not a number
                    contact.Phones.Add(new Phone(Clean.Line(numbers[i].Text), Clean.Line(labels[i].Text)));
            contact.Phones.AddRange(beyondTheForm);

            if (Saved != null) Saved(contact, isNew);
            return true;
        }

        const char Separator = (char)1;   // cannot be typed, so it cannot blur where one field ends

        /// Everything on the form as one string: comparing two of them tells whether anything changed.
        string Snapshot()
        {
            var all = new StringBuilder();
            foreach (PaperLine line in new[] { name, surname, role, email, teams }) all.Append(line.Text).Append(Separator);
            all.Append(category.SelectedIndex).Append(favorite.IsChecked).Append(notes.Text);
            for (int i = 0; i < numbers.Count; i++) all.Append(Separator).Append(numbers[i].Text).Append(Separator).Append(labels[i].Text);
            return all.ToString();
        }

        // ---- pieces --------------------------------------------------------------------

        /// Typed title, "1 / 2" on its baseline, a red rule: 23 px in all.
        static FrameworkElement Header(string title, string page)
        {
            var header = new Grid { Height = 23 };
            header.Children.Add(new GlyphText(title, AppFonts.Type, 15, Controls.Ink, 3) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top });
            header.Children.Add(new GlyphText(page, AppFonts.Type, 10, Controls.Pencil, 2)
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 4, 0, 0),   // baseline of the 15 px title (11) minus its own ascent (7)
            });
            header.Children.Add(new Rectangle { Height = 2, Fill = Css.Fill(Css.Hex("#c9564a")), VerticalAlignment = VerticalAlignment.Bottom });
            return header;
        }

        /// Caption, 2 px, the field; 14 px above, as between all the groups of the form.
        static FrameworkElement Group(string caption, FrameworkElement field)
        {
            var group = new StackPanel { Margin = new Thickness(0, FieldGap, 0, 0) };
            group.Children.Add(Controls.Caption(caption));
            field.Margin = new Thickness(0, 2, 0, 0);
            field.HorizontalAlignment = HorizontalAlignment.Left;
            group.Children.Add(field);
            return group;
        }

        static PaperLine AddField(Panel page, string caption, string id, string value)
        {
            var line = new PaperLine(PageWidth, id) { Text = value };
            page.Children.Add(Group(caption, line));
            return line;
        }

        FrameworkElement FavoriteRow()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Height = 44, Margin = new Thickness(0, FieldGap, 0, 0) };
            favorite.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(favorite);

            Path star = Icons.Make(Icons.Star, 16, Css.Fill(Css.Hex("#8a6a12")), 1.8, Css.Fill(Css.Hex("#d9a520")));
            star.Margin = new Thickness(10, 0, 0, 0);
            star.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(star);
            row.Children.Add(new GlyphText(T("FAVORITE / SPEED DIAL"), AppFonts.Type, 10, Controls.Pencil, 2)
            {
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            return row;
        }

        /// PHONE n (the wide field) and its LABEL (96 px), 12 px apart.
        void AddPhoneRow(Phone phone)
        {
            int n = numbers.Count + 1;
            var number = new PaperLine(PageWidth - 12 - 96, "edit-phone-" + n) { Text = phone == null ? "" : phone.Number };
            var label = new PaperLine(96, "edit-label-" + n) { Text = phone == null ? "" : phone.Label };
            numbers.Add(number);
            labels.Add(label);

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Group(T("PHONE {0}", n), number));
            FrameworkElement labelGroup = Group(T("LABEL"), label);
            labelGroup.Margin = new Thickness(12, FieldGap, 0, 0);
            row.Children.Add(labelGroup);
            phoneRows.Children.Add(row);
        }

        FrameworkElement AddNumberLink()
        {
            var link = Controls.Id(new InkLink(Controls.Red, Controls.Red, 32, 6) { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, FieldGap, 0, 0) }, "edit-add-number");
            link.AddIcon("M12 5v14M5 12h14", 16, 2.2);
            link.AddText(T("ADD NUMBER"), AppFonts.Type, 11, double.NaN, 2);
            link.MouseLeftButtonUp += delegate
            {
                AddPhoneRow(null);
                ShowOrHideAddNumber();
                numbers[numbers.Count - 1].Box.Focus();
            };
            return link;
        }

        void ShowOrHideAddNumber()
        {
            // WPF: Collapsed = not drawn and takes no room; the fields below move up.
            addNumber.Visibility = numbers.Count < Constants.MaxPhones ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
