using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Abstractions;

namespace ConditioningControlPanel.Shell;

/// <summary>The IUiDispatcher seam, backed by Avalonia's UI thread. (WPF would back it with its Dispatcher.)</summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
    public async Task InvokeAsync(Func<Task> action) => await Dispatcher.UIThread.InvokeAsync(action);
}
