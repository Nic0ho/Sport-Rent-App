using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SportRentApp
{
    public class Rental
    {
        public int Id { get; set; }
        public string OrderNumber { get; set; }
        public int? UserId { get; set; }
        public int? EquipmentId { get; set; }
        public string QueueType { get; set; }
        public string RentDate { get; set; }
        public string ReturnDate { get; set; }
        public string ActualReturnDate { get; set; }
        public double RentalPrice { get; set; }
        public double Deposit { get; set; }
        public double PenaltyFee { get; set; }
        public double TotalPrice { get; set; }
        public string Status { get; set; }
        public string ConditionNotes { get; set; }
        public int RequiresAdmin { get; set; }
        public string AdminNotes { get; set; }
        public string CreatedAt { get; set; }
        public int? KioskId { get; set; }
        public string ShopCode { get; set; }
        public string Phone { get; set; }
        public string RequestKey { get; set; }

        [System.ComponentModel.DataAnnotations.ConcurrencyCheck]
        public int Version { get; set; }

        [NotMapped]
        public bool IsOverdue => Status == "Active" && DateTime.TryParseExact(ReturnDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var due) && due.Date < DateTime.Today;

        [ForeignKey("UserId")]
        public virtual User User { get; set; }

        [ForeignKey("EquipmentId")]
        public virtual Equipment Equipment { get; set; }

        public static string StatusLabel(string status)
        {
            switch (status)
            {
                case "Pending":
                case "Called":
                case "Serving":
                    return "Очікує на касі";
                case "Assembling":
                    return "Готується на складі";
                case "Ready":
                    return "Можна забрати";
                case "Active":
                    return "В оренді";
                case "Completed":
                    return "Повернено";
                case "Cancelled":
                    return "Скасовано";
                default:
                    return status;
            }
        }

        public Rental()
        {
        }
    }
}
