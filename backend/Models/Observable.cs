using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Backend.Models;

/// <summary>
/// Minimal INotifyPropertyChanged base for backend model classes that need
/// to participate in two-way Avalonia bindings. The desktop VM layer uses
/// the heavier CommunityToolkit.Mvvm source generator, but the backend
/// stays dependency-free and uses this hand-rolled version.
/// </summary>
public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
