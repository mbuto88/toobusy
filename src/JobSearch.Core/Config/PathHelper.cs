namespace JobSearch.Core.Config;

public static class PathHelper
{
    private static string? _solutionRoot;

    // Walks up from the assembly location to find the directory containing toobusy.sln.
    // Falls back to the current working directory if not found.
    public static string SolutionRoot
    {
        get
        {
            if (_solutionRoot != null) return _solutionRoot;

            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (dir.GetFiles("*.sln").Length > 0 || dir.GetFiles("toobusy.sln").Length > 0)
                {
                    _solutionRoot = dir.FullName;
                    return _solutionRoot;
                }
                dir = dir.Parent;
            }

            _solutionRoot = Directory.GetCurrentDirectory();
            return _solutionRoot;
        }
    }

    public static string ConfigPath(string relativePath)
        => Path.Combine(SolutionRoot, relativePath);
}
