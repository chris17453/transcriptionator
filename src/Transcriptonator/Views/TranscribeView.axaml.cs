using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Transcriptonator.ViewModels;

namespace Transcriptonator.Views;

public partial class TranscribeView : UserControl
{
    public TranscribeView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is TranscribeViewModel vm)
        {
            vm.BrowseFileInteraction = async () =>
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return;

                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select Audio File",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Audio Files") { Patterns = new[]
                            { "*.mp3", "*.wav", "*.aiff", "*.aif", "*.wma", "*.m4a", "*.ogg", "*.flac", "*.voc" } }
                    }
                });

                if (files.Count > 0)
                {
                    var path = files[0].TryGetLocalPath();
                    if (path != null) vm.LoadFiles(path);
                }
            };

            vm.BrowseFolderInteraction = async () =>
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return;

                var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select Folder with Audio Files",
                    AllowMultiple = false
                });

                if (folders.Count > 0)
                {
                    var path = folders[0].TryGetLocalPath();
                    if (path != null) vm.LoadFiles(path);
                }
            };
        }
    }
}
