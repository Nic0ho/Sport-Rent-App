using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SportRentApp
{
    [System.ComponentModel.DataAnnotations.Schema.Table("Equipment")]
    public class Equipment
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public string Description { get; set; }
        public double PricePerDay { get; set; }
        public string Status { get; set; }
        public string ImagePath { get; set; }

        public Equipment()
        {
        }
    }
}
