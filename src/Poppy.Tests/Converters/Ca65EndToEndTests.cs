// Poppy Compiler - ca65 Converter End-to-End Tests
// Copyright © 2026
//
// Regression coverage for issue #380: converted ca65 output must feed through
// Poppy's own pipeline (lexer/parser/semantic/codegen) and produce the exact
// bytes and symbol addresses ca65 semantics imply. Substring-only converter
// tests passed while all three cases below were broken.

using Poppy.Core.Arch;
using Poppy.Core.CodeGen;
using Poppy.Core.Converters;
using Poppy.Core.Semantics;
using Xunit;

using PoppyLexer = Poppy.Core.Lexer.Lexer;
using PoppyParser = Poppy.Core.Parser.Parser;

namespace Poppy.Tests.Converters;

/// <summary>
/// Converts ca65 fixtures and assembles the result with the real pipeline,
/// asserting exact bytes and label values.
/// </summary>
public sealed class Ca65EndToEndTests {
	private readonly Ca65Converter _converter = new();
	private readonly ConversionOptions _options = new();

	private (byte[] Code, SemanticAnalyzer Analyzer) ConvertAndAssemble(string ca65Source) {
		var tempFile = Path.GetTempFileName();
		try {
			File.WriteAllText(tempFile, ca65Source);
			var result = _converter.ConvertFile(tempFile, _options);
			Assert.True(result.Success);

			var lexer = new PoppyLexer(result.Content, "converted.pasm");
			var tokens = lexer.Tokenize();
			var parser = new PoppyParser(tokens);
			var program = parser.Parse();

			var analyzer = new SemanticAnalyzer(TargetArchitecture.WDC65816);
			analyzer.Analyze(program);

			var generator = new CodeGenerator(analyzer, analyzer.Target);
			var code = generator.Generate(program);

			Assert.False(generator.HasErrors, string.Join("\n", generator.Errors.Select(e => e.Message)));
			return (code, analyzer);
		}
		finally {
			File.Delete(tempFile);
		}
	}

	[Fact]
	public void Case1_DirectivesKeepDots_Assembles16BitAtOrg() {
		// .a16/.i16/.org must survive the conversion; output must be 16-bit
		// code at $8000, not 8-bit code at address zero.
		var (code, analyzer) = ConvertAndAssemble(
			".setcpu \"65816\"\n" +
			".a16\n" +
			".i16\n" +
			".org $8000\n" +
			"start:\n" +
			"    lda #$1234\n" +
			"    ldx #$5678\n" +
			"    rts\n");

		Assert.Equal([0xa9, 0x34, 0x12, 0xa2, 0x78, 0x56, 0x60], code);
		Assert.Equal(0x8000, analyzer.SymbolTable.Symbols["start"].Value);
	}

	[Fact]
	public void Case2_AtLocalLabelsPreserved_AssemblesBranches() {
		// @loop must stay @loop (PASM scoped label), not become a .loop
		// directive the assembler discards.
		var (code, analyzer) = ConvertAndAssemble(
			".setcpu \"65816\"\n" +
			".org $8000\n" +
			"proc:\n" +
			"    bne @loop\n" +
			"    rts\n" +
			"@loop:\n" +
			"    bra @loop\n");

		Assert.Equal([0xd0, 0x01, 0x60, 0x80, 0xfe], code);
		Assert.Equal(0x8000, analyzer.SymbolTable.Symbols["proc"].Value);
	}

	[Fact]
	public void Case3_MacroInvocationExpandsAtCallSite() {
		// The invocation must re-emit as @emit_two so the body expands at the
		// call site and following labels resolve past it.
		var (code, analyzer) = ConvertAndAssemble(
			".setcpu \"65816\"\n" +
			".org $8000\n" +
			".macro emit_two\n" +
			"    nop\n" +
			"    nop\n" +
			".endmacro\n" +
			"after_definition:\n" +
			"    emit_two\n" +
			"after_invocation:\n" +
			"    rts\n");

		Assert.Equal([0xea, 0xea, 0x60], code);
		Assert.Equal(0x8000, analyzer.SymbolTable.Symbols["after_definition"].Value);
		Assert.Equal(0x8002, analyzer.SymbolTable.Symbols["after_invocation"].Value);
	}
}
