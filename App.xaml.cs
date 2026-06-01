using System;
using System.Threading;
using System.Windows;
using FencesWPF.Services;

namespace FencesWPF
{
    public partial class App : Application
    {
        // ── Single-instance mutex ──────────────────────────────────────────────
        // Prevents two copies of the app running simultaneously.
        // Two copies = duplicate tray icons + layout corruption on exit.
        private static Mutex? _instanceMutex;
        private const string MutexName = "FencesWPF_SingleInstance_Mutex";

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // ── Single instance guard ──────────────────────────────────────────
            _instanceMutex = new Mutex(initiallyOwned: true, MutexName,
                                       out bool isNewInstance);

            if (!isNewInstance)
            {
                MessageBox.Show(
                    "FencesWPF ya está en ejecución.\n\nBúscalo en la bandeja del sistema.",
                    "FencesWPF",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                _instanceMutex.Dispose();
                Shutdown();
                return;
            }

            // ── Global exception handlers ──────────────────────────────────────
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            // ── Initialize ─────────────────────────────────────────────────────
            FenceManager.Instance.Initialize();
        }

        private void OnDispatcherUnhandledException(object sender,
            System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            // Try emergency save before showing the error
            try { FenceManager.Instance.SaveLayout(force: true); } catch { }

            MessageBox.Show(
                $"Error inesperado:\n\n{e.Exception.Message}\n\n" +
                $"Tu layout ha sido guardado automáticamente.",
                "FencesWPF — Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            e.Handled = true;
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try { FenceManager.Instance.SaveLayout(force: true); } catch { }

            var ex = e.ExceptionObject as Exception;
            MessageBox.Show(
                $"Error crítico:\n\n{ex?.Message}\n\n" +
                $"Tu layout ha sido guardado automáticamente.",
                "FencesWPF — Error Crítico",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            // Release mutex so the next launch can acquire it
            try
            {
                if (_instanceMutex != null && _instanceMutex.WaitOne(0))
                    _instanceMutex.ReleaseMutex();
                _instanceMutex?.Dispose();
            }
            catch { }
        }
    }
}
