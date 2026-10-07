// класс профиля для настроек отличных от темплейта
class Profile
{
    public string Name { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }

    public int RenderPercent { get; set; }

    // Low = 1, Cinematic = 5
    public int Quality { get; set; }

    // Off = 0, Max = 3
    public int RayTracing { get; set; }

    // Off = 0, Strong = 2
    public int MotionBlur { get; set; }
}
