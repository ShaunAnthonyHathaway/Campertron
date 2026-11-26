using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using System;
using System.Collections.Generic;

namespace Campertron.Views
{
    // A status line that screen readers hear only when it says something new (v0.4.0's phase 4: the Monitoring tab's state,
    // which changes at every check). Its text is always the current one, for screen readers too (its name), and with
    // AutomationProperties.LiveSetting it is a live region, but it tells them that its name changed only when Announcement
    // changes (the monitoring's state of a moment: starting, running, stopping, paused or ended), as SeverityBlock tells them
    // (ER-35); a new text of the same state (the next check, the next check's time) is shown and read when asked for, never
    // announced. It looks and is styled as a TextBlock, and it can take the focus a view gives it (never a Tab stop).
    public sealed class AnnouncedText : TextBlock
    {
        public static readonly StyledProperty<string?> AnnouncementProperty =
            AvaloniaProperty.Register<AnnouncedText, string?>(nameof(Announcement));

        protected override Type StyleKeyOverride => typeof(TextBlock);

        // what the text says of a moment: a change of it is announced with the text as it is then
        public string? Announcement
        {
            get => GetValue(AnnouncementProperty);
            set => SetValue(AnnouncementProperty, value);
        }

        // how often it told screen readers that its name changed (tests)
        internal int Announced { get; private set; }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs Change)
        {
            base.OnPropertyChanged(Change);
            if (Change.Property == AnnouncementProperty && !Equals(Change.OldValue, Change.NewValue))
            {
                Announced++;
                ControlAutomationPeer.FromElement(this)?.RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, null, Text);
            }
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new AnnouncedTextAutomationPeer(this);

        // a text element named by its text; it raises its name's change only when the owner announces
        private sealed class AnnouncedTextAutomationPeer : ControlAutomationPeer
        {
            internal AnnouncedTextAutomationPeer(AnnouncedText Owner) : base(Owner)
            {
            }

            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
            protected override string? GetNameCore() => ((AnnouncedText)Owner).Text;
            protected override bool IsContentElementCore() => true;
            protected override bool IsControlElementCore() => true;
            protected override IReadOnlyList<AutomationPeer>? GetChildrenCore() => null;
        }
    }
}
