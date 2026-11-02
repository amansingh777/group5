using Corrin.Api.Dtos;
using Corrin.Api.Validators;

namespace Corrin.Tests;

public class JobUpdateRequestValidatorTests
{
    private static readonly JobUpdateRequestValidator Validator = new();

    private static JobUpdateRequest Request(string status) => new(null, "P3", status, null, null, null, null, null, null);

    [Theory]
    [InlineData("OnSite")]
    [InlineData("NotCompleted")]
    [InlineData("Requested")]
    [InlineData("Triaged")]
    [InlineData("Allocated")]
    [InlineData("Scheduled")]
    [InlineData("Completed")]
    [InlineData("Cancelled")]
    public void Validate_RecognisedStatus_IsValid(string status) =>
        Assert.True(Validator.Validate(Request(status)).IsValid);

    [Theory]
    [InlineData("onsite")]
    [InlineData("Done")]
    [InlineData("InProgress")] // Not in Domain/WorkOrderStatus.All; the legacy PUT vocabulary is now the work-order vocabulary.
    [InlineData("")]
    public void Validate_UnrecognisedStatus_IsRejectedOnStatus(string status)
    {
        var result = Validator.Validate(Request(status));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Status");
    }
}
