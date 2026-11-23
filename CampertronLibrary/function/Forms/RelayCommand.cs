using System;
using System.Windows.Input;

namespace CampertronLibrary.function.Forms
{
    // A button's action and whether it is enabled (the settings editor's Save, Revert, Reload, ... buttons, CM5). The
    // view model calls Refresh when the answer may have changed.
    public sealed class RelayCommand : ICommand
    {
        private readonly Action _Execute;
        private readonly Func<bool> _CanExecute;

        public RelayCommand(Action Execute, Func<bool>? CanExecute = null)
        {
            _Execute = Execute;
            _CanExecute = CanExecute ?? (() => true);
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? Parameter) => _CanExecute();

        public void Execute(object? Parameter)
        {
            if (_CanExecute())
            {
                _Execute();
            }
        }

        public void Refresh()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
