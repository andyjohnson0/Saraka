namespace Saraka.AndroidSaf.Tests;

/// <summary>
/// Runs an operation on a thread whose synchronization context never runs the callbacks posted to
/// it, and blocks on the result. If the operation needed that context to finish, it would never
/// finish, so a timeout shows a deadlock.
/// </summary>
internal static class BlockingContext
{
    /// <summary>
    /// Starts an operation on such a thread and blocks until it finishes.
    /// </summary>
    /// <typeparam name="T">The operation's result type.</typeparam>
    /// <param name="operation">The operation.</param>
    /// <returns>The operation's result.</returns>
    /// <exception cref="TimeoutException">The operation didn't finish within 30 seconds.</exception>
    public static T Run<T>(Func<Task<T>> operation)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new NeverRunsContext());
            try
            {
                var task = operation();
                if (task.Wait(TimeSpan.FromSeconds(30)))
                    result = task.Result;
                else
                    error = new TimeoutException("The operation deadlocked waiting for the synchronization context.");
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.Start();
        thread.Join();
        if (error != null)
            throw error;
        return result;
    }

    /// <summary>
    /// A synchronization context that drops every callback posted to it.
    /// </summary>
    private sealed class NeverRunsContext : SynchronizationContext
    {
        /// <inheritdoc/>
        public override void Post(SendOrPostCallback d, object? state)
        {
        }

        /// <inheritdoc/>
        public override void Send(SendOrPostCallback d, object? state) =>
            throw new InvalidOperationException("Send isn't supported.");
    }
}
