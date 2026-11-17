using System;

namespace SportRentApp
{
    public static class UserSession
    {
        public static User CurrentUser { get; set; }

        public static UserRole Role
        {
            get
            {
                if (CurrentUser != null && Enum.TryParse(CurrentUser.Role, out UserRole role) && Enum.IsDefined(typeof(UserRole), role) && Enum.GetName(typeof(UserRole), role) == CurrentUser.Role)
                    return role;
                return UserRole.Guest;
            }
        }

        public static bool IsAdmin => Role == UserRole.Admin;
        public static bool IsEmployee => Role == UserRole.Employee;
        public static bool IsClient => Role == UserRole.Client;
        public static bool IsKiosk => Role == UserRole.Kiosk;
    }
}
