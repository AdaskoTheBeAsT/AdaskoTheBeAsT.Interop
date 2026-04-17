namespace AdaskoTheBeAsT.Interop.Execution;

internal static class ExecutionHelpers
{
    internal static void TryIgnore(Action action)
    {
        var safeAction = action ?? throw new ArgumentNullException(nameof(action));

        try
        {
            safeAction();
        }
        catch (Exception)
        {
            GC.KeepAlive(safeAction);
        }
    }
}
