using System;
using System.Collections.Generic;

namespace EfmlGen.Core;

/// <summary>
/// Compute (X, Y) coordinates for each class on the Entity Developer diagram canvas.
/// Simple row/column grid: classes laid out left-to-right, top-to-bottom; each row's
/// vertical advance is the tallest class in that row plus <c>vGap</c>.
/// </summary>
public static class DiagramLayout
{
    public const int ClassWidth = 150;
    public const int ClassHeaderHeight = 35;
    public const int PropertyRowHeight = 18;
    public const int RelationsAreaHeight = 19;
    public const int ClassFooterHeight = 14;

    public static int ComputeClassHeight(EfClass c)
    {
        int propsCount = 0;
        foreach (var _ in c.AllProperties) propsCount++;
        return ClassHeaderHeight + (1 + propsCount * PropertyRowHeight) + RelationsAreaHeight + ClassFooterHeight;
    }

    public static IReadOnlyDictionary<Guid, (int X, int Y)> ComputeGrid(
        EfmlModel model, int columns = 4, int hGap = 50, int vGap = 50)
    {
        if (columns < 1)
            throw new ArgumentOutOfRangeException(nameof(columns), columns, "Must be >= 1.");

        var result = new Dictionary<Guid, (int X, int Y)>(model.Classes.Count);

        int curX = 0;
        int curY = 0;
        int rowMaxHeight = 0;
        int col = 0;

        foreach (var c in model.Classes)
        {
            result[c.Guid] = (curX, curY);
            var h = ComputeClassHeight(c);
            if (h > rowMaxHeight) rowMaxHeight = h;

            col++;
            if (col >= columns)
            {
                col = 0;
                curX = 0;
                curY += rowMaxHeight + vGap;
                rowMaxHeight = 0;
            }
            else
            {
                curX += ClassWidth + hGap;
            }
        }

        return result;
    }
}
