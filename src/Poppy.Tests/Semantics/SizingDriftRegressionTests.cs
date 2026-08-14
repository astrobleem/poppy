// Poppy Compiler - Layout Sizing Regression Tests
// Copyright © 2026
//
// Regression coverage for issue #376: the layout (symbol/expression) pass must
// size operands identically to the code generator so labels and .word label
// references do not drift. The concrete failure was binary constant
// expressions (e.g. "lda CONST+1") being sized as Absolute by the layout pass
// while the code generator optimizes them to ZeroPage, shifting every later
// label by one byte per occurrence.

using Poppy.Core.Arch;
using Poppy.Core.Lexer;
using Poppy.Core.Parser;
using Poppy.Core.Semantics;

namespace Poppy.Tests.Semantics;

/// <summary>
/// Tests that the layout pass sizes 65816 operands consistently with the
/// code generator, keeping label values aligned with emitted code.
/// </summary>
public sealed class SizingDriftRegressionTests {
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
	public void BinaryLiteralOperand_ZeroPageSizing_DoesNotDriftLabels() {
		// "lda $0033+1" is a constant expression that fits ZeroPage; the code
		// generator emits "a5 34" (2 bytes). The layout pass must size it the
		// same way or the trailing label drifts by one byte.
		var source = @"
            .org $8000
start:
            lda $0033
            sta $210D
            lda $0033+1
            sta $210D
            rts
after:
            rts
        ";
		var analyzer = Analyze(source);

		// True layout: 2 + 3 + 2 + 3 + 1 = 11 bytes -> after at $800B.
		Assert.Equal(0x800B, analyzer.SymbolTable.Symbols["after"].Value);
	}

	[Fact]
	public void ConstantSymbolBinaryExpression_ZeroPageSizing_DoesNotDriftLabels() {
		// Same via an EQU-style constant: "lda SCROLL+1" must be 2 bytes.
		var source = @"
            .org $8000
SCROLL = $0033
start:
            lda SCROLL
            sta $210D
            lda SCROLL+1
            sta $210D
            rts
after:
            rts
        ";
		var analyzer = Analyze(source);

		Assert.Equal(0x800B, analyzer.SymbolTable.Symbols["after"].Value);
	}

	[Fact]
	public void SepImmediateSizing_DoesNotDriftLabels() {
		// Issue #376 companion: after "sep #$20", #imm operands are 8-bit
		// (2-byte instructions), so "after" must sit at $8005, not $8006.
		var source = @"
            .org $8000
            sep #$20
            lda #$00
            rts
after:
            rts
        ";
		var analyzer = Analyze(source);

		// True layout: 2 + 2 + 1 = 5 bytes -> after at $8005.
		Assert.Equal(0x8005, analyzer.SymbolTable.Symbols["after"].Value);
	}

	[Fact]
	public void RepThenSepSequence_MixedImmediates_DoNotDriftLabels() {
		// VIDTEST-style sequence: 16-bit immediates under rep #$30, then
		// 8-bit immediates under sep #$20, with index-register immediates.
		var source = @"
            .org $8000
start:
            php
            rep #$30
            lda #$0000
            sta $2116
            sep #$20
            lda #$80
            sta $2105
            ldx #$0000
            ldy #$0000
test_loop:
            tya
            eor #$55
            sta $2118
            iny
            cpy #$100
            bne test_loop
            lda #$01
            sta $212C
            plp
            rtl
after:
            rts
        ";
		var analyzer = Analyze(source);

		// Layout: php(1) rep(2) lda#0000(3) sta(3) sep(2) lda#80(2) sta(3)
		// ldx(3) ldy(3) = 22 bytes -> test_loop at $8016; then tya(1) eor(2)
		// sta(3) iny(1) cpy(3) bne(2) lda#01(2) sta(3) plp(1) rtl(1) = 19
		// more -> after at $8029.
		Assert.Equal(0x8029, analyzer.SymbolTable.Symbols["after"].Value);
		Assert.Equal(0x8016, analyzer.SymbolTable.Symbols["test_loop"].Value);
	}
}
