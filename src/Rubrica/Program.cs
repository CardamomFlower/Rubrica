using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rubrica.Core;
using Rubrica.Model;
using Rubrica.Ui;
using static Rubrica.Ui.Lang;

namespace Rubrica
{
    // Entry point. Command line. For the operator:
    //   --software                paint without the graphics card, from now on (state.xml remembers)
    //   --hardware                back to letting Windows choose
    // For development:
    //   --data FOLDER             keep the book there instead of %APPDATA%\Rubrica
    //   --sample design|large     fill an empty book with generated contacts
    //   --snapshot FILE.png       draw one place into an image and exit
    //   --place NAME              which place to draw: cover, favorites, category:2 ...
    //   --language en|it          speak this language for this run (Setup's choice is not changed)
    static class Program
    {
        [STAThread]   // WPF needs its thread to be a "single-threaded apartment"
        static int Main(string[] args)
        {
            string snapshot = Option(args, "--snapshot");
            try
            {
                return Run(args, snapshot);
            }
            catch (Exception error)
            {
                Report(error, snapshot == null);
                return 1;
            }
        }

        static int Run(string[] args, string snapshot)
        {
            // Windows' language until state.xml says otherwise: something may have to be said before.
            Lang.Current = Lang.Choose(Option(args, "--language") ?? "");
            string folder;
            try
            {
                folder = Path.GetFullPath(Option(args, "--data") ?? BookStore.FolderToUse());
            }
            catch (InvalidOperationException error)
            {
                MessageBox.Show(T(error.Message), "Rubrica", MessageBoxButton.OK, MessageBoxImage.Warning);
                return 4;
            }

            // state.xml first: it says which language to speak, and everything below may speak.
            AppState state = AppState.Load(folder);
            Lang.Current = Lang.Choose(Option(args, "--language") ?? state.Language);

            // One Rubrica per book: two would save over each other. The second one wakes the
            // first and leaves. (A snapshot only reads, so it may run alongside.)
            string key = "Rubrica." + StableHash(folder.ToLowerInvariant()).ToString("x8");
            bool first;
            using (var mutex = new Mutex(true, key, out first))
            using (var wake = new EventWaitHandle(false, EventResetMode.AutoReset, key + ".wake"))
            {
                if (!first && snapshot == null)
                {
                    // The copy that is open would write its own setting back when it closes.
                    if (Array.IndexOf(args, "--software") >= 0 || Array.IndexOf(args, "--hardware") >= 0)
                        MessageBox.Show(T("Rubrica is open. Close it first, then start it again with this option."), "Rubrica", MessageBoxButton.OK, MessageBoxImage.Information);
                    wake.Set();
                    return 0;
                }

                var app = new Application();
                bool reporting = false;
                app.DispatcherUnhandledException += (sender, e) =>
                {
                    e.Handled = true;
                    if (reporting) return;   // the message box below keeps the window alive: one report is enough
                    reporting = true;
                    Report(e.Exception, true);
                    app.Shutdown(1);
                };
                // Anything that goes wrong off the window's thread: no way to carry on, but leave a trace.
                AppDomain.CurrentDomain.UnhandledException += (sender, e) => Report(e.ExceptionObject as Exception, false);

                var store = new BookStore(folder) { FirstCategoryName = T("CONTACTS") };
                LoadNotice notice;
                Book book;
                try
                {
                    book = store.Load(out notice);
                }
                catch (NewerBookException error)
                {
                    MessageBox.Show(T(error.Message) + "\n\n" + T("Nothing was changed. Update Rubrica to open it."), "Rubrica", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return 2;
                }
                catch (BookUnreadableException error)
                {
                    MessageBox.Show(T(error.Message) + "\n\n" + T("Nothing was changed. Try again in a moment."), "Rubrica", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return 3;
                }

                // The development options below write into the book, or walk through it deleting
                // and editing: they only work on a book of their own (--data) or on no book at all
                // (--snapshot never saves). On the operator's real book they are ignored.
                bool development = Option(args, "--data") != null || snapshot != null;

                string sample = development ? Option(args, "--sample") : null;
                if (sample != null && book.Contacts.Count == 0)
                {
                    if (sample == "design") SampleData.FillLikeTheDesign(book); else SampleData.FillLarge(book);
                    if (snapshot == null) store.Save(book);
                }

                state.Prune(book);   // recents of contacts that are no longer in the book

                if (Array.IndexOf(args, "--software") >= 0 || Array.IndexOf(args, "--hardware") >= 0)
                {
                    state.Software = Array.IndexOf(args, "--software") >= 0;
                    if (snapshot == null) state.Save();
                }
                // WPF paints with the graphics card when it can. SoftwareOnly keeps the whole job on
                // the processor: the way out when an old driver paints wrong.
                if (state.Software)
                    RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
                var window = new MainWindow(book, store, state, snapshot == null);

                if (snapshot != null)
                {
                    window.GoTo(Option(args, "--place") ?? "cover");
                    SaveSnapshot((FrameworkElement)window.Content, snapshot);
                    return 0;
                }

                // Development: open straight at a place, to look at it in the real window.
                string startAt = development ? Option(args, "--place") : null;
                if (startAt != null) window.Loaded += delegate { window.GoTo(startAt); };

                ListenForSecondLaunch(wake, window);
                window.Closed += delegate { state.Save(); };
                if (notice != null)
                    window.ContentRendered += delegate { MessageBox.Show(window, Say(notice), "Rubrica", MessageBoxButton.OK, MessageBoxImage.Warning); };
                return app.Run(window);
            }
        }

        /// What the operator is told when the book could not simply be read.
        static string Say(LoadNotice notice)
        {
            string what = notice.SetAsideAs != null ? T("The book could not be read and was set aside as\n{0}", notice.SetAsideAs) : T("book.xml was missing.");
            string instead = notice.FromUnfinishedSave ? T("The last save had been interrupted, but what it wrote was complete: it was loaded in its place.")
                           : notice.FromBackup ? T("The save before the last one was loaded in its place.")
                           : notice.BackupSetAsideAs != null ? T("The save before the last one could not be read either and was set aside as\n{0}\n\nStarting an empty book.", notice.BackupSetAsideAs)
                           : T("No earlier save could be read either: starting an empty book.");
            return what + "\n\n" + instead;
        }

        /// A second launch sets the event; this brings the window that is already open forward.
        static void ListenForSecondLaunch(EventWaitHandle wake, MainWindow window)
        {
            var listener = new Thread(() =>
            {
                try
                {
                    while (wake.WaitOne())
                    {
                        // WPF objects belong to the thread that made them: hand the work over to it.
                        window.Dispatcher.BeginInvoke(new Action(window.ComeForward));
                    }
                }
                catch (ObjectDisposedException)
                {
                    // The program is closing and has put the event away while this thread waited on it.
                }
            });
            listener.IsBackground = true;   // does not keep the process alive after the window closes
            listener.Start();
        }

        /// FNV-1a. string.GetHashCode differs between 32- and 64-bit processes, and the name
        /// of the mutex has to be the same for every copy of Rubrica that opens this folder.
        static uint StableHash(string text)
        {
            uint hash = 2166136261;
            foreach (char c in text)
                hash = unchecked((hash ^ c) * 16777619);
            return hash;
        }

        /// The value after a command-line option, or null.
        static string Option(string[] args, string name)
        {
            int at = Array.IndexOf(args, name);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        }

        /// A windowed exe has no console: leave the details where they can be found.
        static void Report(Exception error, bool tellOperator)
        {
            string log = Path.Combine(Path.GetTempPath(), "Rubrica-crash.txt");
            try
            {
                // Added to what is there - the first error is usually the one that matters - unless the file has grown silly.
                if (File.Exists(log) && new FileInfo(log).Length > 256 * 1024) File.Delete(log);
                File.AppendAllText(log, DateTime.Now + Environment.NewLine + error + Environment.NewLine + Environment.NewLine);
            }
            catch (Exception)
            {
                // Nowhere left to write to.
            }
            if (tellOperator)
                MessageBox.Show(T("Rubrica stopped because of an error.\n\nDetails were saved to:\n{0}", log),
                                "Rubrica", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        /// Draws the scene at its design size, whatever size the window would have had.
        static void SaveSnapshot(FrameworkElement scene, string path)
        {
            var size = new Size(Constants.DesignWidth, Constants.DesignHeight);
            scene.Measure(size);                          // WPF layout, by hand: first ask every
            scene.Arrange(new Rect(size));                // element for its size, then place it
            scene.UpdateLayout();

            // The desk is the window's background, and there is no window here: paint it first.
            var desk = new DrawingVisual();
            using (DrawingContext dc = desk.RenderOpen())
                dc.DrawRectangle(BinderView.Desk(), null, new Rect(size));

            var bitmap = new RenderTargetBitmap(Constants.DesignWidth, Constants.DesignHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(desk);
            bitmap.Render(scene);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream file = File.Create(path))
                encoder.Save(file);
        }
    }
}
