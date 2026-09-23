namespace Rubrica
{
    /// The structural numbers of the program. Nothing else hard-codes them.
    static class Constants
    {
        public const int DesignWidth = 960;
        public const int DesignHeight = 720;

        public const int MaxCategories = 6;
        public const int MaxPhones = 3;
        public const int RecentMax = 24;
        public const int UndoSeconds = 10;

        public const int SchemaVersion = 1;

        /// No field is longer than this, however it arrives (typed, pasted, imported): a page
        /// has to be drawn with it. The notes get more room.
        public const int MaxFieldLength = 120;
        public const int MaxNotesLength = 2000;

        /// How long the window waits before writing state.xml (the recent look-ups): they change
        /// at every look-up and are disposable, so the disk is not touched for each one.
        public const int StateSaveSeconds = 30;

        /// IMPORT refuses anything larger: a few hundred contacts are some tens of KB.
        public const int MaxImportBytes = 4 * 1024 * 1024;

        /// Under %APPDATA%.
        public const string DataFolder = "Rubrica";

        /// Where the book used to be kept, up to version 0.1.0: moved into the folder above on
        /// the first start that finds it.
        public const string OldDataFolder = @"CardamomTools\Rubrica";
    }
}
