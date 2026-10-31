namespace Fixtura;

internal static class TypeNames
{
    // A type nested in a generic one counts as generic but has no arity mark of its own, so it keeps its name.
    public static string Readable(Type type)
    {
        int arity = type.Name.IndexOf('`');
        return !type.IsGenericType || arity < 0
            ? type.Name
            : $"{type.Name[..arity]}<{string.Join(", ", type.GetGenericArguments().Select(Readable))}>";
    }
}
