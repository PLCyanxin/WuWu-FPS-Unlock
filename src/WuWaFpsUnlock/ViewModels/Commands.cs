using System.Windows.Input;
namespace WuWaFpsUnlock.ViewModels;
public sealed class RelayCommand(Action action,Func<bool>? canExecute=null):ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter)=>canExecute?.Invoke()??true;
    public void Execute(object? parameter)=>action();
    public void Refresh()=>CanExecuteChanged?.Invoke(this,EventArgs.Empty);
}
public sealed class AsyncCommand(Func<Task> action,Func<bool> canExecute,Action<Exception> onError):ICommand
{
    private bool _running;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter)=>!_running&&canExecute();
    public async void Execute(object? parameter)
    {
        if(!CanExecute(parameter))return;_running=true;Refresh();
        try{await action();}catch(Exception e){onError(e);}finally{_running=false;Refresh();}
    }
    public void Refresh()=>CanExecuteChanged?.Invoke(this,EventArgs.Empty);
}
