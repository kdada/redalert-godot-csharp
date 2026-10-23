using Godot;
using System;
using System.Text;
using SimCountry = RedAlert.Simulation.Country;

namespace RedAlert;

/// <summary>Local client options; these do not change simulation rules or the network protocol.</summary>
public sealed partial class ClientPreferences
{
    const string ConfigPath = "user://client.cfg";
    const int DefaultFrameLimit = 60;
    const float DefaultCameraSpeed = 1f;
    const float DefaultEdgeScrollMargin = 12f;
    const string DefaultCommanderName = "指挥官";
    const int DefaultPort = 19842;

    int frameLimit = DefaultFrameLimit;
    float cameraSpeed = DefaultCameraSpeed;
    float edgeScrollMargin = DefaultEdgeScrollMargin;
    string commanderName = DefaultCommanderName;
    SimCountry country = SimCountry.America;
    string lastHost = "";
    int port = DefaultPort;

    /// <summary>30, 60, 90, 120, or 0 for unlimited. Unsupported values reset to 60.</summary>
    public int FrameLimit
    {
        get => frameLimit;
        set => frameLimit = value is 0 or 30 or 60 or 90 or 120 ? value : DefaultFrameLimit;
    }

    public bool VSync { get; set; } = true;
    public bool Fullscreen { get; set; }

    /// <summary>Camera movement multiplier, clamped to 0.5–2; nonfinite values reset to 1.</summary>
    public float CameraSpeed
    {
        get => cameraSpeed;
        set => cameraSpeed = float.IsFinite(value) ? Math.Clamp(value, 0.5f, 2f) : DefaultCameraSpeed;
    }

    public bool EdgeScrolling { get; set; } = true;

    /// <summary>Edge scrolling margin in viewport pixels, clamped to 8–32.</summary>
    public float EdgeScrollMargin
    {
        get => edgeScrollMargin;
        set => edgeScrollMargin = float.IsFinite(value) ? Math.Clamp(value, 8f, 32f) : DefaultEdgeScrollMargin;
    }

    /// <summary>Trimmed display name, at most 32 UTF-16 characters; blank names use 指挥官.</summary>
    public string CommanderName
    {
        get => commanderName;
        set => commanderName = CleanText(value, 32, DefaultCommanderName);
    }

    public SimCountry Country
    {
        get => country;
        set => country = Enum.IsDefined(value) ? value : SimCountry.America;
    }

    /// <summary>Remembered host, at most 253 characters. Empty means unset; the UI supplies its placeholder.</summary>
    public string LastHost
    {
        get => lastHost;
        set => lastHost = CleanText(value, 253, "");
    }

    /// <summary>UDP port, clamped to the lobby's supported range of 1024–65535.</summary>
    public int Port
    {
        get => port;
        set => port = Math.Clamp(value, 1024, 65535);
    }

    /// <summary>Loads private application preferences, falling back to defaults for unreadable or invalid values.</summary>
    public static ClientPreferences Load()
    {
        var preferences = new ClientPreferences();
        using var config = new ConfigFile();
        if (config.Load(ConfigPath) != Error.Ok) return preferences;

        preferences.FrameLimit = ReadInt(config, "display", "frame_limit", preferences.FrameLimit);
        preferences.VSync = ReadBool(config, "display", "vsync", preferences.VSync);
        preferences.Fullscreen = ReadBool(config, "display", "fullscreen", preferences.Fullscreen);
        preferences.CameraSpeed = ReadFloat(config, "camera", "speed", preferences.CameraSpeed);
        preferences.EdgeScrolling = ReadBool(config, "camera", "edge_scrolling", preferences.EdgeScrolling);
        preferences.EdgeScrollMargin = ReadFloat(config, "camera", "edge_scroll_margin", preferences.EdgeScrollMargin);
        preferences.CommanderName = ReadString(config, "lobby", "commander_name", preferences.CommanderName);
        preferences.Country = ReadCountry(config, preferences.Country);
        preferences.LastHost = ReadString(config, "lobby", "last_host", preferences.LastHost);
        preferences.Port = ReadInt(config, "lobby", "port", preferences.Port);
        ReadShell(config,preferences);
        return preferences;
    }

    /// <summary>Saves only local preferences. The caller can display a write error using the returned status.</summary>
    public Error Save()
    {
        using var config = new ConfigFile();
        config.SetValue("display", "frame_limit", FrameLimit);
        config.SetValue("display", "vsync", VSync);
        config.SetValue("display", "fullscreen", Fullscreen);
        config.SetValue("camera", "speed", CameraSpeed);
        config.SetValue("camera", "edge_scrolling", EdgeScrolling);
        config.SetValue("camera", "edge_scroll_margin", EdgeScrollMargin);
        config.SetValue("lobby", "commander_name", CommanderName);
        config.SetValue("lobby", "country", Country.ToString());
        config.SetValue("lobby", "last_host", LastHost);
        config.SetValue("lobby", "port", Port);
        WriteShell(config);
        return config.Save(ConfigPath);
    }

    /// <summary>Applies presentation options on the main thread. Headless runs skip native window operations.</summary>
    public void ApplyDisplay(Window window)
    {
        Engine.MaxFps = FrameLimit;
        ApplyAudio();
        if (DisplayServer.GetName().Equals("headless", StringComparison.OrdinalIgnoreCase)) return;
        ArgumentNullException.ThrowIfNull(window);
        window.ContentScaleSize=new Vector2I(ResolutionWidth,ResolutionHeight);
        window.Mode = Fullscreen ? Window.ModeEnum.Fullscreen : Window.ModeEnum.Windowed;
        DisplayServer.WindowSetVsyncMode(VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled,
            window.GetWindowId());
    }

    /// <summary>Returns an independent snapshot suitable for editing or cancelling a settings screen.</summary>
    public ClientPreferences Clone() => (ClientPreferences)MemberwiseClone();

    /// <summary>Resets this instance in memory. Saving and applying display options remain explicit.</summary>
    public void RestoreDefaults()
    {
        FrameLimit = DefaultFrameLimit;
        VSync = true;
        Fullscreen = false;
        CameraSpeed = DefaultCameraSpeed;
        EdgeScrolling = true;
        EdgeScrollMargin = DefaultEdgeScrollMargin;
        CommanderName = DefaultCommanderName;
        Country = SimCountry.America;
        LastHost = "";
        Port = DefaultPort;
        RestoreShellDefaults();
    }

    static int ReadInt(ConfigFile config, string section, string key, int fallback)
    {
        using var value = config.GetValue(section, key, fallback);
        if (value.VariantType != Variant.Type.Int) return fallback;
        var number = value.AsInt64();
        return number is >= int.MinValue and <= int.MaxValue ? (int)number : fallback;
    }

    static float ReadFloat(ConfigFile config, string section, string key, float fallback)
    {
        using var value = config.GetValue(section, key, fallback);
        if (value.VariantType is not (Variant.Type.Float or Variant.Type.Int)) return fallback;
        var number = value.AsDouble();
        return double.IsFinite(number) ? (float)Math.Clamp(number, -float.MaxValue, float.MaxValue) : fallback;
    }

    static bool ReadBool(ConfigFile config, string section, string key, bool fallback)
    {
        using var value = config.GetValue(section, key, fallback);
        return value.VariantType == Variant.Type.Bool ? value.AsBool() : fallback;
    }

    static string ReadString(ConfigFile config, string section, string key, string fallback)
    {
        using var value = config.GetValue(section, key, fallback);
        return value.VariantType == Variant.Type.String ? value.AsString() : fallback;
    }

    static SimCountry ReadCountry(ConfigFile config, SimCountry fallback)
    {
        var text = ReadString(config, "lobby", "country", fallback.ToString());
        return Enum.TryParse<SimCountry>(text, true, out var parsed) && Enum.IsDefined(parsed) ? parsed : fallback;
    }

    static string CleanText(string? value, int maxLength, string fallback)
    {
        value = value?.Trim() ?? "";
        var text = new StringBuilder(Math.Min(value.Length, maxLength));
        foreach (var character in value)
        {
            if (char.IsControl(character)) continue;
            if (text.Length == maxLength) break;
            text.Append(character);
        }
        // Do not split a supplementary Unicode character at the length limit.
        if (text.Length > 0 && char.IsHighSurrogate(text[text.Length - 1])) text.Length--;
        var result = text.ToString().Trim();
        return result.Length == 0 ? fallback : result;
    }
}
