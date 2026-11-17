using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace SportRentApp
{
    [Table("WorkTasks")]
    public class WorkTask
    {
        public int Id { get; set; }
        public string SourceKey { get; set; }
        public string Kind { get; set; }
        public int? EntityId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Station { get; set; }
        public string Status { get; set; } = "Open";
        public int? CreatedBy { get; set; }
        public string CreatedAt { get; set; }

        [NotMapped]
        public List<TaskAssignment> Assignments { get; set; } = new List<TaskAssignment>();

        [NotMapped]
        public bool IsManual => Kind == "Appeal" || Kind == "Kiosk" || Kind == "Personal";

        public bool IsMine(int actor) => CreatedBy == actor || Assignments.Exists(a => a.UserId == actor);
    }

    [Table("TaskAssignments")]
    public class TaskAssignment
    {
        public int Id { get; set; }
        public int TaskId { get; set; }
        public int UserId { get; set; }
        public int? RequestedBy { get; set; }
        public bool IsMentioned { get; set; }
        public string Status { get; set; }
    }

    [Table("LocalMessages")]
    public class LocalMessage
    {
        public int Id { get; set; }
        public int SenderId { get; set; }
        public int? RecipientId { get; set; }
        public string Body { get; set; }
        public string CreatedAt { get; set; }
        public bool Archived { get; set; }
    }

    public class PublicProfile
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Bio { get; set; }
        public string PhotoPath { get; set; }
        public string Role { get; set; }
        public string Tags { get; set; }
        public string Status { get; set; }
        public bool IsOnShift { get; set; }
    }

    [Table("ShopAudit")]
    public class AuditEntry
    {
        public int Id { get; set; }
        public int? ActorId { get; set; }
        public string Action { get; set; }
        public string Target { get; set; }
        public string Details { get; set; }
        public string CreatedAt { get; set; }

        [NotMapped]
        public string ActorName { get; set; }
    }

    public class ArchivedChat
    {
        public int FirstUserId { get; set; }
        public int SecondUserId { get; set; }
        public string Participants { get; set; }
        public string LastMessageAt { get; set; }
        public int MessageCount { get; set; }
    }

    public class ArchivedMessage
    {
        public string Sender { get; set; }
        public string Body { get; set; }
        public string CreatedAt { get; set; }
    }
}
