namespace Fixtura;

// The fixture's call recorders, by service type.
internal sealed class Recorders
{
    private readonly Dictionary<Type, object> _byService = [];
    private readonly List<Action> _saveBaselines = [];
    private readonly List<Action> _resets = [];

    public Recorders()
    {
    }

    // An override records with its parent's recorders until it records a service again.
    private Recorders(Recorders parent)
    {
        foreach (KeyValuePair<Type, object> recorder in parent._byService)
        {
            _byService[recorder.Key] = recorder.Value;
        }

        _saveBaselines.AddRange(parent._saveBaselines);
        _resets.AddRange(parent._resets);
    }

    public CallRecorder<TService> Add<TService>() where TService : class
    {
        var recorder = new CallRecorder<TService>();
        _byService[typeof(TService)] = recorder;
        _saveBaselines.Add(recorder.SaveBaseline);
        _resets.Add(recorder.Reset);
        return recorder;
    }

    public CallRecorder<TService> Find<TService>() where TService : class =>
        _byService.TryGetValue(typeof(TService), out object? recorder)
            ? (CallRecorder<TService>)recorder
            : throw new InvalidOperationException($"The fixture records no {TypeNames.Readable(typeof(TService))}; record "
                + $"it in Configure with host.Services.Record<{TypeNames.Readable(typeof(TService))}>().");

    public Recorders Copy() => new(this);

    public void SaveBaseline() => _saveBaselines.ForEach(save => save());

    public void Reset() => _resets.ForEach(reset => reset());
}
