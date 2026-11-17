using Azure.Identity;
using Azure.Storage.Blobs;
using ReceiptSplitter.Api.Features.Confirm;
using ReceiptSplitter.Api.Features.CreateBill;
using ReceiptSplitter.Api.Features.DeleteBill;
using ReceiptSplitter.Api.Features.GetBill;
using ReceiptSplitter.Api.Features.Lock;
using ReceiptSplitter.Api.Features.PatchBill;
using ReceiptSplitter.Api.Features.Pin;
using ReceiptSplitter.Api.Features.Receipts;
using ReceiptSplitter.Api.Features.Settings;
using ReceiptSplitter.Api.Features.Scans;
using ReceiptSplitter.Api.Features.Share;
using ReceiptSplitter.Api.Features.Undo;
using ReceiptSplitter.Api.Infrastructure.Http;
using ReceiptSplitter.Api.Infrastructure.RateLimits;
using ReceiptSplitter.Api.Infrastructure.Scanning;
using ReceiptSplitter.Api.Infrastructure.Security;
using ReceiptSplitter.Api.Infrastructure.Storage;
using ReceiptSplitter.Api.Shared;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = HttpHardening.MaxScanBodyBytes);
builder.Services.AddHttpHardening(builder.Configuration);
builder.Services.AddApiRateLimits(builder.Configuration);
builder.Services.Configure<BillLimits>(builder.Configuration.GetSection("Bills"));
builder.Services.Configure<NotFoundOptions>(builder.Configuration.GetSection("NotFound"));
builder.Services.Configure<PinOptions>(builder.Configuration.GetSection("Pin"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PrivacyHash>();
builder.Services.AddSingleton<NotFoundGuard>();
builder.Services.AddSingleton<PinGuard>();
builder.Services.AddScoped<BillAccess>();
AddStorage(builder);
AddScanning(builder);

var app = builder.Build();

app.UseHttpHardening();
app.UseRateLimiter();

// Used by the Container Apps health probe and the deploy smoke test.
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
CreateBill.Map(app);
GetBill.Map(app);
SetPin.Map(app);
UnlockPin.Map(app);
PatchBill.Map(app);
ScanReceipt.Map(app);

// Organizer only: X-Organizer-Key on every call.
AddReceipt.Map(app);
ReplaceReceipt.Map(app);
CheckReceipt.Map(app);
DeleteReceipt.Map(app);
UpdateSettings.Map(app);
ShareBill.Map(app);
LockBill.Map(app);
ConfirmPayment.Map(app);
DeclinePayment.Map(app);
UndoClaim.Map(app);
DeleteBill.Map(app);

app.Run();

// Production: Blob Storage through managed identity, never a key. Local: Azurite's well-known
// development connection string, or memory when nothing is configured. Production never falls back.
static void AddStorage(WebApplicationBuilder builder)
{
    var serviceUri = builder.Configuration["Storage:BlobServiceUri"];
    var connectionString = builder.Configuration["Storage:ConnectionString"];

    if (!string.IsNullOrEmpty(serviceUri))
        builder.Services.AddSingleton(new BlobServiceClient(new Uri(serviceUri), new DefaultAzureCredential()));
    else if (!string.IsNullOrEmpty(connectionString) && builder.Environment.IsDevelopment())
        builder.Services.AddSingleton(new BlobServiceClient(connectionString));
    else if (builder.Environment.IsProduction())
        throw new InvalidOperationException("Set Storage:BlobServiceUri. Production only uses managed identity.");
    else
    {
        builder.Services.AddSingleton<IBillStore, InMemoryBillStore>();
        builder.Services.AddSingleton<ILimitStore, InMemoryLimitStore>();
        return;
    }

    builder.Services.AddSingleton<IBillStore, BlobBillStore>();
    builder.Services.AddSingleton<ILimitStore, BlobLimitStore>();
}

// Production: Document Intelligence through managed identity. Local: the sample reader when asked
// for, so the scan flow works without Azure. With neither, scanning answers 503 and people type it in.
static void AddScanning(WebApplicationBuilder builder)
{
    builder.Services.Configure<ScanOptions>(builder.Configuration.GetSection("Scans"));
    builder.Services.AddSingleton<ScanCap>();
    var endpoint = builder.Configuration["DocumentIntelligence:Endpoint"];
    var useSample = builder.Configuration.GetValue<bool>("Scans:UseSample");

    if (!string.IsNullOrEmpty(endpoint))
        builder.Services.AddSingleton<IReceiptReader>(DocumentIntelligenceReader.For(new Uri(endpoint)));
    else if (builder.Environment.IsProduction())
        throw new InvalidOperationException("Set DocumentIntelligence:Endpoint. Production reads receipts with managed identity.");
    else if (useSample)
        builder.Services.AddSingleton<IReceiptReader, SampleReceiptReader>();
    else
        builder.Services.AddSingleton<IReceiptReader, NoReader>();
}

// Lets the test project start the app with WebApplicationFactory<Program>.
public partial class Program { }
