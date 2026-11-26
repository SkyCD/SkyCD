using System;
using System.Collections.Generic;
using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;
using SkyCD.Plugin.OnlineMetadata.Models;

namespace SkyCD.Plugin.Feature.GenDesc.Parsing;

/// <summary>
/// The Scriban globals for one render: the entry facts plus the whitelisted helper functions. Fact lookup
/// is case-insensitive, so <c>{{ name }}</c>, <c>{{ Name }}</c> and <c>{{ NAME }}</c> read the same fact;
/// function names always win over same-named facts so the built-ins keep working.
/// </summary>
internal sealed class TemplateScope : ScriptObject
{
    private readonly Dictionary<string, string> facts = new(StringComparer.OrdinalIgnoreCase);

    public TemplateScope(DescriptionFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        foreach (var key in facts.Keys)
        {
            this.facts[key] = facts.Get(key);
        }

        this.Import(typeof(TemplateFunctions), renamer: member => member.Name.ToLowerInvariant());
    }

    public override bool TryGetValue(TemplateContext? context, SourceSpan span, string member, out object value)
    {
        if (base.TryGetValue(context, span, member, out value!))
        {
            return true;
        }

        if (facts.TryGetValue(member, out var text))
        {
            value = text;
            return true;
        }

        value = null!;
        return false;
    }
}
