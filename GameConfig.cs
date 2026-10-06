static class GameConfig
{
    public static string Create(Profile profile)
    {
        // читаем общий конфиг
        var templatePath = Path.Combine(AppContext.BaseDirectory, "GameUserSettings.template.ini");
        var template = File.ReadAllText(templatePath);

        // считаем внутренний размер рендера из процента профиля
        var renderWidth = profile.Width * profile.RenderPercent / 100;
        var renderHeight = profile.Height * profile.RenderPercent / 100;

        // у меню качества 1..5, у sg 0..4, поэтому вычитаем единицу
        var engineQuality = profile.Quality - 1;

        // для меню RT нужен переключатель, для sg уровень
        var rayTracingEnabled = "0";
        if (profile.RayTracing > 0)
        {
            rayTracingEnabled = "1";
        }

        // заменяем метки значениями
        return template
            .Replace("{Width}", profile.Width.ToString())
            .Replace("{Height}", profile.Height.ToString())
            .Replace("{RenderWidth}", renderWidth.ToString())
            .Replace("{RenderHeight}", renderHeight.ToString())
            .Replace("{RenderPercent}", profile.RenderPercent.ToString())
            .Replace("{Quality}", profile.Quality.ToString())
            .Replace("{EngineQuality}", engineQuality.ToString())
            .Replace("{RayTracingEnabled}", rayTracingEnabled)
            .Replace("{RayTracing}", profile.RayTracing.ToString());
    }
}
