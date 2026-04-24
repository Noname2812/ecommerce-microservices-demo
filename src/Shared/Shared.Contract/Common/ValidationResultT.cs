using Shared.Contract.Abstractions;

namespace Shared.Contract.Common
{
    public sealed class ValidationResult<T> : Result<T>, IValidationResult
    {
        public Error[] Errors { get; }
        private ValidationResult(Error[] errors) : base(default, false, IValidationResult.ValidationError)
        {
            Errors = errors;
        }
        public static ValidationResult<T> WithErrors(Error[] errors) => new(errors);
    }
}
