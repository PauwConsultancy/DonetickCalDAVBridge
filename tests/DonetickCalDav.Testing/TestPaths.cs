namespace DonetickCalDav.Testing;

/// <summary>Locations in the repository, found from the test binaries upwards.</summary>
public static class TestPaths
{
    private static readonly Lazy<string> Root = new(FindRepositoryRoot);

    /// <summary>The repository root (the folder with <c>donetick_caldav.sln</c>).</summary>
    public static string RepositoryRoot => Root.Value;

    /// <summary><c>tests/fixtures</c>.</summary>
    public static string Fixtures => Path.Combine(RepositoryRoot, "tests", "fixtures");

    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "donetick_caldav.sln")))
                return dir.FullName;
        }

        throw new DirectoryNotFoundException(
            $"Repository root with donetick_caldav.sln not found above {AppContext.BaseDirectory}.");
    }
}
