using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text;

namespace ScrollbarHeadersExtension
{
    internal class ScrollbarOverlayMargin : Canvas, IWpfTextViewMargin
    {
        public const string MarginName = "ScrollbarOverlay";

        private readonly IWpfTextView _textView;
        private readonly IVerticalScrollBar _scrollBar;
        private bool _isDisposed;

        public ScrollbarOverlayMargin(IWpfTextView textView, IVerticalScrollBar scrollBar)
        {
            _textView = textView;
            _scrollBar = scrollBar;

            Background = Brushes.Transparent;
            Width = 100;
            IsHitTestVisible = false;

            _textView.LayoutChanged += OnLayoutChanged;
            _textView.TextBuffer.Changed += OnTextBufferChanged;
            _scrollBar.TrackSpanChanged += OnTrackSpanChanged;

            UpdateOverlay();
        }

        private void OnTextBufferChanged(object sender, TextContentChangedEventArgs e) => UpdateOverlay();
        private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e) => UpdateOverlay();
        private void OnTrackSpanChanged(object sender, EventArgs e) => UpdateOverlay();

        private void UpdateOverlay()
        {
            if (_isDisposed) return;

            Children.Clear();

            try
            {
                var snapshot = _textView.TextSnapshot;

                foreach (var line in snapshot.Lines)
                {
                    string lineText = line.GetText().TrimStart();

                    if (IsSectionHeader(lineText))
                    {
                        string headerText = ExtractHeaderText(lineText);
                        var bufferPosition = line.Start;
                        double scrollMapPosition = _scrollBar.Map.GetCoordinateAtBufferPosition(bufferPosition);
                        double yCoordinate = _scrollBar.GetYCoordinateOfScrollMapPosition(scrollMapPosition);
                        yCoordinate -= _scrollBar.TrackSpanTop;

                        DrawHeaderLabel(headerText, yCoordinate);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating overlay: {ex.Message}");
            }
        }

        private bool IsSectionHeader(string lineText)
        {
            if (!lineText.StartsWith("//") && !lineText.StartsWith("/*"))
                return false;

            string comment = lineText.Substring(2).Trim();

            return comment.StartsWith("==") ||
                   comment.StartsWith("--") ||
                   comment.StartsWith("##") ||
                   comment.StartsWith("**");
        }

        private string ExtractHeaderText(string lineText)
        {
            string text = lineText.TrimStart('/', '*', ' ', '\t');
            text = text.Trim('=', '-', '#', '*', ' ', '\t');

            if (text.Length > 15)
                text = text.Substring(0, 12) + "...";

            return text;
        }

        private void DrawHeaderLabel(string text, double yPos)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            var textBlock = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(220, 50, 50, 50)),
                FontSize = 9,
                FontFamily = new FontFamily("Consolas"),
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(3, 1, 3, 1),
                MaxWidth = 95
            };

            var border = new Border
            {
                Child = textBlock,
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2)
            };

            Canvas.SetLeft(border, 0);
            Canvas.SetTop(border, yPos - 8);

            Children.Add(border);
        }

        public FrameworkElement VisualElement => this;
        public bool Enabled => true;
        public double MarginSize => ActualWidth;

        public ITextViewMargin GetTextViewMargin(string marginName)
        {
            return string.Equals(marginName, MarginName, StringComparison.OrdinalIgnoreCase) ? this : null;
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            _textView.LayoutChanged -= OnLayoutChanged;
            _textView.TextBuffer.Changed -= OnTextBufferChanged;
            _scrollBar.TrackSpanChanged -= OnTrackSpanChanged;
            _isDisposed = true;
        }
    }
}