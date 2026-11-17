using System.Linq;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SportRentApp
{
    public sealed class CustomerOrder
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public string Item { get; set; }
        public string Status { get; set; }
        public string Due { get; set; }
        public double Price { get; set; }
        public bool ReturnRequested { get; set; }

        internal static IQueryable<CustomerOrder> ForClient(ApplicationContext db, int clientId) =>
            from order in db.Rentals
            join equipment in db.Equipment on order.EquipmentId equals (int?)equipment.Id into equipmentRows
            from item in equipmentRows.DefaultIfEmpty()
            where order.UserId == clientId
            orderby (order.Status == "Completed" || order.Status == "Cancelled"), order.Id descending
            select new CustomerOrder
            {
                Id = order.Id,
                Number = order.OrderNumber,
                Item = item.Name,
                Status = order.Status,
                Due = order.ReturnDate,
                Price = order.TotalPrice,
                ReturnRequested = db.WorkTasks.Any(x => x.Kind == "Return" && x.EntityId == order.Id && x.Status == "Open")
            };
        public string Label => (Number ?? ("№ " + Id)) + " · " + (Item ?? "Талон");
        public string Summary => Rental.StatusLabel(Status) + (string.IsNullOrEmpty(Due) ? "" : " · до " + Due) + " · " + Price.ToString("0.##") + " грн";
    }

    public sealed class KioskTicket
    {
        public int Id { get; set; }
        public string OrderNumber { get; set; }
        public double RentalPrice { get; set; }
    }

    [Table("EquipmentChangeRequests")]
    public class EquipmentRequest
    {
        public int Id { get; set; }
        public int RequesterId { get; set; }
        public int EquipmentId { get; set; }
        public string Message { get; set; }
        public string Status { get; set; } = "Pending";
        public string Resolution { get; set; }
        public int? ReviewedBy { get; set; }

        [ConcurrencyCheck]
        public int Version { get; set; }
        public string CreatedAt { get; set; }
    }

    [Table("ShopTransactions")]
    public class ShopTransaction
    {
        public int Id { get; set; }
        public int ClientId { get; set; }
        public int? OrderId { get; set; }
        public int? RelatedTransactionId { get; set; }
        public string Kind { get; set; }
        public long AmountMinor { get; set; }
        public string Notes { get; set; }
        public bool IsDemo { get; set; } = true;
        public int CreatedBy { get; set; }
        public string CreatedAt { get; set; }
        public string RequestKey { get; set; }
    }

    public class AccountView
    {
        public int Id { get; set; }
        public string FullName { get; set; }
        public string Username { get; set; }
        public string Phone { get; set; }
        public string Notes { get; set; }
        public string Role { get; set; }
        public string Status { get; set; }
        public string Tags { get; set; }
        public int Version { get; set; }
        public bool PrivateVisible { get; set; }
        public string ShopCode { get; set; }
    }
}
