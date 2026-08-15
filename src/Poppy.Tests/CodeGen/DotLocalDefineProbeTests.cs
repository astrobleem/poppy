// Probe: the dot-local label DEFINITION with plain labels preceding.
using Poppy.Core.Arch;
using Poppy.Core.Lexer;
using Poppy.Core.Parser;
using Poppy.Core.Semantics;

namespace Poppy.Tests.CodeGen;

public sealed class DotLocalDefineProbeTests {
	private static SemanticAnalyzer Analyze(string source) {
		var lexer = new Poppy.Core.Lexer.Lexer(source, "test.pasm");
		var tokens = lexer.Tokenize();
		var parser = new Poppy.Core.Parser.Parser(tokens);
		var program = parser.Parse();
		var analyzer = new SemanticAnalyzer(TargetArchitecture.WDC65816);
		analyzer.Analyze(program);
		return analyzer;
	}

	[Fact]
	public void Probe_PlainLabelThenDotLabel_DefinesDot() {
		var analyzer = Analyze(@"
            .org $8000
l0:
            rts
.sib:
            rts
        ");
		Assert.True(analyzer.SymbolTable.Symbols.ContainsKey("l0"), "l0 missing");
		// dot-labels are SCOPED: after l0:, the .sib is stored as l0.sib
		Assert.True(analyzer.SymbolTable.Symbols.ContainsKey("l0.sib"), "l0.sib missing");
	}

	[Fact]
	public void Probe_OnlyDotLabel_DefinesDot() {
		var analyzer = Analyze(@"
            .org $8000
.sib:
            rts
        ");
		Assert.True(analyzer.SymbolTable.Symbols.ContainsKey(".sib"), ".sib missing");
	}

	[Fact]
	public void Probe_DotLabelMidFile_DefinesDot() {
		var analyzer = Analyze(@"
            .org $8000
l0:
            rts
.sib:
            rts
l1:
            rts
        ");
		Assert.True(analyzer.SymbolTable.Symbols.ContainsKey("l0"), "l0 missing");
		// scoped to the preceding plain label
		Assert.True(analyzer.SymbolTable.Symbols.ContainsKey("l0.sib"), "l0.sib missing");
		Assert.True(analyzer.SymbolTable.Symbols.ContainsKey("l1"), "l1 missing");
	}
}
