using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EvMigration.Core.Models;
using EvMigration.Core.Serialization;

namespace EvMigration.Core.Mock;

public sealed class MockEvDataGenerator
{
    public async Task<MockEvGenerationResult> GenerateAsync(
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        var outputPath = Path.GetFullPath(outputDirectory);
        var blobPath = Path.Combine(outputPath, "blobs");
        Directory.CreateDirectory(blobPath);

        var parts = CreateParts();
        foreach (var part in parts)
        {
            var filePath = Path.Combine(blobPath, $"{part.PartId}.bin");
            await File.WriteAllBytesAsync(filePath, part.Data, cancellationToken);
        }

        var sisParts = parts
            .Select(part => new EvSisPart
            {
                PartId = part.PartId,
                Sha256 = ComputeSha256(part.Data),
                SizeBytes = part.Data.LongLength,
                DataRef = $"blobs/{part.PartId}.bin"
            })
            .ToArray();

        var partSizes = sisParts.ToDictionary(part => part.PartId, part => part.SizeBytes);
        var dataSet = new EvDataSet
        {
            Archives = CreateArchives(),
            Items = CreateItems(partSizes),
            SisParts = sisParts
        };

        var catalogPath = Path.Combine(outputPath, "ev-data.json");
        var temporaryCatalogPath = $"{catalogPath}.tmp";

        await using (var stream = File.Create(temporaryCatalogPath))
        {
            await JsonSerializer.SerializeAsync(stream, dataSet, EvJson.Options, cancellationToken);
        }

        File.Move(temporaryCatalogPath, catalogPath, overwrite: true);

        return new MockEvGenerationResult(
            catalogPath,
            dataSet.Archives.Count,
            dataSet.Items.Count,
            dataSet.SisParts.Count,
            dataSet.SisParts.Sum(part => part.SizeBytes));
    }

    private static IReadOnlyList<EvArchive> CreateArchives() =>
    [
        new()
        {
            ArchiveId = "A1",
            Type = EvArchiveType.Mailbox,
            OwnerUpn = "ayse@contoso.com",
            LegalHold = false
        },
        new()
        {
            ArchiveId = "A2",
            Type = EvArchiveType.Mailbox,
            OwnerUpn = "mehmet@contoso.com",
            LegalHold = true
        },
        new()
        {
            ArchiveId = "A3",
            Type = EvArchiveType.Mailbox,
            OwnerUpn = "former.employee@contoso.com",
            LegalHold = false
        },
        new()
        {
            ArchiveId = "J1",
            Type = EvArchiveType.Journal,
            OwnerUpn = null,
            LegalHold = true
        },
        new()
        {
            ArchiveId = "F1",
            Type = EvArchiveType.Fsa,
            OwnerUpn = null,
            LegalHold = false
        }
    ];

    private static IReadOnlyList<EvItem> CreateItems(IReadOnlyDictionary<string, long> partSizes) =>
    [
        CreateItem(
            "I100",
            "A1",
            "Inbox/Projects",
            "Q3 raporu",
            "2021-03-04T09:12:00Z",
            "bob@partner.com",
            ["ayse@contoso.com"],
            ["P1", "P7", "P9"],
            "7y",
            partSizes),
        CreateItem(
            "I101",
            "A1",
            "Sent Items",
            "RE: Q3 raporu",
            "2021-03-05T10:30:00Z",
            "ayse@contoso.com",
            ["bob@partner.com"],
            ["P2", "P7", "P9"],
            "7y",
            partSizes),
        CreateItem(
            "I200",
            "A2",
            "Inbox/Contracts",
            "Tedarik sözleşmesi",
            "2020-11-18T14:45:00Z",
            "legal@vendor.com",
            ["mehmet@contoso.com"],
            ["P3", "P8", "P9"],
            "10y",
            partSizes),
        CreateItem(
            "I300",
            "A3",
            "Inbox/Handover",
            "Devir notları",
            "2019-06-20T08:00:00Z",
            "former.employee@contoso.com",
            ["manager@contoso.com"],
            ["P4", "P7"],
            "7y",
            partSizes),
        CreateItem(
            "J100",
            "J1",
            "Journal/2021/03",
            "Q3 raporu",
            "2021-03-04T09:12:01Z",
            "bob@partner.com",
            ["ayse@contoso.com"],
            ["P5", "P1", "P7", "P9"],
            "10y",
            partSizes),
        CreateItem(
            "J101",
            "J1",
            "Journal/2020/11",
            "Tedarik sözleşmesi",
            "2020-11-18T14:45:01Z",
            "legal@vendor.com",
            ["mehmet@contoso.com", "audit@contoso.com"],
            ["P6", "P3", "P8", "P9"],
            "10y",
            partSizes),
        CreateItem(
            "F100",
            "F1",
            "Finance/Contracts",
            "signed-contract.pdf",
            "2020-11-18T14:40:00Z",
            "",
            [],
            ["P8"],
            "10y",
            partSizes,
            @"\\fileserver\finance\Contracts\signed-contract.pdf",
            "2020-11-18T14:35:00Z")
    ];

    private static EvItem CreateItem(
        string itemId,
        string archiveId,
        string folderPath,
        string subject,
        string sentDate,
        string from,
        IReadOnlyList<string> to,
        IReadOnlyList<string> contentParts,
        string retentionCategory,
        IReadOnlyDictionary<string, long> partSizes,
        string? filePath = null,
        string? fileModifiedAt = null) =>
        new()
        {
            ItemId = itemId,
            ArchiveId = archiveId,
            FolderPath = folderPath,
            Subject = subject,
            SentDate = DateTimeOffset.Parse(sentDate, System.Globalization.CultureInfo.InvariantCulture),
            From = from,
            To = to,
            FilePath = filePath,
            FileModifiedAt = fileModifiedAt is null
                ? null
                : DateTimeOffset.Parse(fileModifiedAt, System.Globalization.CultureInfo.InvariantCulture),
            ContentParts = contentParts,
            RetentionCategory = retentionCategory,
            SizeBytes = contentParts.Sum(partId => partSizes[partId])
        };

    private static IReadOnlyList<PartDefinition> CreateParts() =>
    [
        CreatePart("P1", "Q3 raporu mesaj gövdesi\nGelir ve gider özeti ektedir.\n"),
        CreatePart("P2", "Q3 raporu yanıtı\nRaporu aldım, teşekkürler.\n"),
        CreatePart("P3", "Tedarik sözleşmesi mesaj gövdesi\nİmzalı nüsha ektedir.\n"),
        CreatePart("P4", "Devir notları\nAçık işler ve sorumlular ekte listelenmiştir.\n"),
        CreatePart("P5", "Journal envelope\nTo: ayse@contoso.com\n"),
        CreatePart("P6", "Journal envelope\nTo: mehmet@contoso.com; Bcc: audit@contoso.com\n"),
        CreatePart("P7", "Q3-Report.csv\nmonth,revenue,cost\nJuly,120000,80000\n"),
        CreatePart("P8", "signed-contract.pdf\n%PDF-mock-contract-content\n"),
        CreatePart("P9", "--\nContoso kurumsal imza\n")
    ];

    private static PartDefinition CreatePart(string partId, string content) =>
        new(partId, Encoding.UTF8.GetBytes(content));

    private static string ComputeSha256(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private sealed record PartDefinition(string PartId, byte[] Data);
}

public sealed record MockEvGenerationResult(
    string CatalogPath,
    int ArchiveCount,
    int ItemCount,
    int UniquePartCount,
    long UniquePartBytes);
