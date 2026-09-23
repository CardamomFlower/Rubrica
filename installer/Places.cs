using System;
using System.IO;
using System.Reflection;

namespace Rubrica.Setup
{
    /// Everything the installer and the uninstaller must agree on, in one place.
    ///
    /// A per-user install: nothing here needs an administrator, nothing is
    /// written outside HKEY_CURRENT_USER and the user's own folders, and the process is never
    /// elevated (app.manifest says asInvoker) - a UAC prompt would point %LOCALAPPDATA% at
    /// the administrator's profile and install in the wrong place.
    sealed class Places
    {
        public const string AppName = "Rubrica";
        public const string Website = "https://github.com/CardamomFlower/Rubrica";
        public const string ExeName = "Rubrica.exe";
        public const string UninstallerName = "Uninstall Rubrica.exe";
        public const string ShortcutName = "Rubrica.lnk";
        public const string ShortcutComment = "A contacts book";
        public const string PayloadResource = "Rubrica.exe";   // the LogicalName in RubricaSetup.csproj

        /// %LOCALAPPDATA%\Programs\Rubrica. Local, not roaming: a program image in roaming
        /// AppData would be dragged through profile sync on a managed PC.
        public string InstallFolder;

        public string StartMenuFolder;
        public string DesktopFolder;

        /// The operator's book, and nothing above it.
        public string DataFolder;

        /// Under HKEY_CURRENT_USER.
        public string UninstallKey;

        public string InstalledExe { get { return Path.Combine(InstallFolder, ExeName); } }
        public string Uninstaller { get { return Path.Combine(InstallFolder, UninstallerName); } }

        public static Places Real()
        {
            // Windows answers "" for a folder it cannot resolve, and Path.Combine would quietly turn
            // that into a path relative to wherever the installer was started from.
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(local) || string.IsNullOrEmpty(roaming))
                throw new InvalidOperationException("Windows did not say where this user's AppData folders are. Nothing was done.");   // said with T() by Program

            return new Places
            {
                InstallFolder = Path.Combine(local, "Programs", AppName),
                StartMenuFolder = Environment.GetFolderPath(Environment.SpecialFolder.Programs),      // "" = no shortcut there, reported
                DesktopFolder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                DataFolder = Path.Combine(roaming, Constants.DataFolder),
                UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppName,
            };
        }

        /// Development and tests: the same layout under one folder, and a registry key that is
        /// not Windows' list of installed programs. Nothing real is touched.
        public static Places Sandbox(string root)
        {
            root = Path.GetFullPath(root);
            return new Places
            {
                InstallFolder = Path.Combine(root, "Programs", AppName),
                StartMenuFolder = Path.Combine(root, "StartMenu"),
                DesktopFolder = Path.Combine(root, "Desktop"),
                DataFolder = Path.Combine(root, "AppData", Constants.DataFolder),
                UninstallKey = @"Software\Rubrica\SetupSandbox\" + Path.GetFileName(root.TrimEnd('\\', '/')),
            };
        }

        /// "0.1.0": the version this installer carries - its own, since both exes are built
        /// from the same Directory.Build.props.
        public static string Version
        {
            get
            {
                Version v = Assembly.GetExecutingAssembly().GetName().Version;
                return v.Major + "." + v.Minor + "." + v.Build;
            }
        }
    }

    /// How a task ended, and what to tell the operator.
    sealed class Outcome
    {
        public bool Ok;
        public string Message = "";

        /// Install: which shortcuts are really there now.
        public bool StartMenuShortcut, DesktopShortcut;

        /// Uninstall: the uninstaller's own file, renamed out of the way. It can only be deleted
        /// once this process has ended: whoever ends it calls UninstallTask.LeaveJanitor.
        public string AsideImage;

        public static Outcome Done(string message)
        {
            return new Outcome { Ok = true, Message = message };
        }

        public static Outcome Failed(string why)
        {
            return new Outcome { Ok = false, Message = why };
        }
    }
}
