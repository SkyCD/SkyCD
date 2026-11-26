using System;
using System.Linq;
using Scriban;
using Scriban.Syntax;
using SkyCD.Plugin.Feature.GenDesc.Exceptions;
using SkyCD.Plugin.OnlineMetadata.Models;

namespace SkyCD.Plugin.Feature.GenDesc.Parsing;

/// <summary>
/// Renders GenDesc bodies with Scriban (Twig-style <c>{{ ... }}</c> blocks) plus the whitelisted
/// <see cref="TemplateFunctions"/> helpers. Facts are exposed as case-insensitive variables, a missing fact
/// renders as nothing, and blank lines left by conditional blocks are collapsed by
/// <see cref="TemplateOutput"/>. A body holding the retired <c>&lt;% ... %&gt;</c> blocks fails fast with a
/// migration hint instead of rendering the tags literally.
/// </summary>
public static class TemplateEngine
{
    /// <summary>Parses a body once so a batch render reuses the parsed template.</summary>
    public static CompiledTemplate Compile(string body)
    {
        var source = body ?? string.Empty;
        if (source.Contains("<%", StringComparison.Ordinal))
        {
            throw new TemplateSyntaxException(
                "legacy '<% %>' blocks are no longer supported; use Scriban '{{ }}' blocks instead" +
                " (for example '{{ Title }}' or '{{ if has(\"Year\") }}({{ Year }}){{ end }}')," +
                " and write a literal '<%' as '{{ \"<%\" }}'");
        }

        var template = Template.Parse(source);
        if (template.HasErrors)
        {
            throw new TemplateSyntaxException(string.Join("; ", template.Messages.Select(message =>
                message.Message)));
        }

        if (HasUnterminatedBlock(source))
        {
            throw new TemplateSyntaxException("unterminated '{{' block, a matching '}}' is required");
        }

        return new CompiledTemplate(template);
    }

    /// <summary>Parses and renders a body against the supplied facts in one call.</summary>
    public static string Render(string body, DescriptionFacts facts) => Compile(body).Render(facts);

    /// <summary>A reusable parsed template; render once per entry with that entry's facts.</summary>
    public sealed class CompiledTemplate
    {
        private readonly Template template;

        internal CompiledTemplate(Template template) => this.template = template;

        /// <summary>Renders the template and normalizes blank lines left by conditional blocks.</summary>
        public string Render(DescriptionFacts facts)
        {
            ArgumentNullException.ThrowIfNull(facts);
            var context = new TemplateContext();
            context.PushGlobal(new TemplateScope(facts));
            string rendered;
            try
            {
                rendered = template.Render(context);
            }
            catch (ScriptRuntimeException exception)
            {
                throw new TemplateSyntaxException(exception.Message);
            }

            return TemplateOutput.Normalize(rendered);
        }
    }

    private static bool HasUnterminatedBlock(string source)
    {
        var index = 0;
        while (true)
        {
            index = source.IndexOf("{{", index, StringComparison.Ordinal);
            if (index < 0) return false;
            if (source.IndexOf("}}", index + 2, StringComparison.Ordinal) < 0) return true;
            index += 2;
        }
    }
}
