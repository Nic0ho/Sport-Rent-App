using System;
using System.Linq;

namespace SportRentApp
{
    public static class StaffWork
    {
        public static readonly string[] Places =
        { "Каса", "Склад" };
        public static bool Has(string tags, string tag) => (tags ?? "").Split(',').Any(x => x.Trim() == tag);
        public static bool Matches(string role, string tags, string station) => role == "Admin" || (role == "Employee" && Places.Contains(station) && Has(tags, station));
        public static string Locations(string tags) => string.Join(", ", Places.Where(x => Has(tags, x)));
        public static void SetTags(User user, string tags)
        {
            bool changed = Locations(user.Tags) != Locations(tags);
            user.Tags = tags;
            if (changed)
                user.IsOnShift = user.Status == "Active" && (user.Role == "Admin" || (user.Role == "Employee" && Locations(tags).Length > 0));
        }

        public static void Start(User user)
        {
            if (user.Status != "Active" || (user.Role != "Admin" && (user.Role != "Employee" || Locations(user.Tags).Length == 0)))
                throw new InvalidOperationException("Спочатку оберіть «Каса» або «Склад» для активного працівника.");
            user.IsOnShift = true;
        }
    }
}
