using Shared.Contract.Abstractions;

namespace Shared.Contract.Common
{
    public sealed class ValidationResult : Result, IValidationResult
    {
        public Error[] Errors { get; }
        private ValidationResult(Error[] errors) : base(false, IValidationResult.ValidationError)
        {
            Errors = errors;
        }
        public static ValidationResult WithErrors(Error[] errors) => new(errors);
    }
}
