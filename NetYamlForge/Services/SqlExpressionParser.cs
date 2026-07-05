// ファイル概要: YAML 設定由来の SQL 式（WHERE 句・集計式）のホワイトリスト検証パーサー。
// 文法に合致しない入力を例外で拒否します。文法にセミコロン・コメント・サブクエリ・UNION が
// 存在しないため、構造的に SQL インジェクションを排除します。

using System.Text;

namespace NetYamlForge.Services;

/// <summary>
/// YAML 設定由来の SQL 式（WHERE 句・集計式）のホワイトリスト検証パーサー。
/// 下記文法に合致しない入力を例外で拒否します。文法にセミコロン・コメント・
/// サブクエリ・UNION が存在しないため、構造的に SQL インジェクションを排除します。
/// </summary>
public static class SqlExpressionParser
{
    private static readonly HashSet<string> AllowedFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "LENGTH", "LOWER", "UPPER", "TRIM", "SUBSTR", "REPLACE",
        "ABS", "ROUND", "COALESCE", "IFNULL", "NULLIF",
        "DATE", "DATETIME", "TIME", "STRFTIME", "JULIANDAY",
        "MIN", "MAX", "SUM", "COUNT", "AVG", "CAST"
    };

    private static readonly HashSet<string> CastTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "INTEGER", "TEXT", "REAL", "NUMERIC", "BLOB"
    };

    /// <summary>式を検証。不正なら InvalidOperationException（context と失敗位置を含む）</summary>
    public static void Validate(string expression, string context)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new InvalidOperationException($"Invalid expression in '{context}': expression is empty.");

        var tokenizer = new Tokenizer(expression, context);
        var tokens = tokenizer.Tokenize();
        if (tokens.Count == 0)
            throw new InvalidOperationException($"Invalid expression in '{context}': no tokens found.");

        var parser = new Parser(tokens, context);
        parser.ParseExpression();
        parser.ExpectEnd();
    }

    private enum TokenType
    {
        Identifier,
        Number,
        StringLiteral,
        Operator,
        Star,
        LParen,
        RParen,
        Comma,
        Dot,
        Keyword,
        Eof
    }

    private sealed record Token(TokenType Type, string Value, int Position);

    private sealed class Tokenizer
    {
        private readonly string _input;
        private readonly string _context;
        private int _pos;

        public Tokenizer(string input, string context)
        {
            _input = input;
            _context = context;
            _pos = 0;
        }

        public List<Token> Tokenize()
        {
            var tokens = new List<Token>();
            while (_pos < _input.Length)
            {
                SkipWhitespace();
                if (_pos >= _input.Length) break;

                var ch = _input[_pos];
                var startPos = _pos;

                if (ch == '(') { tokens.Add(new Token(TokenType.LParen, "(", _pos)); _pos++; }
                else if (ch == ')') { tokens.Add(new Token(TokenType.RParen, ")", _pos)); _pos++; }
                else if (ch == ',') { tokens.Add(new Token(TokenType.Comma, ",", _pos)); _pos++; }
                else if (ch == '.') { tokens.Add(new Token(TokenType.Dot, ".", _pos)); _pos++; }
                else if (ch == '*') { tokens.Add(new Token(TokenType.Star, "*", _pos)); _pos++; }
                else if (ch == '\'' || ch == '"') { tokens.Add(ReadString()); }
                else if (char.IsDigit(ch) || (ch == '-' && _pos + 1 < _input.Length && char.IsDigit(_input[_pos + 1])))
                { tokens.Add(ReadNumber()); }
                else if (ch == '|') { tokens.Add(ReadPipe()); }
                else if (IsOperatorStart(ch)) { tokens.Add(ReadOperator()); }
                else if (IsIdentifierStart(ch)) { tokens.Add(ReadIdentifier()); }
                else
                {
                    throw new InvalidOperationException(
                        $"Unexpected character '{ch}' at position {_pos} in '{_context}'.");
                }
            }
            tokens.Add(new Token(TokenType.Eof, "", _pos));
            return tokens;
        }

        private void SkipWhitespace()
        {
            while (_pos < _input.Length && char.IsWhiteSpace(_input[_pos]))
                _pos++;
        }

        private Token ReadString()
        {
            var quote = _input[_pos];
            var start = _pos;
            _pos++; // skip opening quote
            var sb = new StringBuilder();
            while (_pos < _input.Length)
            {
                if (_input[_pos] == quote)
                {
                    if (_pos + 1 < _input.Length && _input[_pos + 1] == quote)
                    {
                        sb.Append(quote);
                        _pos += 2;
                    }
                    else
                    {
                        _pos++; // skip closing quote
                        return new Token(TokenType.StringLiteral, sb.ToString(), start);
                    }
                }
                else
                {
                    sb.Append(_input[_pos]);
                    _pos++;
                }
            }
            throw new InvalidOperationException(
                $"Unterminated string literal starting at position {start} in '{_context}'.");
        }

        private Token ReadNumber()
        {
            var start = _pos;
            if (_input[_pos] == '-') _pos++;
            while (_pos < _input.Length && char.IsDigit(_input[_pos]))
                _pos++;
            if (_pos < _input.Length && _input[_pos] == '.')
            {
                _pos++;
                while (_pos < _input.Length && char.IsDigit(_input[_pos]))
                    _pos++;
            }
            return new Token(TokenType.Number, _input[start.._pos], start);
        }

        private Token ReadPipe()
        {
            var start = _pos;
            _pos++; // skip first |
            if (_pos < _input.Length && _input[_pos] == '|')
            {
                _pos++; // skip second |
                return new Token(TokenType.Operator, "||", start);
            }
            throw new InvalidOperationException(
                $"Unexpected character '|' at position {start} in '{_context}'. Did you mean '||' (string concatenation)?");
        }

        private Token ReadOperator()
        {
            var start = _pos;
            var ch = _input[_pos];
            _pos++;

            // 2-char operators
            if (_pos < _input.Length)
            {
                var next = _input[_pos];
                if ((ch == '!' && next == '=') || (ch == '<' && (next == '=' || next == '>')) ||
                    (ch == '>' && next == '='))
                {
                    _pos++;
                    return new Token(TokenType.Operator, _input[start.._pos], start);
                }
            }

            return ch switch
            {
                '=' or '<' or '>' or '+' or '-' or '*' or '/' or '%' =>
                    new Token(TokenType.Operator, ch.ToString(), start),
                _ => throw new InvalidOperationException(
                    $"Unexpected operator character '{ch}' at position {start} in '{_context}'.")
            };
        }

        private Token ReadIdentifier()
        {
            var start = _pos;
            while (_pos < _input.Length && IsIdentifierPart(_input[_pos]))
                _pos++;
            var word = _input[start.._pos];

            if (IsKeyword(word))
                return new Token(TokenType.Keyword, word, start);

            return new Token(TokenType.Identifier, word, start);
        }

        private static bool IsIdentifierStart(char ch) =>
            char.IsLetter(ch) || ch == '_' || ch > '\u007F';

        private static bool IsIdentifierPart(char ch) =>
            char.IsLetterOrDigit(ch) || ch == '_' || ch > '\u007F';

        private static bool IsOperatorStart(char ch) =>
            ch is '=' or '!' or '<' or '>' or '+' or '-' or '*' or '/' or '%';

        private static bool IsKeyword(string word) =>
            word.Equals("AND", StringComparison.OrdinalIgnoreCase) ||
            word.Equals("OR", StringComparison.OrdinalIgnoreCase) ||
            word.Equals("NOT", StringComparison.OrdinalIgnoreCase) ||
            word.Equals("IS", StringComparison.OrdinalIgnoreCase) ||
            word.Equals("NULL", StringComparison.OrdinalIgnoreCase) ||
            word.Equals("LIKE", StringComparison.OrdinalIgnoreCase) ||
            word.Equals("IN", StringComparison.OrdinalIgnoreCase) ||
            word.Equals("BETWEEN", StringComparison.OrdinalIgnoreCase) ||
            word.Equals("AS", StringComparison.OrdinalIgnoreCase) ||
            word.Equals("CURRENT_TIMESTAMP", StringComparison.OrdinalIgnoreCase) ||
            word.Equals("CURRENT_DATE", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Parser
    {
        private readonly List<Token> _tokens;
        private readonly string _context;
        private int _pos;

        public Parser(List<Token> tokens, string context)
        {
            _tokens = tokens;
            _context = context;
            _pos = 0;
        }

        private Token Current => _tokens[_pos];
        private Token Peek(int offset = 0) => _tokens[Math.Min(_pos + offset, _tokens.Count - 1)];

        private void Advance() => _pos++;

        public void ExpectEnd()
        {
            if (Current.Type != TokenType.Eof)
                throw new InvalidOperationException(
                    $"Unexpected token '{Current.Value}' at position {Current.Position} in '{_context}'. Expected end of expression.");
        }

        public void ParseExpression()
        {
            ParseOrExpression();
        }

        private void ParseOrExpression()
        {
            ParseAndExpression();
            while (Current.Type == TokenType.Keyword && Current.Value.Equals("OR", StringComparison.OrdinalIgnoreCase))
            {
                Advance();
                ParseAndExpression();
            }
        }

        private void ParseAndExpression()
        {
            ParseNotExpression();
            while (Current.Type == TokenType.Keyword && Current.Value.Equals("AND", StringComparison.OrdinalIgnoreCase))
            {
                Advance();
                ParseNotExpression();
            }
        }

        private void ParseNotExpression()
        {
            if (Current.Type == TokenType.Keyword && Current.Value.Equals("NOT", StringComparison.OrdinalIgnoreCase))
            {
                Advance();
                // Handle NOT IN and NOT LIKE as postfix operators
                if (Current.Type == TokenType.Keyword)
                {
                    if (Current.Value.Equals("IN", StringComparison.OrdinalIgnoreCase))
                    {
                        // NOT IN is handled by ParsePredicate, but we already consumed NOT
                        // So parse IN manually here
                        Advance(); // skip IN
                        ExpectTokenType(TokenType.LParen, "'('");
                        Advance();
                        ParseOperand();
                        while (Current.Type == TokenType.Comma)
                        {
                            Advance();
                            ParseOperand();
                        }
                        ExpectTokenType(TokenType.RParen, "')'");
                        Advance();
                        return;
                    }
                    if (Current.Value.Equals("LIKE", StringComparison.OrdinalIgnoreCase))
                    {
                        Advance();
                        ParseOperand();
                        return;
                    }
                }
            }
            ParsePredicate();
        }

        private void ParsePredicate()
        {
            if (Current.Type == TokenType.LParen)
            {
                Advance();
                ParseExpression();
                ExpectTokenType(TokenType.RParen, "')'");
                Advance();
                return;
            }

            ParseOperand();

            // Check for postfix operations
            if (Current.Type == TokenType.Keyword)
            {
                // Handle NOT IN and NOT LIKE as postfix
                if (Current.Value.Equals("NOT", StringComparison.OrdinalIgnoreCase))
                {
                    var savedPos = _pos;
                    Advance();
                    if (Current.Type == TokenType.Keyword &&
                        (Current.Value.Equals("IN", StringComparison.OrdinalIgnoreCase) ||
                         Current.Value.Equals("LIKE", StringComparison.OrdinalIgnoreCase)))
                    {
                        var op = Current.Value;
                        Advance();
                        if (op.Equals("LIKE", StringComparison.OrdinalIgnoreCase))
                        {
                            ParseOperand();
                            return;
                        }
                        // NOT IN
                        ExpectTokenType(TokenType.LParen, "'('");
                        Advance();
                        ParseOperand();
                        while (Current.Type == TokenType.Comma)
                        {
                            Advance();
                            ParseOperand();
                        }
                        ExpectTokenType(TokenType.RParen, "')'");
                        Advance();
                        return;
                    }
                    // Not NOT IN/NOT LIKE, restore position
                    _pos = savedPos;
                }

                if (Current.Value.Equals("IS", StringComparison.OrdinalIgnoreCase))
                {
                    Advance();
                    if (Current.Type == TokenType.Keyword && Current.Value.Equals("NOT", StringComparison.OrdinalIgnoreCase))
                        Advance();
                    ExpectTokenType(TokenType.Keyword, "NULL");
                    if (!Current.Value.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(
                            $"Expected NULL after IS at position {Current.Position} in '{_context}'.");
                    Advance();
                    return;
                }

                if (Current.Value.Equals("LIKE", StringComparison.OrdinalIgnoreCase))
                {
                    Advance();
                    ParseOperand();
                    return;
                }

                if (Current.Value.Equals("IN", StringComparison.OrdinalIgnoreCase))
                {
                    Advance();
                    ExpectTokenType(TokenType.LParen, "'('");
                    Advance();
                    ParseOperand();
                    while (Current.Type == TokenType.Comma)
                    {
                        Advance();
                        ParseOperand();
                    }
                    ExpectTokenType(TokenType.RParen, "')'");
                    Advance();
                    return;
                }

                if (Current.Value.Equals("BETWEEN", StringComparison.OrdinalIgnoreCase))
                {
                    Advance();
                    ParseOperand();
                    ExpectTokenType(TokenType.Keyword, "AND");
                    if (!Current.Value.Equals("AND", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(
                            $"Expected AND after BETWEEN ... at position {Current.Position} in '{_context}'.");
                    Advance();
                    ParseOperand();
                    return;
                }
            }

            // comparison operators
            if (Current.Type == TokenType.Operator &&
                (Current.Value == "=" || Current.Value == "!=" || Current.Value == "<>" ||
                 Current.Value == "<" || Current.Value == "<=" || Current.Value == ">" || Current.Value == ">="))
            {
                Advance();
                ParseOperand();
            }
        }

        private void ParseOperand()
        {
            ParseTerm();
            while ((Current.Type == TokenType.Operator &&
                   (Current.Value == "+" || Current.Value == "-" || Current.Value == "*" ||
                    Current.Value == "/" || Current.Value == "%" || Current.Value == "||")) ||
                   Current.Type == TokenType.Star)
            {
                Advance();
                ParseTerm();
            }
        }

        private void ParseTerm()
        {
            if (Current.Type == TokenType.LParen)
            {
                Advance();
                ParseOperand();
                ExpectTokenType(TokenType.RParen, "')'");
                Advance();
                return;
            }

            if (Current.Type == TokenType.Number || Current.Type == TokenType.StringLiteral ||
                (Current.Type == TokenType.Keyword &&
                (Current.Value.Equals("NULL", StringComparison.OrdinalIgnoreCase) ||
                 Current.Value.Equals("CURRENT_TIMESTAMP", StringComparison.OrdinalIgnoreCase) ||
                 Current.Value.Equals("CURRENT_DATE", StringComparison.OrdinalIgnoreCase))))
            {
                Advance();
                return;
            }

            // Function call or qualified identifier
            if (Current.Type == TokenType.Identifier)
            {
                var name = Current.Value;
                Advance();

                if (Current.Type == TokenType.LParen)
                {
                    // Function call
                    if (!AllowedFunctions.Contains(name))
                        throw new InvalidOperationException(
                            $"Function '{name}' at position {Current.Position} is not in the allowed function list in '{_context}'.");

                    if (name.Equals("CAST", StringComparison.OrdinalIgnoreCase))
                    {
                        ParseCastFunction();
                    }
                    else
                    {
                        Advance(); // skip (
                        if (Current.Type != TokenType.RParen)
                        {
                            // COUNT(*) special case
                            if (Current.Type == TokenType.Star)
                            {
                                Advance();
                            }
                            else
                            {
                                ParseOperand();
                                while (Current.Type == TokenType.Comma)
                                {
                                    Advance();
                                    ParseOperand();
                                }
                            }
                        }
                        ExpectTokenType(TokenType.RParen, "')'");
                        Advance();
                    }
                    return;
                }

                // Qualified identifier: table.column or just column
                if (Current.Type == TokenType.Dot)
                {
                    Advance();
                    ExpectTokenType(TokenType.Identifier, "column name");
                    Advance();
                }
                return;
            }

            // Handle star as standalone (shouldn't normally appear outside COUNT(*))
            if (Current.Type == TokenType.Star)
            {
                throw new InvalidOperationException(
                    $"Unexpected '*' at position {Current.Position} in '{_context}'. Use COUNT(*) for counting.");
            }

            throw new InvalidOperationException(
                $"Unexpected token '{Current.Value}' at position {Current.Position} in '{_context}'. " +
                $"Expected operand (identifier, literal, function call, or parenthesized expression).");
        }

        private void ParseCastFunction()
        {
            Advance(); // skip (
            ParseOperand();
            ExpectTokenType(TokenType.Keyword, "AS");
            if (!Current.Value.Equals("AS", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Expected AS in CAST at position {Current.Position} in '{_context}'.");
            Advance();
            if (Current.Type != TokenType.Identifier || !CastTypes.Contains(Current.Value))
                throw new InvalidOperationException(
                    $"Invalid CAST type '{Current.Value}' at position {Current.Position} in '{_context}'. " +
                    $"Allowed types: {string.Join(", ", CastTypes)}.");
            Advance();
            ExpectTokenType(TokenType.RParen, "')'");
            Advance();
        }

        private void ExpectTokenType(TokenType expected, string expectedDesc)
        {
            if (Current.Type != expected)
                throw new InvalidOperationException(
                    $"Unexpected token '{Current.Value}' at position {Current.Position} in '{_context}'. " +
                    $"Expected {expectedDesc}.");
        }
    }
}
