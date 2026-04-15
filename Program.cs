using System;
using System.IO;
using System.Collections.Generic;
using DanLang.Lexing;
using DanLang.Parsing;
using DanLang.Semantics;
using DanLang.CodeGen;

namespace DanLang;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("danlangc <source.dl> [-o out.asm] [--tokens] [--ast]");
            return 1;
        }

        string srcPath = args[0];
        string outPath = Path.ChangeExtension(srcPath, ".asm");
        bool dumpTokens = false, dumpAst = false;
        for (int i = 1; i < args.Length; i++)
        {
            if      (args[i] == "-o" && i + 1 < args.Length) outPath = args[++i];
            else if (args[i] == "--tokens") dumpTokens = true;
            else if (args[i] == "--ast")    dumpAst    = true;
        }

        string src;
        try { src = File.ReadAllText(srcPath); }
        catch (Exception ex)
        {
            Console.WriteLine("Не удалось прочитать файл: " + ex.Message);
            return 2;
        }

        // ── Стадия 1: лексический анализ ────────────────────────────────────
        var lexer  = new Lexer(src);
        var tokens = lexer.Tokenize();
        if (dumpTokens)
            foreach (var t in tokens) Console.WriteLine(t);

        // ── Стадия 2: синтаксический анализ ─────────────────────────────────
        var parser  = new Parser(tokens);
        var program = parser.ParseProgram();
        if (dumpAst) AstPrinter.Print(program);

        // ── Стадия 3: семантический анализ ──────────────────────────────────
        var sema = new SemanticAnalyzer();
        sema.Analyze(program);

        // ── Сбор всех ошибок со всех стадий ─────────────────────────────────
        var allErrors = new List<string>();
        allErrors.AddRange(lexer.Errors);
        allErrors.AddRange(parser.Errors);
        allErrors.AddRange(sema.Errors);

        if (allErrors.Count > 0)
        {
            Console.WriteLine($"\n=== Ошибки компиляции ({allErrors.Count}) ===");
            foreach (var e in allErrors)
                Console.WriteLine(e);
            return 3;
        }

        // ── Стадия 4: генерация кода ─────────────────────────────────────────
        try
        {
            var    gen = new MasmGenerator();
            string asm = gen.Generate(program, sema.SymbolTable);
            File.WriteAllText(outPath, asm);
            Console.WriteLine($"OK: {outPath}");
            return 0;
        }
        catch (CompilerException ex)
        {
            Console.WriteLine("\n=== Ошибки компиляции (1) ===");
            Console.WriteLine(ex.Message);
            return 3;
        }
    }
}

public class CompilerException : Exception
{
    public CompilerException(string msg) : base(msg) { }
}
