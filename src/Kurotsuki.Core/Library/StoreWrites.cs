using Kurotsuki.Core.I18n;

namespace Kurotsuki.Core.Library;

/// <summary>
/// Applies a change to memory first (so the UI answers at once), then writes it to the database; if the write fails
/// the change is undone and the user told. Writes are short SQLite statements, so they run on the calling (UI) thread.
/// </summary>
internal static class StoreWrites
{
    public static void Persist(string errorKey, Action<string> onError, Action write, Action undo)
    {
        try
        {
            write();
        }
        catch (Exception e)
        {
            undo();
            onError(Messages.Get(errorKey, e.Message));
        }
    }
}
