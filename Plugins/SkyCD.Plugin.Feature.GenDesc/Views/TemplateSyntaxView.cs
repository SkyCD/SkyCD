using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using SkyCD.Plugin.Feature.GenDesc.Parsing;

namespace SkyCD.Plugin.Feature.GenDesc.Views;

/// <summary>
/// A read-only view of a template body with colored spans. It rebuilds its runs only when the text or one of
/// the brushes changes, so typing in the editor stays cheap.
/// </summary>
public sealed class TemplateSyntaxView : TextBlock
{
    public static readonly StyledProperty<string?> TemplateTextProperty =
        AvaloniaProperty.Register<TemplateSyntaxView, string?>(nameof(TemplateText));

    public static readonly StyledProperty<IBrush?> TextBrushProperty =
        AvaloniaProperty.Register<TemplateSyntaxView, IBrush?>(nameof(TextBrush));

    public static readonly StyledProperty<IBrush?> TagBrushProperty =
        AvaloniaProperty.Register<TemplateSyntaxView, IBrush?>(nameof(TagBrush));

    public static readonly StyledProperty<IBrush?> KeywordBrushProperty =
        AvaloniaProperty.Register<TemplateSyntaxView, IBrush?>(nameof(KeywordBrush));

    public static readonly StyledProperty<IBrush?> FunctionBrushProperty =
        AvaloniaProperty.Register<TemplateSyntaxView, IBrush?>(nameof(FunctionBrush));

    public static readonly StyledProperty<IBrush?> StringBrushProperty =
        AvaloniaProperty.Register<TemplateSyntaxView, IBrush?>(nameof(StringBrush));

    public static readonly StyledProperty<IBrush?> NumberBrushProperty =
        AvaloniaProperty.Register<TemplateSyntaxView, IBrush?>(nameof(NumberBrush));

    public static readonly StyledProperty<IBrush?> CommentBrushProperty =
        AvaloniaProperty.Register<TemplateSyntaxView, IBrush?>(nameof(CommentBrush));

    public static readonly StyledProperty<IBrush?> BbCodeBrushProperty =
        AvaloniaProperty.Register<TemplateSyntaxView, IBrush?>(nameof(BbCodeBrush));

    static TemplateSyntaxView()
    {
        TemplateTextProperty.Changed.AddClassHandler<TemplateSyntaxView>((view, _) => view.Rebuild());
        TextBrushProperty.Changed.AddClassHandler<TemplateSyntaxView>((view, _) => view.Rebuild());
        TagBrushProperty.Changed.AddClassHandler<TemplateSyntaxView>((view, _) => view.Rebuild());
        KeywordBrushProperty.Changed.AddClassHandler<TemplateSyntaxView>((view, _) => view.Rebuild());
        FunctionBrushProperty.Changed.AddClassHandler<TemplateSyntaxView>((view, _) => view.Rebuild());
        StringBrushProperty.Changed.AddClassHandler<TemplateSyntaxView>((view, _) => view.Rebuild());
        NumberBrushProperty.Changed.AddClassHandler<TemplateSyntaxView>((view, _) => view.Rebuild());
        CommentBrushProperty.Changed.AddClassHandler<TemplateSyntaxView>((view, _) => view.Rebuild());
        BbCodeBrushProperty.Changed.AddClassHandler<TemplateSyntaxView>((view, _) => view.Rebuild());
    }

    public string? TemplateText
    {
        get => GetValue(TemplateTextProperty);
        set => SetValue(TemplateTextProperty, value);
    }

    public IBrush? TextBrush
    {
        get => GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    public IBrush? TagBrush
    {
        get => GetValue(TagBrushProperty);
        set => SetValue(TagBrushProperty, value);
    }

    public IBrush? KeywordBrush
    {
        get => GetValue(KeywordBrushProperty);
        set => SetValue(KeywordBrushProperty, value);
    }

    public IBrush? FunctionBrush
    {
        get => GetValue(FunctionBrushProperty);
        set => SetValue(FunctionBrushProperty, value);
    }

    public IBrush? StringBrush
    {
        get => GetValue(StringBrushProperty);
        set => SetValue(StringBrushProperty, value);
    }

    public IBrush? NumberBrush
    {
        get => GetValue(NumberBrushProperty);
        set => SetValue(NumberBrushProperty, value);
    }

    public IBrush? CommentBrush
    {
        get => GetValue(CommentBrushProperty);
        set => SetValue(CommentBrushProperty, value);
    }

    public IBrush? BbCodeBrush
    {
        get => GetValue(BbCodeBrushProperty);
        set => SetValue(BbCodeBrushProperty, value);
    }

    private void Rebuild()
    {
        var inlines = Inlines;
        if (inlines is null)
        {
            return;
        }

        var text = TemplateText ?? string.Empty;
        inlines.Clear();
        var cursor = 0;
        foreach (var span in TemplateSyntaxAnalyzer.Analyze(text))
        {
            if (span.Start < cursor || span.Length == 0)
            {
                continue;
            }

            if (span.Start > cursor)
            {
                inlines.Add(CreateRun(text[cursor..span.Start], TemplateSyntaxKind.Text));
            }

            inlines.Add(CreateRun(text.Substring(span.Start, span.Length), span.Kind));
            cursor = span.Start + span.Length;
        }

        if (cursor < text.Length)
        {
            inlines.Add(CreateRun(text[cursor..], TemplateSyntaxKind.Text));
        }
    }

    private Run CreateRun(string text, TemplateSyntaxKind kind)
    {
        var run = new Run(text);
        if (BrushFor(kind) is { } brush)
        {
            run.Foreground = brush;
        }

        switch (kind)
        {
            case TemplateSyntaxKind.Keyword:
            case TemplateSyntaxKind.Function:
                run.FontWeight = FontWeight.SemiBold;
                break;
            case TemplateSyntaxKind.Comment:
            case TemplateSyntaxKind.String:
                run.FontStyle = FontStyle.Italic;
                break;
        }

        return run;
    }

    private IBrush? BrushFor(TemplateSyntaxKind kind) => kind switch
    {
        TemplateSyntaxKind.Text => TextBrush,
        TemplateSyntaxKind.Tag => TagBrush,
        TemplateSyntaxKind.Keyword => KeywordBrush,
        TemplateSyntaxKind.Function => FunctionBrush,
        TemplateSyntaxKind.String => StringBrush,
        TemplateSyntaxKind.Number => NumberBrush,
        TemplateSyntaxKind.Comment => CommentBrush,
        TemplateSyntaxKind.Operator => TagBrush,
        TemplateSyntaxKind.BbCode => BbCodeBrush,
        _ => TextBrush
    };
}
