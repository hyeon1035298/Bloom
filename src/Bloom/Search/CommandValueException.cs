using System;

namespace Bloom.Search;

/// A 내 명령 value (typed input, clipboard text) that its template would hand to a program in a way the program could
/// split or read as more than one argument (UserCommands.Programs): the command doesn't run, and the message, for the
/// user, says which character to remove.
internal sealed class CommandValueException : InvalidOperationException
{
    public CommandValueException() { }

    public CommandValueException(string message) : base(message) { }

    public CommandValueException(string message, Exception inner) : base(message, inner) { }
}
