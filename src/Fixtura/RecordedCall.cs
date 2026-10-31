using System.Reflection;

namespace Fixtura;

/// <summary>A call the app made on a service a fixture records.</summary>
public sealed class RecordedCall
{
    private readonly ParameterInfo[] _parameters;

    internal RecordedCall(MethodInfo method, object?[] arguments)
    {
        Method = method.Name;
        _parameters = method.GetParameters();
        Arguments = arguments;
    }

    /// <summary>The name of the method the app called, such as <c>SendEmailAsync</c>.</summary>
    public string Method { get; }

    /// <summary>The arguments, in the method's parameter order.</summary>
    public IReadOnlyList<object?> Arguments { get; }

    /// <summary>The argument for the parameter by that name, such as <c>Arg&lt;string&gt;("confirmationLink")</c>.</summary>
    public T Arg<T>(string parameterName)
    {
        int index = Array.FindIndex(_parameters, parameter => parameter.Name == parameterName);
        if (index < 0)
        {
            throw new ArgumentException($"{Method} has no parameter named '{parameterName}'; it has "
                + $"{string.Join(", ", _parameters.Select(parameter => parameter.Name))}.", nameof(parameterName));
        }

        return (T)Arguments[index]!;
    }
}
