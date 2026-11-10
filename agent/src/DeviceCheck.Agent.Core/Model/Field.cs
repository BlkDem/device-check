using System.Collections;
using System.Text.Json.Serialization;

namespace DeviceCheck.Agent.Core.Model;

/// <summary>How much a value can be trusted. Serialized lowercase: exact | approximate | detected | unavailable.</summary>
public enum Confidence
{
    /// <summary>Read directly from an authoritative OS / firmware / hardware interface.</summary>
    Exact,
    /// <summary>Rounded, estimated or measured value.</summary>
    Approximate,
    /// <summary>Inferred indirectly (name parsing, reference tables, derived calculations).</summary>
    Detected,
    /// <summary>No value: see <see cref="Field{T}.Reason"/>.</summary>
    Unavailable,
}

/// <summary>
/// A single hardware parameter with provenance. Mirrors <c>Field&lt;T&gt;</c> of the web HardwareReport contract:
/// <c>{ value, source, confidence, collected_at, reason?, note? }</c>.
/// Use nullable value types (<c>long?</c>, <c>bool?</c>) for <typeparamref name="T"/> so that "no value" serializes as null.
/// </summary>
public sealed class Field<T>
{
    public T Value { get; init; } = default!;
    public string Source { get; init; } = "—";
    public Confidence Confidence { get; init; } = Confidence.Unavailable;
    public DateTimeOffset CollectedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Code explaining why the value is missing (only when unavailable).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }

    /// <summary>Code with a caveat for a present value (derived, from reference table, ...).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Note { get; init; }

    [JsonIgnore]
    public bool HasValue => Confidence != Confidence.Unavailable;

    public override string ToString() => HasValue ? $"{Value} [{Confidence}, {Source}]" : $"— [{Reason}]";
}

/// <summary>Field factory. Any empty value (null, blank string, empty collection, NaN) automatically becomes unavailable.</summary>
public static class F
{
    /// <summary>Standard reason codes.</summary>
    public static class Reasons
    {
        public const string NotCollected = "not_collected";
        public const string NotReported = "not_reported";
        public const string NotExposed = "not_exposed";
        public const string Privacy = "privacy";
        public const string Error = "error";
        public const string Timeout = "timeout";
        public const string RequiresAdmin = "requires_admin";
        public const string NoReliableSource = "no_reliable_source";
        public const string FirmwarePlaceholder = "firmware_placeholder";
        public const string NotApplicable = "not_applicable";
        public const string Unsupported = "api_unsupported";
    }

    public static bool IsEmpty(object? v) => v switch
    {
        null => true,
        string s => string.IsNullOrWhiteSpace(s),
        double d => !double.IsFinite(d),
        ICollection c => c.Count == 0,
        _ => false,
    };

    public static Field<T> Of<T>(T value, string source, Confidence confidence, string? note = null, string? emptyReason = null)
    {
        if (confidence == Confidence.Unavailable || IsEmpty(value))
            return Unavailable<T>(source, emptyReason ?? Reasons.NotReported);
        return new Field<T> { Value = value, Source = source, Confidence = confidence, CollectedAt = DateTimeOffset.UtcNow, Note = note };
    }

    public static Field<T> Exact<T>(T value, string source, string? note = null) => Of(value, source, Confidence.Exact, note);
    public static Field<T> Detected<T>(T value, string source, string? note = null) => Of(value, source, Confidence.Detected, note);
    public static Field<T> Approx<T>(T value, string source, string? note = null) => Of(value, source, Confidence.Approximate, note);

    public static Field<T> Unavailable<T>(string source, string reason) =>
        new() { Value = default!, Source = source, Confidence = Confidence.Unavailable, CollectedAt = DateTimeOffset.UtcNow, Reason = reason };

    /// <summary>Default for fields that a collector has not filled yet.</summary>
    public static Field<T> None<T>() => Unavailable<T>("—", Reasons.NotCollected);

    /// <summary>Identifier deliberately not collected (serial numbers, host name, MAC...).</summary>
    public static Field<T> Private<T>() => Unavailable<T>("—", Reasons.Privacy);

    /// <summary>First field that has a value, otherwise the last one (keeps its reason).</summary>
    public static Field<T> FirstAvailable<T>(params Field<T>[] candidates) =>
        candidates.FirstOrDefault(f => f.HasValue) ?? candidates[^1];

    /// <summary>Confidence of a value derived from several inputs = the weakest of them.</summary>
    public static Confidence Weakest(params Confidence[] cs)
    {
        if (cs.Contains(Confidence.Unavailable)) return Confidence.Unavailable;
        if (cs.Contains(Confidence.Approximate)) return Confidence.Approximate;
        if (cs.Contains(Confidence.Detected)) return Confidence.Detected;
        return Confidence.Exact;
    }
}
