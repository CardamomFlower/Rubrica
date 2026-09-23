using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using static Rubrica.Ui.Lang;

namespace Rubrica.Setup
{
    /// Takes Rubrica off this PC. The book stays unless the operator asked otherwise.
    static class UninstallTask
    {
        /// The file this uninstaller runs from.
        public static Func<string> Self = () => Assembly.GetExecutingAssembly().Location;

        public static Outcome Run(Places places, bool deleteBook)
        {
            try
            {
                return Uninstall(places, deleteBook);
            }
            catch (Exception error)
            {
                return Outcome.Failed(Words.For(error));
            }
        }

        static Outcome Uninstall(Places places, bool deleteBook)
        {
            if (Files.InUse(places.InstalledExe))
                return Outcome.Failed(T("Rubrica is running. Close it and try again."));

            // The program first, while nothing else has been touched: if it cannot go, this
            // fails whole and can simply be tried again.
            DeleteFile(places.InstalledExe);
            DeleteFile(places.InstalledExe + ".new");

            // From here on nothing stops the rest: every step is tried, and what could not be
            // done is said at the end. A half-removed Rubrica that cannot be finished is worse
            // than a leftover.
            var leftovers = new List<string>();

            // Explorer starts a program with its own folder as the working directory, and a
            // process holds on to that: without this the files go but the folder stays.
            Try(() => Directory.SetCurrentDirectory(Path.GetTempPath()), null, leftovers);

            // Stepping aside. A running program cannot delete its own file, but it may rename
            // it - on the same disk. %TEMP% first; when that is another disk, the folder above
            // the program's own, which never is.
            string self = Self(), aside = null;
            if (Files.IsInside(self, places.InstallFolder))
            {
                string name = "RubricaUninstall-" + DateTime.Now.Ticks + ".tmp";
                foreach (string folder in new[] { Path.GetTempPath(), Path.GetDirectoryName(places.InstallFolder.TrimEnd('\\')) })
                {
                    try
                    {
                        File.Move(self, Path.Combine(folder, name));
                        // Across disks Windows copies, cannot delete a running program, and still says
                        // it moved it: then the original is where it was, and the copy is of no use.
                        if (File.Exists(self))
                        {
                            DeleteFile(Path.Combine(folder, name));
                            continue;
                        }
                        aside = Path.Combine(folder, name);
                        break;
                    }
                    catch (Exception)
                    {
                        // Try the next place.
                    }
                }
                if (aside == null) leftovers.Add(T("The uninstaller could not remove itself: delete the Rubrica folder in your Programs folder by hand."));
            }
            else
            {
                Try(() => DeleteFile(places.Uninstaller), T("The uninstaller could not be removed."), leftovers);
            }

            // Only what an install writes - never "everything in that folder" on the word of a
            // path - and the folder itself if that leaves it empty.
            Try(() =>
            {
                if (Directory.Exists(places.InstallFolder) && Directory.GetFileSystemEntries(places.InstallFolder).Length == 0)
                    Directory.Delete(places.InstallFolder);
            }, null, leftovers);

            Try(() => RemoveShortcut(Path.Combine(places.StartMenuFolder ?? "", Places.ShortcutName), places), T("The Start Menu shortcut could not be removed."), leftovers);
            Try(() => RemoveShortcut(Path.Combine(places.DesktopFolder ?? "", Places.ShortcutName), places), T("The desktop shortcut could not be removed."), leftovers);
            Try(() => Registration.Remove(places), T("The entry in Settings > Apps could not be removed."), leftovers);

            string note = T("Your contacts book was left where it is.");
            if (deleteBook)
            {
                note = T("Your contacts book went with it.");
                Try(() => { if (Directory.Exists(places.DataFolder)) Directory.Delete(places.DataFolder, true); }, null, leftovers);
                // A book still in the folder of 0.1.0, on a PC where the new Rubrica was never started,
                // is the same book: it goes too, on its own account, and nothing above it does.
                Try(() => { if (Directory.Exists(places.OldDataFolder)) Directory.Delete(places.OldDataFolder, true); }, null, leftovers);
                if (Directory.Exists(places.DataFolder) || Directory.Exists(places.OldDataFolder))
                    note = T("Your contacts book could not be deleted: it is still where it was.");
            }

            Outcome outcome = Outcome.Done((note + " " + string.Join(" ", leftovers)).Trim());
            outcome.AsideImage = aside;
            return outcome;
        }

        static void Try(Action step, string ifItFails, List<string> leftovers)
        {
            try
            {
                step();
            }
            catch (Exception)
            {
                if (ifItFails != null) leftovers.Add(ifItFails);
            }
        }

        static void DeleteFile(string path)
        {
            if (!File.Exists(path)) return;
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }

        /// A shortcut called Rubrica.lnk is removed only if it is Rubrica's: one the operator
        /// made for something else that happens to have the name stays.
        static void RemoveShortcut(string link, Places places)
        {
            if (File.Exists(link) && Files.PointsAt(link, places.InstalledExe)) DeleteFile(link);
        }

        /// Called last of all, by whoever ends the process (Program for --unattended, the window
        /// when it closes): a detached shell that waits for this process to let go of its own
        /// image, deletes it, then deletes itself. If a policy forbids batch files the image
        /// simply stays where it is, where it harms nobody.
        public static void LeaveJanitor(string image)
        {
            if (string.IsNullOrEmpty(image)) return;
            try
            {
                string batch = Path.Combine(Path.GetTempPath(), "rubrica-cleanup-" + DateTime.Now.Ticks + ".cmd");
                string target = image.Replace("%", "%%");   // a percent sign in a path would be read as a variable
                File.WriteAllText(batch,
                    "@echo off\r\n" +
                    "chcp 65001 >nul\r\n" +                  // the paths below are UTF-8, not the console's own code page
                    "for /l %%i in (1,1,120) do (\r\n" +
                    "  del /f /q \"" + target + "\" >nul 2>&1\r\n" +
                    "  if not exist \"" + target + "\" goto gone\r\n" +
                    "  ping -n 2 127.0.0.1 >nul\r\n" +
                    ")\r\n" +
                    ":gone\r\n" +
                    "del /f /q \"%~f0\" >nul 2>&1\r\n",
                    new UTF8Encoding(false));

                // The full path to cmd.exe: a bare name would be looked up along PATH.
                string cmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
                using (Process.Start(new ProcessStartInfo(cmd, "/c \"\"" + batch + "\"\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetTempPath(),
                })) { }
            }
            catch (Exception)
            {
                // Not worth failing an uninstall that has already succeeded.
            }
        }
    }
}
