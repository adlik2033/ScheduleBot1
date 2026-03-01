using System.Drawing;
using System.Drawing.Imaging;
using ScheduleBot.Data;
using ScheduleBot.Models;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace ScheduleBot.Services
{
    public class ScheduleImageGenerator
    {
        private readonly BotDbContext _context;

        // Внедрение контекста через конструктор - это правильный подход
        public ScheduleImageGenerator(BotDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<Stream> GenerateScheduleImage(DateTime weekStart)
        {
            try
            {
                // Явно загружаем данные с отслеживанием (AsNoTracking для производительности)
                var employees = await _context.Employees
                    .AsNoTracking()
                    .Where(e => e.IsActive)
                    .OrderBy(e => e.FullName)
                    .ToListAsync();

                var preferences = await _context.ShiftPreferences
                    .AsNoTracking()
                    .Where(p => p.WeekStart == weekStart)
                    .Include(p => p.Employee)
                    .ToListAsync();

                // Проверка на отсутствие сотрудников
                if (employees == null || employees.Count == 0)
                {
                    return CreateSimpleImage("Нет зарегистрированных сотрудников");
                }

                // Параметры изображения
                int cellWidth = 120;
                int cellHeight = 40;
                int headerHeight = 80;
                int leftMargin = 220; // Увеличен для длинных имен
                int rowCount = employees.Count;

                int width = leftMargin + (7 * cellWidth);
                int height = headerHeight + (rowCount * cellHeight) + 50; // +50 для легенды

                using var bitmap = new Bitmap(width, height);
                using var graphics = Graphics.FromImage(bitmap);

                // Настройки графики
                graphics.Clear(Color.White);
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                // Заголовок
                var headerFont = new Font("Arial", 14, FontStyle.Bold);
                var weekEnd = weekStart.AddDays(6);
                graphics.DrawString(
                    $"Расписание на неделю: {weekStart:dd.MM.yyyy} - {weekEnd:dd.MM.yyyy}",
                    headerFont, Brushes.Black, new PointF(10, 10));

                // Заголовки дней недели
                string[] dayNames = { "ПН", "ВТ", "СР", "ЧТ", "ПТ", "СБ", "ВС" };
                var dayFont = new Font("Arial", 10, FontStyle.Bold);
                var dayFormat = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };

                for (int i = 0; i < 7; i++)
                {
                    var dayDate = weekStart.AddDays(i);
                    var rect = new Rectangle(
                        leftMargin + (i * cellWidth),
                        45,
                        cellWidth,
                        35);

                    graphics.DrawRectangle(Pens.Black, rect);
                    graphics.DrawString(
                        $"{dayNames[i]}\n{dayDate:dd.MM}",
                        dayFont,
                        Brushes.Black,
                        rect,
                        dayFormat);
                }

                // Отрисовка данных сотрудников
                var employeeFont = new Font("Arial", 9);
                var preferenceFont = new Font("Arial", 8);
                var nameFormat = new StringFormat
                {
                    Alignment = StringAlignment.Near,
                    LineAlignment = StringAlignment.Center
                };

                for (int empIndex = 0; empIndex < employees.Count; empIndex++)
                {
                    var employee = employees[empIndex];
                    int yPos = 80 + (empIndex * cellHeight);

                    // Ячейка с именем сотрудника
                    var nameRect = new Rectangle(0, yPos, leftMargin - 10, cellHeight);
                    graphics.DrawRectangle(Pens.Black, nameRect);

                    string displayName = employee.FullName.Length > 25
                        ? employee.FullName.Substring(0, 22) + "..."
                        : employee.FullName;

                    graphics.DrawString(
                        displayName,
                        employeeFont,
                        Brushes.Black,
                        new RectangleF(nameRect.X + 5, nameRect.Y, nameRect.Width - 10, nameRect.Height),
                        nameFormat);

                    // Ячейки с пожеланиями по дням
                    for (int day = 0; day < 7; day++)
                    {
                        DayOfWeek currentDay = (DayOfWeek)((day + 1) % 7); // Преобразование в DayOfWeek
                        var preference = preferences.FirstOrDefault(p =>
                            p.EmployeeId == employee.Id && p.Day == currentDay);

                        var cellRect = new Rectangle(
                            leftMargin + (day * cellWidth),
                            yPos,
                            cellWidth,
                            cellHeight);

                        graphics.DrawRectangle(Pens.Black, cellRect);

                        var (displayText, bgColor) = GetPreferenceDisplay(preference?.PreferenceText);

                        // Заливка цветом
                        using (var brush = new SolidBrush(bgColor))
                        {
                            graphics.FillRectangle(brush, cellRect);
                        }

                        // Текст пожелания
                        var textFormat = new StringFormat
                        {
                            Alignment = StringAlignment.Center,
                            LineAlignment = StringAlignment.Center
                        };

                        graphics.DrawString(
                            displayText,
                            preferenceFont,
                            Brushes.Black,
                            cellRect,
                            textFormat);
                    }
                }

                // Легенда внизу
                DrawLegend(graphics, 10, height - 40);

                // Сохранение в поток
                var stream = new MemoryStream();
                bitmap.Save(stream, ImageFormat.Png);
                stream.Position = 0;
                return stream;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка генерации изображения: {ex.Message}");
                return CreateSimpleImage($"Ошибка генерации:\n{ex.Message}");
            }
        }

        private Stream CreateSimpleImage(string message)
        {
            using var bitmap = new Bitmap(600, 200);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.White);
            graphics.DrawString(message, new Font("Arial", 12), Brushes.Red, 20, 50);

            var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            stream.Position = 0;
            return stream;
        }

        private (string text, Color color) GetPreferenceDisplay(string? preferenceText)
        {
            if (string.IsNullOrWhiteSpace(preferenceText))
                return ("❓", Color.White);

            string lowerText = preferenceText.ToLower().Trim();

            if (lowerText == "любое время" || lowerText == "любое")
                return ("✅ Любое", Color.FromArgb(200, 255, 200));

            if (lowerText == "выходной")
                return ("❌ Выходной", Color.FromArgb(255, 200, 200));

            // Сокращаем длинный текст
            string displayText = preferenceText.Length > 15
                ? preferenceText.Substring(0, 12) + ".."
                : preferenceText;

            return (displayText, Color.FromArgb(200, 200, 255));
        }

        private void DrawLegend(Graphics graphics, int x, int y)
        {
            var legendFont = new Font("Arial", 8);
            graphics.DrawString("✅ Любое время", legendFont, Brushes.Black, x, y);
            graphics.DrawString("❌ Выходной", legendFont, Brushes.Black, x + 120, y);
            graphics.DrawString("❓ Не указано", legendFont, Brushes.Black, x + 220, y);
            graphics.DrawString("🔵 Указано время", legendFont, Brushes.Black, x + 320, y);
        }
    }
}