namespace ZeroFileCommander.Controls;

public enum PaneCommand
{
    Open,
    OpenWith,
    Preview,
    Play,
    Cut,
    Copy,
    Paste,
    Rename,
    Delete,
    CopyPath,
    CopyToOther,
    MoveToOther,
    Properties,
    NewFolder,
    Refresh,
    OpenTerminal,
    ShowInExplorer,
    RunAsAdmin,
    Edit,
}

/// <summary>Panelin komutları ve bırakılan dosyaları ilettiği ana pencere.</summary>
public interface IPaneHost
{
    bool CanPaste { get; }

    void Execute(PaneCommand command, FilePane pane);

    Task DropFilesAsync(FilePane target, IReadOnlyList<string> paths, bool copy);

    /// <summary>Panelin klasöründe yeni klasör (<paramref name="entry"/> null) ya da dosya oluşturur.</summary>
    Task CreateNewAsync(FilePane pane, Services.ShellNewEntry? entry);
}
