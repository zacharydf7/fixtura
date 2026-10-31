using System.Reflection;

namespace Fixtura;

/// <summary>
/// Stands in for <typeparamref name="TService"/>, which <c>host.Services.Record&lt;TService&gt;()</c> replaced: records the
/// calls the app makes and answers them with a result a test set, or else a completed task or a default value.
/// <c>ResetDataAsync</c> clears the calls and brings back the results set in <c>Configure</c>.
/// </summary>
public sealed class CallRecorder<TService> where TService : class
{
    private readonly Lock _gate = new();
    private readonly List<RecordedCall> _calls = [];
    private readonly Dictionary<string, Func<RecordedCall, object?>> _results = [];
    private Dictionary<string, Func<RecordedCall, object?>>? _baseline;

    internal CallRecorder()
    {
        // A proxy can only stand in for an interface.
        if (!typeof(TService).IsInterface)
        {
            throw new ArgumentException($"{TypeNames.Readable(typeof(TService))} isn't an interface; Record<TService> records calls on an "
                + "interface the app registers, such as IEmailSender.");
        }

        Service = DispatchProxy.Create<TService, RecordingProxy>();
        ((RecordingProxy)(object)Service).Recorded = Answer;
    }

    /// <summary>The calls the app made, oldest first.</summary>
    public IReadOnlyList<RecordedCall> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    internal TService Service { get; }

    /// <summary>
    /// Waits until the app has made a call that <paramref name="match"/> accepts, any call unless given, and returns the
    /// first one, for a call the app makes after a request returns. Fails after <paramref name="timeout"/>, five seconds
    /// unless given.
    /// </summary>
    public Task<RecordedCall> WaitForCallAsync(Func<RecordedCall, bool>? match = null, TimeSpan? timeout = null) =>
        Waiting.UntilAsync(() => Task.FromResult(Calls.FirstOrDefault(match ?? (_ => true))
            ?? throw new InvalidOperationException($"The app made no matching call on {TypeNames.Readable(typeof(TService))}.")), timeout);

    /// <summary>
    /// Answers calls to the method by that name, such as <c>nameof(IRates.GetRateAsync)</c>, with what
    /// <paramref name="result"/> returns. A task's result can be given as is: it is wrapped in a completed task.
    /// </summary>
    public CallRecorder<TService> Returns(string method, Func<RecordedCall, object?> result)
    {
        List<string> methods = [.. typeof(TService).GetInterfaces().Prepend(typeof(TService))
            .SelectMany(type => type.GetMethods()).Select(candidate => candidate.Name).Distinct()];
        if (!methods.Contains(method))
        {
            throw new ArgumentException($"{TypeNames.Readable(typeof(TService))} has no method named '{method}'; it has "
                + $"{string.Join(", ", methods)}.", nameof(method));
        }

        lock (_gate)
        {
            _results[method] = result;
        }

        return this;
    }

    // The first call wins, so an override host can't make a test's results the baseline.
    internal void SaveBaseline()
    {
        lock (_gate)
        {
            _baseline ??= new Dictionary<string, Func<RecordedCall, object?>>(_results);
        }
    }

    internal void Reset()
    {
        lock (_gate)
        {
            _calls.Clear();
            if (_baseline is not null)
            {
                _results.Clear();
                foreach (KeyValuePair<string, Func<RecordedCall, object?>> result in _baseline)
                {
                    _results[result.Key] = result.Value;
                }
            }
        }
    }

    private object? Answer(MethodInfo method, object?[] arguments)
    {
        var call = new RecordedCall(method, [.. arguments]);
        Func<RecordedCall, object?>? result;
        lock (_gate)
        {
            _calls.Add(call);
            _results.TryGetValue(method.Name, out result);
        }

        return Results.Wrap(method, result?.Invoke(call), $"{TypeNames.Readable(typeof(TService))}.{method.Name}");
    }
}

// Calls through to its recorder; DispatchProxy needs a class it can derive from, with a parameterless constructor.
internal class RecordingProxy : DispatchProxy
{
    internal Func<MethodInfo, object?[], object?> Recorded { get; set; } = null!;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Recorded(targetMethod!, args ?? []);
}

// Turns a test's result, or none, into what the method returns: a completed task for an async method.
internal static class Results
{
    public static object? Wrap(MethodInfo method, object? value, string name)
    {
        Type returnType = method.ReturnType;
        if (value is not null && returnType.IsInstanceOfType(value))
        {
            return value;
        }

        bool isGeneric = returnType.IsGenericType;
        Type? taskOf = isGeneric && returnType.GetGenericTypeDefinition() == typeof(Task<>) ? returnType.GetGenericArguments()[0] : null;
        Type? valueTaskOf = isGeneric && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>)
            ? returnType.GetGenericArguments()[0]
            : null;
        Type? resultType = taskOf ?? valueTaskOf
            ?? (returnType == typeof(void) || returnType == typeof(Task) || returnType == typeof(ValueTask) ? null : returnType);

        // Without this, a result of the wrong type fails inside the app's call, naming neither the service nor the method.
        if (value is not null && resultType?.IsInstanceOfType(value) != true)
        {
            throw new InvalidOperationException($"The result given for {name} is a {TypeNames.Readable(value.GetType())}, "
                + $"but it returns {(resultType is null ? "no value" : "a " + TypeNames.Readable(resultType))}.");
        }

        object? result = value ?? (resultType is { IsValueType: true } ? Activator.CreateInstance(resultType) : null);
        if (taskOf is not null)
        {
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(taskOf).Invoke(null, [result]);
        }

        if (valueTaskOf is not null)
        {
            return typeof(ValueTask).GetMethod(nameof(ValueTask.FromResult))!.MakeGenericMethod(valueTaskOf).Invoke(null, [result]);
        }

        return returnType == typeof(Task) ? Task.CompletedTask : returnType == typeof(ValueTask) ? default(ValueTask) : result;
    }
}
