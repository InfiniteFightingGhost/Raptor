namespace Raptor.Compiler
{
    public class Lexer
    {
        private readonly string _source;
        private int _index;
        private int _line = 1;
        private int _column = 0;
        private readonly DiagnosticReporter _reporter;

        public Lexer(string source, DiagnosticReporter reporter)
        {
            _source = source;
            _reporter = reporter;
        }

        public List<Token> ScanTokens()
        {
            var tokens = new List<Token>();
            while (!IsAtEnd())
            {
                char c = Peek();
                if (char.IsWhiteSpace(c))
                {
                    if (c == '\n')
                    {
                        _line++;
                        _column = -1;
                    }
                    Advance();
                    continue;
                }

                if (c == '/' && PeekNext() == '/')
                {
                    // Single-line comment: consume until newline or EOF
                    while (Peek() != '\n' && !IsAtEnd())
                        Advance();
                    continue;
                }

                if (char.IsDigit(c))
                {
                    tokens.Add(ScanNumber());
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    tokens.Add(ScanIdentifierOrKeyword());
                    continue;
                }

                Token? result = ScanOperatorOrPunctuation();
                if (result != null)
                    tokens.Add(result);
                else
                {
                    LexerSynchronize();
                }
            }

            tokens.Add(new Token(TokenType.EOF, "", _line, _column));
            return tokens;
        }

        private char Advance()
        {
            _column++;
            return _source[_index++];
        }

        private char Peek() => IsAtEnd() ? '\0' : _source[_index];

        private char PeekNext() => _index + 1 >= _source.Length ? '\0' : _source[_index + 1];

        private bool IsAtEnd() => _index >= _source.Length;

        private void LexerSynchronize()
        {
            while (!IsAtEnd())
            {
                char c = Peek();

                // Stop skipping when we hit whitespace or statement punctuation
                if (char.IsWhiteSpace(c) || c == ';' || c == ')' || c == '}' || c == ']')
                {
                    return;
                }

                Advance(); // Discard the bad character
            }
        }

        private Token ScanNumber()
        {
            int startColumn = _column + 1;
            int start = _index;
            while (char.IsDigit(Peek()))
                Advance();

            if (Peek() == '.' && char.IsDigit(PeekNext()))
            {
                Advance(); // Consume '.'
                while (char.IsDigit(Peek()))
                    Advance();
            }

            string val = _source[start.._index];
            return new Token(TokenType.Number, val, _line, startColumn);
        }

        private Token ScanIdentifierOrKeyword()
        {
            int startColumn = _column + 1;
            int start = _index;
            while (char.IsLetterOrDigit(Peek()) || Peek() == '_')
            {
                Advance();
            }

            string val = _source[start.._index];
            TokenType type = val switch
            {
                "var" => TokenType.Var,
                "if" => TokenType.If,
                "else" => TokenType.Else,
                "while" => TokenType.While,
                "return" => TokenType.Return,
                "for" => TokenType.For,
                "true" => TokenType.True,
                "false" => TokenType.False,
                _ => TokenType.Identifier,
            };

            return new Token(type, val, _line, startColumn);
        }

        private Token? ScanOperatorOrPunctuation()
        {
            int startColumn = _column + 1;
            char c = Advance();
            try
            {
                return c switch
                {
                    ';' => new Token(TokenType.Semicolon, ";", _line, startColumn),
                    '(' => new Token(TokenType.OpenParenthesis, "(", _line, startColumn),
                    ')' => new Token(TokenType.CloseParenthesis, ")", _line, startColumn),
                    '[' => new Token(TokenType.OpenBracket, "[", _line, startColumn),
                    ']' => new Token(TokenType.CloseBracket, "]", _line, startColumn),
                    '{' => new Token(TokenType.OpenBrace, "{", _line, startColumn),
                    '}' => new Token(TokenType.CloseBrace, "}", _line, startColumn),
                    ',' => new Token(TokenType.Comma, ",", _line, startColumn),
                    '.' => new Token(TokenType.Dot, ".", _line, startColumn),

                    '+' => Peek() switch
                    {
                        '=' => ConsumeAndReturn(TokenType.PlusEquals, "+=", startColumn),
                        '+' => ConsumeAndReturn(TokenType.PlusPlus, "++", startColumn),
                        _ => new Token(TokenType.Plus, "+", _line, startColumn),
                    },
                    '-' => Peek() switch
                    {
                        '=' => ConsumeAndReturn(TokenType.MinusEquals, "-=", startColumn),
                        '-' => ConsumeAndReturn(TokenType.MinusMinus, "--", startColumn),
                        _ => new Token(TokenType.Minus, "-", _line, startColumn),
                    },
                    '*' => Peek() switch
                    {
                        '=' => ConsumeAndReturn(TokenType.StarEquals, "*=", startColumn),
                        _ => new Token(TokenType.Star, "*", _line, startColumn),
                    },
                    '/' => Peek() switch
                    {
                        '=' => ConsumeAndReturn(TokenType.SlashEquals, "/=", startColumn),
                        _ => new Token(TokenType.Slash, "/", _line, startColumn),
                    },
                    '=' => Match('=')
                        ? new Token(TokenType.Equal, "==", _line, startColumn)
                        : new Token(TokenType.Assign, "=", _line, startColumn),
                    '!' => Peek() switch
                    {
                        '=' => ConsumeAndReturn(TokenType.NotEqual, "!=", startColumn),
                        _ => new Token(TokenType.Bang, "!", _line, startColumn),
                    },
                    '<' => Peek() switch
                    {
                        '=' => ConsumeAndReturn(TokenType.LessEqual, "<=", startColumn),
                        '<' => ConsumeAndReturn(TokenType.LessLess, "<<", startColumn),
                        _ => new Token(TokenType.Less, "<", _line, startColumn),
                    },
                    '>' => Peek() switch
                    {
                        '=' => ConsumeAndReturn(TokenType.GreaterEqual, ">=", startColumn),
                        '>' => ConsumeAndReturn(TokenType.GreaterGreater, ">>", startColumn),
                        _ => new Token(TokenType.Greater, ">", _line, startColumn),
                    },
                    '%' => Peek() switch
                    {
                        '=' => ConsumeAndReturn(TokenType.PercentEquals, "%=", startColumn),
                        _ => new Token(TokenType.Percent, "%", _line, startColumn),
                    },
                    '&' => Peek() switch
                    {
                        '=' => ConsumeAndReturn(TokenType.AmpersandEquals, "&=", startColumn),
                        '&' => ConsumeAndReturn(TokenType.AmpersandAmpersand, "&&", startColumn),
                        _ => new Token(TokenType.Ampersand, "&", _line, startColumn),
                    },
                    '|' => Peek() switch
                    {
                        '=' => ConsumeAndReturn(TokenType.PipeEquals, "|=", startColumn),
                        '|' => ConsumeAndReturn(TokenType.PipePipe, "||", startColumn),
                        _ => new Token(TokenType.Pipe, "|", _line, startColumn),
                    },
                    '^' => Peek() switch
                    {
                        '=' => ConsumeAndReturn(TokenType.CaretEquals, "^=", startColumn),
                        _ => new Token(TokenType.Caret, "^", _line, startColumn),
                    },
                    _ => throw new LexerException(
                        $"Unexpected character '{c}' at line {_line} at column {startColumn}"
                    ),
                };
            }
            catch (LexerException ex)
            {
                _reporter.Report(
                    new Diagnostic(
                        "E00017",
                        DiagnosticSeverity.Error,
                        ex.Message,
                        _line,
                        startColumn,
                        1
                    )
                );
                return null;
            }
        }

        private bool Match(char expected)
        {
            if (IsAtEnd() || _source[_index] != expected)
                return false;
            _index++;
            _column++;
            return true;
        }

        private Token ConsumeAndReturn(TokenType type, string value, int startColumn)
        {
            Advance(); // Consume the peeked character
            return new Token(type, value, _line, startColumn);
        }
    }

    [Serializable]
    internal class LexerException : Exception
    {
        public LexerException() { }

        public LexerException(string? message)
            : base(message) { }

        public LexerException(string? message, Exception? innerException)
            : base(message, innerException) { }
    }
}
