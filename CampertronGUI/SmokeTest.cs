using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using Campertron.ViewModels;
using Campertron.Views;
using CampertronLibrary.function.Base;
using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace Campertron
{
    // `CampertronGUI --smoke-test` (R8): a release smoke test of the real GUI on a real platform. It starts the normal
    // application and window, lets the normal startup prepare the configuration and database, waits for the window's
    // native handle, the ready state with data and a rendered frame, then closes the window through the normal lifetime.
    // Every step prints one "SMOKE key=value" line to standard output; the exit code is 0 only if every step passed.
    // Failures are explained on standard error. Nothing else differs from a normal start: same data folder, same
    // Load.GetConfig, no extra network access, no file written by the smoke test itself.
    public sealed class SmokeTest
    {
        public const string Argument = "--smoke-test";
        public const int PassExitCode = 0;
        public const int FailExitCode = 1;
        public const int TimeoutExitCode = 2;
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

        private readonly TextWriter _Out;
        private readonly TextWriter _Err;
        private readonly TimeSpan _Timeout;
        private readonly Action<int> _Abort;
        private readonly Func<TopLevel, IPlatformHandle?> _Handle;
        private readonly Action<TopLevel, Action> _OnRenderedFrame;
        private readonly object _Lock = new object();
        private Timer? _Timer;
        private MainWindow? _Window;
        private MainWindowViewModel? _ViewModel;
        private string _Waiting = "the window to open";
        private bool _Opened;
        private bool _Ready;
        private bool _CloseRequested;
        private bool _Closed;
        private bool _Finished;
        private string? _Failure;

        public static bool IsRequest(string[]? Args)
        {
            return Args != null && Args.Length == 1 && Args[0] == Argument;
        }

        // Production: the process exits with code 2 on timeout, even if the UI thread is blocked.
        public SmokeTest(TextWriter Out, TextWriter Err)
            : this(Out, Err, DefaultTimeout, Code => Environment.Exit(Code), Window => Window.TryGetPlatformHandle(), WaitForRenderedFrame)
        {
        }

        // Tests replace the timeout, the abort, the native handle and the rendered-frame signal.
        public SmokeTest(TextWriter Out, TextWriter Err, TimeSpan Timeout, Action<int> Abort,
            Func<TopLevel, IPlatformHandle?> Handle, Action<TopLevel, Action> OnRenderedFrame)
        {
            _Out = Out;
            _Err = Err;
            _Timeout = Timeout;
            _Abort = Abort;
            _Handle = Handle;
            _OnRenderedFrame = OnRenderedFrame;
        }

        public string? Failure => _Failure;

        // Runs the GUI exactly as Program.Main does, with the smoke test attached to the main window.
        public static int Run(AppBuilder Builder, string[] Args)
        {
            var Smoke = new SmokeTest(Console.Out, Console.Error);
            Smoke.Line("startup", RuntimeInfo.Describe(typeof(SmokeTest).Assembly).Split('\n')[0].Trim());
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Smoke.Fail("unhandled exception: " + e.ExceptionObject);
            int LifetimeExitCode = Builder.StartWithClassicDesktopLifetime(Args, Lifetime =>
            {
                Lifetime.Startup += (_, _) =>
                {
                    if (Lifetime.MainWindow is MainWindow Window)
                    {
                        Smoke.Attach(Window);
                    }
                    else
                    {
                        Smoke.Fail("the application created no main window");
                    }
                };
            });
            (Application.Current as App)?.WaitForPendingPreparation();
            return Smoke.Finish(LifetimeExitCode);
        }

        // On the UI thread, before or after the window is shown.
        public void Attach(MainWindow Window)
        {
            _Window = Window;
            _ViewModel = Window.DataContext as MainWindowViewModel;
            if (_ViewModel == null)
            {
                Fail("the main window has no MainWindowViewModel");
                return;
            }
            Dispatcher.UIThread.UnhandledException += (_, e) => Fail("unhandled UI exception: " + e.Exception);
            _ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            Window.Opened += (_, _) => OnOpened();
            Window.Closing += Window_Closing;
            Window.Closed += (_, _) => _Closed = true;
            _Timer = new Timer(_ => OnTimeout(), null, _Timeout, System.Threading.Timeout.InfiniteTimeSpan);
            if (Window.IsVisible)
            {
                OnOpened();
            }
        }

        private void OnOpened()
        {
            if (_Opened || _Finished || _Window == null)
            {
                return;
            }
            _Opened = true;
            Line("window-opened", "yes");
            IPlatformHandle? Handle = _Handle(_Window);
            if (Handle == null || Handle.Handle == IntPtr.Zero)
            {
                Fail("the main window has no native platform handle");
                return;
            }
            Line("native-handle", (Handle.HandleDescriptor ?? "handle") + ":0x" + Handle.Handle.ToString("x"));
            _Waiting = "the startup to prepare the campground data";
            CheckState();
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.State))
            {
                CheckState();
            }
        }

        private void CheckState()
        {
            if (!_Opened || _Ready || _Finished || _ViewModel == null || _Window == null)
            {
                return;
            }
            if (_ViewModel.IsFailed)
            {
                Fail("startup failed: " + _ViewModel.ErrorMessage);
                return;
            }
            if (!_ViewModel.IsReady || _ViewModel.ReactiveViewModel == null)
            {
                return;
            }
            _Ready = true;
            Line("ready", "yes");
            Line("config-path", _ViewModel.ConfigPath);
            _Waiting = "a rendered frame";
            _OnRenderedFrame(_Window, OnRenderedFrame);
        }

        // After a rendered frame the ready window has been laid out, so its selectors show the data.
        private void OnRenderedFrame()
        {
            if (_Finished || _Window == null || _ViewModel?.ReactiveViewModel == null || _CloseRequested || _Failure != null)
            {
                return;
            }
            Line("rendered-frame", "yes");
            int Parks = _Window.FindControl<ComboBox>("ParkComboBox")?.ItemCount ?? 0;
            int States = _ViewModel.ReactiveViewModel.StateList.Count;
            Line("parks", Parks.ToString(CultureInfo.InvariantCulture));
            Line("states", States.ToString(CultureInfo.InvariantCulture));
            if (Parks <= 0 || States <= 0)
            {
                Fail("the campground data is empty (parks " + Parks + ", states " + States + ")");
                return;
            }
            Line("window-size", _Window.ClientSize.Width.ToString("0", CultureInfo.InvariantCulture) + "x"
                + _Window.ClientSize.Height.ToString("0", CultureInfo.InvariantCulture));
            Line("render-scaling", _Window.RenderScaling.ToString("0.###", CultureInfo.InvariantCulture));
            Line("theme", Application.Current?.ActualThemeVariant?.ToString() ?? "unknown");
            _Waiting = "the window to close";
            _CloseRequested = true;
            Line("close-requested", "yes");
            _Window.Close();
        }

        private void Window_Closing(object? sender, WindowClosingEventArgs e)
        {
            if (_Finished)
            {
                return;
            }
            if (!_CloseRequested)
            {
                Fail("the window was closed before the smoke test finished (" + Classify(e) + ")");
                return;
            }
            Line("closing", Classify(e));
            if (!e.IsProgrammatic)
            {
                Fail("the window closed for another reason than the smoke test's request");
            }
        }

        private static string Classify(WindowClosingEventArgs e)
        {
            return "programmatic=" + (e.IsProgrammatic ? "yes" : "no") + " reason=" + e.CloseReason;
        }

        // After the application lifetime has ended (in tests: after the window closed). Returns the exit code.
        public int Finish(int LifetimeExitCode)
        {
            lock (_Lock)
            {
                if (_Finished)
                {
                    return _Failure == null ? PassExitCode : TimeoutExitCode;
                }
                _Finished = true;
            }
            _Timer?.Dispose();
            if (_Failure == null)
            {
                if (!_CloseRequested || !_Closed)
                {
                    _Failure = "the application ended before the smoke test finished, while waiting for " + _Waiting;
                }
                else if (LifetimeExitCode != 0)
                {
                    _Failure = "the application lifetime returned exit code " + LifetimeExitCode;
                }
                else if (_ViewModel != null && !_ViewModel.PendingPreparation.IsCompleted)
                {
                    _Failure = "the startup preparation was still running after the window closed";
                }
            }
            if (_Failure != null)
            {
                Explain(_Failure);
                Line("result", "fail");
                return FailExitCode;
            }
            Line("shutdown", "clean exit-code=" + LifetimeExitCode.ToString(CultureInfo.InvariantCulture));
            Line("result", "pass");
            return PassExitCode;
        }

        // Records the first failure and closes the window so the lifetime ends; Finish reports it.
        public void Fail(string Reason)
        {
            lock (_Lock)
            {
                if (_Finished || _Failure != null)
                {
                    return;
                }
                _Failure = Reason;
            }
            Dispatcher.UIThread.Post(() =>
            {
                if (_Window != null && !_Closed)
                {
                    _Window.Close();
                }
            });
        }

        private void OnTimeout()
        {
            lock (_Lock)
            {
                if (_Finished)
                {
                    return;
                }
                _Finished = true;
                _Failure ??= "timed out after " + _Timeout.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)
                    + " s waiting for " + _Waiting;
            }
            Explain(_Failure);
            Line("result", "fail");
            _Out.Flush();
            _Err.Flush();
            _Abort(TimeoutExitCode);
        }

        private void Explain(string Failure)
        {
            lock (_Out)
            {
                _Err.WriteLine("CampertronGUI smoke test failed: " + Failure);
                _Err.Flush();
            }
        }

        private void Line(string Key, string Value)
        {
            string Clean = string.Join(" ", (Value ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
            lock (_Out)
            {
                _Out.WriteLine("SMOKE " + Key + "=" + Clean);
                _Out.Flush();
            }
        }

        // Avalonia calls an animation-frame callback when it renders the next frame; the second callback comes after
        // the first frame was rendered and presented.
        public static void WaitForRenderedFrame(TopLevel Window, Action Rendered)
        {
            Window.RequestAnimationFrame(_ => Window.RequestAnimationFrame(_ => Rendered()));
        }
    }
}
