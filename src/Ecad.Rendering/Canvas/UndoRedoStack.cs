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
