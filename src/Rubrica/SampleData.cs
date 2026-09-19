using System;
using Rubrica.Model;

namespace Rubrica
{
    /// Generated books for development ("--sample design" and "--sample large").
    /// Nobody in here exists: the numbers use the unassigned 000 prefix and every
    /// address is at example.com. No real contact ever goes into the repository.
    static class SampleData
    {
        /// The placeholder book drawn on the design canvas, so a screen can be compared
        /// with its artboard pixel by pixel.
        public static void FillLikeTheDesign(Book book)
        {
            book.Categories.Clear();
            book.Contacts.Clear();
            book.Name = "[RADIO NAME]";   // what the canvas writes on the cover's plate
            Category one = AddCategory(book, "CATEGORY 1", "#b8382a");
            Category two = AddCategory(book, "CATEGORY 2", "#367a3e");
            AddCategory(book, "CATEGORY 3", "#474790");
            Category four = AddCategory(book, "CATEGORY 4", "#7d4f22");

            string[] names = { "ALPHA", "BRAVO", "CHARLIE", "DELTA", "ECHO", "FOXTROT", "GOLF", "HOTEL", "INDIA", "JULIETT", "KILO", "LIMA", "MIKE" };
            string[] firstLabel = { "OFFICE", "OFFICE", "MOBILE", "OFFICE", "OFFICE", "MOBILE", "OFFICE", "OFFICE", "MOBILE", "OFFICE", "OFFICE", "MOBILE", "OFFICE" };
            string[] secondLabel = { "MOBILE", null, null, "STUDIO", null, null, "MOBILE", null, null, null, null, null, "MOBILE" };
            bool[] favorite = { true, false, true, false, false, false, true, false, false, false, true, false, true };

            for (int i = 0; i < names.Length; i++)
            {
                var contact = new Contact
                {
                    Id = book.NewId(),
                    CategoryId = i < 10 ? one.Id : two.Id,
                    Name = names[i],
                    Role = "ROLE / ORG",
                    Email = names[i].ToLowerInvariant() + "@example.com",
                    Teams = names[i].ToLowerInvariant() + "@example.com",
                    Favorite = favorite[i],
                    Notes = i == 0 ? "[NOTES]" : "",
                };
                contact.Phones.Add(new Phone("+39 000 000 " + (i + 1).ToString("0000"), firstLabel[i]));
                if (secondLabel[i] != null)
                    contact.Phones.Add(new Phone("+39 000 000 " + (i + 101).ToString("0000"), secondLabel[i]));
                book.Contacts.Add(contact);
            }

            // The "Manage tabs" artboard says CATEGORY 4 holds three contacts, without naming them.
            foreach (string name in new[] { "NOVEMBER", "OSCAR", "PAPA" })
            {
                var contact = new Contact { Id = book.NewId(), CategoryId = four.Id, Name = name, Role = "ROLE / ORG", Email = name.ToLowerInvariant() + "@example.com" };
                contact.Phones.Add(new Phone("+39 000 000 02" + (book.Contacts.Count % 100).ToString("00"), "OFFICE"));
                book.Contacts.Add(contact);
            }
        }

        /// About sixty made-up people and organisations: enough to turn pages, with accents,
        /// double surnames and half-empty entries to exercise sorting and layout.
        public static void FillLarge(Book book)
        {
            book.Categories.Clear();
            book.Contacts.Clear();
            Category[] categories =
            {
                AddCategory(book, "GUESTS", "#b8382a"),
                AddCategory(book, "PRESS", "#367a3e"),
                AddCategory(book, "STAFF", "#474790"),
                AddCategory(book, "SERVICES", "#7d4f22"),
            };

            string[] first = { "Anna", "Bruno", "Carla", "Dario", "Elena", "Fabio", "Giulia", "Ivan", "Lucia", "Marco",
                               "Nicol\u00f2", "Olga", "Paolo", "Rita", "Sara", "Tommaso", "Ugo", "Vera", "Maria Grazia", "Pier Paolo" };
            string[] last = { "Amato", "Bianchi", "Conti", "D'Angelo", "De Luca", "Esposito", "Ferri", "Gallo", "Dalla Chiesa", "Leone",
                              "Marino", "Neri", "Orlando", "Pace", "Riva", "Serra", "Testa", "Villa", "Zanetti", "\u00c8rcole" };
            string[] roles = { "Journalist", "Press office", "Councillor", "Musician", "Author", "Sound engineer",
                               "Producer", "Presenter", "Promoter", "Photographer" };
            string[] organisations = { "Taxi Centrale", "City Hall - Press Office", "Police - Control Room", "Theatre Box Office",
                                       "Studio Maintenance", "Courier Express", "Weather Service", "Traffic Info Line" };

            var random = new Random(7);   // fixed seed: the same book every time
            for (int i = 0; i < 52; i++)
            {
                var contact = new Contact
                {
                    Id = book.NewId(),
                    CategoryId = categories[random.Next(3)].Id,
                    Name = first[random.Next(first.Length)],
                    Surname = last[random.Next(last.Length)],
                    Role = roles[random.Next(roles.Length)],
                    Favorite = random.Next(8) == 0,
                };
                FillChannels(contact, random, i);
                book.Contacts.Add(contact);
            }
            for (int i = 0; i < organisations.Length; i++)
            {
                var contact = new Contact { Id = book.NewId(), CategoryId = categories[3].Id, Name = organisations[i], Favorite = i == 0 };
                FillChannels(contact, random, 100 + i);
                book.Contacts.Add(contact);
            }
        }

        static void FillChannels(Contact contact, Random random, int serial)
        {
            string[] labels = { "OFFICE", "MOBILE", "STUDIO", "HOME" };
            int phones = 1 + random.Next(3) / 2 + (random.Next(10) == 0 ? 1 : 0);   // mostly one, sometimes two or three
            for (int p = 0; p < phones; p++)
                contact.Phones.Add(new Phone("+39 000 000 " + (serial * 3 + p + 1).ToString("0000"), labels[(serial + p) % labels.Length]));

            string mailbox = (contact.Name + "." + contact.Surname).Trim('.').ToLowerInvariant()
                .Replace(' ', '.').Replace("'", "").Replace("\u00f2", "o").Replace("\u00e8", "e");
            if (random.Next(5) != 0) contact.Email = mailbox + "@example.com";
            if (random.Next(3) == 0) contact.Teams = mailbox + "@example.com";
        }

        static Category AddCategory(Book book, string name, string colour)
        {
            var category = new Category { Id = book.NewId(), Name = name, Colour = colour };
            book.Categories.Add(category);
            return category;
        }
    }
}
