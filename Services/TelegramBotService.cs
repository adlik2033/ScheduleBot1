using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using ScheduleBot.Data;
using ScheduleBot.Models;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace ScheduleBot.Services
{
    public class TelegramBotService
    {
        private readonly ITelegramBotClient _botClient;
        private readonly BotDbContext _context;
        private readonly ScheduleImageGenerator _imageGenerator; // Добавлено
        private readonly string _adminUsername = "@adlik2033";
        private long? _adminChatId = null;

        // Словари для состояний
        private readonly Dictionary<long, (DayOfWeek day, DateTime weekStart)> _waitingForText = new();
        private readonly Dictionary<long, List<DayOfWeek>> _selectedDaysOff = new();
        private readonly Dictionary<long, bool> _waitingForUniformSchedule = new();
        private readonly Dictionary<long, bool> _waitingForFeedbackText = new();
        private readonly Dictionary<long, bool> _waitingForBroadcastText = new();

        private bool _isCollectingPreferences = true;

        // ИСПРАВЛЕННЫЙ КОНСТРУКТОР
        public TelegramBotService(string token)
        {
            _botClient = new TelegramBotClient(token);
            _context = new BotDbContext(); // Создаем контекст
            _imageGenerator = new ScheduleImageGenerator(_context); // Передаем контекст в генератор
            _context.Database.EnsureCreated(); // Гарантируем создание БД
        }
        private void Log(string message, ConsoleColor color = ConsoleColor.Gray)
        {
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
            Console.ForegroundColor = originalColor;
        }

        public async Task StartBotAsync()
        {
            try
            {
                Log("🔄 Запуск бота...", ConsoleColor.Cyan);
                var me = await _botClient.GetMeAsync();
                Log($"✅ Бот @{me.Username} успешно запущен!", ConsoleColor.Green);

                var receiverOptions = new ReceiverOptions
                {
                    AllowedUpdates = Array.Empty<UpdateType>()
                };

                _botClient.StartReceiving(
                    updateHandler: HandleUpdateAsync,
                    pollingErrorHandler: HandlePollingErrorAsync,
                    receiverOptions: receiverOptions
                );

                Log("📢 Бот слушает сообщения...", ConsoleColor.Yellow);
                Log($"👑 Администратор: {_adminUsername}", ConsoleColor.Magenta);
                Log($"📊 Статус сбора: {(_isCollectingPreferences ? "ОТКРЫТ" : "ЗАКРЫТ")}", ConsoleColor.Cyan);

                await Task.Delay(-1);
            }
            catch (Exception ex)
            {
                Log($"💥 Критическая ошибка запуска: {ex.Message}", ConsoleColor.Red);
            }
        }

        public async Task<bool> TestBotTokenAsync()
        {
            try
            {
                var me = await _botClient.GetMeAsync();
                Log($"✅ Тест подключения успешен: @{me.Username}", ConsoleColor.Green);
                return true;
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка подключения: {ex.Message}", ConsoleColor.Red);
                return false;
            }
        }

        private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken cancellationToken)
        {
            try
            {
                if (update.Message is { } message)
                {
                    await HandleMessageAsync(message, cancellationToken);
                }
                else if (update.CallbackQuery is { } callbackQuery)
                {
                    await HandleCallbackQueryAsync(callbackQuery, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка обработки обновления: {ex.Message}", ConsoleColor.Red);
            }
        }

        private Task HandlePollingErrorAsync(ITelegramBotClient bot, Exception exception, CancellationToken cancellationToken)
        {
            Log($"⚠️ Ошибка polling: {exception.Message}", ConsoleColor.Yellow);
            return Task.CompletedTask;
        }

        private async Task HandleMessageAsync(Message message, CancellationToken cancellationToken)
        {
            var chatId = message.Chat.Id;
            var text = message.Text ?? "";
            var username = message.From?.Username ?? "без username";
            var firstName = message.From?.FirstName ?? "";
            var lastName = message.From?.LastName ?? "";
            var fullName = $"{firstName} {lastName}".Trim();

            Log($"📨 Сообщение от {fullName} (@{username}): {text}", ConsoleColor.White);

            if (username == "adlik2033" && _adminChatId == null)
            {
                _adminChatId = chatId;
                Log($"👑 Администратор авторизован! ChatID: {chatId}", ConsoleColor.Magenta);
            }

            try
            {
                if (_waitingForBroadcastText.ContainsKey(chatId) && _waitingForBroadcastText[chatId])
                {
                    await SendBroadcastToAllAsync(chatId, text, cancellationToken);
                    return;
                }

                if (_waitingForFeedbackText.ContainsKey(chatId) && _waitingForFeedbackText[chatId])
                {
                    await SaveFeedbackAsync(chatId, text, message.From, cancellationToken);
                    return;
                }

                if (_waitingForText.ContainsKey(chatId))
                {
                    await SaveTextPreferenceAsync(chatId, text, cancellationToken);
                    return;
                }

                if (_waitingForUniformSchedule.ContainsKey(chatId))
                {
                    await SaveUniformScheduleAsync(chatId, text, cancellationToken);
                    return;
                }

                switch (text)
                {
                    case "/start":
                        await ShowMainMenuAsync(chatId, message.From, cancellationToken);
                        break;
                    case "📋 Регистрация":
                        await StartRegistrationAsync(chatId, username, fullName, cancellationToken);
                        break;
                    case "📅 Заполнить пожелания (разные)":
                        await CheckCollectionStatusAndExecute(chatId, () => ShowScheduleMenuAsync(chatId, cancellationToken), cancellationToken);
                        break;
                    case "📅 Заполнить пожелания (одинаковые)":
                        await CheckCollectionStatusAndExecute(chatId, () => StartUniformScheduleAsync(chatId, cancellationToken), cancellationToken);
                        break;
                    case "👀 Мои пожелания":
                        await ShowCurrentPreferencesAsync(chatId, cancellationToken);
                        break;
                    case "📊 Таблица расписания":
                        await GenerateAndSendScheduleAsync(chatId, cancellationToken);
                        break;
                    case "💬 Обратная связь":
                        await StartFeedbackAsync(chatId, cancellationToken);
                        break;
                    case "👑 Админ панель":
                        if (username == "adlik2033")
                            await ShowAdminPanelAsync(chatId, cancellationToken);
                        break;
                    case "📊 Статус сбора":
                        if (username == "adlik2033")
                            await ShowCollectionStatusAsync(chatId, cancellationToken);
                        break;
                    case "🔄 Изменить статус":
                        if (username == "adlik2033")
                            await ToggleCollectionStatusAsync(chatId, cancellationToken);
                        break;
                    case "🔔 Уведомить всех":
                        if (username == "adlik2033")
                            await StartBroadcastAsync(chatId, cancellationToken);
                        break;
                    case "👥 Список сотрудников":
                        if (username == "adlik2033")
                            await ShowEmployeesListAsync(chatId, cancellationToken);
                        break;
                    case "🔙 Назад в меню":
                        await ShowMainMenuAsync(chatId, message.From, cancellationToken);
                        break;
                    default:
                        if (!string.IsNullOrEmpty(text) && !text.StartsWith("/"))
                        {
                            await ShowMainMenuAsync(chatId, message.From, cancellationToken);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка обработки сообщения: {ex.Message}", ConsoleColor.Red);
                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "❌ Произошла ошибка. Попробуйте позже.",
                    cancellationToken: cancellationToken);
            }
        }

        private async Task ShowMainMenuAsync(long chatId, User? user, CancellationToken cancellationToken)
        {
            var isAdmin = user?.Username == "adlik2033";
            var status = _isCollectingPreferences ? "🟢 ОТКРЫТ" : "🔴 ЗАКРЫТ";

            var buttons = new List<KeyboardButton[]>();

            if (isAdmin)
            {
                buttons.Add(new[] { new KeyboardButton("👑 Админ панель") });
            }

            buttons.AddRange(new[]
            {
                new[] { new KeyboardButton("📋 Регистрация") },
                new[] { new KeyboardButton("📅 Заполнить пожелания (разные)") },
                new[] { new KeyboardButton("📅 Заполнить пожелания (одинаковые)") },
                new[] { new KeyboardButton("👀 Мои пожелания") },
                new[] { new KeyboardButton("📊 Таблица расписания") },
                new[] { new KeyboardButton("💬 Обратная связь") }
            });

            var keyboard = new ReplyKeyboardMarkup(buttons)
            {
                ResizeKeyboard = true,
                OneTimeKeyboard = false
            };

            var menuText = $"🏪 <b>Бот управления расписанием</b>\n\n" +
                          $"📊 <b>Статус сбора:</b> {status}\n\n" +
                          "<b>Доступные действия:</b>\n" +
                          "• 📋 Регистрация в системе\n" +
                          "• 📅 Заполнить пожелания (разные)\n" +
                          "• 📅 Заполнить пожелания (одинаковые)\n" +
                          "• 👀 Мои пожелания\n" +
                          "• 📊 Таблица расписания\n" +
                          "• 💬 Обратная связь";

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: menuText,
                parseMode: ParseMode.Html,
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);

            Log($"📋 Меню показано пользователю {chatId}", ConsoleColor.DarkGray);
        }

        private async Task ShowAdminPanelAsync(long chatId, CancellationToken cancellationToken)
        {
            var status = _isCollectingPreferences ? "🟢 ОТКРЫТ" : "🔴 ЗАКРЫТ";

            var keyboard = new ReplyKeyboardMarkup(new[]
            {
                new[] { new KeyboardButton("📊 Статус сбора"), new KeyboardButton("🔄 Изменить статус") },
                new[] { new KeyboardButton("🔔 Уведомить всех"), new KeyboardButton("👥 Список сотрудников") },
                new[] { new KeyboardButton("🔙 Назад в меню") }
            })
            {
                ResizeKeyboard = true
            };

            var employeesCount = await _context.Employees.CountAsync(cancellationToken);
            var preferencesCount = await _context.ShiftPreferences.CountAsync(cancellationToken);

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: $"👑 <b>Панель администратора</b>\n\n" +
                      $"📊 <b>Текущий статус:</b> {status}\n" +
                      $"👥 <b>Сотрудников:</b> {employeesCount}\n" +
                      $"📝 <b>Всего пожеланий:</b> {preferencesCount}\n\n" +
                      $"<b>Доступные команды:</b>\n" +
                      $"• Изменить статус - открыть/закрыть сбор\n" +
                      $"• Уведомить всех - массовая рассылка\n" +
                      $"• Список сотрудников - просмотр всех пользователей",
                parseMode: ParseMode.Html,
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);

            Log($"👑 Админ панель открыта", ConsoleColor.Magenta);
        }

        private async Task ShowCollectionStatusAsync(long chatId, CancellationToken cancellationToken)
        {
            var status = _isCollectingPreferences ? "🟢 ОТКРЫТ" : "🔴 ЗАКРЫТ";
            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: $"📊 <b>Статус сбора пожеланий:</b> {status}",
                parseMode: ParseMode.Html,
                cancellationToken: cancellationToken);
        }

        private async Task ToggleCollectionStatusAsync(long chatId, CancellationToken cancellationToken)
        {
            _isCollectingPreferences = !_isCollectingPreferences;
            var status = _isCollectingPreferences ? "🟢 ОТКРЫТ" : "🔴 ЗАКРЫТ";

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: $"✅ <b>Статус сбора изменен!</b>\n\nНовый статус: {status}",
                parseMode: ParseMode.Html,
                cancellationToken: cancellationToken);

            Log($"🔄 Админ изменил статус сбора на: {(_isCollectingPreferences ? "ОТКРЫТ" : "ЗАКРЫТ")}", ConsoleColor.Yellow);
        }

        private async Task StartBroadcastAsync(long chatId, CancellationToken cancellationToken)
        {
            _waitingForBroadcastText[chatId] = true;
            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "🔔 <b>Создание рассылки</b>\n\nВведите текст для отправки ВСЕМ сотрудникам:",
                parseMode: ParseMode.Html,
                cancellationToken: cancellationToken);

            Log($"📢 Админ начал создание рассылки", ConsoleColor.Yellow);
        }

        private async Task SendBroadcastToAllAsync(long adminChatId, string broadcastText, CancellationToken cancellationToken)
        {
            _waitingForBroadcastText.Remove(adminChatId);

            if (string.IsNullOrWhiteSpace(broadcastText) || broadcastText == "/cancel")
            {
                await _botClient.SendTextMessageAsync(
                    chatId: adminChatId,
                    text: "❌ Рассылка отменена",
                    cancellationToken: cancellationToken);
                await ShowAdminPanelAsync(adminChatId, cancellationToken);
                return;
            }

            var employees = await _context.Employees.Where(e => e.IsActive).ToListAsync(cancellationToken);
            var successCount = 0;
            var failCount = 0;

            await _botClient.SendTextMessageAsync(
                chatId: adminChatId,
                text: $"🔄 Начинаю рассылку {employees.Count} сотрудникам...",
                cancellationToken: cancellationToken);

            foreach (var employee in employees)
            {
                try
                {
                    await _botClient.SendTextMessageAsync(
                        chatId: employee.TelegramId,
                        text: $"🔔 <b>Уведомление от администратора:</b>\n\n{broadcastText}",
                        parseMode: ParseMode.Html,
                        cancellationToken: cancellationToken);

                    successCount++;
                    Log($"📤 Рассылка отправлена {employee.FullName}", ConsoleColor.DarkGreen);
                }
                catch (Exception ex)
                {
                    failCount++;
                    Log($"❌ Не удалось отправить {employee.FullName}: {ex.Message}", ConsoleColor.Red);
                }
            }

            await _botClient.SendTextMessageAsync(
                chatId: adminChatId,
                text: $"✅ <b>Рассылка завершена!</b>\n\n" +
                      $"📤 Отправлено: {successCount}\n" +
                      $"❌ Ошибок: {failCount}\n" +
                      $"👥 Всего: {employees.Count}",
                parseMode: ParseMode.Html,
                cancellationToken: cancellationToken);

            Log($"📢 Рассылка завершена. Успешно: {successCount}, Ошибок: {failCount}",
                failCount > 0 ? ConsoleColor.Yellow : ConsoleColor.Green);

            await ShowAdminPanelAsync(adminChatId, cancellationToken);
        }

        private async Task ShowEmployeesListAsync(long chatId, CancellationToken cancellationToken)
        {
            var employees = await _context.Employees.Where(e => e.IsActive).ToListAsync(cancellationToken);

            if (!employees.Any())
            {
                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "📭 Нет зарегистрированных сотрудников",
                    cancellationToken: cancellationToken);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"👥 <b>Список сотрудников</b> ({employees.Count} чел.)\n");

            foreach (var emp in employees.OrderBy(e => e.FullName))
            {
                var preferences = await _context.ShiftPreferences
                    .CountAsync(p => p.EmployeeId == emp.Id, cancellationToken);

                sb.AppendLine($"• <b>{emp.FullName}</b>");
                sb.AppendLine($"  🆔 ID: {emp.TelegramId}");
                sb.AppendLine($"  📱 @{emp.Username}");
                sb.AppendLine($"  📝 Пожеланий: {preferences}");
                sb.AppendLine($"  📅 Рег: {emp.RegisteredAt:dd.MM.yyyy}\n");
            }

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: sb.ToString(),
                parseMode: ParseMode.Html,
                cancellationToken: cancellationToken);

            Log($"👥 Админ запросил список сотрудников ({employees.Count} чел.)", ConsoleColor.DarkYellow);
        }

        private async Task CheckCollectionStatusAndExecute(long chatId, Func<Task> action, CancellationToken cancellationToken)
        {
            if (!_isCollectingPreferences)
            {
                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "❌ <b>Пожелания закрыты</b>\n\nСбор пожеланий временно не ведется.",
                    parseMode: ParseMode.Html,
                    cancellationToken: cancellationToken);
                return;
            }
            await action();
        }

        private async Task StartFeedbackAsync(long chatId, CancellationToken cancellationToken)
        {
            _waitingForFeedbackText[chatId] = true;
            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "💬 <b>Обратная связь</b>\n\nНапишите ваше сообщение администратору:",
                parseMode: ParseMode.Html,
                cancellationToken: cancellationToken);

            Log($"💬 Пользователь {chatId} начал обратную связь", ConsoleColor.Cyan);
        }

        private async Task SaveFeedbackAsync(long chatId, string feedbackText, User? user, CancellationToken cancellationToken)
        {
            _waitingForFeedbackText.Remove(chatId);

            if (string.IsNullOrWhiteSpace(feedbackText) || feedbackText == "/cancel")
            {
                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "❌ Отправка отменена",
                    cancellationToken: cancellationToken);
                await ShowMainMenuAsync(chatId, user, cancellationToken);
                return;
            }

            var employee = await _context.Employees.FirstOrDefaultAsync(e => e.TelegramId == chatId, cancellationToken);
            var employeeName = employee?.FullName ?? $"{user?.FirstName} {user?.LastName}".Trim();
            var username = employee?.Username ?? user?.Username ?? "нет username";

            Log($"💬 Получена обратная связь от {employeeName}: {feedbackText}", ConsoleColor.Cyan);

            if (_adminChatId.HasValue)
            {
                try
                {
                    await _botClient.SendTextMessageAsync(
                        chatId: _adminChatId.Value,
                        text: $"💬 <b>Новое сообщение обратной связи</b>\n\n" +
                              $"👤 <b>От:</b> {employeeName}\n" +
                              $"🆔 <b>ID:</b> {chatId}\n" +
                              $"📱 <b>Username:</b> @{username}\n" +
                              $"📝 <b>Сообщение:</b>\n{feedbackText}",
                        parseMode: ParseMode.Html,
                        cancellationToken: cancellationToken);

                    Log($"💬 Обратная связь переслана админу", ConsoleColor.Green);
                }
                catch (Exception ex)
                {
                    Log($"❌ Не удалось отправить обратную связь админу: {ex.Message}", ConsoleColor.Red);
                }
            }

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "✅ <b>Сообщение отправлено администратору!</b>",
                parseMode: ParseMode.Html,
                cancellationToken: cancellationToken);

            await ShowMainMenuAsync(chatId, user, cancellationToken);
        }

        private async Task StartRegistrationAsync(long chatId, string username, string fullName, CancellationToken cancellationToken)
        {
            var existingEmployee = await _context.Employees.FirstOrDefaultAsync(e => e.TelegramId == chatId, cancellationToken);

            if (existingEmployee != null)
            {
                await _botClient.SendTextMessageAsync(chatId, "✅ Вы уже зарегистрированы!", cancellationToken: cancellationToken);
                return;
            }

            var employee = new Employee
            {
                TelegramId = chatId,
                Username = username ?? "unknown",
                FullName = fullName,
                Position = "Сотрудник"
            };

            _context.Employees.Add(employee);
            await _context.SaveChangesAsync(cancellationToken);

            Log($"✅ Новый пользователь зарегистрирован: {fullName} (@{username})", ConsoleColor.Green);

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "🎉 <b>Регистрация завершена!</b>\n\nТеперь вы можете указывать пожелания.",
                parseMode: ParseMode.Html,
                cancellationToken: cancellationToken);

            if (_adminChatId.HasValue)
            {
                await _botClient.SendTextMessageAsync(
                    chatId: _adminChatId.Value,
                    text: $"✅ <b>Новый сотрудник зарегистрировался!</b>\n\n" +
                          $"👤 <b>Имя:</b> {fullName}\n" +
                          $"📱 <b>Username:</b> @{username}\n" +
                          $"🆔 <b>ID:</b> {chatId}",
                    parseMode: ParseMode.Html,
                    cancellationToken: cancellationToken);
            }
        }

        private async Task ShowScheduleMenuAsync(long chatId, CancellationToken cancellationToken)
        {
            var employee = await _context.Employees.FirstOrDefaultAsync(e => e.TelegramId == chatId, cancellationToken);
            if (employee == null)
            {
                await _botClient.SendTextMessageAsync(chatId, "Сначала зарегистрируйтесь!", cancellationToken: cancellationToken);
                return;
            }

            var weekStart = GetCurrentWeekStart();
            var preferences = await _context.ShiftPreferences
                .Where(p => p.EmployeeId == employee.Id && p.WeekStart == weekStart)
                .ToListAsync(cancellationToken);

            var inlineKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(GetDayButtonText(preferences, DayOfWeek.Monday), "preference_1"),
                    InlineKeyboardButton.WithCallbackData(GetDayButtonText(preferences, DayOfWeek.Tuesday), "preference_2")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(GetDayButtonText(preferences, DayOfWeek.Wednesday), "preference_3"),
                    InlineKeyboardButton.WithCallbackData(GetDayButtonText(preferences, DayOfWeek.Thursday), "preference_4")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(GetDayButtonText(preferences, DayOfWeek.Friday), "preference_5"),
                    InlineKeyboardButton.WithCallbackData(GetDayButtonText(preferences, DayOfWeek.Saturday), "preference_6")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(GetDayButtonText(preferences, DayOfWeek.Sunday), "preference_7")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("✅ Завершить", "finish_preferences")
                }
            });

            var weekEnd = weekStart.AddDays(6);
            var preferencesText = GetPreferencesText(preferences);

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: $"📅 <b>Пожелания на следующую неделю</b>\n" +
                     $"{weekStart:dd.MM.yyyy} - {weekEnd:dd.MM.yyyy}\n\n" +
                     $"{preferencesText}\n\n" +
                     "Выберите день:",
                parseMode: ParseMode.Html,
                replyMarkup: inlineKeyboard,
                cancellationToken: cancellationToken);
        }

        private async Task ShowPreferenceOptionsAsync(long chatId, DayOfWeek day, CancellationToken cancellationToken)
        {
            var employee = await _context.Employees.FirstOrDefaultAsync(e => e.TelegramId == chatId, cancellationToken);
            if (employee == null) return;

            var weekStart = GetCurrentWeekStart();
            _waitingForText[chatId] = (day, weekStart);

            var inlineKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("⌨️ Написать время", "text_input") },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("✅ Любое время", $"quick_{(int)day}_any"),
                    InlineKeyboardButton.WithCallbackData("❌ Выходной", $"quick_{(int)day}_dayoff")
                },
                new[] { InlineKeyboardButton.WithCallbackData("↩️ Назад", "back_to_days") }
            });

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: $"🗓️ <b>{GetDayName(day)}</b>\n\nВыберите вариант:",
                parseMode: ParseMode.Html,
                replyMarkup: inlineKeyboard,
                cancellationToken: cancellationToken);
        }

        private async Task SaveTextPreferenceAsync(long chatId, string text, CancellationToken cancellationToken)
        {
            if (!_waitingForText.ContainsKey(chatId))
            {
                await _botClient.SendTextMessageAsync(chatId, "Ошибка. Начните заново.", cancellationToken: cancellationToken);
                return;
            }

            var (day, weekStart) = _waitingForText[chatId];
            _waitingForText.Remove(chatId);

            if (IsCommand(text))
            {
                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "❌ Нельзя использовать команды. Введите время или комментарий.",
                    cancellationToken: cancellationToken);
                return;
            }

            await SavePreferenceAsync(chatId, day, text, cancellationToken);
        }

        private async Task SavePreferenceAsync(long chatId, DayOfWeek day, string preferenceText, CancellationToken cancellationToken)
        {
            var employee = await _context.Employees.FirstOrDefaultAsync(e => e.TelegramId == chatId, cancellationToken);
            if (employee == null) return;

            var weekStart = GetCurrentWeekStart();
            var existingPreference = await _context.ShiftPreferences
                .FirstOrDefaultAsync(p => p.EmployeeId == employee.Id && p.WeekStart == weekStart && p.Day == day, cancellationToken);

            if (existingPreference != null)
            {
                existingPreference.PreferenceText = preferenceText;
                existingPreference.SubmittedAt = DateTime.Now;
                _context.ShiftPreferences.Update(existingPreference);
            }
            else
            {
                var newPreference = new ShiftPreference
                {
                    EmployeeId = employee.Id,
                    WeekStart = weekStart,
                    Day = day,
                    PreferenceText = preferenceText,
                    SubmittedAt = DateTime.Now
                };
                await _context.ShiftPreferences.AddAsync(newPreference, cancellationToken);
            }

            await _context.SaveChangesAsync(cancellationToken);

            Log($"📝 {employee.FullName} сохранил пожелание на {GetDayName(day)}: {preferenceText}", ConsoleColor.DarkGreen);

            var emoji = preferenceText.ToLower() switch
            {
                "любое время" => "✅",
                "выходной" => "❌",
                _ => "✍️"
            };

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: $"{emoji} <b>Пожелание сохранено!</b>",
                parseMode: ParseMode.Html,
                cancellationToken: cancellationToken);

            var inlineKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("📅 Выбрать другой день", "back_to_days") },
                new[] { InlineKeyboardButton.WithCallbackData("✅ Завершить", "finish_preferences") }
            });

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "Что дальше?",
                replyMarkup: inlineKeyboard,
                cancellationToken: cancellationToken);
        }

        private async Task StartUniformScheduleAsync(long chatId, CancellationToken cancellationToken)
        {
            var employee = await _context.Employees.FirstOrDefaultAsync(e => e.TelegramId == chatId, cancellationToken);
            if (employee == null)
            {
                await _botClient.SendTextMessageAsync(chatId, "Сначала зарегистрируйтесь!", cancellationToken: cancellationToken);
                return;
            }

            _selectedDaysOff[chatId] = new List<DayOfWeek>();

            var inlineKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("Пн", "dayoff_1"),
                    InlineKeyboardButton.WithCallbackData("Вт", "dayoff_2"),
                    InlineKeyboardButton.WithCallbackData("Ср", "dayoff_3")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("Чт", "dayoff_4"),
                    InlineKeyboardButton.WithCallbackData("Пт", "dayoff_5"),
                    InlineKeyboardButton.WithCallbackData("Сб", "dayoff_6")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("Вс", "dayoff_7")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("✅ Подтвердить выходные", "confirm_uniform_schedule")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("↩️ Назад", "back_to_main")
                }
            });

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "📅 <b>Одинаковый график</b>\n\nВыберите 1-2 выходных дня:",
                parseMode: ParseMode.Html,
                replyMarkup: inlineKeyboard,
                cancellationToken: cancellationToken);
        }

        private async Task HandleDayOffSelectionAsync(long chatId, string data, CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            if (!_selectedDaysOff.ContainsKey(chatId))
                _selectedDaysOff[chatId] = new List<DayOfWeek>();

            var dayNumber = int.Parse(data.Split('_')[1]);
            var day = (DayOfWeek)(dayNumber % 7);
            var selectedDays = _selectedDaysOff[chatId];

            if (selectedDays.Contains(day))
            {
                selectedDays.Remove(day);
            }
            else if (selectedDays.Count < 2)
            {
                selectedDays.Add(day);
            }
            else
            {
                await _botClient.AnswerCallbackQueryAsync(
                    callbackQueryId: callbackQuery.Id,
                    text: "❌ Можно выбрать только 2 выходных!",
                    cancellationToken: cancellationToken);
                return;
            }

            await UpdateDayOffKeyboardAsync(chatId, callbackQuery.Message.MessageId, selectedDays, cancellationToken);
        }

        private async Task UpdateDayOffKeyboardAsync(long chatId, int messageId, List<DayOfWeek> selectedDays, CancellationToken cancellationToken)
        {
            var buttons = new List<InlineKeyboardButton[]>
            {
                new[]
                {
                    CreateDayOffButton(DayOfWeek.Monday, selectedDays),
                    CreateDayOffButton(DayOfWeek.Tuesday, selectedDays),
                    CreateDayOffButton(DayOfWeek.Wednesday, selectedDays)
                },
                new[]
                {
                    CreateDayOffButton(DayOfWeek.Thursday, selectedDays),
                    CreateDayOffButton(DayOfWeek.Friday, selectedDays),
                    CreateDayOffButton(DayOfWeek.Saturday, selectedDays)
                },
                new[]
                {
                    CreateDayOffButton(DayOfWeek.Sunday, selectedDays)
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("✅ Подтвердить выходные", "confirm_uniform_schedule")
                }
            };

            try
            {
                await _botClient.EditMessageReplyMarkupAsync(
                    chatId: chatId,
                    messageId: messageId,
                    replyMarkup: new InlineKeyboardMarkup(buttons),
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                Log($"Ошибка обновления клавиатуры: {ex.Message}", ConsoleColor.Red);
            }
        }

        private InlineKeyboardButton CreateDayOffButton(DayOfWeek day, List<DayOfWeek> selectedDays)
        {
            var dayNumber = ((int)day + 6) % 7 + 1;
            var isSelected = selectedDays.Contains(day);
            var buttonText = isSelected ? $"🟢 {GetShortDayName(day)}" : $"⚪️ {GetShortDayName(day)}";
            return InlineKeyboardButton.WithCallbackData(buttonText, $"dayoff_{dayNumber}");
        }

        private async Task ConfirmUniformScheduleAsync(long chatId, CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            if (!_selectedDaysOff.ContainsKey(chatId) || _selectedDaysOff[chatId].Count == 0)
            {
                await _botClient.AnswerCallbackQueryAsync(
                    callbackQueryId: callbackQuery.Id,
                    text: "❌ Выберите хотя бы 1 выходной день!",
                    cancellationToken: cancellationToken);
                return;
            }

            try
            {
                await _botClient.DeleteMessageAsync(chatId, callbackQuery.Message.MessageId, cancellationToken);
            }
            catch { }

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "✍️ <b>Введите ваш рабочий график</b>\n\nНапример: 9:00-18:00",
                parseMode: ParseMode.Html,
                cancellationToken: cancellationToken);

            _waitingForUniformSchedule[chatId] = true;
        }

        private async Task SaveUniformScheduleAsync(long chatId, string scheduleText, CancellationToken cancellationToken)
        {
            if (!_waitingForUniformSchedule.ContainsKey(chatId) || !_selectedDaysOff.ContainsKey(chatId))
            {
                await _botClient.SendTextMessageAsync(chatId, "Ошибка. Начните заново.", cancellationToken: cancellationToken);
                return;
            }

            var selectedDaysOff = _selectedDaysOff[chatId];
            _waitingForUniformSchedule.Remove(chatId);

            var employee = await _context.Employees.FirstOrDefaultAsync(e => e.TelegramId == chatId, cancellationToken);
            if (employee == null) return;

            var weekStart = GetCurrentWeekStart();

            for (int i = 1; i <= 7; i++)
            {
                var day = (DayOfWeek)(i % 7);
                var preferenceText = selectedDaysOff.Contains(day) ? "выходной" : scheduleText;

                var existingPreference = await _context.ShiftPreferences
                    .FirstOrDefaultAsync(p => p.EmployeeId == employee.Id && p.WeekStart == weekStart && p.Day == day, cancellationToken);

                if (existingPreference != null)
                {
                    existingPreference.PreferenceText = preferenceText;
                    existingPreference.SubmittedAt = DateTime.Now;
                }
                else
                {
                    var newPreference = new ShiftPreference
                    {
                        EmployeeId = employee.Id,
                        WeekStart = weekStart,
                        Day = day,
                        PreferenceText = preferenceText,
                        SubmittedAt = DateTime.Now
                    };
                    await _context.ShiftPreferences.AddAsync(newPreference, cancellationToken);
                }
            }

            await _context.SaveChangesAsync(cancellationToken);

            Log($"📝 {employee.FullName} сохранил одинаковый график: {scheduleText}", ConsoleColor.DarkGreen);

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "✅ <b>График успешно сохранен!</b>",
                parseMode: ParseMode.Html,
                cancellationToken: cancellationToken);

            await ShowMainMenuAsync(chatId, null, cancellationToken);
        }

        private async Task ShowCurrentPreferencesAsync(long chatId, CancellationToken cancellationToken)
        {
            var employee = await _context.Employees.FirstOrDefaultAsync(e => e.TelegramId == chatId, cancellationToken);
            if (employee == null)
            {
                await _botClient.SendTextMessageAsync(chatId, "Сначала зарегистрируйтесь!", cancellationToken: cancellationToken);
                return;
            }

            var weekStart = GetCurrentWeekStart();
            var preferences = await _context.ShiftPreferences
                .Where(p => p.EmployeeId == employee.Id && p.WeekStart == weekStart)
                .ToListAsync(cancellationToken);

            var weekEnd = weekStart.AddDays(6);
            var preferencesText = GetPreferencesText(preferences);

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: $"📋 <b>Ваши пожелания</b>\n{weekStart:dd.MM.yyyy} - {weekEnd:dd.MM.yyyy}\n\n{preferencesText}",
                parseMode: ParseMode.Html,
                cancellationToken: cancellationToken);
        }

        public async Task GenerateAndSendScheduleAsync(long chatId, CancellationToken cancellationToken)
        {
            try
            {
                var weekStart = GetCurrentWeekStart();

                // Используем _imageGenerator вместо создания нового
                using var imageStream = await _imageGenerator.GenerateScheduleImage(weekStart);

                await _botClient.SendPhotoAsync(
                    chatId: chatId,
                    photo: InputFile.FromStream(imageStream, "schedule.png"),
                    caption: $"📊 <b>Расписание на следующую неделю</b>\n{weekStart:dd.MM.yyyy} - {weekStart.AddDays(6):dd.MM.yyyy}",
                    parseMode: ParseMode.Html,
                    cancellationToken: cancellationToken);

                Log($"📊 Таблица расписания отправлена пользователю {chatId}", ConsoleColor.DarkCyan);
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка генерации таблицы: {ex.Message}", ConsoleColor.Red);
                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "❌ Не удалось сгенерировать таблицу",
                    cancellationToken: cancellationToken);
            }
        }

        public async Task SendScheduleToAdminAsync(Stream imageStream, DateTime weekStart, CancellationToken cancellationToken)
        {
            if (!_adminChatId.HasValue) return;

            try
            {
                await _botClient.SendPhotoAsync(
                    chatId: _adminChatId.Value,
                    photo: InputFile.FromStream(imageStream, "schedule.png"),
                    caption: $"📊 <b>Финальное расписание</b>\n{weekStart:dd.MM.yyyy} - {weekStart.AddDays(6):dd.MM.yyyy}",
                    parseMode: ParseMode.Html,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка отправки расписания админу: {ex.Message}", ConsoleColor.Red);
            }
        }


        public async Task SendTextMessageToEmployeeAsync(long chatId, string text, CancellationToken cancellationToken)
        {
            try
            {
                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: text,
                    parseMode: ParseMode.Html,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка отправки сообщения {chatId}: {ex.Message}", ConsoleColor.Red);
            }
        }

        private async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            var chatId = callbackQuery.Message.Chat.Id;
            var data = callbackQuery.Data;
            var username = callbackQuery.From.Username ?? "без username";

            Log($"🔄 Callback от {username}: {data}", ConsoleColor.DarkYellow);

            try
            {
                switch (data)
                {
                    case string s when s.StartsWith("preference_"):
                        var dayNumber = int.Parse(s.Split('_')[1]);
                        var day = (DayOfWeek)(dayNumber % 7);
                        await ShowPreferenceOptionsAsync(chatId, day, cancellationToken);
                        break;

                    case string s when s.StartsWith("quick_"):
                        var parts = s.Split('_');
                        var quickDay = (DayOfWeek)int.Parse(parts[1]);
                        var preference = parts[2] switch
                        {
                            "any" => "любое время",
                            "dayoff" => "выходной",
                            _ => ""
                        };
                        await SavePreferenceAsync(chatId, quickDay, preference, cancellationToken);
                        break;

                    case "text_input":
                        var (targetDay, _) = _waitingForText[chatId];
                        await _botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: $"✍️ Введите пожелание для {GetDayName(targetDay)}:",
                            cancellationToken: cancellationToken);
                        break;

                    case "finish_preferences":
                        await _botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "✅ Ввод пожеланий завершен",
                            cancellationToken: cancellationToken);
                        await ShowMainMenuAsync(chatId, null, cancellationToken);
                        break;

                    case "back_to_days":
                        await ShowScheduleMenuAsync(chatId, cancellationToken);
                        break;

                    case "back_to_main":
                        await ShowMainMenuAsync(chatId, null, cancellationToken);
                        break;

                    case string s when s.StartsWith("dayoff_"):
                        await HandleDayOffSelectionAsync(chatId, s, callbackQuery, cancellationToken);
                        break;

                    case "confirm_uniform_schedule":
                        await ConfirmUniformScheduleAsync(chatId, callbackQuery, cancellationToken);
                        break;
                }

                if (!data.StartsWith("dayoff_") && data != "confirm_uniform_schedule")
                {
                    try
                    {
                        await _botClient.DeleteMessageAsync(chatId, callbackQuery.Message.MessageId, cancellationToken);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка обработки callback: {ex.Message}", ConsoleColor.Red);
            }
        }

        private bool IsCommand(string text)
        {
            var commands = new[] {
                "/start", "📋 Регистрация", "📅 Заполнить пожелания (разные)",
                "📅 Заполнить пожелания (одинаковые)", "👀 Мои пожелания",
                "📊 Таблица расписания", "💬 Обратная связь", "👑 Админ панель",
                "📊 Статус сбора", "🔄 Изменить статус", "🔔 Уведомить всех",
                "👥 Список сотрудников", "🔙 Назад в меню"
            };
            return commands.Contains(text);
        }

        private string GetShortDayName(DayOfWeek day) => day switch
        {
            DayOfWeek.Monday => "Пн",
            DayOfWeek.Tuesday => "Вт",
            DayOfWeek.Wednesday => "Ср",
            DayOfWeek.Thursday => "Чт",
            DayOfWeek.Friday => "Пт",
            DayOfWeek.Saturday => "Сб",
            DayOfWeek.Sunday => "Вс",
            _ => "??"
        };

        private string GetDayName(DayOfWeek day) => day switch
        {
            DayOfWeek.Monday => "Понедельник",
            DayOfWeek.Tuesday => "Вторник",
            DayOfWeek.Wednesday => "Среда",
            DayOfWeek.Thursday => "Четверг",
            DayOfWeek.Friday => "Пятница",
            DayOfWeek.Saturday => "Суббота",
            DayOfWeek.Sunday => "Воскресенье",
            _ => "Неизвестно"
        };

        private string GetDayButtonText(List<ShiftPreference> preferences, DayOfWeek day)
        {
            var preference = preferences.FirstOrDefault(p => p.Day == day);
            var dayName = GetShortDayName(day);

            if (preference == null) return $"{dayName} ❓";

            var emoji = preference.PreferenceText.ToLower() switch
            {
                "любое время" => "✅",
                "выходной" => "❌",
                _ => "✍️"
            };

            return $"{dayName} {emoji}";
        }

        private string GetPreferencesText(List<ShiftPreference> preferences)
        {
            if (!preferences.Any()) return "❌ Пожелания не указаны";

            var days = new[] {
                DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
            };

            var sb = new StringBuilder();
            foreach (var day in days)
            {
                var pref = preferences.FirstOrDefault(p => p.Day == day);
                var emoji = pref == null ? "❓" :
                    pref.PreferenceText.ToLower() == "любое время" ? "✅" :
                    pref.PreferenceText.ToLower() == "выходной" ? "❌" : "✍️";

                sb.AppendLine($"{emoji} {GetDayName(day)}: {pref?.PreferenceText ?? "не указано"}");
            }
            return sb.ToString();
        }

        private DateTime GetCurrentWeekStart()
        {
            var today = DateTime.Today;
            var diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
            var currentWeekStart = today.AddDays(-diff).Date;
            return currentWeekStart.AddDays(7);
        }
    }
}