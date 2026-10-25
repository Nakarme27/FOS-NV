namespace OpenNV.Runtime.Content;

internal enum NativeGame
{
    Fallout1,
    Fallout2,
    Fallout3,
    FalloutNewVegas,
}

internal sealed record NativeGameInstallation(NativeGame Game, string InstallRoot, string ContentRoot)
{
    private const string FalloutNewVegasMasterName = "FalloutNV" + ".esm";

    internal bool IsTaleOfTwoWastelands => ContainsFile(ContentRoot, "TaleOfTwoWastelands.esm");

    internal static NativeGameInstallation Detect(string selectedRoot)
    {
        if (string.IsNullOrWhiteSpace(selectedRoot))
            throw new ArgumentException("A game installation root is required.", nameof(selectedRoot));

        var selected = Path.GetFullPath(selectedRoot);
        if (!Directory.Exists(selected))
            throw new DirectoryNotFoundException($"Selected game folder does not exist: {selected}");

        try
        {
            // Accept both the actual game root and its Data directory. Android users
            // often navigate directly to Data because that is where the large retail
            // payload lives. Keep InstallRoot normalized to the parent so INI and
            // other installation-level lookups continue to work.
            var selectedIsData = Path.GetFileName(selected)
                .Equals("Data", StringComparison.OrdinalIgnoreCase);
            var dataRoot = selectedIsData ? selected : FindDirectory(selected, "Data");
            var installRoot = selectedIsData
                ? Directory.GetParent(selected)?.FullName ?? selected
                : selected;
            var contentRoot = dataRoot ?? selected;
            var game = DetectGame(installRoot, contentRoot);
            return new NativeGameInstallation(game, installRoot, contentRoot);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new UnauthorizedAccessException(
                "Android can see the selected folder but FOS NV cannot read its files. " +
                "Grant ‘All files access’ to FOS NV, return to the app, and select the folder again.",
                exception);
        }
    }

    private static NativeGame DetectGame(string installRoot, string contentRoot)
    {
        if (ContainsFile(installRoot, "patch000.dat") &&
            ContainsFile(installRoot, "critter.dat") &&
            ContainsFile(installRoot, "master.dat"))
            return NativeGame.Fallout2;
        if (ContainsFile(installRoot, "critter.dat") && ContainsFile(installRoot, "master.dat"))
            return NativeGame.Fallout1;
        // TTW uses the New Vegas engine even though its output also contains
        // Fallout3.esm. Do not route it through standalone Fallout 3 behavior.
        if (ContainsFile(contentRoot, FalloutNewVegasMasterName))
            return NativeGame.FalloutNewVegas;
        if (ContainsFile(contentRoot, "Fallout3.esm"))
            return NativeGame.Fallout3;
        throw new InvalidDataException(
            $"No Fallout master file was found in '{contentRoot}'. For Fallout: New Vegas, " +
            "select the game folder containing Data/FalloutNV.esm or select that Data folder itself. " +
            "On Android, also make sure FOS NV has ‘All files access’. ");
    }

    private static string? FindDirectory(string root, string name) =>
        Directory.EnumerateDirectories(root)
            .SingleOrDefault(path => Path.GetFileName(path).Equals(name, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsFile(string root, string name) =>
        Directory.EnumerateFiles(root)
            .Any(path => Path.GetFileName(path).Equals(name, StringComparison.OrdinalIgnoreCase));
}
