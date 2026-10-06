using System.Diagnostics;
using System.Text.Json;

static class Benchmark
{
    public static List<Process> GetProcesses()
    {
        // ищем лаунчер и сам бенчмарк по именам процессов
        var processes = new List<Process>();
        string[] processNames = { "b1_benchmark", "b1-Win64-Shipping" };
        foreach (var name in processNames)
        {
            processes.AddRange(Process.GetProcessesByName(name));
        }
        return processes;
    }

    public static JsonElement Run(string steamFolder, Profile profile, CancellationToken cancellationToken)
    {
        // запоминаем старые файлы, чтобы не взять результат прошлого прохода
        var resultFolder = Path.Combine(Path.GetTempPath(), "b1", "BenchMarkHistory", "Tool");
        var oldFiles = new List<string>();
        if (Directory.Exists(resultFolder))
        {
            oldFiles.AddRange(Directory.GetFiles(resultFolder));
        }
        var startedAt = DateTime.Now;
        try
        {
            // запускаем через стим, с -benchmark сразу переходим к сцене
            var launchInfo = new ProcessStartInfo(Path.Combine(steamFolder, "steam.exe"));
            launchInfo.UseShellExecute = false;
            launchInfo.ArgumentList.Add("-applaunch");
            launchInfo.ArgumentList.Add("3132990");
            launchInfo.ArgumentList.Add("-benchmark");
            launchInfo.ArgumentList.Add("-windowed");
            launchInfo.ArgumentList.Add("-forceres");
            launchInfo.ArgumentList.Add($"-ResX={profile.Width}");
            launchInfo.ArgumentList.Add($"-ResY={profile.Height}");
            using var launch = Process.Start(launchInfo);

            // ждем новый JSON и закрываем игру
            while (DateTime.Now - startedAt < TimeSpan.FromMinutes(5))
            {
                // при Ctrl+C переходим в finally и закрываем текущий проход
                cancellationToken.ThrowIfCancellationRequested();
                if (Directory.Exists(resultFolder))
                {
                    foreach (var file in Directory.GetFiles(resultFolder))
                    {
                        if (oldFiles.Contains(file))
                        {
                            continue;
                        }

                        JsonElement result;
                        try
                        {
                            using var json = JsonDocument.Parse(File.ReadAllText(file));
                            // копируем корень JSON, чтобы читать его после закрытия JsonDocument
                            result = json.RootElement.Clone();
                        }
                        catch (Exception)
                        {
                            // на случай если файл занят или еще дописывается
                            continue;
                        }

                        CheckSettings(profile, result);
                        return result;
                    }
                }

                // делаем паузу на секунду, на следующем круге снова проверяем Ctrl+C
                Thread.Sleep(1000);
            }
            throw new Exception($"{profile.Name}: за 5 минут результат не появился.");
        }
        finally
        {
            // закрываем найденные процессы, запущенные после начала прохода
            foreach (var process in GetProcesses())
            {
                using (process)
                {
                    if (!process.HasExited && process.StartTime >= startedAt)
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(10000);
                    }
                }
            }
            // даем Steam 5 секунд перед следующим запуском
            Thread.Sleep(5000);
        }
    }

    static void CheckSettings(Profile profile, JsonElement result)
    {
        // сравниваем результат с тем же профилем, из которого сделали конфиг
        var resolution = $"{profile.Width} × {profile.Height}";
        if (result.GetProperty("ScreenResolution").GetString() != resolution)
        {
            throw new Exception($"{profile.Name}: игра применила другое разрешение.");
        }

        // имена полей из JSON игры
        var expected = new Dictionary<string, int>
        {
            { "ImageQuality", profile.RenderPercent },
            { "ViewDistance", 5 },
            { "Rtx", profile.RayTracing },
            { "Dlss", 3 },
            { "InsertFrame", 0 },
            { "MotionBlur", 0 }
        };

        // у эффектов один уровень качества, добавляем их одним циклом
        string[] qualityFields =
        {
            "AntiAliasing", "PostProcessing", "ShadowQuality",
            "TextureQuality", "MaterialQuality", "VegetationQuality"
        };
        foreach (var field in qualityFields)
        {
            expected.Add(field, profile.Quality);
        }

        foreach (var setting in expected)
        {
            var actual = result.GetProperty(setting.Key).GetInt32();
            // для GPU допускаем, что игра отключила трассировку
            if (profile.Name == "GPU" && setting.Key == "Rtx" && actual == 0)
            {
                continue;
            }
            if (actual != setting.Value)
            {
                throw new Exception($"{profile.Name}: игра применила другие настройки ({setting.Key}).");
            }
        }
    }
}
