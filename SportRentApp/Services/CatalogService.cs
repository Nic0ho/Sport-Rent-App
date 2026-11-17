using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace SportRentApp
{
    public sealed class CatalogService
    {
        public Task<AccessPolicy> GetAccessAsync()
        {
            int id = AccessPolicy.CaptureAccount();
            return Task.Run(() =>
            {
                using (var db = new ApplicationContext())
                    return AccessPolicy.Read(db, id);
            });
        }

        public Task<List<CatalogItem>> LoadAsync()
        {
            int id = AccessPolicy.CaptureAccount();
            return Task.Run(() =>
            {
                using (var db = new ApplicationContext())
                {
                    var access = AccessPolicy.Read(db, id);
                    IQueryable<Equipment> query = db.Equipment.AsNoTracking().Where(x => x.Status != "Deleted");
                    if (!access.CanReadOperations)
                        query = query.Where(x => x.Status == "Available" || x.Status == "Rented");
                    var records = query.OrderBy(x => x.Name).ToList();
                    var orders = db.Rentals.AsNoTracking().Where(x => x.EquipmentId.HasValue && x.Status != "Completed" && x.Status != "Cancelled").ToList();
                    var items = new List<CatalogItem>();
                    foreach (Equipment record in records)
                    {
                        DateTime? available = RentalRules.AvailableFrom(record, orders.Where(x => x.EquipmentId == record.Id));
                        bool canReserve = available.HasValue && (record.Status == "Available" || record.Status == "Rented");
                        if (!access.CanReadOperations && (!available.HasValue || available.Value > DateTime.Today))
                            record.Status = "Unavailable";
                        items.Add(new CatalogItem(record, true, available, canReserve));
                    }

                    return items;
                }
            });
        }

        public Task<List<CatalogItem>> LoadInventoryAsync()
        {
            int id = AccessPolicy.CaptureAccount();
            return Task.Run(() =>
            {
                using (var db = new ApplicationContext())
                {
                    AccessPolicy.Read(db, id).RequireOperations();
                    return db.Equipment.AsNoTracking().Where(x => x.Status != "Deleted").OrderBy(x => x.Name).ToList().Select(x => new CatalogItem(x, false)).ToList();
                }
            });
        }

        public Task<List<Rental>> LoadRentalsAsync(int? rentalId = null)
        {
            int id = AccessPolicy.CaptureAccount();
            return Task.Run(() =>
            {
                using (var db = new ApplicationContext())
                {
                    var access = AccessPolicy.Read(db, id);
                    access.RequireOperations();
                    var query = db.Rentals.AsNoTracking().AsQueryable();
                    if (rentalId.HasValue)
                        query = query.Where(x => x.Id == rentalId.Value);
                    var orders = query.Include(x => x.Equipment).OrderByDescending(x => x.Id).ToList();
                    var clientIds = orders.Where(x => x.UserId.HasValue).Select(x => x.UserId.Value).Distinct().ToList();
                    var names = db.Users.Where(x => clientIds.Contains(x.Id)).Select(x => new { x.Id, x.FullName }).ToDictionary(x => x.Id, x => x.FullName);
                    foreach (var order in orders)
                        if (order.UserId.HasValue && names.ContainsKey(order.UserId.Value))
                            order.User = new User
                            {
                                Id = order.UserId.Value,
                                FullName = names[order.UserId.Value]
                            };
                    if (!access.CanEditCatalog)
                        foreach (var order in orders)
                            order.Phone = string.IsNullOrEmpty(order.Phone) ? "" : "•••" + order.Phone.Substring(Math.Max(0, order.Phone.Length - 4));
                    return orders;
                }
            });
        }

        public Task<int> SaveAsync(Equipment value, Equipment original, string selectedPhoto)
        {
            int userId = AccessPolicy.CaptureAccount();
            if (value == null || string.IsNullOrWhiteSpace(value.Name) || value.Name.Trim().Length > 160 || string.IsNullOrWhiteSpace(value.Category) || value.Category.Trim().Length > 80 || (value.Description != null && value.Description.Length > 4000) || !InputRules.IsMoney(value.PricePerDay, true))
                throw new ArgumentException("Перевірте назву, категорію, опис та ціну (до 1 000 000 грн, максимум 2 знаки після коми).");
            return Task.Run(() =>
            {
                string copiedFile = null;
                try
                {
                    using (var db = new ApplicationContext())
                    using (var transaction = db.Database.BeginTransaction())
                    {
                        AccessPolicy.Read(db, userId).RequireEdit();
                        Equipment target;
                        if (original == null)
                        {
                            target = new Equipment
                            {
                                Status = "Available"
                            };
                            db.Equipment.Add(target);
                        }
                        else
                        {
                            target = db.Equipment.SingleOrDefault(x => x.Id == original.Id);
                            if (!Unchanged(target, original))
                                throw new InvalidOperationException("Товар уже змінено. Закрийте форму, оновіть каталог і відкрийте товар повторно.");
                        }

                        if (!string.IsNullOrEmpty(selectedPhoto))
                        {
                            var photo = LoadPhoto(selectedPhoto);
                            if (photo == null)
                                throw new InvalidOperationException("Не вдалося прочитати фото.");
                            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CatalogImages");
                            Directory.CreateDirectory(folder);
                            copiedFile = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".png");
                            var encoder = new PngBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(photo));
                            using (var stream = File.Create(copiedFile))
                                encoder.Save(stream);
                            target.ImagePath = Path.Combine("CatalogImages", Path.GetFileName(copiedFile));
                        }

                        target.Name = value.Name.Trim();
                        target.Category = value.Category.Trim();
                        target.Description = value.Description?.Trim();
                        target.PricePerDay = value.PricePerDay;
                        db.SaveChanges();
                        db.Record(userId, original == null ? "Товар створено" : "Товар змінено", "Спорядження #" + target.Id, target.Name);
                        db.SaveChanges();
                        transaction.Commit();
                        return target.Id;
                    }
                }
                catch
                {
                    if (copiedFile != null)
                    {
                        try
                        {
                            File.Delete(copiedFile);
                        }
                        catch (IOException)
                        {
                        }
                        catch (UnauthorizedAccessException)
                        {
                        }
                    }

                    throw;
                }
            });
        }

        private static bool Unchanged(Equipment current, Equipment original) => current != null && original != null && current.Status != "Deleted" && current.Name == original.Name && current.Category == original.Category && current.Description == original.Description && current.PricePerDay == original.PricePerDay && current.ImagePath == original.ImagePath && current.Status == original.Status;
        public Task RestoreAsync(Equipment original)
        {
            int actor = AccessPolicy.CaptureAccount();
            return Task.Run(() =>
            {
                using (var db = new ApplicationContext())
                using (var tx = db.Database.BeginTransaction())
                {
                    AccessPolicy.Read(db, actor).RequireEdit();
                    if (original == null)
                        throw new ArgumentException("Оберіть спорядження.");
                    Equipment current = db.Equipment.SingleOrDefault(x => x.Id == original.Id);
                    if (!Unchanged(current, original))
                        throw new InvalidOperationException("Товар уже змінено. Оновіть каталог.");
                    if (current.Status != "Maintenance" && current.Status != "Lost")
                        throw new InvalidOperationException("Відновлення доступне після ремонту або знаходження спорядження.");
                    if (db.Rentals.Any(x => x.EquipmentId == current.Id && (x.Status == "Assembling" || x.Status == "Ready" || x.Status == "Active")))
                        throw new InvalidOperationException("Спочатку завершіть поточну оренду.");
                    current.Status = "Available";
                    db.Record(actor, "Доступність спорядження відновлено", "Спорядження #" + current.Id);
                    db.SaveChanges();
                    tx.Commit();
                }
            });
        }

        public Task DeleteAsync(Equipment original)
        {
            int actor = AccessPolicy.CaptureAccount();
            return Task.Run(() =>
            {
                using (var db = new ApplicationContext())
                using (var tx = db.Database.BeginTransaction())
                {
                    AccessPolicy.Read(db, actor).RequireEdit();
                    if (original == null)
                        throw new ArgumentException("Оберіть товар.");
                    var current = db.Equipment.SingleOrDefault(x => x.Id == original.Id);
                    if (!Unchanged(current, original))
                        throw new InvalidOperationException("Товар уже змінено. Оновіть каталог.");
                    if (db.Rentals.Any(x => x.EquipmentId == current.Id && x.Status != "Completed" && x.Status != "Cancelled"))
                        throw new InvalidOperationException("Товар є в незавершеному замовленні. Спочатку завершіть або скасуйте його.");
                    foreach (var request in db.EquipmentChangeRequests.Where(x => x.EquipmentId == current.Id && x.Status == "Pending"))
                    {
                        request.Status = "Rejected";
                        request.Resolution = "Товар видалено з каталогу.";
                        request.ReviewedBy = actor;
                        request.Version++;
                    }

                    if (db.Rentals.Any(x => x.EquipmentId == current.Id) || db.EquipmentChangeRequests.Any(x => x.EquipmentId == current.Id))
                        current.Status = "Deleted";
                    else
                        db.Equipment.Remove(current);
                    db.Record(actor, "Товар видалено", "Спорядження #" + current.Id, current.Name);
                    db.SaveChanges();
                    tx.Commit();
                }
            });
        }

        public static BitmapSource LoadPhoto(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            try
            {
                var full = Path.IsPathRooted(path) ? path : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
                if (!File.Exists(full) || new FileInfo(full).Length > 10 * 1024 * 1024)
                    return null;
                var image = new BitmapImage();
                using (var stream = File.OpenRead(full))
                {
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.DecodePixelWidth = 1200;
                    image.StreamSource = stream;
                    image.EndInit();
                    image.Freeze();
                }

                return image;
            }
            catch (Exception ex)when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is ArgumentException || ex is System.Runtime.InteropServices.COMException || ex is System.IO.FileFormatException)
            {
                return null;
            }
        }
    }
}
