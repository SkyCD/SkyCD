using System;
using System.Collections.Generic;
using System.Text;
using SkyCD.Plugin.Ai.GenDesc.Models;

namespace SkyCD.Plugin.Ai.GenDesc.Parsing;

/// <summary>
/// A small, safe template engine for GenDesc. A body mixes literal text with <c>&lt;% ... %&gt;</c> blocks that
/// either output an expression or open a conditional. Expressions read imported facts by name and call a fixed
/// whitelist of text and BBCode helpers; no arbitrary code is ever compiled or executed.
/// </summary>
public static class TemplateEngine
{
    /// <summary>Compiles a body once so a batch render reuses the parsed node tree.</summary>
    public static CompiledTemplate Compile(string body) => new(TemplateCompiler.Compile(body ?? string.Empty));

    /// <summary>Compiles and renders a body against the supplied facts in one call.</summary>
    public static string Render(string body, DescriptionFacts facts) => Compile(body).Render(facts);

    /// <summary>A reusable parsed template made of <see cref="TemplateNode"/> instances.</summary>
    public sealed class CompiledTemplate
    {
        private readonly IReadOnlyList<TemplateNode> nodes;

        internal CompiledTemplate(IReadOnlyList<TemplateNode> nodes) => this.nodes = nodes;

        /// <summary>Number of top-level nodes, exposed so tests can assert on the parsed shape.</summary>
        public int NodeCount => nodes.Count;

        /// <summary>Renders the template and normalizes blank lines left by conditional blocks.</summary>
        public string Render(DescriptionFacts facts)
        {
            ArgumentNullException.ThrowIfNull(facts);
            var builder = new StringBuilder();
            foreach (var node in nodes) node.Render(builder, facts);
            return TemplateOutput.Normalize(builder.ToString());
        }
    }
}
