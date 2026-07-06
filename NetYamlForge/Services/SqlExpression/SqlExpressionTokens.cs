namespace NetYamlForge.Services;

internal enum TokenType
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

internal sealed record Token(TokenType Type, string Value, int Position);
