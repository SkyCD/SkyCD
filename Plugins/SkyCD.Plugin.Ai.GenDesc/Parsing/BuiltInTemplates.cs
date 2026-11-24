using System.Collections.Generic;
using SkyCD.Plugin.Ai.GenDesc.Models;

namespace SkyCD.Plugin.Ai.GenDesc.Parsing;

/// <summary>
/// The templates shipped with the plugin, ported from the old GenDesc.NET defaults. They are BBCode-emitting
/// and safe as a starting point: every field is guarded so an entry with few imported facts still renders. A
/// copy of each is seeded into the user template directory so it can be edited or replaced per catalog site.
/// </summary>
internal static class BuiltInTemplates
{
    public const string EnglishId = "default-english";
    public const string LithuanianId = "default-lithuanian";

    private const string English = """
        ## id=default-english
        ## site=default
        ## lang=en
        ## version=1
        [b]<% Default("Title", Name) %><% if Year %> (<% Year %>)<% end %>[/b]
        <%- if Has("OriginalTitle") -%>
        [i]Original title:[/i] <% OriginalTitle %>
        <%- end -%>
        <%- if Has("Genre") -%>
        [i]Genre:[/i] <% Join("Genre", ", ") %>
        <%- end -%>
        <%- if Has("Runtime") -%>
        [i]Runtime:[/i] <% Runtime %>
        <%- end -%>
        <%- if Has("Director") -%>
        [i]Director:[/i] <% Director %>
        <%- end -%>
        <%- if Has("Cast") -%>
        [i]Cast:[/i] <% Cast %>
        <%- end -%>
        <%- if Has("Rating") -%>
        [i]Rating:[/i] <% Rating %>
        <%- end -%>
        <%- if Has("Plot") -%>

        <% Plot %>
        <%- end -%>

        """;

    private const string Lithuanian = """
        ## id=default-lithuanian
        ## site=default
        ## lang=lt
        ## version=1
        [b]<% Default("Title", Name) %><% if Year %> (<% Year %>)<% end %>[/b]
        <%- if Has("Genre") -%>
        [i]Žanras:[/i] <% Join("Genre", ", ") %>
        <%- end -%>
        <%- if Has("Runtime") -%>
        [i]Trukmė:[/i] <% Runtime %>
        <%- end -%>
        <%- if Has("Director") -%>
        [i]Režisierius:[/i] <% Director %>
        <%- end -%>
        <%- if Has("Cast") -%>
        [i]Vaidina:[/i] <% Cast %>
        <%- end -%>
        <%- if Has("Plot") -%>

        <% Plot %>
        <%- end -%>

        """;

    public static IReadOnlyList<TemplateInfo> All() =>
    [
        TemplateInfo.Parse(English, EnglishId, "en"),
        TemplateInfo.Parse(Lithuanian, LithuanianId, "lt")
    ];
}
