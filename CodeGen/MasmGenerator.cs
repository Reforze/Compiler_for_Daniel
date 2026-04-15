using System.Collections.Generic;
using System.Text;
using DanLang.Parsing;
using DanLang.Semantics;

namespace DanLang.CodeGen;

// Генератор MASM (x86, 32-бит) для языка DanLang.
// Самодостаточный вывод: использует только kernel32.lib
// (GetStdHandle / ReadConsoleA / WriteConsoleA / ExitProcess).
// Встроенные процедуры: DL_PrintInt (EAX -> stdout), DL_ReadInt (stdin -> EAX).
//
// Соглашения:
//   - результат любого выражения — в EAX
//   - переменные хранятся в сегменте .data с префиксом d_<имя>
//   - метки имеют формат DL_<тег>_<номер>
//   - для бинарных операций: левый операнд сохраняется в стеке, правый вычисляется в EAX
public sealed class MasmGenerator
{
    private readonly StringBuilder _data = new();
    private readonly StringBuilder _code = new();
    private int _labelCounter;

    // Уникальная метка вида DL_<тег>_<номер>
    private string NewLabel(string tag) => $"DL_{tag}_{_labelCounter++}";

    public string Generate(ProgramNode prog, SymbolTable st)
    {
        // Резервируем место под каждую переменную в .data
        foreach (var kv in st.Variables)
            _data.AppendLine($"    d_{kv.Key} DWORD 0");

        // Генерируем тело программы
        foreach (var s in prog.Statements) EmitStmt(s);

        return BuildOutput();
    }

    private string BuildOutput()
    {
        var sb = new StringBuilder();
        sb.AppendLine("; ============================================================");
        sb.AppendLine("; Сгенерировано компилятором DanLang -> MASM (x86, kernel32)");
        sb.AppendLine("; Сборка (MASM32 SDK / Visual Studio ML):");
        sb.AppendLine(";   ml /c /coff program.asm");
        sb.AppendLine(";   link /subsystem:console program.obj kernel32.lib");
        sb.AppendLine("; ============================================================");
        sb.AppendLine(".486");
        sb.AppendLine(".model flat, stdcall");
        sb.AppendLine("option casemap:none");
        sb.AppendLine(".stack 4096");
        sb.AppendLine();
        sb.AppendLine("ExitProcess    PROTO STDCALL :DWORD");
        sb.AppendLine("GetStdHandle   PROTO STDCALL :DWORD");
        sb.AppendLine("WriteConsoleA  PROTO STDCALL :DWORD, :DWORD, :DWORD, :DWORD, :DWORD");
        sb.AppendLine("ReadConsoleA   PROTO STDCALL :DWORD, :DWORD, :DWORD, :DWORD, :DWORD");
        sb.AppendLine("includelib kernel32.lib");
        sb.AppendLine();
        sb.AppendLine("STD_OUTPUT_HANDLE equ -11");
        sb.AppendLine("STD_INPUT_HANDLE  equ -10");
        sb.AppendLine();
        sb.AppendLine(".data");
        sb.AppendLine("    _hOut      DWORD 0");
        sb.AppendLine("    _hIn       DWORD 0");
        sb.AppendLine("    _nWritten  DWORD 0");
        sb.AppendLine("    _nRead     DWORD 0");
        sb.AppendLine("    _outBuf    BYTE  20 DUP(0)");
        sb.AppendLine("    _inBuf     BYTE  32 DUP(0)");
        sb.AppendLine("    _crlf      BYTE  13, 10");
        sb.Append(_data);
        sb.AppendLine();
        sb.AppendLine(".code");
        sb.AppendLine();
        sb.AppendLine(RuntimeAsm());
        sb.AppendLine("main PROC");
        sb.AppendLine("    invoke GetStdHandle, STD_OUTPUT_HANDLE");
        sb.AppendLine("    mov _hOut, eax");
        sb.AppendLine("    invoke GetStdHandle, STD_INPUT_HANDLE");
        sb.AppendLine("    mov _hIn, eax");
        sb.AppendLine();
        sb.Append(_code);
        sb.AppendLine("    invoke ExitProcess, 0");
        sb.AppendLine("main ENDP");
        sb.AppendLine("END main");
        return sb.ToString();
    }

    // Runtime-процедуры, встраиваемые в каждый файл
    private static string RuntimeAsm() => @"; --- DL_PrintInt: печатает знаковое 32-битное число из EAX ---
DL_PrintInt PROC
    pushad
    lea   edi, _outBuf
    add   edi, 19
    mov   byte ptr [edi], 0
    mov   ecx, 10
    xor   esi, esi          ; флаг знака (1 = отрицательное)
    cmp   eax, 0
    jge   DL_pi_loop
    neg   eax
    mov   esi, 1
DL_pi_loop:
    xor   edx, edx
    div   ecx
    add   dl, '0'
    dec   edi
    mov   [edi], dl
    test  eax, eax
    jnz   DL_pi_loop
    cmp   esi, 0
    je    DL_pi_write
    dec   edi
    mov   byte ptr [edi], '-'
DL_pi_write:
    lea   eax, _outBuf
    add   eax, 19
    sub   eax, edi          ; длина строки
    invoke WriteConsoleA, _hOut, edi, eax, ADDR _nWritten, 0
    invoke WriteConsoleA, _hOut, ADDR _crlf, 2, ADDR _nWritten, 0
    popad
    ret
DL_PrintInt ENDP

; --- DL_ReadInt: читает знаковое 32-битное число из stdin в EAX ---
DL_ReadInt PROC
    pushad
    invoke ReadConsoleA, _hIn, ADDR _inBuf, 31, ADDR _nRead, 0
    lea   esi, _inBuf
    xor   eax, eax
    xor   ebx, ebx          ; знак (1 = отрицательное)
    mov   cl, [esi]
    cmp   cl, '-'
    jne   DL_ri_loop
    mov   ebx, 1
    inc   esi
DL_ri_loop:
    mov   cl, [esi]
    cmp   cl, '0'
    jl    DL_ri_done
    cmp   cl, '9'
    jg    DL_ri_done
    sub   cl, '0'
    movzx ecx, cl
    imul  eax, eax, 10
    add   eax, ecx
    inc   esi
    jmp   DL_ri_loop
DL_ri_done:
    cmp   ebx, 0
    je    DL_ri_end
    neg   eax
DL_ri_end:
    mov   [esp + 28], eax   ; возвращаем через сохранённый pushad EAX
    popad
    ret
DL_ReadInt ENDP

";

    // ─── Генерация операторов ────────────────────────────────────────────────

    private void EmitStmt(Stmt s)
    {
        switch (s)
        {
            case VarDecl v:
                _code.AppendLine($"    ; --- объявление переменной {v.Name} ---");
                if (v.Init != null)
                {
                    EmitExpr(v.Init);
                    _code.AppendLine($"    mov d_{v.Name}, eax");
                }
                break;

            case Assign a:
                _code.AppendLine($"    ; --- присваивание {a.Name} ---");
                EmitExpr(a.Value);
                _code.AppendLine($"    mov d_{a.Name}, eax");
                break;

            case IfStmt i:
            {
                // if (expr) stmt1 [ else stmt2 ]
                string lElse = NewLabel("else");
                string lEnd  = NewLabel("endif");
                _code.AppendLine("    ; --- if ---");
                EmitExpr(i.Cond);
                _code.AppendLine("    cmp eax, 0");
                _code.AppendLine($"    je {(i.Else != null ? lElse : lEnd)}");
                EmitStmt(i.Then);
                if (i.Else != null)
                {
                    _code.AppendLine($"    jmp {lEnd}");
                    _code.AppendLine($"{lElse}:");
                    EmitStmt(i.Else);
                }
                _code.AppendLine($"{lEnd}:");
                break;
            }

            case WhileStmt w:
            {
                string lStart = NewLabel("while");
                string lEnd   = NewLabel("endwhile");
                _code.AppendLine("    ; --- while ---");
                _code.AppendLine($"{lStart}:");
                EmitExpr(w.Cond);
                _code.AppendLine("    cmp eax, 0");
                _code.AppendLine($"    je {lEnd}");
                EmitStmt(w.Body);
                _code.AppendLine($"    jmp {lStart}");
                _code.AppendLine($"{lEnd}:");
                break;
            }

            case Block b:
                foreach (var x in b.Stmts) EmitStmt(x);
                break;

            case OutputStmt o:
                _code.AppendLine("    ; --- output ---");
                EmitExpr(o.Value);
                _code.AppendLine("    call DL_PrintInt");
                break;

            case InputStmt r:
                _code.AppendLine($"    ; --- input({r.Name}) ---");
                _code.AppendLine("    call DL_ReadInt");
                _code.AppendLine($"    mov d_{r.Name}, eax");
                break;

            case ExprStmt es:
                EmitExpr(es.Value);
                break;
        }
    }

    // ─── Генерация выражений (результат -> EAX) ──────────────────────────────

    private void EmitExpr(Expr e)
    {
        switch (e)
        {
            case NumberLit n: _code.AppendLine($"    mov eax, {n.Value}"); break;
            case BoolLit b:   _code.AppendLine($"    mov eax, {(b.Value ? 1 : 0)}"); break;
            case VarRef v:    _code.AppendLine($"    mov eax, d_{v.Name}"); break;

            case Unary u:
                EmitExpr(u.Operand);
                if (u.Op == "-")
                {
                    _code.AppendLine("    neg eax");
                }
                else // "!"
                {
                    _code.AppendLine("    cmp eax, 0");
                    _code.AppendLine("    mov eax, 0");
                    _code.AppendLine("    sete al");
                }
                break;

            case Binary bi:
                EmitBinary(bi);
                break;
        }
    }

    private void EmitBinary(Binary b)
    {
        if (b.Op == "&&") { EmitLogicAnd(b); return; }
        if (b.Op == "||") { EmitLogicOr(b);  return; }

        // Общая схема: EAX = left; push; EAX = right; EBX = EAX; pop EAX
        EmitExpr(b.Left);
        _code.AppendLine("    push eax");
        EmitExpr(b.Right);
        _code.AppendLine("    mov ebx, eax");
        _code.AppendLine("    pop eax");         // eax = left, ebx = right

        switch (b.Op)
        {
            case "+": _code.AppendLine("    add eax, ebx"); break;
            case "-": _code.AppendLine("    sub eax, ebx"); break;
            case "*": _code.AppendLine("    imul eax, ebx"); break;
            case "/": _code.AppendLine("    cdq"); _code.AppendLine("    idiv ebx"); break;
            case "%": _code.AppendLine("    cdq"); _code.AppendLine("    idiv ebx");
                      _code.AppendLine("    mov eax, edx"); break;
            default:  EmitCmp(b.Op); break;
        }
    }

    private void EmitCmp(string op)
    {
        // eax = left, ebx = right (установлены до вызова)
        string setcc = op switch
        {
            "==" => "sete",
            "!=" => "setne",
            "<"  => "setl",
            "<=" => "setle",
            ">"  => "setg",
            ">=" => "setge",
            _    => throw new CompilerException($"Неизвестный оператор: {op}")
        };
        _code.AppendLine("    cmp eax, ebx");
        _code.AppendLine("    mov eax, 0");
        _code.AppendLine($"    {setcc} al");
    }

    // Ленивое вычисление &&: если левый ложный — сразу false
    private void EmitLogicAnd(Binary b)
    {
        string lFalse = NewLabel("and_no");
        string lEnd   = NewLabel("and_end");
        EmitExpr(b.Left);
        _code.AppendLine("    cmp eax, 0");
        _code.AppendLine($"    je {lFalse}");
        EmitExpr(b.Right);
        _code.AppendLine("    cmp eax, 0");
        _code.AppendLine($"    je {lFalse}");
        _code.AppendLine("    mov eax, 1");
        _code.AppendLine($"    jmp {lEnd}");
        _code.AppendLine($"{lFalse}:");
        _code.AppendLine("    mov eax, 0");
        _code.AppendLine($"{lEnd}:");
    }

    // Ленивое вычисление ||: если левый истинный — сразу true
    private void EmitLogicOr(Binary b)
    {
        string lTrue = NewLabel("or_yes");
        string lEnd  = NewLabel("or_end");
        EmitExpr(b.Left);
        _code.AppendLine("    cmp eax, 0");
        _code.AppendLine($"    jne {lTrue}");
        EmitExpr(b.Right);
        _code.AppendLine("    cmp eax, 0");
        _code.AppendLine($"    jne {lTrue}");
        _code.AppendLine("    mov eax, 0");
        _code.AppendLine($"    jmp {lEnd}");
        _code.AppendLine($"{lTrue}:");
        _code.AppendLine("    mov eax, 1");
        _code.AppendLine($"{lEnd}:");
    }
}
