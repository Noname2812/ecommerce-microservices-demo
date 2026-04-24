using Shared.Contract.Common;

namespace Shared.Contract.Abstractions
{
    public interface IValidationResult
    {
        public static readonly Error ValidationError = new("ValidationError", "A validation problem occurred");
        Error[] Errors { get; }
    }
}
