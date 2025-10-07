using System.ComponentModel.Composition;
using System.Windows;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace ScrollbarHeadersExtension
{
    [Export(typeof(IWpfTextViewMarginProvider))]
    [Name(ScrollbarOverlayMargin.MarginName)]
    [Order(After = PredefinedMarginNames.VerticalScrollBar)]
    [MarginContainer(PredefinedMarginNames.VerticalScrollBarContainer)]
    [ContentType("code")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class ScrollbarOverlayMarginProvider : IWpfTextViewMarginProvider
    {
        public IWpfTextViewMargin CreateMargin(IWpfTextViewHost wpfTextViewHost, IWpfTextViewMargin marginContainer)
        {
            var scrollBarMargin = marginContainer?.GetTextViewMargin(PredefinedMarginNames.VerticalScrollBar);

            if (scrollBarMargin is IVerticalScrollBar scrollBar)
            {
                var margin = new ScrollbarOverlayMargin(wpfTextViewHost.TextView, scrollBar, MinimapSettings.Instance);
                margin.Margin = new Thickness(-100, 0, 0, 0); // negative margin to draw on top of minimap
                return margin;
            }

            return null;
        }
    }
}