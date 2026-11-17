using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace SportRentApp
{
    public sealed class TeamService
    {
        private static string Now => DateTime.UtcNow.ToString("o");

        private static Task<T> Run<T>(Func<ApplicationContext, int, AccessPolicy, T> action)
        {
            int actor = AccessPolicy.CaptureAccount();
            return Task.Run(() =>
            {
                using (var db = new ApplicationContext())
                    return action(db, actor, AccessPolicy.Read(db, actor));
            });
        }

        private static IQueryable<PublicProfile> Profiles(ApplicationContext db) => db.Users.Select(u => new PublicProfile {
            Id = u.Id,
            Name = u.FullName,
            Bio = u.Role == "Admin" || u.Role == "Employee" ? u.Bio : null,
            PhotoPath = u.Role == "Admin" || u.Role == "Employee" ? u.PhotoPath : null,
            Role = u.Role,
            Tags = u.Tags,
            Status = u.Status,
            IsOnShift = u.IsOnShift
        });
        public Task<PublicProfile> ProfileAsync(int id) => Run((db, actor, access) =>
        {
            if (actor != id)
                access.RequireOperations();
            return Profiles(db).Single(x => x.Id == id && x.Status != "Deleted");
        });
        public Task<List<PublicProfile>> StaffAsync() => Run((db, actor, access) =>
        {
            access.RequireOperations();
            return Profiles(db).Where(x => x.Status == "Active" && (x.Role == "Admin" || x.Role == "Employee")).OrderBy(x => x.Name).ToList();
        });
        public Task SaveProfileAsync(int id, string name, string bio, string photo) => Run((db, actor, access) =>
        {
            if (actor != id)
                access.RequireEdit();
            if (InputRules.NameError(name) != null || (bio ?? "").Length > 2000)
                throw new ArgumentException("Перевірте ім’я та опис (до 2000 символів).");
            var user = db.Users.Single(x => x.Id == id && x.Status != "Deleted");
            if (user.Role != "Employee" && user.Role != "Admin")
                throw new UnauthorizedAccessException("Фото та опис профілю доступні лише персоналу.");
            string created = null;
            try
            {
                if (!string.IsNullOrEmpty(photo))
                {
                    var image = CatalogService.LoadPhoto(photo);
                    if (image == null)
                        throw new ArgumentException("Оберіть JPEG або PNG до 10 МБ.");
                    string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProfileImages");
                    Directory.CreateDirectory(folder);
                    created = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".png");
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(image));
                    using (var stream = File.Create(created))
                        encoder.Save(stream);
                    user.PhotoPath = Path.Combine("ProfileImages", Path.GetFileName(created));
                }

                user.FullName = name.Trim();
                user.Bio = bio?.Trim();
                user.Version++;
                user.UpdatedAt = Now;
                db.Record(actor, "Профіль змінено", "Користувач #" + id);
                db.SaveChanges();
                return true;
            }
            catch
            {
                if (created != null && File.Exists(created))
                    File.Delete(created);
                throw;
            }
        });
        public Task<PublicProfile> SetStaffTagAsync(int id, string tag, bool enabled) => Run((db, actor, access) =>
        {
            access.RequireOperations();
            if (id != actor || !StaffWork.Places.Contains(tag))
                access.RequireEdit();
            var user = db.Users.Single(x => x.Id == id);
            if ((user.Role != "Employee" && user.Role != "Admin") || user.Status == "Deleted" || !ShopService.TagNames.Contains(tag))
                throw new ArgumentException("Ця позначка доступна лише персоналу.");
            var tags = (user.Tags ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0 && x != tag).ToList();
            if (enabled)
                tags.Add(tag);
            StaffWork.SetTags(user, string.Join(", ", tags.Distinct()));
            user.Version++;
            user.UpdatedAt = Now;
            db.Record(actor, "Позначки працівника змінено", "Користувач #" + id, user.Tags);
            db.SaveChanges();
            return Profiles(db).Single(x => x.Id == id);
        });
        public Task<PublicProfile> StartShiftAsync(int id) => Run((db, actor, access) =>
        {
            access.RequireOperations();
            if (id != actor)
                access.RequireEdit();
            var user = db.Users.Single(x => x.Id == id);
            StaffWork.Start(user);
            user.Version++;
            user.UpdatedAt = Now;
            db.Record(actor, "Зміну розпочато", "Користувач #" + id, StaffWork.Locations(user.Tags));
            db.SaveChanges();
            return Profiles(db).Single(x => x.Id == id);
        });
        public Task<List<PublicProfile>> TransferCandidatesAsync(int taskId) => Run((db, actor, access) =>
        {
            access.RequireOperations();
            var task = db.WorkTasks.AsNoTracking().Single(x => x.Id == taskId && x.Status == "Open");
            var assignments = db.TaskAssignments.AsNoTracking().Where(x => x.TaskId == taskId).ToList();
            if (!access.CanEditCatalog && !assignments.Any(x => x.UserId == actor && x.Status == "Assigned"))
                throw new UnauthorizedAccessException("Передавати можна своє завдання.");
            return Profiles(db).Where(x => x.Id != actor && x.Status == "Active" && (x.Role == "Employee" || x.Role == "Admin")).ToList().Where(x => (task.Kind == "Personal" || (x.IsOnShift && StaffWork.Matches(x.Role, x.Tags, task.Station))) && (access.CanEditCatalog || !assignments.Any(a => a.UserId == x.Id && (a.Status == "Assigned" || a.Status == "Pending")))).OrderBy(x => x.Name).ToList();
        });
        private static void Synchronize(ApplicationContext db)
        {
            var desired = new List<WorkTask>();
            var orders = (
                from r in db.Rentals.AsNoTracking()join u in db.Users on r.UserId equals (int?)u.Id into users
                from client in users.DefaultIfEmpty()join e in db.Equipment on r.EquipmentId equals (int?)e.Id into items
                from item in items.DefaultIfEmpty()
                where r.Status != "Completed" && r.Status != "Cancelled"
                select new
                {
                    r.Id,
                    r.Status,
                    r.OrderNumber,
                    r.ReturnDate,
                    r.RentalPrice,
                    r.RequiresAdmin,
                    Client = client.FullName,
                    Item = item.Name
                }).ToList();
            foreach (var rental in orders)
            {
                string title = (rental.Item ?? "Оформити талон") + " · " + rental.OrderNumber;
                string details = (rental.Client ?? "Гість") + " · " + rental.RentalPrice.ToString("0.##") + " грн";
                if (rental.Status == "Active")
                    details += " · до " + rental.ReturnDate;
                desired.Add(new WorkTask {
                    SourceKey = "rental:" + rental.Id + ":" + rental.Status,
                    Kind = "Rental",
                    EntityId = rental.Id,
                    Title = title,
                    Description = details,
                    Station = rental.RequiresAdmin == 1 ? "Менеджер" : rental.Status == "Assembling" ? "Склад" : "Каса",
                    CreatedAt = Now
                });
            }

            desired.AddRange(db.WorkTasks.AsNoTracking().Where(x => x.Kind == "Return" && x.Status == "Open" && db.Rentals.Any(r => r.Id == x.EntityId && r.Status == "Active")).ToList());
            foreach (var user in db.Users.AsNoTracking().Where(x => x.Role == "Employee" && x.Status == "Pending").ToList())
                desired.Add(new WorkTask {
                    SourceKey = "account:" + user.Id,
                    Kind = "Account",
                    EntityId = user.Id,
                    Title = "Погодити працівника · " + user.FullName,
                    Station = "Менеджер",
                    CreatedAt = Now
                });
            foreach (var request in db.EquipmentChangeRequests.AsNoTracking().Where(x => x.Status == "Pending").ToList())
                desired.Add(new WorkTask {
                    SourceKey = "request:" + request.Id,
                    Kind = "Request",
                    EntityId = request.Id,
                    Title = "Розглянути звернення",
                    Description = request.Message,
                    Station = "Менеджер",
                    CreatedAt = Now
                });
            var keys = desired.Select(x => x.SourceKey).ToList();
            var existing = db.WorkTasks.Where(x => x.Status == "Open" || keys.Contains(x.SourceKey)).ToList();
            foreach (var item in existing.Where(x => x.Status == "Open" && !x.IsManual && !keys.Contains(x.SourceKey)))
                item.Status = "Done";
            var known = existing.ToDictionary(x => x.SourceKey);
            foreach (var item in desired)
            {
                WorkTask current;
                if (known.TryGetValue(item.SourceKey, out current))
                {
                    current.Status = "Open";
                    current.Title = item.Title;
                    current.Description = item.Description;
                    current.Station = item.Station;
                }
                else
                {
                    db.WorkTasks.Add(item);
                    existing.Add(item);
                }
            }

            db.SaveChanges();
            var staff = db.Users.Where(x => x.IsOnShift && x.Status == "Active" && (x.Role == "Admin" || x.Role == "Employee")).ToList();
            var taskIds = existing.Where(x => x.Status == "Open").Select(x => x.Id).ToList();
            var assignments = db.TaskAssignments.Where(x => taskIds.Contains(x.TaskId)).ToList();
            foreach (var task in existing.Where(x => x.Status == "Open" && x.Kind != "Personal"))
            {
                foreach (var stale in assignments.Where(x => x.TaskId == task.Id && x.Status == "Assigned" && !staff.Any(u => u.Id == x.UserId && StaffWork.Matches(u.Role, u.Tags, task.Station))))
                    stale.Status = "Released";
                if (assignments.Any(x => x.TaskId == task.Id && x.Status == "Assigned" && x.RequestedBy != null))
                    continue;
                var candidates = staff.Where(u => task.Station == "Менеджер" ? u.Role == "Admin" : u.Role == "Employee" && StaffWork.Has(u.Tags, task.Station)).ToList();
                if (candidates.Count == 0)
                    candidates = staff.Where(u => u.Role == "Admin").ToList();
                foreach (var old in assignments.Where(x => x.TaskId == task.Id && x.Status == "Assigned" && !candidates.Any(u => u.Id == x.UserId)))
                    old.Status = "Released";
                foreach (var user in candidates)
                {
                    var assignment = assignments.SingleOrDefault(x => x.TaskId == task.Id && x.UserId == user.Id);
                    if (assignment == null)
                    {
                        assignment = new TaskAssignment
                        {
                            TaskId = task.Id,
                            UserId = user.Id,
                            Status = "Assigned"
                        };
                        db.TaskAssignments.Add(assignment);
                        assignments.Add(assignment);
                    }
                    else if (assignment.Status != "Pending")
                        assignment.Status = "Assigned";
                }
            }

            db.SaveChanges();
        }

        public Task<List<WorkTask>> TasksAsync() => Run((db, actor, access) =>
        {
            access.RequireOperations();
            using (var tx = db.Database.BeginTransaction())
            {
                Synchronize(db);
                var tasks = db.WorkTasks.AsNoTracking().Where(x => x.Status == "Open").OrderBy(x => x.Id).ToList();
                var ids = tasks.Select(x => x.Id).ToList();
                var assignments = db.TaskAssignments.AsNoTracking().Where(x => ids.Contains(x.TaskId) && (x.Status == "Assigned" || x.Status == "Pending" || x.IsMentioned)).ToList();
                foreach (var task in tasks)
                    task.Assignments = assignments.Where(x => x.TaskId == task.Id).ToList();
                tx.Commit();
                return tasks;
            }
        });
        public Task AssignAsync(int taskId, int targetId) => Run((db, actor, access) =>
        {
            access.RequireOperations();
            using (var tx = db.Database.BeginTransaction())
            {
                var task = db.WorkTasks.Single(x => x.Id == taskId && x.Status == "Open");
                var target = db.Users.Single(x => x.Id == targetId && (x.IsOnShift || task.Kind == "Personal") && x.Status == "Active" && (x.Role == "Employee" || x.Role == "Admin"));
                if (task.Station == "Менеджер" && target.Role != "Admin")
                    throw new UnauthorizedAccessException("Це завдання менеджера.");
                if (task.Kind != "Personal" && !StaffWork.Matches(target.Role, target.Tags, task.Station))
                    throw new InvalidOperationException("Працівник має бути на відповідному робочому місці.");
                if (!access.CanEditCatalog && !db.TaskAssignments.Any(x => x.TaskId == taskId && x.UserId == actor && x.Status == "Assigned"))
                    throw new UnauthorizedAccessException("Передавати можна своє завдання.");
                var row = db.TaskAssignments.SingleOrDefault(x => x.TaskId == taskId && x.UserId == targetId);
                if (row != null && row.Status == "Assigned" && !access.CanEditCatalog)
                    return true;
                if (row != null && row.Status == "Pending" && !access.CanEditCatalog)
                    throw new InvalidOperationException("Запит уже очікує рішення.");
                if (row == null)
                {
                    row = new TaskAssignment
                    {
                        TaskId = taskId,
                        UserId = targetId
                    };
                    db.TaskAssignments.Add(row);
                }

                row.RequestedBy = actor;
                row.Status = access.CanEditCatalog ? "Assigned" : "Pending";
                if (access.CanEditCatalog)
                    foreach (var old in db.TaskAssignments.Where(x => x.TaskId == taskId && x.UserId != targetId))
                        old.Status = "Released";
                db.Record(actor, "Передача завдання", task.Title, "Отримувач #" + targetId + " · " + row.Status);
                db.SaveChanges();
                tx.Commit();
                return true;
            }
        });
        public Task DecideAssignmentAsync(int id, bool accept) => Run((db, actor, access) =>
        {
            access.RequireOperations();
            using (var tx = db.Database.BeginTransaction())
            {
                var row = db.TaskAssignments.Single(x => x.Id == id);
                if (row.UserId != actor && !access.CanEditCatalog)
                    throw new UnauthorizedAccessException("Рішення приймає отримувач або менеджер.");
                if (row.Status != "Pending" || !db.WorkTasks.Any(x => x.Id == row.TaskId && x.Status == "Open"))
                    throw new InvalidOperationException("Запит уже змінено.");
                var target = db.Users.Single(x => x.Id == row.UserId);
                var task = db.WorkTasks.Single(x => x.Id == row.TaskId);
                if (accept && (target.Status != "Active" || (target.Role != "Admin" && target.Role != "Employee") || (task.Kind != "Personal" && (!target.IsOnShift || (!StaffWork.Matches(target.Role, target.Tags, task.Station))))))
                    throw new InvalidOperationException("Отримувач уже не на цьому робочому місці.");
                row.Status = accept ? "Assigned" : "Rejected";
                if (accept)
                    foreach (var old in db.TaskAssignments.Where(x => x.TaskId == row.TaskId && x.UserId == row.RequestedBy && x.Id != row.Id))
                        old.Status = "Released";
                db.Record(actor, accept ? "Передачу прийнято" : "Передачу відхилено", task.Title);
                db.SaveChanges();
                tx.Commit();
                return true;
            }
        });
        public Task RequestAsync(string kind, string description, int[] mentions = null, string title = null) => SaveTaskAsync(null, kind, title, description, mentions);
        public Task SaveTaskAsync(int? id, string kind, string title, string description, int[] mentions) => Run((db, actor, access) =>
        {
            access.RequireOperations();
            description = (description ?? "").Trim();
            title = (title ?? "").Trim();
            if (!new[] { "Appeal", "Kiosk", "Personal" }.Contains(kind) || description.Length < 5 || description.Length > 2000 || title.Length > 120)
                throw new ArgumentException("Назва: до 120 символів. Опис: 5–2000 символів.");
            if (title.Length == 0)
                title = kind == "Kiosk" ? "Налаштувати кіоск" : kind == "Personal" ? "Особисте завдання" : "Звернення працівника";
            int[] people = (mentions ?? new int[0]).Distinct().ToArray();
            using (var tx = db.Database.BeginTransaction())
            {
                if (db.Users.Count(x => people.Contains(x.Id) && x.Status == "Active" && (x.Role == "Admin" || x.Role == "Employee")) != people.Length)
                    throw new ArgumentException("Відмічати можна активних працівників і менеджерів.");
                WorkTask task;
                if (id.HasValue)
                {
                    task = db.WorkTasks.Single(x => x.Id == id && x.Status == "Open");
                    if (!task.IsManual || (!access.CanEditCatalog && task.CreatedBy != actor))
                        throw new UnauthorizedAccessException("Редагувати може автор або менеджер.");
                    if (task.Kind != kind)
                        throw new ArgumentException("Тип створеного завдання не змінюється.");
                }
                else
                {
                    task = new WorkTask
                    {
                        SourceKey = Guid.NewGuid().ToString(),
                        Kind = kind,
                        Station = kind == "Personal" ? "" : "Менеджер",
                        CreatedBy = actor,
                        CreatedAt = Now
                    };
                    db.WorkTasks.Add(task);
                }

                task.Title = title;
                task.Description = description;
                db.SaveChanges();
                var assignments = db.TaskAssignments.Where(x => x.TaskId == task.Id).ToList();
                if (!id.HasValue && kind == "Personal")
                    db.TaskAssignments.Add(new TaskAssignment { TaskId = task.Id, UserId = actor, RequestedBy = actor, Status = "Assigned" });
                foreach (var old in assignments.Where(x => x.IsMentioned && !people.Contains(x.UserId)))
                    old.IsMentioned = false;
                foreach (int person in people)
                {
                    var row = assignments.SingleOrDefault(x => x.UserId == person);
                    if (row == null)
                        db.TaskAssignments.Add(new TaskAssignment { TaskId = task.Id, UserId = person, Status = "Released", IsMentioned = true });
                    else
                        row.IsMentioned = true;
                }

                db.Record(actor, id.HasValue ? "Завдання змінено" : "Завдання створено", "Завдання #" + task.Id, title);
                db.SaveChanges();
                tx.Commit();
                return true;
            }
        });
        public Task CloseRequestAsync(int id) => Run((db, actor, access) =>
        {
            access.RequireOperations();
            var task = db.WorkTasks.Single(x => x.Id == id && x.Status == "Open");
            if (!task.IsManual || (!access.CanEditCatalog && (task.Kind != "Personal" || !(task.CreatedBy == actor || db.TaskAssignments.Any(x => x.TaskId == id && x.UserId == actor && x.Status == "Assigned")))))
                throw new UnauthorizedAccessException("Завершувати можна власне завдання. Звернення закриває менеджер.");
            task.Status = "Done";
            db.Record(actor, "Завдання завершено", "Завдання #" + id, task.Title);
            db.SaveChanges();
            return true;
        });
        public Task<List<LocalMessage>> ConversationAsync(int peer, bool archive) => Run((db, actor, access) =>
        {
            access.RequireOperations();
            return db.LocalMessages.AsNoTracking().Where(x => ((x.SenderId == actor && x.RecipientId == peer) || (x.SenderId == peer && x.RecipientId == actor)) && (archive || !x.Archived)).OrderBy(x => x.Id).ToList();
        });
        public Task<int> ArchiveConversationAsync(int peer) => Run((db, actor, access) =>
        {
            access.RequireOperations();
            if (peer == actor)
                throw new ArgumentException("Оберіть співрозмовника.");
            using (var tx = db.Database.BeginTransaction())
            {
                var rows = db.LocalMessages.Where(x => !x.Archived && ((x.SenderId == actor && x.RecipientId == peer) || (x.SenderId == peer && x.RecipientId == actor))).ToList();
                if (rows.Count == 0)
                    return 0;
                foreach (var row in rows)
                    row.Archived = true;
                db.Record(actor, "Чат збережено", "Користувач #" + peer, "Повідомлень: " + rows.Count);
                db.SaveChanges();
                tx.Commit();
                return rows.Count;
            }
        });
        public Task SendAsync(int peer, string body) => Run((db, actor, access) =>
        {
            access.RequireOperations();
            body = (body ?? "").Trim();
            if (peer == actor || body.Length == 0 || body.Length > 2000)
                throw new ArgumentException("Повідомлення: 1–2000 символів.");
            if (!db.Users.Any(x => x.Id == peer && x.Status == "Active" && (x.Role == "Admin" || x.Role == "Employee")))
                throw new UnauthorizedAccessException("Отримувач недоступний.");
            db.LocalMessages.Add(new LocalMessage { SenderId = actor, RecipientId = peer, Body = body, CreatedAt = Now });
            db.SaveChanges();
            return true;
        });
        public Task<string> EndShiftAsync() => Run((db, actor, access) =>
        {
            access.RequireOperations();
            using (var tx = db.Database.BeginTransaction())
            {
                var messages = db.LocalMessages.Where(x => x.SenderId == actor && x.RecipientId.HasValue && !x.Archived).ToList();
                var user = db.Users.Single(x => x.Id == actor);
                if (!user.IsOnShift)
                    throw new InvalidOperationException("Зміну вже завершено. Оберіть робоче місце, щоб почати нову.");
                string summary = "Зміну завершено. Нових повідомлень в архіві: " + messages.Count + ".";
                foreach (var message in messages)
                    message.Archived = true;
                db.Record(actor, "Зміну завершено", StaffWork.Locations(user.Tags), "Збережено повідомлень: " + messages.Count);
                user.IsOnShift = false;
                user.Version++;
                foreach (var row in db.TaskAssignments.Where(x => x.UserId == actor && (x.Status == "Assigned" || x.Status == "Pending") && db.WorkTasks.Any(t => t.Id == x.TaskId && t.Kind != "Personal")))
                    row.Status = "Released";
                db.SaveChanges();
                tx.Commit();
                return summary;
            }
        });
    }
}
