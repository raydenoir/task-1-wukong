using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.Win32;

class Program
{
    // путь к игре переданный в args или находим в реестре
    static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length > 1)
        {
            Console.WriteLine("Запуск: WukongRunner.exe [\"папка Black Myth Wukong Benchmark Tool\"]");
            return;
        }

        // при Ctrl+C просим цикл остановиться, чтобы успеть вернуть конфиг
        // через using освобождаем ресурсы cancellation при выходе из Main
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (sender, eventArgs) =>
        {
            // откладываем выход из программы, чтобы вернуть конфиг через finally
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            var gameFolder = GetGameFolder(args);
            var iniFolder = Path.Combine(gameFolder, "b1", "Saved", "Config", "Windows");
            var iniPath = Path.Combine(iniFolder, "GameUserSettings.ini");

            // ищем Steam в реестре, иначе пробуем его обычную папку
            var steamFolder = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
            if (string.IsNullOrEmpty(steamFolder))
            {
                steamFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
            }

            // проверяем, что указали папку Benchmark Tool
            if (!File.Exists(Path.Combine(gameFolder, "b1_benchmark.exe")))
            {
                throw new Exception("Проверьте папку Benchmark Tool.");
            }

            var running = Benchmark.GetProcesses();
            foreach (var process in running)
            {
                // освобождаем объект Process
                process.Dispose();
            }
            if (running.Count > 0)
            {
                throw new Exception("Закройте Benchmark Tool перед запуском.");
            }

            // если конфиг уже есть, сохраняем его целиком
            var hadConfig = File.Exists(iniPath);
            byte[] originalBytes = [];
            if (hadConfig)
            {
                originalBytes = File.ReadAllBytes(iniPath);
            }
            var report = new StringBuilder();
            var reportPath = Path.GetFullPath("report.txt");

            // читаем размер основного экрана для GPU-прохода
            SetProcessDPIAware();
            Profile[] profiles =
            {
                new Profile
                {
                    Name = "CPU",
                    Width = 1280,
                    Height = 720,
                    RenderPercent = 25,
                    Quality = 1,
                    RayTracing = 0,
                    MotionBlur = 0
                },
                new Profile
                {
                    Name = "GPU",
                    Width = GetSystemMetrics(0),
                    Height = GetSystemMetrics(1),
                    RenderPercent = 100,
                    Quality = 5,
                    RayTracing = 3,
                    MotionBlur = 2
                }
            };

            try
            {
                // перед первым запуском сами создаем папку настроек
                Directory.CreateDirectory(iniFolder);

                // запускаем бенчмарк для обоих профилей
                foreach (var profile in profiles)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    // подставляем значения профиля в общий шаблон
                    File.WriteAllText(iniPath, GameConfig.Create(profile));
                    Console.WriteLine($"Запуск {profile.Name}-теста...");

                    // ждем результат этого прохода, потом переходим к следующему
                    var result = Benchmark.Run(steamFolder, profile, cancellation.Token);

                    // железо берем из JSON игры и выводим один раз
                    if (profile.Name == "CPU")
                    {
                        var hardware = FormatHardware(result);
                        report.AppendLine(hardware);
                        Console.WriteLine(hardware);
                    }

                    // сохраняем после каждого прохода, чтобы не потерять CPU при ошибке GPU
                    var text = FormatResult(profile, result);
                    report.AppendLine(text);
                    Console.WriteLine(text);
                    File.WriteAllText(reportPath, report.ToString());
                }
            }
            finally
            {
                // возвращаем прежний файл, а если его не было то убираем временный
                if (hadConfig)
                {
                    File.WriteAllBytes(iniPath, originalBytes);
                }
                else
                {
                    File.Delete(iniPath);
                }
            }
            Console.WriteLine($"Отчет сохранен: {reportPath}");
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Запуск отменен.");
            Environment.ExitCode = 2;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            Environment.ExitCode = 1;
        }
    }

    static string GetGameFolder(string[] args)
    {
        // если передали путь сами, используем его
        if (args.Length == 1)
        {
            return Path.GetFullPath(args[0]).TrimEnd(Path.DirectorySeparatorChar);
        }

        // по App ID берем папку из записи Steam об установленной программе
        var registryPath = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 3132990";
        var gameFolder = Registry.GetValue(registryPath, "InstallLocation", null) as string;
        if (string.IsNullOrEmpty(gameFolder))
        {
            throw new Exception("Не нашли Benchmark Tool. Передайте папку установки в аргументе запуска.");
        }
        return Path.GetFullPath(gameFolder).TrimEnd(Path.DirectorySeparatorChar);
    }

    static string FormatHardware(JsonElement result)
    {
        // через GetProperty берем поле по имени, названия оставляем как в JSON игры
        return $"CPU: {result.GetProperty("CPUModel")}\n"
            + $"GPU: {result.GetProperty("GPUModel")}\n"
            + $"RAM: {result.GetProperty("SysMem")}, VRAM: {result.GetProperty("VideoMemSize")}\n"
            + $"ОС: {result.GetProperty("SysVer")}, Benchmark Tool: {result.GetProperty("GameVer")}\n";
    }

    static string FormatResult(Profile profile, JsonElement result)
    {
        // в Records берем замеры CPU и GPU в мс, считаем среднее для каждого
        double cpuTime = 0;
        double gpuTime = 0;
        int count = 0;
        foreach (var record in result.GetProperty("Records").EnumerateArray())
        {
            cpuTime += record.GetProperty("CPUFrameTime").GetDouble();
            gpuTime += record.GetProperty("GPUFrameTime").GetDouble();
            count++;
        }
        if (count == 0)
        {
            throw new Exception("В результате нет измерений времени кадра.");
        }
        cpuTime /= count;
        gpuTime /= count;

        // вместо чисел 1..5 выводим названия качества из меню
        string[] qualityNames = { "", "Low", "Medium", "High", "Very High", "Cinematic" };
        var quality = qualityNames[result.GetProperty("ShadowQuality").GetInt32()];
        var distance = qualityNames[result.GetProperty("ViewDistance").GetInt32()];
        var rt = result.GetProperty("Rtx").GetInt32();
        string[] motionBlurNames = { "выключен", "Weak", "Strong" };
        var motionBlur = motionBlurNames[result.GetProperty("MotionBlur").GetInt32()];
        var rtText = "выключен";
        if (rt > 0)
        {
            rtText = $"уровень {rt}";
        }
        // если игра отключила RT, отмечаем это в отчете
        var rtNote = "";
        if (profile.Name == "GPU" && profile.RayTracing > 0 && rt == 0)
        {
            rtNote = "Full Ray Tracing не применился, GPU-тест выполнен без него.\n";
        }

        // FPS берем готовые из игры, F2 используем для двух знаков после запятой
        return $"{profile.Name}-тест\n"
            + $"Настройки: {result.GetProperty("ScreenResolution")}, "
            + $"рендер {result.GetProperty("ImageQuality")}%, {quality}, дальность {distance}, RT {rtText}.\n"
            + rtNote
            + $"TSR; Motion Blur {motionBlur}; генерация кадров и VSync выключены; лимита FPS нет.\n"
            + $"FPS: средний {result.GetProperty("FPSAvg")}, "
            + $"минимум {result.GetProperty("FPSMin")}, максимум {result.GetProperty("FPSMax")}.\n"
            + $"Среднее время на кадр: CPU {cpuTime:F2} мс, GPU {gpuTime:F2} мс.\n"
            + $"Загрузка: CPU {result.GetProperty("CPUAvg")}%, GPU {result.GetProperty("GPUAvg")}%\n"
            + $"Использовано VRAM: {result.GetProperty("VideoMem").GetDouble():F2} GB.\n";
    }

    // чтобы получить ширину и высоту экрана
    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int index);

    // перед чтением размера убираем поправку на масштаб Windows
    [DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();
}
