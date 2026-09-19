using System;
using System.Collections.Generic;
using System.Globalization;

namespace Rubrica.Ui
{
    enum Language { English, Italian }

    /// Every word the program says, in English and in Italian (owner's decision, 2026-09-19).
    /// The code is written with the English text, as the design canvas draws it, and passes
    /// it through T(): the English is the key, the Italian is looked up in the table below.
    /// A text with no Italian is shown in English and noted in Missing, which the tests read.
    static class Lang
    {
        public static Language Current = Language.English;

        /// Texts asked for in Italian that the table does not have. Empty, or a test fails.
        public static readonly HashSet<string> Missing = new HashSet<string>();

        public static string T(string english)
        {
            if (Current == Language.English) return english;
            string italian;
            if (Italian.TryGetValue(english, out italian)) return italian;
            Missing.Add(english);
            return english;
        }

        /// A text with blanks in it: T("PHONE {0}", 2).
        public static string T(string english, params object[] values)
        {
            return string.Format(CultureInfo.InvariantCulture, T(english), values);
        }

        /// One text for one, another for more: N(3, "{0} CONTACT ADDED", "{0} CONTACTS ADDED").
        /// Both are whole sentences because Italian changes more than the noun.
        public static string N(int count, string one, string many)
        {
            return T(count == 1 ? one : many, count);
        }

        // ---- which language ------------------------------------------------------------------

        /// code: what state.xml says - "en", "it", or "" for not chosen yet, which follows the
        /// language Windows itself speaks.
        public static Language Choose(string code)
        {
            if (code == "it") return Language.Italian;
            if (code == "en") return Language.English;
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "it" ? Language.Italian : Language.English;
        }

        public static string Code(Language language)
        {
            return language == Language.Italian ? "it" : "en";
        }

        // ---- the line under a name on the Recent page ------------------------------------------

        static readonly string[] EnglishDays = { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" };
        static readonly string[] ItalianDays = { "DOM", "LUN", "MAR", "MER", "GIO", "VEN", "SAB" };
        static readonly string[] EnglishMonths = { "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };
        static readonly string[] ItalianMonths = { "GEN", "FEB", "MAR", "APR", "MAG", "GIU", "LUG", "AGO", "SET", "OTT", "NOV", "DIC" };

        /// "COPIED - TODAY 10:42". action: what Recent recorded - "view", "copy" or "chat".
        public static string RecentLine(string action, DateTime at, DateTime now)
        {
            string what = action == "copy" ? T("COPIED") : action == "chat" ? T("TEAMS") : T("VIEWED");
            return what + " - " + When(at, now);
        }

        /// TODAY 10:42, YESTERDAY 18:03, MON 16:45 within the week, then 12 SEP, then 12 SEP 2025.
        public static string When(DateTime at, DateTime now)
        {
            bool italian = Current == Language.Italian;
            string time = at.ToString("HH:mm", CultureInfo.InvariantCulture);
            int daysAgo = (now.Date - at.Date).Days;
            if (daysAgo == 0) return T("TODAY") + " " + time;
            if (daysAgo == 1) return T("YESTERDAY") + " " + time;
            if (daysAgo > 1 && daysAgo < 7) return (italian ? ItalianDays : EnglishDays)[(int)at.DayOfWeek] + " " + time;
            string day = at.Day + " " + (italian ? ItalianMonths : EnglishMonths)[at.Month - 1];
            return at.Year == now.Year ? day : day + " " + at.Year;
        }

        // ---- the Italian ---------------------------------------------------------------------

        /// Every English text the program uses, with its Italian. For the tests.
        public static IEnumerable<KeyValuePair<string, string>> Pairs { get { return Italian; } }

        static readonly Dictionary<string, string> Italian = new Dictionary<string, string>
        {
            // the binder, its tabs and its cover
            { "FAVORITES", "PREFERITI" },
            { "SEARCH", "CERCA" },
            { "NEW", "NUOVO" },
            { "RECENT", "RECENTI" },
            { "TABS", "CATEGORIE" },
            { "SETUP", "OPZIONI" },
            { "CONTACTS", "CONTATTI" },
            { "OPEN", "APRI" },
            { "Rubrica - software rendering", "Rubrica - rendering software" },

            // lists
            { "SPEED DIAL", "A PORTATA DI MANO" },
            { "No favorites yet.", "Ancora nessun preferito." },
            { "STAR A CONTACT TO KEEP IT ONE CLICK AWAY.", "METTI LA STELLA A UN CONTATTO PER AVERLO A UN CLIC." },
            { "LAST USED", "ULTIMI USATI" },
            { "Nobody looked up yet.", "Ancora nessuno consultato." },
            { "OPEN A CARD, COPY A NUMBER OR START A CHAT, AND THEY WILL BE LISTED HERE.", "APRI UNA SCHEDA, COPIA UN NUMERO O AVVIA UNA CHAT: COMPARIRANNO QUI." },
            { "Nothing written here yet.", "Qui non c'\u00e8 ancora niente." },
            { "ADD A CONTACT, OR IMPORT YOUR LIST FROM SETUP.", "AGGIUNGI UN CONTATTO, O IMPORTA IL TUO ELENCO DA OPZIONI." },
            { "NEW CONTACT", "NUOVO CONTATTO" },
            { "IMPORT CSV", "IMPORTA CSV" },
            { "Teams chat", "Chat di Teams" },
            { "COPIED", "COPIATO" },
            { "TEAMS", "TEAMS" },
            { "VIEWED", "APERTO" },
            { "TODAY", "OGGI" },
            { "YESTERDAY", "IERI" },

            // search
            { "{0} CONTACT IN ALL CATEGORIES", "{0} CONTATTO IN TUTTE LE CATEGORIE" },
            { "{0} CONTACTS IN ALL CATEGORIES", "{0} CONTATTI IN TUTTE LE CATEGORIE" },
            { "{0} RESULT IN ALL CATEGORIES", "{0} RISULTATO IN TUTTE LE CATEGORIE" },
            { "{0} RESULTS IN ALL CATEGORIES", "{0} RISULTATI IN TUTTE LE CATEGORIE" },
            { "No match for \"{0}\".", "Nessun risultato per \"{0}\"." },
            { "CHECK THE SPELLING, OR ADD IT AS A NEW CONTACT.", "CONTROLLA COME L'HAI SCRITTO, O AGGIUNGILO COME NUOVO CONTATTO." },

            // the card
            { "PHONE", "TELEFONO" },
            { "EMAIL", "EMAIL" },
            { "COPY", "COPIA" },
            { "OPEN CHAT", "APRI CHAT" },
            { "NOTES", "NOTE" },
            { "DELETE", "ELIMINA" },
            { "EDIT", "MODIFICA" },
            { "NUMBER COPIED", "NUMERO COPIATO" },
            { "EMAIL COPIED", "EMAIL COPIATA" },
            { "THE CLIPBOARD IS BUSY - TRY AGAIN", "GLI APPUNTI SONO OCCUPATI - RIPROVA" },
            { "TEAMS COULD NOT BE OPENED", "IMPOSSIBILE APRIRE TEAMS" },

            // delete and undo
            { "REMOVE {0} FROM THE BOOK?", "TOGLIERE {0} DALLA RUBRICA?" },
            { "The page is torn out. UNDO stays available right after.", "La pagina viene strappata. Subito dopo puoi ancora RIPRISTINARLA." },
            { "KEEP", "LASCIA" },
            { "YES, REMOVE", "S\u00cc, TOGLI" },
            { "{0} REMOVED FROM THE BOOK", "{0} NON \u00c8 PI\u00d9 IN RUBRICA" },   // the name first: it is what gets cut short; no participle to agree with it
            { "UNDO", "RIPRISTINA" },   // not ANNULLA, which is CANCEL - and Windows' own Cancel

            // the form
            { "EDIT CONTACT", "MODIFICA CONTATTO" },
            { "NAME", "NOME" },
            { "SURNAME", "COGNOME" },
            { "ROLE / ORG", "RUOLO / ENTE" },
            { "CATEGORY", "CATEGORIA" },
            { "FAVORITE / SPEED DIAL", "PREFERITO / A PORTATA DI MANO" },
            { "CHANNELS", "RECAPITI" },
            { "PHONE {0}", "TELEFONO {0}" },
            { "LABEL", "ETICHETTA" },
            { "ADD NUMBER", "AGGIUNGI NUMERO" },
            { "TEAMS (ACCOUNT EMAIL)", "TEAMS (EMAIL DELL'ACCOUNT)" },
            { "SAVE", "SALVA" },
            { "CANCEL", "ANNULLA" },
            { "DISCARD WHAT YOU WROTE?", "SCARTARE QUELLO CHE HAI SCRITTO?" },
            { "Nothing of it has been saved.", "Non \u00e8 stato salvato niente." },
            { "KEEP WRITING", "CONTINUA" },
            { "DISCARD", "SCARTA" },

            // the Tabs page
            { "MANAGE CATEGORIES", "GESTIONE" },
            { "FAVORITES IS FIXED. RENAME IN PLACE, REORDER WITH THE ARROWS.", "LA CATEGORIA PREFERITI \u00c8 FISSA. RINOMINA SCRIVENDO SUL NOME, RIORDINA CON LE FRECCE." },
            { "ADD A TAB", "AGGIUNGI UNA CATEGORIA" },
            { "THE BINDER IS FULL: {0} TABS", "IL RACCOGLITORE \u00c8 PIENO: {0} CATEGORIE" },
            { "ADD", "AGGIUNGI" },
            { "DELETE TAB", "ELIMINA" },   // the page head and the stamp; the question names the tab
            { "DELETE TAB {0}?", "ELIMINARE LA CATEGORIA {0}?" },
            { "It is empty.", "\u00c8 vuota." },
            { "It holds {0} contact. Move it to:", "Contiene {0} contatto. Spostalo in:" },
            { "It holds {0} contacts. Move them to:", "Contiene {0} contatti. Spostali in:" },

            // Setup
            { "INSIDE COVER", "INTERNO COPERTINA" },
            { "DATA", "DATI" },
            { "NOTEPAD LIST -> BOOK", "ELENCO -> RUBRICA" },
            { "EXPORT CSV", "ESPORTA CSV" },
            { "BOOK -> FILE", "RUBRICA -> FILE" },
            { "BACKUP NOW", "BACKUP ADESSO" },
            { "SAFETY COPY OF THE BOOK", "COPIA DI SICUREZZA" },
            { "SORT A-Z BY", "ORDINA A-Z PER" },
            { "FIRST NAME", "NOME" },
            { "KEYBOARD SHORTCUTS", "SCORCIATOIE DA TASTIERA" },
            { "FOCUS SEARCH", "VAI ALLA RICERCA" },
            { "CLOSE / BACK", "CHIUDI / INDIETRO" },
            { "LANGUAGE", "LINGUA" },

            // import, export, backup
            { "Import contacts", "Importa contatti" },
            { "Lists and CSV files (*.csv;*.txt)|*.csv;*.txt|All files (*.*)|*.*", "Elenchi e file CSV (*.csv;*.txt)|*.csv;*.txt|Tutti i file (*.*)|*.*" },
            { "Export contacts", "Esporta contatti" },
            { "CSV file (*.csv)|*.csv", "File CSV (*.csv)|*.csv" },
            { "Rubrica contacts {0}.csv", "Rubrica contatti {0}.csv" },
            { "Backup the book", "Backup della rubrica" },
            { "Rubrica book (*.xml)|*.xml", "Rubrica (*.xml)|*.xml" },
            { "Rubrica backup {0}.xml", "Rubrica backup {0}.xml" },
            { "THE FILE COULD NOT BE READ", "IMPOSSIBILE LEGGERE IL FILE" },
            { "THE FILE COULD NOT BE WRITTEN", "IMPOSSIBILE SCRIVERE IL FILE" },
            { "THE BACKUP COULD NOT BE WRITTEN", "IMPOSSIBILE SCRIVERE IL BACKUP" },
            { "NOTHING TO IMPORT", "NIENTE DA IMPORTARE" },
            { "IMPORT {0} CONTACT?", "IMPORTARE {0} CONTATTO?" },
            { "IMPORT {0} CONTACTS?", "IMPORTARE {0} CONTATTI?" },
            { "IMPORT", "IMPORTA" },
            { "{0} CONTACT ADDED", "{0} CONTATTO AGGIUNTO" },
            { "{0} CONTACTS ADDED", "{0} CONTATTI AGGIUNTI" },
            { "{0} CONTACT EXPORTED", "{0} CONTATTO ESPORTATO" },
            { "{0} CONTACTS EXPORTED", "{0} CONTATTI ESPORTATI" },
            { "BACKUP SAVED", "BACKUP SALVATO" },
            { "A plain list of names and numbers: {0}.", "Un elenco semplice di nomi e numeri: {0}." },
            { "From {0}.", "Da {0}." },
            { "{0} is already in the book and is left alone.", "{0} \u00e8 gi\u00e0 in rubrica e resta com'\u00e8." },
            { "{0} are already in the book and are left alone.", "{0} sono gi\u00e0 in rubrica e restano come sono." },
            { "{0} line has no number: left out.", "{0} riga non ha un numero: esclusa." },
            { "{0} lines have no number: left out.", "{0} righe non hanno un numero: escluse." },
            { "{0} row has no name: left out.", "{0} riga non ha un nome: esclusa." },
            { "{0} rows have no name: left out.", "{0} righe non hanno un nome: escluse." },
            { "New tab: {0}.", "Nuova categoria: {0}." },
            { "New tabs: {0}.", "Nuove categorie: {0}." },
            { "The binder is full: {0} goes under {1}.", "Il raccoglitore \u00e8 pieno: {0} va in {1}." },
            { "The binder is full: {0} go under {1}.", "Il raccoglitore \u00e8 pieno: {0} vanno in {1}." },
            { "Put them under:", "Mettili in:" },
            { "Everybody in {0} is already in the book.", "Tutti quelli di {0} sono gi\u00e0 in rubrica." },
            { "{0}: {1} already in the book, {2} without a number.", "{0}: {1} gi\u00e0 in rubrica, {2} senza numero." },
            { "{0}: {1} already in the book, {2} without a name.", "{0}: {1} gi\u00e0 in rubrica, {2} senza nome." },
            { "No contacts were found in {0}. A list has one name and number per line; a CSV starts with a row of column names.",
              "In {0} non c'\u00e8 nessun contatto. Un elenco ha un nome e un numero per riga; un CSV comincia con una riga di nomi di colonna." },
            { "OK", "OK" },

            // what went wrong, in a few words
            { "It is not there any more.", "Non c'\u00e8 pi\u00f9." },
            { "Windows does not allow it: the file or its folder is protected.", "Windows non lo permette: il file o la sua cartella sono protetti." },
            { "The disk refused: the file may be open in another program, or the disk may be full.", "Il disco ha rifiutato: il file potrebbe essere aperto in un altro programma, o il disco essere pieno." },
            { "Unexpected: {0}.", "Imprevisto: {0}." },
            { "The file is too large to be a contacts list.", "Il file \u00e8 troppo grande per essere un elenco di contatti." },
            { "This is not a text file.", "Questo non \u00e8 un file di testo." },
            { "That is the book Rubrica is working on. Choose another name or folder.", "\u00c8 la rubrica su cui Rubrica sta lavorando. Scegli un altro nome o un'altra cartella." },
            { "A book that was not there when Rubrica started has appeared in its folder. Nothing was overwritten: close Rubrica and start it again.",
              "Nella cartella \u00e8 comparsa una rubrica che all'avvio non c'era. Non \u00e8 stato sovrascritto niente: chiudi Rubrica e riaprila." },
            { "The book is there but could not be opened: another program may be using it, or Windows denies access to it.",
              "La rubrica c'\u00e8, ma non si riesce ad aprirla: forse la sta usando un altro programma, o Windows ne nega l'accesso." },
            { "Not a Rubrica book.", "Non \u00e8 una rubrica di Rubrica." },
            { "Windows did not say where this user's AppData folder is.", "Windows non ha indicato dove si trova la cartella AppData di questo utente." },
            { "This book was written by a newer version of Rubrica.", "Questa rubrica \u00e8 stata scritta da una versione pi\u00f9 recente di Rubrica." },
            { "Nothing was changed. Update Rubrica to open it.", "Non \u00e8 stato cambiato niente. Aggiorna Rubrica per aprirla." },
            { "Nothing was changed. Try again in a moment.", "Non \u00e8 stato cambiato niente. Riprova tra un momento." },
            { "Rubrica is open. Close it first, then start it again with this option.", "Rubrica \u00e8 aperta. Chiudila, poi riavviala con questa opzione." },
            { "Rubrica stopped because of an error.\n\nDetails were saved to:\n{0}", "Rubrica si \u00e8 fermata per un errore.\n\nI dettagli sono stati salvati in:\n{0}" },
            { "The book could not be saved.\n\n{0}", "Impossibile salvare la rubrica.\n\n{0}" },
            { "The book still cannot be saved. Close anyway, and lose what was changed since the last good save?",
              "La rubrica ancora non si riesce a salvare. Chiudere lo stesso, perdendo quello che \u00e8 cambiato dall'ultimo salvataggio riuscito?" },
            { "The book could not be read and was set aside as\n{0}", "Non \u00e8 stato possibile leggere la rubrica, che \u00e8 stata messa da parte come\n{0}" },
            { "book.xml was missing.", "book.xml non c'era." },
            { "The save before the last one was loaded in its place.", "Al suo posto \u00e8 stato caricato il salvataggio precedente." },
            { "The last save had been interrupted, but what it wrote was complete: it was loaded in its place.",
              "L'ultimo salvataggio era stato interrotto, ma quello che aveva scritto era completo: \u00e8 stato caricato al suo posto." },
            { "The save before the last one could not be read either and was set aside as\n{0}\n\nStarting an empty book.",
              "Neanche il salvataggio precedente si \u00e8 potuto leggere: \u00e8 stato messo da parte come\n{0}\n\nSi parte da una rubrica vuota." },
            { "No earlier save could be read either: starting an empty book.", "Neanche il salvataggio precedente si \u00e8 potuto leggere: si parte da una rubrica vuota." },

            // the installer
            { "Rubrica Setup", "Installazione di Rubrica" },
            { "Remove Rubrica", "Rimuovi Rubrica" },
            { "The radio's contacts book", "La rubrica della radio" },
            { "INSTALL RUBRICA {0}", "INSTALLA RUBRICA {0}" },
            { "The radio's contacts book. It goes into your own programs folder: no administrator is needed.",
              "La rubrica della radio. Va nella tua cartella dei programmi: non serve l'amministratore." },
            { "Rubrica {0} is installed and will be replaced. Your contacts book is not touched.",
              "Rubrica {0} \u00e8 installata e verr\u00e0 sostituita. La tua rubrica non viene toccata." },
            { "START MENU ENTRY", "VOCE NEL MENU START" },
            { "DESKTOP SHORTCUT", "COLLEGAMENTO SUL DESKTOP" },
            { "CLOSE", "CHIUDI" },
            { "INSTALL", "INSTALLA" },
            { "REMOVE RUBRICA?", "RIMUOVERE RUBRICA?" },
            { "The program goes. Your contacts book stays on this PC, unless you tick the box.",
              "Il programma se ne va. La rubrica resta su questo PC, a meno che tu non spunti la casella." },
            { "DELETE MY CONTACTS BOOK TOO", "ELIMINA ANCHE LA MIA RUBRICA" },
            { "REMOVE", "RIMUOVI" },
            { "INSTALLING...", "INSTALLAZIONE..." },
            { "REMOVING...", "RIMOZIONE..." },
            { "A moment.", "Un momento." },
            { "RUBRICA WAS NOT INSTALLED", "RUBRICA NON \u00c8 STATA INSTALLATA" },
            { "RUBRICA WAS NOT REMOVED", "RUBRICA NON \u00c8 STATA RIMOSSA" },
            { "TRY AGAIN", "RIPROVA" },
            { "RUBRICA HAS BEEN REMOVED", "RUBRICA \u00c8 STATA RIMOSSA" },
            { "RUBRICA IS INSTALLED", "RUBRICA \u00c8 INSTALLATA" },
            { "OPEN RUBRICA", "APRI RUBRICA" },
            { "It is in the Start Menu and on the desktop.", "La trovi nel menu Start e sul desktop." },
            { "It is in the Start Menu.", "La trovi nel menu Start." },
            { "It is on the desktop.", "La trovi sul desktop." },
            { "It has no shortcut: it is in your own programs folder.", "Non ha collegamenti: \u00e8 nella tua cartella dei programmi." },
            { "This installer is incomplete: it does not carry Rubrica. Download it again.", "Questo programma di installazione \u00e8 incompleto: non contiene Rubrica. Scaricalo di nuovo." },
            { "Rubrica is running. Close it and try again.", "Rubrica \u00e8 aperta. Chiudila e riprova." },
            { "The uninstaller could not be written.", "Non \u00e8 stato possibile scrivere il programma di disinstallazione." },
            { "The shortcut in the Start Menu could not be made.", "Non \u00e8 stato possibile creare il collegamento nel menu Start." },
            { "The shortcut on the desktop could not be made.", "Non \u00e8 stato possibile creare il collegamento sul desktop." },
            { "A shortcut called Rubrica in the Start Menu opens something else: it was left as it is.", "Nel menu Start c'\u00e8 gi\u00e0 un collegamento Rubrica che apre altro: \u00e8 stato lasciato com'\u00e8." },
            { "A shortcut called Rubrica on the desktop opens something else: it was left as it is.", "Sul desktop c'\u00e8 gi\u00e0 un collegamento Rubrica che apre altro: \u00e8 stato lasciato com'\u00e8." },
            { "But Windows would not start it: this PC may not allow programs in your own folders. Ask whoever looks after it.",
              "Ma Windows non l'ha avviata: questo PC potrebbe non permettere programmi nelle tue cartelle. Chiedi a chi se ne occupa." },
            { "It could not be listed in Settings > Apps.", "Non \u00e8 stato possibile elencarla in Impostazioni > App." },
            { "Windows does not allow writing there: the folder is protected.", "Windows non permette di scrivere l\u00ec: la cartella \u00e8 protetta." },
            { "The disk refused: it may be full, or a file may be open in another program.", "Il disco ha rifiutato: potrebbe essere pieno, o un file potrebbe essere aperto in un altro programma." },
            { "Windows did not say where this user's AppData folders are. Nothing was done.", "Windows non ha indicato dove sono le cartelle AppData di questo utente. Non \u00e8 stato fatto niente." },
            { "The uninstaller could not remove itself: delete the Rubrica folder in your Programs folder by hand.",
              "Il programma di disinstallazione non ha potuto eliminare se stesso: elimina a mano la cartella Rubrica nella cartella Programs." },
            { "The uninstaller could not be removed.", "Non \u00e8 stato possibile eliminare il programma di disinstallazione." },
            { "The Start Menu shortcut could not be removed.", "Non \u00e8 stato possibile eliminare il collegamento nel menu Start." },
            { "The desktop shortcut could not be removed.", "Non \u00e8 stato possibile eliminare il collegamento sul desktop." },
            { "The entry in Settings > Apps could not be removed.", "Non \u00e8 stato possibile togliere la voce da Impostazioni > App." },
            { "Your contacts book was left where it is.", "La tua rubrica \u00e8 rimasta dov'era." },
            { "Your contacts book went with it.", "Anche la tua rubrica \u00e8 stata eliminata." },
            { "Your contacts book could not be deleted: it is still where it was.", "Non \u00e8 stato possibile eliminare la rubrica: \u00e8 ancora dov'era." },
            { "Rubrica Setup stopped because of an error.\n\nDetails were saved to:\n{0}", "L'installazione di Rubrica si \u00e8 fermata per un errore.\n\nI dettagli sono stati salvati in:\n{0}" },
        };
    }
}
