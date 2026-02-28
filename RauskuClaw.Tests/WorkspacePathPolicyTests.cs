using RauskuClaw.Models;
using RauskuClaw.Services;

namespace RauskuClaw.Tests;

public sealed class WorkspacePathPolicyTests
{
    [Fact]
    public void CanDeleteFile_ReturnsFalse_ForPathOutsideManagedRoots()
    {
        using var temp = new TempDir();
        var resolver = new AppPathResolver(temp.Path);
        var settings = new Settings { VmBasePath = "VMRoot", WorkspacePath = "WorkspacesRoot" };
        var sut = new WorkspacePathPolicy(resolver);
        var outsidePath = Path.Combine(temp.Path, "outside", "rogue.txt");

        var allowed = sut.CanDeleteFile(outsidePath, settings, out _, out var reason);

        Assert.False(allowed);
        Assert.Contains("outside managed roots", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CanDeleteFile_ReturnsTrue_ForWorkspaceOwnedPath()
    {
        using var temp = new TempDir();
        var resolver = new AppPathResolver(temp.Path);
        var settings = new Settings { VmBasePath = "VMRoot", WorkspacePath = "WorkspacesRoot" };
        var sut = new WorkspacePathPolicy(resolver);
        var workspaceRoot = resolver.ResolveWorkspaceRootPath(settings);
        var ownedPath = Path.Combine(workspaceRoot, "ws-a", "a.txt");

        var allowed = sut.CanDeleteFile(ownedPath, settings, out var resolved, out _);

        Assert.True(allowed);
        Assert.Equal(Path.GetFullPath(ownedPath), resolved, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void CanDeleteDirectory_ReturnsFalse_ForManagedRootDirectory()
    {
        using var temp = new TempDir();
        var resolver = new AppPathResolver(temp.Path);
        var settings = new Settings { VmBasePath = "VMRoot", WorkspacePath = "WorkspacesRoot" };
        var sut = new WorkspacePathPolicy(resolver);
        var vmRoot = resolver.ResolveVmBasePath(settings);

        var allowed = sut.CanDeleteDirectory(vmRoot, settings, out _, out var reason);

        Assert.False(allowed);
        Assert.Contains("cannot be deleted", reason, StringComparison.OrdinalIgnoreCase);
    }
}
