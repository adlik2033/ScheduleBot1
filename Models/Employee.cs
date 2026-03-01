namespace ScheduleBot.Models
{
    public class Employee
    {
        public int Id { get; set; }
        public long TelegramId { get; set; }
        public string Username { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Position { get; set; } = "";
        public DateTime RegisteredAt { get; set; } = DateTime.Now;
        public bool IsActive { get; set; } = true;
    }
}