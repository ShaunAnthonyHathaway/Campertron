using System;
using System.Collections.Generic;
using System.Linq;

// One item of Campertron's current configuration (decision D60): a label ("Config") and its value as text. The console's
// View Config writes it as one line, "Label => value" (Menu.ViewConfigLines, the label colored); the GUI's Settings >
// System > Current configuration (V011-2) shows the label and the value side by side and copies Text.
public sealed class ConfigurationItem
{
    public ConfigurationItem(string Label, string Value)
    {
        this.Label = Label;
        this.Value = Value;
    }

    public string Label { get; }
    public string Value { get; }

    // the item as one line of text: the console's line without its color, and a line of the GUI's Copy
    public string Text => Label + ConfigurationSummary.Separator + Value;
}

// Campertron's current configuration (decision D60): the eight items of View Config, in their order (Version, Config,
// Cache, Output, Searches, RIDB refresh, OS, Framework), built by CurrentConfiguration for both the console and the GUI.
// Immutable. It holds no value of Email.yaml, no search file's name or content, no environment variable's value, no
// database content and of General.yaml only outputTo and the date of the last RIDB refresh.
public sealed class ConfigurationSummary
{
    // between a label and its value, in the console's line and in the copied text
    public const string Separator = " => ";

    public ConfigurationSummary(IEnumerable<ConfigurationItem> Items)
    {
        this.Items = Array.AsReadOnly(Items.ToArray());
    }

    public IReadOnlyList<ConfigurationItem> Items { get; }

    // every item's Text, in order, one per line (Environment.NewLine, as the console ends its lines), without a line
    // break after the last: what the GUI's Copy puts on the clipboard
    public string Text => string.Join(Environment.NewLine, Items.Select(Item => Item.Text));
}
