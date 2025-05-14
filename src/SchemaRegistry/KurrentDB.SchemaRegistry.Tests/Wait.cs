namespace KurrentDB.SchemaRegistry.Tests;

static class Wait {
    static readonly TimeSpan DefaultDelay     = TimeSpan.FromMilliseconds(100);
    static readonly Action?  DefaultOnTimeout = () => throw new TimeoutException();
    static readonly TimeSpan DefaultTimeout   = TimeSpan.FromSeconds(60);

    public static Task Until(Func<Task<bool>> predicate, TimeSpan? delay = null, TimeSpan? timeout = null, Action? onTimeout = null, CancellationToken cancellationToken = default) {
        onTimeout ??= DefaultOnTimeout;

        return Task.Run(
            async () => {
                bool satisfied;

                do {
                    satisfied = await predicate();

                    if (!satisfied)
                        await Task.Delay(delay.GetValueOrDefault(DefaultDelay), cancellationToken);
                } while (!satisfied);
            },
            cancellationToken
        ).WithTimeout(timeout.GetValueOrDefault(DefaultTimeout), onTimeout, cancellationToken);
    }

    public static async Task UntilAsserted(Func<Task> assertion, TimeSpan? delay = null, TimeSpan? timeout = null, Action? onTimeout = null, CancellationToken cancellationToken = default) {
        Exception? lastException = null;

        await Until(
            async () => {
                lastException = null;

                try {
                    await assertion();
                }
                catch (Exception ex) {
                    lastException = ex;
                }

                return lastException == null;
            },
            delay,
            timeout,
            () => {
                onTimeout?.Invoke();
                throw lastException!;
            },
            cancellationToken
        );
    }
}

static class TaskExtensions {
    public static async Task WithTimeout(this Task task, TimeSpan timeout, Action? onTimeout, CancellationToken cancellationToken = default) {
        if (await Task.WhenAny(task, Task.Delay(timeout, cancellationToken)) != task)
            onTimeout?.Invoke();

        await task;
    }
}