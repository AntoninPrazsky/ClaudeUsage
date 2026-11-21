using System.Reflection;

namespace ClaudeUsage;

/// <summary>What the About window says about the app, the same in the Windows and the Linux front end.</summary>
public static class AppInfo
{
    public const string Name = "Claude Usage";

    public const string RepositoryUrl = "https://github.com/AntoninPrazsky/ClaudeUsage";

    /// <summary>The latest release of BS3D, the game the About window's animation advertises; it opens in the browser.</summary>
    public const string GameUrl = "https://github.com/AntoninPrazsky/BS3D/releases/latest";

    /// <summary>The line under the animation and its link: English in either UI language.</summary>
    public const string GameCaption = "BS3D, a 3D bubble shooter by the same author";
    public const string GameLinkText = "Try the new game";

    /// <summary>The version the build was given (a release takes it from its tag), as major.minor.patch.</summary>
    public static string Version { get; } = Assembly.GetEntryAssembly()?.GetName().Version is { } v
        ? v.Build >= 0 ? $"{v.Major}.{v.Minor}.{v.Build}" : $"{v.Major}.{v.Minor}"
        : "?";

    /// <summary>The project file's copyright line ("Copyright (c) 2026 …"), written with the © sign.</summary>
    public static string Copyright { get; } =
        (Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "")
        .Replace("Copyright (c)", "©", StringComparison.Ordinal);
}
