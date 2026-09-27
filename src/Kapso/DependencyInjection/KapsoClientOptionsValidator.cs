using Kapso;

using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Validates <see cref="KapsoClientOptions"/> at startup.
/// </summary>
/// <remarks>
/// Source-generated from the data annotations on the options type. The reflection
/// based <c>ValidateDataAnnotations()</c> would work too, but it is not safe under
/// trimming or Native AOT, and this package promises both.
/// </remarks>
[OptionsValidator]
internal sealed partial class KapsoClientOptionsValidator : IValidateOptions<KapsoClientOptions>;
