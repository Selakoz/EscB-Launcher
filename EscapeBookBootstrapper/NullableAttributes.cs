#if !NET5_0_OR_GREATER
// Полифилл атрибута nullable-анализа: в .NET Framework 4.8 его нет,
// а компилятору он нужен для корректного анализа TryParse-подобных методов.
namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Parameter)]
    internal sealed class NotNullWhenAttribute : Attribute
    {
        public NotNullWhenAttribute(bool returnValue)
        {
            ReturnValue = returnValue;
        }

        public bool ReturnValue { get; }
    }
}
#endif
