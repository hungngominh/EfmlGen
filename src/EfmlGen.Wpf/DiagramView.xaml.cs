using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EfmlGen.Core;
using EfmlGen.Wpf.Services;
using EfmlGen.Xml;
using Microsoft.Win32;

namespace EfmlGen.Wpf;

public partial class DiagramView : UserControl
{
    private const double MinZoom = 0.2;
    private const double MaxZoom = 4.0;
    private const double ZoomStep = 1.15;

    private double _contentWidth;
    private double _contentHeight;

    private bool _panning;
    private Point _panOrigin;
    private double _panOriginH;
    private double _panOriginV;

    public DiagramView()
    {
        InitializeComponent();
    }

    public string CurrentEfmlPath
    {
        get => EfmlPathBox.Text;
        set => EfmlPathBox.Text = value;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Entity Developer model (*.efml)|*.efml|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (!string.IsNullOrWhiteSpace(EfmlPathBox.Text))
        {
            try { dlg.InitialDirectory = Path.GetDirectoryName(EfmlPathBox.Text); } catch { }
        }
        if (dlg.ShowDialog() == true)
            EfmlPathBox.Text = dlg.FileName;
    }

    private void Load_Click(object sender, RoutedEventArgs e) => LoadDiagram();

    public void LoadDiagram()
    {
        var efmlPath = EfmlPathBox.Text?.Trim();
        if (string.IsNullOrEmpty(efmlPath) || !File.Exists(efmlPath))
        {
            SetStatus($"File not found: {efmlPath}", error: true);
            return;
        }

        try
        {
            var model = EfmlReader.ReadFile(efmlPath);
            var viewPath = FindSiblingViewFile(efmlPath);
            ViewLayout layout;
            string layoutSource;
            if (viewPath is null)
            {
                var grid = DiagramLayout.ComputeGrid(model);
                var dict = grid.ToDictionary(
                    kv => kv.Key,
                    kv => new ClassPosition(
                        kv.Value.X, kv.Value.Y,
                        DiagramLayout.ClassWidth,
                        DiagramLayout.ComputeClassHeight(model.Classes.First(c => c.Guid == kv.Key))));
                layout = new ViewLayout(dict);
                layoutSource = "auto-layout (no .view found)";
            }
            else
            {
                layout = ViewReader.ReadFile(viewPath);
                layoutSource = Path.GetFileName(viewPath);
            }

            DiagramRenderer.Render(DiagramCanvas, model, layout);
            ResizeCanvasToContent(layout);
            ZoomTransform.ScaleX = ZoomTransform.ScaleY = 1.0;
            UpdateZoomText();
            SetStatus($"Loaded {model.Classes.Count} classes, {model.Associations.Count} associations from {layoutSource}.", error: false);
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}", error: true);
        }
    }

    private static string? FindSiblingViewFile(string efmlPath)
    {
        var dir = Path.GetDirectoryName(efmlPath);
        var name = Path.GetFileNameWithoutExtension(efmlPath);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name)) return null;

        var preferred = Path.Combine(dir, $"{name}.Diagram1.view");
        if (File.Exists(preferred)) return preferred;

        var anyMatch = Directory.EnumerateFiles(dir, $"{name}.*.view").FirstOrDefault();
        return anyMatch;
    }

    private void ResizeCanvasToContent(ViewLayout layout)
    {
        double maxX = 0, maxY = 0;
        foreach (var pos in layout.Classes.Values)
        {
            if (pos.X + pos.Width > maxX) maxX = pos.X + pos.Width;
            if (pos.Y + pos.Height > maxY) maxY = pos.Y + pos.Height;
        }
        _contentWidth = Math.Max(600, maxX + 80);
        _contentHeight = Math.Max(400, maxY + 80);
        DiagramCanvas.Width = _contentWidth;
        DiagramCanvas.Height = _contentHeight;
    }

    private void DiagramScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        e.Handled = true;
        var factor = e.Delta > 0 ? ZoomStep : 1.0 / ZoomStep;
        ApplyZoom(ZoomTransform.ScaleX * factor);
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ApplyZoom(ZoomTransform.ScaleX * ZoomStep);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ApplyZoom(ZoomTransform.ScaleX / ZoomStep);

    private void Fit_Click(object sender, RoutedEventArgs e)
    {
        if (_contentWidth <= 0 || _contentHeight <= 0)
        {
            ApplyZoom(1.0);
            return;
        }
        var availW = DiagramScroll.ActualWidth - 24;
        var availH = DiagramScroll.ActualHeight - 24;
        if (availW <= 0 || availH <= 0) return;
        var scale = Math.Min(availW / _contentWidth, availH / _contentHeight);
        ApplyZoom(scale);
    }

    private void ApplyZoom(double newScale)
    {
        newScale = Math.Clamp(newScale, MinZoom, MaxZoom);
        ZoomTransform.ScaleX = ZoomTransform.ScaleY = newScale;
        UpdateZoomText();
    }

    private void UpdateZoomText() => ZoomText.Text = $"{ZoomTransform.ScaleX * 100:0}%";

    private void DiagramScroll_PanStart(object sender, MouseButtonEventArgs e)
    {
        _panning = true;
        _panOrigin = e.GetPosition(DiagramScroll);
        _panOriginH = DiagramScroll.HorizontalOffset;
        _panOriginV = DiagramScroll.VerticalOffset;
        DiagramScroll.CaptureMouse();
        Mouse.OverrideCursor = Cursors.ScrollAll;
        e.Handled = true;
    }

    private void DiagramScroll_PanMove(object sender, MouseEventArgs e)
    {
        if (!_panning) return;
        var pos = e.GetPosition(DiagramScroll);
        DiagramScroll.ScrollToHorizontalOffset(_panOriginH - (pos.X - _panOrigin.X));
        DiagramScroll.ScrollToVerticalOffset(_panOriginV - (pos.Y - _panOrigin.Y));
    }

    private void DiagramScroll_PanEnd(object sender, MouseButtonEventArgs e)
    {
        if (!_panning) return;
        _panning = false;
        DiagramScroll.ReleaseMouseCapture();
        Mouse.OverrideCursor = null;
        e.Handled = true;
    }

    private void SetStatus(string message, bool error)
    {
        StatusText.Text = message;
        StatusText.Foreground = error
            ? System.Windows.Media.Brushes.DarkRed
            : System.Windows.Media.Brushes.DimGray;
    }
}
