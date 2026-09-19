using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Rubrica.Ui;
using static Rubrica.Ui.Lang;

namespace Rubrica.Setup
{
    /// The installer's one window: a slip of paper lying on the binder's blue leather, written
    /// and stamped like the slips inside Rubrica (it is drawn by the same code - see the
    /// linked files in RubricaSetup.csproj). The same window serves the uninstaller.
    sealed class SetupWindow : Window
    {
        const double W = 480, H = 400;

        /// Starts the installed program (OPEN RUBRICA), in its own folder: started in the
        /// installer's, it would keep the USB stick or the download folder busy for as long as
        /// it stays open. Replaced by the tests.
        public static Action<string> Launch = path =>
        {
            using (Process.Start(new ProcessStartInfo(path) { WorkingDirectory = System.IO.Path.GetDirectoryName(path) })) { }
        };

        readonly Places places;
        readonly bool removing;
        readonly Canvas scene = new Canvas { Width = W, Height = H, UseLayoutRounding = true, SnapsToDevicePixels = true };
        readonly Canvas slipHolder = new Canvas { Width = W, Height = H };

        // What is ticked, kept here because the slip is written anew at every step.
        bool startMenu = true, desktop = true, deleteBook;
        bool working;
        string asideImage;   // the uninstaller's own file, renamed away: swept once this process has ended

        public SetupWindow(Places places, bool removing)
        {
            this.places = places;
            this.removing = removing;

            Title = removing ? T("Remove Rubrica") : T("Rubrica Setup");
            ResizeMode = ResizeMode.CanMinimize;
            SizeToContent = SizeToContent.WidthAndHeight;   // WPF: the window wraps itself around its content
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Content = scene;

            // The cover's leather, stitched along the edge.
            var square = new CornerRadius(0);
            scene.Children.Add(Css.Box(W, H, square, new[] { Css.Fill(Css.Hex("#2450a0")), BinderView.Sheen(W, H) }));
            scene.Children.Add(BinderView.Texture(Textures.Leather((int)W, (int)H), W, H, square));
            Css.Place(scene, BinderView.Stitching(W - 20, H - 20, new CornerRadius(15)), 10, 10);
            scene.Children.Add(slipHolder);

            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape && !working) Close();
            };
            Closing += (sender, e) => e.Cancel = working;   // not in the middle of it
            Closed += delegate { UninstallTask.LeaveJanitor(asideImage); };
            Ready();
        }

        // ---- the steps -----------------------------------------------------------------------

        /// The question: what is about to happen, the boxes to tick, the two stamps.
        public void Ready()
        {
            var boxes = new StackPanel();
            if (removing)
            {
                boxes.Children.Add(CheckRow("setup-delete-book", T("DELETE MY CONTACTS BOOK TOO"), deleteBook, ticked => deleteBook = ticked));
                Lay(PaperSlip.Sheet(T("REMOVE RUBRICA?"), T("The program goes. Your contacts book stays on this PC, unless you tick the box."),
                    Sized(boxes, 1), T("KEEP"), T("REMOVE"), StampStyle.RedSolid, Close, Work));
                return;
            }

            string installed = Registration.InstalledVersion(places);
            string body = installed == null
                ? T("The radio's contacts book. It goes into your own programs folder: no administrator is needed.")
                : T("Rubrica {0} is installed and will be replaced. Your contacts book is not touched.", installed);
            boxes.Children.Add(CheckRow("setup-start-menu", T("START MENU ENTRY"), startMenu, ticked => startMenu = ticked));
            boxes.Children.Add(CheckRow("setup-desktop", T("DESKTOP SHORTCUT"), desktop, ticked => desktop = ticked));
            Lay(PaperSlip.Sheet(T("INSTALL RUBRICA {0}", Places.Version), body, Sized(boxes, 2), T("CLOSE"), T("INSTALL"), StampStyle.RedSolid, Close, Work));
        }

        /// The stamp was pressed: say so, let the window paint, then do the work. It takes a
        /// moment at most, so it runs here, on the window's own thread - which is also the kind
        /// of thread the shell's shortcut object insists on.
        void Work()
        {
            working = true;
            Lay(PaperSlip.Sheet(removing ? T("REMOVING...") : T("INSTALLING..."), T("A moment."), null, null, null, StampStyle.RedSolid, null, null));
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
            {
                Outcome outcome = removing ? UninstallTask.Run(places, deleteBook) : InstallTask.Run(places, startMenu, desktop);
                if (outcome.AsideImage != null) asideImage = outcome.AsideImage;
                working = false;
                Finished(outcome);
            }));
        }

        /// How it went.
        public void Finished(Outcome outcome)
        {
            if (!outcome.Ok)
            {
                Lay(PaperSlip.Sheet(removing ? T("RUBRICA WAS NOT REMOVED") : T("RUBRICA WAS NOT INSTALLED"), outcome.Message,
                    null, T("CLOSE"), T("TRY AGAIN"), StampStyle.DarkOutline, Close, Ready));
            }
            else if (removing)
            {
                Lay(PaperSlip.Sheet(T("RUBRICA HAS BEEN REMOVED"), outcome.Message, null, null, T("CLOSE"), StampStyle.DarkOutline, null, Close));
            }
            else
            {
                // What is really there, not what was ticked: a shortcut may have failed.
                bool inMenu = outcome.StartMenuShortcut, onDesktop = outcome.DesktopShortcut;
                string where = inMenu && onDesktop ? T("It is in the Start Menu and on the desktop.")
                             : inMenu ? T("It is in the Start Menu.")
                             : onDesktop ? T("It is on the desktop.")
                             : T("It has no shortcut: it is in your own programs folder.");
                string body = (where + " " + outcome.Message).Trim();
                Lay(PaperSlip.Sheet(T("RUBRICA IS INSTALLED"), body, null, T("CLOSE"), T("OPEN RUBRICA"), StampStyle.RedSolid, Close, delegate
                {
                    try
                    {
                        Launch(places.InstalledExe);
                        Close();
                    }
                    catch (Exception)
                    {
                        // It is installed; it is starting it that Windows refused (a policy, an antivirus).
                        Lay(PaperSlip.Sheet(T("RUBRICA IS INSTALLED"), T("But Windows would not start it: this PC may not allow programs in your own folders. Ask whoever looks after it."),
                            null, null, T("CLOSE"), StampStyle.DarkOutline, null, Close));
                    }
                }));
            }
        }

        // ---- pieces --------------------------------------------------------------------------

        /// Puts a slip on the leather, in the middle and a little crooked.
        void Lay(Canvas slip)
        {
            slipHolder.Children.Clear();
            slip.RenderTransformOrigin = new Point(0.5, 0.5);
            slip.RenderTransform = new RotateTransform(-1.5);
            Css.Place(slipHolder, Controls.Id(slip, "slip-paper"), Math.Floor((W - slip.Width) / 2), Math.Floor((H - slip.Height) / 2));
        }

        /// A tick box and its label, 30 px high; the whole row takes the click.
        static FrameworkElement CheckRow(string id, string label, bool ticked, Action<bool> changed)
        {
            var row = Controls.Id(new Canvas { Width = PaperSlip.Width - 50, Height = 30, Background = Brushes.Transparent, Cursor = Cursors.Hand }, id);
            var box = new PaperCheck("check-" + id) { IsChecked = ticked, IsHitTestVisible = false };
            Css.Place(row, box, 0, 4);

            var text = new GlyphText(label, AppFonts.Type, 11, Controls.Ink, 2) { IsHitTestVisible = false };
            Css.Place(row, text, 32, Math.Floor((30 - text.LineHeight) / 2 + 0.5));

            row.MouseLeftButtonUp += delegate
            {
                box.IsChecked = !box.IsChecked;
                changed(box.IsChecked);
            };
            return row;
        }

        /// The slip reads the height of what it carries before WPF has laid it out: say it.
        static FrameworkElement Sized(StackPanel boxes, int rows)
        {
            boxes.Width = PaperSlip.Width - 50;
            boxes.Height = 30 * rows;
            return boxes;
        }
    }
}
