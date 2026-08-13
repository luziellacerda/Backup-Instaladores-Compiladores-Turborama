using System.Security;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
    return await OnlineServerSelfTest.RunAsync();

var configuration = OnlineServerConfiguration.Load();
using var repository = new OnlineStateRepository(configuration.StateFile, configuration.StateIntegrityKey,
    configuration.StateEncryptionKey);

if (args.Length > 0 && args[0].Equals("--create-license", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length is < 4 or > 5)
        throw new InvalidOperationException("Uso: --create-license CLIENTE LICENCA PERFIL [MAX_MAQUINAS]");
    var maximumDevices = args.Length == 5 && int.TryParse(args[4], out var parsed) ? parsed : 1;
    var code = repository.CreateLicense(args[1], args[2],
        OnlineProtectionProfileCodec.Parse(args[3]), maximumDevices);
    Console.WriteLine("Licenca criada. O codigo abaixo aparece uma unica vez:");
    Console.WriteLine(code);
    return 0;
}

if (args.Length > 0 && args[0].Equals("--list-licenses", StringComparison.OrdinalIgnoreCase))
{
    foreach (var item in repository.ListLicenses())
        Console.WriteLine($"{item.LicenseId} | {item.CustomerId} | {item.Status} | {item.BindingType} | {item.Devices.Count}/{item.MaximumDevices}");
    return 0;
}

if (args.Length == 2 && args[0].Equals("--list-devices", StringComparison.OrdinalIgnoreCase))
{
    foreach (var item in repository.ListDevices(args[1]))
        Console.WriteLine($"{item.Descriptor.DeviceId} | {item.Status} | {item.Descriptor.BindingType} | {item.Descriptor.AgentVersion} | ultimo={item.LastContactUnixSeconds} | recusas={item.RejectedAttempts}");
    return 0;
}

if (args.Length == 3 && args[0].Equals("--set-license-status", StringComparison.OrdinalIgnoreCase))
{
    repository.SetLicenseStatus(args[1], args[2]);
    Console.WriteLine("Status da licenca atualizado.");
    return 0;
}

if (args.Length == 4 && args[0].Equals("--set-device-status", StringComparison.OrdinalIgnoreCase))
{
    repository.SetDeviceStatus(args[1], args[2], args[3]);
    Console.WriteLine("Status da maquina atualizado.");
    return 0;
}

if (args.Length == 3 && args[0].Equals("--force-reauth", StringComparison.OrdinalIgnoreCase))
{
    repository.ForceReauthentication(args[1], args[2]);
    Console.WriteLine("Sessao da maquina encerrada; nova autenticacao sera exigida.");
    return 0;
}

if (args.Length == 2 && args[0].Equals("--issue-activation-code", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("Novo codigo de uso unico:");
    Console.WriteLine(repository.IssueActivationCode(args[1]));
    return 0;
}

if (args.Length == 7 && args[0].Equals("--set-prices", StringComparison.OrdinalIgnoreCase))
{
    var minutes = new[] { 15, 30, 45, 60, 120 };
    var prices = new Dictionary<int, long>();
    for (var index = 0; index < minutes.Length; index++)
    {
        if (!long.TryParse(args[index + 2], out var cents))
            throw new InvalidOperationException("Os precos devem ser informados em centavos inteiros.");
        prices[minutes[index]] = cents;
    }
    repository.SetPackagePrices(args[1], prices);
    Console.WriteLine("Tabela de precos vinculada a licenca.");
    return 0;
}

if (args.Length == 3 && args[0].Equals("--set-mercadopago", StringComparison.OrdinalIgnoreCase))
{
    Console.Write("Cole o Access Token do cliente e pressione Enter: ");
    var token = ReadSecret();
    Console.WriteLine();
    try
    {
        repository.SetMercadoPagoConnection(args[1], args[2], token);
        Console.WriteLine("Conexao Mercado Pago criptografada para o cliente. O token nao foi exibido nem salvo em texto aberto.");
        return 0;
    }
    finally { token = ""; }
}

if (args.Length != 0)
    throw new InvalidOperationException("Comando desconhecido. Use --self-test, --create-license, --list-licenses, --list-devices, --issue-activation-code, --set-prices, --set-mercadopago, --set-license-status, --set-device-status ou --force-reauth.");

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = OnlineLicenseProtocol.MaximumBodyBytes);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1),
                AutoReplenishment = true
            }));
});
builder.Services.AddSingleton(repository);
builder.Services.AddSingleton<IPixPaymentGateway>(_ => new MercadoPagoServerGateway(repository,
    configuration.PaymentExpirationMinutes));
builder.Services.AddSingleton<OnlineLicensingService>();

var app = builder.Build();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    var loopbackAllowed = configuration.AllowHttpLoopback
        && context.Connection.RemoteIpAddress is { } address && System.Net.IPAddress.IsLoopback(address);
    if (!context.Request.IsHttps && !loopbackAllowed)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new OnlineErrorResponse(1, "TR-ACT-104",
            "Nao foi possivel validar esta instalacao. Codigo: TR-ACT-104."));
        return;
    }
    await next();
});

app.MapGet("/v1/health", (OnlineLicensingService service) => service.Readiness());
app.MapPost("/v1/activations/challenge", async (HttpContext context, OnlineActivationChallengeRequest request,
    OnlineLicensingService service, CancellationToken token) =>
    await Endpoint.Run(context, () => service.CreateActivationChallengeAsync(request, token)));
app.MapPost("/v1/activations/complete", async (HttpContext context, OnlineActivationProof request,
    OnlineLicensingService service, CancellationToken token) =>
    await Endpoint.Run(context, () => service.CompleteActivationAsync(request, token)));
app.MapPost("/v1/challenges", async (HttpContext context, OnlineChallengeRequest request,
    OnlineLicensingService service, CancellationToken token) =>
    await Endpoint.Run(context, () => service.CreateOperationChallengeAsync(request, token)));
app.MapPost("/v1/sessions", async (HttpContext context, OnlineSessionProof request,
    OnlineLicensingService service, CancellationToken token) =>
    await Endpoint.Run(context, () => service.CompleteSessionAsync(request, token)));
app.MapPost("/v1/orders", async (HttpContext context, OnlinePaymentCreateProof request,
    OnlineLicensingService service, CancellationToken token) =>
    await Endpoint.Run(context, () => service.CreateOrderAsync(request, token)));
app.MapPost("/v1/orders/status", async (HttpContext context, OnlinePaymentReadProof request,
    OnlineLicensingService service, CancellationToken token) =>
    await Endpoint.Run(context, () => service.ReadOrderAsync(request, token)));

await app.RunAsync();
return 0;

static string ReadSecret()
{
    if (Console.IsInputRedirected) return (Console.ReadLine() ?? "").Trim();
    var value = new System.Text.StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) break;
        if (key.Key == ConsoleKey.Backspace && value.Length > 0) { value.Length--; Console.Write("\b \b"); continue; }
        if (!char.IsControl(key.KeyChar)) { value.Append(key.KeyChar); Console.Write('*'); }
    }
    return value.ToString().Trim();
}

static class Endpoint
{
    public static async Task<IResult> Run<T>(HttpContext context, Func<Task<T>> operation)
    {
        try { return Results.Json(await operation()); }
        catch (OnlineServerException ex)
        {
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("TurboRama.Online.Denied");
            logger.LogWarning("Operacao recusada. Motivo={Reason}; IP={RemoteIp}",
                ex.InternalReason, context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            return Results.Json(new OnlineErrorResponse(1, "TR-ACT-104",
                "Nao foi possivel validar esta instalacao. Codigo: TR-ACT-104."), statusCode: ex.StatusCode);
        }
        catch (Exception ex) when (ex is SecurityException or CryptographicException
            or InvalidOperationException or ArgumentException or JsonException)
        {
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("TurboRama.Online.InvalidRequest");
            logger.LogWarning("Solicitacao invalida. Tipo={ExceptionType}; IP={RemoteIp}",
                ex.GetType().Name, context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            return Results.Json(new OnlineErrorResponse(1, "TR-ACT-104",
                "Nao foi possivel validar esta instalacao. Codigo: TR-ACT-104."), statusCode: 403);
        }
    }
}
