using SkiaSharp;
using ScheduleBot.Data;
using ScheduleBot.Models;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace ScheduleBot.Services
{
    public class ScheduleImageGenerator
    {
        private readonly BotDbContext _context;

        public ScheduleImageGenerator(BotDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public ScheduleImageGenerator() : this(new BotDbContext())
        {
        }

        public async Task<Stream> GenerateScheduleImage(DateTime weekStart)
        {
            try
            {
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

                if (employees == null || employees.Count == 0)
                {
                    return CreateSimpleImage("Нет зарегистрированных сотрудников");
                }

                int cellWidth = 120;
                int cellHeight = 40;
                int headerHeight = 80;
                int leftMargin = 220;
                int rowCount = employees.Count;

                int width = leftMargin + (7 * cellWidth);
                int height = headerHeight + (rowCount * cellHeight) + 50;

                using var bitmap = new SKBitmap(width, height);
                using var canvas = new SKCanvas(bitmap);

                // Белый фон
                canvas.Clear(SKColors.White);

                // Заголовок
                using var headerPaint = new SKPaint
                {
                    Color = SKColors.Black,
                    IsAntialias = true,
                    TextSize = 14,
                    Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
                };

                var weekEnd = weekStart.AddDays(6);
                canvas.DrawText(
                    $"Расписание на неделю: {weekStart:dd.MM.yyyy} - {weekEnd:dd.MM.yyyy}",
                    10, 30, headerPaint);

                // Заголовки дней
                string[] dayNames = { "ПН", "ВТ", "СР", "ЧТ", "ПТ", "СБ", "ВС" };
                using var dayPaint = new SKPaint
                {
                    Color = SKColors.Black,
                    IsAntialias = true,
                    TextSize = 10,
                    Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
                    TextAlign = SKTextAlign.Center
                };

                for (int i = 0; i < 7; i++)
                {
                    var dayDate = weekStart.AddDays(i);
                    int x = leftMargin + (i * cellWidth);
                    int y = 45;

                    // Рамка
                    using var strokePaint = new SKPaint
                    {
                        Style = SKPaintStyle.Stroke,
                        Color = SKColors.Black,
                        StrokeWidth = 1
                    };
                    var rect = new SKRect(x, y, x + cellWidth, y + 35);
                    canvas.DrawRect(rect, strokePaint);

                    // Текст
                    canvas.DrawText(
                        $"{dayNames[i]}\n{dayDate:dd.MM}",
                        x + cellWidth / 2, y + 18,
                        dayPaint);
                }

                // Сотрудники и пожелания
                using var employeePaint = new SKPaint
                {
                    Color = SKColors.Black,
                    IsAntialias = true,
                    TextSize = 9,
                    Typeface = SKTypeface.FromFamilyName("Arial"),
                    TextAlign = SKTextAlign.Left
                };

                using var preferencePaint = new SKPaint
                {
                    Color = SKColors.Black,
                    IsAntialias = true,
                    TextSize = 8,
                    Typeface = SKTypeface.FromFamilyName("Arial"),
                    TextAlign = SKTextAlign.Center
                };

                using var strokePaint2 = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = SKColors.Black,
                    StrokeWidth = 1
                };

                for (int empIndex = 0; empIndex < employees.Count; empIndex++)
                {
                    var employee = employees[empIndex];
                    int yPos = 80 + (empIndex * cellHeight);

                    // Имя сотрудника
                    var nameRect = new SKRect(0, yPos, leftMargin - 10, yPos + cellHeight);
                    canvas.DrawRect(nameRect, strokePaint2);

                    string displayName = employee.FullName.Length > 25
                        ? employee.FullName.Substring(0, 22) + "..."
                        : employee.FullName;

                    canvas.DrawText(displayName, 5, yPos + cellHeight / 2 + 3, employeePaint);

                    // Пожелания по дням
                    for (int day = 0; day < 7; day++)
                    {
                        DayOfWeek currentDay = (DayOfWeek)((day + 1) % 7);
                        var preference = preferences.FirstOrDefault(p =>
                            p.EmployeeId == employee.Id && p.Day == currentDay);

                        int x = leftMargin + (day * cellWidth);
                        var cellRect = new SKRect(x, yPos, x + cellWidth, yPos + cellHeight);

                        // Рамка
                        canvas.DrawRect(cellRect, strokePaint2);

                        var (displayText, bgColor) = GetPreferenceDisplay(preference?.PreferenceText);

                        // Заливка цветом
                        using var bgPaint = new SKPaint
                        {
                            Style = SKPaintStyle.Fill,
                            Color = bgColor
                        };
                        canvas.DrawRect(cellRect, bgPaint);

                        // Текст пожелания
                        if (!string.IsNullOrEmpty(displayText) && displayText != "❓")
                        {
                            canvas.DrawText(
                                displayText,
                                x + cellWidth / 2, yPos + cellHeight / 2 + 3,
                                preferencePaint);
                        }
                        else
                        {
                            // Для эмодзи используем чуть больший размер
                            using var emojiPaint = new SKPaint
                            {
                                Color = SKColors.Black,
                                IsAntialias = true,
                                TextSize = 12,
                                TextAlign = SKTextAlign.Center
                            };
                            canvas.DrawText("❓", x + cellWidth / 2, yPos + cellHeight / 2 + 4, emojiPaint);
                        }
                    }
                }

                // Легенда
                DrawLegend(canvas, 10, height - 40);

                // Сохранение в поток
                using var image = SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                var stream = new MemoryStream();
                data.SaveTo(stream);
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
            try
            {
                using var bitmap = new SKBitmap(600, 200);
                using var canvas = new SKCanvas(bitmap);
                canvas.Clear(SKColors.White);

                using var paint = new SKPaint
                {
                    Color = SKColors.Red,
                    IsAntialias = true,
                    TextSize = 12,
                    Typeface = SKTypeface.FromFamilyName("Arial")
                };

                canvas.DrawText(message, 20, 50, paint);

                using var image = SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                var stream = new MemoryStream();
                data.SaveTo(stream);
                stream.Position = 0;
                return stream;
            }
            catch
            {
                return new MemoryStream();
            }
        }

        private (string text, SKColor color) GetPreferenceDisplay(string? preferenceText)
        {
            if (string.IsNullOrWhiteSpace(preferenceText))
                return ("", SKColors.White);

            string lowerText = preferenceText.ToLower().Trim();

            if (lowerText == "любое время" || lowerText == "любое")
                return ("✅", new SKColor(200, 255, 200));

            if (lowerText == "выходной")
                return ("❌", new SKColor(255, 200, 200));

            string displayText = preferenceText.Length > 10
                ? preferenceText.Substring(0, 8) + ".."
                : preferenceText;

            return (displayText, new SKColor(200, 200, 255));
        }

        private void DrawLegend(SKCanvas canvas, int x, int y)
        {
            using var paint = new SKPaint
            {
                Color = SKColors.Black,
                IsAntialias = true,
                TextSize = 8,
                Typeface = SKTypeface.FromFamilyName("Arial")
            };

            canvas.DrawText("✅ Любое", x, y, paint);
            canvas.DrawText("❌ Выходной", x + 100, y, paint);
            canvas.DrawText("❓ Не указано", x + 200, y, paint);
            canvas.DrawText("🔵 Время", x + 300, y, paint);
        }
    }
}