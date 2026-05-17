using Shared.Kernel.Primitives;

namespace UrbanX.Order.Application.Usecases.V1.Command.Common;

internal static class ParallelValidator
{
    public static async Task<Result> RunAsync(
        CancellationToken ct,
        params Func<CancellationToken, Task<Result>>[] validators)
    {
        if (validators is null || validators.Length == 0)
            return Result.Success();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var running = validators.Select(v => v(cts.Token)).ToList();

        while (running.Count > 0)
        {
            var completed = await Task.WhenAny(running);
            running.Remove(completed);

            var result = await completed;
            if (result.IsFailure)
            {
                cts.Cancel();
                _ = Task.WhenAll(running).ContinueWith(
                    _ => { },
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
                return result;
            }
        }

        return Result.Success();
    }
}
