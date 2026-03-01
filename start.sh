#!/bin/bash
echo "==================================="
echo "?? ЗАПУСК SCHEDULEBOT1"
echo "==================================="
echo "?? Текущая папка: $(pwd)"
echo "?? Содержимое:"
ls -la

echo "?? Проверка токена..."
if [ -f "token.txt" ]; then
    echo "? Файл token.txt найден"
    echo "?? Первые символы: $(head -c 10 token.txt)..."
else
    echo "?? Файл token.txt не найден"
    echo "?? Создаю файл token.txt..."
    echo "8458111413:AAFb8AUJrVapGXolNXEj6aV1VbTzu4zJUDA" > token.txt
    echo "? Файл token.txt создан"
fi

echo "?? Запуск бота..."
dotnet /app/ScheduleBot.dll

echo "? Бот остановлен"