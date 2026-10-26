namespace Corrin.Api.Infrastructure;

public class NotFoundException : Exception
{
    public NotFoundException(string what, object id) : base($"{what} {id} was not found.") { }
}

public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}

public class BusinessRuleException : Exception
{
    public BusinessRuleException(string message) : base(message) { }
}
