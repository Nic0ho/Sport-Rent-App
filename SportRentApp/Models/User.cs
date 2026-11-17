using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SportRentApp
{
    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string FullName { get; set; }
        public string Phone { get; set; }
        public string Role { get; set; }
        public string Notes { get; set; }
        public string Bio { get; set; }
        public string PhotoPath { get; set; }
        public bool IsOnShift { get; set; } = true;
        public string CreatedAt { get; set; }
        public string Status { get; set; } = "Active";
        public string Tags { get; set; } = "";
        public string ShopCode { get; set; }

        [System.ComponentModel.DataAnnotations.ConcurrencyCheck]
        public int Version { get; set; }
        public int? ApprovedBy { get; set; }
        public string UpdatedAt { get; set; }

        public User()
        {
        }

        public User(string username, string password, string fullName, string phone, string role = "Client")
        {
            Username = username;
            Password = password;
            FullName = fullName;
            Phone = phone;
            Role = role;
        }
    }
}
