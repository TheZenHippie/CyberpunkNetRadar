using System;
using System.Windows;

namespace CyberPunkNetRadar
{
    public partial class App : Application
    {
        private static int _isExiting = 0;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Fix WPF submenu alignment
            EnsureStandardMenuDropAlignment();
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += (s, args) => EnsureStandardMenuDropAlignment();

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                CleanProcessExit(1);
            };

            DispatcherUnhandledException += (s, args) =>
            {
                args.Handled = true;
            };
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
            CleanProcessExit(e.ApplicationExitCode);
        }

        public static void CleanProcessExit(int exitCode = 0)
        {
            if (System.Threading.Interlocked.Exchange(ref _isExiting, 1) != 0)
            {
                return;
            }

            try
            {
                GC.Collect(2, GCCollectionMode.Forced, true);
                GC.WaitForPendingFinalizers();
            }
            catch { }
            finally
            {
                Environment.Exit(exitCode);
            }
        }

        public static void EnsureStandardMenuDropAlignment()
        {
            if (SystemParameters.MenuDropAlignment)
            {
                try
                {
                    var field = typeof(SystemParameters).GetField("_menuDropAlignment",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    if (field != null)
                    {
                        field.SetValue(null, false);
                    }
                }
                catch { }
            }
        }
    }
}

