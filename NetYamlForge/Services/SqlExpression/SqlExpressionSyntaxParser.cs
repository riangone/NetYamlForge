using System;
using System.Collections.Generic;

namespace NetYamlForge.Services;

internal sealed class SqlExpressionSyntaxParser
{
    private readonly List<Token> _tokens;
    private readonly string _context;
    private int _pos;

    public SqlExpressionSyntaxParser(List<Token> tokens, string context)
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
                if (!SqlExpressionParser.AllowedFunctions.Contains(name))
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
        if (Current.Type != TokenType.Identifier || !SqlExpressionParser.CastTypes.Contains(Current.Value))
            throw new InvalidOperationException(
                $"Invalid CAST type '{Current.Value}' at position {Current.Position} in '{_context}'. " +
                $"Allowed types: {string.Join(", ", SqlExpressionParser.CastTypes)}.");
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
