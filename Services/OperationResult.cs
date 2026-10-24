namespace UbuntuLedger.Services;

public record OperationResult(bool Succeeded, string? Error = null, int? Id = null)
{
    public static OperationResult Ok(int? id = null) => new(true, null, id);
    public static OperationResult Fail(string error) => new(false, error);
}
