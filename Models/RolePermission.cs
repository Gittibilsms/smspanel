namespace GittBilSmsCore.Models
{
    public class RolePermission
    {
        public int RoleId { get; set; }
        public Role Role { get; set; }

        public string Module { get; set; }
        public string Type { get; set; }

        public bool Special { get; set; }
        public bool CanRead { get; set; }
        public bool CanCreate { get; set; }
        public bool CanEdit { get; set; }
        public bool CanDelete { get; set; }

        public bool? RequiresMainUser { get; set; }

    }
}
