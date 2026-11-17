using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.Linq;

namespace SportRentApp
{
    public static class RentalRules
    {
        public static DateTime ReadDate(string value)
        {
            DateTime date;
            if (!DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                throw new ArgumentException("Вкажіть коректну дату оренди.");
            return date;
        }

        public static DateTime PeriodEnd(DateTime start, DateTime end)
        {
            if (end < start || start == DateTime.MaxValue.Date)
                throw new ArgumentException("Повернення не може бути раніше початку оренди.");
            return end == start ? start.AddDays(1) : end;
        }

        public static double Price(double daily, string startValue, string endValue, bool futureOnly = false)
        {
            DateTime start = ReadDate(startValue);
            DateTime end = ReadDate(endValue);
            if (futureOnly && start < DateTime.Today)
                throw new ArgumentException("Початок оренди не може бути раніше сьогодні.");
            end = PeriodEnd(start, end);
            if (!InputRules.IsMoney(daily, true))
                throw new ArgumentException("Тариф недоступний. Зверніться до касира.");
            double total = (double)decimal.Round((decimal)daily * (end - start).Days, 2);
            if (!InputRules.IsMoney(total, true))
                throw new ArgumentException("Вартість прокату перевищує допустиму суму.");
            return total;
        }

        public static bool IsReservation(string status)
        {
            return status == "Pending" || status == "Called" || status == "Serving" || status == "Assembling" || status == "Ready" || status == "Active";
        }

        public static bool CanCancel(string status)
        {
            return IsReservation(status) && status != "Active";
        }

        public static string NextStatus(string status)
        {
            switch (status)
            {
                case "Pending":
                case "Called":
                case "Serving":
                    return "Assembling";
                case "Assembling":
                    return "Ready";
                case "Ready":
                    return "Active";
                case "Active":
                    return "Completed";
                default:
                    return null;
            }
        }

        public static void RequireReservation(ApplicationContext db, Equipment equipment, string startValue, string endValue, int orderId = 0)
        {
            if (equipment == null || (equipment.Status != "Available" && equipment.Status != "Rented"))
                throw new InvalidOperationException("Спорядження недоступне для оренди.");
            DateTime start = ReadDate(startValue);
            DateTime end = PeriodEnd(start, ReadDate(endValue));
            List<Rental> orders = db.Rentals.AsNoTracking().Where(x => x.EquipmentId == equipment.Id && x.Id != orderId && x.Status != "Completed" && x.Status != "Cancelled").ToList();
            foreach (Rental order in orders)
            {
                if (!IsReservation(order.Status))
                    continue;
                DateTime bookedStart = ReadDate(order.RentDate);
                DateTime bookedEnd = PeriodEnd(bookedStart, ReadDate(order.ReturnDate));
                if (order.Status == "Active" && bookedEnd <= DateTime.Today)
                    throw new InvalidOperationException("Попередню оренду прострочено. Дату доступності уточнює касир.");
                if (start < bookedEnd && bookedStart < end)
                    throw new InvalidOperationException("Спорядження вже заброньовано на ці дати. Оберіть інший період.");
            }

            if (equipment.Status == "Rented" && !orders.Any(x => x.Status == "Active") && !db.Rentals.Any(x => x.Id == orderId && x.Status == "Active"))
                throw new InvalidOperationException("Повернення спорядження ще не підтверджено. Зверніться до касира.");
        }

        public static DateTime? AvailableFrom(Equipment equipment, IEnumerable<Rental> rentals)
        {
            if (equipment.Status != "Available" && equipment.Status != "Rented")
                return null;
            List<Rental> orders = rentals.Where(x => IsReservation(x.Status)).ToList();
            if (equipment.Status == "Rented" && !orders.Any(x => x.Status == "Active"))
                return null;
            List<Tuple<DateTime, DateTime>> periods = new List<Tuple<DateTime, DateTime>>();
            foreach (Rental order in orders)
            {
                DateTime start;
                DateTime end;
                try
                {
                    start = ReadDate(order.RentDate);
                    end = PeriodEnd(start, ReadDate(order.ReturnDate));
                }
                catch (ArgumentException)
                {
                    return null;
                }

                if (order.Status == "Active" && end <= DateTime.Today)
                    return null;
                if (order.Status == "Active" || order.Status == "Assembling" || order.Status == "Ready")
                    start = start < DateTime.Today ? start : DateTime.Today;
                periods.Add(Tuple.Create(start, end));
            }

            DateTime available = DateTime.Today;
            foreach (Tuple<DateTime, DateTime> period in periods.OrderBy(x => x.Item1))
            {
                if (period.Item1 <= available && period.Item2 > available)
                    available = period.Item2;
            }

            return available;
        }
    }
}
