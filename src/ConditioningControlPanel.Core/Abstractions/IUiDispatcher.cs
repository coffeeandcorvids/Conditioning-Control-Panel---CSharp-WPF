namespace ConditioningControlPanel.Core.Abstractions;

/// <summary>
/// The #1 portability seam (per upstream PORTABILITY_REPORT). WPF code couples to
/// Application.Current.Dispatcher everywhere; portable Core code depends on THIS instead.
/// WPF supplies a Dispatcher-backed impl; headless/Avalonia/tests supply their own.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>Marshal an action onto the UI context (or run inline when headless).</summary>
    void Post(Action action);
    /// <summary>Await an async action on the UI context.</summary>
    Task InvokeAsync(Func<Task> action);
}

/// <summary>Headless default: runs inline. Used by the Core, tests, and any non-UI host (the Pi).</summary>
public sealed class ImmediateUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
    public Task InvokeAsync(Func<Task> action) => action();
}
