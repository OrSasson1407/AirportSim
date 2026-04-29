using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AirportSim.Client.Views;
using System;

namespace AirportSim.Client
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainWindow = new MainWindow();
                desktop.MainWindow = mainWindow;

                // Graceful shutdown — ViewModel property added in Step 7
                // so the full shutdown hook is wired there instead
                desktop.ShutdownRequested += (_, _) =>
                {
                    // placeholder — extended in Step 7
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}