using System.Net.Sockets;
using Azure.Storage.Blobs;
using ReceiptSplitter.Api.Infrastructure.Security;
using ReceiptSplitter.Api.Infrastructure.Storage;
using ReceiptSplitter.Api.Shared;

namespace ReceiptSplitter.Api.Tests.Infrastructure.Storage;

/// <summary>
/// The same rules for every store: exact-id reads, create-once, and writes only at the read ETag.
/// The Blob store runs against Azurite on localhost:10000 only when it is up (for example
/// <c>npx azurite</c>), so CI needs no emulator.
/// </summary>
public class StoreContractTests
{
    private static readonly Lazy<bool> AzuriteUp = new(() =>
    {
        try
        {
            using var tcp = new TcpClient();
            return tcp.ConnectAsync("127.0.0.1", 10000).Wait(TimeSpan.FromMilliseconds(300));
        }
        catch (Exception e) when (e is SocketException or AggregateException)
        {
            return false;
        }
    });

    public static TheoryData<string> Stores()
    {
        var stores = new TheoryData<string> { "memory" };
        if (AzuriteUp.Value)
            stores.Add("blob");
        return stores;
    }

    private static (IBillStore Bills, ILimitStore Limits) Open(string kind)
    {
        if (kind == "memory")
            return (new InMemoryBillStore(), new InMemoryLimitStore());
        var service = new BlobServiceClient("UseDevelopmentStorage=true");
        return (new BlobBillStore(service), new BlobLimitStore(service, TimeProvider.System));
    }

    private static BillDocument NewBill() => new()
    {
        Id = SecureIds.NewSecret(),
        Version = 1,
        Title = "Test",
        Currency = new CurrencyInfo("PHP", 2),
        OrganizerKeyHash = OrganizerKey.Hash(SecureIds.NewSecret()),
    };

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task A_bill_reads_back_as_written(string kind)
    {
        var (bills, limits) = Open(kind);
        var bill = NewBill();
        bill.People.Add(new PersonDoc { Id = "p1", Name = "Ana" });

        Assert.True(await bills.CreateAsync(bill, default));
        var stored = await bills.GetAsync(bill.Id, default);

        Assert.NotNull(stored);
        Assert.Equal("Ana", stored.Bill.People[0].Name);
        Assert.Equal(bill.OrganizerKeyHash, stored.Bill.OrganizerKeyHash);
        Assert.Null(await bills.GetAsync(SecureIds.NewSecret(), default));
    }

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task Create_refuses_an_id_that_exists(string kind)
    {
        var (bills, limits) = Open(kind);
        var bill = NewBill();
        Assert.True(await bills.CreateAsync(bill, default));
        bill.Title = "Overwritten";

        Assert.False(await bills.CreateAsync(bill, default));
        Assert.Equal("Test", (await bills.GetAsync(bill.Id, default))!.Bill.Title);
    }

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task Replace_only_succeeds_at_the_ETag_it_read(string kind)
    {
        var (bills, limits) = Open(kind);
        var bill = NewBill();
        await bills.CreateAsync(bill, default);
        var first = (await bills.GetAsync(bill.Id, default))!;
        var second = (await bills.GetAsync(bill.Id, default))!;

        first.Bill.Title = "First";
        second.Bill.Title = "Second";

        Assert.True(await bills.ReplaceAsync(first.Bill, first.ETag, default));
        Assert.False(await bills.ReplaceAsync(second.Bill, second.ETag, default));
        Assert.Equal("First", (await bills.GetAsync(bill.Id, default))!.Bill.Title);
    }

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task Delete_removes_the_bill_and_replace_then_fails(string kind)
    {
        var (bills, limits) = Open(kind);
        var bill = NewBill();
        await bills.CreateAsync(bill, default);
        var stored = (await bills.GetAsync(bill.Id, default))!;

        await bills.DeleteAsync(bill.Id, default);
        await bills.DeleteAsync(bill.Id, default);

        Assert.Null(await bills.GetAsync(bill.Id, default));
        Assert.False(await bills.ReplaceAsync(stored.Bill, stored.ETag, default));
    }

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task Counters_count_and_the_salt_stays_put(string kind)
    {
        var (bills, limits) = Open(kind);
        var counter = $"{DateTime.UtcNow:yyyy-MM-dd}/test-{SecureIds.NewShort()}";

        Assert.Equal(0, await limits.GetCountAsync(counter, default));
        Assert.Equal(1, await limits.IncrementAsync(counter, default));
        Assert.Equal(2, await limits.IncrementAsync(counter, default));
        Assert.Equal(2, await limits.GetCountAsync(counter, default));

        var salt = await limits.GetSaltAsync(default);
        Assert.Equal(32, salt.Length);
        Assert.Equal(salt, await limits.GetSaltAsync(default));
    }
}
