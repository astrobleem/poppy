// Poppy Compiler - Width Hazard Warning Tests
// Copyright © 2026
//
// Coverage for poppy#386: M/X width inference is linear over source order,
// not control flow, so a rep/sep between a conditional branch and its target
// label still updates the state used to size the target's own immediates,
// even on paths that never execute that rep/sep. Full control-flow analysis
// is not attempted (see issue #386's own scope note); these tests cover the
// narrow, mechanical diagnostic that is attempted instead.

using Poppy.Core.Arch;
using Poppy.Core.Semantics;
using Xunit;

using PoppyLexer = Poppy.Core.Lexer.Lexer;
using PoppyParser = Poppy.Core.Parser.Parser;

namespace Poppy.Tests.Semantics;

public sealed class WidthHazardWarningTests {
	private static SemanticAnalyzer Analyze(string source) {
		var lexer = new PoppyLexer(source, "<input>");
		var tokens = lexer.Tokenize();
		var parser = new PoppyParser(tokens);
		var program = parser.Parse();

		var analyzer = new SemanticAnalyzer(TargetArchitecture.WDC65816);
		analyzer.Analyze(program);
		return analyzer;
	}

	[Fact]
	public void BranchOverRepSep_WithNoDirectiveAtTarget_Warns() {
		// Issue #386's own minimal repro: control reaches `skip` with M=1
		// whenever the branch is taken, but the rep #$30 is only on the
		// fall-through path -- Poppy's linear estimator doesn't know that.
		var source = ".snes\n.org $8000\n" +
			"    sep #$20\n" +
			"    lda #$01\n" +
			"    bne skip\n" +
			"    rep #$30\n" +
			"    lda #$1234\n" +
			"skip:\n" +
			"    cmp #$02\n" +
			"    rts\n";
		var analyzer = Analyze(source);

		Assert.Contains(analyzer.Warnings, w => w.Message.Contains("skip") && w.Message.Contains("386"));
	}

	[Fact]
	public void BranchOverRepSep_WithExplicitDirectiveAtTarget_DoesNotWarn() {
		var source = ".snes\n.org $8000\n" +
			"    sep #$20\n" +
			"    lda #$01\n" +
			"    bne skip\n" +
			"    rep #$30\n" +
			"    lda #$1234\n" +
			"skip:\n" +
			".a8\n" +
			"    cmp #$02\n" +
			"    rts\n";
		var analyzer = Analyze(source);

		Assert.Empty(analyzer.Warnings);
	}

	[Fact]
	public void BranchOverRepSep_WithOwnRepSepAtTarget_DoesNotWarn() {
		// The label's own rep/sep re-syncs the linear tracker the same way
		// an explicit .a8/.a16 directive would.
		var source = ".snes\n.org $8000\n" +
			"    sep #$20\n" +
			"    lda #$01\n" +
			"    bne skip\n" +
			"    rep #$30\n" +
			"    lda #$1234\n" +
			"skip:\n" +
			"    sep #$20\n" +
			"    cmp #$02\n" +
			"    rts\n";
		var analyzer = Analyze(source);

		Assert.Empty(analyzer.Warnings);
	}

	[Fact]
	public void BranchWithNoRepSepBetween_DoesNotWarn() {
		// No rep/sep sits between the branch and its target at all -- the
		// linear estimator's state is genuinely unaffected by the branch, so
		// this is not the hazard shape.
		var source = ".snes\n.org $8000\n" +
			"    sep #$20\n" +
			"    lda #$01\n" +
			"    bne skip\n" +
			"    lda #$02\n" +
			"skip:\n" +
			"    cmp #$02\n" +
			"    rts\n";
		var analyzer = Analyze(source);

		Assert.Empty(analyzer.Warnings);
	}

	[Fact]
	public void BackwardBranchOverRepSep_DoesNotWarn() {
		// The label precedes the branch: it was already visited in linear
		// order when originally reached, so there is no "skipped" region --
		// not the hazard this diagnostic targets.
		var source = ".snes\n.org $8000\n" +
			"    sep #$20\n" +
			"loop:\n" +
			"    cmp #$02\n" +
			"    rep #$30\n" +
			"    lda #$1234\n" +
			"    sep #$20\n" +
			"    bne loop\n" +
			"    rts\n";
		var analyzer = Analyze(source);

		Assert.Empty(analyzer.Warnings);
	}
}
