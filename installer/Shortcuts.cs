using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Rubrica.Setup
{
    /// Windows shortcuts (.lnk). .NET has no class for them: they are made by the shell's own
    /// ShellLink object, reached through COM. Nothing here depends on the Windows Script Host,
    /// which a managed PC may have switched off.
    static class Shortcuts
    {
        public static void Create(string linkPath, string target, string comment)
        {
            var link = (IShellLinkW)new ShellLink();
            try
            {
                link.SetPath(target);
                link.SetWorkingDirectory(System.IO.Path.GetDirectoryName(target));
                link.SetDescription(comment);
                link.SetIconLocation(target, 0);      // the program's own icon
                ((IPersistFile)link).Save(linkPath, false);
            }
            finally
            {
                Marshal.ReleaseComObject(link);
            }
        }

        /// What a shortcut points at (the tests read back what Create wrote).
        public static string Target(string linkPath)
        {
            var link = (IShellLinkW)new ShellLink();
            try
            {
                ((IPersistFile)link).Load(linkPath, 0);
                var path = new StringBuilder(1024);
                link.GetPath(path, path.Capacity, IntPtr.Zero, 0);
                return path.ToString();
            }
            finally
            {
                Marshal.ReleaseComObject(link);
            }
        }

        // The COM class and its interface, declared the way the Windows SDK does. The methods
        // must stay in exactly this order: COM finds them by position, not by name.
        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        class ShellLink
        {
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int capacity, IntPtr findData, int flags);
            void GetIDList(out IntPtr idList);
            void SetIDList(IntPtr idList);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int capacity);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder folder, int capacity);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string folder);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int capacity);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
            void GetHotkey(out short hotkey);
            void SetHotkey(short hotkey);
            void GetShowCmd(out int showCommand);
            void SetShowCmd(int showCommand);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int capacity, out int index);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int index);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, int reserved);
            void Resolve(IntPtr window, int flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
        }
    }
}
