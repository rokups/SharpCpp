using System.Text;

namespace SharpCpp.Model.Utilities;

public sealed class CodeWriter
{
    private readonly StringBuilder _builder = new();
    private const int IndentSize = 4;
    private int _indentLevel;
    private bool _isAtStartOfLine = true;

    public void Write(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return;
        }

        if (!_isAtStartOfLine && !StartsWithNewLine(code))
        {
            _builder.AppendLine();
            _isAtStartOfLine = true;
        }

        var endsWithClosingBraceWithoutTrailingNewLine = EndsWithClosingBrace(code) && !EndsWithNewLine(code);
        var lines = code.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            var line = NormalizeIndentation(lines[index]);
            var appendNewLine = index < lines.Length - 1 || index == lines.Length - 1 && endsWithClosingBraceWithoutTrailingNewLine;
            WriteNormalizedLine(line, appendNewLine);
        }
    }

    public void WriteLine(string? line = null)
    {
        Write((line ?? string.Empty) + Environment.NewLine);
    }

    public override string ToString()
    {
        return _builder.ToString();
    }

    private void WriteNormalizedLine(string line, bool appendNewLine)
    {
        if (line.Length == 0)
        {
            if (appendNewLine)
            {
                _builder.AppendLine();
                _isAtStartOfLine = true;
            }

            return;
        }

        var firstContentIndex = GetFirstContentIndex(line);
        var leadingClosingBraceCount = CountLeadingClosingBraces(line, firstContentIndex);
        var effectiveIndentLevel = Math.Max(0, _indentLevel - leadingClosingBraceCount);

        _builder.Append(' ', effectiveIndentLevel * IndentSize);
        _builder.Append(line);

        if (appendNewLine)
        {
            _builder.AppendLine();
            _isAtStartOfLine = true;
        }
        else
        {
            _isAtStartOfLine = false;
        }

        _indentLevel = Math.Max(0, effectiveIndentLevel + CountBraceDelta(line, firstContentIndex + leadingClosingBraceCount));
    }

    private static string NormalizeIndentation(string line)
    {
        if (line.Length == 0)
        {
            return line;
        }

        var firstContentIndex = 0;

        while (firstContentIndex < line.Length && char.IsWhiteSpace(line[firstContentIndex]))
        {
            firstContentIndex++;
        }

        return line[firstContentIndex..];
    }

    private static int GetFirstContentIndex(string line)
    {
        var index = 0;

        while (index < line.Length && char.IsWhiteSpace(line[index]))
        {
            index++;
        }

        return index;
    }

    private static int CountLeadingClosingBraces(string line, int startIndex)
    {
        var index = startIndex;

        while (index < line.Length && line[index] == '}')
        {
            index++;
        }

        return index - startIndex;
    }

    private static int CountBraceDelta(string line, int startIndex)
    {
        var delta = 0;
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var isEscaped = false;

        for (var index = startIndex; index < line.Length; index++)
        {
            var current = line[index];
            var next = index + 1 < line.Length ? line[index + 1] : '\0';

            if (!inSingleQuote && !inDoubleQuote && current == '/' && next == '/')
            {
                break;
            }

            if (isEscaped)
            {
                isEscaped = false;
                continue;
            }

            if (current == '\\' && (inSingleQuote || inDoubleQuote))
            {
                isEscaped = true;
                continue;
            }

            if (current == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (current == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (inSingleQuote || inDoubleQuote)
            {
                continue;
            }

            if (current == '{')
            {
                delta++;
            }
            else if (current == '}')
            {
                delta--;
            }
        }

        return delta;
    }

    private static bool EndsWithClosingBrace(string code)
    {
        for (var index = code.Length - 1; index >= 0; index--)
        {
            if (char.IsWhiteSpace(code[index]))
            {
                continue;
            }

            return code[index] == '}';
        }

        return false;
    }

    private static bool EndsWithNewLine(string code)
    {
        return code.EndsWith('\n') || code.EndsWith('\r');
    }

    private static bool StartsWithNewLine(string code)
    {
        return code[0] is '\n' or '\r';
    }
}
