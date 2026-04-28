namespace KeyMouseSyncReplica;

public sealed class AppConfig
{
    public string Dm { get; set; } = "0";

    public string Display { get; set; } = "normal";

    public string Mouse { get; set; } = "0";

    public string Keypad { get; set; } = "0";

    public string Public { get; set; } = string.Empty;

    public string Mode { get; set; } = "0";

    public AppConfig Clone()
    {
        return new AppConfig
        {
            Dm = Dm,
            Display = Display,
            Mouse = Mouse,
            Keypad = Keypad,
            Public = Public,
            Mode = Mode
        };
    }
}
