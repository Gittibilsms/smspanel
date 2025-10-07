using GittBilSmsCore.Models;

namespace GittBilSmsCore.ViewModels
{
    public class UserIndexViewModel
    {
        public List<User> Users { get; set; }
        public User NewUser { get; set; } = new User();
        public List<Role> Roles { get; set; }
    }
}
