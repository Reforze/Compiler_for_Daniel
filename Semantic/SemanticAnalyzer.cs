using System.Collections.Generic;
using DanLang.Parsing;

namespace DanLang.Semantics;

// Типы данных языка DanLang
public enum DanType { Int, Bool }

// Запись о переменной в таблице символов
public sealed class VariableInfo
{
    public string Name { get; }
    public DanType Type { get; }
    public VariableInfo(string name, DanType type) { Name = name; Type = type; }
}

// Таблица символов (плоская — без вложенных областей видимости)
public sealed class SymbolTable
{
    private readonly Dictionary<string, VariableInfo> _vars = new();

    public IReadOnlyDictionary<string, VariableInfo> Variables => _vars;

    public bool TryDeclare(string name, DanType type)
    {
        if (_vars.ContainsKey(name)) return false;
        _vars[name] = new VariableInfo(name, type);
        return true;
    }

    public VariableInfo? Lookup(string name) =>
        _vars.TryGetValue(name, out var v) ? v : null;
}

// Семантический анализатор: проверка объявлений и типов.
// Собирает все ошибки вместо остановки на первой.
public sealed class SemanticAnalyzer
{
    public SymbolTable SymbolTable { get; } = new();

    private readonly List<string> _errors = new();
    public IReadOnlyList<string> Errors => _errors;

    public void Analyze(ProgramNode p)
    {
        foreach (var s in p.Statements)
        {
            try { CheckStmt(s); }
            catch (CompilerException ex) { _errors.Add(ex.Message); }
        }
    }

    private void CheckStmt(Stmt s)
    {
        switch (s)
        {
            case VarDecl v:
                if (!SymbolTable.TryDeclare(v.Name, DanType.Int))
                    throw new CompilerException(
                        $"[{v.Line}:{v.Col}] Семантическая ошибка: переменная '{v.Name}' уже объявлена");
                if (v.Init != null) CheckExpr(v.Init);
                break;

            case Assign a:
                if (SymbolTable.Lookup(a.Name) == null)
                    throw new CompilerException(
                        $"[{a.Line}:{a.Col}] Семантическая ошибка: переменная '{a.Name}' не объявлена");
                CheckExpr(a.Value);
                break;

            case IfStmt i:
                CheckExpr(i.Cond);
                CheckStmt(i.Then);
                if (i.Else != null) CheckStmt(i.Else);
                break;

            case WhileStmt w:
                CheckExpr(w.Cond);
                CheckStmt(w.Body);
                break;

            case Block b:
                foreach (var x in b.Stmts)
                {
                    try { CheckStmt(x); }
                    catch (CompilerException ex) { _errors.Add(ex.Message); }
                }
                break;

            case OutputStmt o:
                CheckExpr(o.Value);
                break;

            case InputStmt r:
                if (SymbolTable.Lookup(r.Name) == null)
                    throw new CompilerException(
                        $"[{r.Line}:{r.Col}] Семантическая ошибка: переменная '{r.Name}' не объявлена");
                break;

            case ExprStmt e:
                CheckExpr(e.Value);
                break;
        }
    }

    private DanType CheckExpr(Expr e)
    {
        switch (e)
        {
            case NumberLit: return DanType.Int;
            case BoolLit:   return DanType.Bool;

            case VarRef v:
                var info = SymbolTable.Lookup(v.Name) ??
                    throw new CompilerException(
                        $"[{v.Line}:{v.Col}] Семантическая ошибка: переменная '{v.Name}' не объявлена");
                return info.Type;

            case Unary u:
                var ut = CheckExpr(u.Operand);
                if (u.Op == "-" && ut != DanType.Int)
                    throw new CompilerException($"[{u.Line}:{u.Col}] Унарный '-' применим только к int");
                return u.Op == "!" ? DanType.Bool : DanType.Int;

            case Binary b:
                var lt = CheckExpr(b.Left);
                var rt = CheckExpr(b.Right);
                switch (b.Op)
                {
                    case "+": case "-": case "*": case "/": case "%":
                        if (lt != DanType.Int || rt != DanType.Int)
                            throw new CompilerException(
                                $"[{b.Line}:{b.Col}] Арифметический оператор '{b.Op}' требует операнды типа int");
                        return DanType.Int;
                    case "<": case "<=": case ">": case ">=":
                        if (lt != DanType.Int || rt != DanType.Int)
                            throw new CompilerException(
                                $"[{b.Line}:{b.Col}] Оператор сравнения '{b.Op}' требует операнды типа int");
                        return DanType.Bool;
                    case "==": case "!=":
                        if (lt != rt)
                            throw new CompilerException(
                                $"[{b.Line}:{b.Col}] Оператор '{b.Op}': несовместимые типы ({lt} и {rt})");
                        return DanType.Bool;
                    case "&&": case "||":
                        // допускаем int как логическое значение (0=false, иное=true), как в C
                        return DanType.Bool;
                }
                break;
        }
        throw new CompilerException("Неизвестный тип выражения");
    }
}
