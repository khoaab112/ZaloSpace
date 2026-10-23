using System;

namespace ZaloSpaceManager.Models
{
    public class Profile
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string DataDir { get; set; }
        public bool AutoStart { get; set; }
        public string AvatarPath { get; set; }
        public string IconPath { get; set; }
        public DateTime CreatedAt { get; set; }

        public Profile()
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 8);
            Name = "Zalo Mới";
            AutoStart = false;
            CreatedAt = DateTime.Now;
        }
    }
}
