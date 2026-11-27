using System.Text.Json;

namespace Embm.EFiscal.Api.Modules.Identity;

public interface IIdentityEmail
{
    bool Available { get; }
    Task SendAsync(string recipient, string subject, string path, string token, CancellationToken ct);
}

internal sealed class IdentityEmail(IWebHostEnvironment environment, IConfiguration config, ILogger<IdentityEmail> logger) : IIdentityEmail
{
    public bool Available => environment.IsDevelopment() && !string.IsNullOrWhiteSpace(config["Identity:DevelopmentMailbox"]);

    public async Task SendAsync(string recipient, string subject, string path, string token, CancellationToken ct)
    {
        if (!Available) throw new InvalidOperationException("No identity email provider is configured.");
        var origin = new Uri(config["Identity:PublicOrigin"] ?? "http://127.0.0.1:5173");
        if (!origin.IsLoopback || origin.Scheme is not ("http" or "https")) throw new InvalidOperationException("Development links must use loopback.");
        var url = new Uri(origin, path + "#token=" + Uri.EscapeDataString(token));
        var directory = Path.GetFullPath(config["Identity:DevelopmentMailbox"]!);
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfffffff}-{Guid.NewGuid():N}.json");
        var temporary = file + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new { recipient, subject, url = url.AbsoluteUri }), ct);
            File.Move(temporary, file);
        }
        catch (IOException)
        {
            // The user can request a new message. Keep the delivery failure free of addresses and secrets.
            logger.LogError("Development identity email could not be written.");
        }
    }
}
