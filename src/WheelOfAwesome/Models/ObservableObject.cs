using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WheelOfAwesome.Models;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised for any persisted property change on any model; the config service debounces a save off this.</summary>
    public static event Action? AnyChanged;

    internal static void RaiseAnyChanged() => AnyChanged?.Invoke();

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        AnyChanged?.Invoke();
        return true;
    }

    /// <summary>Notifies bindings without marking the config dirty (for derived, unsaved properties).</summary>
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
