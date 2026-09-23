using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Rubrica.Model
{
    enum SortBy { Name, Surname }

    sealed class Category
    {
        /// The six tab colours of the palette (the Tabs page offers them; an import
        /// that creates a tab takes the first one still free).
        public static readonly string[] Palette = { "#b8382a", "#367a3e", "#474790", "#7d4f22", "#2a7a78", "#4a5a70" };

        public int Id;
        public string Name = "";
        public string Colour = "#b8382a";
    }

    /// The whole book: what book.xml holds. Categories keep their order; contacts have
    /// none of their own and are sorted whenever they are shown.
    sealed class Book
    {
        public string Name = "";                // what the cover's plate says under CONTACTS, if anything
        public string Cover = "#2450a0";
        public SortBy SortBy = SortBy.Name;
        public int NextId = 1;

        public readonly List<Category> Categories = new List<Category>();
        public readonly List<Contact> Contacts = new List<Contact>();

        /// Ids are never reused, so a stale reference can never point at someone else.
        public int NewId()
        {
            return NextId++;
        }

        public Category CategoryOf(Contact contact)
        {
            return Categories.Find(c => c.Id == contact.CategoryId);
        }

        // ---- categories (the Tabs page) ----------------------------------------------------

        public bool CanAddCategory
        {
            get { return Categories.Count < Constants.MaxCategories; }
        }

        /// null when the book is full or the name is blank.
        public Category AddCategory(string name, string colour)
        {
            name = Clean.Line(name);
            if (!CanAddCategory || name.Length == 0) return null;
            var category = new Category { Id = NewId(), Name = name, Colour = colour };
            Categories.Add(category);
            return category;
        }

        /// A blank name is refused: the tab keeps the one it had.
        public void RenameCategory(Category category, string name)
        {
            name = Clean.Line(name);
            if (name.Length > 0) category.Name = name;
        }

        /// step: -1 towards the top of the binder, +1 towards the bottom. The ends stay put.
        public void MoveCategory(Category category, int step)
        {
            int from = Categories.IndexOf(category), to = from + step;
            if (from < 0 || to < 0 || to >= Categories.Count) return;
            Categories.RemoveAt(from);
            Categories.Insert(to, category);
        }

        /// Its contacts move to another category first: a contact always lives somewhere,
        /// which is also why the last category cannot be deleted. False when refused.
        public bool DeleteCategory(Category category, Category moveContactsTo)
        {
            if (Categories.Count <= 1 || moveContactsTo == null || moveContactsTo == category) return false;
            if (!Categories.Contains(category) || !Categories.Contains(moveContactsTo)) return false;

            foreach (Contact contact in Contacts)
                if (contact.CategoryId == category.Id) contact.CategoryId = moveContactsTo.Id;
            Categories.Remove(category);
            return true;
        }

        public int CountIn(Category category)
        {
            return Contacts.FindAll(c => c.CategoryId == category.Id).Count;
        }

        /// What is written on the page and what the order follows - the same string:
        /// "Mario Rossi" by name, "Rossi Mario" by surname. With no surname, as for an
        /// organisation, the name alone in both cases.
        public string DisplayName(Contact contact)
        {
            if (SortBy == SortBy.Surname && contact.Surname.Length > 0)
                return contact.Surname + " " + contact.Name;
            return (contact.Name + " " + contact.Surname).Trim();
        }

        /// The letter a contact is filed under: "E" for "Eric" and for an accented "E" alike,
        /// "#" for anything that does not start with a letter.
        public string Initial(Contact contact)
        {
            string name = DisplayName(contact);
            if (name.Length == 0 || char.IsSurrogate(name[0])) return "#";   // Normalize refuses half an emoji

            // Decomposing splits an accented letter into the plain letter plus its accent.
            char first = name.Substring(0, 1).Normalize(NormalizationForm.FormD)[0];
            return char.IsLetter(first) ? char.ToUpperInvariant(first).ToString() : "#";
        }

        public List<Contact> InCategory(int categoryId)
        {
            return Sorted(Contacts.FindAll(c => c.CategoryId == categoryId));
        }

        public List<Contact> Favorites()
        {
            return Sorted(Contacts.FindAll(c => c.Favorite));
        }

        /// Alphabetical, ignoring case and accents; the id settles ties so the order is stable.
        public List<Contact> Sorted(List<Contact> contacts)
        {
            var compare = CultureInfo.InvariantCulture.CompareInfo;
            const CompareOptions loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

            contacts.Sort((a, b) =>
            {
                int byName = compare.Compare(DisplayName(a), DisplayName(b), loose);
                return byName != 0 ? byName : a.Id.CompareTo(b.Id);
            });
            return contacts;
        }
    }
}
