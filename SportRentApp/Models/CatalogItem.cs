using System;
using System.Globalization;
using System.Windows.Media.Imaging;

namespace SportRentApp
{
    public sealed class CatalogItem
    {
        internal Equipment Record { get; }
        public int Id => Record.Id;
        public string Name => Record.Name;
        public string Category => Record.Category;
        public string Description => string.IsNullOrWhiteSpace(Record.Description) ? "Опис ще не додано." : Record.Description;
        public string PriceLabel => Record.PricePerDay.ToString("0.##", CultureInfo.GetCultureInfo("uk-UA")) + " грн / добу";

        public string StatusLabel
        {
            get
            {
                switch (Record.Status)
                {
                    case "Available":
                        return "Доступний";
                    case "Unavailable":
                        return "Зараз недоступний";
                    case "Rented":
                        return "В оренді";
                    case "Maintenance":
                        return "На обслуговуванні";
                    case "Lost":
                        return "Втрачено";
                    default:
                        return Record.Status;
                }
            }
        }

        public BitmapSource Photo { get; }
        public DateTime? AvailableFrom { get; }
        public bool CanReserve { get; }

        public string AvailabilityLabel
        {
            get
            {
                if (!AvailableFrom.HasValue)
                    return "Дату доступності уточнює касир.";
                if (AvailableFrom.Value <= DateTime.Today)
                    return "Доступний сьогодні. Період перевіряється під час бронювання.";
                return "Очікується з " + AvailableFrom.Value.ToString("dd.MM.yyyy") + ". Можна обрати майбутні дати.";
            }
        }

        public CatalogItem(Equipment record, bool loadPhoto = true, DateTime? availableFrom = null, bool? canReserve = null)
        {
            Record = record;
            Photo = loadPhoto ? CatalogService.LoadPhoto(record.ImagePath) : null;
            AvailableFrom = canReserve.HasValue ? availableFrom : record.Status == "Available" ? (DateTime?)DateTime.Today : null;
            CanReserve = canReserve ?? record.Status == "Available";
        }
    }
}
