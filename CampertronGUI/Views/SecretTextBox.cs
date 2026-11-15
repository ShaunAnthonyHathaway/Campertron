using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using System;

namespace Campertron.Views
{
    // The SMTP password box of the Email settings (CM5, decision D43): a TextBox with a PasswordChar, whose accessibility
    // value is always the mask. Avalonia's own TextBoxAutomationPeer reports the real text once RevealPassword is on;
    // this one never does, so revealing the password for a moment shows it on screen only, never to screen readers or
    // other accessibility clients. Copying is disabled by the PasswordChar (TextBox.CanCopy), also while revealed.
    public class SecretTextBox : TextBox
    {
        protected override Type StyleKeyOverride => typeof(TextBox);

        protected override AutomationPeer OnCreateAutomationPeer() => new SecretTextBoxAutomationPeer(this);
    }

    public sealed class SecretTextBoxAutomationPeer : ControlAutomationPeer, IValueProvider
    {
        public SecretTextBoxAutomationPeer(SecretTextBox Owner) : base(Owner)
        {
        }

        private new SecretTextBox Owner => (SecretTextBox)base.Owner;

        // one mask character per character, as a masked TextBox reports itself, whether or not it is revealed
        public string? Value => new string(Owner.PasswordChar == default ? '\u2022' : Owner.PasswordChar, Owner.Text?.Length ?? 0);

        public bool IsReadOnly => Owner.IsReadOnly;

        public void SetValue(string? NewValue)
        {
            Owner.Text = NewValue;
        }

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Edit;
    }
}
