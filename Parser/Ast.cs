using System.Collections.Generic;

namespace DanLang.Parsing;

// Базовые узлы AST
public abstract record Node(int Line, int Col);
public abstract record Stmt(int Line, int Col) : Node(Line, Col);
public abstract record Expr(int Line, int Col) : Node(Line, Col);

// Операторы
public sealed record ProgramNode(List<Stmt> Statements, int Line, int Col) : Node(Line, Col);
public sealed record VarDecl(string Name, Expr? Init, int Line, int Col)    : Stmt(Line, Col);
public sealed record Assign(string Name, Expr Value, int Line, int Col)      : Stmt(Line, Col);
public sealed record IfStmt(Expr Cond, Stmt Then, Stmt? Else, int Line, int Col) : Stmt(Line, Col);
public sealed record WhileStmt(Expr Cond, Stmt Body, int Line, int Col)      : Stmt(Line, Col);
public sealed record Block(List<Stmt> Stmts, int Line, int Col)              : Stmt(Line, Col);
public sealed record OutputStmt(Expr Value, int Line, int Col)               : Stmt(Line, Col);
public sealed record InputStmt(string Name, int Line, int Col)               : Stmt(Line, Col);
public sealed record ExprStmt(Expr Value, int Line, int Col)                 : Stmt(Line, Col);

// Выражения
public sealed record NumberLit(int Value, int Line, int Col)                       : Expr(Line, Col);
public sealed record BoolLit(bool Value, int Line, int Col)                        : Expr(Line, Col);
public sealed record VarRef(string Name, int Line, int Col)                        : Expr(Line, Col);
public sealed record Binary(string Op, Expr Left, Expr Right, int Line, int Col)   : Expr(Line, Col);
public sealed record Unary(string Op, Expr Operand, int Line, int Col)             : Expr(Line, Col);
