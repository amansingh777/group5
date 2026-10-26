using Microsoft.AspNetCore.Mvc.Testing;

namespace Corrin.Tests;

public class TestApiFactory : WebApplicationFactory<Program>
{
}

[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<TestApiFactory>
{
}
