using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using RauskuClaw.Services;

namespace RauskuClaw
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private VmProcessRegistry? _vmProcessRegistry;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            RegisterGlobalCleanupHandlers();
            SweepOrphanedVmProcessesOnStartup();
            ValidateConfiguredPathsOnStartup();
        }

        private void RegisterGlobalCleanupHandlers()
        {
            _vmProcessRegistry = new VmProcessRegistry(new AppPathResolver());

            DispatcherUnhandledException += (_, _) =>
            {
                TryCleanupRegisteredVmProcesses();
            };

            AppDomain.CurrentDomain.UnhandledException += (_, _) =>
            {
                TryCleanupRegisteredVmProcesses();
            };

            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                TryCleanupRegisteredVmProcesses();
                args.SetObserved();
            };

            Exit += (_, _) =>
            {
                TryCleanupRegisteredVmProcesses();
            };
        }

        private void SweepOrphanedVmProcessesOnStartup()
        {
            try
            {
                _vmProcessRegistry ??= new VmProcessRegistry(new AppPathResolver());
                var result = _vmProcessRegistry.SweepOrphanedProcesses();
                if (result.Killed <= 0)
                {
                    return;
                }

                MessageBox.Show(
                    $"Recovered and stopped {result.Killed} orphaned VM process(es) from a previous run.",
                    "RauskuClaw VM cleanup",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch
            {
                // Best-effort startup cleanup.
            }
        }

        private void TryCleanupRegisteredVmProcesses()
        {
            try
            {
                _vmProcessRegistry ??= new VmProcessRegistry(new AppPathResolver());
                _vmProcessRegistry.CleanupRegisteredProcesses();
            }
            catch
            {
                // Best-effort crash/exit cleanup.
            }
        }

        private static void ValidateConfiguredPathsOnStartup()
        {
            try
            {
                var resolver = new AppPathResolver();
                var settingsService = new SettingsService(pathResolver: resolver);
                var settings = settingsService.LoadSettings();
                var failures = new List<string>();

                ValidatePath("VM base path", resolver.ResolveVmBasePath(settings), resolver, failures);
                ValidatePath("Workspace path", resolver.ResolveWorkspaceRootPath(settings), resolver, failures);
                ValidatePath("Settings directory", resolver.ResolveSettingsDirectory(), resolver, failures);
                ValidatePath("Templates directory", resolver.ResolveTemplateDirectory(), resolver, failures);
                ValidatePath("Default templates directory", resolver.ResolveDefaultTemplateDirectory(), resolver, failures);

                if (failures.Count == 0)
                {
                    return;
                }

                MessageBox.Show(
                    "One or more configured paths are invalid or not writable:\n\n" + string.Join("\n", failures),
                    "RauskuClaw path validation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Startup path validation failed: {ex.Message}",
                    "RauskuClaw path validation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private static void ValidatePath(string label, string path, AppPathResolver resolver, List<string> failures)
        {
            if (!resolver.TryValidateWritableDirectory(path, out var error))
            {
                failures.Add($"- {label}: '{path}' ({error})");
            }
        }
    }
}
