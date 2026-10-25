namespace LootWindow.Update;

/// <summary>
/// Where the community data lives: an open repository on GitHub that players add to by uploading the file the app saves for them
/// (see "Share data with the community"). These are only addresses; the app itself sends nothing there.
/// </summary>
public static class CommunityData
{
    public const string Owner = "ExploreTheFloor", Repo = "MnMData";

    public static string RepoUrl => $"https://github.com/{Owner}/{Repo}";

    /// <summary>The folder players upload their file into.</summary>
    public static string SubmitUrl => $"{RepoUrl}/tree/main/contributions";

    /// <summary>The page that explains, step by step, what is shared and how to send it.</summary>
    public static string HowToUrl => $"{RepoUrl}/blob/main/CONTRIBUTING.md";

    /// <summary>The licence the data is shared under, as people are told it.</summary>
    public const string LicenseName = "CC BY-NC-SA 4.0";
}
