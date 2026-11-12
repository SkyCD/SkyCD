using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using SkyCD.Formatting.Rtf;

namespace SkyCD.UI.Controls.RichText;

public partial class RichTextEditor : UserControl
{
    public static readonly StyledProperty<string> RtfTextProperty =
        AvaloniaProperty.Register<RichTextEditor, string>(
            nameof(RtfText),
            defaultValue: string.Empty,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<RichTextEditor, bool>(
            nameof(IsReadOnly),
            defaultValue: false);

    private sealed class Snapshot
    {
        public string Text { get; init; } = string.Empty;
        public List<RichFormatFlags> Flags { get; init; } = [];
        public List<bool> Bullets { get; init; } = [];
        public int SelectionStart { get; init; }
        public int SelectionEnd { get; init; }
    }

    private string _currentText = string.Empty;
    private readonly List<RichFormatFlags> _charFlags = [];
    private readonly List<bool> _lineBullets = [];
    private RichFormatFlags _activeTypingFormat = RichFormatFlags.None;

    private readonly Stack<Snapshot> _undoStack = new();
    private readonly Stack<Snapshot> _redoStack = new();
    private bool _isUpdating;
    private bool _suppressUndoPush;

    public RichTextEditor()
    {
        InitializeComponent();

        EditorTextBox.AddHandler(KeyDownEvent, OnTextBoxKeyDown, RoutingStrategies.Tunnel);
        EditorTextBox.PropertyChanged += OnTextBoxPropertyChanged;

        _lineBullets.Add(false);
        PushSnapshot();
    }

    public string RtfText
    {
        get => GetValue(RtfTextProperty);
        set => SetValue(RtfTextProperty, value);
    }

    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == RtfTextProperty)
        {
            if (!_isUpdating)
            {
                LoadFromRtfOrPlainText(change.GetNewValue<string>());
            }
        }
        else if (change.Property == IsReadOnlyProperty)
        {
            EditorTextBox.IsReadOnly = change.GetNewValue<bool>();
            BoldButton.IsEnabled = !IsReadOnly;
            ItalicButton.IsEnabled = !IsReadOnly;
            UnderlineButton.IsEnabled = !IsReadOnly;
            StrikeoutButton.IsEnabled = !IsReadOnly;
            BulletButton.IsEnabled = !IsReadOnly;
            UndoButton.IsEnabled = !IsReadOnly;
            RedoButton.IsEnabled = !IsReadOnly;
        }
    }

    private void OnTextBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.TextProperty)
        {
            if (!_isUpdating)
            {
                HandleTextChanged(EditorTextBox.Text ?? string.Empty);
            }
        }
        else if (e.Property == TextBox.SelectionStartProperty || e.Property == TextBox.SelectionEndProperty)
        {
            UpdateToolbarStates();
        }
    }

    private void OnTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        var isCtrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        if (!isCtrl || (e.KeyModifiers & KeyModifiers.Alt) != 0)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.B:
                ToggleBold();
                e.Handled = true;
                break;
            case Key.I:
                ToggleItalic();
                e.Handled = true;
                break;
            case Key.U:
                ToggleUnderline();
                e.Handled = true;
                break;
            case Key.S:
                ToggleStrikeout();
                e.Handled = true;
                break;
            case Key.L:
                ToggleBullet();
                e.Handled = true;
                break;
            case Key.Z:
                if ((e.KeyModifiers & KeyModifiers.Shift) != 0)
                {
                    Redo();
                }
                else
                {
                    Undo();
                }
                e.Handled = true;
                break;
            case Key.Y:
                Redo();
                e.Handled = true;
                break;
            case Key.A:
                SelectAll();
                e.Handled = true;
                break;
        }
    }

    public void ToggleBold() => ApplyFormatToggle(RichFormatFlags.Bold);
    public void ToggleItalic() => ApplyFormatToggle(RichFormatFlags.Italic);
    public void ToggleUnderline() => ApplyFormatToggle(RichFormatFlags.Underline);
    public void ToggleStrikeout() => ApplyFormatToggle(RichFormatFlags.Strikeout);

    public void ToggleBullet()
    {
        if (IsReadOnly) return;

        var (start, end) = GetSelectionRange();
        var lines = _currentText.Split('\n');
        var lineStarts = new int[lines.Length];
        var running = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            lineStarts[i] = running;
            running += lines[i].Length + 1;
        }

        while (_lineBullets.Count < lines.Length)
        {
            _lineBullets.Add(false);
        }

        var affectedLines = new List<int>();
        for (var i = 0; i < lines.Length; i++)
        {
            var lineStart = lineStarts[i];
            var lineEnd = lineStart + lines[i].Length;

            if (start <= lineEnd && end >= lineStart)
            {
                affectedLines.Add(i);
            }
        }

        if (affectedLines.Count == 0 && lines.Length > 0)
        {
            affectedLines.Add(0);
        }

        var allBulleted = affectedLines.All(idx => _lineBullets[idx]);
        var targetState = !allBulleted;

        foreach (var lineIdx in affectedLines)
        {
            _lineBullets[lineIdx] = targetState;
        }

        UpdateFormattedDisplay();
        SyncRtfText();
        PushSnapshot();
        UpdateToolbarStates();
    }

    public void Undo()
    {
        if (_undoStack.Count <= 1) return;

        var current = _undoStack.Pop();
        _redoStack.Push(current);

        var prev = _undoStack.Peek();
        RestoreSnapshot(prev);
    }

    public void Redo()
    {
        if (_redoStack.Count == 0) return;

        var next = _redoStack.Pop();
        _undoStack.Push(next);
        RestoreSnapshot(next);
    }

    public void SelectAll()
    {
        EditorTextBox.SelectionStart = 0;
        EditorTextBox.SelectionEnd = _currentText.Length;
    }

    private void ApplyFormatToggle(RichFormatFlags flag)
    {
        if (IsReadOnly) return;

        var (start, end) = GetSelectionRange();
        if (start == end)
        {
            if ((_activeTypingFormat & flag) != 0)
            {
                _activeTypingFormat &= ~flag;
            }
            else
            {
                _activeTypingFormat |= flag;
            }
        }
        else
        {
            var length = end - start;
            var allHaveFlag = true;
            for (var i = start; i < end && i < _charFlags.Count; i++)
            {
                if ((_charFlags[i] & flag) == 0)
                {
                    allHaveFlag = false;
                    break;
                }
            }

            var apply = !allHaveFlag;
            for (var i = start; i < end && i < _charFlags.Count; i++)
            {
                if (apply)
                {
                    _charFlags[i] |= flag;
                }
                else
                {
                    _charFlags[i] &= ~flag;
                }
            }

            if (apply)
            {
                _activeTypingFormat |= flag;
            }
            else
            {
                _activeTypingFormat &= ~flag;
            }

            UpdateFormattedDisplay();
            SyncRtfText();
            PushSnapshot();
        }

        UpdateToolbarStates();
    }

    private (int start, int end) GetSelectionRange()
    {
        var start = Math.Min(EditorTextBox.SelectionStart, EditorTextBox.SelectionEnd);
        var end = Math.Max(EditorTextBox.SelectionStart, EditorTextBox.SelectionEnd);
        start = Math.Clamp(start, 0, _currentText.Length);
        end = Math.Clamp(end, 0, _currentText.Length);
        return (start, end);
    }

    private void HandleTextChanged(string newText)
    {
        var oldText = _currentText;
        if (oldText == newText) return;

        var prefixLen = 0;
        var minLen = Math.Min(oldText.Length, newText.Length);
        while (prefixLen < minLen && oldText[prefixLen] == newText[prefixLen])
        {
            prefixLen++;
        }

        var suffixLen = 0;
        while (suffixLen < (minLen - prefixLen) &&
               oldText[oldText.Length - 1 - suffixLen] == newText[newText.Length - 1 - suffixLen])
        {
            suffixLen++;
        }

        var deletedCount = oldText.Length - prefixLen - suffixLen;
        var insertedCount = newText.Length - prefixLen - suffixLen;
        var changeIndex = prefixLen;

        if (deletedCount > 0 && changeIndex < _charFlags.Count)
        {
            var countToRemove = Math.Min(deletedCount, _charFlags.Count - changeIndex);
            _charFlags.RemoveRange(changeIndex, countToRemove);
        }

        var insertFormat = _activeTypingFormat;
        if (insertFormat == RichFormatFlags.None && changeIndex > 0 && changeIndex - 1 < _charFlags.Count)
        {
            insertFormat = _charFlags[changeIndex - 1];
        }

        for (var i = 0; i < insertedCount; i++)
        {
            _charFlags.Insert(changeIndex + i, insertFormat);
        }

        _currentText = newText;

        var lineCount = newText.Count(c => c == '\n') + 1;
        while (_lineBullets.Count < lineCount)
        {
            _lineBullets.Add(false);
        }
        while (_lineBullets.Count > lineCount && _lineBullets.Count > 1)
        {
            _lineBullets.RemoveAt(_lineBullets.Count - 1);
        }

        UpdateFormattedDisplay();
        SyncRtfText();

        if (!_suppressUndoPush)
        {
            PushSnapshot();
        }

        UpdateToolbarStates();
    }

    private void LoadFromRtfOrPlainText(string? text)
    {
        _charFlags.Clear();
        _lineBullets.Clear();
        _undoStack.Clear();
        _redoStack.Clear();

        var doc = RichTextDocument.FromRtfOrPlainText(text);
        var sb = new System.Text.StringBuilder();

        for (var p = 0; p < doc.Paragraphs.Count; p++)
        {
            var para = doc.Paragraphs[p];
            _lineBullets.Add(para.IsBullet);

            foreach (var span in para.Spans)
            {
                var flags = (span.Bold ? RichFormatFlags.Bold : 0) |
                            (span.Italic ? RichFormatFlags.Italic : 0) |
                            (span.Underline ? RichFormatFlags.Underline : 0) |
                            (span.Strikeout ? RichFormatFlags.Strikeout : 0);

                sb.Append(span.Text);
                for (var i = 0; i < span.Text.Length; i++)
                {
                    _charFlags.Add(flags);
                }
            }

            if (p < doc.Paragraphs.Count - 1)
            {
                sb.Append('\n');
                _charFlags.Add(RichFormatFlags.None);
            }
        }

        if (_lineBullets.Count == 0)
        {
            _lineBullets.Add(false);
        }

        _currentText = sb.ToString();

        _isUpdating = true;
        EditorTextBox.Text = _currentText;
        _isUpdating = false;

        UpdateFormattedDisplay();
        PushSnapshot();
        UpdateToolbarStates();
    }

    private void UpdateFormattedDisplay()
    {
        var inlines = new InlineCollection();
        var lines = _currentText.Split('\n');
        var charIndex = 0;

        for (var lineIdx = 0; lineIdx < lines.Length; lineIdx++)
        {
            var line = lines[lineIdx];
            var isBullet = lineIdx < _lineBullets.Count && _lineBullets[lineIdx];

            if (isBullet)
            {
                inlines.Add(new Run("• ") { FontWeight = FontWeight.Bold });
            }

            if (line.Length > 0)
            {
                var runStart = 0;
                while (runStart < line.Length)
                {
                    var flags = (charIndex + runStart < _charFlags.Count)
                        ? _charFlags[charIndex + runStart]
                        : RichFormatFlags.None;

                    var runEnd = runStart + 1;
                    while (runEnd < line.Length &&
                           (charIndex + runEnd < _charFlags.Count) &&
                           _charFlags[charIndex + runEnd] == flags)
                    {
                        runEnd++;
                    }

                    var runText = line[runStart..runEnd];
                    var run = new Run(runText);

                    if ((flags & RichFormatFlags.Bold) != 0)
                    {
                        run.FontWeight = FontWeight.Bold;
                    }
                    if ((flags & RichFormatFlags.Italic) != 0)
                    {
                        run.FontStyle = FontStyle.Italic;
                    }

                    var decorations = new TextDecorationCollection();
                    if ((flags & RichFormatFlags.Underline) != 0)
                    {
                        decorations.Add(TextDecorations.Underline[0]);
                    }
                    if ((flags & RichFormatFlags.Strikeout) != 0)
                    {
                        decorations.Add(TextDecorations.Strikethrough[0]);
                    }
                    if (decorations.Count > 0)
                    {
                        run.TextDecorations = decorations;
                    }

                    inlines.Add(run);
                    runStart = runEnd;
                }
            }

            charIndex += line.Length;
            if (lineIdx < lines.Length - 1)
            {
                inlines.Add(new LineBreak());
                charIndex++;
            }
        }

        FormattedDisplay.Inlines = inlines;
    }

    private void SyncRtfText()
    {
        var doc = BuildDocument();
        _isUpdating = true;
        RtfText = doc.HasFormatting ? doc.ToRtf() : doc.ToPlainText();
        _isUpdating = false;
    }

    public RichTextDocument BuildDocument()
    {
        var doc = new RichTextDocument();
        doc.Paragraphs.Clear();

        var lines = _currentText.Split('\n');
        var charIndex = 0;

        for (var lineIdx = 0; lineIdx < lines.Length; lineIdx++)
        {
            var line = lines[lineIdx];
            var isBullet = lineIdx < _lineBullets.Count && _lineBullets[lineIdx];
            var para = new RichTextParagraph { IsBullet = isBullet };

            if (line.Length == 0)
            {
                para.Spans.Add(new RichTextSpan(string.Empty));
            }
            else
            {
                var runStart = 0;
                while (runStart < line.Length)
                {
                    var flags = (charIndex + runStart < _charFlags.Count)
                        ? _charFlags[charIndex + runStart]
                        : RichFormatFlags.None;

                    var runEnd = runStart + 1;
                    while (runEnd < line.Length &&
                           (charIndex + runEnd < _charFlags.Count) &&
                           _charFlags[charIndex + runEnd] == flags)
                    {
                        runEnd++;
                    }

                    var runText = line[runStart..runEnd];
                    para.Spans.Add(new RichTextSpan(
                        runText,
                        bold: (flags & RichFormatFlags.Bold) != 0,
                        italic: (flags & RichFormatFlags.Italic) != 0,
                        underline: (flags & RichFormatFlags.Underline) != 0,
                        strikeout: (flags & RichFormatFlags.Strikeout) != 0));

                    runStart = runEnd;
                }
            }

            doc.Paragraphs.Add(para);
            charIndex += line.Length + 1;
        }

        if (doc.Paragraphs.Count == 0)
        {
            doc.Paragraphs.Add(new RichTextParagraph());
        }

        return doc;
    }

    private void PushSnapshot()
    {
        _undoStack.Push(new Snapshot
        {
            Text = _currentText,
            Flags = [.. _charFlags],
            Bullets = [.. _lineBullets],
            SelectionStart = EditorTextBox.SelectionStart,
            SelectionEnd = EditorTextBox.SelectionEnd
        });
        _redoStack.Clear();
    }

    private void RestoreSnapshot(Snapshot snapshot)
    {
        _suppressUndoPush = true;
        _currentText = snapshot.Text;
        _charFlags.Clear();
        _charFlags.AddRange(snapshot.Flags);
        _lineBullets.Clear();
        _lineBullets.AddRange(snapshot.Bullets);

        _isUpdating = true;
        EditorTextBox.Text = _currentText;
        EditorTextBox.SelectionStart = Math.Clamp(snapshot.SelectionStart, 0, _currentText.Length);
        EditorTextBox.SelectionEnd = Math.Clamp(snapshot.SelectionEnd, 0, _currentText.Length);
        _isUpdating = false;

        UpdateFormattedDisplay();
        SyncRtfText();
        _suppressUndoPush = false;
        UpdateToolbarStates();
    }

    private void UpdateToolbarStates()
    {
        var (start, end) = GetSelectionRange();
        RichFormatFlags flags;

        if (start == end)
        {
            flags = _activeTypingFormat;
            if (flags == RichFormatFlags.None && start > 0 && start - 1 < _charFlags.Count)
            {
                flags = _charFlags[start - 1];
            }
        }
        else
        {
            flags = RichFormatFlags.Bold | RichFormatFlags.Italic | RichFormatFlags.Underline | RichFormatFlags.Strikeout;
            for (var i = start; i < end && i < _charFlags.Count; i++)
            {
                flags &= _charFlags[i];
            }
        }

        BoldButton.IsChecked = (flags & RichFormatFlags.Bold) != 0;
        ItalicButton.IsChecked = (flags & RichFormatFlags.Italic) != 0;
        UnderlineButton.IsChecked = (flags & RichFormatFlags.Underline) != 0;
        StrikeoutButton.IsChecked = (flags & RichFormatFlags.Strikeout) != 0;

        var lineIdx = 0;
        var running = 0;
        var lines = _currentText.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (start >= running && start <= running + lines[i].Length)
            {
                lineIdx = i;
                break;
            }
            running += lines[i].Length + 1;
        }

        BulletButton.IsChecked = lineIdx < _lineBullets.Count && _lineBullets[lineIdx];
    }

    private void OnBoldClicked(object? sender, RoutedEventArgs e) => ToggleBold();
    private void OnItalicClicked(object? sender, RoutedEventArgs e) => ToggleItalic();
    private void OnUnderlineClicked(object? sender, RoutedEventArgs e) => ToggleUnderline();
    private void OnStrikeoutClicked(object? sender, RoutedEventArgs e) => ToggleStrikeout();
    private void OnBulletClicked(object? sender, RoutedEventArgs e) => ToggleBullet();
    private void OnUndoClicked(object? sender, RoutedEventArgs e) => Undo();
    private void OnRedoClicked(object? sender, RoutedEventArgs e) => Redo();
}
