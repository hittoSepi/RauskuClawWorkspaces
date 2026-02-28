using RauskuClaw.GUI.ViewModels;
using RauskuClaw.Models;
using RauskuClaw.Services;

namespace RauskuClaw.Tests;

public class MainViewModelHomeTests
{
    [Fact]
    public void Constructor_SelectsHomeSection_WhenStartPageSettingEnabled()
    {
        using var temp = new TempDir();
        var resolver = new AppPathResolver(temp.Path);
        var settingsService = new SettingsService(new SettingsServiceOptions(), resolver, new SecretStorageService(resolver));
        var settings = settingsService.LoadSettings();
        settings.ShowStartPageOnStartup = true;
        settingsService.SaveSettings(settings);

        var vm = new MainViewModel(settingsService, resolver);

        Assert.Equal(MainViewModel.MainContentSection.Home, vm.SelectedMainSection);
    }

    [Fact]
    public void Constructor_SelectsWorkspaceTabs_WhenStartPageSettingDisabled()
    {
        using var temp = new TempDir();
        var resolver = new AppPathResolver(temp.Path);
        var settingsService = new SettingsService(new SettingsServiceOptions(), resolver, new SecretStorageService(resolver));
        var settings = settingsService.LoadSettings();
        settings.ShowStartPageOnStartup = false;
        settingsService.SaveSettings(settings);

        var vm = new MainViewModel(settingsService, resolver);

        Assert.Equal(MainViewModel.MainContentSection.WorkspaceTabs, vm.SelectedMainSection);
    }

    [Fact]
    public void OpenWorkspaceFromHomeCommand_SelectsWorkspaceAndNavigatesToWorkspaceTabs()
    {
        using var temp = new TempDir();
        var resolver = new AppPathResolver(temp.Path);
        var settingsService = new SettingsService(new SettingsServiceOptions(), resolver, new SecretStorageService(resolver));
        var vm = new MainViewModel(settingsService, resolver);

        var workspace = new Workspace { Name = "Test Workspace" };
        vm.Workspaces.Add(workspace);
        vm.SelectedMainSection = MainViewModel.MainContentSection.Home;

        vm.OpenWorkspaceFromHomeCommand.Execute(workspace);

        Assert.Same(workspace, vm.SelectedWorkspace);
        Assert.Equal(MainViewModel.MainContentSection.WorkspaceTabs, vm.SelectedMainSection);
    }
}
