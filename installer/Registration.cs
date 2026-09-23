using System;
using Microsoft.Win32;

namespace Rubrica.Setup
{
    /// The entry in Settings > Apps (Add or remove programs). For this user only: the key is
    /// under HKEY_CURRENT_USER, which needs no administrator.
    static class Registration
    {
        public static void Write(Places places, long installedBytes)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(places.UninstallKey))
            {
                key.SetValue("DisplayName", Places.AppName);
                key.SetValue("DisplayVersion", Places.Version);
                key.SetValue("URLInfoAbout", Places.Website);
                key.SetValue("InstallLocation", places.InstallFolder);
                key.SetValue("DisplayIcon", places.InstalledExe + ",0");

                // In quotes: the path holds the user's name, which often has a space in it.
                key.SetValue("UninstallString", "\"" + places.Uninstaller + "\" --uninstall");

                key.SetValue("EstimatedSize", (int)Math.Max(1, installedBytes / 1024), RegistryValueKind.DWord);   // in KB
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        /// The version that is installed, or null when Rubrica is not.
        public static string InstalledVersion(Places places)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(places.UninstallKey))
                return key == null ? null : key.GetValue("DisplayVersion") as string ?? "";
        }

        public static void Remove(Places places)
        {
            Registry.CurrentUser.DeleteSubKeyTree(places.UninstallKey, false);   // false = fine if it is not there
        }
    }
}
