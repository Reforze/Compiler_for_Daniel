namespace DanLang.Lexing;

// Виды токенов языка DanLang
public enum TokenKind
{
    Error,           // неизвестный символ (лексическая ошибка)
    Number, Ident,
    KwInt, KwIf, KwElse, KwWhile, KwOutput, KwInput, KwTrue, KwFalse,
    Plus, Minus, Star, Slash, Percent, Assign,
    Eq, NotEq, Less, LessEq, Greater, GreaterEq,
    AndAnd, OrOr, Not,
    LParen, RParen, LBrace, RBrace, Semicolon,
    Eof
}

public record Token(TokenKind Kind, string Lexeme, int Line, int Col)
{
    public int NumValue { get; init; }
    public override string ToString() => $"[{Line}:{Col}] {Kind,-12} '{Lexeme}'";
}
