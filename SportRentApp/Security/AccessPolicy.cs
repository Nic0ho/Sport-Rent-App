using System;
using System.Data.Entity;
using System.Linq;

namespace SportRentApp
{
    public sealed class AccessPolicy
    {
        public UserRole Role { get; }
        public bool CanBrowse => Role == UserRole.Client || Role == UserRole.Kiosk || CanReadOperations;
        public bool CanReadOperations => Role == UserRole.Employee || Role == UserRole.Admin;
        public bool CanEditCatalog => Role == UserRole.Admin;

        public AccessPolicy(UserRole role)
        {
            Role = role;
        }

        internal static int CaptureAccount()
        {
            var user = UserSession.CurrentUser;
            if (user == null || user.Id <= 0)
                throw new UnauthorizedAccessException("Увійдіть до облікового запису.");
            return user.Id;
        }

        internal static AccessPolicy Read(ApplicationContext db, int accountId)
        {
            var account = (
                from u in db.Users.AsNoTracking()
                where u.Id == accountId
                select new
                {
                    u.Role,
                    u.Status
                }).SingleOrDefault();
            if (account == null || account.Status != "Active")
                throw new UnauthorizedAccessException(account?.Role == "Employee" && account.Status == "Pending" ? "Заявка працівника очікує підтвердження менеджера." : "Обліковий запис недоступний. Зверніться до менеджера.");
            string name = account.Role;
            UserRole role;
            if (name == null || !Enum.TryParse(name, out role) || !Enum.IsDefined(typeof(UserRole), role) || Enum.GetName(typeof(UserRole), role) != name)
                throw new UnauthorizedAccessException("Обліковий запис недоступний або його роль не підтримується.");
            var access = new AccessPolicy(role);
            if (!access.CanBrowse)
                throw new UnauthorizedAccessException("Доступ до системи не дозволено.");
            return access;
        }

        internal void RequireOperations()
        {
            if (!CanReadOperations)
                throw new UnauthorizedAccessException("Перегляд інвентарю та оренд доступний лише персоналу.");
        }

        internal void RequireEdit()
        {
            if (!CanEditCatalog)
                throw new UnauthorizedAccessException("Лише менеджер може змінювати каталог.");
        }
    }
}
