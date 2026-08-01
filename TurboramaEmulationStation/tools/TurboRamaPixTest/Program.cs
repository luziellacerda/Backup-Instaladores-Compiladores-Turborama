using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);

var options = builder.Configuration.GetSection("PixTest").Get<PixTestOptions>() ?? new PixTestOptions();
options.Normalize();

var dataRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, options.DataDirectory));
var paths = new PixTestPaths(dataRoot);
paths.EnsureDirectories();

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(paths);
builder.Services.AddSingleton<PixTransactionStore>();
builder.Services.AddSingleton<TestCreditCounter>();
builder.Services.AddHostedService<InboxProcessor>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new
{
    name = "TurboRama PIX Test",
    mode = "simulation-only",
    provider = "mock",
    now = DateTimeOffset.UtcNow
}));

app.MapGet("/api/options", (PixTestOptions pixOptions) => Results.Ok(new
{
    allowedMinutes = pixOptions.AllowedMinutes,
    priceCentsPerMinute = pixOptions.PriceCentsPerMinute,
    priceLabel = Money(pixOptions.PriceCentsPerMinute)
}));

app.MapPost("/api/charges", (CreateChargeRequest? request, PixTransactionStore store) =>
{
    if (request is null)
        return Results.BadRequest(new { error = "Pedido PIX inválido." });

    var result = store.Create(request.Minutes, request.SessionId);
    return result.Success
        ? Results.Created($"/api/charges/{result.Charge!.Id}", result.Charge)
        : Results.BadRequest(new { error = result.Error });
});

app.MapGet("/api/charges/{id}", (string id, PixTransactionStore store) =>
{
    var charge = store.Get(id);
    return charge is null ? Results.NotFound(new { error = "Cobrança não encontrada." }) : Results.Ok(charge);
});

app.MapPost("/api/charges/{id}/simulate-approval", (string id, PixTransactionStore store, PixTestPaths testPaths) =>
{
    var result = store.Approve(id);
    if (!result.Success)
        return Results.BadRequest(new { error = result.Error });

    if (result.NewlyApproved && result.Charge is not null)
    {
        var credit = new PixCreditEvent(
            result.Charge.Id,
            result.Charge.SessionId,
            result.Charge.Minutes,
            result.Charge.AmountCents,
            "mock",
            DateTimeOffset.UtcNow);

        testPaths.WriteCreditEventAtomically(credit);
    }

    return Results.Ok(new
    {
        charge = result.Charge,
        inboxFileCreated = result.NewlyApproved,
        message = result.NewlyApproved
            ? "Pagamento simulado aprovado. Crédito enviado à fila local."
            : "Cobrança já estava aprovada; nenhum crédito duplicado foi criado."
    });
});

app.MapGet("/api/charges", (PixTransactionStore store) => Results.Ok(store.GetAll()));

app.MapGet("/api/inbox", (PixTestPaths testPaths) => Results.Ok(testPaths.ReadInbox()));

app.MapGet("/api/counter", (TestCreditCounter counter) => Results.Ok(counter.Snapshot()));

app.MapPost("/api/counter/start", (TestCreditCounter counter) =>
{
    counter.Start();
    return Results.Ok(counter.Snapshot());
});

app.MapPost("/api/counter/pause", (TestCreditCounter counter) =>
{
    counter.Pause();
    return Results.Ok(counter.Snapshot());
});

app.MapPost("/api/counter/speed", (CounterSpeedRequest? request, TestCreditCounter counter) =>
{
    if (request is null || request.SecondsPerTick is < 1 or > 300)
        return Results.BadRequest(new { error = "A velocidade deve estar entre 1 e 300 segundos por segundo real." });

    counter.SetSpeed(request.SecondsPerTick);
    return Results.Ok(counter.Snapshot());
});

app.MapPost("/api/counter/reset", (TestCreditCounter counter) =>
{
    counter.Reset();
    return Results.Ok(counter.Snapshot());
});

app.MapGet("/api/logs", (TestCreditCounter counter) => Results.Ok(counter.GetLogs()));

app.MapFallbackToFile("index.html");

app.Logger.LogInformation("TurboRama PIX Test disponível em {Url}", options.ListenUrl);
app.Run(options.ListenUrl);

static string Money(long cents)
    => (cents / 100m).ToString("C", new System.Globalization.CultureInfo("pt-BR"));

sealed class PixTestOptions
{
    public string ListenUrl { get; set; } = "http://127.0.0.1:18888";
    public long PriceCentsPerMinute { get; set; } = 50;
    public List<int> AllowedMinutes { get; set; } = [15, 30, 45, 60, 120];
    public string DataDirectory { get; set; } = "runtime";

    public void Normalize()
    {
        AllowedMinutes = AllowedMinutes.Where(x => x > 0).Distinct().Order().ToList();
        if (AllowedMinutes.Count == 0)
            AllowedMinutes = [15, 30, 45, 60, 120];

        PriceCentsPerMinute = Math.Clamp(PriceCentsPerMinute, 1, 100_000);
        if (!Uri.TryCreate(ListenUrl, UriKind.Absolute, out _))
            ListenUrl = "http://127.0.0.1:18888";

        if (string.IsNullOrWhiteSpace(DataDirectory))
            DataDirectory = "runtime";
    }
}

sealed class PixTestPaths
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public PixTestPaths(string root)
    {
        Root = root;
        Inbox = Path.Combine(root, "pix", "inbox");
        TransactionsFile = Path.Combine(root, "transactions.json");
        ProcessedFile = Path.Combine(root, "pix", "processed.json");
    }

    public string Root { get; }
    public string Inbox { get; }
    public string TransactionsFile { get; }
    public string ProcessedFile { get; }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Inbox);
        Directory.CreateDirectory(Path.GetDirectoryName(ProcessedFile)!);
    }

    public void WriteCreditEventAtomically(PixCreditEvent credit)
    {
        var target = Path.Combine(Inbox, $"{credit.TransactionId}.json");
        if (File.Exists(target))
            return;

        var temporary = Path.Combine(Inbox, $"{credit.TransactionId}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(temporary, JsonSerializer.Serialize(credit, JsonOptions), Encoding.UTF8);

        try
        {
            File.Move(temporary, target, false);
        }
        catch (IOException) when (File.Exists(target))
        {
            File.Delete(temporary);
        }
    }

    public IReadOnlyList<object> ReadInbox()
    {
        if (!Directory.Exists(Inbox))
            return [];

        return Directory.EnumerateFiles(Inbox, "*.json")
            .OrderBy(Path.GetFileName)
            .Select(path => new
            {
                file = Path.GetFileName(path),
                content = File.ReadAllText(path, Encoding.UTF8)
            })
            .Cast<object>()
            .ToList();
    }
}

sealed class PixTransactionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _sync = new();
    private readonly Dictionary<string, PixCharge> _charges = new(StringComparer.OrdinalIgnoreCase);
    private readonly PixTestOptions _options;
    private readonly PixTestPaths _paths;

    public PixTransactionStore(PixTestOptions options, PixTestPaths paths)
    {
        _options = options;
        _paths = paths;
        Load();
    }

    public CreateChargeResult Create(int minutes, string? requestedSession)
    {
        if (!_options.AllowedMinutes.Contains(minutes))
            return new(false, null, "Escolha um pacote de 15, 30, 45, 60 ou 120 minutos.");

        var id = $"PIXTEST-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var amount = checked(minutes * _options.PriceCentsPerMinute);
        var sessionId = SanitizeSessionId(requestedSession);
        var testCode = CreateTestPixCode(id, minutes, amount, sessionId);
        var charge = new PixCharge(id, sessionId, minutes, amount, "AWAITING_PAYMENT", now, now.AddMinutes(15), testCode, null);

        lock (_sync)
        {
            _charges[id] = charge;
            SaveUnlocked();
        }

        return new(true, charge, null);
    }

    public PixCharge? Get(string id)
    {
        lock (_sync)
        {
            ExpirePendingUnlocked();
            return _charges.TryGetValue(id, out var charge) ? charge : null;
        }
    }

    public IReadOnlyList<PixCharge> GetAll()
    {
        lock (_sync)
        {
            ExpirePendingUnlocked();
            return _charges.Values.OrderByDescending(x => x.CreatedAt).ToList();
        }
    }

    public ApprovalResult Approve(string id)
    {
        lock (_sync)
        {
            ExpirePendingUnlocked();
            if (!_charges.TryGetValue(id, out var charge))
                return new(false, false, null, "Cobrança não encontrada.");

            if (charge.Status == "APPROVED")
                return new(true, false, charge, null);

            if (charge.Status != "AWAITING_PAYMENT")
                return new(false, false, charge, "A cobrança está expirada ou não pode mais ser aprovada.");

            charge = charge with { Status = "APPROVED", ApprovedAt = DateTimeOffset.UtcNow };
            _charges[id] = charge;
            SaveUnlocked();
            return new(true, true, charge, null);
        }
    }

    private void Load()
    {
        if (!File.Exists(_paths.TransactionsFile))
            return;

        try
        {
            var saved = JsonSerializer.Deserialize<List<PixCharge>>(File.ReadAllText(_paths.TransactionsFile, Encoding.UTF8)) ?? [];
            foreach (var charge in saved)
                _charges[charge.Id] = charge;
        }
        catch (JsonException)
        {
            // O protótipo nunca descarta o arquivo corrompido; apenas inicia vazio.
        }
    }

    private void ExpirePendingUnlocked()
    {
        var now = DateTimeOffset.UtcNow;
        var changed = false;
        foreach (var pair in _charges.ToArray())
        {
            if (pair.Value.Status == "AWAITING_PAYMENT" && pair.Value.ExpiresAt <= now)
            {
                _charges[pair.Key] = pair.Value with { Status = "EXPIRED" };
                changed = true;
            }
        }

        if (changed)
            SaveUnlocked();
    }

    private void SaveUnlocked()
    {
        var temporary = _paths.TransactionsFile + ".tmp";
        var data = JsonSerializer.Serialize(_charges.Values.OrderBy(x => x.CreatedAt).ToList(), JsonOptions);
        File.WriteAllText(temporary, data, Encoding.UTF8);
        File.Move(temporary, _paths.TransactionsFile, true);
    }

    private static string SanitizeSessionId(string? value)
    {
        var cleaned = Regex.Replace(value ?? "visitante", "[^A-Za-z0-9_-]", "");
        return string.IsNullOrWhiteSpace(cleaned) ? "visitante" : cleaned[..Math.Min(cleaned.Length, 64)];
    }

    private static string CreateTestPixCode(string id, int minutes, long cents, string sessionId)
    {
        var source = $"TurboRama|TESTE|{id}|{minutes}|{cents}|{sessionId}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..20];
        return $"TURBORAMA-PIX-TESTE|{id}|{minutes}MIN|{cents}CENT|{hash}";
    }
}

sealed class TestCreditCounter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _sync = new();
    private readonly PixTestPaths _paths;
    private readonly HashSet<string> _processedIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<CounterLogEntry> _logs = new();
    private long _remainingSeconds;
    private int _secondsPerTick = 1;
    private bool _active;
    private bool _warned15;
    private bool _warned5;

    public TestCreditCounter(PixTestPaths paths)
    {
        _paths = paths;
        LoadProcessedIds();
        AddLog("SIMULAÇÃO pronta. Nenhum arquivo do CreditManager real é usado.");
    }

    public void Start()
    {
        lock (_sync)
        {
            _active = _remainingSeconds > 0;
            AddLog(_active ? "Contador de teste iniciado." : "Não há crédito para iniciar o contador de teste.");
        }
    }

    public void Pause()
    {
        lock (_sync)
        {
            _active = false;
            AddLog("Contador de teste pausado.");
        }
    }

    public void SetSpeed(int secondsPerTick)
    {
        lock (_sync)
        {
            _secondsPerTick = secondsPerTick;
            AddLog($"Velocidade de teste: {secondsPerTick} segundo(s) virtuais por segundo real.");
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _remainingSeconds = 0;
            _active = false;
            _warned15 = false;
            _warned5 = false;
            AddLog("Contador de teste zerado. A fila PIX permanece preservada.");
        }
    }

    public void ImportInbox()
    {
        if (!Directory.Exists(_paths.Inbox))
            return;

        foreach (var file in Directory.EnumerateFiles(_paths.Inbox, "*.json").OrderBy(x => x))
        {
            PixCreditEvent? credit;
            try
            {
                credit = JsonSerializer.Deserialize<PixCreditEvent>(File.ReadAllText(file, Encoding.UTF8));
            }
            catch (JsonException)
            {
                continue;
            }

            if (credit is null || string.IsNullOrWhiteSpace(credit.TransactionId) || credit.Minutes <= 0)
                continue;

            lock (_sync)
            {
                if (_processedIds.Contains(credit.TransactionId))
                    continue;

                _processedIds.Add(credit.TransactionId);
                _remainingSeconds = Math.Min(_remainingSeconds + (long)credit.Minutes * 60, 7L * 24 * 3600);
                _active = true;

                if (_remainingSeconds > 15 * 60)
                    _warned15 = false;
                if (_remainingSeconds > 5 * 60)
                    _warned5 = false;

                AddLog($"PIX TESTE aprovado: +{credit.Minutes} min. Transação {credit.TransactionId}.");
                SaveProcessedIdsUnlocked();
            }
        }
    }

    public void Tick()
    {
        lock (_sync)
        {
            if (!_active || _remainingSeconds <= 0)
                return;

            var before = _remainingSeconds;
            _remainingSeconds = Math.Max(0, _remainingSeconds - _secondsPerTick);

            if (!_warned15 && before > 15 * 60 && _remainingSeconds <= 15 * 60)
            {
                _warned15 = true;
                AddLog("AVISO: restam 15 minutos. Cliente pode adicionar PIX.");
            }

            if (!_warned5 && before > 5 * 60 && _remainingSeconds <= 5 * 60)
            {
                _warned5 = true;
                AddLog("AVISO: restam 5 minutos. Cliente pode adicionar PIX.");
            }

            if (_remainingSeconds == 0)
            {
                _active = false;
                AddLog("Saldo de teste encerrado.");
            }
        }
    }

    public object Snapshot()
    {
        lock (_sync)
        {
            return new
            {
                active = _active,
                remainingSeconds = _remainingSeconds,
                remainingLabel = FormatDuration(_remainingSeconds),
                secondsPerTick = _secondsPerTick,
                warnings = new[] { "15 minutos", "5 minutos" }
            };
        }
    }

    public IReadOnlyList<CounterLogEntry> GetLogs()
    {
        lock (_sync)
            return _logs.Reverse().ToList();
    }

    private void LoadProcessedIds()
    {
        if (!File.Exists(_paths.ProcessedFile))
            return;

        try
        {
            var ids = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_paths.ProcessedFile, Encoding.UTF8)) ?? [];
            foreach (var id in ids)
                _processedIds.Add(id);
        }
        catch (JsonException)
        {
            // Mantém a fila intacta para investigação manual.
        }
    }

    private void SaveProcessedIdsUnlocked()
    {
        var temporary = _paths.ProcessedFile + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(_processedIds.Order().ToList(), JsonOptions), Encoding.UTF8);
        File.Move(temporary, _paths.ProcessedFile, true);
    }

    private void AddLog(string message)
    {
        _logs.Enqueue(new CounterLogEntry(DateTimeOffset.Now, message));
        while (_logs.Count > 80)
            _logs.Dequeue();
    }

    private static string FormatDuration(long seconds)
        => TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss");
}

sealed class InboxProcessor(TestCreditCounter counter, ILogger<InboxProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                counter.ImportInbox();
                counter.Tick();
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Falha no processamento de teste da fila PIX.");
            }
        }
    }
}

sealed record CreateChargeRequest(int Minutes, string? SessionId);
sealed record CounterSpeedRequest(int SecondsPerTick);
sealed record CreateChargeResult(bool Success, PixCharge? Charge, string? Error);
sealed record ApprovalResult(bool Success, bool NewlyApproved, PixCharge? Charge, string? Error);
sealed record PixCharge(
    string Id,
    string SessionId,
    int Minutes,
    long AmountCents,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string TestPixCode,
    DateTimeOffset? ApprovedAt);
sealed record PixCreditEvent(
    string TransactionId,
    string SessionId,
    int Minutes,
    long AmountCents,
    string Provider,
    DateTimeOffset ApprovedAt);
sealed record CounterLogEntry(DateTimeOffset At, string Message);
