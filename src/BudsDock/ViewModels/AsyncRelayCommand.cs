using System.ComponentModel;
using System.Windows.Input;

namespace BudsDock.ViewModels;

public sealed class AsyncRelayCommand : ICommand, INotifyPropertyChanged
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private readonly object _completionLock = new();
    private int _isRunning;
    private Task _completion = Task.CompletedTask;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool IsRunning => Volatile.Read(ref _isRunning) != 0;
    public Task Completion
    {
        get
        {
            lock (_completionLock)
            {
                return _completion;
            }
        }
    }

    public event EventHandler? CanExecuteChanged;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<Exception>? ExecutionFailed;

    public bool CanExecute(object? parameter) => !IsRunning && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        try
        {
            await ExecuteAsync();
        }
        catch (Exception ex)
        {
            ExecutionFailed?.Invoke(this, ex);
        }
    }

    public Task ExecuteAsync()
    {
        if (_canExecute?.Invoke() == false || Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
        {
            return Task.CompletedTask;
        }

        NotifyRunningStateChanged();
        var completion = ExecuteCoreAsync();
        lock (_completionLock)
        {
            _completion = completion;
        }
        return completion;
    }

    private async Task ExecuteCoreAsync()
    {
        try
        {
            await _execute();
        }
        finally
        {
            Interlocked.Exchange(ref _isRunning, 0);
            NotifyRunningStateChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    private void NotifyRunningStateChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRunning)));
        RaiseCanExecuteChanged();
    }
}
