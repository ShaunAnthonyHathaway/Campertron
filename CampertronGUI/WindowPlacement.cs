using Avalonia;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Campertron
{
    // A screen as the placement sees it (Avalonia's Screen: bounds and working area in device pixels, its scaling).
    internal readonly record struct PlacementScreen(PixelRect Bounds, PixelRect WorkingArea, double Scaling, bool IsPrimary);

    // Where the new window goes: its position in device pixels (null: the window's own startup placement, centered), its
    // size in device-independent pixels, and whether it is maximized once it is shown.
    internal readonly record struct PlacementResult(PixelPoint? Position, double Width, double Height, bool Maximized);

    // The automatic restart's window placement (decision D62, business-rules-gui.md §26): the old window's normal bounds,
    // fitted to the screens the new process has. A window that lies within a screen's working area keeps its position and
    // size; one that doesn't (a monitor gone, the arrangement or scaling changed, values out of range) is moved and shrunk
    // the least to fit the working area of the screen that holds its top-left corner, or of the nearest one, with room for
    // its title bar.
    // The size never goes below the window's minimum. Without any screen information only the size is used.
    internal static class WindowPlacement
    {
        // When a window is moved down into a working area, room above it for its title bar (Avalonia's position is the
        // frame's on some platforms and the client area's on others).
        internal const double TitleBarRoom = 40;

        internal static PlacementResult? Fit(RestartWindow Stored, IReadOnlyList<PlacementScreen> Screens, double MinWidth, double MinHeight)
        {
            if (!double.IsFinite(Stored.Width) || !double.IsFinite(Stored.Height) || Stored.Width < 1 || Stored.Height < 1
                || Stored.Width > RestartHandoffStore.MaxSize || Stored.Height > RestartHandoffStore.MaxSize
                || Math.Abs((long)Stored.X) > RestartHandoffStore.MaxCoordinate || Math.Abs((long)Stored.Y) > RestartHandoffStore.MaxCoordinate)
            {
                return null;
            }
            double Width = Math.Max(Stored.Width, MinWidth);
            double Height = Math.Max(Stored.Height, MinHeight);
            List<PlacementScreen> Usable = Screens.Where(S => S.WorkingArea.Width > 0 && S.WorkingArea.Height > 0 && double.IsFinite(S.Scaling) && S.Scaling > 0).ToList();
            if (Usable.Count == 0)
            {
                return new PlacementResult(null, Width, Height, Stored.Maximized);
            }
            PlacementScreen Screen = Choose(Stored, Usable);
            PixelRect Area = Screen.WorkingArea;
            double Scale = Screen.Scaling;
            int Room = Math.Min((int)Math.Round(TitleBarRoom * Scale), Area.Height / 4);
            // the size within the working area (leaving room for the title bar), never below the minimum
            Width = Math.Max(Math.Min(Stored.Width, Area.Width / Scale), MinWidth);
            Height = Math.Max(Math.Min(Stored.Height, (Area.Height - Room) / Scale), MinHeight);
            int WidthPx = (int)Math.Round(Width * Scale);
            int HeightPx = (int)Math.Round(Height * Scale);
            // the position: unchanged within the working area, otherwise moved the least into it
            int X = Math.Clamp(Stored.X, Area.X, Math.Max(Area.X, Area.Right - WidthPx));
            int Y = Stored.Y;
            int Lowest = Math.Max(Area.Y, Area.Bottom - HeightPx);
            if (Y < Area.Y)
            {
                Y = Math.Min(Area.Y + Room, Lowest);
            }
            else if (Y > Lowest)
            {
                Y = Lowest;
            }
            return new PlacementResult(new PixelPoint(X, Y), Width, Height, Stored.Maximized);
        }

        // The screen that holds the window's top-left corner; otherwise the one whose working area is nearest to it; the
        // primary screen on a tie. A corner in device pixels means the same on every screen, whatever its scaling (a size in
        // device-independent pixels would cover more pixels on a screen with a larger scaling).
        private static PlacementScreen Choose(RestartWindow Stored, List<PlacementScreen> Screens)
        {
            var Corner = new PixelPoint(Stored.X, Stored.Y);
            List<PlacementScreen> Ordered = Screens.OrderByDescending(S => S.IsPrimary).ToList();
            foreach (PlacementScreen Screen in Ordered)
            {
                if (Screen.Bounds.Contains(Corner))
                {
                    return Screen;
                }
            }
            return Ordered.OrderBy(S => Distance(Corner, S.WorkingArea)).First();
        }

        // how far a point is from an area (0 inside it)
        private static double Distance(PixelPoint Point, PixelRect Area)
        {
            double Dx = Math.Max(Math.Max(Area.X - Point.X, 0), Point.X - Area.Right);
            double Dy = Math.Max(Math.Max(Area.Y - Point.Y, 0), Point.Y - Area.Bottom);
            return Math.Sqrt(Dx * Dx + Dy * Dy);
        }
    }
}
