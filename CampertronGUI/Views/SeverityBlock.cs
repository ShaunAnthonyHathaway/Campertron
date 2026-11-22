using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Collections.Generic;

namespace Campertron.Views
{
    // One validation or status message, in the one shared style of its severity (decision D74, business-rules-gui §21): a
    // tinted block with a colored left edge and a severity icon beside the message's own TextBlock (its child in XAML),
    // which keeps its name, its bindings and its words. Error (red): Save is blocked or Campertron refuses to start; Warning
    // (amber): it works but needs attention; Info (blue): neutral information. The severity is Severity when set, else the
    // label the text starts with ("Error: ", "Warning: ", "Info: "), else Error when IsError, else Info. Color is never the
    // only sign: the icon's shape differs by severity and the text keeps its label. For screen readers the block is the
    // message (a text element whose name is the text, with its severity before it when the text has no label of its own),
    // the TextBlock is hidden from them so nothing is read twice, and the block takes the TextBlock's live setting (Avalonia's
    // TextBlock peer reads only its text, and a Grid is no element). The block is shown while its TextBlock is visible and has text; it
    // is a Grid (no template), so the TextBlock is in the visual tree also while hidden. The colors are
    // Campertron's theme resources for Light and Dark (AppStyles.axaml)
    public class SeverityBlock : Grid
    {
        public static readonly StyledProperty<string?> SeverityProperty = AvaloniaProperty.Register<SeverityBlock, string?>(nameof(Severity));
        public static readonly StyledProperty<bool> IsErrorProperty = AvaloniaProperty.Register<SeverityBlock, bool>(nameof(IsError));
        public static readonly StyledProperty<Geometry?> IconDataProperty = AvaloniaProperty.Register<SeverityBlock, Geometry?>(nameof(IconData));

        public const string Error = "Error";
        public const string Warning = "Warning";
        public const string Info = "Info";

        private TextBlock? _Text;
        private readonly Border _Frame;
        private readonly PathIcon _Icon;

        public SeverityBlock()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*");
            // behind everything: the tint and the colored left edge (styled by severity, AppStyles.axaml)
            _Frame = new Border { Name = "SeverityFrame", BorderThickness = new Thickness(4, 0, 0, 0), CornerRadius = new CornerRadius(4) };
            _Frame.Classes.Add("severityFrame");
            Grid.SetColumnSpan(_Frame, 2);
            _Icon = new PathIcon { Name = "SeverityIcon", Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(14, 9, 8, 6) };
            _Icon.SetValue(AutomationProperties.AccessibilityViewProperty, AccessibilityView.Raw);
            Children.Add(_Frame);
            Children.Add(_Icon);
            Children.CollectionChanged += (_, _) => FindText();
            IsVisible = false;
            Update();
        }

        // "Error", "Warning" or "Info"; empty: from the text's label, else IsError
        public string? Severity
        {
            get => GetValue(SeverityProperty);
            set => SetValue(SeverityProperty, value);
        }
        // a message without a label: an error when true, information when false
        public bool IsError
        {
            get => GetValue(IsErrorProperty);
            set => SetValue(IsErrorProperty, value);
        }
        // the severity's icon, set by the style of its class
        public Geometry? IconData
        {
            get => GetValue(IconDataProperty);
            set => SetValue(IconDataProperty, value);
        }

        // the severity the block shows, what screen readers read, its message, and its tint and edge
        public string EffectiveSeverity { get; private set; } = Info;
        public string AccessibleText { get; private set; } = string.Empty;
        public TextBlock? Message => _Text;
        public Border Frame => _Frame;
        public PathIcon Icon => _Icon;

        // the label a text starts with, or null
        internal static string? LabelOf(string? Text)
        {
            foreach (string Label in new[] { Error, Warning, Info })
            {
                if (Text != null && Text.StartsWith(Label + ": ", StringComparison.Ordinal))
                {
                    return Label;
                }
            }
            return null;
        }

        // the message's TextBlock (the child XAML adds) goes beside the icon
        private void FindText()
        {
            TextBlock? Found = null;
            foreach (Control Child in Children)
            {
                if (Child is TextBlock Text)
                {
                    Found = Text;
                }
            }
            if (ReferenceEquals(Found, _Text))
            {
                return;
            }
            if (_Text != null)
            {
                _Text.PropertyChanged -= Text_PropertyChanged;
            }
            _Text = Found;
            if (_Text != null)
            {
                Grid.SetColumn(_Text, 1);
                _Text.Margin = new Thickness(0, 6, 10, 6);
                _Text.PropertyChanged += Text_PropertyChanged;
            }
            Update();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs Change)
        {
            base.OnPropertyChanged(Change);
            if (Change.Property == SeverityProperty || Change.Property == IsErrorProperty)
            {
                Update();
            }
            else if (Change.Property == IconDataProperty)
            {
                _Icon.Data = IconData;
            }
        }

        private void Text_PropertyChanged(object? Sender, AvaloniaPropertyChangedEventArgs Change)
        {
            if (Change.Property == TextBlock.TextProperty || Change.Property == IsVisibleProperty || Change.Property == AutomationProperties.LiveSettingProperty)
            {
                Update();
            }
        }

        private void Update()
        {
            string? Text = _Text?.Text;
            string? Label = LabelOf(Text);
            string Effective = !string.IsNullOrEmpty(Severity) ? Severity! : Label ?? (IsError ? Error : Info);
            EffectiveSeverity = Effective;
            Classes.Set("sevError", Effective == Error);
            Classes.Set("sevWarning", Effective == Warning);
            Classes.Set("sevInfo", Effective == Info);
            IsVisible = _Text != null && _Text.IsVisible && !string.IsNullOrEmpty(Text);
            // a text without its label: screen readers get the severity first; with it, the text as it is
            string Previous = AccessibleText;
            AccessibleText = string.IsNullOrEmpty(Text) ? string.Empty : Label == null ? Effective + ": " + Text : Text;
            if (_Text != null)
            {
                _Text.SetValue(AutomationProperties.AccessibilityViewProperty, AccessibilityView.Raw);
                SetValue(AutomationProperties.LiveSettingProperty, AutomationProperties.GetLiveSetting(_Text));
            }
            if (Previous != AccessibleText)
            {
                ControlAutomationPeer.FromElement(this)?.RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, Previous, AccessibleText);
            }
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new SeverityBlockAutomationPeer(this);

        // the message as one text element: its name is AccessibleText; its TextBlock and icon are not read again
        private sealed class SeverityBlockAutomationPeer : ControlAutomationPeer
        {
            internal SeverityBlockAutomationPeer(SeverityBlock Owner) : base(Owner)
            {
            }

            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
            protected override string? GetNameCore() => ((SeverityBlock)Owner).AccessibleText;
            protected override bool IsContentElementCore() => true;
            protected override bool IsControlElementCore() => true;
            protected override IReadOnlyList<AutomationPeer>? GetChildrenCore() => null;
        }
    }
}
