using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace BlinkReminder.App.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Notify(name);
        return true;
    }
}

public sealed class RelayCommand(Action action, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => action();
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncCommand(Func<Task> action, Action<Exception> onError) : ICommand
{
    private bool executing;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !executing;
    public async void Execute(object? parameter)
    {
        if (executing) return;
        executing = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await action(); }
        catch (Exception error) { onError(error); }
        finally { executing = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }
}

public sealed record Choice<T>(T Value, string Label);
public sealed class DayChoice(DayOfWeek value, bool selected) : ObservableObject
{
    private bool selected = selected;
    public DayOfWeek Value { get; } = value;
    public string Label => Localizer.Current[Value.ToString()];
    public void RefreshLabel() => Notify(nameof(Label));
    public bool Selected { get => selected; set => Set(ref selected, value); }
}
