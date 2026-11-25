using System.Collections.Generic;
using System.Text;
using SkyCD.Plugin.OnlineMetadata.Models;

namespace SkyCD.Plugin.Ai.GenDesc.Parsing;

/// <summary>A compiled template element. Nodes form the tree the compiler builds and the renderer walks.</summary>
internal abstract class TemplateNode
{
    public abstract void Render(StringBuilder builder, DescriptionFacts facts);
}

/// <summary>Literal text emitted verbatim, including any newlines the author wrote.</summary>
internal sealed class TextNode(string text) : TemplateNode
{
    public override void Render(StringBuilder builder, DescriptionFacts facts) => builder.Append(text);
}

/// <summary>An expression whose rendered text is inserted into the output.</summary>
internal sealed class OutputNode(TemplateExpr expr) : TemplateNode
{
    public override void Render(StringBuilder builder, DescriptionFacts facts) => builder.Append(expr.Eval(facts).Display());
}

/// <summary>
/// A conditional chain: the first branch whose condition is truthy contributes its body, the rest are skipped.
/// The <c>else</c> form compiles to a trailing branch with an always-true condition.
/// </summary>
internal sealed class IfNode : TemplateNode
{
    public List<(TemplateExpr Condition, List<TemplateNode> Body)> Branches { get; } = [];

    public override void Render(StringBuilder builder, DescriptionFacts facts)
    {
        foreach (var branch in Branches)
        {
            if (!branch.Condition.Eval(facts).IsTruthy()) continue;
            foreach (var node in branch.Body) node.Render(builder, facts);
            return;
        }
    }
}
