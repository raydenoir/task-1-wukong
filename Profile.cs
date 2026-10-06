// класс профиля для настроек отличных от темплейта
class Profile
{
    public string Name { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }

    public int RenderPercent { get; set; }

    // Low = 1 Cinematic = 5
    public int Quality { get; set; }

    // 0 - выключаем, 3 - максимум
    public int RayTracing { get; set; }
}
