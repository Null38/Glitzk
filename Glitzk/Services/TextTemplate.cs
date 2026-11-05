using System.Diagnostics;
using System.Text;

namespace Glitzk.Services;

class TextTemplate
{
    const string Ellipsis = "...";
    const int MinMaxLength = 4;// Ellipsis.Length + 1
    const int NumberLimit = 90;

    readonly TemplateArg[] _args;
    string _format = string.Empty;
    Segment[] _segments = [];
    int _literalLength;
    List<TemplateError> _errors = [];

    public int MaxLength { get; private set; }

    public IReadOnlyList<TemplateError> Errors => _errors;

    public bool IsValid => _errors.Count == 0;

    public string Template
    {
        get => _format;
        set => ApplyFormat(value);
    }

    public readonly record struct TemplateArg(string Name, int DefaultLength);

    public readonly record struct TemplateError(int Start, int Length, string Message);

    readonly record struct Segment(string? Literal, int ArgIndex, int MaxLength);

    public TextTemplate(TemplateArg[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string name = args[i].Name;
            if (name.Length == 0 || name.AsSpan().ContainsAnyExceptInRange('a', 'z'))
                throw new ArgumentException($"Argument name '{name}' must match [a-z]+.", nameof(args));

            for (int j = 0; j < i; j++)
            {
                if (args[j].Name == name)
                    throw new ArgumentException($"Argument name '{name}' is duplicated.", nameof(args));
            }
        }

        _args = args;
    }

    public TextTemplate(string format, TemplateArg[] args)
        :this(args)
    {
        ApplyFormat(format);
    }

    private void ApplyFormat(string format)
    {
        var errors = new List<TemplateError>();
        Segment[] segments = ParseSegments(format, _args, errors);

        _format = format;
        _errors = errors;
        if (errors.Count > 0)
        {
            _segments = [];
            _literalLength = 0;
            MaxLength = 0;
            return;
        }

        _segments = segments;
        _literalLength = 0;
        MaxLength = 0;
        foreach (Segment segment in _segments)
        {
            if (segment.Literal is string literal)
                _literalLength += literal.Length;
            else
                MaxLength += segment.MaxLength;
        }
        MaxLength += _literalLength;
    }

    enum ParseState { Text, AfterOpen, AfterName, AfterRange, AfterNumber, Skipping }
    enum TokenKind { Text, Open, Close, Name, Number, Range, End }

    readonly record struct Token(TokenKind Kind, int Start, int Length, int Value = 0);

    static Segment[] ParseSegments(string format, TemplateArg[] args, List<TemplateError> errors)
    {
        var segments = new List<Segment>();
        var literal = new StringBuilder();
        int pos = 0;

        ParseState state = ParseState.Text;
        int holeStart = -1;
        int argIndex = -1;
        int maxLength = -1;

        while (true)
        {
            Token token = NextToken(format, ref pos, state != ParseState.Text, errors);
            bool isHoleOpen = state is not (ParseState.Text or ParseState.Skipping);

            if (token.Kind is TokenKind.Open or TokenKind.End && isHoleOpen)
                errors.Add(new TemplateError(holeStart, token.Start - holeStart, "'{'가 닫히지 않았습니다."));

            if (token.Kind == TokenKind.End)
            {
                FlushLiteral(literal, segments);
                break;
            }

            if (token.Kind == TokenKind.Open)
            {
                FlushLiteral(literal, segments);
                holeStart = token.Start;
                argIndex = -1;
                maxLength = -1;
                state = ParseState.AfterOpen;
                continue;
            }

            switch ((state, token.Kind))
            {
                case (ParseState.Text, TokenKind.Text):
                    literal.Append(format, token.Start, token.Length);
                    break;

                case (ParseState.AfterOpen, TokenKind.Name):
                    argIndex = IndexOfArg(format, token, args);
                    if (argIndex < 0)
                    {
                        errors.Add(new TemplateError(token.Start, token.Length, "알 수 없는 변수입니다."));
                        state = ParseState.Skipping;
                        break;
                    }
                    state = ParseState.AfterName;
                    break;
                case (ParseState.AfterName, TokenKind.Range):
                    state = ParseState.AfterRange;
                    break;
                case (ParseState.AfterRange, TokenKind.Number):
                    if (token.Value < 0)
                    {
                        errors.Add(new TemplateError(token.Start, token.Length, $"최대 길이는 {NumberLimit} 이하여야 합니다."));
                        state = ParseState.Skipping;
                        break;
                    }
                    maxLength = token.Value;
                    state = ParseState.AfterNumber;
                    break;

                case (ParseState.AfterName or ParseState.AfterNumber, TokenKind.Close):
                    int length = maxLength >= 0 ? maxLength : args[argIndex].DefaultLength;
                    segments.Add(new Segment(null, argIndex, Math.Max(length, MinMaxLength)));
                    state = ParseState.Text;
                    break;

                case (ParseState.Skipping, TokenKind.Close):
                    state = ParseState.Text;
                    break;
                case (ParseState.Skipping, _):
                    break;

                default:
                    errors.Add(new TemplateError(token.Start, token.Length, ExpectedMessage(state)));
                    state = token.Kind == TokenKind.Close ? ParseState.Text : ParseState.Skipping;
                    break;
            }
        }

        return segments.ToArray();

        static void FlushLiteral(StringBuilder literal, List<Segment> segments)
        {
            if (literal.Length == 0)
                return;

            segments.Add(new Segment(literal.ToString(), -1, 0));
            literal.Clear();
        }

        static string ExpectedMessage(ParseState state) => state switch
        {
            ParseState.AfterOpen => "변수 이름이 와야 합니다.",
            ParseState.AfterName => "'..' 또는 '}'가 와야 합니다.",
            ParseState.AfterRange => "숫자가 와야 합니다.",
            ParseState.AfterNumber => "'}'가 와야 합니다.",
            _ => throw new UnreachableException(),
        };
    }

    static int IndexOfArg(string format, Token name, TemplateArg[] args)
    {
        ReadOnlySpan<char> span = format.AsSpan(name.Start, name.Length);
        for (int i = 0; i < args.Length; i++)
        {
            if (span.SequenceEqual(args[i].Name))
                return i;
        }
        return -1;
    }

    static Token NextToken(string format, ref int pos, bool inHole, List<TemplateError> errors)
        => inHole ? LexHole(format, ref pos) : LexText(format, ref pos, errors);

    static Token LexText(string format, ref int pos, List<TemplateError> errors)
    {
        while (true)
        {
            if (pos >= format.Length)
                return new Token(TokenKind.End, pos, 0);

            int start = pos;
            char c = format[pos];
            bool isDoubled = pos + 1 < format.Length && format[pos + 1] == c;

            if (c == '{')
            {
                pos += isDoubled ? 2 : 1;
                return new Token(isDoubled ? TokenKind.Text : TokenKind.Open, start, 1);
            }

            if (c == '}')
            {
                pos += isDoubled ? 2 : 1;
                if (isDoubled)
                    return new Token(TokenKind.Text, start, 1);

                errors.Add(new TemplateError(start, 1, "'}'를 사용하려면 '}}'으로 표현해야합니다."));
                continue;
            }

            int end = format.AsSpan(pos).IndexOfAny('{', '}');
            pos = end < 0 ? format.Length : pos + end;
            return new Token(TokenKind.Text, start, pos - start);
        }
    }

    static Token LexHole(string format, ref int pos)
    {
        while (pos < format.Length && char.IsWhiteSpace(format[pos]))
            pos++;

        if (pos >= format.Length)
            return new Token(TokenKind.End, pos, 0);

        int start = pos;
        char c = format[pos];

        if (c == '}')
        {
            pos++;
            return new Token(TokenKind.Close, start, 1);
        }

        if (c == '{')
        {
            pos++;
            return new Token(TokenKind.Open, start, 1);
        }

        if (c == '.' && pos + 1 < format.Length && format[pos + 1] == '.')
        {
            pos += 2;
            return new Token(TokenKind.Range, start, 2);
        }

        if (char.IsAsciiLetterLower(c))
        {
            while (pos < format.Length && char.IsAsciiLetterLower(format[pos]))
                pos++;
            return new Token(TokenKind.Name, start, pos - start);
        }

        if (char.IsAsciiDigit(c))
        {
            int value = 0;
            while (pos < format.Length && char.IsAsciiDigit(format[pos]))
            {
                if (value >= 0)
                {
                    value = value * 10 + (format[pos] - '0');
                    if (value > NumberLimit)
                        value = -1;
                }
                pos++;
            }
            return new Token(TokenKind.Number, start, pos - start, value);
        }

        pos += char.IsSurrogatePair(format, pos) ? 2 : 1;
        return new Token(TokenKind.Text, start, pos - start);
    }

    readonly ref struct WriteState(Segment[] segments, ReadOnlySpan<string> args)
    {
        public readonly Segment[] Segments = segments;
        public readonly ReadOnlySpan<string> Args = args;
    }

    public string Format(params ReadOnlySpan<string> args)
    {
        if (!IsValid)
            throw new InvalidOperationException("The template has errors.");

        int length = _literalLength;
        foreach (Segment segment in _segments)
        {
            if (segment.Literal is not null)
                continue;

            length += FormattedLength(args[segment.ArgIndex], segment.MaxLength);
        }

        return string.Create(length, new WriteState(_segments, args), static (destination, state) =>
        {
            foreach (Segment segment in state.Segments)
            {
                if (segment.Literal is string literal)
                {
                    literal.CopyTo(destination);
                    destination = destination[literal.Length..];
                    continue;
                }

                string value = state.Args[segment.ArgIndex];

                if (value.Length <= segment.MaxLength)
                {
                    value.CopyTo(destination);
                    destination = destination[value.Length..];
                    continue;
                }

                int cut = CutIndex(value, segment.MaxLength);
                value.AsSpan(0, cut).CopyTo(destination);
                Ellipsis.CopyTo(destination[cut..]);
                destination = destination[(cut + Ellipsis.Length)..];
            }
        });
    }

    static int FormattedLength(string value, int maxLength)
        => value.Length <= maxLength ? value.Length : CutIndex(value, maxLength) + Ellipsis.Length;

    static int CutIndex(string value, int maxLength)
    {
        int cut = maxLength - Ellipsis.Length;
        return char.IsHighSurrogate(value[cut - 1]) ? cut - 1 : cut;
    }
}
