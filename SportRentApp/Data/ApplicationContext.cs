using System.Data.Entity;

namespace SportRentApp
{
    public class ApplicationContext : DbContext
    {
        public DbSet<WorkTask> WorkTasks { get; set; }
        public DbSet<TaskAssignment> TaskAssignments { get; set; }
        public DbSet<LocalMessage> LocalMessages { get; set; }
        public DbSet<AuditEntry> ShopAudit { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Equipment> Equipment { get; set; }
        public DbSet<Rental> Rentals { get; set; }
        public DbSet<EquipmentRequest> EquipmentChangeRequests { get; set; }
        public DbSet<ShopTransaction> ShopTransactions { get; set; }

        static ApplicationContext()
        {
            Database.SetInitializer<ApplicationContext>(null);
            StoreSchema.Ensure();
        }

        public ApplicationContext() : base(new System.Data.SQLite.SQLiteConnection(StoreSchema.ConnectionString), true)
        {
            Configuration.LazyLoadingEnabled = false;
            Configuration.ProxyCreationEnabled = false;
        }

        public void Record(int? actor, string action, string target, string details = null)
        {
            ShopAudit.Add(new AuditEntry {
                ActorId = actor,
                Action = action,
                Target = target,
                Details = details,
                CreatedAt = System.DateTime.UtcNow.ToString("o")
            });
        }
    }
}
