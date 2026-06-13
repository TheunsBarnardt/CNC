using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Backend.Machine; // JobLogEvent

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

/// <summary>Bool → "👁" / "👁‍🗨" (simplified visibility icon)</summary>
public sealed class VisibilityIconConverter : IValueConverter
{
    public static readonly VisibilityIconConverter Instance = new();
    public object Convert(object? v, Type t, object? p, CultureInfo c) =>
        v is true ? "👁" : "○";
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}
