using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace SportRentApp
{
    public sealed class ShopService
    {
        public static readonly string[] AccountRoles =
        { "Client", "Employee", "Kiosk", "Admin" };
        public static readonly string[] AccountStates =
        { "Pending", "Active", "Rejected", "Blocked" };
        public static readonly string[] ClientTagNames =
        { "Порушення правил", "Бан" };
        public static string[] AllowedTags(string role) => role == "Client" ? ClientTagNames : role == "Kiosk" ? new[] { "Кіоск" }

        : TagNames;
        public static readonly string[] TagNames =
        { "Стажер", "Склад", "Каса" };
        public static readonly string[] TransactionKinds =
        { "Оплата", "Застава", "Повернення коштів", "Повернення застави", "Прострочене повернення", "Компенсація", "Штраф" };
        private static string Now => DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

        public static bool NeedsSetup()
        {
            using (var db = new ApplicationContext())
                return !db.Users.Any(u => u.Role == "Admin" && u.Status == "Active");
        }

        public Task CompleteSetupAsync(string login, string password, string name, string phone) => Task.Run(() =>
        {
            phone = NormalizePhone(phone);
            login = (login ?? "").Trim();
            name = (name ?? "").Trim();
            string error = InputRules.LoginError(login) ?? InputRules.NameError(name);
            if (error != null)
                throw new ArgumentException(error);
            string hash = PasswordHash.Create(password);
            using (var db = new ApplicationContext())
            using (var tx = db.Database.BeginTransaction())
            {
                if (db.Users.Any(u => u.Role == "Admin" && u.Status == "Active"))
                    throw new InvalidOperationException("Першого менеджера вже створено.");
                var bootstrap = db.Users.SingleOrDefault(x => x.Role == "Bootstrap");
                if (db.Users.Any(x => x.Username.ToLower() == login.ToLower()))
                    throw new ArgumentException("Логін уже існує.");
                var manager = new User(login, hash, name, phone, "Admin")
                {
                    CreatedAt = Now
                };
                db.Users.Add(manager);
                db.SaveChanges();
                manager.ApprovedBy = manager.Id;
                manager.UpdatedAt = Now;
                if (bootstrap != null)
                    db.Users.Remove(bootstrap);
                db.Record(manager.Id, "Магазин налаштовано", "Перший менеджер");
                db.SaveChanges();
                tx.Commit();
            }
        });
        private static Task<T> Run<T>(Func<ApplicationContext, int, AccessPolicy, T> work)
        {
            int actor = AccessPolicy.CaptureAccount();
            return Task.Run(() =>
            {
                using (var db = new ApplicationContext())
                    return work(db, actor, AccessPolicy.Read(db, actor));
            });
        }

        public static string NormalizePhone(string phone) => InputRules.NormalizePhone(phone);
        public Task RegisterAsync(string login, string password, string name, string phone)
        {
            login = (login ?? "").Trim();
            name = (name ?? "").Trim();
            phone = NormalizePhone(phone);
            string error = InputRules.LoginError(login) ?? InputRules.NameError(name);
            if (error != null)
                throw new ArgumentException(error);
            return Task.Run(() =>
            {
                string hash = PasswordHash.Create(password);
                using (var db = new ApplicationContext())
                using (var tx = db.Database.BeginTransaction())
                {
                    if (db.Users.Any(x => x.Username.ToLower() == login.ToLower()))
                        throw new InvalidOperationException("Цей логін уже використовується.");
                    var user = new User(login, hash, name, phone, "Employee")
                    {
                        CreatedAt = Now,
                        Status = "Pending",
                        IsOnShift = false,
                        UpdatedAt = Now
                    };
                    db.Users.Add(user);
                    db.SaveChanges();
                    db.Record(user.Id, "Реєстрація", "Працівник · очікує");
                    db.SaveChanges();
                    tx.Commit();
                }
            });
        }

        public Task<User> SignInAsync(string login, string password, bool kioskOnly = false) => Task.Run(() =>
        {
            using (var db = new ApplicationContext())
            {
                var user = db.Users.SingleOrDefault(x => x.Username == login && (!kioskOnly || x.Role == "Kiosk"));
                if (user == null && kioskOnly && InputRules.StoreNumber(login, out int terminalId))
                    user = db.Users.SingleOrDefault(x => x.Id == terminalId && x.Role == "Kiosk");
                if (user == null || (kioskOnly ? user.Role != "Kiosk" : user.Role != "Employee" && user.Role != "Admin") || !PasswordHash.Verify(password, user.Password))
                    return null;
                AccessPolicy.Read(db, user.Id);
                if (kioskOnly && string.IsNullOrWhiteSpace(user.ShopCode))
                    return null;
                if (PasswordHash.IsLegacy(user.Password))
                {
                    string hash = PasswordHash.CreateForMigration(password);
                    int changed = db.Database.ExecuteSqlCommand("UPDATE Users SET Password=@p0 WHERE Id=@p1 AND Password=@p2", hash, user.Id, user.Password);
                    if (changed != 1)
                        return null;
                }

                db.Record(user.Id, "Вхід", user.Role);
                db.SaveChanges();
                return new User
                {
                    Id = user.Id,
                    Username = user.Username,
                    FullName = user.FullName,
                    Phone = user.Phone,
                    Role = user.Role,
                    ShopCode = user.ShopCode
                };
            }
        });
        public static string GenerateTerminalCode()
        {
            var bytes = new byte[18];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_');
        }

        private static IQueryable<AccountView> AccountQuery(ApplicationContext db, int actor, AccessPolicy policy)
        {
            var query = db.Users.AsNoTracking().Where(x => x.Status != "Deleted");
            if (!policy.CanEditCatalog)
                query = query.Where(x => x.Status == "Active");
            return query.Select(x => new AccountView {
                Id = x.Id,
                FullName = x.FullName,
                Role = x.Role,
                Tags = x.Tags,
                Status = x.Status,
                Version = x.Version,
                PrivateVisible = policy.CanEditCatalog || x.Id == actor,
                Username = policy.CanEditCatalog || x.Id == actor ? x.Username : null,
                Phone = policy.CanEditCatalog || x.Id == actor ? x.Phone : null,
                ShopCode = x.Role == "Kiosk" ? x.ShopCode : null,
                Notes = policy.CanEditCatalog || x.Id == actor ? x.Notes : null
            });
        }

        public Task<AccountView> AccountAsync(int id) => Run((db, actor, policy) =>
        {
            if (id != actor)
                policy.RequireOperations();
            return AccountQuery(db, actor, policy).Single(x => x.Id == id);
        });
        public Task<List<AccountView>> AccountsAsync(bool clients) => Run((db, actor, policy) =>
        {
            policy.RequireOperations();
            return AccountQuery(db, actor, policy).Where(x => clients ? x.Role == "Client" : (x.Role == "Employee" || x.Role == "Admin" || x.Role == "Kiosk")).OrderBy(x => x.FullName).ToList();
        });
        public Task<int> SaveAccountAsync(AccountView input, string newPassword) => Run((db, actor, policy) =>
        {
            policy.RequireEdit();
            if (input == null || !AccountRoles.Contains(input.Role) || !AccountStates.Contains(input.Status) || InputRules.NameError(input.FullName) != null)
                throw new ArgumentException("Перевірте ім’я, роль та стан.");
            var tags = (input.Tags ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToArray();
            if (tags.Any(x => !AllowedTags(input.Role).Contains(x)))
                throw new ArgumentException("Ці теги недоступні для обраної ролі.");
            if (input.Id == actor && (input.Role != "Admin" || input.Status != "Active"))
                throw new InvalidOperationException("Не можна вимкнути власний менеджерський доступ.");
            string phone = input.Role == "Kiosk" ? "" : NormalizePhone(input.Phone);
            string updatedLogin = (input.Username ?? "").Trim();
            if (InputRules.LoginError(updatedLogin) != null)
                throw new ArgumentException(InputRules.LoginError(updatedLogin));
            if (input.Role == "Kiosk")
            {
                if (!InputRules.StoreNumber(input.ShopCode, out int number))
                    throw new ArgumentException("Введіть номер магазину, наприклад 1.");
                input.ShopCode = number.ToString(CultureInfo.InvariantCulture);
            }

            using (var tx = db.Database.BeginTransaction())
            {
                User user;
                AccessPolicy.Read(db, actor).RequireEdit();
                if (input.Id == 0)
                {
                    if (input.Role != "Employee" && input.Role != "Kiosk")
                        throw new InvalidOperationException("Нові клієнти подають реєстрацію; тут створюються працівники та кіоски.");
                    string login = (input.Username ?? "").Trim();
                    if (db.Users.Any(x => x.Username.ToLower() == login.ToLower()))
                        throw new ArgumentException("Логін має бути унікальним (5–80 символів).");
                    user = new User(login, PasswordHash.Create(newPassword), input.FullName.Trim(), phone, input.Role)
                    {
                        CreatedAt = Now
                    };
                    db.Users.Add(user);
                }
                else
                {
                    user = db.Users.Single(x => x.Id == input.Id);
                    if ((user.Role == "Kiosk") != (input.Role == "Kiosk"))
                        throw new ArgumentException("Термінал і працівник — окремі типи акаунтів. Створіть потрібний акаунт окремо.");
                    if (user.Status == "Deleted")
                        throw new InvalidOperationException("Акаунт видалено.");
                    if (user.Role == "Admin" && user.Status == "Active" && (input.Role != "Admin" || input.Status != "Active") && !db.Users.Any(u => u.Id != input.Id && u.Role == "Admin" && u.Status == "Active"))
                        throw new InvalidOperationException("Має залишитися щонайменше один активний менеджер.");
                    if (db.Users.Any(x => x.Id != input.Id && x.Username.ToLower() == updatedLogin.ToLower()))
                        throw new ArgumentException("Логін уже існує.");
                    if (user.Version != input.Version)
                        throw new InvalidOperationException("Обліковий запис уже змінено. Оновіть список.");
                    if (!string.IsNullOrEmpty(newPassword))
                        throw new UnauthorizedAccessException("Для зміни пароля використайте окрему дію.");
                }

                if (input.Role == "Client")
                {
                    bool wasBanned = user.Role == "Client" && (user.Status == "Blocked" || (user.Tags ?? "").Split(',').Any(x => x.Trim() == "Бан"));
                    if (wasBanned && input.Status != user.Status && input.Status != "Blocked")
                        tags = tags.Where(x => x != "Бан").ToArray();
                    else if (wasBanned && !tags.Contains("Бан") && input.Status == "Blocked")
                        input.Status = "Active";
                    if (tags.Contains("Бан"))
                        input.Status = "Blocked";
                    else if (input.Status == "Blocked")
                        tags = tags.Concat(new[] { "Бан" }).ToArray();
                }

                user.Username = updatedLogin;
                user.FullName = input.FullName.Trim();
                user.Phone = phone;
                user.Notes = input.Notes;
                user.Role = input.Role;
                user.Status = input.Status;
                StaffWork.SetTags(user, string.Join(", ", tags));
                user.ShopCode = input.Role == "Kiosk" ? input.ShopCode?.Trim() : null;
                user.Version++;
                user.UpdatedAt = Now;
                if (input.Role != "Employee" && input.Role != "Admin")
                {
                    user.PhotoPath = null;
                    user.Bio = null;
                    user.IsOnShift = false;
                }

                if (input.Status == "Active")
                    user.ApprovedBy = actor;
                db.SaveChanges();
                db.Record(actor, input.Id == 0 ? "Акаунт створено" : "Доступ змінено", "Користувач #" + user.Id, user.Role + " · " + user.Status);
                db.SaveChanges();
                tx.Commit();
                return user.Id;
            }
        });
        public Task DeleteEmployeeAsync(int accountId, int version) => Run((db, actor, policy) =>
        {
            policy.RequireEdit();
            using (var tx = db.Database.BeginTransaction())
            {
                AccessPolicy.Read(db, actor).RequireEdit();
                var user = db.Users.Single(x => x.Id == accountId);
                if (accountId == actor || user.Role != "Employee")
                    throw new UnauthorizedAccessException("Видаляти можна лише акаунти працівників.");
                if (user.Version != version || user.Status == "Deleted")
                    throw new InvalidOperationException("Акаунт уже змінено. Оновіть список.");
                user.Status = "Deleted";
                user.Version++;
                user.UpdatedAt = Now;
                db.Record(actor, "Працівника видалено", "Користувач #" + accountId);
                db.SaveChanges();
                tx.Commit();
                return true;
            }
        });
        public Task ChangePasswordAsync(int accountId, string currentPassword, string newPassword) => Run((db, actor, policy) =>
        {
            var original = db.Users.AsNoTracking().Single(x => x.Id == accountId);
            if (accountId == actor)
            {
                if (original.Role == "Kiosk")
                    throw new UnauthorizedAccessException("Пароль кіоска змінює менеджер.");
                if (!PasswordHash.Verify(currentPassword, original.Password))
                    throw new ArgumentException("Поточний пароль неправильний.");
            }
            else
            {
                policy.RequireEdit();
                if (original.Role != "Employee" && original.Role != "Kiosk")
                    throw new UnauthorizedAccessException("Менеджер може скидати паролі лише працівників і кіосків.");
            }

            string hash = PasswordHash.Create(newPassword);
            using (var tx = db.Database.BeginTransaction())
            {
                var currentAccess = AccessPolicy.Read(db, actor);
                if (accountId != actor)
                    currentAccess.RequireEdit();
                var user = db.Users.Single(x => x.Id == accountId);
                if (user.Password != original.Password || user.Role != original.Role)
                    throw new InvalidOperationException("Акаунт уже змінено. Повторіть спробу.");
                user.Password = hash;
                db.Record(actor, "Пароль змінено", "Користувач #" + accountId);
                db.SaveChanges();
                tx.Commit();
                return true;
            }
        });
        public Task<int> RequestEquipmentChangeAsync(int equipmentId, string message) => Run((db, actor, policy) =>
        {
            policy.RequireOperations();
            message = (message ?? "").Trim();
            if (message.Length < 5 || message.Length > 2000 || !db.Equipment.Any(x => x.Id == equipmentId && x.Status != "Deleted"))
                throw new ArgumentException("Оберіть товар і опишіть зміну (5–2000 символів).");
            var request = new EquipmentRequest
            {
                EquipmentId = equipmentId,
                RequesterId = actor,
                Message = message,
                CreatedAt = Now
            };
            db.EquipmentChangeRequests.Add(request);
            db.Record(actor, "Запит на зміну товару", "Спорядження #" + equipmentId);
            db.SaveChanges();
            return request.Id;
        });
        public Task<List<EquipmentRequest>> RequestsAsync() => Run((db, actor, policy) =>
        {
            policy.RequireOperations();
            return db.EquipmentChangeRequests.AsNoTracking().Where(x => policy.CanEditCatalog || x.RequesterId == actor).OrderByDescending(x => x.Id).ToList();
        });
        private static void RequireCashier(ApplicationContext db, int actor, AccessPolicy access)
        {
            access.RequireOperations();
            var user = db.Users.Single(x => x.Id == actor);
            if (!access.CanEditCatalog && (!user.IsOnShift || !StaffWork.Has(user.Tags, "Каса")))
                throw new UnauthorizedAccessException("Оформлення доступне касиру або менеджеру.");
        }

        public Task<int> RegisterClientAsync(string name, string number) => Run((db, actor, policy) =>
        {
            RequireCashier(db, actor, policy);
            string phone = NormalizePhone(number);
            string error = InputRules.NameError(name);
            if (error != null)
                throw new ArgumentException(error);
            using (var tx = db.Database.BeginTransaction())
            {
                RequireCashier(db, actor, AccessPolicy.Read(db, actor));
                if (db.Users.Any(x => x.Role == "Client" && x.Phone == phone))
                    throw new InvalidOperationException("Клієнт із цим номером уже зареєстрований. Знайдіть його в списку клієнтів.");
                var client = new User("client-" + Guid.NewGuid().ToString("N"), PasswordHash.Create(Guid.NewGuid().ToString("N")), name.Trim(), phone, "Client")
                {
                    IsOnShift = false,
                    CreatedAt = Now
                };
                db.Users.Add(client);
                db.SaveChanges();
                db.Record(actor, "Клієнта зареєстровано", "Користувач #" + client.Id);
                db.SaveChanges();
                tx.Commit();
                return client.Id;
            }
        });
        public Task FlagReturnAsync(int id, int version, string note) => Run((db, actor, policy) =>
        {
            RequireCashier(db, actor, policy);
            note = (note ?? "").Trim();
            if (note.Length < 5 || note.Length > 2000)
                throw new ArgumentException("Опишіть причину звернення до менеджера: 5–2000 символів.");
            using (var tx = db.Database.BeginTransaction())
            {
                RequireCashier(db, actor, AccessPolicy.Read(db, actor));
                Rental order = db.Rentals.Single(x => x.Id == id);
                if (order.Status != "Active" || order.Version != version)
                    throw new InvalidOperationException("Замовлення вже змінено. Оновіть список.");
                order.RequiresAdmin = 1;
                order.ConditionNotes = note;
                order.Version++;
                db.Record(actor, "Повернення передано менеджеру", order.OrderNumber, note);
                db.SaveChanges();
                tx.Commit();
                return true;
            }
        });
        public Task<int> SaveOrderAsync(Rental value, Rental original) => Run((db, actor, policy) =>
        {
            RequireCashier(db, actor, policy);
            if (value == null || (original != null && value.Id != original.Id))
                throw new ArgumentException("Некоректний запис замовлення.");
            DateTime start, end;
            if (!DateTime.TryParseExact(value.RentDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out start) || !DateTime.TryParseExact(value.ReturnDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out end) || end < start)
                throw new ArgumentException("Період прокату: дати РРРР-ММ-ДД, повернення не раніше початку.");
            if (!new[] { "Online", "WalkIn" }.Contains(value.QueueType) || !InputRules.IsMoney(value.Deposit) || !InputRules.IsMoney(value.PenaltyFee))
                throw new ArgumentException("Перевірте тип черги, заставу та штраф.");
            using (var tx = db.Database.BeginTransaction())
            {
                RequireCashier(db, actor, AccessPolicy.Read(db, actor));
                var client = db.Users.SingleOrDefault(x => x.Id == value.UserId && x.Role == "Client" && x.Status == "Active");
                var equipment = db.Equipment.SingleOrDefault(x => x.Id == value.EquipmentId && x.Status != "Deleted");
                if (client == null || equipment == null)
                    throw new ArgumentException("Оберіть активного клієнта та спорядження.");
                Rental target;
                if (original == null)
                {
                    if (value.Id != 0)
                        throw new ArgumentException("Некоректний номер замовлення.");
                    target = new Rental
                    {
                        CreatedAt = Now,
                        Status = "Pending"
                    };
                    db.Rentals.Add(target);
                }
                else
                {
                    target = db.Rentals.Single(x => x.Id == original.Id);
                    if (target.Version != original.Version)
                        throw new InvalidOperationException("Замовлення вже змінено. Оновіть дані.");
                    if (!new[] { "Pending", "Called", "Serving" }.Contains(target.Status))
                        throw new InvalidOperationException("Дані можна змінювати до початку підготовки.");
                    if (target.UserId != value.UserId && db.ShopTransactions.Any(x => x.OrderId == target.Id))
                        throw new InvalidOperationException("Не можна змінити клієнта замовлення з пов’язаними операціями.");
                }

                RentalRules.RequireReservation(db, equipment, value.RentDate, value.ReturnDate, target.Id);
                double price = RentalRules.Price(equipment.PricePerDay, value.RentDate, value.ReturnDate);
                double total = (double)((decimal)price + (decimal)value.PenaltyFee);
                if (!InputRules.IsMoney(price) || !InputRules.IsMoney(total))
                    throw new ArgumentException("Вартість замовлення перевищує допустиму суму.");
                target.UserId = client.Id;
                target.EquipmentId = equipment.Id;
                target.QueueType = target.KioskId.HasValue ? "Online" : value.QueueType;
                target.RentDate = value.RentDate;
                target.ReturnDate = value.ReturnDate;
                target.RentalPrice = price;
                target.Deposit = value.Deposit;
                target.PenaltyFee = value.PenaltyFee;
                target.TotalPrice = total;
                target.ConditionNotes = value.ConditionNotes;
                target.AdminNotes = value.AdminNotes;
                target.RequiresAdmin = value.RequiresAdmin == 1 ? 1 : 0;
                target.Version++;
                db.SaveChanges();
                if (string.IsNullOrWhiteSpace(target.OrderNumber))
                {
                    target.OrderNumber = "SD-" + target.Id.ToString("D6");
                    db.SaveChanges();
                }

                db.Record(actor, original == null ? "Замовлення створено" : "Замовлення змінено", target.OrderNumber);
                db.SaveChanges();
                tx.Commit();
                return target.Id;
            }
        });
        public Task<List<CustomerOrder>> OrdersForClientAsync(int clientId) => Run((db, actor, policy) =>
        {
            policy.RequireOperations();
            return CustomerOrder.ForClient(db, clientId).ToList();
        });
        private static void AddMovement(ApplicationContext db, Rental order, int actor, string kind, long amount, int? parent = null)
        {
            if (amount <= 0)
                return;
            if (!order.UserId.HasValue)
                throw new InvalidOperationException("Вкажіть клієнта перед розрахунком.");
            db.ShopTransactions.Add(new ShopTransaction {
                ClientId = order.UserId.Value,
                OrderId = order.Id,
                CreatedBy = actor,
                Kind = kind,
                AmountMinor = amount,
                RelatedTransactionId = parent,
                CreatedAt = Now,
                RequestKey = Guid.NewGuid().ToString()
            });
        }

        private static long ToMinor(double value) => decimal.ToInt64(decimal.Round((decimal)value * 100m, 0));
        internal static long Balance(List<ShopTransaction> rows, string kind, string refund = null) => rows.Where(x => x.Kind == kind).Sum(x => x.AmountMinor) - rows.Where(x => refund != null && x.Kind == refund).Sum(x => x.AmountMinor);
        private static void CollectDue(ApplicationContext db, Rental order, int actor, List<ShopTransaction> ledger, string kind, string refund, double target)
        {
            long due = ToMinor(target) - Balance(ledger, kind, refund);
            if (due < 0)
                throw new InvalidOperationException("У журналі є переплата. Менеджер має звірити операції.");
            AddMovement(db, order, actor, kind, due);
        }

        private static void RefundPayments(ApplicationContext db, Rental order, int actor, List<ShopTransaction> ledger, string kind, string refund)
        {
            foreach (var payment in ledger.Where(x => x.Kind == kind))
            {
                long remaining = payment.AmountMinor - ledger.Where(x => x.RelatedTransactionId == payment.Id && x.Kind == refund).Sum(x => x.AmountMinor);
                AddMovement(db, order, actor, refund, remaining, payment.Id);
            }
        }

        public Task<List<ShopTransaction>> OrderLedgerAsync(int orderId) => Run((db, actor, policy) =>
        {
            RequireCashier(db, actor, policy);
            return db.ShopTransactions.AsNoTracking().Where(x => x.OrderId == orderId).ToList();
        });
        public Task AdvanceOrderAsync(int id, int version, string next, double? returnPenalty = null, double? deposit = null, string condition = null) => Run((db, actor, policy) =>
        {
            policy.RequireOperations();
            using (var tx = db.Database.BeginTransaction())
            {
                policy = AccessPolicy.Read(db, actor);
                policy.RequireOperations();
                var order = db.Rentals.Single(x => x.Id == id);
                if (order.Version != version)
                    throw new InvalidOperationException("Замовлення вже змінено. Оновіть список.");
                bool valid = (order.Status == "Pending" && next == "Called") || (order.Status == "Called" && next == "Serving") || (new[] { "Pending", "Called", "Serving" }.Contains(order.Status) && next == "Assembling") || (order.Status == "Assembling" && next == "Ready") || (order.Status == "Ready" && next == "Active") || (order.Status == "Active" && next == "Completed") || (new[] { "Pending", "Called", "Serving", "Assembling", "Ready" }.Contains(order.Status) && next == "Cancelled");
                if (next == "Ready")
                {
                    var staff = db.Users.Single(x => x.Id == actor);
                    if (!policy.CanEditCatalog && (!staff.IsOnShift || !StaffWork.Has(staff.Tags, "Склад")))
                        throw new UnauthorizedAccessException("Підготовку підтверджує працівник складу.");
                }
                else
                    RequireCashier(db, actor, AccessPolicy.Read(db, actor));
                if (!valid)
                    throw new InvalidOperationException("Ця дія недоступна для поточного стану.");
                if (next == "Completed" && order.RequiresAdmin == 1 && !policy.CanEditCatalog)
                    throw new UnauthorizedAccessException("Це повернення має перевірити менеджер.");
                Equipment equipment = null;
                if (new[] { "Assembling", "Ready", "Active", "Completed" }.Contains(next))
                {
                    if (!order.UserId.HasValue || !order.EquipmentId.HasValue || string.IsNullOrEmpty(order.RentDate) || string.IsNullOrEmpty(order.ReturnDate))
                        throw new InvalidOperationException("Спочатку заповніть клієнта, спорядження та період оренди.");
                    equipment = db.Equipment.Single(x => x.Id == order.EquipmentId);
                    if (next != "Completed")
                        RentalRules.RequireReservation(db, equipment, order.RentDate, order.ReturnDate, order.Id);
                    if (next != "Completed" && (equipment.Status != "Available" || db.Rentals.Any(x => x.Id != id && x.EquipmentId == order.EquipmentId && (x.Status == "Assembling" || x.Status == "Ready" || x.Status == "Active"))))
                        throw new InvalidOperationException("Це спорядження вже зайняте або недоступне.");
                }

                var ledger = db.ShopTransactions.Where(x => x.OrderId == id).ToList();
                if (deposit.HasValue)
                {
                    if (next != "Assembling" || !InputRules.IsMoney(deposit.Value))
                        throw new ArgumentException("Перевірте суму застави.");
                    order.Deposit = deposit.Value;
                }

                if (next == "Assembling")
                {
                    CollectDue(db, order, actor, ledger, "Оплата", "Повернення коштів", order.RentalPrice);
                    CollectDue(db, order, actor, ledger, "Застава", "Повернення застави", order.Deposit);
                }

                if (next == "Cancelled")
                {
                    RefundPayments(db, order, actor, ledger, "Оплата", "Повернення коштів");
                    RefundPayments(db, order, actor, ledger, "Застава", "Повернення застави");
                }

                if (next == "Active")
                {
                    if (DateTime.ParseExact(order.RentDate, "yyyy-MM-dd", CultureInfo.InvariantCulture).Date > DateTime.Today)
                        throw new InvalidOperationException("Дата початку оренди ще не настала.");
                    CollectDue(db, order, actor, ledger, "Оплата", "Повернення коштів", order.RentalPrice);
                    CollectDue(db, order, actor, ledger, "Застава", "Повернення застави", order.Deposit);
                    equipment.Status = "Rented";
                }

                if (returnPenalty.HasValue)
                {
                    policy.RequireEdit();
                    if (next != "Completed" || !InputRules.IsMoney(returnPenalty.Value) || !InputRules.IsMoney(order.RentalPrice + returnPenalty.Value))
                        throw new ArgumentException("Перевірте штраф при поверненні.");
                    order.PenaltyFee = returnPenalty.Value;
                    order.TotalPrice = (double)((decimal)order.RentalPrice + (decimal)returnPenalty.Value);
                }

                if (next == "Completed")
                {
                    CollectDue(db, order, actor, ledger, "Штраф", null, order.PenaltyFee);
                    RefundPayments(db, order, actor, ledger, "Застава", "Повернення застави");
                    order.ActualReturnDate = DateTime.Today.ToString("yyyy-MM-dd");
                    equipment.Status = db.Rentals.Any(x => x.Id != id && x.EquipmentId == order.EquipmentId && x.Status == "Active") ? "Rented" : "Available";
                    order.RequiresAdmin = 0;
                    foreach (WorkTask request in db.WorkTasks.Where(x => x.Kind == "Return" && x.EntityId == id && x.Status == "Open"))
                        request.Status = "Done";
                }

                if (condition != null)
                {
                    if (next != "Completed" || !policy.CanEditCatalog || (condition != "Пошкоджено" && condition != "Втрачено"))
                        throw new ArgumentException("Перевірте стан спорядження.");
                    order.ConditionNotes = condition;
                    equipment.Status = condition == "Пошкоджено" ? "Maintenance" : "Lost";
                }

                order.Status = next;
                order.Version++;
                db.Record(actor, "Стан замовлення змінено", "Замовлення #" + id, next);
                db.SaveChanges();
                tx.Commit();
                return true;
            }
        });
        public Task ReviewRequestAsync(int id, int version, bool accepted, string note) => Run((db, actor, policy) =>
        {
            policy.RequireEdit();
            var request = db.EquipmentChangeRequests.Single(x => x.Id == id);
            if (request.Version != version || request.Status != "Pending")
                throw new InvalidOperationException("Заявку вже розглянуто.");
            if (string.IsNullOrWhiteSpace(note))
                throw new ArgumentException("Додайте пояснення рішення.");
            request.Status = accepted ? "Accepted" : "Rejected";
            request.Resolution = note.Trim();
            request.ReviewedBy = actor;
            request.Version++;
            db.Record(actor, "Запит розглянуто", "Запит #" + id, accepted ? "Прийнято" : "Відхилено");
            db.SaveChanges();
            return true;
        });
        public Task<List<ShopTransaction>> TransactionsAsync(int? clientId = null) => Run((db, actor, policy) =>
        {
            if (!policy.CanEditCatalog && policy.Role != UserRole.Client)
                throw new UnauthorizedAccessException("Фінансовий журнал доступний менеджеру.");
            var query = db.ShopTransactions.AsNoTracking().AsQueryable();
            if (!policy.CanEditCatalog)
                query = query.Where(x => x.ClientId == actor);
            else if (clientId.HasValue)
                query = query.Where(x => x.ClientId == clientId.Value);
            return query.OrderByDescending(x => x.Id).ToList();
        });
        public Task<int> AddDemoTransactionAsync(ShopTransaction input) => Run((db, actor, policy) =>
        {
            policy.RequireEdit();
            if (input == null || !TransactionKinds.Contains(input.Kind) || input.AmountMinor <= 0 || input.AmountMinor > 100000000 || !Guid.TryParse(input.RequestKey, out var key))
                throw new ArgumentException("Перевірте тип, суму та ідентифікатор операції.");
            using (var tx = db.Database.BeginTransaction())
            {
                AccessPolicy.Read(db, actor).RequireEdit();
                var existing = db.ShopTransactions.SingleOrDefault(x => x.RequestKey == input.RequestKey);
                if (existing != null)
                {
                    if (existing.CreatedBy != actor)
                        throw new UnauthorizedAccessException();
                    if (existing.ClientId != input.ClientId || existing.OrderId != input.OrderId || existing.RelatedTransactionId != input.RelatedTransactionId || existing.Kind != input.Kind || existing.AmountMinor != input.AmountMinor || existing.Notes != input.Notes)
                        throw new InvalidOperationException("Ключ операції вже використаний з іншими даними.");
                    return existing.Id;
                }

                if (!db.Users.Any(x => x.Id == input.ClientId && x.Role == "Client"))
                    throw new ArgumentException("Оберіть клієнта.");
                if (input.OrderId.HasValue && !db.Rentals.Any(x => x.Id == input.OrderId.Value && x.UserId == input.ClientId))
                    throw new ArgumentException("Замовлення не належить цьому клієнту.");
                if (input.Kind == "Повернення коштів" || input.Kind == "Повернення застави")
                {
                    var parent = db.ShopTransactions.SingleOrDefault(x => x.Id == input.RelatedTransactionId);
                    string kind = input.Kind == "Повернення коштів" ? "Оплата" : "Застава";
                    if (parent == null || parent.ClientId != input.ClientId || parent.OrderId != input.OrderId || parent.Kind != kind)
                        throw new ArgumentException("Повернення має посилатися на відповідну оплату цього клієнта й замовлення.");
                    long returned = db.ShopTransactions.Where(x => x.RelatedTransactionId == parent.Id).Select(x => (long?)x.AmountMinor).Sum() ?? 0;
                    if (returned + input.AmountMinor > parent.AmountMinor)
                        throw new ArgumentException("Сума повернень перевищує початковий платіж.");
                }
                else if (input.RelatedTransactionId.HasValue)
                    throw new ArgumentException("Посилання на платіж потрібне лише для повернення.");
                var row = new ShopTransaction
                {
                    ClientId = input.ClientId,
                    OrderId = input.OrderId,
                    RelatedTransactionId = input.RelatedTransactionId,
                    Kind = input.Kind,
                    AmountMinor = input.AmountMinor,
                    Notes = input.Notes,
                    CreatedBy = actor,
                    CreatedAt = Now,
                    RequestKey = input.RequestKey,
                    IsDemo = true
                };
                db.ShopTransactions.Add(row);
                db.SaveChanges();
                db.Record(actor, "Демонстраційна транзакція", "Транзакція #" + row.Id, row.Kind + " · " + (row.AmountMinor / 100m).ToString("0.00") + " грн");
                db.SaveChanges();
                tx.Commit();
                return row.Id;
            }
        });
    }
}
