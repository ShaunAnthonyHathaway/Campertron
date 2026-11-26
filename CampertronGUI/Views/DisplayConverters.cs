using Avalonia;
using Avalonia.Data.Converters;

namespace Campertron.Views
{
    // Display-only conversions. They change what is shown, never the selected value: the City ComboBox still
    // selects (and queries with) the stored value, including an empty string.
    public static class DisplayConverters
    {
        // the shared form layer's words (D99), which the web UI's campgrounds pages show too
        public const string NoCityListed = CampertronLibrary.function.Forms.CampgroundsWording.NoCityListed;

        public static readonly FuncValueConverter<string?, string> City =
            new FuncValueConverter<string?, string>(Value => CampertronLibrary.function.Forms.CampgroundsWording.City(Value));

        // a search of the monitoring's last check as its list names it (the shared words, v0.4.0's phase 4)
        public static readonly FuncValueConverter<CampertronLibrary.function.Forms.ResultSearchViewModel?, string> MonitoringSearch =
            new FuncValueConverter<CampertronLibrary.function.Forms.ResultSearchViewModel?, string>(Search =>
                Search == null ? string.Empty : CampertronLibrary.function.Forms.MonitoringWording.SearchLabel(Search));

        // a filter selector's row at its level (FilterOptionRow.Level): 28 pixels further right for each level
        public static readonly FuncValueConverter<int, Thickness> Indent =
            new FuncValueConverter<int, Thickness>(Level => new Thickness(Level * 28, 0, 0, 0));
    }
}
