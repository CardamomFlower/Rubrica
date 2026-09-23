using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static Rubrica.Ui.Lang;

namespace Rubrica.Setup
{
    // RubricaSetup.exe                          installs: a window with two tick boxes
    // "Uninstall Rubrica.exe" --uninstall       removes: what Settings > Apps runs
    //
    //   --unattended      no window: do it (both shortcuts; the book is kept) and exit with 0,
    //                     or with 1 and the reason in %TEMP%\RubricaSetup-log.txt
    //
    // Development:
    //   --language en|it  speak this language (else: the one picked in Rubrica, else Windows' own)
    //   --sandbox FOLDER  everything goes under FOLDER and a registry key of its own:
    //                     nothing real is touched
    //   --snapshot FILE.png [--show done|failed]   draw the window into an image and exit
    static class Program
    {
        [STAThread]   // WPF and the shell's shortcut object both need a "single-threaded apartment"
        static int Main(string[] args)
        {
            bool unattended = Has(args, "--unattended") || Option(args, "--snapshot") != null;
            try
            {
                return Run(args);
            }
            catch (Exception error)
            {
                string log = Path.Combine(Path.GetTempPath(), "RubricaSetup-crash.txt");
                try
                {
                    File.WriteAllText(log, DateTime.Now + Environment.NewLine + error);
                }
                catch (Exception)
                {
                    // Nowhere left to write to.
                }
                if (!unattended)
                    MessageBox.Show(T("Rubrica Setup stopped because of an error.\n\nDetails were saved to:\n{0}", log), T("Rubrica Setup"), MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }

        static int Run(string[] args)
        {
            // Settings > Apps passes --uninstall. A double-click on the installed "Uninstall Rubrica.exe"
            // passes nothing, and means the same.
            bool removing = Has(args, "--uninstall")
                            || Path.GetFileName(UninstallTask.Self()).StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase);
            string sandbox = Option(args, "--sandbox");
            Ui.Lang.Current = Ui.Lang.Choose(Option(args, "--language") ?? "");
            Places places;
            try
            {
                places = sandbox != null ? Places.Sandbox(sandbox) : Places.Real();
            }
            catch (InvalidOperationException error)
            {
                if (!Has(args, "--unattended")) MessageBox.Show(T(error.Message), T("Rubrica Setup"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return 1;
            }
            // The language the operator picked in Rubrica, if they did.
            if (Option(args, "--language") == null) Ui.Lang.Current = Ui.Lang.Choose(Places.PickedLanguage(places));

            if (Has(args, "--unattended"))
            {
                Outcome outcome = removing ? UninstallTask.Run(places, false) : InstallTask.Run(places, true, true);
                UninstallTask.LeaveJanitor(outcome.AsideImage);
                if (!outcome.Ok)
                    File.WriteAllText(Path.Combine(Path.GetTempPath(), "RubricaSetup-log.txt"), DateTime.Now + " " + outcome.Message);
                return outcome.Ok ? 0 : 1;
            }

            var app = new Application();
            var window = new SetupWindow(places, removing);

            string snapshot = Option(args, "--snapshot");
            if (snapshot != null)
            {
                string show = Option(args, "--show");
                if (show == "done") window.Finished(new Outcome { Ok = true, Message = removing ? T("Your contacts book was left where it is.") : "", StartMenuShortcut = true, DesktopShortcut = true });
                else if (show == "failed") window.Finished(Outcome.Failed(T("Rubrica is running. Close it and try again.")));
                SaveSnapshot((FrameworkElement)window.Content, snapshot);
                return 0;
            }
            return app.Run(window);
        }

        static bool Has(string[] args, string name)
        {
            return Array.IndexOf(args, name) >= 0;
        }

        static string Option(string[] args, string name)
        {
            int at = Array.IndexOf(args, name);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        }

        static void SaveSnapshot(FrameworkElement scene, string path)
        {
            var size = new Size(scene.Width, scene.Height);
            scene.Measure(size);
            scene.Arrange(new Rect(size));
            scene.UpdateLayout();

            var bitmap = new RenderTargetBitmap((int)scene.Width, (int)scene.Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(scene);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream file = File.Create(path))
                encoder.Save(file);
        }
    }
}
