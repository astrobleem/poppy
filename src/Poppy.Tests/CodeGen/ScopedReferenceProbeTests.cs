// Probe: the SCOPED dot-local reference — beq .sib inside a plain-label scope.
using Poppy.Core.Arch;
using Poppy.Core.CodeGen;
using Poppy.Core.Lexer;
using Poppy.Core.Parser;
using Poppy.Core.Semantics;

namespace Poppy.Tests.CodeGen;

public sealed class ScopedReferenceProbeTests {
	private static byte[] Compile(string source) {
		var lexer = new Poppy.Core.Lexer.Lexer(source, "test.pasm");
		var tokens = lexer.Tokenize();
		var parser = new Poppy.Core.Parser.Parser(tokens);
		var program = parser.Parse();
		var analyzer = new SemanticAnalyzer(TargetArchitecture.WDC65816);
		analyzer.Analyze(program);
		Assert.False(analyzer.HasErrors,
			$"Semantic errors: {string.Join("; ", analyzer.Errors.Select(e => e.Message))}");
		var generator = new CodeGenerator(analyzer, TargetArchitecture.WDC65816);
		var code = generator.Generate(program);
		Assert.False(generator.HasErrors,
			$"Codegen errors: {string.Join("; ", generator.Errors.Select(e => e.Message))}");
		return code;
	}

	[Fact]
	public void Probe_ScopedReference_Emits() {
		// l0: defines the scope; .sib is stored as l0.sib; the beq .sib
		// inside the scope must resolve. Layout: lda (2) beq (2) nop (1)
		// -> .sib at $8005; the beq at $8002, next $8004, offset 1 (f0 01).
		var source = @"
            .org $8000
l0:
            lda $02
            beq .sib
            nop
.sib:
            rts
        ";
		var code = Compile(source);
		Assert.Equal(new byte[] { 0xa5, 0x02, 0xf0, 0x01, 0xea, 0x60 }, code);
	}

	[Fact]
	public void Probe_ScopedReference_SymbolExists() {
		var source = @"
            .org $8000
l0:
            lda $02
            beq .sib
            nop
.sib:
            rts
        ";
		var lexer = new Poppy.Core.Lexer.Lexer(source, "test.pasm");
		var tokens = lexer.Tokenize();
		var parser = new Poppy.Core.Parser.Parser(tokens);
		var program = parser.Parse();
		var analyzer = new SemanticAnalyzer(TargetArchitecture.WDC65816);
		analyzer.Analyze(program);
		Assert.True(analyzer.SymbolTable.Symbols.ContainsKey("l0.sib"),
			"l0.sib missing; keys: " + string.Join(",", analyzer.SymbolTable.Symbols.Keys));
	}

	[Fact]
	public void Probe_MultiScope_ReferencesResolve() {
		// Two scopes; func_a's .done_a reference must resolve against
		// func_a (the codegen's VisitLabel scope tracking), not the
		// whole-program final scope (func_b). Both beqs land at offset 1.
		var source = @"
            .org $8000
func_a:
            lda $02
            beq .done_a
            nop
.done_a:
            rts
func_b:
            lda $02
            beq .done_b
            nop
.done_b:
            rts
        ";
		var code = Compile(source);
		Assert.Equal(new byte[] {
			0xa5, 0x02, 0xf0, 0x01, 0xea, 0x60,
			0xa5, 0x02, 0xf0, 0x01, 0xea, 0x60,
		}, code);
	}
}
