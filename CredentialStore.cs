using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeUsage;

/// <summary>
/// Reads and writes %USERPROFILE%\.claude\.credentials.json, the file where Claude Code
/// on Windows stores the OAuth tokens of the claude.ai account.
/// </summary>
public static class CredentialStore
{
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json");

    public static OAuthCredentials Load()
    {
        if (!File.Exists(FilePath))
        {
            throw new UsageException(L.F("err.noCredsFile", FilePath));
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(FilePath));
        }
        catch (JsonException ex)
        {
            throw new UsageException(L.Get("err.invalidJson"), ex);
        }

        var oauth = root?["claudeAiOauth"]?.AsObject()
            ?? throw new UsageException(L.Get("err.noOauth"));

        var access = oauth["accessToken"]?.GetValue<string>();
        var refresh = oauth["refreshToken"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(access) || string.IsNullOrWhiteSpace(refresh))
        {
            throw new UsageException(L.Get("err.noTokens"));
        }

        long expiresAt = oauth["expiresAt"]?.GetValue<long>() ?? 0;
        var scopes = oauth["scopes"]?.AsArray()
            .Select(n => n?.GetValue<string>())
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(s => s!)
            .ToArray() ?? Array.Empty<string>();
        var subscription = oauth["subscriptionType"]?.GetValue<string>();

        return new OAuthCredentials(access, refresh, expiresAt, scopes, subscription);
    }

    /// <summary>
    /// Writes refreshed tokens back in the same format Claude Code uses, so Claude Code
    /// and this app keep sharing one login. All other fields (scopes, subscriptionType, ...)
    /// are left untouched. The file is replaced atomically through a temp file.
    /// </summary>
    public static void Save(string accessToken, string refreshToken, long expiresAtUnixMs)
    {
        var root = JsonNode.Parse(File.ReadAllText(FilePath))?.AsObject()
            ?? throw new UsageException(L.Get("err.invalidJson"));
        var oauth = root["claudeAiOauth"]?.AsObject()
            ?? throw new UsageException(L.Get("err.noOauth"));

        oauth["accessToken"] = accessToken;
        oauth["refreshToken"] = refreshToken;
        oauth["expiresAt"] = expiresAtUnixMs;

        var tmp = FilePath + ".tmp-" + Environment.ProcessId;
        File.WriteAllText(tmp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
        File.Move(tmp, FilePath, overwrite: true);
    }
}
