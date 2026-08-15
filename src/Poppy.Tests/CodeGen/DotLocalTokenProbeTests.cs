// Probe: the token stream + parsed statements for the plain-then-dot shape.
// The lexer classifies any .name as a Directive (test-enshrined), and the
// parser accepts Directive+':' as a dot-local label definition.
using Poppy.Core.Lexer;
using Poppy.Core.Parser;

namespace Poppy.Tests.CodeGen;

public sealed class DotLocalTokenProbeTests {
	[Fact]
	public void Probe_Tokens_PlainThenDot() {
		var lexer = new Poppy.Core.Lexer.Lexer("l0:\n rts\n.sib:\n rts", "test.pasm");
		var tokens = lexer.Tokenize();
		// the l0 label is a plain identifier label; the .sib is a Directive
		Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Text == "l0");
		Assert.Contains(tokens, t => t.Type == TokenType.Directive && t.Text == ".sib");
	}

	[Fact]
	public void Probe_Statements_PlainThenDot() {
		var lexer = new Poppy.Core.Lexer.Lexer("l0:\n rts\n.sib:\n rts", "test.pasm");
		var tokens = lexer.Tokenize();
		var parser = new Poppy.Core.Parser.Parser(tokens);
		var program = parser.Parse();
		// both label definitions parse: the plain l0 and the dot-local .sib
		var labels = program.Statements.OfType<LabelNode>().Select(l => l.Name).ToList();
		Assert.Contains("l0", labels);
		Assert.Contains(".sib", labels);
	}
}
