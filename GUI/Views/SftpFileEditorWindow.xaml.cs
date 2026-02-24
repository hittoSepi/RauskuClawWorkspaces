using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace RauskuClaw.GUI.Views
{
    public partial class SftpFileEditorWindow : Window
    {
        private readonly string _remotePath;
        private readonly string _tempPath;
        private readonly bool _readOnly;

        public SftpFileEditorWindow(string remotePath, string tempPath, string initialContent, bool readOnly)
        {
            InitializeComponent();
            _remotePath = remotePath;
            _tempPath = tempPath;
            _readOnly = readOnly;

            RemotePathTextBlock.Text = remotePath;
            TempPathTextBlock.Text = tempPath;
            EditorTextBox.Text = initialContent ?? string.Empty;
            EditorTextBox.IsReadOnly = readOnly;
            UploadButton.IsEnabled = !readOnly;

            if (readOnly)
            {
                StatusTextBlock.Text = "Read-only mode.";
            }
        }

        public event EventHandler<UploadRequestedEventArgs>? UploadRequested;

        private async void SaveTempButton_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                await File.WriteAllTextAsync(_tempPath, EditorTextBox.Text ?? string.Empty);
                StatusTextBlock.Text = $"Temp saved: {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Save failed: {ex.Message}";
            }
        }

        private async void UploadButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (_readOnly)
            {
                return;
            }

            try
            {
                await File.WriteAllTextAsync(_tempPath, EditorTextBox.Text ?? string.Empty);
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Save failed: {ex.Message}";
                return;
            }

            var uploadArgs = new UploadRequestedEventArgs(_remotePath, _tempPath);
            UploadRequested?.Invoke(this, uploadArgs);
            var result = await uploadArgs.WaitForResultAsync();
            StatusTextBlock.Text = result.Success
                ? $"Uploaded: {DateTime.Now:HH:mm:ss}"
                : $"Upload failed: {result.Message}";
        }

        private void CloseButton_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    public sealed class UploadRequestedEventArgs : EventArgs
    {
        private readonly TaskCompletionSource<(bool Success, string Message)> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public UploadRequestedEventArgs(string remotePath, string tempPath)
        {
            RemotePath = remotePath;
            TempPath = tempPath;
        }

        public string RemotePath { get; }
        public string TempPath { get; }

        public void SetResult(bool success, string message)
        {
            _completion.TrySetResult((success, message));
        }

        public Task<(bool Success, string Message)> WaitForResultAsync()
        {
            return _completion.Task;
        }
    }
}
