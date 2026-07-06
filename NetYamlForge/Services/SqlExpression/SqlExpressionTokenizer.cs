using System;
using System.Collections.Generic;
using System.Text;

namespace NetYamlForge.Services;

internal sealed class SqlExpressionTokenizer
{
    private readonly string _input;
    private readonly string _context;
    private int _pos;

    public SqlExpressionTokenizer(string input, string context)
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
