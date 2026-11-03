using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Snapper.Core.Annotate.Model;
using Snapper.Core.Support;

namespace Snapper.Core.Settings;

public sealed class JsonSettingsStore(string filePath) : ISettingsStore
{
    public string FilePath { get; } = filePath;
    public static string DefaultFilePath(string appDataFolder) => Path.Combine(appDataFolder, "Snapper", "settings.json");
    public SettingsValues Load()
    {
        try { return Parse(File.ReadAllText(FilePath, Encoding.UTF8)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        { return SettingsValues.Defaults; }
    }
    public void Save(SettingsValues values)
    {
        var temporary = FilePath + ".tmp";
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var bytes = Encoding.UTF8.GetBytes(Serialize(values));
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temporary); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            throw new SettingsSaveException(error);
        }
    }
    public static SettingsValues Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return SettingsValues.Defaults;
            var defaults = SettingsValues.Defaults;
            var shortcuts = Member(root, "shortcuts");
            var screenshot = Chord(shortcuts, "screenshot", defaults.ScreenshotShortcut);
            var record = Unique(Chord(shortcuts, "record", defaults.RecordShortcut), defaults.RecordShortcut, screenshot);
            var annotate = Unique(Chord(shortcuts, "annotate", defaults.AnnotateShortcut), defaults.AnnotateShortcut, screenshot, record);
            var folder = String(Member(root, "saveFolder"));
            return new SettingsValues
            {
                LaunchAtLogin = Bool(root, "launchAtLogin", defaults.LaunchAtLogin),
                SaveFolder = folder is not null && IsValidSaveFolderPath(folder) ? folder : null,
                ScreenshotDestination = String(Member(root, "screenshotDestination")) switch
                { "file" => ScreenshotDestination.File, "clipboard" => ScreenshotDestination.Clipboard, "both" => ScreenshotDestination.Both, _ => defaults.ScreenshotDestination },
                PlayShutterSound = Bool(root, "playShutterSound", defaults.PlayShutterSound),
                RecordMicrophone = Bool(root, "recordMicrophone", defaults.RecordMicrophone),
                RecordingFormat = String(Member(root, "recordingFormat")) switch { "mp4" => RecordingFormat.Mp4, "gif" => RecordingFormat.Gif, _ => defaults.RecordingFormat },
                Mp4Quality = String(Member(root, "mp4Quality")) switch { "high" => Mp4Quality.High, "medium" => Mp4Quality.Medium, "low" => Mp4Quality.Low, _ => defaults.Mp4Quality },
                GifQuality = String(Member(root, "gifQuality")) switch { "high" => GifQuality.High, "medium" => GifQuality.Medium, "low" => GifQuality.Low, _ => defaults.GifQuality },
                AnnotationColor = Color(Member(root, "annotationColor"), defaults.AnnotationColor),
                AnnotationStrokeWidth = String(Member(root, "annotationStrokeWidth")) switch { "s" => StrokeWidth.S, "m" => StrokeWidth.M, "l" => StrokeWidth.L, _ => defaults.AnnotationStrokeWidth },
                ScreenshotShortcut = screenshot, RecordShortcut = record, AnnotateShortcut = annotate
            };
        }
        catch (JsonException) { return SettingsValues.Defaults; }
    }
    private static JsonElement Member(JsonElement parent, string name) => parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) ? value : default;
    private static string? String(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool Bool(JsonElement root, string key, bool fallback) => Member(root, key).ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => fallback };
    private static RgbaColor Color(JsonElement value, RgbaColor fallback)
    {
        var parts = new double[4];
        var keys = new[] { "red", "green", "blue", "alpha" };
        for (var i = 0; i < keys.Length; i++)
        {
            var component = Member(value, keys[i]);
            if (component.ValueKind != JsonValueKind.Number || !component.TryGetDouble(out parts[i])) return fallback;
        }
        return RgbaColor.TryCreateStrict(parts[0], parts[1], parts[2], parts[3], out var color) ? color : fallback;
    }
    private static KeyChord? Chord(JsonElement shortcuts, string name, KeyChord? fallback)
    {
        var value = Member(shortcuts, name);
        if (value.ValueKind == JsonValueKind.Null) return null;
        var key = Member(value, "key");
        var modifiers = Member(value, "modifiers");
        if (key.ValueKind != JsonValueKind.Number || !key.TryGetInt32(out var vk) || modifiers.ValueKind != JsonValueKind.Array) return fallback;
        var flags = KeyModifiers.None;
        foreach (var modifier in modifiers.EnumerateArray())
        {
            var flag = String(modifier) switch { "Ctrl" => KeyModifiers.Ctrl, "Alt" => KeyModifiers.Alt, "Shift" => KeyModifiers.Shift, "Win" => KeyModifiers.Win, _ => KeyModifiers.None };
            if (flag == KeyModifiers.None || (flags & flag) != 0) return fallback;
            flags |= flag;
        }
        return KeyChord.TryCreate(flags, vk, out var chord) && chord.IsValidBinding ? chord : fallback;
    }
    private static KeyChord? Unique(KeyChord? chord, KeyChord? fallback, params KeyChord?[] earlier)
    {
        if (chord is null || !earlier.Contains(chord)) return chord;
        return fallback is null || !earlier.Contains(fallback) ? fallback : null;
    }
    internal static bool IsValidSaveFolderPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Any(c => c < ' ' || "<>\"|?*".Contains(c))) return false;
        var drive = path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\';
        if (path.Where((c, i) => c == ':' && !(drive && i == 1)).Any()) return false;
        if (drive) return true;
        if (!path.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        var segments = path[2..].Split('\\');
        return segments.Length >= 2 && !string.IsNullOrWhiteSpace(segments[0]) && !string.IsNullOrWhiteSpace(segments[1]);
    }
    public static string Serialize(SettingsValues values)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 1);
            writer.WriteBoolean("launchAtLogin", values.LaunchAtLogin);
            if (values.SaveFolder is not null) writer.WriteString("saveFolder", values.SaveFolder);
            writer.WriteString("screenshotDestination", values.ScreenshotDestination switch { ScreenshotDestination.File => "file", ScreenshotDestination.Clipboard => "clipboard", ScreenshotDestination.Both => "both", _ => throw new ArgumentOutOfRangeException(nameof(values)) });
            writer.WriteBoolean("playShutterSound", values.PlayShutterSound);
            writer.WriteBoolean("recordMicrophone", values.RecordMicrophone);
            writer.WriteString("recordingFormat", values.RecordingFormat switch { RecordingFormat.Mp4 => "mp4", RecordingFormat.Gif => "gif", _ => throw new ArgumentOutOfRangeException(nameof(values)) });
            writer.WriteString("mp4Quality", values.Mp4Quality switch { Mp4Quality.High => "high", Mp4Quality.Medium => "medium", Mp4Quality.Low => "low", _ => throw new ArgumentOutOfRangeException(nameof(values)) });
            writer.WriteString("gifQuality", values.GifQuality switch { GifQuality.High => "high", GifQuality.Medium => "medium", GifQuality.Low => "low", _ => throw new ArgumentOutOfRangeException(nameof(values)) });
            writer.WriteStartObject("annotationColor");
            writer.WriteNumber("red", values.AnnotationColor.R); writer.WriteNumber("green", values.AnnotationColor.G);
            writer.WriteNumber("blue", values.AnnotationColor.B); writer.WriteNumber("alpha", values.AnnotationColor.Alpha);
            writer.WriteEndObject();
            writer.WriteString("annotationStrokeWidth", values.AnnotationStrokeWidth switch { StrokeWidth.S => "s", StrokeWidth.M => "m", StrokeWidth.L => "l", _ => throw new ArgumentOutOfRangeException(nameof(values)) });
            writer.WriteStartObject("shortcuts");
            WriteChord(writer, "screenshot", values.ScreenshotShortcut);
            WriteChord(writer, "record", values.RecordShortcut);
            WriteChord(writer, "annotate", values.AnnotateShortcut);
            writer.WriteEndObject(); writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    private static void WriteChord(Utf8JsonWriter writer, string name, KeyChord? chord)
    {
        if (chord is not { } value) { writer.WriteNull(name); return; }
        writer.WriteStartObject(name); writer.WriteStartArray("modifiers");
        foreach (var (flag, label) in KeyChord.ModifierNames)
            if ((value.Modifiers & flag) != 0) writer.WriteStringValue(label);
        writer.WriteEndArray(); writer.WriteNumber("key", value.VirtualKey); writer.WriteEndObject();
    }
}
