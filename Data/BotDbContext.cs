using Microsoft.EntityFrameworkCore;
using ScheduleBot.Models;
using System.IO;

namespace ScheduleBot.Data
{
    public class BotDbContext : DbContext
    {
        public DbSet<Employee> Employees { get; set; }
        public DbSet<ShiftPreference> ShiftPreferences { get; set; }

        // Добавляем конструктор для возможности внедрения зависимостей
        public BotDbContext()
        {
            // Автоматическое создание базы данных при первом запуске
            Database.EnsureCreated();
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // Критически важно: определяем путь к базе данных
            // На сервере Bothost это будет папка с приложением
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string dbPath = Path.Combine(baseDirectory, "ScheduleBot.db");

            // Логируем путь для отладки (пригодится на сервере)
            Console.WriteLine($"📁 Путь к базе данных: {dbPath}");

            // Используем SQLite с пулом соединений для надежности
            optionsBuilder.UseSqlite($"Data Source={dbPath};Pooling=true;");

            // Опция для детального логирования запросов (можно отключить на сервере)
            // optionsBuilder.LogTo(Console.WriteLine, LogLevel.Information);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Настройка связи ShiftPreference → Employee
            modelBuilder.Entity<ShiftPreference>()
                .HasOne(sp => sp.Employee)
                .WithMany()
                .HasForeignKey(sp => sp.EmployeeId)
                .OnDelete(DeleteBehavior.Cascade);

            // Уникальный индекс для предотвращения дубликатов
            modelBuilder.Entity<ShiftPreference>()
                .HasIndex(sp => new { sp.EmployeeId, sp.WeekStart, sp.Day })
                .IsUnique();

            // Дополнительные индексы для производительности
            modelBuilder.Entity<Employee>()
                .HasIndex(e => e.TelegramId)
                .IsUnique();

            modelBuilder.Entity<ShiftPreference>()
                .HasIndex(p => p.WeekStart);
        }
    }
}