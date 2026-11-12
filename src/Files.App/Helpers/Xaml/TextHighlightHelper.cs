// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Files.App.Helpers
{
	public static class TextHighlightHelper
	{
		private static readonly SolidColorBrush DarkThemeBrush = new(Color.FromArgb(0xFF, 0x9B, 0xD9, 0xFF));
		private static readonly SolidColorBrush LightThemeBrush = new(Color.FromArgb(0xFF, 0x0B, 0x6E, 0xC4));

		// A highlighter with no background still gets a filled highlight box, so the fill is made explicit.
		private static readonly SolidColorBrush TransparentBrush = new(Color.FromArgb(0x00, 0x00, 0x00, 0x00));

		public static readonly DependencyProperty HighlightQueryProperty = DependencyProperty.RegisterAttached(
			"HighlightQuery",
			typeof(string),
			typeof(TextHighlightHelper),
			new PropertyMetadata(null, OnHighlightQueryChanged));

		private static readonly DependencyProperty TextChangedTokenProperty = DependencyProperty.RegisterAttached(
			"TextChangedToken",
			typeof(long),
			typeof(TextHighlightHelper),
			new PropertyMetadata(-1L));

		public static string? GetHighlightQuery(TextBlock element) => (string?)element.GetValue(HighlightQueryProperty);

		public static void SetHighlightQuery(TextBlock element, string? value) => element.SetValue(HighlightQueryProperty, value);

		private static void OnHighlightQueryChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
		{
			if (sender is not TextBlock textBlock)
				return;

			// Virtualized containers are reused, so the text can change while the query stays the same.
			if ((long)textBlock.GetValue(TextChangedTokenProperty) < 0)
				textBlock.SetValue(TextChangedTokenProperty, textBlock.RegisterPropertyChangedCallback(TextBlock.TextProperty, OnTextChanged));

			ApplyHighlight(textBlock);
		}

		private static void OnTextChanged(DependencyObject sender, DependencyProperty dp)
		{
			if (sender is TextBlock textBlock)
				ApplyHighlight(textBlock);
		}

		private static void ApplyHighlight(TextBlock textBlock)
		{
			var text = textBlock.Text ?? string.Empty;
			var query = GetHighlightQuery(textBlock);

			var ranges = new List<TextRange>();
			if (!string.IsNullOrEmpty(query))
			{
				for (var index = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
					index >= 0;
					index = text.IndexOf(query, index + query.Length, StringComparison.OrdinalIgnoreCase))
				{
					ranges.Add(new TextRange { StartIndex = index, Length = query.Length });
				}
			}

			textBlock.TextHighlighters.Clear();

			if (ranges.Count == 0)
				return;

			var highlighter = new TextHighlighter
			{
				Background = TransparentBrush,
				Foreground = textBlock.ActualTheme == ElementTheme.Dark ? DarkThemeBrush : LightThemeBrush,
			};

			foreach (var range in ranges)
				highlighter.Ranges.Add(range);

			textBlock.TextHighlighters.Add(highlighter);
		}
	}
}
