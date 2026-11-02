namespace GB.Experiments;

public interface ISerialRequestHandler
{
    Task<string> GetPageAsync(CancellationToken cancellationToken);
    Task<int> GetPrimeAsync(int index, CancellationToken cancellationToken);
}
