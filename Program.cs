using ScheduleBot.Services;
using System.Text;

namespace ScheduleBot
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Настройка кодировки для корректного отображения русского текста
            Console.OutputEncoding = Encoding.UTF8;

            Console.WriteLine("=".PadRight(50, '='));
            Console.WriteLine("🚀 ЗАПУСК БОТА РАСПИСАНИЯ");
            Console.WriteLine("=".PadRight(50, '='));

            try
            {
                // Вместо Environment.GetEnvironmentVariable
                string? mainBotToken = null;

                // Пробуем прочитать из файла token.txt
                if (File.Exists("token.txt"))
                {
                    mainBotToken = File.ReadAllText("token.txt").Trim();
                    Console.WriteLine("📄 Токен загружен из файла token.txt");
                }
                else
                {
                    // Пробуем из переменной окружения (для совместимости)
                    mainBotToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");

                    if (!string.IsNullOrEmpty(mainBotToken))
                    {
                        Console.WriteLine("🔑 Токен загружен из переменной окружения");
                    }
                }
                // 2. Если не нашли в окружении, пробуем прочитать из файла (для локальной разработки)
                if (string.IsNullOrEmpty(mainBotToken) && File.Exists("token.txt"))
                {
                    mainBotToken = File.ReadAllText("token.txt").Trim();
                    Console.WriteLine("📄 Токен загружен из файла token.txt");
                }

                // 3. Если всё еще нет токена - ошибка
                if (string.IsNullOrEmpty(mainBotToken))
                {
                    Console.WriteLine("❌ КРИТИЧЕСКАЯ ОШИБКА: Токен бота не найден!");
                    Console.WriteLine("\n🔧 Инструкция:");
                    Console.WriteLine("   На сервере: установите переменную окружения TELEGRAM_BOT_TOKEN");
                    Console.WriteLine("   Локально: создайте файл token.txt с токеном в папке с программой");
                    Console.WriteLine("\nНажмите любую клавишу для выхода...");
                    Console.ReadKey();
                    return;
                }

                // Проверяем длину токена (базовая валидация)
                if (mainBotToken.Length < 40)
                {
                    Console.WriteLine("⚠️ Внимание: Токен подозрительно короткий. Проверьте правильность!");
                }

                Console.WriteLine($"🔑 Токен загружен (первые символы: {mainBotToken.Substring(0, 8)}...)");

                // Создаем сервис бота
                var botService = new TelegramBotService(mainBotToken);

                // Тестируем подключение к Telegram API
                Console.WriteLine("🔄 Проверка подключения к Telegram API...");

                if (await botService.TestBotTokenAsync())
                {
                    Console.WriteLine("✅ Подключение успешно!");
                    Console.WriteLine("🟢 Бот запускается и будет работать 24/7...");
                    Console.WriteLine("=".PadRight(50, '='));

                    // Запускаем бота
                    await botService.StartBotAsync();
                }
                else
                {
                    Console.WriteLine("❌ Ошибка подключения к Telegram API!");
                    Console.WriteLine("   Проверьте:");
                    Console.WriteLine("   1. Правильность токена");
                    Console.WriteLine("   2. Доступ к api.telegram.org (может быть заблокирован)");
                    Console.WriteLine("   3. Не забанен ли бот");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥 Фатальная ошибка: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }

            Console.WriteLine("\n❌ Бот остановлен. Нажмите любую клавишу...");
            Console.ReadKey();
        }
    }
}