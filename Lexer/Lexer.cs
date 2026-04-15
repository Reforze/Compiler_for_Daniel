using System.Collections.Generic;

namespace DanLang.Lexing;

// Лексер языка DanLang.
// Поддерживает однострочные (// ...) и многострочные (/* ... */) комментарии.
// При неизвестном символе не бросает исключение, а добавляет ошибку в список
// и возвращает токен Error — это позволяет продолжить анализ и собрать все ошибки.
public sealed class Lexer
{
    private readonly string _src;
    private int _pos, _line = 1, _col = 1;

    private readonly List<string> _errors = new();
    public IReadOnlyList<string> Errors => _errors;

    private static readonly Dictionary<string, TokenKind> Keywords = new()
    {
        ["int"]    = TokenKind.KwInt,
        ["if"]     = TokenKind.KwIf,
        ["else"]   = TokenKind.KwElse,
        ["while"]  = TokenKind.KwWhile,
        ["output"] = TokenKind.KwOutput,
        ["input"]  = TokenKind.KwInput,
        ["true"]   = TokenKind.KwTrue,
        ["false"]  = TokenKind.KwFalse,
    };

    public Lexer(string src) => _src = src;

    private char Cur  => _pos     < _src.Length ? _src[_pos]     : '\0';
    private char Next => _pos + 1 < _src.Length ? _src[_pos + 1] : '\0';

    private void Advance()
    {
        if (_pos < _src.Length)
        {
            if (_src[_pos] == '\n') { _line++; _col = 1; } else _col++;
            _pos++;
        }
    }

    private Token Make(TokenKind k, string lex, int line, int col) => new(k, lex, line, col);

    private Token OneChar(TokenKind k)
    {
        int l = _line, c = _col; char ch = Cur;
        Advance();
        return Make(k, ch.ToString(), l, c);
    }

    private Token TwoChar(TokenKind k, string lex)
    {
        int l = _line, c = _col;
        Advance(); Advance();
        return Make(k, lex, l, c);
    }

    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();
        while (true)
        {
            SkipWhitespaceAndComments();
            if (_pos >= _src.Length) { tokens.Add(Make(TokenKind.Eof, "", _line, _col)); break; }
            tokens.Add(ReadToken());
        }
        return tokens;
    }

    private void SkipWhitespaceAndComments()
    {
        while (_pos < _src.Length)
        {
            if (char.IsWhiteSpace(Cur)) { Advance(); continue; }

            // однострочный комментарий
            if (Cur == '/' && Next == '/')
            {
                while (_pos < _src.Length && Cur != '\n') Advance();
                continue;
            }

            // многострочный комментарий
            if (Cur == '/' && Next == '*')
            {
                int startLine = _line, startCol = _col;
                Advance(); Advance();
                while (_pos < _src.Length && !(Cur == '*' && Next == '/')) Advance();
                if (_pos < _src.Length) { Advance(); Advance(); }
                else _errors.Add($"[{startLine}:{startCol}] Лексическая ошибка: незакрытый комментарий /*");
                continue;
            }
            break;
        }
    }

    private Token ReadToken()
    {
        char c = Cur, n = Next;
        int line = _line, col = _col;

        if (char.IsLetter(c) || c == '_') return ReadIdent();
        if (char.IsDigit(c))              return ReadNumber();

        return c switch
        {
            '+' => OneChar(TokenKind.Plus),
            '-' => OneChar(TokenKind.Minus),
            '*' => OneChar(TokenKind.Star),
            '/' => OneChar(TokenKind.Slash),
            '%' => OneChar(TokenKind.Percent),
            '(' => OneChar(TokenKind.LParen),
            ')' => OneChar(TokenKind.RParen),
            '{' => OneChar(TokenKind.LBrace),
            '}' => OneChar(TokenKind.RBrace),
            ';' => OneChar(TokenKind.Semicolon),
            '=' => n == '=' ? TwoChar(TokenKind.Eq,         "==") : OneChar(TokenKind.Assign),
            '!' => n == '=' ? TwoChar(TokenKind.NotEq,      "!=") : OneChar(TokenKind.Not),
            '<' => n == '=' ? TwoChar(TokenKind.LessEq,     "<=") : OneChar(TokenKind.Less),
            '>' => n == '=' ? TwoChar(TokenKind.GreaterEq,  ">=") : OneChar(TokenKind.Greater),
            '&' => n == '&' ? TwoChar(TokenKind.AndAnd,     "&&") : BadChar(c, line, col, "одиночный '&' — ожидалось '&&'"),
            '|' => n == '|' ? TwoChar(TokenKind.OrOr,       "||") : BadChar(c, line, col, "одиночный '|' — ожидалось '||'"),
            _   => BadChar(c, line, col),
        };
    }

    private Token BadChar(char c, int line, int col, string? hint = null)
    {
        string msg = $"[{line}:{col}] Лексическая ошибка: неизвестный символ '{c}'";
        if (hint != null) msg += $" ({hint})";
        _errors.Add(msg);
        Advance();
        return new Token(TokenKind.Error, c.ToString(), line, col);
    }

    private Token ReadIdent()
    {
        int line = _line, col = _col, start = _pos;
        while (_pos < _src.Length && (char.IsLetterOrDigit(Cur) || Cur == '_')) Advance();
        string lex = _src[start.._pos];
        var kind = Keywords.TryGetValue(lex, out var kw) ? kw : TokenKind.Ident;
        return Make(kind, lex, line, col);
    }

    private Token ReadNumber()
    {
        int line = _line, col = _col, start = _pos;
        while (_pos < _src.Length && char.IsDigit(Cur)) Advance();
        string lex = _src[start.._pos];
        return new Token(TokenKind.Number, lex, line, col) { NumValue = int.Parse(lex) };
    }
}
