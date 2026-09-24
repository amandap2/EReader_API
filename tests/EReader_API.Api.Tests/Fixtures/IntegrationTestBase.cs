namespace EReader_API.Api.Tests.Fixtures;

[Collection("Api")]
public abstract class IntegrationTestBase(EReaderApiFactory factory) : IAsyncLifetime
{
    protected HttpClient Client { get; } = factory.CreateClient();
    protected EReaderApiFactory Factory { get; } = factory;

    // xUnit cria uma instância nova da classe de teste por [Fact]/[Theory] case, então isto
    // roda antes de cada teste, não uma vez por classe.
    public Task InitializeAsync() => Factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;
}
