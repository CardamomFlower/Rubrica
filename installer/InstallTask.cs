using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using static Rubrica.Ui.Lang;

namespace Rubrica.Setup
{
    /// Puts Rubrica on this PC, for this user. Running it over an older install replaces the
    /// program; it never goes near the data folder, so the book is safe whatever happens here.
    static class InstallTask
    {
        /// Where Rubrica.exe comes from: the copy compiled into this installer. (The tests put
        /// a stand-in here.)
        public static Func<Stream> Payload = () => Assembly.GetExecutingAssembly().GetManifestResourceStream(Places.PayloadResource);

        /// The file this installer runs from: it becomes the uninstaller.
        public static Func<string> Self = () => Assembly.GetExecutingAssembly().Location;

        public static Outcome Run(Places places, bool startMenuShortcut, bool desktopShortcut)
        {
            try
            {
                return Install(places, startMenuShortcut, desktopShortcut);
            }
            catch (Exception error)
            {
                return Outcome.Failed(Words.For(error));
            }
        }

        static Outcome Install(Places places, bool startMenuShortcut, bool desktopShortcut)
        {
            // ---- checking
            byte[] program = null;
            using (Stream payload = Payload())
                if (payload != null) program = Files.ReadAll(payload);
            if (program == null || program.Length < 2 || program[0] != 'M' || program[1] != 'Z')
                return Outcome.Failed(T("This installer is incomplete: it does not carry Rubrica. Download it again."));

            if (Files.InUse(places.InstalledExe))
                return Outcome.Failed(T("Rubrica is running. Close it and try again."));

            Directory.CreateDirectory(places.InstallFolder);

            // ---- the program: complete under another name first, so that a failure half-way
            // leaves the old one working
            string fresh = places.InstalledExe + ".new";
            File.WriteAllBytes(fresh, program);
            if (File.Exists(places.InstalledExe)) File.Replace(fresh, places.InstalledExe, null);   // one step: never a moment without a program
            else File.Move(fresh, places.InstalledExe);

            // ---- the uninstaller: this very file. Read and written, not copied: a copy would
            // carry along the "came from the Internet" mark, and Windows would warn about it
            // every time it is run from Settings.
            // From here on the program is installed: what goes wrong is reported, not fatal.
            var problems = new List<string>();
            string self = Self();
            try
            {
                if (!Files.SamePath(self, places.Uninstaller))
                    File.WriteAllBytes(places.Uninstaller, File.ReadAllBytes(self));
            }
            catch (Exception)
            {
                // An older one may be running (its window left open): it still uninstalls this version.
                if (!File.Exists(places.Uninstaller)) problems.Add(T("The uninstaller could not be written."));
            }

            // ---- shortcuts
            var outcome = Outcome.Done("");
            if (startMenuShortcut)
                outcome.StartMenuShortcut = MakeShortcut(places.StartMenuFolder, places, problems, T("The shortcut in the Start Menu could not be made."),
                                                         T("A shortcut called Rubrica in the Start Menu opens something else: it was left as it is."));
            if (desktopShortcut)
                outcome.DesktopShortcut = MakeShortcut(places.DesktopFolder, places, problems, T("The shortcut on the desktop could not be made."),
                                                       T("A shortcut called Rubrica on the desktop opens something else: it was left as it is."));

            // ---- Settings > Apps
            try
            {
                Registration.Write(places, program.Length + new FileInfo(self).Length);
            }
            catch (Exception)
            {
                problems.Add(T("It could not be listed in Settings > Apps."));
            }

            outcome.Message = string.Join(" ", problems);
            return outcome;
        }

        /// "Rubrica" is also the Italian for a phone book: a Rubrica.lnk that is already there and
        /// opens something else - the station's own list, say - is the operator's, and stays.
        static bool MakeShortcut(string folder, Places places, List<string> problems, string ifItFails, string ifSomeoneElses)
        {
            try
            {
                if (string.IsNullOrEmpty(folder)) throw new IOException("Windows would not say where it is.");
                string link = Path.Combine(folder, Places.ShortcutName);
                if (File.Exists(link) && !Files.PointsAt(link, places.InstalledExe))
                {
                    problems.Add(ifSomeoneElses);
                    return false;
                }
                Directory.CreateDirectory(folder);
                Shortcuts.Create(link, places.InstalledExe, T(Places.ShortcutComment));
                return true;
            }
            catch (Exception)
            {
                problems.Add(ifItFails);
                return false;
            }
        }
    }

    /// Small file chores shared by the two tasks.
    static class Files
    {
        public static byte[] ReadAll(Stream stream)
        {
            using (var memory = new MemoryStream())
            {
                stream.CopyTo(memory);
                return memory.ToArray();
            }
        }

        /// True when a program file cannot be replaced because it is running: Windows lets
        /// nobody open a running exe for writing.
        public static bool InUse(string path)
        {
            if (!File.Exists(path)) return false;
            try
            {
                File.SetAttributes(path, FileAttributes.Normal);   // a read-only file is not a running one
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    return false;
            }
            catch (IOException)
            {
                return true;
            }
        }

        /// True when the shortcut opens that program. A shortcut to something that is not a file
        /// (an app, a web page) or one that cannot be read is somebody else's.
        public static bool PointsAt(string link, string program)
        {
            try
            {
                string target = Shortcuts.Target(link);
                return !string.IsNullOrEmpty(target) && SamePath(target, program);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool SamePath(string a, string b)
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsInside(string file, string folder)
        {
            string root = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
            return Path.GetFullPath(file).StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// Why something failed, in a few words. Windows' own messages are in the language of the
    /// PC and carry whole paths; the installer's slip has room for neither.
    static class Words
    {
        public static string For(Exception error)
        {
            if (error is UnauthorizedAccessException) return T("Windows does not allow writing there: the folder is protected.");
            if (error is IOException) return T("The disk refused: it may be full, or a file may be open in another program.");
            return T("Unexpected: {0}.", error.GetType().Name);
        }
    }
}
