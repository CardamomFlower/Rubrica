using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Rubrica.Core;
using Rubrica.Model;
using static Rubrica.Ui.Lang;

namespace Rubrica.Ui
{
    /// The window: scales the 960 x 720 scene to whatever size it is given, remembers where
    /// it was, and decides which place of the book is on show.
    /// Every change to the book goes through here, and is saved at once.
    sealed class MainWindow : Window
    {
        enum Place { Cover, List, Search, Edit, Tabs, Setup }

        /// The "tab" of the Recent page, which has none on the right: it is the RECENT tab on the left.
        const int RecentTab = -1;

        readonly Book book;
        readonly BookStore store;
        readonly AppState state;
        readonly bool saves;

        /// Puts a text on the clipboard. Replaced by the tests, which must leave the real one alone.
        public static Action<string> ToClipboard = Clipboard.SetText;

        /// Ask Windows for a file to read (title, filter) or a place to write (title, filter,
        /// suggested name, folder to start in or null). null = the operator cancelled. Replaced by
        /// the tests, which cannot press the buttons of a Windows dialog.
        public static Func<string, string, string> PickFileToOpen = AskFileToOpen;
        public static Func<string, string, string, string, string> PickFileToSave = AskFileToSave;

        // WPF: a Viewbox scales its one child uniformly to the room it gets - the whole scene
        // grows and shrinks as one piece, and stays vector-sharp.
        readonly Grid stage = new Grid { Width = Constants.DesignWidth, Height = Constants.DesignHeight };
        readonly Viewbox scaler = new Viewbox { Stretch = Stretch.Uniform };
        readonly Canvas overlay = new Canvas { Width = Constants.DesignWidth, Height = Constants.DesignHeight };

        CoverView cover;
        BinderView binder;

        // Where we are.
        Place place = Place.Cover;
        int currentTab;                 // a category id, or BinderView.FavoritesTab
        int spread;
        ContactListSpread list;
        Contact open;                   // whose card lies on the right page; null = none
        EditSpread edit;
        Contact editing;                // null while the form is for a new contact
        TabsSpread tabs;
        SearchSpread search;
        int searchCameFrom;             // the tab to go back to when the search is left
        Contact lastDeleted;            // what UNDO would put back
        SetupSpread setup;
        ImportResult lastImport;        // what UNDO would take out again
        Place editCameFrom;             // where CANCEL goes back to
        DispatcherTimer stateDue;       // a look-up is waiting to be written to state.xml (see Touch)
        bool unsaved;                   // the last save failed: the screen is ahead of the disk
        bool closingAgreed;             // the operator said yes to losing what the form holds
        bool swallowNextMouseUp;        // the second click of a double-click
        bool pressedHere;               // the button went down in this window, not on its title bar or in a dialog
        WindowState lastShownState = WindowState.Normal;   // normal or maximised - never "minimised"

        /// saves: false for a snapshot, which may walk through deletes and edits to reach the
        /// place it has to draw, and must leave the book on disk as it found it.
        public MainWindow(Book book, BookStore store, AppState state, bool saves = true)
        {
            this.book = book;
            this.store = store;
            this.state = state;
            this.saves = saves;

            Title = WindowTitle();
            Background = BinderView.Desk();
            stage.Children.Add(new Grid());     // slot 0: the view on show
            stage.Children.Add(overlay);        // slot 1: slips, strips and menus, above everything
            Overlay.Attach(overlay);
            scaler.Child = stage;
            Content = scaler;

            ShowCover();
            PlaceOnScreen();

            PreviewKeyDown += OnKey;
            PreviewTextInput += OnTextInput;
            PreviewMouseLeftButtonDown += (sender, e) =>
            {
                pressedHere = true;
                swallowNextMouseUp = IsSecondClickToIgnore(e.ClickCount, e.OriginalSource as DependencyObject);
            };
            // Everything acts when the button comes up. A release whose press happened elsewhere - the
            // second click of a double-click on the title bar, which maximises the window, or in a
            // file dialog that has just closed - lands on whatever is under the pointer now.
            PreviewMouseLeftButtonUp += (sender, e) =>
            {
                if (swallowNextMouseUp || !pressedHere) e.Handled = true;   // WPF: a handled event reaches no "+=" handler further down
                swallowNextMouseUp = false;
                pressedHere = false;
            };
            StateChanged += delegate { if (WindowState != WindowState.Minimized) lastShownState = WindowState; };
            Closing += BeforeClosing;

            // The binder takes a moment to build (textures, shadows): do it once the cover is
            // on screen, so neither the first frame nor the first click waits for it.
            Dispatcher.BeginInvoke(new Action(delegate { EnsureBinder(); }), DispatcherPriority.ApplicationIdle);
        }

        /// Everything in Rubrica acts when the mouse button comes up. A double-click brings two
        /// of those, and by the second one the first has often changed what lies under the
        /// pointer: the book opened by a double-click on its cover would at once have a star
        /// toggled or a number copied. So the second click of a double-click does nothing -
        /// except on the page corners, which are meant to be clicked in a row, and in a text box,
        /// which selects a word with it. (Not the arrows of the Tabs page: the rows are drawn again
        /// after a move, and the second click would move the neighbour back.)
        internal static bool IsSecondClickToIgnore(int clickCount, DependencyObject on)
        {
            if (clickCount < 2) return false;
            for (DependencyObject e = on; e != null; e = e is Visual ? VisualTreeHelper.GetParent(e) : LogicalTreeHelper.GetParent(e))
            {
                if (e is System.Windows.Controls.Primitives.TextBoxBase) return false;
                string id = System.Windows.Automation.AutomationProperties.GetAutomationId(e);
                if (id == "page-back" || id == "page-forward") return false;
            }
            return true;
        }

        /// Every way out of a place that is not the form's own SAVE or CANCEL comes through
        /// here: a form with something typed in it is not left without asking (section 4.4).
        void Leaving(Action go)
        {
            Overlay.CloseMenu();
            if (place == Place.Edit && edit != null && edit.IsDirty) AskDiscard(go);
            else go();
        }

        void AskDiscard(Action thenGo)
        {
            Canvas slip = PaperSlip.Question(T("DISCARD WHAT YOU WROTE?"), T("Nothing of it has been saved."), T("KEEP WRITING"), T("DISCARD"),
                Overlay.CloseSlip,
                delegate
                {
                    Overlay.CloseSlip();
                    thenGo();
                });
            Overlay.ShowSlip(slip, 560, 270);
        }

        /// The window is closing. Two things may still need a word: a form that was being filled
        /// in, and a save that failed earlier and is tried once more.
        internal void BeforeClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (place == Place.Edit && edit != null && edit.IsDirty && !closingAgreed)
            {
                e.Cancel = true;
                if (!Overlay.SlipIsOpen)
                    AskDiscard(delegate
                    {
                        closingAgreed = true;
                        Close();
                        closingAgreed = false;   // the close may still be called off (a save that fails): the next one asks again
                    });
                return;
            }
            if (unsaved)
            {
                Changed();
                if (unsaved && MessageBox.Show(this, T("The book still cannot be saved. Close anyway, and lose what was changed since the last good save?"),
                                               "Rubrica", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }
            RememberBounds();
            WriteState();
        }

        /// Changes place, and lets go of what the place being left built: the PC stays up for
        /// months, and a form, the Tabs page or Setup's two leather textures have no business
        /// staying in memory. A tab name still being typed on the Tabs page is taken first.
        void Enter(Place next)
        {
            if (place == Place.Tabs && next != Place.Tabs && tabs != null) tabs.CommitRenames();
            place = next;
            if (next != Place.Edit) edit = null;
            if (next != Place.Tabs) tabs = null;
            if (next != Place.Setup) setup = null;
            if (next != Place.Search && next != Place.Edit) search = null;   // the form goes back to the search
        }

        /// A star was clicked in a list: saved, and the open card shows it too.
        void StarToggled(Contact contact)
        {
            Changed();
            if (open == contact) ShowSpread();
        }

        // ---- cover and lists -----------------------------------------------------------

        public void ShowCover()
        {
            if (cover == null)
            {
                cover = new CoverView(book);
                cover.Opened += delegate { OpenAt(book.Categories[0].Id); };
                cover.TabClicked += OpenAt;
            }
            Enter(Place.Cover);
            Show(cover);
        }

        /// Opens the book at a tab: a category id, BinderView.FavoritesTab, or RecentTab.
        public void OpenAt(int tab)
        {
            OpenAt(tab, 0);
        }

        public void OpenAt(int tab, int atSpread)
        {
            EnsureBinder();
            Enter(Place.List);
            open = null;

            if (tab == BinderView.FavoritesTab)
            {
                currentTab = tab;
                list = new ContactListSpread(book, T("FAVORITES"), T("SPEED DIAL"), book.Favorites(),
                    () => ContactListSpread.EmptyState(T("No favorites yet."), T("STAR A CONTACT TO KEEP IT ONE CLICK AWAY.")),
                    StarToggled);
            }
            else if (tab == RecentTab)
            {
                currentTab = tab;
                var now = DateTime.Now;
                var recent = new List<Contact>();
                var lines = new Dictionary<int, string>();
                foreach (RecentEntry entry in state.Recents)
                {
                    Contact contact = book.Contacts.Find(c => c.Id == entry.ContactId);
                    if (contact == null) continue;
                    recent.Add(contact);
                    lines[contact.Id] = RecentLine(entry.Action, entry.At, now);
                }
                list = new ContactListSpread(book, T("RECENT"), T("LAST USED"), recent,
                    () => ContactListSpread.EmptyState(T("Nobody looked up yet."), T("OPEN A CARD, COPY A NUMBER OR START A CHAT, AND THEY WILL BE LISTED HERE.")),
                    StarToggled)
                {
                    RedLine = contact => lines[contact.Id],
                };
            }
            else
            {
                Category category = book.Categories.Find(c => c.Id == tab) ?? book.Categories[0];
                currentTab = category.Id;
                list = new ContactListSpread(book, category.Name, null, book.InCategory(category.Id),
                    () => ContactListSpread.EmptyState(T("Nothing written here yet."), T("ADD A CONTACT, OR IMPORT YOUR LIST FROM SETUP."),
                        Controls.Id(Controls.Stamp(T("NEW CONTACT"), StampStyle.RedOutline, -1.5, () => OpenEdit(null)), "empty-new"),
                        Controls.Id(Controls.Stamp(T("IMPORT CSV"), StampStyle.DarkOutline, 1, Import), "empty-import")),
                    StarToggled);
            }
            Wire(list);

            spread = Math.Max(0, Math.Min(atSpread, list.SpreadCount - 1));
            binder.SetTabs(book.Categories, currentTab);
            binder.SetTopTab(null);
            binder.SetLeftTab(currentTab == RecentTab ? "recent" : null);
            ShowSpread();
            Show(binder);
        }

        /// What every list does when its entries are clicked.
        void Wire(ContactListSpread entries)
        {
            entries.NameClicked = contact => { if (open == contact) CloseCard(); else OpenCard(contact); };
            entries.ChannelClicked = Copy;
            entries.ChatClicked = contact => OpenChat(contact, contact.Teams);
        }

        void ShowSpread()
        {
            list.SelectedId = open == null ? 0 : open.Id;
            // With a card open the right page is left blank under it, as the design has it.
            FrameworkElement right = open == null ? list.Page(2 * spread + 1) : null;
            binder.SetPages(list.Page(2 * spread), right, 2 * spread + 1, spread > 0, spread < list.SpreadCount - 1);
            if (open != null) binder.LayOnRightPage(Card(open), 20, 40);
        }

        // ---- the contact's card ----------------------------------------------------------

        void OpenCard(Contact contact)
        {
            open = contact;
            Touch(contact, "view");
            ShowSpread();
        }

        void CloseCard()
        {
            open = null;
            ShowSpread();
        }

        ContactCard Card(Contact contact)
        {
            var card = new ContactCard(book, contact);
            card.EditRequested += delegate { OpenEdit(contact); };
            card.DeleteRequested += delegate { AskDelete(contact); };
            card.CloseRequested += CloseCard;
            card.FavoriteToggled += delegate
            {
                Changed();
                ShowSpread();   // the star beside the name on the left page follows
            };
            card.CopyRequested += (what, text) => Copy(contact, what, text);
            card.ChatRequested += account => OpenChat(contact, account);
            return card;
        }

        /// what: "NUMBER" or "EMAIL" - what was copied, for the strip that says so.
        void Copy(Contact contact, string what, string text)
        {
            try
            {
                ToClipboard(text);
                Touch(contact, "copy");
                Overlay.ShowStrip(PaperSlip.Strip(what == "EMAIL" ? T("EMAIL COPIED") : T("NUMBER COPIED"), null, null), 300, 646, 3);
            }
            catch (Exception)
            {
                // Another program is holding the clipboard: say so instead of pretending.
                Overlay.ShowStrip(PaperSlip.Strip(T("THE CLIPBOARD IS BUSY - TRY AGAIN"), null, null), 300, 646, 3);
            }
        }

        void OpenChat(Contact contact, string account)
        {
            if (TeamsLink.OpenChat(account))
                Touch(contact, "chat");
            else
                Overlay.ShowStrip(PaperSlip.Strip(T("TEAMS COULD NOT BE OPENED"), null, null), 300, 646, 4);
        }

        /// Something was done with a contact: the Recent page will show it first.
        void Touch(Contact contact, string action)
        {
            state.Touch(contact.Id, action, DateTime.Now);
            if (!saves || stateDue != null) return;

            // state.xml changes at every look-up and holds nothing that cannot be lost - where the
            // window was, and who was looked up lately. Writing it for each one would keep touching
            // the disk of a PC that stays on for months, so a burst of look-ups is written once, a
            // few seconds later, and closing the window writes what is still waiting.
            // WPF: a DispatcherTimer ticks on the window's own thread. This one stops at its first
            // tick - nothing of Rubrica's is left ticking while the operator does nothing.
            stateDue = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Constants.StateSaveSeconds) };
            stateDue.Tick += delegate { WriteState(); };
            stateDue.Start();
        }

        /// Writes state.xml when a look-up is waiting to be written, and stops the timer.
        internal void WriteState()
        {
            if (stateDue == null) return;
            stateDue.Stop();
            stateDue = null;
            state.Save();
        }

        // ---- search ----------------------------------------------------------------------

        /// The SEARCH tab or the "/" key: the field on the left page, the results after it.
        void OpenSearch()
        {
            EnsureBinder();
            if (place != Place.Search) searchCameFrom = place == Place.Cover ? int.MinValue : currentTab;
            Enter(Place.Search);
            open = null;
            spread = 0;

            search = new SearchSpread(book)
            {
                NewContact = () => OpenEdit(null),
                FavoriteToggled = StarToggled,
                NameClicked = contact => { if (open == contact) CloseCard(); else OpenCard(contact); },
                ChannelClicked = Copy,
                ChatClicked = contact => OpenChat(contact, contact.Teams),
            };
            search.ResultsChanged += delegate
            {
                if (place != Place.Search) return;
                list = search.List;
                spread = 0;
                open = null;
                ShowSpread();
            };
            list = search.List;

            binder.SetTabs(book.Categories, -1);
            binder.SetTopTab("search");
            binder.SetLeftTab(null);
            ShowSpread();
            Show(binder);
            Dispatcher.BeginInvoke(new Action(search.Focus), DispatcherPriority.Input);
        }

        /// The search again, as it was left, looking afresh in case the book changed meanwhile.
        void BackToSearch()
        {
            Enter(Place.Search);
            open = null;
            binder.SetTabs(book.Categories, -1);
            binder.SetTopTab("search");
            binder.SetLeftTab(null);
            search.Refresh();   // raises ResultsChanged, which puts the first spread on the pages
            Show(binder);
            Dispatcher.BeginInvoke(new Action(search.Focus), DispatcherPriority.Input);
        }

        void LeaveSearch()
        {
            if (searchCameFrom == int.MinValue) ShowCover();
            else OpenAt(searchCameFrom);
        }

        // ---- delete and undo -------------------------------------------------------------

        void AskDelete(Contact contact)
        {
            Canvas slip = PaperSlip.Question(T("REMOVE {0} FROM THE BOOK?", book.DisplayName(contact).ToUpperInvariant()),
                T("The page is torn out. UNDO stays available right after."), T("KEEP"), T("YES, REMOVE"),
                Overlay.CloseSlip,
                delegate
                {
                    Overlay.CloseSlip();
                    Delete(contact);
                });
            Overlay.ShowSlip(slip, 560, 270);
        }

        void Delete(Contact contact)
        {
            book.Contacts.Remove(contact);
            Changed();
            lastDeleted = contact;
            if (place == Place.Search) BackToSearch();
            else OpenAt(currentTab, spread);

            Overlay.ShowStrip(PaperSlip.Strip(T("{0} REMOVED FROM THE BOOK", book.DisplayName(contact).ToUpperInvariant()), T("UNDO"), Undo), 300, 646, Constants.UndoSeconds);
        }

        void Undo()
        {
            Contact back = lastDeleted;
            if (back == null) return;
            if (book.CategoryOf(back) == null) back.CategoryId = book.Categories[0].Id;   // its tab went meanwhile
            book.Contacts.Add(back);
            Changed();
            if (place == Place.List) OpenAt(currentTab, spread);
            else if (place == Place.Search) BackToSearch();
        }

        // ---- the form --------------------------------------------------------------------

        /// contact: whom to edit, or null for a new one.
        void OpenEdit(Contact contact)
        {
            EnsureBinder();
            Overlay.CloseStrip();
            if (place != Place.Edit) editCameFrom = place;
            Enter(Place.Edit);
            editing = contact;
            int startCategory = currentTab != BinderView.FavoritesTab && book.Categories.Exists(c => c.Id == currentTab) ? currentTab : book.Categories[0].Id;
            edit = new EditSpread(book, contact, startCategory);
            edit.Saved += OnSaved;
            edit.CancelRequested += CancelEdit;
            ShowEdit();
            // Once the fields are on screen, put the caret in NAME.
            Dispatcher.BeginInvoke(new Action(edit.FocusFirst), DispatcherPriority.Input);
        }

        void ShowEdit()
        {
            binder.SetTabs(book.Categories, currentTab);
            binder.SetTopTab(editing == null ? "new" : null);
            binder.SetLeftTab(null);
            binder.SetPages(edit.Left, edit.Right, 1, false, false);
            Show(binder);
        }

        void OnSaved(Contact contact, bool isNew)
        {
            if (isNew)
            {
                contact.Id = book.NewId();
                book.Contacts.Add(contact);
            }
            Changed();

            // Back to the book, open at the contact just written.
            OpenAt(contact.CategoryId);
            spread = list.SpreadOf(contact);
            OpenCard(contact);
        }

        void CancelEdit()
        {
            if (edit.IsDirty) AskDiscard(LeaveEdit);
            else LeaveEdit();
        }

        /// Back where the form was opened from: the search with what was typed in it, or the
        /// list - on the contact's card, if it was one being edited.
        void LeaveEdit()
        {
            Contact backTo = editing;
            if (editCameFrom == Place.Search && search != null) BackToSearch();
            else OpenAt(currentTab, spread);
            if (backTo != null && list.Holds(backTo))
            {
                spread = list.SpreadOf(backTo);
                OpenCard(backTo);
            }
        }

        // ---- the Tabs page -----------------------------------------------------------------

        void OpenTabs()
        {
            EnsureBinder();
            Enter(Place.Tabs);
            open = null;
            tabs = new TabsSpread(book);
            tabs.Renamed += delegate
            {
                Changed();
                cover = null;   // its tabs carry the old names: rebuilt the next time the book is closed
                // A rename may be committed by leaving the page: the tab that sticks out is then the new place's.
                binder.SetTabs(book.Categories, place == Place.List ? currentTab : -1);
            };
            tabs.Changed += delegate
            {
                Changed();
                cover = null;
                ShowTabs();
            };
            ShowTabs();
            Show(binder);
        }

        void ShowTabs()
        {
            binder.SetTabs(book.Categories, -1);
            binder.SetTopTab(null);
            binder.SetLeftTab("tabs");
            binder.SetPages(tabs.Left(), tabs.Right(), 1, false, false);
        }

        // ---- Setup: import, export, backup -------------------------------------------------

        void OpenSetup()
        {
            EnsureBinder();
            Enter(Place.Setup);
            open = null;
            setup = new SetupSpread(book);
            setup.ImportRequested += Import;
            setup.ExportRequested += Export;
            setup.BackupRequested += Backup;
            setup.SortChanged += delegate
            {
                Changed();
                ShowSetup();
            };
            setup.LanguageChanged += SwitchLanguage;
            ShowSetup();
            Show(binder);
        }

        /// Everything is drawn again in the other language; state.xml remembers it for next time.
        void SwitchLanguage(Language language)
        {
            // A brand-new book's one tab was named in the language of the first start: while it is
            // still untouched, it follows.
            string firstName = T("CONTACTS");
            bool untouched = book.Contacts.Count == 0 && book.Categories.Count == 1 && book.Categories[0].Name == firstName;
            Lang.Current = language;
            if (untouched)
            {
                book.RenameCategory(book.Categories[0], T("CONTACTS"));
                Changed();
            }
            state.Language = Lang.Code(language);
            if (saves) state.Save();
            cover = null;   // the closed book is built once: built again, in the new words, when next shown
            Title = WindowTitle();
            Overlay.CloseStrip();
            OpenSetup();
        }

        string WindowTitle()
        {
            return state.Software ? T("Rubrica - software rendering") : "Rubrica";
        }

        void ShowSetup()
        {
            binder.SetTabs(book.Categories, -1);
            binder.SetTopTab(null);
            binder.SetLeftTab("setup");
            binder.SetInsideCover(setup.InsideCover(), setup.Right(), true);
        }

        /// IMPORT CSV: pick a file, read it, say on a slip what it would add, and add it only on
        /// a yes. Nothing already in the book is replaced.
        void Import()
        {
            string path = PickFileToOpen(T("Import contacts"), T("Lists and CSV files (*.csv;*.txt)|*.csv;*.txt|All files (*.*)|*.*"));
            if (path == null) return;

            ImportPlan plan;
            try
            {
                plan = Csv.Plan(book, Csv.ReadText(path));
            }
            catch (Exception error)
            {
                Tell(T("THE FILE COULD NOT BE READ"), Path.GetFileName(path) + ". " + Reason(error));
                return;
            }

            string file = Path.GetFileName(path);
            if (plan.Rows.Count == 0)
            {
                Tell(T("NOTHING TO IMPORT"), NothingToImport(plan, file));
                return;
            }

            // A plain list names no tab: the slip asks which one it goes under.
            PaperSelect under = null;
            if (plan.PlainList)
            {
                var names = new List<string>();
                foreach (Category category in book.Categories) names.Add(category.Name);
                int preset = Math.Max(0, book.Categories.FindIndex(c => c.Id == currentTab));
                under = new PaperSelect(PaperSlip.Width - 50, "import-under", names, preset);
            }

            string title = N(plan.Rows.Count, "IMPORT {0} CONTACT?", "IMPORT {0} CONTACTS?");
            Canvas slip = PaperSlip.Sheet(title, ImportSummary(plan, file), under, T("CANCEL"), T("IMPORT"), StampStyle.RedSolid,
                Overlay.CloseSlip,
                delegate
                {
                    Overlay.CloseSlip();
                    Category target = under == null ? null : book.Categories[Math.Min(under.SelectedIndex, book.Categories.Count - 1)];
                    ImportResult result = Csv.Apply(book, plan, target);
                    Changed();
                    lastImport = result;
                    cover = null;   // an import may have made tabs: the cover is rebuilt when next shown
                    OpenAt(result.Added[0].CategoryId);   // the book opens where they went, to be seen
                    Overlay.ShowStrip(PaperSlip.Strip(N(result.Added.Count, "{0} CONTACT ADDED", "{0} CONTACTS ADDED"), T("UNDO"), UndoImport), 300, 646, Constants.UndoSeconds);
                });
            Overlay.ShowSlip(slip, Math.Floor((Constants.DesignWidth - slip.Width) / 2), Math.Floor((Constants.DesignHeight - slip.Height) / 2));
        }

        /// The handwritten lines of the import slip: where from, what is left out and why,
        /// which tabs appear.
        string ImportSummary(ImportPlan plan, string file)
        {
            string text = plan.PlainList ? T("A plain list of names and numbers: {0}.", file) : T("From {0}.", file);
            if (plan.AlreadyThere > 0)
                text += " " + N(plan.AlreadyThere, "{0} is already in the book and is left alone.", "{0} are already in the book and are left alone.");
            if (plan.Unusable > 0)
                text += " " + (plan.PlainList ? N(plan.Unusable, "{0} line has no number: left out.", "{0} lines have no number: left out.")
                                              : N(plan.Unusable, "{0} row has no name: left out.", "{0} rows have no name: left out."));
            if (plan.NewCategories.Count > 0)
                text += " " + T(plan.NewCategories.Count == 1 ? "New tab: {0}." : "New tabs: {0}.", string.Join(", ", plan.NewCategories).ToUpperInvariant());
            if (plan.ToFirstCategory > 0)
                text += " " + T(plan.ToFirstCategory == 1 ? "The binder is full: {0} goes under {1}." : "The binder is full: {0} go under {1}.",
                                plan.ToFirstCategory, book.Categories[0].Name.ToUpperInvariant());
            if (plan.PlainList) text += " " + T("Put them under:");
            return text;
        }

        static string NothingToImport(ImportPlan plan, string file)
        {
            if (plan.AlreadyThere > 0 && plan.Unusable == 0)
                return T("Everybody in {0} is already in the book.", file);
            if (plan.AlreadyThere > 0)
                return T(plan.PlainList ? "{0}: {1} already in the book, {2} without a number." : "{0}: {1} already in the book, {2} without a name.",
                         file, plan.AlreadyThere, plan.Unusable);
            return T("No contacts were found in {0}. A list has one name and number per line; a CSV starts with a row of column names.", file);
        }

        void UndoImport()
        {
            ImportResult back = lastImport;
            if (back == null) return;
            Csv.Undo(book, back);
            Changed();
            cover = null;
            OpenSetup();
        }

        /// EXPORT CSV: the whole book, in the format IMPORT reads.
        void Export()
        {
            string path = PickFileToSave(T("Export contacts"), T("CSV file (*.csv)|*.csv"), T("Rubrica contacts {0}.csv", DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)), null);
            if (path == null) return;
            try
            {
                Csv.Export(book, path);
            }
            catch (Exception error)
            {
                Tell(T("THE FILE COULD NOT BE WRITTEN"), Path.GetFileName(path) + ". " + Reason(error));
                return;
            }
            Overlay.ShowStrip(PaperSlip.Strip(N(book.Contacts.Count, "{0} CONTACT EXPORTED", "{0} CONTACTS EXPORTED"), null, null), 300, 646, 4);
        }

        /// BACKUP NOW: a copy of the book, in the folder the last backup went to.
        void Backup()
        {
            string folder = state.BackupFolder.Length > 0 && Directory.Exists(state.BackupFolder) ? state.BackupFolder : null;
            string path = PickFileToSave(T("Backup the book"), T("Rubrica book (*.xml)|*.xml"), T("Rubrica backup {0}.xml", DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)), folder);
            if (path == null) return;
            try
            {
                store.Backup(book, path);
            }
            catch (Exception error)
            {
                Tell(T("THE BACKUP COULD NOT BE WRITTEN"), Path.GetFileName(path) + ". " + Reason(error));
                return;
            }
            state.BackupFolder = Path.GetDirectoryName(Path.GetFullPath(path));
            if (saves) state.Save();
            Overlay.ShowStrip(PaperSlip.Strip(T("BACKUP SAVED"), null, null), 300, 646, 4);
        }

        /// Why a file could not be read or written, in a few words. Windows' own message is in
        /// the language of the PC and carries the whole path; Rubrica's own are passed on as they are.
        static string Reason(Exception error)
        {
            // Rubrica's own messages (Core) are fixed English sentences, which the Italian table holds.
            if (error is InvalidDataException || error is InvalidOperationException) return T(error.Message);
            if (error is FileNotFoundException || error is DirectoryNotFoundException) return T("It is not there any more.");
            if (error is UnauthorizedAccessException) return T("Windows does not allow it: the file or its folder is protected.");
            if (error is IOException) return T("The disk refused: the file may be open in another program, or the disk may be full.");
            return T("Unexpected: {0}.", error.GetType().Name);
        }

        /// A slip that says something and waits for OK.
        void Tell(string title, string body)
        {
            Canvas slip = PaperSlip.Notice(title, body, Overlay.CloseSlip);
            Overlay.ShowSlip(slip, Math.Floor((Constants.DesignWidth - slip.Width) / 2), Math.Floor((Constants.DesignHeight - slip.Height) / 2));
        }

        // WPF has its own open and save dialogs (Microsoft.Win32): the ordinary Windows ones.
        static string AskFileToOpen(string title, string filter)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        static string AskFileToSave(string title, string filter, string suggestedName, string folder)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Title = title, Filter = filter, FileName = suggestedName, AddExtension = true, OverwritePrompt = true };
            if (folder != null) dialog.InitialDirectory = folder;
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        // ---- plumbing --------------------------------------------------------------------

        void EnsureBinder()
        {
            if (binder != null) return;
            binder = new BinderView(Css.Hex(book.Cover));
            binder.TabClicked += tab => Leaving(() => OpenAt(tab));
            binder.PageTurnRequested += delegate(int step)
            {
                // The inside of the cover comes before everything: its corner turns to the first tab.
                if (place == Place.Setup)
                {
                    if (step > 0) OpenAt(book.Categories[0].Id);
                    return;
                }
                if (place != Place.List && place != Place.Search) return;
                spread = Math.Max(0, Math.Min(spread + step, list.SpreadCount - 1));
                open = null;
                ShowSpread();
            };
            binder.TopTabClicked += key => Leaving(delegate
            {
                if (key == "new") OpenEdit(null);
                else if (key == "search") OpenSearch();
            });
            binder.LeftTabClicked += key => Leaving(delegate
            {
                if (key == "tabs") OpenTabs();
                else if (key == "recent") OpenAt(RecentTab);
                else if (key == "setup") OpenSetup();
            });
        }

        void Show(FrameworkElement view)
        {
            if (stage.Children[0] == view) return;
            stage.Children.RemoveAt(0);
            stage.Children.Insert(0, view);
        }

        /// The book changed: whatever UNDO was offering is off the table, and the book is saved
        /// at once. A failed save must not take the program down: the operator is told, and the
        /// book on screen stays as it is.
        void Changed()
        {
            lastDeleted = null;
            lastImport = null;
            Overlay.CloseStrip();
            if (!saves) return;
            try
            {
                store.Save(book);
                unsaved = false;
            }
            catch (Exception error)
            {
                // Remembered: the next change saves everything anyway, and closing tries once more.
                unsaved = true;
                MessageBox.Show(this, T("The book could not be saved.\n\n{0}", Reason(error)), "Rubrica", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                // One step back, innermost thing first.
                if (Overlay.MenuIsOpen) Overlay.CloseMenu();
                else if (Overlay.SlipIsOpen) Overlay.CloseSlip();
                else if (place == Place.Edit) edit.RequestCancel();
                else if (place == Place.Tabs || place == Place.Setup) OpenAt(currentTab, spread);
                else if ((place == Place.List || place == Place.Search) && open != null) CloseCard();
                else if (place == Place.Search) LeaveSearch();
                else if (place == Place.List) ShowCover();
                else return;
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && place == Place.Edit && !Overlay.SlipIsOpen && !Overlay.MenuIsOpen)
            {
                // Enter saves, except in the notes, where it starts a new line.
                var box = Keyboard.FocusedElement as TextBox;
                if (box != null && box.AcceptsReturn) return;
                edit.Save();
                e.Handled = true;
            }
        }

        /// The letter shortcuts, by the character typed rather than the key, so that they hold
        /// on any keyboard layout ("/" is Shift+7 on an Italian one). Ignored while writing.
        void OnTextInput(object sender, TextCompositionEventArgs e)
        {
            if (Keyboard.FocusedElement is TextBox || Overlay.SlipIsOpen || Overlay.MenuIsOpen || place == Place.Edit) return;
            if (e.Text == "/")
            {
                OpenSearch();
                e.Handled = true;
            }
            else if (e.Text == "n" || e.Text == "N")
            {
                if (place == Place.Cover) return;
                OpenEdit(null);
                e.Handled = true;
            }
        }

        // ---- the window on the screen ---------------------------------------------------

        /// A second launch asked for the window that is already open.
        public void ComeForward()
        {
            if (WindowState == WindowState.Minimized) WindowState = lastShownState;   // maximised again, if it was
            Activate();
            Topmost = true;    // Windows may refuse Activate from the background;
            Topmost = false;   // this at least raises the window above the others
        }

        /// Opens at the design size, or smaller when the screen is: never larger than the
        /// work area, never out of reach - a saved position on a screen that is no longer there
        /// is pulled back.
        void PlaceOnScreen()
        {
            Rect work = SystemParameters.WorkArea;   // the primary screen minus the taskbar

            // Title bar and borders. Windows under-reports them (SystemParameters said 8 x 31 where
            // the real frame is 16 x 39), so this is a generous allowance, used only to fit the screen.
            const double chromeW = 16, chromeH = 40;

            MinWidth = Constants.DesignWidth / 2 + chromeW;
            MinHeight = Constants.DesignHeight / 2 + chromeH;

            if (state.HasBounds)
            {
                Width = Math.Min(state.Width, work.Width);
                Height = Math.Min(state.Height, work.Height);

                // Inside the desktop as a whole: a screen may have been unplugged since last time.
                double screenLeft = SystemParameters.VirtualScreenLeft, screenTop = SystemParameters.VirtualScreenTop;
                double screenRight = screenLeft + SystemParameters.VirtualScreenWidth, screenBottom = screenTop + SystemParameters.VirtualScreenHeight;
                Left = Math.Max(screenLeft, Math.Min(state.Left, screenRight - Width));
                Top = Math.Max(screenTop, Math.Min(state.Top, screenBottom - Height));
                if (state.Maximized) WindowState = WindowState.Maximized;
                return;
            }

            // First launch: 1:1 if it fits, else the largest 4:3 that does. The scene is given its
            // exact size and WPF wraps the window around it (SizeToContent) - the only way to get
            // the client area right to the pixel, which is what keeps the drawing sharp at 1:1.
            double scale = Math.Min(1, Math.Min((work.Width - chromeW) / Constants.DesignWidth, (work.Height - chromeH) / Constants.DesignHeight));
            scaler.Width = Math.Floor(Constants.DesignWidth * scale);
            scaler.Height = Math.Floor(Constants.DesignHeight * scale);
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            // Once it is on screen the size belongs to the operator: the scene follows the window again.
            EventHandler release = null;
            release = delegate
            {
                ContentRendered -= release;
                SizeToContent = SizeToContent.Manual;
                scaler.Width = double.NaN;    // WPF: NaN = "no fixed size, take what the parent gives"
                scaler.Height = double.NaN;
            };
            ContentRendered += release;
        }

        void RememberBounds()
        {
            // WPF: RestoreBounds is where the window goes back to when it is not maximised.
            Rect bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            if (bounds.IsEmpty) return;
            state.Left = bounds.Left;
            state.Top = bounds.Top;
            state.Width = bounds.Width;
            state.Height = bounds.Height;
            state.Maximized = lastShownState == WindowState.Maximized;   // closed while minimised, it was still one or the other
        }

        // ---- development ---------------------------------------------------------------

        // "--place", for snapshots. n and i count from 1; i is the contact's position in category n.
        //   cover | favorites[:spread] | category:n[:spread]
        //   contact:n:i | delete:n:i | undo:n:i        the card, the question, the strip after it
        //   edit:new | edit:as-drawn | edit:n:i        as-drawn = without SURNAME, as first drawn
        //   tabs | tabs:delete:n
        //   search[:text] | recent | recent:demo    demo = the six look-ups the Recent page was drawn with
        //   language:en | language:it               as the radios in Setup do (the soak test walks through it)
        //   setup | setup:import:FILE               FILE = the slip that importing it shows, without the dialog
        public void GoTo(string where)
        {
            string[] parts = where.Split(':');
            string what = parts[0];
            int a = Number(parts, 1), b = Number(parts, 2);

            if (what == "cover") ShowCover();
            else if (what == "favorites") OpenAt(BinderView.FavoritesTab, Math.Max(0, a));
            else if (what == "search")
            {
                OpenAt(book.Categories[0].Id);
                OpenSearch();
                if (parts.Length > 1) search.Query = parts[1];
            }
            else if (what == "recent")
            {
                if (parts.Length > 1 && parts[1] == "demo") SeedRecents();
                OpenAt(RecentTab);
            }
            else if (what == "category") OpenAt(CategoryAt(a).Id, Math.Max(0, b));
            else if (what == "contact" || what == "delete" || what == "undo")
            {
                Category category = CategoryAt(a);
                Contact contact = book.InCategory(category.Id)[Math.Max(1, b) - 1];
                OpenAt(category.Id);
                if (what == "undo")
                {
                    Delete(contact);
                    return;
                }
                spread = list.SpreadOf(contact);   // the pages it is on, so that its ring shows
                OpenCard(contact);
                if (what == "delete") AskDelete(contact);
            }
            else if (what == "edit")
            {
                OpenAt(book.Categories[0].Id);
                if (parts.Length > 1 && parts[1] == "as-drawn")
                {
                    place = Place.Edit;
                    editing = null;
                    edit = new EditSpread(book, null, book.Categories[0].Id, true);
                    ShowEdit();
                }
                else if (a > 0) OpenEdit(book.InCategory(CategoryAt(a).Id)[Math.Max(1, b) - 1]);
                else OpenEdit(null);
            }
            else if (what == "language")
            {
                OpenAt(book.Categories[0].Id);
                SwitchLanguage(parts.Length > 1 && parts[1] == "it" ? Ui.Language.Italian : Ui.Language.English);
            }
            else if (what == "setup")
            {
                OpenAt(book.Categories[0].Id);
                OpenSetup();
                const string import = "setup:import:";
                if (where.StartsWith(import))
                {
                    string file = where.Substring(import.Length);   // a path has colons of its own
                    Func<string, string, string> ask = PickFileToOpen;
                    PickFileToOpen = (title, filter) => file;
                    Import();
                    PickFileToOpen = ask;
                }
            }
            else if (what == "tabs")
            {
                OpenAt(book.Categories[0].Id);
                OpenTabs();
                if (parts.Length > 2 && parts[1] == "delete") Click("tabs-delete-" + CategoryAt(b).Id);
            }
        }

        /// The six look-ups the Recent page was drawn with, at the times it was drawn with.
        void SeedRecents()
        {
            DateTime today = DateTime.Today;
            DateTime monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
            if (monday >= today.AddDays(-1)) monday = monday.AddDays(-7);   // keep it a weekday label, not TODAY or YESTERDAY
            var demo = new[] { "BRAVO|chat|" + monday.AddHours(9.5), "MIKE|copy|" + monday.AddHours(16.75), "HOTEL|copy|" + today.AddDays(-1).AddHours(11).AddMinutes(20),
                               "ALPHA|copy|" + today.AddDays(-1).AddHours(18).AddMinutes(3), "KILO|chat|" + today.AddHours(9).AddMinutes(15), "DELTA|copy|" + today.AddHours(10).AddMinutes(42) };
            state.Recents.Clear();
            foreach (string line in demo)
            {
                string[] f = line.Split('|');
                Contact contact = book.Contacts.Find(c => c.Name == f[0]);
                if (contact != null) state.Touch(contact.Id, f[1], DateTime.Parse(f[2]));
            }
        }

        Category CategoryAt(int n)
        {
            return book.Categories[Math.Max(1, Math.Min(n, book.Categories.Count)) - 1];
        }

        static int Number(string[] parts, int index)
        {
            int value;
            return parts.Length > index && int.TryParse(parts[index], out value) ? value : 0;
        }

        /// Presses the element with that id, as the mouse would (snapshots only).
        void Click(string id)
        {
            FrameworkElement target = Find(stage, id);
            if (target != null)
                target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = MouseLeftButtonUpEvent });
        }

        static FrameworkElement Find(DependencyObject root, string id)
        {
            if (System.Windows.Automation.AutomationProperties.GetAutomationId(root) == id) return root as FrameworkElement;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                FrameworkElement found = Find(VisualTreeHelper.GetChild(root, i), id);
                if (found != null) return found;
            }
            return null;
        }
    }
}
