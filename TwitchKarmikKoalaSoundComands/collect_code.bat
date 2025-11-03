@echo off
setlocal enabledelayedexpansion

:: Устанавливаем имя выходного файла
set OUTPUT_FILE=project_code_dump.txt

:: Очищаем файл, если он уже существует
if exist "%OUTPUT_FILE%" del "%OUTPUT_FILE%"

:: Записываем заголовок
echo =============== ПРОЕКТ: КОД ИЗ ВСЕХ .CS ФАЙЛОВ =============== > "%OUTPUT_FILE%"
echo. >> "%OUTPUT_FILE%"

:: Проходим по всем .cs файлам рекурсивно
for /r %%f in (*.cs) do (
    echo. >> "%OUTPUT_FILE%"
    echo ==================== Файл: %%f ==================== >> "%OUTPUT_FILE%"
    echo. >> "%OUTPUT_FILE%"
    type "%%f" >> "%OUTPUT_FILE%"
    echo. >> "%OUTPUT_FILE%"
    echo -------------------------------------------------------- >> "%OUTPUT_FILE%"
    echo. >> "%OUTPUT_FILE%"
)

echo.
echo Готово! Все .cs файлы собраны в "%OUTPUT_FILE%"
pause