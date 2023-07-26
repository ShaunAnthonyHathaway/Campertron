using System;
using System.Collections.Generic;
using System.Text;

namespace Campertron.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {        
        public ReactiveViewModel ReactiveViewModel { get; } = new ReactiveViewModel();
    }
}
