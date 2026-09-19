using System.Collections.Generic;

namespace Rubrica.Model
{
    sealed class Phone
    {
        public string Number = "";
        public string Label = "";

        public Phone(string number, string label)
        {
            Number = number;
            Label = label;
        }
    }

    sealed class Contact
    {
        public int Id;
        public int CategoryId;

        public string Name = "";
        public string Surname = "";     // empty for an organisation
        public string Role = "";
        public List<Phone> Phones = new List<Phone>();
        public string Email = "";
        public string Teams = "";       // the Teams account; never guessed from the email
        public bool Favorite;
        public string Notes = "";
    }
}
