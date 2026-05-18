using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using EfmlGen.Core;
using EfmlGen.Xml;

namespace EfmlGen.Wpf.Services;

/// <summary>
/// Populates a WPF <see cref="Canvas"/> with shapes for an EfmlModel using
/// positions from a parsed <see cref="ViewLayout"/>. Pure procedural rendering —
/// no data binding, no MVVM.
/// </summary>
public static class DiagramRenderer
{
    private static readonly Brush HeaderBrush = new SolidColorBrush(Color.FromRgb(0x5B, 0x71, 0x97));
    private static readonly Brush HeaderForeground = Brushes.White;
    private static readonly Brush BodyBackground = Brushes.White;
    private static readonly Brush BorderBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
    private static readonly Brush LineBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
    private static readonly Brush LabelBackground = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF));
    private static readonly Brush IdForeground = new SolidColorBrush(Color.FromRgb(0xB0, 0x80, 0x00));

    static DiagramRenderer()
    {
        HeaderBrush.Freeze();
        BorderBrush.Freeze();
        LineBrush.Freeze();
        LabelBackground.Freeze();
        IdForeground.Freeze();
    }

    public static void Render(Canvas canvas, EfmlModel model, ViewLayout layout)
    {
        canvas.Children.Clear();

        var classByName = new Dictionary<string, EfClass>(StringComparer.Ordinal);
        foreach (var c in model.Classes) classByName[c.Name] = c;

        var bounds = new Dictionary<Guid, Rect>(model.Classes.Count);

        // 1. Pre-compute bounds for everyone with a layout position.
        foreach (var c in model.Classes)
        {
            if (!layout.Classes.TryGetValue(c.Guid, out var pos)) continue;
            bounds[c.Guid] = new Rect(pos.X, pos.Y, pos.Width, pos.Height);
        }

        // 2. Render FK lines first (so they sit beneath shapes).
        foreach (var assoc in model.Associations)
        {
            if (!classByName.TryGetValue(assoc.End1.ClassName, out var c1)) continue;
            if (!classByName.TryGetValue(assoc.End2.ClassName, out var c2)) continue;
            if (!bounds.TryGetValue(c1.Guid, out var r1)) continue;
            if (!bounds.TryGetValue(c2.Guid, out var r2)) continue;

            var (p1, p2) = ComputeEndpoints(r1, r2);

            var line = new Line
            {
                X1 = p1.X,
                Y1 = p1.Y,
                X2 = p2.X,
                Y2 = p2.Y,
                Stroke = LineBrush,
                StrokeThickness = 1.5,
                SnapsToDevicePixels = true,
                IsHitTestVisible = false
            };
            canvas.Children.Add(line);

            AddMarker(canvas, p1, r1, MultiplicityLabel(assoc.End1.Multiplicity));
            AddMarker(canvas, p2, r2, MultiplicityLabel(assoc.End2.Multiplicity));
        }

        // 3. Render class shapes on top.
        foreach (var c in model.Classes)
        {
            if (!bounds.TryGetValue(c.Guid, out var r)) continue;
            var shape = BuildClassShape(c, r.Width, r.Height);
            Canvas.SetLeft(shape, r.X);
            Canvas.SetTop(shape, r.Y);
            canvas.Children.Add(shape);
        }
    }

    private static Border BuildClassShape(EfClass c, double width, double height)
    {
        var header = new Border
        {
            Background = HeaderBrush,
            Height = 28,
            Padding = new Thickness(8, 0, 8, 0),
            Child = new TextBlock
            {
                Text = c.Name,
                Foreground = HeaderForeground,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            }
        };

        var body = new StackPanel { Background = BodyBackground };
        foreach (var p in c.AllProperties)
            body.Children.Add(BuildPropertyRow(p, isId: ReferenceEquals(p, c.Id)));

        var content = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(header, Dock.Top);
        content.Children.Add(header);
        content.Children.Add(body);

        return new Border
        {
            Width = width,
            Height = height,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Background = BodyBackground,
            SnapsToDevicePixels = true,
            Child = content,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 4,
                ShadowDepth = 1.5,
                Opacity = 0.25,
                Color = Colors.Black
            }
        };
    }

    private static UIElement BuildPropertyRow(EfProperty p, bool isId)
    {
        var label = isId ? "🔑 " + p.Name : p.Name;
        var typeText = TypeMap.ToCSharp(p.Type) + (p.IsNullable ? "?" : "");

        var tb = new TextBlock
        {
            Padding = new Thickness(8, 1, 8, 1),
            FontSize = 11,
            FontWeight = isId ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = isId ? IdForeground : Brushes.Black,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        tb.Inlines.Add(new System.Windows.Documents.Run(label));
        tb.Inlines.Add(new System.Windows.Documents.Run("  : " + typeText)
        {
            Foreground = Brushes.Gray,
            FontStyle = FontStyles.Italic
        });
        return tb;
    }

    private static void AddMarker(Canvas canvas, Point at, Rect ownerRect, string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        // Offset marker a few px inside the rect from the edge endpoint.
        var center = new Point(ownerRect.X + ownerRect.Width / 2, ownerRect.Y + ownerRect.Height / 2);
        var dx = center.X - at.X;
        var dy = center.Y - at.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.001) return;
        dx /= len; dy /= len;
        var labelPt = new Point(at.X + dx * 14, at.Y + dy * 12);

        var label = new Border
        {
            Background = LabelBackground,
            Padding = new Thickness(3, 0, 3, 0),
            CornerRadius = new CornerRadius(2),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = LineBrush
            }
        };
        Canvas.SetLeft(label, labelPt.X - 8);
        Canvas.SetTop(label, labelPt.Y - 8);
        canvas.Children.Add(label);
    }

    private static string MultiplicityLabel(Multiplicity m) => m switch
    {
        Multiplicity.One => "1",
        Multiplicity.ZeroOrOne => "0..1",
        Multiplicity.Many => "∞",
        _ => ""
    };

    /// <summary>
    /// Compute connector endpoints: intersect the center-to-center segment with
    /// each rectangle's boundary.
    /// </summary>
    private static (Point P1, Point P2) ComputeEndpoints(Rect r1, Rect r2)
    {
        var c1 = new Point(r1.X + r1.Width / 2, r1.Y + r1.Height / 2);
        var c2 = new Point(r2.X + r2.Width / 2, r2.Y + r2.Height / 2);
        return (IntersectRect(c1, c2, r1), IntersectRect(c2, c1, r2));
    }

    private static Point IntersectRect(Point inside, Point outside, Rect rect)
    {
        var dx = outside.X - inside.X;
        var dy = outside.Y - inside.Y;
        if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001) return inside;

        double tMin = double.PositiveInfinity;
        // For each of 4 edges, compute parameter t in (0,1] where segment crosses.
        if (Math.Abs(dx) > 0.001)
        {
            var tLeft = (rect.X - inside.X) / dx;
            var tRight = (rect.Right - inside.X) / dx;
            if (tLeft > 0)
            {
                var y = inside.Y + tLeft * dy;
                if (y >= rect.Y && y <= rect.Bottom && tLeft < tMin) tMin = tLeft;
            }
            if (tRight > 0)
            {
                var y = inside.Y + tRight * dy;
                if (y >= rect.Y && y <= rect.Bottom && tRight < tMin) tMin = tRight;
            }
        }
        if (Math.Abs(dy) > 0.001)
        {
            var tTop = (rect.Y - inside.Y) / dy;
            var tBottom = (rect.Bottom - inside.Y) / dy;
            if (tTop > 0)
            {
                var x = inside.X + tTop * dx;
                if (x >= rect.X && x <= rect.Right && tTop < tMin) tMin = tTop;
            }
            if (tBottom > 0)
            {
                var x = inside.X + tBottom * dx;
                if (x >= rect.X && x <= rect.Right && tBottom < tMin) tMin = tBottom;
            }
        }
        if (double.IsPositiveInfinity(tMin)) return inside;
        return new Point(inside.X + tMin * dx, inside.Y + tMin * dy);
    }
}
