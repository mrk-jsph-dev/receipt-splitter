using System.Net;
using System.Text;
using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.Core.Pipeline;
using ReceiptSplitter.Api.Infrastructure.Scanning;

namespace ReceiptSplitter.Api.Tests.Infrastructure.Scanning;

/// <summary>
/// The real reader against a fake Document Intelligence. Microsoft keeps the photo and its result
/// for 24 hours unless asked to delete them, so the reader must ask as soon as it has read them.
/// </summary>
public class DocumentIntelligenceReaderTests
{
    private const string ResultId = "3b5e0c6d-0000-4000-8000-000000000001";
    private static readonly Uri Endpoint = new("https://docintel.test/");

    private sealed class FakeService(HttpStatusCode deleteStatus = HttpStatusCode.NoContent) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path)> Calls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Calls.Add((request.Method, path));
            var resultUri = $"{Endpoint}documentintelligence/documentModels/prebuilt-receipt/analyzeResults/{ResultId}?api-version=2024-11-30";

            HttpResponseMessage response;
            if (request.Method == HttpMethod.Post)
            {
                response = new HttpResponseMessage(HttpStatusCode.Accepted);
                response.Headers.Add("Operation-Location", resultUri);
                response.Headers.Add("Retry-After", "0");
            }
            else if (request.Method == HttpMethod.Get)
            {
                response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Succeeded, Encoding.UTF8, "application/json"),
                };
            }
            else
            {
                response = new HttpResponseMessage(deleteStatus);
            }
            return Task.FromResult(response);
        }
    }

    private const string Succeeded = """
        {
          "status": "succeeded",
          "createdDateTime": "2026-11-17T00:00:00Z",
          "lastUpdatedDateTime": "2026-11-17T00:00:01Z",
          "analyzeResult": {
            "apiVersion": "2024-11-30",
            "modelId": "prebuilt-receipt",
            "stringIndexType": "textElements",
            "content": "",
            "pages": [],
            "documents": [{
              "docType": "receipt",
              "confidence": 0.99,
              "spans": [],
              "fields": { "MerchantName": { "type": "string", "valueString": "Ramen Nagi", "confidence": 0.99 } }
            }]
          }
        }
        """;

    private static DocumentIntelligenceReader Reader(FakeService service)
    {
        var options = new DocumentIntelligenceClientOptions { Transport = new HttpClientTransport(new HttpClient(service)) };
        options.Retry.MaxRetries = 0;
        return new DocumentIntelligenceReader(new DocumentIntelligenceClient(Endpoint, new AzureKeyCredential("test"), options));
    }

    [Fact]
    public async Task Deletes_the_photo_and_result_from_Document_Intelligence_after_reading()
    {
        var service = new FakeService();

        var receipt = await Reader(service).ReadAsync(BinaryData.FromBytes([0xFF, 0xD8]), CancellationToken.None);

        Assert.Equal("Ramen Nagi", receipt.Merchant);
        Assert.Equal(HttpMethod.Delete, service.Calls[^1].Method);
        Assert.EndsWith($"/analyzeResults/{ResultId}", service.Calls[^1].Path);
    }

    [Fact]
    public async Task A_failed_delete_still_returns_the_scan()
    {
        var service = new FakeService(HttpStatusCode.InternalServerError);

        var receipt = await Reader(service).ReadAsync(BinaryData.FromBytes([0xFF, 0xD8]), CancellationToken.None);

        Assert.Equal("Ramen Nagi", receipt.Merchant);
        Assert.Contains(service.Calls, c => c.Method == HttpMethod.Delete);
    }
}
