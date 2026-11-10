using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using ZeroFileCommander.Models;

namespace ZeroFileCommander.Controls;

/// <summary>Tek bir dosya paneli. İki tanesi yan yana ana pencereyi oluşturur.</summary>
public sealed partial class FilePane : UserControl
{
    private static readonly EnumerationOptions Enumeration = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
    };

    public ObservableCollection<FileItem> Items { get; } = new();

    /// <summary>Boş string = "Bu bilgisayar" (sürücü listesi).</summary>
    public string CurrentPath { get; private set; } = string.Empty;

    public bool IsDriveList => CurrentPath.Length == 0;

    /// <summary>Panel odak aldığında tetiklenir; ana pencere aktif paneli buna göre belirler.</summary>
    public event EventHandler? Activated;

    public FilePane()
    {
        InitializeComponent();
    }

    public IReadOnlyList<FileItem> SelectedItems =>
        FileList.SelectedItems.Cast<FileItem>().Where(i => !i.IsParent && !i.IsDrive).ToList();

    public FileItem? FocusedItem => FileList.SelectedItem as FileItem;

    public void FocusList() => FileList.Focus(FocusState.Programmatic);

    public void SetActive(bool active)
    {
        var key = active ? "AccentFillColorDefaultBrush" : "CardStrokeColorDefaultBrush";
        RootBorder.BorderBrush = (Brush)Application.Current.Resources[key];
        RootBorder.BorderThickness = new Thickness(active ? 1.5 : 1);
    }

    public void Navigate(string? path)
    {
        var previous = CurrentPath;
        var newItems = new List<FileItem>();

        if (string.IsNullOrEmpty(path))
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                var label = drive.IsReady && !string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? $"{drive.Name.TrimEnd('\\')}  {drive.VolumeLabel}"
                    : drive.Name.TrimEnd('\\');
                newItems.Add(new FileItem { Name = label, FullPath = drive.RootDirectory.FullName, IsDirectory = true, IsDrive = true });
            }
            CurrentPath = string.Empty;
            PathBox.Text = "Bu bilgisayar";
        }
        else
        {
            try
            {
                var dir = new DirectoryInfo(path);
                newItems.Add(new FileItem { Name = "..", FullPath = dir.Parent?.FullName ?? string.Empty, IsDirectory = true, IsParent = true });

                foreach (var d in dir.EnumerateDirectories("*", Enumeration).OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    newItems.Add(new FileItem { Name = d.Name, FullPath = d.FullName, IsDirectory = true, Modified = d.LastWriteTime });
                }
                foreach (var f in dir.EnumerateFiles("*", Enumeration).OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    newItems.Add(new FileItem { Name = f.Name, FullPath = f.FullName, Size = f.Length, Modified = f.LastWriteTime });
                }
                CurrentPath = dir.FullName;
                PathBox.Text = CurrentPath;
            }
            catch (Exception ex)
            {
                StatusText.Text = ex.Message;
                PathBox.Text = previous.Length == 0 ? "Bu bilgisayar" : previous;
                return;
            }
        }

        Items.Clear();
        foreach (var item in newItems) Items.Add(item);

        // Üst klasöre çıkınca geldiğimiz klasörü seçili getir (Total Commander davranışı).
        var reselect = Items.FirstOrDefault(i => !i.IsParent && string.Equals(i.FullPath.TrimEnd('\\'), previous.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
        FileList.SelectedItem = reselect ?? Items.FirstOrDefault();
        if (FileList.SelectedItem is not null) FileList.ScrollIntoView(FileList.SelectedItem);

        UpdateStatus();
    }

    public void Refresh() => Navigate(CurrentPath);

    public void GoUp()
    {
        if (IsDriveList) return;
        Navigate(Directory.GetParent(CurrentPath)?.FullName ?? string.Empty);
    }

    public void Open(FileItem item)
    {
        if (item.IsDirectory)
        {
            Navigate(item.FullPath);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void UpdateStatus()
    {
        var selected = SelectedItems;
        if (selected.Count > 1)
        {
            long bytes = selected.Where(i => !i.IsDirectory).Sum(i => i.Size ?? 0);
            StatusText.Text = $"{selected.Count} öğe seçili · {FileItem.FormatSize(bytes)}";
            return;
        }
        int dirs = Items.Count(i => i.IsDirectory && !i.IsParent);
        int files = Items.Count(i => !i.IsDirectory);
        StatusText.Text = IsDriveList ? $"{dirs} sürücü" : $"{dirs} klasör, {files} dosya";
    }

    private void OnGotFocus(object sender, RoutedEventArgs e) => Activated?.Invoke(this, EventArgs.Empty);

    private void UpButton_Click(object sender, RoutedEventArgs e) => GoUp();

    private void PathBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        var text = PathBox.Text.Trim();
        Navigate(text == "Bu bilgisayar" ? string.Empty : Environment.ExpandEnvironmentVariables(text));
        FocusList();
        e.Handled = true;
    }

    private void List_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (FocusedItem is { } item) Open(item);
    }

    private void List_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter when FocusedItem is { } item:
                Open(item);
                e.Handled = true;
                break;
            case VirtualKey.Back:
                GoUp();
                e.Handled = true;
                break;
        }
    }

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateStatus();
}
