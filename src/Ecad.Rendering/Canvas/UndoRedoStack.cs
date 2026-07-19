namespace Ecad.Rendering.Canvas;

public interface IUndoableCommand
{
    void Do();
    void Undo();
}

/// <summary>Generic undo/redo command stack — no WPF or Ecad.Data dependency, drives whatever IUndoableCommand implementations the caller provides.</summary>
public sealed class UndoRedoStack
{
    private readonly Stack<IUndoableCommand> _undoStack = new();
    private readonly Stack<IUndoableCommand> _redoStack = new();

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>Runs the command and pushes it onto the undo stack. Any pending redo history is discarded — the usual behavior once a new action is taken after an undo.</summary>
    public void Execute(IUndoableCommand command)
    {
        command.Do();
        _undoStack.Push(command);
        _redoStack.Clear();
    }

    /// <summary>Pushes a command that has ALREADY run (its Do() was already called by the caller)
    /// onto the undo stack, without running it again — for composing a triggering action with
    /// cascading side effects decided only after the triggering action's own Do() runs (e.g.
    /// auto-connect's new/broken wires, which depend on the placement's post-move position) into one
    /// atomic undo step, rather than pushing the triggering command and each side effect separately.</summary>
    public void Record(IUndoableCommand command)
    {
        _undoStack.Push(command);
        _redoStack.Clear();
    }

    public void Undo()
    {
        if (_undoStack.Count == 0) return;
        var command = _undoStack.Pop();
        command.Undo();
        _redoStack.Push(command);
    }

    public void Redo()
    {
        if (_redoStack.Count == 0) return;
        var command = _redoStack.Pop();
        command.Do();
        _undoStack.Push(command);
    }
}
