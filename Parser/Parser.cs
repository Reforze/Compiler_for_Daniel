using System.Collections.Generic;
using DanLang.Lexing;

namespace DanLang.Parsing;

// Грамматика DanLang (EBNF):
//   program      = { statement } ;
//   statement    = var_decl | assign | if_stmt | while_stmt | block | output_stmt | input_stmt | expr_stmt ;
//   var_decl     = "int" IDENT [ "=" expression ] ";" ;
//   assign       = IDENT "=" expression ";" ;
//   if_stmt      = "if" "(" expression ")" statement [ "else" statement ] ;
//   while_stmt   = "while" "(" expression ")" statement ;
//   block        = "{" { statement } "}" ;
//   output_stmt  = "output" "(" expression ")" ";" ;
//   input_stmt   = "input" "(" IDENT ")" ";" ;
//   expr_stmt    = expression ";" ;
//   expression   = logic_or ;
//   logic_or     = logic_and { "||" logic_and } ;
//   logic_and    = equality  { "&&" equality } ;
//   equality     = relational { ("=="|"!=") relational } ;
//   relational   = additive   { ("<"|"<="|">"|">=") additive } ;
//   additive     = term       { ("+"|"-") term } ;
//   term         = unary      { ("*"|"/"|"%") unary } ;
//   unary        = ("-"|"!") unary | primary ;
//   primary      = NUMBER | "true" | "false" | IDENT | "(" expression ")" ;

public sealed class Parser
{
    private readonly List<Token> _tokens;
    private int _pos;

    private readonly List<string> _errors = new();
    public IReadOnlyList<string> Errors => _errors;

    public Parser(List<Token> tokens) { _tokens = tokens; }

    private Token Peek(int k = 0) => _tokens[_pos + k];
    private Token Advance() => _tokens[_pos++];
    private bool Check(TokenKind k) => Peek().Kind == k;
    private bool Match(TokenKind k) { if (Check(k)) { _pos++; return true; } return false; }

    private Token Expect(TokenKind k, string what)
    {
        if (Check(k)) return Advance();
        var t = Peek();
        throw new CompilerException(
            $"строка {t.Line}, столбец {t.Col}: Синтаксическая ошибка: ожидалось {what}, получено '{t.Lexeme}'");
    }

    // Разбор всей программы с восстановлением после ошибок
    public ProgramNode ParseProgram()
    {
        var stmts = new List<Stmt>();
        int l = Peek().Line, c = Peek().Col;
        while (!Check(TokenKind.Eof))
        {
            // Пропускаем одиночные ошибочные токены или лишние '}' на верхнем уровне
            if (Check(TokenKind.Error) || Check(TokenKind.RBrace)) { Advance(); continue; }
            try
            {
                stmts.Add(ParseStatement());
            }
            catch (CompilerException ex)
            {
                _errors.Add(ex.Message);
                Synchronize();
            }
        }
        return new ProgramNode(stmts, l, c);
    }

    // Восстановление: пропускаем токены до ближайшей границы оператора
    private void Synchronize()
    {
        while (!Check(TokenKind.Eof))
        {
            var k = Peek().Kind;
            if (k == TokenKind.Semicolon) { Advance(); return; }
            if (k == TokenKind.RBrace)    { Advance(); return; } // потребляем '}', иначе зависнем
            if (k == TokenKind.KwInt    || k == TokenKind.KwIf     ||
                k == TokenKind.KwWhile  || k == TokenKind.KwOutput ||
                k == TokenKind.KwInput) return;
            Advance();
        }
    }

    private Stmt ParseStatement()
    {
        var t = Peek();
        return t.Kind switch
        {
            TokenKind.KwInt    => ParseVarDecl(),
            TokenKind.KwIf     => ParseIf(),
            TokenKind.KwWhile  => ParseWhile(),
            TokenKind.LBrace   => ParseBlock(),
            TokenKind.KwOutput => ParseOutput(),
            TokenKind.KwInput  => ParseInput(),
            TokenKind.Ident when Peek(1).Kind == TokenKind.Assign => ParseAssign(),
            _                  => ParseExprStmt(),
        };
    }

    private Stmt ParseVarDecl()
    {
        var kw = Expect(TokenKind.KwInt, "'int'");
        var id = Expect(TokenKind.Ident, "идентификатор");
        Expr? init = null;
        if (Match(TokenKind.Assign)) init = ParseExpression();
        Expect(TokenKind.Semicolon, "';'");
        return new VarDecl(id.Lexeme, init, kw.Line, kw.Col);
    }

    private Stmt ParseAssign()
    {
        var id = Expect(TokenKind.Ident, "идентификатор");
        Expect(TokenKind.Assign, "'='");
        var val = ParseExpression();
        Expect(TokenKind.Semicolon, "';'");
        return new Assign(id.Lexeme, val, id.Line, id.Col);
    }

    // if (expr) stmt1 [ else stmt2 ]
    private Stmt ParseIf()
    {
        var kw = Expect(TokenKind.KwIf, "'if'");
        Expect(TokenKind.LParen, "'(' после 'if'");
        var cond = ParseExpression();
        Expect(TokenKind.RParen, "')' после условия if");
        var thenS = ParseStatement();
        Stmt? elseS = null;
        if (Match(TokenKind.KwElse)) elseS = ParseStatement();
        return new IfStmt(cond, thenS, elseS, kw.Line, kw.Col);
    }

    private Stmt ParseWhile()
    {
        var kw = Expect(TokenKind.KwWhile, "'while'");
        Expect(TokenKind.LParen, "'(' после 'while'");
        var cond = ParseExpression();
        Expect(TokenKind.RParen, "')' после условия while");
        var body = ParseStatement();
        return new WhileStmt(cond, body, kw.Line, kw.Col);
    }

    private Stmt ParseBlock()
    {
        var br = Expect(TokenKind.LBrace, "'{'");
        var list = new List<Stmt>();
        while (!Check(TokenKind.RBrace) && !Check(TokenKind.Eof))
        {
            try { list.Add(ParseStatement()); }
            catch (CompilerException ex) { _errors.Add(ex.Message); Synchronize(); }
        }
        Expect(TokenKind.RBrace, "'}'");
        return new Block(list, br.Line, br.Col);
    }

    private Stmt ParseOutput()
    {
        var kw = Expect(TokenKind.KwOutput, "'output'");
        Expect(TokenKind.LParen, "'('");
        var e = ParseExpression();
        Expect(TokenKind.RParen, "')'");
        Expect(TokenKind.Semicolon, "';'");
        return new OutputStmt(e, kw.Line, kw.Col);
    }

    private Stmt ParseInput()
    {
        var kw = Expect(TokenKind.KwInput, "'input'");
        Expect(TokenKind.LParen, "'('");
        var id = Expect(TokenKind.Ident, "идентификатор");
        Expect(TokenKind.RParen, "')'");
        Expect(TokenKind.Semicolon, "';'");
        return new InputStmt(id.Lexeme, kw.Line, kw.Col);
    }

    private Stmt ParseExprStmt()
    {
        var t = Peek();
        var e = ParseExpression();
        Expect(TokenKind.Semicolon, "';'");
        return new ExprStmt(e, t.Line, t.Col);
    }

    // ─── Выражения ─────────────────────────────────────────────────────────────

    private Expr ParseExpression() => ParseLogicOr();

    private Expr ParseLogicOr()
    {
        var left = ParseLogicAnd();
        while (Check(TokenKind.OrOr))
        { var op = Advance(); var r = ParseLogicAnd(); left = new Binary("||", left, r, op.Line, op.Col); }
        return left;
    }

    private Expr ParseLogicAnd()
    {
        var left = ParseEquality();
        while (Check(TokenKind.AndAnd))
        { var op = Advance(); var r = ParseEquality(); left = new Binary("&&", left, r, op.Line, op.Col); }
        return left;
    }

    private Expr ParseEquality()
    {
        var left = ParseRelational();
        while (Check(TokenKind.Eq) || Check(TokenKind.NotEq))
        { var op = Advance(); var r = ParseRelational(); left = new Binary(op.Lexeme, left, r, op.Line, op.Col); }
        return left;
    }

    private Expr ParseRelational()
    {
        var left = ParseAdditive();
        while (Check(TokenKind.Less)    || Check(TokenKind.LessEq) ||
               Check(TokenKind.Greater) || Check(TokenKind.GreaterEq))
        { var op = Advance(); var r = ParseAdditive(); left = new Binary(op.Lexeme, left, r, op.Line, op.Col); }
        return left;
    }

    private Expr ParseAdditive()
    {
        var left = ParseTerm();
        while (Check(TokenKind.Plus) || Check(TokenKind.Minus))
        { var op = Advance(); var r = ParseTerm(); left = new Binary(op.Lexeme, left, r, op.Line, op.Col); }
        return left;
    }

    private Expr ParseTerm()
    {
        var left = ParseUnary();
        while (Check(TokenKind.Star) || Check(TokenKind.Slash) || Check(TokenKind.Percent))
        { var op = Advance(); var r = ParseUnary(); left = new Binary(op.Lexeme, left, r, op.Line, op.Col); }
        return left;
    }

    private Expr ParseUnary()
    {
        if (Check(TokenKind.Minus) || Check(TokenKind.Not))
        { var op = Advance(); var e = ParseUnary(); return new Unary(op.Lexeme, e, op.Line, op.Col); }
        return ParsePrimary();
    }

    private Expr ParsePrimary()
    {
        var t = Peek();
        switch (t.Kind)
        {
            case TokenKind.Number:  Advance(); return new NumberLit(t.NumValue, t.Line, t.Col);
            case TokenKind.KwTrue:  Advance(); return new BoolLit(true,  t.Line, t.Col);
            case TokenKind.KwFalse: Advance(); return new BoolLit(false, t.Line, t.Col);
            case TokenKind.Ident:   Advance(); return new VarRef(t.Lexeme, t.Line, t.Col);
            case TokenKind.LParen:
                Advance();
                var e = ParseExpression();
                Expect(TokenKind.RParen, "')'");
                return e;
            case TokenKind.Error:
                // Ошибочный символ уже записан лексером — потребляем и возвращаем 0
                Advance();
                return new NumberLit(0, t.Line, t.Col);
            default:
                throw new CompilerException(
                    $"строка {t.Line}, столбец {t.Col}: Синтаксическая ошибка: ожидалось выражение, получено '{t.Lexeme}'");
        }
    }
}

// Печать AST для отладки (--ast)
public static class AstPrinter
{
    public static void Print(ProgramNode p)
    {
        foreach (var s in p.Statements) PrintStmt(s, 0);
    }

    private static void PrintStmt(Stmt s, int ind)
    {
        var pad = new string(' ', ind * 2);
        switch (s)
        {
            case VarDecl v:
                System.Console.WriteLine($"{pad}VarDecl {v.Name}");
                if (v.Init != null) PrintExpr(v.Init, ind + 1);
                break;
            case Assign a:
                System.Console.WriteLine($"{pad}Assign {a.Name}");
                PrintExpr(a.Value, ind + 1);
                break;
            case IfStmt i:
                System.Console.WriteLine($"{pad}If");
                PrintExpr(i.Cond, ind + 1);
                System.Console.WriteLine($"{pad}  Then:"); PrintStmt(i.Then, ind + 2);
                if (i.Else != null) { System.Console.WriteLine($"{pad}  Else:"); PrintStmt(i.Else, ind + 2); }
                break;
            case WhileStmt w:
                System.Console.WriteLine($"{pad}While");
                PrintExpr(w.Cond, ind + 1); PrintStmt(w.Body, ind + 1);
                break;
            case Block b:
                System.Console.WriteLine($"{pad}Block");
                foreach (var x in b.Stmts) PrintStmt(x, ind + 1);
                break;
            case OutputStmt o:
                System.Console.WriteLine($"{pad}Output"); PrintExpr(o.Value, ind + 1);
                break;
            case InputStmt r:
                System.Console.WriteLine($"{pad}Input {r.Name}");
                break;
            case ExprStmt es:
                System.Console.WriteLine($"{pad}ExprStmt"); PrintExpr(es.Value, ind + 1);
                break;
        }
    }

    private static void PrintExpr(Expr e, int ind)
    {
        var pad = new string(' ', ind * 2);
        switch (e)
        {
            case NumberLit n: System.Console.WriteLine($"{pad}Num {n.Value}"); break;
            case BoolLit b:   System.Console.WriteLine($"{pad}Bool {b.Value}"); break;
            case VarRef v:    System.Console.WriteLine($"{pad}Var {v.Name}"); break;
            case Binary bi:
                System.Console.WriteLine($"{pad}Bin {bi.Op}");
                PrintExpr(bi.Left, ind + 1); PrintExpr(bi.Right, ind + 1);
                break;
            case Unary u:
                System.Console.WriteLine($"{pad}Un {u.Op}"); PrintExpr(u.Operand, ind + 1);
                break;
        }
    }
}
