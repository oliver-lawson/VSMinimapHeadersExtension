using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text;
using System.Linq;

namespace ScrollbarHeadersExtension
{
    internal class ScrollbarOverlayMargin : Canvas, IWpfTextViewMargin
    {
        public const string MarginName = "ScrollbarOverlay";

        private readonly IWpfTextView _textView;
        private readonly IVerticalScrollBar _scrollBar;
        private readonly bool _isCSharp;
        private bool _isDisposed;

        private bool _showCommentHeaders;
        private bool _showFunctions;
        private bool _showClasses;
        private bool _showAccessSpecifiers;
        private bool _shortenAccessSpecifiers;

        private enum MarkerType { Header, Function, Class, AccessSpecifier }

        // C++ function regex - looks for optional return type, optional scope (Class::), name, params, and NO semicolon before brace
        private static readonly Regex FunctionDefRegex = new Regex(
            @"^\s*(?:[\w\s*&<>:,]+\s+)?(?:(\w+)::)?(\w+)\s*\([^)]*\)\s*(?:const\s*)?(?:override\s*)?(?:final\s*)?\s*(?:{|$)",
            RegexOptions.Compiled);

        // C# function regex - handles lambdas with => and regular methods
        private static readonly Regex CSharpFunctionRegex = new Regex(
            @"^\s*(?:public|private|protected|internal|static|virtual|override|async|sealed|abstract|extern|unsafe)*\s*(?:[\w<>[\],\s*&?]+\s+)?(\w+)\s*\([^)]*\)\s*(?:=>|{|$)",
            RegexOptions.Compiled);

        // C++ class regex - captures everything between class/struct and the inheritance/body
        private static readonly Regex ClassRegex = new Regex(
            @"^\s*(?:template\s*<[^>]*>\s*)?(?:class|struct)\s+(.+?)(?:\s*[:{\r\n]|$)",
            RegexOptions.Compiled);

        // C# class regex - handles inheritance with : syntax
        private static readonly Regex CSharpClassRegex = new Regex(
            @"^\s*(?:public|private|protected|internal|static|sealed|abstract|partial)*\s*(?:class|struct|interface|record)\s+(\w+)",
            RegexOptions.Compiled);

        public ScrollbarOverlayMargin(IWpfTextView textView, IVerticalScrollBar scrollBar, MinimapSettings initialSettings, bool isCSharp)
        {
            _textView = textView;
            _scrollBar = scrollBar;
            _isCSharp = isCSharp;

            // copy initial settings
            _showCommentHeaders = initialSettings.ShowCommentHeaders;
            _showFunctions = initialSettings.ShowFunctions;
            _showClasses = initialSettings.ShowClasses;
            _showAccessSpecifiers = initialSettings.ShowAccessSpecifiers;
            _shortenAccessSpecifiers = initialSettings.ShortenAccessSpecifiers;

            Background = Brushes.Transparent;
            Width = 100;
            IsHitTestVisible = false;

            _textView.LayoutChanged += OnLayoutChanged;
            _textView.TextBuffer.Changed += OnTextBufferChanged;
            _scrollBar.TrackSpanChanged += OnTrackSpanChanged;

            // subscribe to the static event
            MinimapSettings.SettingsChanged += OnStaticSettingsChanged;

            UpdateOverlay();
        }

        private void OnStaticSettingsChanged(object sender, SettingsChangedEventArgs e)
        {
            // update our local settings
            _showCommentHeaders = e.ShowCommentHeaders;
            _showFunctions = e.ShowFunctions;
            _showClasses = e.ShowClasses;
            _showAccessSpecifiers = e.ShowAccessSpecifiers;
            _shortenAccessSpecifiers = e.ShortenAccessSpecifiers;
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
                    string lineText = line.GetText();
                    string trimmedText = lineText.TrimStart();

                    if (_showCommentHeaders && IsSectionHeader(trimmedText))
                    {
                        string headerText = ExtractHeaderText(trimmedText);
                        DrawMarker(headerText, line.Start, MarkerType.Header);
                    }
                    else if (_showClasses && IsClassDefinition(trimmedText, out string className))
                    {
                        DrawMarker(className, line.Start, MarkerType.Class);
                    }
                    else if (_showFunctions && IsFunctionDefinition(trimmedText, out string functionName))
                    {
                        DrawMarker(functionName, line.Start, MarkerType.Function);
                    }
                    else if (_showAccessSpecifiers && !_isCSharp && IsAccessSpecifier(trimmedText, out string accessName))
                    {
                        DrawMarker(accessName, line.Start, MarkerType.AccessSpecifier);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MinimapHeaders: Error updating overlay: {ex.Message}");
            }
        }

        ////
        // === Code Parsing ===

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
            return TruncateName(text);
        }

        private bool IsFunctionDefinition(string lineText, out string functionName)
        {
            if (_isCSharp)
                return IsCSharpFunctionDefinition(lineText, out functionName);
            else
                return IsCppFunctionDefinition(lineText, out functionName);
        }

        private bool IsCSharpFunctionDefinition(string lineText, out string functionName)
        {
            functionName = null;

            // skip empty lines and comments
            if (string.IsNullOrWhiteSpace(lineText) || lineText.TrimStart().StartsWith("//"))
                return false;

            string trimmed = lineText.Trim();

            // skip attributes
            if (trimmed.StartsWith("["))
                return false;

            // skip property definitions (get/set)
            if (trimmed.Contains("{ get") || trimmed.Contains("{ set") || trimmed.Contains("=>") && trimmed.Contains(";"))
            {
                // but allow lambda methods that end with => Expression;
                // we need to distinguish between properties and methods
                if (!trimmed.Contains("("))
                    return false;
            }

            var match = CSharpFunctionRegex.Match(lineText);
            if (match.Success)
            {
                functionName = match.Groups[1].Value;

                // filter out common false positives
                if (IsLikelyNotFunction(functionName))
                    return false;

                functionName = TruncateName(functionName);
                return true;
            }

            return false;
        }

        private bool IsCppFunctionDefinition(string lineText, out string functionName)
        {
            functionName = null;

            // skip empty lines and comments
            if (string.IsNullOrWhiteSpace(lineText) || lineText.TrimStart().StartsWith("//"))
                return false;

            string trimmed = lineText.Trim();

            // skip lines with semicolons - these are function calls, not definitions
            //  this also stops .h declarations spamming the minibar, as these
            //  are usually compact and not needed
            // exception: allow semicolons after } (like "} catch {};") 
            int semiIndex = trimmed.IndexOf(';');
            if (semiIndex >= 0)
            {
                // check if semicolon is after a closing brace
                string beforeSemi = trimmed.Substring(0, semiIndex).Trim();
                if (!beforeSemi.EndsWith("}"))
                    return false; // it's a function call or forward declaration (probably)
            }

            // skip lines inside string literals (simple check for quotes before brackets)
            int firstQuote = trimmed.IndexOfAny(new[] { '"', '\'' });
            int firstParen = trimmed.IndexOf('(');
            if (firstQuote >= 0 && firstQuote < firstParen)
                return false; // we're probably inside a string

            // skip some obvious control structures
            if (trimmed.StartsWith("if") || trimmed.StartsWith("while") ||
                trimmed.StartsWith("for") || trimmed.StartsWith("switch") ||
                trimmed.StartsWith("return"))
                return false;

            // skip UE macros (UFUNCTION, UPROPERTY, UCLASS, GENERATED_BODY, etc.)
            if (Regex.IsMatch(trimmed, @"^\s*[A-Z_]+\s*\("))
                return false;

            var match = FunctionDefRegex.Match(lineText);
            if (match.Success)
            {
                // if there's a scope (Class::method), use the method name
                string scopeName = match.Groups[1].Value;
                string methodName = match.Groups[2].Value;

                if (!string.IsNullOrEmpty(scopeName))
                {
                    // found a scoped method definition like "SiteManager::createSite"
                    functionName = methodName;
                }
                else
                {
                    // no scope, just use the function name!
                    functionName = methodName;
                }

                // skip if there's no space before the opening bracket (likely a macro?)
                //  but allow constructors (where the function name matches the class name pattern)
                int namePos = lineText.LastIndexOf(methodName, StringComparison.Ordinal);
                if (namePos >= 0 && namePos + methodName.Length < lineText.Length)
                {
                    char nextChar = lineText[namePos + methodName.Length];
                    if (nextChar == '(' && IsLikelyMacro(methodName))
                        return false;
                }

                // filter out common false positives
                if (IsLikelyNotFunction(functionName))
                    return false;

                // and finally, truncate to fit our minimap
                //  I'm not bothering with a trailing ellipsis, it's obvious to me
                //  when names are cut off and we'd just waste a char
                //
                //  TODO: make this responsive to different VS minimap size options
                functionName = TruncateName(functionName);
                return true;
            }

            return false;
        }

        private bool IsLikelyMacro(string name)
        {
            if (string.IsNullOrEmpty(name))
                return true;

            // remove underscores to check letter composition - ie allowing functions that were Named_So
            string lettersOnly = name.Replace("_", "");

            // if all letters are uppercase, it's *likely* a macro (UFUNCTION, GENERATED_BODY, etc)
            if (!string.IsNullOrEmpty(lettersOnly) && lettersOnly.All(char.IsUpper))
                return true;

            return false;
        }

        private bool IsAccessSpecifier(string lineText, out string accessName)
        {
            accessName = null;

            if (string.IsNullOrWhiteSpace(lineText))
                return false;

            string trimmed = lineText.Trim();

            // C++ access specifiers end with a colon
            //  + some Qt/UE-specific ones I'm often dealing with
            if (trimmed.EndsWith(":"))
            {
                string specifier = trimmed.TrimEnd(':').Trim();

                // standard C++ access specifiers
                if (specifier == "public" || specifier == "private" || specifier == "protected")
                {
                    accessName = (_shortenAccessSpecifiers ? ShortenAccessSpecifier(specifier) : specifier); // + ":";
                    // trailing : isn't a bad idea I think, but it doesn't quite
                    //  read.  could add as an extra setting but getting quite
                    //  cluttered in the options panel maybe.
                    return true;
                }

                // Qt-specific access specifiers
                if (specifier == "signals" || specifier == "slots" ||
                    specifier == "Q_SIGNALS" || specifier == "Q_SLOTS")
                {
                    accessName = (_shortenAccessSpecifiers ? ShortenAccessSpecifier(specifier) : specifier); // + ":";
                    return true;
                }

                // Qt combined specifiers like "public slots", "private slots", etc
                if (specifier.StartsWith("public ") || specifier.StartsWith("private ") || specifier.StartsWith("protected "))
                {
                    accessName = (_shortenAccessSpecifiers ? ShortenAccessSpecifier(specifier) : specifier); // + ":";
                    return true;
                }
            }

            return false;
        }

        private string ShortenAccessSpecifier(string specifier)
        {
            // shorten common access specifiers
            switch (specifier)
            {
                case "public": return "pub";
                case "private": return "priv";
                case "protected": return "prot";
                case "signals": return "sig";
                case "slots": return "slot";
                case "Q_SIGNALS": return "Q_SIG";
                case "Q_SLOTS": return "Q_SLOT";

                // handle combined forms like "public slots"
                case string s when s.StartsWith("public "):
                    return "pub " + ShortenAccessSpecifier(s.Substring(7));
                case string s when s.StartsWith("private "):
                    return "priv " + ShortenAccessSpecifier(s.Substring(8));
                case string s when s.StartsWith("protected "):
                    return "prot " + ShortenAccessSpecifier(s.Substring(10));

                default: return specifier;
            }
        }

        private bool IsClassDefinition(string lineText, out string className)
        {
            if (_isCSharp)
                return IsCSharpClassDefinition(lineText, out className);
            else
                return IsCppClassDefinition(lineText, out className);
        }

        private bool IsCSharpClassDefinition(string lineText, out string className)
        {
            className = null;

            if (string.IsNullOrWhiteSpace(lineText) || lineText.TrimStart().StartsWith("//"))
                return false;

            var match = CSharpClassRegex.Match(lineText);
            if (match.Success)
            {
                className = match.Groups[1].Value;

                if (!string.IsNullOrEmpty(className))
                {
                    className = TruncateName(className);
                    return true;
                }
            }

            return false;
        }

        private bool IsCppClassDefinition(string lineText, out string className)
        {
            // much easier than function parsing.
            //  just need to check for API and final/abstract
            className = null;

            if (string.IsNullOrWhiteSpace(lineText) || lineText.TrimStart().StartsWith("//"))
                return false;

            var match = ClassRegex.Match(lineText);
            if (match.Success)
            {
                // get the captured text between 'class/struct' and ':/{'
                string captured = match.Groups[1].Value.Trim();

                // split by whitespace and filter out API macros/other noise
                var parts = captured.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                // find the actual class name (skip API macros, final, etc.)
                //  this is ugly but seems to behave surprisingly consistently,
                //  at least with the UE codebases I checked
                // TODO: verify this more thoroughly, look into more weird keywords in
                //  modern C++ I may have missed
                className = parts.LastOrDefault(part =>
                    !part.EndsWith("_API") &&
                    !part.EndsWith("_EXPORT") &&
                    !part.Equals("final", StringComparison.OrdinalIgnoreCase) &&
                    !part.Equals("abstract", StringComparison.OrdinalIgnoreCase) &&
                    Regex.IsMatch(part, @"^[A-Z_]\w*$")); // starts with uppercase?

                if (!string.IsNullOrEmpty(className))
                {
                    className = TruncateName(className);
                    return true;
                }
            }

            return false;
        }

        ////
        // === Helper Funcs ===
        private string TruncateName(string name)
        {
            const int maxLength = 20;
            if (name.Length > maxLength)
                return name.Substring(0, maxLength);
            return name;
        }

        private bool IsLikelyNotFunction(string name)
        {
            // skip common keywords
            string[] notFunctions = { "if", "while", "for", "switch", "return", "throw", "catch", "sizeof", "typeof", "delete", "new" };
            return notFunctions.Contains(name, StringComparer.OrdinalIgnoreCase);
        }

        ////
        // === Draw Marker ===
        private void DrawMarker(string text, SnapshotPoint position, MarkerType type)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;
            Brush foreground = Brushes.White;
            int fontsize = 10;

            // this defaul offset works OK.  on my system and standard DPI, this places
            //  the cursor line either exactly in the middle, or underneath the appropriate
            //  text line, I think depending on how squished the VS minimap is.
            int heightOffset = 9;


            switch (type)
            {
                case MarkerType.Header:
                    foreground = Brushes.White;
                    fontsize = 13;
                    heightOffset = -1; // draw it with header text bottom point on the line.
                                       //  this way, headers just above functions (typical)
                                       //  don't overlap!
                    break;
                case MarkerType.Function:
                    // these colours match my default VS colours
                    // TODO: grab appropriate colours from themes if possible
                    foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xAA));
                    break;
                case MarkerType.Class:
                    foreground = new SolidColorBrush(Color.FromRgb(0xBE, 0xB7, 0xFF));
                    break;
                case MarkerType.AccessSpecifier:
                    foreground = new SolidColorBrush(Color.FromRgb(0x50, 0x9C, 0xD6));
                    break;
                default:
                    break;
            }

            double scrollMapPosition = _scrollBar.Map.GetCoordinateAtBufferPosition(position);
            double yCoordinate = _scrollBar.GetYCoordinateOfScrollMapPosition(scrollMapPosition);
            yCoordinate -= _scrollBar.TrackSpanTop;

            var textBlock = new TextBlock
            {
                Text = text,
                Foreground = foreground,
                // transparent bg.  feels enough to make text stand out from the
                //  "text" behind it, but not enough that it fully obscures the
                //  gutter columns showing git changes/errors/finds/breakpoints.
                // maybe a better option is to draw the text one char to the right,
                //  and leave at least the git part intact, but in practice the full
                //  span text feels to be better to me.
                Background = new SolidColorBrush(Color.FromArgb(180, 50, 50, 50)),
                FontSize = fontsize,
                //  this should ship with VS, TODO: verify and do fallbacks - Consolas?
                //  TODO: options page this too
                FontFamily = new FontFamily("Cascadia Code"),
                FontWeight = FontWeights.Bold,
                MaxWidth = 101
            };

            Canvas.SetLeft(textBlock, 0);
            Canvas.SetTop(textBlock, yCoordinate + heightOffset);

            Children.Add(textBlock);
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
            MinimapSettings.SettingsChanged -= OnStaticSettingsChanged;
            _isDisposed = true;
        }
    }
}