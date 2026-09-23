using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace Rubrica.Core
{
    /// The one thing Rubrica does outside itself: open the Teams chat with a contact.
    /// A chat, never a call.
    static class TeamsLink
    {
        /// Hands an address to Windows. Replaced by the tests, which must not open anything.
        public static Func<string, bool> Start = StartWithWindows;

        /// account: the contact's Teams sign-in, normally an email address.
        /// False when nothing could be started.
        public static bool OpenChat(string account)
        {
            // Escaped, so that nothing typed in the field can add parameters of its own to the link.
            string who;
            try
            {
                who = Uri.EscapeDataString((account ?? "").Trim());
            }
            catch (UriFormatException)
            {
                return false;   // half of a surrogate pair, or a text too long to be an address
            }
            if (who.Length == 0) return false;

            // The Teams program registers "msteams:" and opens the chat directly; without it the
            // same link through the browser lets Teams on the web, or the program, take over.
            if (IsRegistered("msteams") && Start("msteams:/l/chat/0/0?users=" + who)) return true;
            return Start("https://teams.microsoft.com/l/chat/0/0?users=" + who);
        }

        static bool IsRegistered(string scheme)
        {
            try
            {
                using (RegistryKey key = Registry.ClassesRoot.OpenSubKey(scheme))
                    return key != null && key.GetValue("URL Protocol") != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static bool StartWithWindows(string address)
        {
            try
            {
                // The handle on the program Windows starts is of no use here: given back at once,
                // not whenever the garbage collector gets to it.
                using (Process.Start(address)) { }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
