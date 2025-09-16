using System;
using CouchbaseLiteQueryTester.Utilities;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace CouchbaseLiteQueryTester.Controls
{
    public class SyntaxHighlightingEditor : Grid
    {
        private readonly Editor _editor;
        private readonly Label _overlayLabel;
        private bool _suppressTextCallback;
        private readonly Application? _application;

        public SyntaxHighlightingEditor()
        {
            _overlayLabel = new Label
            {
                LineBreakMode = LineBreakMode.CharacterWrap,
                HorizontalTextAlignment = TextAlignment.Start,
                VerticalTextAlignment = TextAlignment.Start,
                TextColor = Colors.Black,
                BackgroundColor = Colors.Transparent,
                InputTransparent = true
            };

            _editor = new Editor
            {
                BackgroundColor = Colors.Transparent,
                TextColor = Colors.Transparent,
                PlaceholderColor = Color.FromArgb("#7F7F7F"),
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                AutoSize = EditorAutoSizeOption.TextChanges
            };

            _editor.TextChanged += HandleEditorTextChanged;

            Children.Add(_overlayLabel);
            Children.Add(_editor);

            SetDefaultFontValues();
            ApplyHighlighting(string.Empty);

            _application = Application.Current;
            if (_application is not null)
            {
                _application.RequestedThemeChanged += HandleRequestedThemeChanged;
            }
        }

        public event EventHandler<TextChangedEventArgs>? TextChanged;

        public Editor InnerEditor => _editor;

        public static readonly BindableProperty TextProperty = BindableProperty.Create(
            nameof(Text),
            typeof(string),
            typeof(SyntaxHighlightingEditor),
            string.Empty,
            BindingMode.TwoWay,
            propertyChanged: OnTextPropertyChanged);

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        private static void OnTextPropertyChanged(BindableObject bindable, object oldValue, object newValue)
        {
            var control = (SyntaxHighlightingEditor)bindable;
            var text = newValue as string ?? string.Empty;

            if (control._editor.Text != text)
            {
                control._suppressTextCallback = true;
                control._editor.Text = text;
                control._suppressTextCallback = false;
            }

            control.ApplyHighlighting(text);
        }

        public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(
            nameof(Placeholder),
            typeof(string),
            typeof(SyntaxHighlightingEditor),
            string.Empty,
            propertyChanged: OnPlaceholderChanged);

        public string Placeholder
        {
            get => (string)GetValue(PlaceholderProperty);
            set => SetValue(PlaceholderProperty, value);
        }

        private static void OnPlaceholderChanged(BindableObject bindable, object oldValue, object newValue)
        {
            var control = (SyntaxHighlightingEditor)bindable;
            control._editor.Placeholder = newValue as string;
        }

        public static readonly BindableProperty FontSizeProperty = BindableProperty.Create(
            nameof(FontSize),
            typeof(double),
            typeof(SyntaxHighlightingEditor),
            Device.GetNamedSize(NamedSize.Medium, typeof(Editor)),
            propertyChanged: OnFontSizeChanged);

        public double FontSize
        {
            get => (double)GetValue(FontSizeProperty);
            set => SetValue(FontSizeProperty, value);
        }

        private static void OnFontSizeChanged(BindableObject bindable, object oldValue, object newValue)
        {
            var control = (SyntaxHighlightingEditor)bindable;
            var size = (double)newValue;
            control._editor.FontSize = size;
            control._overlayLabel.FontSize = size;
        }

        public static readonly BindableProperty FontFamilyProperty = BindableProperty.Create(
            nameof(FontFamily),
            typeof(string),
            typeof(SyntaxHighlightingEditor),
            default(string),
            propertyChanged: OnFontFamilyChanged);

        public string? FontFamily
        {
            get => (string?)GetValue(FontFamilyProperty);
            set => SetValue(FontFamilyProperty, value);
        }

        private static void OnFontFamilyChanged(BindableObject bindable, object oldValue, object newValue)
        {
            var control = (SyntaxHighlightingEditor)bindable;
            var family = newValue as string;
            control._editor.FontFamily = family;
            control._overlayLabel.FontFamily = family;
        }

        public new bool Focus()
        {
            return _editor.Focus();
        }

        public new void Unfocus()
        {
            _editor.Unfocus();
        }

        protected override void OnHandlerChanging(HandlerChangingEventArgs args)
        {
            if (args.NewHandler is null && _application is not null)
            {
                _application.RequestedThemeChanged -= HandleRequestedThemeChanged;
            }

            base.OnHandlerChanging(args);
        }

        private void HandleEditorTextChanged(object? sender, TextChangedEventArgs e)
        {
            if (_suppressTextCallback)
            {
                return;
            }

            ApplyHighlighting(e.NewTextValue ?? string.Empty);
            SetValue(TextProperty, e.NewTextValue ?? string.Empty);
            TextChanged?.Invoke(this, e);
        }

        private void ApplyHighlighting(string? text)
        {
            var formatted = TextHighlighter.CreateSqlFormattedString(text);
            _overlayLabel.FormattedText = formatted;
        }

        private void HandleRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e)
        {
            ApplyHighlighting(Text);
        }

        private void SetDefaultFontValues()
        {
            var defaultSize = Device.GetNamedSize(NamedSize.Medium, typeof(Editor));
            _editor.FontSize = defaultSize;
            _overlayLabel.FontSize = defaultSize;
        }
    }
}
