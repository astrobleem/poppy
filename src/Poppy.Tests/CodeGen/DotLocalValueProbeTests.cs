// Probe: the dot-local label's VALUE vs the actual layout position.
using Poppy.Core.Arch;
using Poppy.Core.Lexer;
using Poppy.Core.Parser;
using Poppy.Core.Semantics;

namespace Poppy.Tests.CodeGen;

public sealed class DotLocalValueProbeTests {
	[Fact]
	public void Probe_DotLocalLabel_Value() {
		var source = @"
            .org $8000
            lda $02
            beq .sib
            dec $0E
            dec $0E
.sib:
            rts
        ";
		var lexer = new Poppy.Core.Lexer.Lexer(source, "test.pasm");
		var tokens = lexer.Tokenize();
		var parser = new Poppy.Core.Parser.Parser(tokens);
		var program = parser.Parse();

		var analyzer = new SemanticAnalyzer(TargetArchitecture.WDC65816);
		analyzer.Analyze(program);

		var sib = analyzer.SymbolTable.Symbols[".sib"].Value;
		// True position: $8000 + 2 (lda) + 2 (beq) + 2 (dec $0E) + 2 (dec $0E)
		// = $8008 — the zero-page dec is the 2-byte c6 0e, not 1 byte.
		Assert.True(sib.HasValue, ".sib has no value");
		Assert.Equal(0x8008, sib.Value);
	}

	[Fact]
	public void Probe_DotLocalLabel_WithSizes() {
		// The same program, but with a plain label after each instruction to
		// observe the layout's per-instruction addresses.
		var source = @"
            .org $8000
l0:
            lda $02
l1:
            beq .sib
l2:
            dec $0E
l3:
            dec $0E
l4:
            rts
.sib:
            rts
        ";
		var lexer = new Poppy.Core.Lexer.Lexer(source, "test.pasm");
		var tokens = lexer.Tokenize();
		var parser = new Poppy.Core.Parser.Parser(tokens);
		var program = parser.Parse();

		var analyzer = new SemanticAnalyzer(TargetArchitecture.WDC65816);
		analyzer.Analyze(program);

		var syms = analyzer.SymbolTable.Symbols;
		Assert.Equal(0x8000, syms["l0"].Value);
		Assert.Equal(0x8002, syms["l1"].Value);  // lda = 2
		Assert.Equal(0x8004, syms["l2"].Value);  // beq = 2
		Assert.Equal(0x8006, syms["l3"].Value);  // dec $0E = 2 (c6 0e)
		Assert.Equal(0x8008, syms["l4"].Value);  // dec $0E = 2 (c6 0e)
	}
}
