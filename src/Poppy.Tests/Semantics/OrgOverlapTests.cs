// Poppy Compiler - .org Overlap Regression Tests
// Copyright © 2026
//
// Regression coverage for issue #377: overlapping .org sections must fail
// loudly instead of silently overwriting earlier bytes, while legitimate
// layouts that re-org into earlier gaps (forward-then-backward pinning)
// must keep assembling.

using Poppy.Core.Arch;
using Poppy.Core.Lexer;
using Poppy.Core.Parser;
using Poppy.Core.Semantics;

namespace Poppy.Tests.Semantics;

/// <summary>
/// Tests that .org sections are checked for byte-range collisions with
/// previously emitted sections.
/// </summary>
public sealed class OrgOverlapTests {
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
	public void OverlappingOrgSections_ReportError() {
		// The issue's exact repro: the second section restarts at $8000 and
		// overwrites the first section's bytes.
		var source = @"
            .org $8000
            lda #$01
            rts
            .org $8000
            lda #$02
            rts
        ";
		var analyzer = Analyze(source);

		Assert.True(analyzer.HasErrors);
		Assert.Contains(analyzer.Errors, e => e.Message.Contains("overlaps previously emitted bytes"));
	}

	[Fact]
	public void OverlapInsideGrowingSection_ReportError() {
		// Second section lands in the middle of the first section's range.
		var source = @"
            .org $8000
            lda #$01
            rts
            .org $8001
            lda #$02
            rts
        ";
		var analyzer = Analyze(source);

		Assert.True(analyzer.HasErrors);
		Assert.Contains(analyzer.Errors, e => e.Message.Contains("overlaps previously emitted bytes"));
	}

	[Fact]
	public void ForwardGapSections_NoError() {
		var source = @"
            .org $8000
            lda #$01
            rts
            .org $8010
            lda #$02
            rts
        ";
		var analyzer = Analyze(source);

		Assert.False(analyzer.HasErrors);
	}

	[Fact]
	public void BackwardOrgIntoEarlierGap_NoError() {
		// escbank2-style layout: $D800 section, then $E000 section, then a
		// backward .org into the gap between them — no byte collision.
		var source = @"
            .org $D800
            lda #$01
            rts
            .org $E000
            lda #$02
            rts
            .org $DB00
            lda #$03
            rts
        ";
		var analyzer = Analyze(source);

		Assert.False(analyzer.HasErrors);
	}

	[Fact]
	public void AdjacentOrgNoContent_NoError() {
		// Immediately consecutive .org directives produce an empty section.
		var source = @"
            .org $8000
            .org $8100
            lda #$01
            rts
        ";
		var analyzer = Analyze(source);

		Assert.False(analyzer.HasErrors);
	}

	[Fact]
	public void AdjacentOrgNoContent_AfterCode_NoError() {
		// Section ending exactly where the next begins is not an overlap.
		var source = @"
            .org $8000
            lda #$01
            rts
            .org $8003
            lda #$02
            rts
        ";
		var analyzer = Analyze(source);

		Assert.False(analyzer.HasErrors);
	}
}
