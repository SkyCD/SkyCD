using System;
using System.Collections.Generic;
using System.Globalization;
using SkyCD.Plugin.Feature.GenDesc.Exceptions;

namespace SkyCD.Plugin.Feature.GenDesc.Parsing;

/// <summary>
/// Turns a template body into a node tree. The body is split into literal text and <c>&lt;% ... %&gt;</c>
/// blocks (with <c>&lt;%-</c> / <c>-%&gt;</c> trimming adjacent whitespace), blocks are classified as directives
/// or output expressions, and the conditional structure is validated so an unbalanced <c>if</c> fails to
/// compile rather than rendering silently.
/// </summary>
internal static class TemplateCompiler
{
    private const string Open = "<%";
    private const string Close = "%>";

    public static IReadOnlyList<TemplateNode> Compile(string body)
    {
        var segments = Scan(body);
        return BuildNodes(segments);
    }

    // Segment scanning ---------------------------------------------------------------

    private enum SegmentKind { Text, If, ElseIf, Else, End, Output, Comment }

    private sealed class Segment
    {
        public SegmentKind Kind;
        public string Text = string.Empty;
        public bool TrimLeft;
        public bool TrimRight;
    }

    private static List<Segment> Scan(string body)
    {
        var segments = new List<Segment>();
        var i = 0;
        while (i < body.Length)
        {
            var open = body.IndexOf(Open, i, StringComparison.Ordinal);
            if (open < 0)
            {
                segments.Add(new Segment { Kind = SegmentKind.Text, Text = body[i..] });
                break;
            }
            if (open > i) segments.Add(new Segment { Kind = SegmentKind.Text, Text = body[i..open] });

            var codeStart = open + Open.Length;
            var trimLeft = codeStart < body.Length && body[codeStart] == '-';
            var innerStart = trimLeft ? codeStart + 1 : codeStart;
            var close = body.IndexOf(Close, innerStart, StringComparison.Ordinal);
            if (close < 0) throw new TemplateSyntaxException("unterminated '<%' block, a matching '%>' is required");
            var trimRight = close > innerStart && body[close - 1] == '-';
            var innerEnd = trimRight ? close - 1 : close;
            var code = body[innerStart..innerEnd].Trim();
            segments.Add(new Segment { Kind = Classify(code, out var expression), Text = expression, TrimLeft = trimLeft, TrimRight = trimRight });

            i = close + Close.Length;
        }

        ApplyWhitespaceControl(segments);
        return segments;
    }

    private static void ApplyWhitespaceControl(List<Segment> segments)
    {
        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            if (segment.Kind == SegmentKind.Text) continue;
            if (segment.TrimLeft && index > 0 && segments[index - 1].Kind == SegmentKind.Text)
                segments[index - 1].Text = segments[index - 1].Text.TrimEnd();
            if (segment.TrimRight && index + 1 < segments.Count && segments[index + 1].Kind == SegmentKind.Text)
                segments[index + 1].Text = segments[index + 1].Text.TrimStart();
        }
    }

    private static SegmentKind Classify(string code, out string expression)
    {
        expression = string.Empty;
        if (code.Length == 0) return SegmentKind.Text; // an empty <% %> is a no-op
        if (code[0] == '#') return SegmentKind.Comment;
        if (Matches(code, "elseif", out var elifRest)) return Assign(SegmentKind.ElseIf, elifRest, out expression);
        if (Matches(code, "else if", out var elseIfRest)) return Assign(SegmentKind.ElseIf, elseIfRest, out expression);
        if (string.Equals(code, "else", StringComparison.OrdinalIgnoreCase)) return SegmentKind.Else;
        if (string.Equals(code, "end", StringComparison.OrdinalIgnoreCase)) return SegmentKind.End;
        if (Matches(code, "if", out var ifRest)) return Assign(SegmentKind.If, ifRest, out expression);
        if (Matches(code, "write", out var writeRest)) return Assign(SegmentKind.Output, writeRest, out expression);
        expression = code;
        return SegmentKind.Output;
    }

    private static SegmentKind Assign(SegmentKind kind, string rest, out string expression)
    {
        expression = rest.Trim();
        return kind;
    }

    private static bool Matches(string code, string keyword, out string rest)
    {
        rest = string.Empty;
        if (code.Length < keyword.Length || !code.StartsWith(keyword, StringComparison.OrdinalIgnoreCase)) return false;
        if (code.Length == keyword.Length) { rest = string.Empty; return true; }
        var next = code[keyword.Length];
        if (char.IsLetterOrDigit(next) || next == '_') return false; // avoid matching 'elsewhere' as 'else'
        rest = code[keyword.Length..];
        return true;
    }

    // Node building ------------------------------------------------------------------

    private static IReadOnlyList<TemplateNode> BuildNodes(List<Segment> segments)
    {
        var root = new List<TemplateNode>();
        var stack = new Stack<IfScope>();
        var current = root;

        foreach (var segment in segments)
        {
            switch (segment.Kind)
            {
                case SegmentKind.Text:
                    if (segment.Text.Length > 0) current.Add(new TextNode(segment.Text));
                    break;
                case SegmentKind.Output:
                    current.Add(new OutputNode(ParseExpression(segment.Text)));
                    break;
                case SegmentKind.Comment:
                    break;
                case SegmentKind.If:
                    var ifNode = new IfNode();
                    var ifBranch = new List<TemplateNode>();
                    ifNode.Branches.Add((ParseExpression(segment.Text), ifBranch));
                    current.Add(ifNode);
                    stack.Push(new IfScope(ifNode, current));
                    current = ifBranch;
                    break;
                case SegmentKind.ElseIf:
                    RequireOpen(stack, "elseif");
                    var elseIfBranch = new List<TemplateNode>();
                    stack.Peek().Node.Branches.Add((ParseExpression(segment.Text), elseIfBranch));
                    stack.Peek().Current = elseIfBranch;
                    current = elseIfBranch;
                    break;
                case SegmentKind.Else:
                    RequireOpen(stack, "else");
                    var elseBranch = new List<TemplateNode>();
                    stack.Peek().Node.Branches.Add((new TemplateExpr.Literal(TemplateValue.Bool(true)), elseBranch));
                    stack.Peek().Current = elseBranch;
                    current = elseBranch;
                    break;
                case SegmentKind.End:
                    RequireOpen(stack, "end");
                    var closed = stack.Pop();
                    current = closed.Parent;
                    break;
            }
        }

        if (stack.Count > 0) throw new TemplateSyntaxException("unclosed '<% if %>' block, a matching '<% end %>' is required");
        return root;
    }

    private static void RequireOpen(Stack<IfScope> stack, string keyword)
    {
        if (stack.Count == 0) throw new TemplateSyntaxException($"'<% {keyword} %>' appears without a matching '<% if %>'");
    }

    private sealed class IfScope(IfNode node, List<TemplateNode> parent)
    {
        public IfNode Node { get; } = node;
        public List<TemplateNode> Parent { get; } = parent;
        public List<TemplateNode> Current { get; set; } = parent;
    }

    // Expression parsing -------------------------------------------------------------

    public static TemplateExpr ParseExpression(string source)
    {
        var parser = new ExpressionParser(Lex(source, source), source);
        var expr = parser.ParseFilters();
        if (parser.Current.Kind != TokenKind.End) throw new TemplateSyntaxException($"unexpected token '{parser.Current.Text}' after expression");
        return expr;
    }

    private enum TokenKind { Number, String, Ident, Operator, LParen, RParen, Comma, End }

    private readonly struct Token
    {
        public Token(TokenKind kind, string text, double number = 0)
        {
            Kind = kind;
            Text = text;
            Number = number;
        }

        public TokenKind Kind { get; }
        public string Text { get; }
        public double Number { get; }
    }

    private static List<Token> Lex(string source, string context)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (char.IsDigit(c) || (c == '.' && i + 1 < source.Length && char.IsDigit(source[i + 1])))
            {
                var start = i;
                while (i < source.Length && (char.IsDigit(source[i]) || source[i] == '.')) i++;
                var literal = source[start..i];
                if (!double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    throw new TemplateSyntaxException($"malformed number '{literal}'");
                tokens.Add(new Token(TokenKind.Number, literal, value));
                continue;
            }
            if (c is '"' or '\'')
            {
                var quote = c;
                var start = ++i;
                while (i < source.Length && source[i] != quote) i++;
                if (i >= source.Length) throw new TemplateSyntaxException($"unterminated string literal in '{context}'");
                tokens.Add(new Token(TokenKind.String, source[start..i]));
                i++;
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_')) i++;
                tokens.Add(new Token(TokenKind.Ident, source[start..i]));
                continue;
            }
            if (MatchesOperator(source, i, out var op, out var length))
            {
                tokens.Add(new Token(TokenKind.Operator, op));
                i += length;
                continue;
            }
            switch (c)
            {
                case '(': tokens.Add(new Token(TokenKind.LParen, "(")); i++; break;
                case ')': tokens.Add(new Token(TokenKind.RParen, ")")); i++; break;
                case ',': tokens.Add(new Token(TokenKind.Comma, ",")); i++; break;
                default: throw new TemplateSyntaxException($"unexpected character '{c}' in expression");
            }
        }
        tokens.Add(new Token(TokenKind.End, string.Empty));
        return tokens;
    }

    private static bool MatchesOperator(string source, int index, out string op, out int length)
    {
        if (index + 1 < source.Length)
        {
            var two = source.Substring(index, 2);
            switch (two)
            {
                case "==":
                case "!=":
                case "<=":
                case ">=":
                case "&&":
                case "||":
                    op = two;
                    length = 2;
                    return true;
            }
        }
        switch (source[index])
        {
            case '+':
            case '-':
            case '<':
            case '>':
            case '!':
            case '|':
                op = source[index].ToString();
                length = 1;
                return true;
            default:
                op = string.Empty;
                length = 0;
                return false;
        }
    }

    private sealed class ExpressionParser
    {
        private readonly List<Token> tokens;
        private readonly string source;
        private int position;

        public ExpressionParser(List<Token> tokens, string source)
        {
            this.tokens = tokens;
            this.source = source;
        }

        public Token Current => tokens[position];

        private Token Advance() => tokens[position++];

        private bool IsOperator(string op) => Current.Kind == TokenKind.Operator && Current.Text == op;

        private bool IsKeyword(string word) => Current.Kind == TokenKind.Ident && string.Equals(Current.Text, word, StringComparison.OrdinalIgnoreCase);

        private bool TakeOperator(string op)
        {
            if (!IsOperator(op)) return false;
            Advance();
            return true;
        }

        private bool TakeKeyword(string word)
        {
            if (!IsKeyword(word)) return false;
            Advance();
            return true;
        }

        /// <summary>
        /// Parses an expression followed by any number of Twig-style filters: <c>Name | upper</c> or
        /// <c>Plot | replace("a", "b")</c>. A filter is the same whitelisted function, with the piped
        /// expression as its first argument, so extending the function registry extends the syntax.
        /// </summary>
        public TemplateExpr ParseFilters()
        {
            var left = ParseOr();
            while (TakeOperator("|"))
            {
                if (Current.Kind != TokenKind.Ident)
                {
                    throw new TemplateSyntaxException($"a filter name is required after '|' in expression '{source}'");
                }

                var name = Advance().Text;
                var lower = name.ToLowerInvariant();
                var args = new List<TemplateExpr> { left };
                if (Current.Kind == TokenKind.LParen)
                {
                    Advance();
                    if (Current.Kind != TokenKind.RParen)
                    {
                        args.Add(ParseFilters());
                        while (Current.Kind == TokenKind.Comma)
                        {
                            Advance();
                            args.Add(ParseFilters());
                        }
                    }
                    if (!TakeOperatorCloseParen()) throw new TemplateSyntaxException($"missing ')' after arguments to filter '{name}'");
                }

                if (!TemplateFunctions.IsKnown(lower)) throw new TemplateSyntaxException($"unknown template filter '{name}'");
                left = new TemplateExpr.Call(lower, args);
            }

            return left;
        }

        public TemplateExpr ParseOr()
        {
            var left = ParseAnd();
            while (TakeKeyword("or") || TakeOperator("||")) left = new TemplateExpr.Binary("or", left, ParseAnd());
            return left;
        }

        private TemplateExpr ParseAnd()
        {
            var left = ParseNot();
            while (TakeKeyword("and") || TakeOperator("&&")) left = new TemplateExpr.Binary("and", left, ParseNot());
            return left;
        }

        private TemplateExpr ParseNot()
        {
            if (TakeKeyword("not") || TakeOperator("!")) return new TemplateExpr.Unary("not", ParseNot());
            return ParseComparison();
        }

        private TemplateExpr ParseComparison()
        {
            var left = ParseAdditive();
            string[] comparisons = ["==", "!=", "<=", ">=", "<", ">"];
            foreach (var op in comparisons)
            {
                if (!TakeOperator(op)) continue;
                return new TemplateExpr.Binary(op, left, ParseAdditive());
            }
            return left;
        }

        private TemplateExpr ParseAdditive()
        {
            var left = ParseUnary();
            while (IsOperator("+") || IsOperator("-"))
            {
                var op = Advance().Text;
                left = new TemplateExpr.Binary(op, left, ParseUnary());
            }
            return left;
        }

        private TemplateExpr ParseUnary()
        {
            if (TakeOperator("-")) return new TemplateExpr.Unary("-", ParseUnary());
            return ParsePrimary();
        }

        private TemplateExpr ParsePrimary()
        {
            var token = Current;
            switch (token.Kind)
            {
                case TokenKind.Number:
                    Advance();
                    return new TemplateExpr.Literal(TemplateValue.Number(token.Number));
                case TokenKind.String:
                    Advance();
                    return new TemplateExpr.Literal(TemplateValue.Text(token.Text));
                case TokenKind.LParen:
                    Advance();
                    var inner = ParseFilters();
                    if (!TakeOperatorCloseParen()) throw new TemplateSyntaxException($"missing ')' in expression '{source}'");
                    return inner;
                case TokenKind.Ident:
                    return ParseIdentifier();
                default:
                    throw new TemplateSyntaxException($"unexpected token '{token.Text}' in expression '{source}'");
            }
        }

        private bool TakeOperatorCloseParen()
        {
            if (Current.Kind != TokenKind.RParen) return false;
            Advance();
            return true;
        }

        private TemplateExpr ParseIdentifier()
        {
            var name = Advance().Text;
            var lower = name.ToLowerInvariant();
            if (lower == "true") return new TemplateExpr.Literal(TemplateValue.Bool(true));
            if (lower == "false") return new TemplateExpr.Literal(TemplateValue.Bool(false));
            if (Current.Kind != TokenKind.LParen) return new TemplateExpr.Variable(name);

            Advance(); // consume '('
            var args = new List<TemplateExpr>();
            if (Current.Kind != TokenKind.RParen)
            {
                args.Add(ParseFilters());
                while (Current.Kind == TokenKind.Comma)
                {
                    Advance();
                    args.Add(ParseFilters());
                }
            }
            if (!TakeOperatorCloseParen()) throw new TemplateSyntaxException($"missing ')' after arguments to '{name}'");
            if (!TemplateFunctions.IsKnown(lower)) throw new TemplateSyntaxException($"unknown template function '{name}'");
            return new TemplateExpr.Call(lower, args);
        }
    }
}
