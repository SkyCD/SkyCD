using System;
using System.Threading.Tasks;
using SkyCD.Core.UndoRedo;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class UndoRedoHistoryTests
{
    // --- grouping -----------------------------------------------------------

    [Fact]
    public async Task Transaction_GroupsActionsIntoSingleUndoEntry()
    {
        var history = new UndoRedoHistory();
        int value = 0;

        history.BeginTransaction("Multi-step");
        history.PushAction(new LambdaAction("step1", () => value--, () => value++));
        history.PushAction(new LambdaAction("step2", () => value--, () => value++));
        history.CommitTransaction();

        // Both actions form one undo entry
        Assert.Equal(1, history.UndoCount);

        value = 10;
        await history.UndoAsync();

        // Both decrements were undone in one step; item is now on redo stack
        Assert.Equal(8, value);
        Assert.Equal(0, history.UndoCount);
        Assert.Equal(1, history.RedoCount);
    }

    [Fact]
    public async Task Transaction_RedoAppliesAllActionsForward()
    {
        var history = new UndoRedoHistory();
        int value = 0;

        history.BeginTransaction("Multi-step");
        history.PushAction(new LambdaAction("inc1", undo: () => value--, redo: () => value++));
        history.PushAction(new LambdaAction("inc2", undo: () => value--, redo: () => value++));
        history.CommitTransaction();

        value = 10;
        await history.UndoAsync();
        await history.RedoAsync();

        Assert.Equal(10, value);
    }

    [Fact]
    public void Transaction_RollbackDiscardsAllActions()
    {
        var history = new UndoRedoHistory();

        history.BeginTransaction("aborted");
        history.PushAction(new LambdaAction("a", () => { }, () => { }));
        history.RollbackTransaction();

        Assert.Equal(0, history.UndoCount);
        Assert.Equal(0, history.RedoCount);
    }

    [Fact]
    public void Transaction_EmptyCommitDoesNotPushToHistory()
    {
        var history = new UndoRedoHistory();
        history.BeginTransaction("empty");
        history.CommitTransaction();

        Assert.Equal(0, history.UndoCount);
    }

    // --- failures -----------------------------------------------------------

    [Fact]
    public async Task UndoAsync_WhenActionThrows_HistoryRemainsUnchanged()
    {
        var history = new UndoRedoHistory();
        history.PushAction(new LambdaAction("faulty", undo: () => throw new InvalidOperationException("undo fail"), redo: () => { }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => history.UndoAsync());

        // The item must be back on the undo stack – redo stack still empty
        Assert.Equal(1, history.UndoCount);
        Assert.Equal(0, history.RedoCount);
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public async Task RedoAsync_WhenActionThrows_HistoryRemainsUnchanged()
    {
        var history = new UndoRedoHistory();
        history.PushAction(new LambdaAction("faulty", undo: () => { }, redo: () => throw new InvalidOperationException("redo fail")));

        await history.UndoAsync();
        Assert.Equal(1, history.RedoCount);

        await Assert.ThrowsAsync<InvalidOperationException>(() => history.RedoAsync());

        Assert.Equal(0, history.UndoCount);
        Assert.Equal(1, history.RedoCount);
        Assert.True(history.CanRedo);
    }

    // --- redo invalidation --------------------------------------------------

    [Fact]
    public async Task PushAction_AfterUndo_InvalidatesRedoStack()
    {
        var history = new UndoRedoHistory();
        history.PushAction(new LambdaAction("a", () => { }, () => { }));
        await history.UndoAsync();
        Assert.True(history.CanRedo);

        history.PushAction(new LambdaAction("b", () => { }, () => { }));

        Assert.False(history.CanRedo);
        Assert.Equal(0, history.RedoCount);
    }

    [Fact]
    public async Task TransactionCommit_AfterUndo_InvalidatesRedoStack()
    {
        var history = new UndoRedoHistory();
        history.PushAction(new LambdaAction("first", () => { }, () => { }));
        await history.UndoAsync();

        history.BeginTransaction("override");
        history.PushAction(new LambdaAction("override-child", () => { }, () => { }));
        history.CommitTransaction();

        Assert.False(history.CanRedo);
    }

    // --- memory eviction ----------------------------------------------------

    [Fact]
    public void PushAction_ExceedingMaxSize_EvictsOldestItem()
    {
        var history = new UndoRedoHistory(maxHistorySize: 3);
        for (int i = 0; i < 5; i++)
        {
            history.PushAction(new LambdaAction($"action{i}", () => { }, () => { }));
        }

        Assert.Equal(3, history.UndoCount);
        Assert.Equal("action4", history.UndoActionName);
    }

    [Fact]
    public void PushAction_ExceedingMaxSize_AdjustsSavedRevisionIndexDownward()
    {
        var history = new UndoRedoHistory(maxHistorySize: 3);
        history.PushAction(new LambdaAction("a", () => { }, () => { }));
        history.PushAction(new LambdaAction("b", () => { }, () => { }));
        history.MarkAsSaved();      // savedRevisionIndex = 2

        // Push enough to evict the first two entries
        history.PushAction(new LambdaAction("c", () => { }, () => { }));
        history.PushAction(new LambdaAction("d", () => { }, () => { }));
        history.PushAction(new LambdaAction("e", () => { }, () => { }));

        // savedRevisionIndex should have been decremented twice but floored at 0
        Assert.True(history.IsDirty, "History should be dirty because saved state was evicted.");
    }

    [Fact]
    public void SavedRevisionIndex_NeverGoesNegative_WhenAllEvicted()
    {
        var history = new UndoRedoHistory(maxHistorySize: 2);
        history.PushAction(new LambdaAction("x", () => { }, () => { }));
        history.MarkAsSaved();      // savedRevisionIndex = 1

        // Push 3 more – evicts everything that was there before
        history.PushAction(new LambdaAction("y", () => { }, () => { }));
        history.PushAction(new LambdaAction("z", () => { }, () => { }));
        history.PushAction(new LambdaAction("w", () => { }, () => { }));

        Assert.True(history.IsDirty);
        Assert.Equal(0, history.SavedRevisionIndex);
    }

    // --- save-point transitions ---------------------------------------------

    [Fact]
    public void IsDirty_FalseAfterMarkAsSaved()
    {
        var history = new UndoRedoHistory();
        history.PushAction(new LambdaAction("a", () => { }, () => { }));
        history.MarkAsSaved();

        Assert.False(history.IsDirty);
    }

    [Fact]
    public async Task IsDirty_BecomesClean_WhenUndoReturnsToSavePoint()
    {
        var history = new UndoRedoHistory();
        history.PushAction(new LambdaAction("a", () => { }, () => { }));
        history.MarkAsSaved();
        history.PushAction(new LambdaAction("b", () => { }, () => { }));

        Assert.True(history.IsDirty);

        await history.UndoAsync();

        Assert.False(history.IsDirty);
    }

    [Fact]
    public async Task IsDirty_BecomesTrue_AfterRedoPassesSavePoint()
    {
        var history = new UndoRedoHistory();
        history.PushAction(new LambdaAction("a", () => { }, () => { }));
        history.MarkAsSaved();
        history.PushAction(new LambdaAction("b", () => { }, () => { }));
        await history.UndoAsync();     // back to save point → clean
        Assert.False(history.IsDirty);

        await history.RedoAsync();     // past save point → dirty

        Assert.True(history.IsDirty);
    }

    [Fact]
    public async Task ResetForNewCatalog_ClearsBothStacks_AndResetsDirtyState()
    {
        var history = new UndoRedoHistory();
        history.PushAction(new LambdaAction("a", () => { }, () => { }));
        await history.UndoAsync();   // undo stack empty, redo has 1

        history.ResetForNewCatalog();

        Assert.Equal(0, history.UndoCount);
        Assert.Equal(0, history.RedoCount);
        Assert.Equal(-1, history.SavedRevisionIndex);
    }

    [Fact]
    public void Clear_ResetsEverythingIncludingSavePoint()
    {
        var history = new UndoRedoHistory();
        history.PushAction(new LambdaAction("a", () => { }, () => { }));
        history.MarkAsSaved();
        history.Clear();

        Assert.Equal(0, history.UndoCount);
        Assert.Equal(0, history.RedoCount);
        Assert.Equal(-1, history.SavedRevisionIndex);
        // After Clear(), savedRevisionIndex=-1 and undoCount=0, so IsDirty is true.
        // The caller should MarkAsSaved() after clearing if the cleared state is considered clean.
        Assert.True(history.IsDirty);
    }

    // --- helper action type -------------------------------------------------

    private sealed class LambdaAction : UndoableActionBase
    {
        private readonly Action _undo;
        private readonly Action _redo;

        public LambdaAction(string name, Action undo, Action redo)
            : base(name)
        {
            _undo = undo;
            _redo = redo;
        }

        public override bool CanUndo => true;
        public override bool CanRedo => true;

        public override Task UndoAsync() { _undo(); return Task.CompletedTask; }
        public override Task RedoAsync() { _redo(); return Task.CompletedTask; }
    }
}
