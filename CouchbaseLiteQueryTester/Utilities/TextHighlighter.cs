using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace CouchbaseLiteQueryTester.Utilities
{
    public static class TextHighlighter
    {
        private static readonly HashSet<string> SqlKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "FROM", "WHERE", "GROUP", "BY", "HAVING", "ORDER", "LIMIT", "OFFSET",
            "JOIN", "INNER", "LEFT", "RIGHT", "FULL", "OUTER", "ON", "AS", "AND", "OR", "NOT",
            "IN", "IS", "NULL", "ARRAY", "FOR", "WHEN", "THEN", "ELSE", "END", "DISTINCT", "ANY",
            "EVERY", "SATISFIES", "LIKE", "BETWEEN", "CASE", "LET", "USE", "KEYS", "INSERT", "UPDATE",
            "DELETE", "UNNEST", "META", "TRUE", "FALSE", "UNION", "ALL", "EXCEPT", "INTERSECT", "UPSERT",
            "VALUES", "RETURNING", "EXISTS", "PRIMARY", "KEY", "SET"
        };

        private readonly record struct SyntaxPalette(
            Color Default,
            Color Keyword,
            Color String,
            Color Number,
            Color Comment,
            Color Property,
            Color Boolean);

        public static FormattedString CreateSqlFormattedString(string? text)
        {
            var formatted = new FormattedString();
            var palette = GetPalette();
            if (string.IsNullOrEmpty(text))
            {
                formatted.Spans.Add(new Span { Text = string.Empty, TextColor = palette.Default });
                return formatted;
            }

            int index = 0;
            while (index < text.Length)
            {
                var span = NextSqlSpan(text, ref index, palette);
                if (span is not null)
                {
                    formatted.Spans.Add(span);
                }
            }

            return formatted;
        }

        public static FormattedString CreateJsonFormattedString(string? json)
        {
            var formatted = new FormattedString();
            var palette = GetPalette();
            if (string.IsNullOrWhiteSpace(json))
            {
                formatted.Spans.Add(new Span { Text = string.Empty, TextColor = palette.Default });
                return formatted;
            }

            try
            {
                using var document = JsonDocument.Parse(json);
                AppendJsonElement(formatted, document.RootElement, 0, palette);
            }
            catch (JsonException)
            {
                formatted.Spans.Add(new Span { Text = json, TextColor = palette.Default });
            }

            return formatted;
        }

        private static Span? NextSqlSpan(string text, ref int index, SyntaxPalette palette)
        {
            if (index >= text.Length)
            {
                return null;
            }

            char current = text[index];

            if (char.IsWhiteSpace(current))
            {
                int start = index;
                while (index < text.Length && char.IsWhiteSpace(text[index]))
                {
                    index++;
                }

                return CreateSpan(text[start..index], palette.Default);
            }

            if (current == '\'' || current == '"')
            {
                return ParseQuoted(text, ref index, current, palette);
            }

            if (current == '-' && index + 1 < text.Length && text[index + 1] == '-')
            {
                int start = index;
                index += 2;
                while (index < text.Length && text[index] != '\n')
                {
                    index++;
                }

                return CreateSpan(text[start..index], palette.Comment);
            }

            if (current == '/' && index + 1 < text.Length && text[index + 1] == '*')
            {
                int start = index;
                index += 2;
                while (index + 1 < text.Length && !(text[index] == '*' && text[index + 1] == '/'))
                {
                    index++;
                }

                index = Math.Min(text.Length, index + 2);
                return CreateSpan(text[start..index], palette.Comment);
            }

            if (char.IsLetter(current) || current == '_')
            {
                int start = index;
                index++;
                while (index < text.Length && (char.IsLetterOrDigit(text[index]) || text[index] == '_'))
                {
                    index++;
                }

                var token = text[start..index];
                if (SqlKeywords.Contains(token))
                {
                    return new Span
                    {
                        Text = token,
                        TextColor = palette.Keyword,
                        FontAttributes = FontAttributes.Bold
                    };
                }

                return CreateSpan(token, palette.Default);
            }

            if ((current == '-' && index + 1 < text.Length && char.IsDigit(text[index + 1])) || char.IsDigit(current))
            {
                int start = index;
                index++;
                while (index < text.Length && (char.IsDigit(text[index]) || text[index] == '.' || text[index] == '_'))
                {
                    index++;
                }

                return CreateSpan(text[start..index], palette.Number);
            }

            index++;
            return CreateSpan(current.ToString(), palette.Default);
        }

        private static Span ParseQuoted(string text, ref int index, char quote, SyntaxPalette palette)
        {
            int start = index;
            index++;
            var escaped = false;
            while (index < text.Length)
            {
                var current = text[index];
                index++;

                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (current == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (current == quote)
                {
                    if (index < text.Length && text[index] == quote)
                    {
                        index++;
                        continue;
                    }

                    break;
                }
            }

            return CreateSpan(text[start..Math.Min(index, text.Length)], palette.String);
        }

        private static void AppendJsonElement(FormattedString formatted, JsonElement element, int indentLevel, SyntaxPalette palette)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    AppendJsonObject(formatted, element, indentLevel, palette);
                    break;
                case JsonValueKind.Array:
                    AppendJsonArray(formatted, element, indentLevel, palette);
                    break;
                case JsonValueKind.String:
                    formatted.Spans.Add(CreateSpan(element.GetRawText(), palette.String));
                    break;
                case JsonValueKind.Number:
                    formatted.Spans.Add(CreateSpan(element.GetRawText(), palette.Number));
                    break;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    formatted.Spans.Add(CreateSpan(element.GetRawText(), palette.Boolean));
                    break;
                case JsonValueKind.Null:
                    formatted.Spans.Add(CreateSpan("null", palette.Boolean));
                    break;
                default:
                    formatted.Spans.Add(CreateSpan(element.GetRawText(), palette.Default));
                    break;
            }
        }

        private static void AppendJsonObject(FormattedString formatted, JsonElement element, int indentLevel, SyntaxPalette palette)
        {
            var properties = element.EnumerateObject().ToList();
            if (properties.Count == 0)
            {
                formatted.Spans.Add(CreateSpan("{}", palette.Default));
                return;
            }

            formatted.Spans.Add(CreateSpan("{\n", palette.Default));
            for (int i = 0; i < properties.Count; i++)
            {
                var property = properties[i];
                AppendIndent(formatted, indentLevel + 1, palette);
                var encodedName = System.Text.Json.JsonEncodedText.Encode(property.Name).ToString();
                formatted.Spans.Add(CreateSpan($"\"{encodedName}\"", palette.Property));
                formatted.Spans.Add(CreateSpan(": ", palette.Default));
                AppendJsonElement(formatted, property.Value, indentLevel + 1, palette);

                if (i < properties.Count - 1)
                {
                    formatted.Spans.Add(CreateSpan(",\n", palette.Default));
                }
                else
                {
                    formatted.Spans.Add(CreateSpan("\n", palette.Default));
                }
            }

            AppendIndent(formatted, indentLevel, palette);
            formatted.Spans.Add(CreateSpan("}", palette.Default));
        }

        private static void AppendJsonArray(FormattedString formatted, JsonElement element, int indentLevel, SyntaxPalette palette)
        {
            var items = element.EnumerateArray().ToList();
            if (items.Count == 0)
            {
                formatted.Spans.Add(CreateSpan("[]", palette.Default));
                return;
            }

            formatted.Spans.Add(CreateSpan("[\n", palette.Default));
            for (int i = 0; i < items.Count; i++)
            {
                AppendIndent(formatted, indentLevel + 1, palette);
                AppendJsonElement(formatted, items[i], indentLevel + 1, palette);
                if (i < items.Count - 1)
                {
                    formatted.Spans.Add(CreateSpan(",\n", palette.Default));
                }
                else
                {
                    formatted.Spans.Add(CreateSpan("\n", palette.Default));
                }
            }

            AppendIndent(formatted, indentLevel, palette);
            formatted.Spans.Add(CreateSpan("]", palette.Default));
        }

        private static void AppendIndent(FormattedString formatted, int indentLevel, SyntaxPalette palette)
        {
            if (indentLevel <= 0)
            {
                return;
            }

            formatted.Spans.Add(CreateSpan(new string(' ', indentLevel * 2), palette.Default));
        }

        private static Span CreateSpan(string text, Color color)
        {
            return new Span
            {
                Text = text,
                TextColor = color
            };
        }

        private static SyntaxPalette GetPalette()
        {
            var theme = Application.Current?.RequestedTheme ?? AppTheme.Light;
            return theme == AppTheme.Dark
                ? new SyntaxPalette(
                    Color.FromArgb("#E8E8E8"),
                    Color.FromArgb("#4FC1FF"),
                    Color.FromArgb("#CE9178"),
                    Color.FromArgb("#B5CEA8"),
                    Color.FromArgb("#6A9955"),
                    Color.FromArgb("#4EC9B0"),
                    Color.FromArgb("#C586C0"))
                : new SyntaxPalette(
                    Color.FromArgb("#202020"),
                    Color.FromArgb("#0066CC"),
                    Color.FromArgb("#A31515"),
                    Color.FromArgb("#098658"),
                    Color.FromArgb("#6A9955"),
                    Color.FromArgb("#1A4B94"),
                    Color.FromArgb("#B000B5"));
        }
    }
}
