// Poppy Compiler - Silent Miscompile Regression Tests
// Copyright © 2026
//
// Regression coverage for defects that each produced a wrong binary with
// no diagnostic at all, so nothing downstream could tell the build had failed:
//
//   #382 - parse errors collected and then discarded
//   #384 - .incbin sized 0 bytes in the layout pass
//   #385 - a size suffix narrower than the resolved encoding truncating the
//          operand without shortening the opcode
//   #387 - the generic .sym exporter deriving banks from addresses
//   #388 - "-name" lexed as a named anonymous label in operand position, so
//          the subtraction in "#$ffff-SIZE" was silently dropped
//   #389 - the ^ (bank-byte) operator ignoring a referenced symbol's real
//          bank and always evaluating to 0
//   #390 - resuming a previously-visited bank with no explicit .org reset
//          the address cursor to that OTHER bank's leftover value instead
//          of resuming where this bank's own code left off, so labels and
//          bytes both landed at the wrong address and overwrote each other

using Poppy.Core.Arch;
using Poppy.Core.CodeGen;
using Poppy.Core.Lexer;
using Poppy.Core.Semantics;
using Xunit;

using PoppyLexer = Poppy.Core.Lexer.Lexer;
using PoppyParser = Poppy.Core.Parser.Parser;

namespace Poppy.Tests.Semantics;

/// <summary>
/// Tests that constructs which used to miscompile silently now either assemble
/// correctly or fail loudly.
/// </summary>
public sealed class SilentMiscompileRegressionTests {
	private static (byte[] Code, CodeGenerator Generator, SemanticAnalyzer Analyzer, PoppyParser Parser)
		Assemble(string source, string filePath = "<input>") {
		var lexer = new PoppyLexer(source, filePath);
		var tokens = lexer.Tokenize();
		var parser = new PoppyParser(tokens);
		var program = parser.Parse();

		var analyzer = new SemanticAnalyzer(TargetArchitecture.WDC65816);
		analyzer.Analyze(program);

		var generator = new CodeGenerator(analyzer, analyzer.Target);
		var code = generator.Generate(program);

		return (code, generator, analyzer, parser);
	}

	// ------------------------------------------------------------------
	// #382 - parse errors must survive to the caller
	// ------------------------------------------------------------------

	[Fact]
	public void UnknownMnemonic_IsReportedAsAParseError() {
		// "frobnicate" used to be dropped from the statement list; the file
		// assembled clean and simply did not contain that step.
		var source = ".snes\n.org $8000\n    lda #$01\n    frobnicate\n    rts\n";
		var (_, _, _, parser) = Assemble(source);

		Assert.True(parser.HasErrors);
		Assert.Contains(parser.Errors, e => e.Message.Contains("frobnicate"));
		Assert.Contains(parser.Errors, e => e.Location.Line == 4);
	}

	[Fact]
	public void UnknownMnemonic_MessageDoesNotOnlySuggestAMacro() {
		// The old text was "Unexpected identifier 'x'. Did you mean '@x' for a
		// macro invocation?", which sends the reader looking for a macro bug.
		var source = ".snes\n.org $8000\n    frobnicate\n";
		var (_, _, _, parser) = Assemble(source);

		Assert.Contains(parser.Errors, e => e.Message.Contains("Unknown instruction or macro"));
	}

	[Fact]
	public void ValidSource_ProducesNoParseErrors() {
		var source = ".snes\n.org $8000\n    lda #$01\n    rts\n";
		var (_, _, _, parser) = Assemble(source);

		Assert.False(parser.HasErrors);
	}

	// ------------------------------------------------------------------
	// #388 - "-name" in operand position is subtraction, not a label
	// ------------------------------------------------------------------

	[Fact]
	public void MinusBeforeIdentifier_AfterANumber_IsSubtraction() {
		// "lda #$ffff-SIZE" used to emit a9 ff ff: the lexer took "-SIZE" as a
		// named anonymous label reference and the parser dropped the rest of
		// the statement.
		var source = ".snes\n.org $8000\nSIZE = $0100\n    rep #$20\n    lda #$ffff-SIZE\n    rts\n";
		var (code, gen, _, parser) = Assemble(source);

		Assert.False(parser.HasErrors);
		Assert.False(gen.HasErrors);
		Assert.Equal([0xc2, 0x20, 0xa9, 0xff, 0xfe, 0x60], code);
	}

	[Fact]
	public void MinusBetweenTwoLabels_IsSubtraction() {
		// "cmp #End-Start" must compare against the length, not an address.
		var source = ".snes\n.org $8000\nStart:\n    .db 1, 2, 3, 4\nEnd:\n    cmp #End-Start\n    rts\n";
		var (code, gen, _, parser) = Assemble(source);

		Assert.False(parser.HasErrors);
		Assert.False(gen.HasErrors);
		Assert.Equal([0x01, 0x02, 0x03, 0x04, 0xc9, 0x04, 0x60], code);
	}

	[Fact]
	public void PlusBeforeIdentifier_AfterAnIdentifier_IsAddition() {
		var source = ".snes\n.org $8000\nBASE = $10\nOFF = $05\n    lda #BASE+OFF\n    rts\n";
		var (code, gen, _, parser) = Assemble(source);

		Assert.False(parser.HasErrors);
		Assert.False(gen.HasErrors);
		Assert.Equal([0xa9, 0x15, 0x60], code);
	}

	[Fact]
	public void NamedAnonymousLabel_InPrefixPosition_StillLexesAsALabel() {
		// The fix must not break the feature it collides with: a '-'/'+' that
		// is NOT preceded by an operand is still a named anonymous label.
		var tokens = new PoppyLexer("-loop:\n    dex\n    bne -loop\n").Tokenize();

		Assert.Contains(tokens, t => t.Type == TokenType.NamedAnonymousBackward && t.Text == "-loop");
		Assert.DoesNotContain(tokens, t => t.Type == TokenType.Minus);
	}

	// ------------------------------------------------------------------
	// #385 - a narrower size suffix must not truncate a long encoding
	// ------------------------------------------------------------------

	[Fact]
	public void WSuffix_OnAnOperandNeedingThreeBytes_FailsLoud() {
		// "sta.w $7E1234,x" resolved to the long indexed opcode $9f and then
		// emitted only two operand bytes: three bytes where the CPU decodes
		// four, desynchronising everything after it.
		var source = ".snes\n.org $8000\n    sta.w $7E1234,x\n    rts\n";
		var (_, gen, _, _) = Assemble(source);

		Assert.True(gen.HasErrors);
		Assert.Contains("truncate", string.Join(" ", gen.Errors.Select(e => e.Message)));
	}

	[Fact]
	public void LSuffix_OnTheSameOperand_StillAssembles() {
		var source = ".snes\n.org $8000\n    sta.l $7E1234,x\n    rts\n";
		var (code, gen, _, _) = Assemble(source);

		Assert.False(gen.HasErrors);
		Assert.Equal([0x9f, 0x34, 0x12, 0x7e, 0x60], code);
	}

	[Fact]
	public void BSuffix_OnAnImmediate_IsStillAllowedToNarrow() {
		// Immediate is exempt: there the suffix legitimately overrides the
		// width the M flag would imply.
		var source = ".snes\n.org $8000\n    rep #$20\n    lda.b #$12\n    rts\n";
		var (_, gen, _, _) = Assemble(source);

		Assert.False(gen.HasErrors);
	}

	// ------------------------------------------------------------------
	// #384 - .incbin must cost its real length in the layout pass
	// ------------------------------------------------------------------

	[Fact]
	public void IncbinAdvancesTheAddressCounter_ForLaterLabels() {
		var dir = Path.Combine(Path.GetTempPath(), "poppy-incbin-" + Guid.NewGuid().ToString("n"));
		Directory.CreateDirectory(dir);
		try {
			File.WriteAllBytes(Path.Combine(dir, "a.bin"), new byte[16]);
			File.WriteAllBytes(Path.Combine(dir, "b.bin"), new byte[32]);
			var sourcePath = Path.Combine(dir, "t.pasm");

			var source = ".snes\n.org $8000\nblob_a:\n    .incbin \"a.bin\"\nblob_b:\n    .incbin \"b.bin\"\nafter:\n    rts\n";
			var (_, gen, analyzer, _) = Assemble(source, sourcePath);

			Assert.False(gen.HasErrors);
			Assert.True(analyzer.SymbolTable.TryGetSymbol("blob_a", out var a));
			Assert.True(analyzer.SymbolTable.TryGetSymbol("blob_b", out var b));
			Assert.True(analyzer.SymbolTable.TryGetSymbol("after", out var c));
			Assert.Equal(0x8000, a!.Value);
			Assert.Equal(0x8010, b!.Value);
			Assert.Equal(0x8030, c!.Value);
		} finally {
			Directory.Delete(dir, recursive: true);
		}
	}

	[Fact]
	public void IncbinWithOffsetAndLength_AdvancesByTheSlicedLength() {
		var dir = Path.Combine(Path.GetTempPath(), "poppy-incbin-" + Guid.NewGuid().ToString("n"));
		Directory.CreateDirectory(dir);
		try {
			File.WriteAllBytes(Path.Combine(dir, "a.bin"), new byte[64]);
			var sourcePath = Path.Combine(dir, "t.pasm");

			var source = ".snes\n.org $8000\n    .incbin \"a.bin\", 8, 10\nafter:\n    rts\n";
			var (_, gen, analyzer, _) = Assemble(source, sourcePath);

			Assert.False(gen.HasErrors);
			Assert.True(analyzer.SymbolTable.TryGetSymbol("after", out var after));
			Assert.Equal(0x800a, after!.Value);
		} finally {
			Directory.Delete(dir, recursive: true);
		}
	}

	[Fact]
	public void IncbinOverrunningAFixedOrg_IsNowDetected() {
		// The overlap check could not see .incbin bytes at all, so a blob that
		// ran past the next fixed .org was silently half-overwritten.
		var dir = Path.Combine(Path.GetTempPath(), "poppy-incbin-" + Guid.NewGuid().ToString("n"));
		Directory.CreateDirectory(dir);
		try {
			File.WriteAllBytes(Path.Combine(dir, "big.bin"), new byte[0x40]);
			var sourcePath = Path.Combine(dir, "t.pasm");

			var source = ".snes\n.org $8000\n    .incbin \"big.bin\"\n.org $8020\n    rts\n";
			var (_, _, analyzer, _) = Assemble(source, sourcePath);

			Assert.True(analyzer.HasErrors);
			Assert.Contains("overlaps", string.Join(" ", analyzer.Errors.Select(e => e.Message)));
		} finally {
			Directory.Delete(dir, recursive: true);
		}
	}

	// ------------------------------------------------------------------
	// #387 - .sym banks come from the symbol, not from its address
	// ------------------------------------------------------------------

	[Fact]
	public void GenericSymExport_UsesTheSymbolsOwnBank() {
		var source = ".snes\n.bank 1\n.org $8000\nbank_one:\n    rts\n";
		var (_, _, analyzer, _) = Assemble(source);

		var path = Path.Combine(Path.GetTempPath(), "poppy-sym-" + Guid.NewGuid().ToString("n") + ".sym");
		try {
			new SymbolExporter(analyzer.SymbolTable, analyzer.Target).Export(path);
			var lines = File.ReadAllLines(path);

			Assert.Contains(lines, l => l.Trim() == "01:8000 bank_one");
		} finally {
			if (File.Exists(path)) File.Delete(path);
		}
	}

	// ------------------------------------------------------------------
	// #389 - the ^ (bank-byte) operator ignores a label's real bank
	// ------------------------------------------------------------------

	[Fact]
	public void BankByteOperator_UsesTheLabelsRealBank() {
		// EvaluateIdentifier only ever returned Symbol.Value (a plain 16-bit
		// address) -- the bank lives in the separate Symbol.Bank field, which
		// CodeGenerator.ApplySymbolBank already folds in for jsl/.long
		// operands. `^` was the one reference form that never consulted it,
		// so `lda #^(bank_one)` always emitted $00 regardless of bank_one's
		// real bank ($01 here).
		var source = ".snes\n.bank 0\n.org $8000\n.a8\n    lda #^(bank_one)\n    rts\n" +
			".bank 1\n.org $8000\nbank_one:\n    lda #$aa\n    rtl\n";
		var (code, _, _, _) = Assemble(source);

		Assert.Equal(0xA9, code[0]); // LDA #imm
		Assert.Equal(0x01, code[1]); // bank_one's real bank, not $00
	}

	// ------------------------------------------------------------------
	// #390 - resuming a bank with no .org must continue that bank's own
	// cursor, not inherit wherever some other bank left off
	// ------------------------------------------------------------------

	[Fact]
	public void ResumingABank_WithNoOrg_ContinuesThatBanksOwnCursor() {
		// SemanticAnalyzer.HandleBankDirective and CodeGenerator.HandleBankDirective
		// each unconditionally overwrote CurrentAddress/_currentAddress on every
		// .bank switch. Marker_D (back in bank 0 after a visit to bank 1) landed
		// at $8000 (bank 1's org) instead of $8002 (right after Marker_B), and its
		// byte overwrote the first sei's file position instead of appending after it.
		var source =
			".snes\n.bank 0\n.org $8000\nMarker_A:\n    sei\n    sei\nMarker_B:\n\n" +
			".bank 1\n.org $8000\nMarker_C:\n    clv\n\n" +
			".bank 0\nMarker_D:\n    clc\n";
		var (code, _, analyzer, _) = Assemble(source);

		Assert.True(analyzer.SymbolTable.TryGetSymbol("Marker_B", out var markerB));
		Assert.True(analyzer.SymbolTable.TryGetSymbol("Marker_D", out var markerD));
		Assert.Equal(0, markerD!.Bank);
		Assert.Equal(markerB!.Value, markerD.Value); // both resolve to $8002

		Assert.Equal(0x78, code[0]); // sei
		Assert.Equal(0x78, code[1]); // sei (must survive, not be overwritten)
		Assert.Equal(0x18, code[2]); // clc, appended after both seis
	}
}
