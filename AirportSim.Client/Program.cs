using System;
using Avalonia;

namespace AirportSim.Client
{
    internal sealed class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called.
        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                BuildAvaloniaApp()
                    .StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                // Force the crash details to print to the console
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n================ FATAL CRASH ================");
                Console.WriteLine($"ERROR: {ex.Message}");
                Console.WriteLine("================ STACK TRACE ================");
                Console.WriteLine(ex.StackTrace);
                Console.WriteLine("=============================================\n");
                Console.ResetColor();
            }
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}