using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace SportRentApp
{
    public sealed class KioskService
    {
        private static Task<T> Run<T>(Func<ApplicationContext, int, T> action)
        {
            int actor = AccessPolicy.CaptureAccount();
            return Task.Run(() =>
            {
                using (var db = new ApplicationContext())
                using (var tx = db.Database.BeginTransaction())
                {
                    if (AccessPolicy.Read(db, actor).Role != UserRole.Kiosk)
                        throw new UnauthorizedAccessException("Оформлення доступне лише запущеному кіоску.");
                    var result = action(db, actor);
                    tx.Commit();
                    return result;
                }
            });
        }

        private static KioskTicket Ticket(Rental order) => new KioskTicket
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            RentalPrice = order.RentalPrice
        };
        private static void RequireClient(User client)
        {
            if (client.Role != "Client" || client.Status != "Active" || (client.Tags ?? "").Split(',').Any(x => x.Trim() == "Бан"))
                throw new InvalidOperationException("Оформлення за цим номером недоступне. Зверніться до касира.");
        }

        public sealed class Visitor
        {
            internal Visitor(int terminal, User customer)
            {
                Terminal = terminal;
                Client = customer.Id;
                Name = customer.FullName;
            }

            internal int Terminal { get; }
            internal int Client { get; }
            public string Name { get; }
        }

        private static User FindClient(ApplicationContext db, string phone)
        {
            var matches = db.Users.Where(x => x.Role == "Client" && x.Phone == phone).Take(2).ToList();
            if (matches.Count > 1)
                throw new InvalidOperationException("За цим номером кілька карток. Зверніться до касира.");
            var client = matches.SingleOrDefault();
            if (client != null)
                RequireClient(client);
            return client;
        }

        public Task<Visitor> IdentifyAsync(string number) => Run((db, actor) =>
        {
            var client = FindClient(db, InputRules.NormalizePhone(number));
            return client == null ? null : new Visitor(actor, client);
        });
        public Task<Visitor> RegisterAsync(string number, string name) => Run((db, actor) =>
        {
            string phone = InputRules.NormalizePhone(number);
            string error = InputRules.NameError(name);
            if (error != null)
                throw new ArgumentException(error);
            if (FindClient(db, phone) != null)
                throw new InvalidOperationException("Цей номер уже зареєстровано. Увійдіть за номером.");
            var client = new User("client-" + Guid.NewGuid().ToString("N"), PasswordHash.Create(Guid.NewGuid().ToString("N")), name.Trim(), phone, "Client")
            {
                IsOnShift = false,
                CreatedAt = DateTime.UtcNow.ToString("o")
            };
            db.Users.Add(client);
            db.SaveChanges();
            db.Record(actor, "Клієнта зареєстровано", "Користувач #" + client.Id);
            db.SaveChanges();
            return new Visitor(actor, client);
        });
        private static User CurrentClient(ApplicationContext db, int actor, Visitor visitor)
        {
            if (visitor == null || visitor.Terminal != actor)
                throw new UnauthorizedAccessException("Увійдіть за номером телефону.");
            var client = db.Users.Single(x => x.Id == visitor.Client);
            RequireClient(client);
            return client;
        }

        public Task<System.Collections.Generic.List<CustomerOrder>> MyOrdersAsync(Visitor visitor) => Run((db, actor) =>
        {
            var client = CurrentClient(db, actor, visitor);
            return CustomerOrder.ForClient(db, client.Id).ToList();
        });
        public Task RequestReturnAsync(Visitor visitor, int orderId) => Run((db, actor) =>
        {
            User client = CurrentClient(db, actor, visitor);
            Rental order = db.Rentals.SingleOrDefault(x => x.Id == orderId && x.UserId == client.Id);
            if (order == null || order.Status != "Active")
                throw new InvalidOperationException("Звернення доступне лише для вашої активної оренди.");
            string key = "return:" + order.Id;
            if (db.WorkTasks.Any(x => x.SourceKey == key))
                return true;
            db.WorkTasks.Add(new WorkTask {
                SourceKey = key,
                Kind = "Return",
                EntityId = order.Id,
                Title = "Прийняти повернення · " + order.OrderNumber,
                Description = client.FullName + " звернувся щодо повернення спорядження.",
                Station = "Каса",
                CreatedBy = client.Id,
                CreatedAt = DateTime.UtcNow.ToString("o")
            });
            db.Record(actor, "Клієнт подав звернення про повернення", order.OrderNumber);
            db.SaveChanges();
            return true;
        });
        private static Rental NewTicket(ApplicationContext db, int actor, User client, string key)
        {
            string shop = db.Users.Single(x => x.Id == actor).ShopCode;
            if (string.IsNullOrWhiteSpace(shop))
                throw new InvalidOperationException("Для кіоску не вказано номер магазину.");
            var order = new Rental
            {
                KioskId = actor,
                ShopCode = shop,
                UserId = client.Id,
                Phone = client.Phone,
                RequestKey = key,
                CreatedAt = DateTime.UtcNow.ToString("o"),
                Status = "Pending",
                QueueType = "Online"
            };
            db.Rentals.Add(order);
            db.SaveChanges();
            order.OrderNumber = "SD-" + order.Id.ToString("D6");
            db.Record(actor, "Талон створено", order.OrderNumber, shop);
            db.SaveChanges();
            return order;
        }

        public Task<KioskTicket> BeginVisitAsync(string phone, string requestKey) => Run((db, actor) =>
        {
            if (!Guid.TryParse(requestKey, out var key))
                throw new ArgumentException("Некоректний номер запиту.");
            var client = FindClient(db, InputRules.NormalizePhone(phone));
            if (client == null)
                throw new InvalidOperationException("Спочатку зареєструйтеся.");
            var order = db.Rentals.SingleOrDefault(x => x.RequestKey == requestKey);
            if (order != null && (order.KioskId != actor || order.UserId != client.Id))
                throw new UnauthorizedAccessException();
            return Ticket(order ?? NewTicket(db, actor, client, requestKey));
        });
        public static double Price(double daily, string startValue, string endValue)
        {
            return RentalRules.Price(daily, startValue, endValue, true);
        }

        public Task<KioskTicket> OrderAsync(string requestKey, int equipmentId, string start, string end, double expectedDaily) => OrderAsync(null, requestKey, equipmentId, start, end, expectedDaily);
        public Task<KioskTicket> OrderAsync(Visitor visitor, string requestKey, int equipmentId, string start, string end, double expectedDaily) => Run((db, actor) =>
        {
            if (!Guid.TryParse(requestKey, out var key))
                throw new ArgumentException("Спочатку введіть номер телефону.");
            var order = db.Rentals.SingleOrDefault(x => x.RequestKey == requestKey && x.KioskId == actor);
            if (visitor != null)
            {
                var client = CurrentClient(db, actor, visitor);
                if (order == null)
                    order = NewTicket(db, actor, client, requestKey);
                else if (order.UserId != client.Id)
                    throw new UnauthorizedAccessException();
            }

            if (order == null)
                throw new UnauthorizedAccessException("Візит недоступний. Введіть номер телефону ще раз.");
            RequireClient(db.Users.Single(x => x.Id == order.UserId));
            if (order.EquipmentId.HasValue)
            {
                if (order.EquipmentId == equipmentId && order.RentDate == start && order.ReturnDate == end)
                    return Ticket(order);
                throw new InvalidOperationException("Замовлення вже оформлено. Змінити його можна на касі.");
            }

            if (order.Status != "Pending" || order.Version != 0)
                throw new InvalidOperationException("Касир уже почав обслуговування. Продовжіть оформлення на касі.");
            var equipment = db.Equipment.SingleOrDefault(x => x.Id == equipmentId);
            RentalRules.RequireReservation(db, equipment, start, end, order.Id);
            if (equipment.PricePerDay != expectedDaily)
                throw new InvalidOperationException("Тариф змінився. Оновіть каталог і перевірте нову ціну.");
            double price = Price(equipment.PricePerDay, start, end);
            order.EquipmentId = equipmentId;
            order.RentDate = start;
            order.ReturnDate = end;
            order.RentalPrice = order.TotalPrice = price;
            order.Version++;
            db.Record(actor, "Замовлення оформлено", order.OrderNumber, "Спорядження #" + equipmentId + " · оплата на касі");
            db.SaveChanges();
            return Ticket(order);
        });
    }
}
