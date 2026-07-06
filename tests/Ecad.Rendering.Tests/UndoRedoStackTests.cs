using Ecad.Rendering.Canvas;
using Xunit;

namespace Ecad.Rendering.Tests;

public class UndoRedoStackTests
{
    private sealed class RecordingCommand(List<string> log, string name) : IUndoableCommand
    {
        public void Do() => log.Add($"do:{name}");
        public void Undo() => log.Add($"undo:{name}");
    }

    [Fact]
    public void Execute_RunsTheCommandImmediately()
    {
        var log = new List<string>();
        var stack = new UndoRedoStack();

        stack.Execute(new RecordingCommand(log, "A"));

        Assert.Equal(["do:A"], log);
    }

    [Fact]
    public void Undo_ThenRedo_RepeatsDoAfterUndo()
    {
        var log = new List<string>();
        var stack = new UndoRedoStack();
        stack.Execute(new RecordingCommand(log, "A"));

        stack.Undo();
        stack.Redo();

        Assert.Equal(["do:A", "undo:A", "do:A"], log);
    }

    [Fact]
    public void Undo_WithNothingToUndo_DoesNothing()
    {
        var stack = new UndoRedoStack();

        stack.Undo(); // should not throw

        Assert.False(stack.CanUndo);
    }

    [Fact]
    public void Execute_AfterUndo_ClearsRedoHistory()
    {
        var log = new List<string>();
        var stack = new UndoRedoStack();
        stack.Execute(new RecordingCommand(log, "A"));
        stack.Undo();

        stack.Execute(new RecordingCommand(log, "B"));

        Assert.False(stack.CanRedo);
        Assert.Equal(["do:A", "undo:A", "do:B"], log);
    }

    [Fact]
    public void CanUndoCanRedo_ReflectStackState()
    {
        var stack = new UndoRedoStack();
        Assert.False(stack.CanUndo);
        Assert.False(stack.CanRedo);

        stack.Execute(new RecordingCommand([], "A"));
        Assert.True(stack.CanUndo);
        Assert.False(stack.CanRedo);

        stack.Undo();
        Assert.False(stack.CanUndo);
        Assert.True(stack.CanRedo);
    }
}
