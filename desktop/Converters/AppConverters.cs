using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Backend.Machine;
using Backend.Models;

namespace Desktop.Converters;

/// <summary>Bool → "Disconnect" / "Connect"</summary>
public sealed class ConnectLabelConverter : IValueConverter
{
    public static readonly ConnectLabelConverter Instance = new();
    public object Convert(object? v, Type t, object? p, CultureInfo c) =>
        v is true ? "Disconnect" : "Connect";
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}

/// <summary>Bool → green / grey brush for the status dot</summary>
public sealed class ConnectedColorConverter : IValueConverter
{
    public static readonly ConnectedColorConverter Instance = new();
    private static readonly SolidColorBrush Green = new(Color.Parse("#10b981"));
    private static readonly SolidColorBrush Grey  = new(Color.Parse("#555555"));
    public object Convert(object? v, Type t, object? p, CultureInfo c) =>
        v is true ? Green : Grey;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}

/// <summary>JobLogEvent → foreground color</summary>
public sealed class LogColorConverter : IValueConverter
{
    public static readonly LogColorConverter Instance = new();
    private static readonly SolidColorBrush Ok   = new(Color.Parse("#10b981"));
    private static readonly SolidColorBrush Err  = new(Color.Parse("#ef4444"));
    private static readonly SolidColorBrush Info = new(Color.Parse("#888888"));
    public object Convert(object? v, Type t, object? p, CultureInfo c) =>
        v is JobLogEvent ev
            ? ev == JobLogEvent.Completed ? Ok
            : ev == JobLogEvent.Error     ? Err
            : Info
            : Info;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}

/// <summary>Bool → eye / eye-off vector geometry (for a PathIcon).</summary>
public sealed class VisibilityIconConverter : IValueConverter
{
    public static readonly VisibilityIconConverter Instance = new();

    // Same path data as Themes/Icons.axaml (Icon.Eye / Icon.EyeOff).
    private static readonly Geometry Eye = Geometry.Parse(
        "F0 M12,5 C6,5 1.7,9 0.5,12 C1.7,15 6,19 12,19 C18,19 22.3,15 23.5,12 C22.3,9 18,5 12,5 Z " +
        "M7,12 A5 5 0 1 0 17 12 A5 5 0 1 0 7 12 Z M9.5,12 A2.5 2.5 0 1 0 14.5 12 A2.5 2.5 0 1 0 9.5 12 Z");
    private static readonly Geometry EyeOff = Geometry.Parse(
        "M3,11 C6,15 18,15 21,11 L19.5,9.7 C17,13 7,13 4.5,9.7 Z M4,4.5 L19.5,18.5 L18.2,20 L2.7,6 Z");

    public object Convert(object? v, Type t, object? p, CultureInfo c) => v is true ? Eye : EyeOff;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}

/// <summary>Object → bool (true if not null, false if null).</summary>
public sealed class NotNullConverter : IValueConverter
{
    public static readonly NotNullConverter Instance = new();
    public object Convert(object? v, Type t, object? p, CultureInfo c) => v is not null;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}

/// <summary>Object → bool (true if null, false if not null). Inverse of NotNullConverter.</summary>
public sealed class NullConverter : IValueConverter
{
    public static readonly NullConverter Instance = new();
    public object Convert(object? v, Type t, object? p, CultureInfo c) => v is null;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}

/// <summary>Bool → lock / unlock vector geometry (matching Icon.Lock / Icon.Unlock in Icons.axaml).</summary>
public sealed class LockIconConverter : IValueConverter
{
    public static readonly LockIconConverter Instance = new();

    // Paths match Icon.Lock and Icon.Unlock in Themes/Icons.axaml
    private static readonly Geometry Locked = Geometry.Parse(
        "F0 M7,10 V7 A5 5 0 0 1 17,7 V10 H15 V7 A3 3 0 0 0 9,7 V10 Z M5.5,10 H18.5 V21 H5.5 Z M11,13.5 H13 V17.5 H11 Z");
    private static readonly Geometry Unlocked = Geometry.Parse(
        "F0 M9,10 V7 A3 3 0 0 1 15,7 H17 A5 5 0 0 0 7,7 V10 Z M5.5,10 H18.5 V21 H5.5 Z M11,13.5 H13 V17.5 H11 Z");

    public object Convert(object? v, Type t, object? p, CultureInfo c) => v is true ? Locked : Unlocked;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}

/// <summary>Static source for LayerOperationMode combo — avoids inline ComboBoxItem children.</summary>
public static class LayerEditorHelper
{
    public static LayerOperationMode[] OperationModes { get; } =
        Enum.GetValues<LayerOperationMode>();
}
