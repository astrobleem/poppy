// Poppy Compiler - Long Operand Validation Tests
// Copyright © 2026
//
// Regression coverage for issue #379: on the 65816 target, a 24-bit memory
// operand must fail loudly when the selected instruction/index combination
// has no long addressing mode, instead of silently truncating to 16 bits
// (which produced wrong-address writes on real hardware).

using Poppy.Core.Arch;
using Poppy.Core.CodeGen;
using Poppy.Core.Semantics;
using Xunit;

using PoppyLexer = Poppy.Core.Lexer.Lexer;
using PoppyParser = Poppy.Core.Parser.Parser;

namespace Poppy.Tests.CodeGen;

/// <summary>
/// Tests that 24-bit memory operands are validated against the resolved
/// encoding on the WDC65816 target.
/// </summary>
public sealed class LongOperandValidationTests {
	private static (byte[] Code, CodeGenerator Generator, SemanticAnalyzer Analyzer) GenerateCode(string source) {
		var lexer = new PoppyLexer(source);
		var tokens = lexer.Tokenize();
		var parser = new PoppyParser(tokens);
		var program = parser.Parse();

		var analyzer = new SemanticAnalyzer(TargetArchitecture.WDC65816);
		analyzer.Analyze(program);

		var generator = new CodeGenerator(analyzer, analyzer.Target);
		var code = generator.Generate(program);

		return (code, generator, analyzer);
	}

	[Fact]
	public void StaAbsoluteY_BankByteOverflow_FailsLoud() {
		// 65816 has no absolute-long,Y encoding; the $7E bank byte must not
		// silently vanish from "sta $7E74C0,y".
		var source = ".snes\n.org $8000\n    sta $7E74C0,y\n    rts\n";
		var (_, gen, analyzer) = GenerateCode(source);

		Assert.True(analyzer.HasErrors || gen.HasErrors);
		Assert.Contains("does not fit", string.Join(" ", gen.Errors.Select(e => e.Message)));
	}

	[Fact]
	public void StxAbsolute_BankByteOverflow_FailsLoud() {
		// STX has no absolute-long encoding at all.
		var source = ".snes\n.org $8000\n    stx $7E1234\n    rts\n";
		var (_, gen, analyzer) = GenerateCode(source);

		Assert.True(analyzer.HasErrors || gen.HasErrors);
		Assert.Contains("does not fit", string.Join(" ", gen.Errors.Select(e => e.Message)));
	}

	[Fact]
	public void StaAbsoluteLongX_Valid_EmitsLongForm() {
		// absolute-long,X is legal: "sta $7E74C0,x" -> 9F C0 74 7E.
		var source = ".snes\n.org $8000\n    sta $7E74C0,x\n    rts\n";
		var (code, gen, _) = GenerateCode(source);

		Assert.False(gen.HasErrors);
		Assert.Equal([0x9f, 0xc0, 0x74, 0x7e, 0x60], code);
	}

	[Fact]
	public void StaAbsoluteLong_Valid_EmitsLongForm() {
		// plain absolute-long is legal: "sta $7E1234" -> 8F 34 12 7E.
		var source = ".snes\n.org $8000\n    sta $7E1234\n    rts\n";
		var (code, gen, _) = GenerateCode(source);

		Assert.False(gen.HasErrors);
		Assert.Equal([0x8f, 0x34, 0x12, 0x7e, 0x60], code);
	}
}
