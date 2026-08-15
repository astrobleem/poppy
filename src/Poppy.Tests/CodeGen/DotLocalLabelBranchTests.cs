// Poppy Compiler - Dot-Local-Label Branch Regression Tests
// Copyright © 2026
//
// Regression coverage for the G8 blktiger discovery: a branch instruction
// (beq/bne/bra/...) whose target is a dot-prefixed LOCAL label (e.g.
// "beq .sib") was emitting NO bytes at all — the branch vanished, leaving
// only the fall-through. The z80_impl.pasm has 649 dot-local branch sites,
// so the assembled interpreter was silently missing its ALU/flag/control
// branches. Plain labels assemble correctly; the dot-prefixed form dropped
// the entire instruction.

using Poppy.Core.Arch;
using Poppy.Core.CodeGen;
using Poppy.Core.Lexer;
using Poppy.Core.Parser;
using Poppy.Core.Semantics;

namespace Poppy.Tests.CodeGen;

public sealed class DotLocalLabelBranchTests {
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
	public void BranchToDotLocalLabel_EmitsBranchBytes() {
		// The beq .sib must emit f0 04 (opcode + relative offset); the
		// regression drops the branch entirely (a5 02 c6 0e c6 0e 60).
		var source = @"
            .org $8000
            lda $02
            beq .sib
            dec $0E
            dec $0E
.sib:
            rts
        ";
		var code = Compile(source);
		Assert.Equal(
			new byte[] { 0xa5, 0x02, 0xf0, 0x04, 0xc6, 0x0e, 0xc6, 0x0e, 0x60 },
			code);
	}

	[Fact]
	public void BranchToDotLocalLabel_Backward_EmitsBranchBytes() {
		// Backward dot-local branch must also emit the branch.
		var source = @"
            .org $8000
.sib:
            lda $02
            beq .sib
            rts
        ";
		var code = Compile(source);
		// beq .sib (backward to $8000): offset = $8000 - $8004 = -4 = fc
		Assert.Equal(new byte[] { 0xa5, 0x02, 0xf0, 0xfc, 0x60 }, code);
	}

	[Fact]
	public void BranchToDotLocalLabel_AfterModeChange_EmitsBranchBytes() {
		// The dot-local branch after a sep/rep mode change (the z80_indr
		// shape: sep #$20; lda $02; beq .done; rep #$20; dec $0E). The
		// zero-page dec is 2 bytes (c6 0e), so .done = $800A and the beq
		// at $8004 has offset $800A - $8006 = 4 (f0 04).
		var source = @"
            .org $8000
            sep #$20
            lda $02
            beq .done
            rep #$20
            dec $0E
.done:
            rts
        ";
		var code = Compile(source);
		Assert.Equal(
			new byte[] { 0xe2, 0x20, 0xa5, 0x02, 0xf0, 0x04, 0xc2, 0x20, 0xc6, 0x0e, 0x60 },
			code);
	}
}
