namespace ScheduleBot.Models
{
    public class ShiftPreference
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public virtual Employee Employee { get; set; } = null!;
        public DateTime WeekStart { get; set; }
        public DayOfWeek Day { get; set; }
        public string PreferenceText { get; set; } = "";
        public DateTime SubmittedAt { get; set; } = DateTime.Now;
    }
}