using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace SportRentApp
{
    public sealed class JournalService
    {
        private static Task<T> Read<T>(System.Func<ApplicationContext, T> query)
        {
            int actor = AccessPolicy.CaptureAccount();
            return Task.Run(() =>
            {
                using (var db = new ApplicationContext())
                {
                    AccessPolicy.Read(db, actor).RequireEdit();
                    return query(db);
                }
            });
        }

        public Task<List<AuditEntry>> AuditAsync() => Read(db =>
        {
            var names = db.Users.AsNoTracking().ToDictionary(x => x.Id, x => x.FullName);
            var rows = db.ShopAudit.AsNoTracking().OrderByDescending(x => x.Id).ToList();
            foreach (var row in rows)
                row.ActorName = row.ActorId.HasValue && names.ContainsKey(row.ActorId.Value) ? names[row.ActorId.Value] : "Система";
            return rows;
        });
        public Task<List<ArchivedChat>> ChatsAsync() => Read(db =>
        {
            var names = db.Users.AsNoTracking().ToDictionary(x => x.Id, x => x.FullName);
            return db.LocalMessages.AsNoTracking().Where(x => x.Archived && x.RecipientId.HasValue).Select(x => new { x.SenderId, x.RecipientId, x.CreatedAt }).ToList().GroupBy(x => new { First = System.Math.Min(x.SenderId, x.RecipientId.Value), Second = System.Math.Max(x.SenderId, x.RecipientId.Value) }).Select(g => new ArchivedChat {
                FirstUserId = g.Key.First,
                SecondUserId = g.Key.Second,
                Participants = names[g.Key.First] + " / " + names[g.Key.Second],
                MessageCount = g.Count(),
                LastMessageAt = g.Max(x => x.CreatedAt)
            }).OrderByDescending(x => x.LastMessageAt).ToList();
        });
        public Task<List<ArchivedMessage>> MessagesAsync(int first, int second) => Read(db => (
            from message in db.LocalMessages.AsNoTracking()join sender in db.Users on message.SenderId equals sender.Id
            where message.Archived && ((message.SenderId == first && message.RecipientId == second) || (message.SenderId == second && message.RecipientId == first))orderby message.Id
            select new ArchivedMessage { Sender = sender.FullName, Body = message.Body, CreatedAt = message.CreatedAt }).ToList());
    }
}
