namespace KeyRadar.Windows.Configuration;

public sealed class RunningApplicationConfigurationRegistry(
    IReadOnlyList<IApplicationConfigurationReader> readers)
{
    public async Task<IReadOnlyList<LocalConfigurationHotkey>> ReadAsync(
        IReadOnlyList<RunningApplicationVariant> runningApplications,
        CancellationToken cancellationToken)
    {
        var results = new List<LocalConfigurationHotkey>();
        foreach (var application in runningApplications)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var reader in readers.Where(reader => reader.Supports(application.Variant)))
            {
                var discovered = await reader
                    .ReadAsync(application.Process, application.Variant, cancellationToken)
                    .ConfigureAwait(false);
                results.AddRange(discovered);
            }
        }

        return results;
    }
}
