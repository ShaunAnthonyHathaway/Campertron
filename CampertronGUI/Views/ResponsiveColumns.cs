using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using System;
using System.Linq;

namespace Campertron.Views
{
    // The desktop GUI's responsive layout (decision D106, found in the maintainer's personal test of v0.3.0: maximized, the
    // content stayed in a narrow column at the left). A page's content is one or two parts: a side part (the first child)
    // and a main part (the second). Below WideMinWidth they are stacked in one column of at most NarrowMaxWidth, centered;
    // from WideMinWidth up (a deterministic boundary: exactly WideMinWidth is wide) the side part takes SideWidth on the
    // left and the main part the rest, up to MainMaxWidth, the two centered together. With one child, or while the main part
    // is hidden (the Results tab before its first run), the first child takes the column's width (narrow) or both columns'
    // width (wide), so a card above a two-column area lines up with it and no column stands empty (up to LoneMaxWidth, at
    // the columns' left).
    // How far a page grows is its own (decision D107, the maintainer's retest of D106: one cap for every page left large
    // margins around Results and cut its campsite details short): the width categories are style classes in
    // AppStyles.axaml (readable, data, mixed), and a page sets its own. MainMaxWidth and NarrowMaxWidth may be infinite (a
    // data page's main column takes the rest of the width). With EvenColumns (the class pair, inside a mixed page's main
    // column) the two parts share the width equally from WideMinWidth up, each at most MainMaxWidth, each at its own height.
    // The parts keep their order (side first: reading order and Tab order are the same in both layouts), their instances
    // and their state: only their place and size change. The side part (and a lone first child) is as high as it wants;
    // the main part gets at least the rest of the height, so a bounded area (a scroll viewer, a star row) fills the page. Nothing here reads the
    // window or a view model; the panel's own width decides, so every page switches at the same tab width.
    public sealed class ResponsiveColumns : Panel
    {
        public static readonly StyledProperty<double> WideMinWidthProperty =
            AvaloniaProperty.Register<ResponsiveColumns, double>(nameof(WideMinWidth), 1136);

        public static readonly StyledProperty<double> SideWidthProperty =
            AvaloniaProperty.Register<ResponsiveColumns, double>(nameof(SideWidth), 400);

        public static readonly StyledProperty<double> MainMaxWidthProperty =
            AvaloniaProperty.Register<ResponsiveColumns, double>(nameof(MainMaxWidth), 960);

        public static readonly StyledProperty<double> NarrowMaxWidthProperty =
            AvaloniaProperty.Register<ResponsiveColumns, double>(nameof(NarrowMaxWidth), 960);

        public static readonly StyledProperty<double> ColumnSpacingProperty =
            AvaloniaProperty.Register<ResponsiveColumns, double>(nameof(ColumnSpacing), 16);

        public static readonly StyledProperty<double> RowSpacingProperty =
            AvaloniaProperty.Register<ResponsiveColumns, double>(nameof(RowSpacing), 12);

        public static readonly StyledProperty<bool> EvenColumnsProperty =
            AvaloniaProperty.Register<ResponsiveColumns, bool>(nameof(EvenColumns));

        public static readonly StyledProperty<double> LoneMaxWidthProperty =
            AvaloniaProperty.Register<ResponsiveColumns, double>(nameof(LoneMaxWidth), double.PositiveInfinity);

        public static readonly DirectProperty<ResponsiveColumns, bool> IsWideProperty =
            AvaloniaProperty.RegisterDirect<ResponsiveColumns, bool>(nameof(IsWide), o => o.IsWide);

        static ResponsiveColumns()
        {
            AffectsMeasure<ResponsiveColumns>(WideMinWidthProperty, SideWidthProperty, MainMaxWidthProperty, NarrowMaxWidthProperty,
                ColumnSpacingProperty, RowSpacingProperty, EvenColumnsProperty, LoneMaxWidthProperty);
        }

        // the least width of the two-column layout (from it up: wide)
        public double WideMinWidth
        {
            get => GetValue(WideMinWidthProperty);
            set => SetValue(WideMinWidthProperty, value);
        }

        // the side part's width in the two-column layout
        public double SideWidth
        {
            get => GetValue(SideWidthProperty);
            set => SetValue(SideWidthProperty, value);
        }

        // the main part's greatest width in the two-column layout (a readable line length; infinite on a data page)
        public double MainMaxWidth
        {
            get => GetValue(MainMaxWidthProperty);
            set => SetValue(MainMaxWidthProperty, value);
        }

        // the one column's greatest width below WideMinWidth (a readable line length; infinite on a data page)
        public double NarrowMaxWidth
        {
            get => GetValue(NarrowMaxWidthProperty);
            set => SetValue(NarrowMaxWidthProperty, value);
        }

        public double ColumnSpacing
        {
            get => GetValue(ColumnSpacingProperty);
            set => SetValue(ColumnSpacingProperty, value);
        }

        public double RowSpacing
        {
            get => GetValue(RowSpacingProperty);
            set => SetValue(RowSpacingProperty, value);
        }

        // whether the two parts share the width equally in the two-column layout (SideWidth unused), each at most MainMaxWidth
        public bool EvenColumns
        {
            get => GetValue(EvenColumnsProperty);
            set => SetValue(EvenColumnsProperty, value);
        }

        // a lone part's greatest width in the two-column layout, at the columns' left (a mixed page's filter card above its
        // wider details); infinite: the columns' width
        public double LoneMaxWidth
        {
            get => GetValue(LoneMaxWidthProperty);
            set => SetValue(LoneMaxWidthProperty, value);
        }

        private bool _IsWide;

        // whether the last measure chose the two-column layout (also the :wide pseudo-class)
        public bool IsWide
        {
            get => _IsWide;
            private set
            {
                if (SetAndRaise(IsWideProperty, ref _IsWide, value))
                {
                    PseudoClasses.Set(":wide", value);
                }
            }
        }

        private Control? Side => Children.Count > 0 ? Children[0] : null;

        // the main part, while it is shown (a hidden one leaves its width to the side part)
        private Control? Main => Children.Count > 1 && Children[1].IsVisible ? Children[1] : null;

        // the layout for a width: wide or not, the side width, the main width (or the one column's width) and the group's width
        internal (bool Wide, double SideWidth, double MainWidth, double GroupWidth) Columns(double Width)
        {
            return Columns(Width, !double.IsNaN(Width) && !double.IsInfinity(Width) && Width >= WideMinWidth);
        }

        private (bool Wide, double SideWidth, double MainWidth, double GroupWidth) Columns(double Width, bool Wide)
        {
            if (!Wide)
            {
                // infinite only while both the width and the greatest width are (a measure without a width bound)
                double Column = double.IsNaN(Width) ? NarrowMaxWidth : Math.Min(Width, NarrowMaxWidth);
                return (false, Column, Column, Column);
            }
            if (EvenColumns)
            {
                double Part = Math.Max(0, Math.Min((Width - ColumnSpacing) / 2, MainMaxWidth));
                return (true, Part, Part, Part + ColumnSpacing + Part);
            }
            double SidePart = Math.Min(SideWidth, Width);
            double MainPart = Math.Max(0, Math.Min(Width - SidePart - ColumnSpacing, MainMaxWidth));
            return (true, SidePart, MainPart, SidePart + ColumnSpacing + MainPart);
        }

        private static bool Shown(Control? Child) => Child != null && Child.IsVisible;

        protected override Size MeasureOverride(Size AvailableSize)
        {
            var (Wide, SidePart, MainPart, Group) = Columns(AvailableSize.Width);
            IsWide = Wide;
            Control? SideChild = Side;
            Control? MainChild = Main;
            if (MainChild == null)
            {
                // one child: the column (narrow) or both columns (wide), up to LoneMaxWidth
                SideChild?.Measure(new Size(Wide ? Math.Min(Group, LoneMaxWidth) : Group, AvailableSize.Height));
                return new Size(Finite(Group, SideChild), SideChild?.DesiredSize.Height ?? 0);
            }
            if (Wide)
            {
                SideChild!.Measure(new Size(SidePart, AvailableSize.Height));
                MainChild.Measure(new Size(MainPart, AvailableSize.Height));
                return new Size(Group, Math.Max(SideChild.DesiredSize.Height, MainChild.DesiredSize.Height));
            }
            SideChild!.Measure(new Size(SidePart, double.PositiveInfinity));
            double SideHeight = SideChild.DesiredSize.Height;
            double Gap = Shown(SideChild) ? RowSpacing : 0;
            double Rest = double.IsInfinity(AvailableSize.Height) ? double.PositiveInfinity : Math.Max(0, AvailableSize.Height - SideHeight - Gap);
            MainChild.Measure(new Size(MainPart, Rest));
            return new Size(Finite(Group, SideChild, MainChild), SideHeight + Gap + MainChild.DesiredSize.Height);
        }

        // the desired width: the column's, or the widest part's when the column is unbounded (a measure without a width bound)
        private static double Finite(double Group, params Control?[] Parts) =>
            !double.IsInfinity(Group) ? Group : Parts.Select(p => p?.DesiredSize.Width ?? 0).Max();

        protected override Size ArrangeOverride(Size FinalSize)
        {
            // the mode of the last measure (a layout pass measures before it arranges), with the final width's sizes
            var (_, SidePart, MainPart, Group) = Columns(FinalSize.Width, IsWide);
            double Left = Math.Max(0, (FinalSize.Width - Group) / 2);
            Control? SideChild = Side;
            Control? MainChild = Main;
            if (MainChild == null)
            {
                SideChild?.Arrange(new Rect(Left, 0, IsWide ? Math.Min(Group, LoneMaxWidth) : Group, SideChild.DesiredSize.Height));
                return FinalSize;
            }
            if (IsWide)
            {
                // a page's main part fills the page's height; two even parts (cards in an editor) keep their own heights
                SideChild!.Arrange(new Rect(Left, 0, SidePart, SideChild.DesiredSize.Height));
                MainChild.Arrange(new Rect(Left + SidePart + ColumnSpacing, 0, MainPart,
                    EvenColumns ? MainChild.DesiredSize.Height : Math.Max(MainChild.DesiredSize.Height, FinalSize.Height)));
                return FinalSize;
            }
            double SideHeight = SideChild!.DesiredSize.Height;
            double Gap = Shown(SideChild) ? RowSpacing : 0;
            SideChild.Arrange(new Rect(Left, 0, SidePart, SideHeight));
            MainChild.Arrange(new Rect(Left, SideHeight + Gap, MainPart, Math.Max(MainChild.DesiredSize.Height, FinalSize.Height - SideHeight - Gap)));
            return FinalSize;
        }
    }
}
