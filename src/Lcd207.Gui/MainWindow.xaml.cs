using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Lcd207.Gui;

public partial class MainWindow : Window
{
    private static readonly Brush IdleBorder = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
    private static readonly Brush HoverBorder = new SolidColorBrush(Color.FromRgb(0x3A, 0x96, 0xDD));

    private string? _imagePath;
    private bool _busy;
    private bool _resendQueued;

    public MainWindow() => InitializeComponent();

    private void DropZone_DragEnter(object sender, DragEventArgs e)
    {
        bool ok = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        DropZone.BorderBrush = ok ? HoverBorder : IdleBorder;
        e.Handled = true;
    }

    private void DropZone_DragLeave(object sender, DragEventArgs e) => DropZone.BorderBrush = IdleBorder;

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        DropZone.BorderBrush = IdleBorder;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            SetImage(files[0]);
    }

    private void DropZone_Click(object sender, MouseButtonEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choose an image",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files|*.*",
        };
        if (dlg.ShowDialog(this) == true)
            SetImage(dlg.FileName);
    }

    private void Options_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded && _imagePath != null)
            _ = SendAsync();
    }

    private void SetImage(string path)
    {
        _imagePath = path;
        _ = SendAsync();
    }

    /// <summary>
    /// Encodes and pushes the current image. If a push is already running, a single
    /// re-send is queued so the panel ends up showing the latest image/options.
    /// </summary>
    private async Task SendAsync()
    {
        if (_busy)
        {
            _resendQueued = true;
            return;
        }

        _busy = true;
        try
        {
            do
            {
                _resendQueued = false;
                string path = _imagePath!;
                int? rotation = (RotationBox.SelectedItem as ComboBoxItem)?.Tag is string { Length: > 0 } r ? int.Parse(r) : null;
                var fit = Enum.Parse<FitMode>((string)((ComboBoxItem)FitBox.SelectedItem).Tag);

                StatusText.Text = $"Encoding {Path.GetFileName(path)}…";
                byte[] jpeg;
                try
                {
                    jpeg = await Task.Run(() => ImageEncoder.EncodeForPanel(path, Lcd207Panel.Width, Lcd207Panel.Height,
                        Lcd207Panel.MaxPayload, rotation, fit));
                }
                catch (Exception ex)
                {
                    StatusText.Text = $"Could not load image: {ex.Message}";
                    continue;
                }

                ShowPreview(jpeg);
                StatusText.Text = $"Sending {Path.GetFileName(path)} ({jpeg.Length:N0} bytes)…";
                try
                {
                    string? fw = await Task.Run(() =>
                    {
                        using var panel = Lcd207Panel.Open();
                        panel.Init(clearLayers: true);
                        panel.PushJpeg(jpeg);
                        return panel.Firmware;
                    });
                    StatusText.Text = $"Sent {Path.GetFileName(path)} to panel (firmware {fw ?? "unknown"}).";
                }
                catch (Exception ex)
                {
                    StatusText.Text = $"Panel error: {ex.Message}\nIs L-Connect closed and the panel in LCD mode?";
                }
            } while (_resendQueued);
        }
        finally
        {
            _busy = false;
        }
    }

    private void ShowPreview(byte[] jpeg)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = new MemoryStream(jpeg);
        bmp.EndInit();
        bmp.Freeze();
        Preview.Source = bmp;
        DropHint.Visibility = Visibility.Collapsed;
    }
}
