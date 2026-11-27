using System.Text.Json;

namespace Embm.EFiscal.Api.Modules.CompanyConfiguration;

internal interface ICompanyMailSampleSink
{
    bool Available { get; }
    Task WriteAsync(Guid company, string documentType, object message, CancellationToken ct);
}

internal sealed class CompanyMailSampleSink(IWebHostEnvironment environment, IConfiguration configuration) : ICompanyMailSampleSink
{
    public bool Available => environment.IsDevelopment() &&
        !string.IsNullOrWhiteSpace(configuration["Identity:DevelopmentMailbox"]);

    public async Task WriteAsync(Guid company, string documentType, object message, CancellationToken ct)
    {
        if (!Available) throw new InvalidOperationException("The local preview mailbox is unavailable.");
        var directory = Path.Combine(Path.GetFullPath(configuration["Identity:DevelopmentMailbox"]!), "company-previews");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
        {
            companyId = company, documentType, mode = "development-preview", sender = "preview@example.test",
            recipient = "customer@example.test", message
        }, TemplateRules.Json), ct);
    }
}
