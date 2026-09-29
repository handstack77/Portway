using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using System.Net;

namespace Portway.Core.Protocols;

public sealed class S3FileSystem : IRemoteFileSystem
{
    readonly AmazonS3Client client;
    readonly string bucket;
    readonly RequestPayer? payer;
    readonly S3StorageClass storageClass;
    public S3FileSystem(Site site)
    {
        bucket = site.Bucket;
        payer = site.S3RequesterPays ? RequestPayer.Requester : null;
        storageClass = S3StorageClass.FindValue(site.S3StorageClass);
        var config = new AmazonS3Config
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(site.Region),
            ForcePathStyle = site.S3PathStyle
        };
        if (site.Host != "s3.amazonaws.com")
        {
            config.ServiceURL = site.Host.StartsWith("http", StringComparison.Ordinal) ? site.Host : $"https://{site.Host}:{site.EffectivePort}";
            config.AuthenticationRegion = site.Region;
            config.RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED;
        }

        if (site.Proxy.Type != "none")
        {
            if (site.Proxy.Type != "http")
                throw new NotSupportedException("S3 프록시는 HTTP CONNECT를 사용하세요.");
            config.ProxyHost = site.Proxy.Host;
            config.ProxyPort = site.Proxy.Port;
            config.ProxyCredentials = new NetworkCredential(site.Proxy.Username, site.Proxy.Password);
        }

        client = string.IsNullOrEmpty(site.Username) ? new AmazonS3Client(config) : new AmazonS3Client(string.IsNullOrEmpty(site.SessionToken) ? new BasicAWSCredentials(site.Username, site.Password ?? "") : new SessionAWSCredentials(site.Username, site.Password ?? "", site.SessionToken), config);
    }

    public Capabilities Capabilities => new(false, false, false, false);

    static string Key(string p) => RemotePaths.Normalize(p).TrimStart('/');
    public async Task Connect(CancellationToken ct) => await client.ListObjectsV2Async(new ListObjectsV2Request { RequestPayer = payer, BucketName = bucket, MaxKeys = 1 }, ct);
    public async Task<Entry[]> List(string path, CancellationToken ct)
    {
        var prefix = Key(path);
        if (prefix.Length > 0)
            prefix += "/";
        var result = new List<Entry>();
        string? token = null;
        do
        {
            var r = await client.ListObjectsV2Async(new ListObjectsV2Request { RequestPayer = payer, BucketName = bucket, Prefix = prefix, Delimiter = "/", ContinuationToken = token }, ct);
            result.AddRange((r.CommonPrefixes ?? []).Select(p => new Entry(RemotePaths.Name(p), "/" + p.TrimEnd('/'), true, 0, DateTimeOffset.UnixEpoch)));
            result.AddRange((r.S3Objects ?? []).Where(o => o.Key != prefix).Select(o => new Entry(RemotePaths.Name(o.Key), "/" + o.Key, false, o.Size ?? 0, o.LastModified ?? DateTime.UnixEpoch)));
            token = r.IsTruncated == true ? r.NextContinuationToken : null;
        }
        while (token != null);
        return result.ToArray();
    }

    public async Task<Entry?> Stat(string path, CancellationToken ct)
    {
        if (Key(path).Length == 0)
            return new("", "/", true, 0, DateTimeOffset.UnixEpoch);
        try
        {
            var r = await client.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = bucket, Key = Key(path), RequestPayer = payer }, ct);
            return new(RemotePaths.Name(path), path, false, r.ContentLength, r.LastModified ?? DateTime.UnixEpoch);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            var r = await client.ListObjectsV2Async(new ListObjectsV2Request { RequestPayer = payer, BucketName = bucket, Prefix = Key(path).TrimEnd('/') + "/", MaxKeys = 1 }, ct);
            return r.KeyCount > 0 ? new(RemotePaths.Name(path), path, true, 0, DateTimeOffset.UnixEpoch) : null;
        }
    }

    public async Task CreateDirectory(string path, CancellationToken ct) => await client.PutObjectAsync(new PutObjectRequest { RequestPayer = payer, BucketName = bucket, Key = Key(path).TrimEnd('/') + "/", ContentBody = "" }, ct);
    public async Task Delete(string path, bool directory, CancellationToken ct) => await client.DeleteObjectAsync(new DeleteObjectRequest { BucketName = bucket, Key = Key(path) + (directory ? "/" : ""), RequestPayer = payer }, ct);
    public async Task Move(string source, string destination, CancellationToken ct)
    {
        await Copy(source, destination, ct);
        await TransferOperations.DeleteTree(this, source, ct);
    }

    public async Task Copy(string source, string destination, CancellationToken ct)
    {
        if (await Stat(destination, ct) != null)
            throw new IOException("대상 파일이 이미 있습니다.");
        var entry = await Stat(source, ct) ?? throw new FileNotFoundException(source);
        if (entry.IsDirectory)
        {
            await CreateDirectory(destination, ct);
            foreach (var child in await List(source, ct))
                await Copy(child.Path, RemotePaths.Join(destination, child.Name), ct);
        }
        else
        {
            if (entry.Size <= 5L * 1024 * 1024 * 1024)
                await client.CopyObjectAsync(new CopyObjectRequest { StorageClass = storageClass, RequestPayer = payer, SourceBucket = bucket, SourceKey = Key(source), DestinationBucket = bucket, DestinationKey = Key(destination) }, ct);
            else
            {
                var upload = await client.InitiateMultipartUploadAsync(new InitiateMultipartUploadRequest { StorageClass = storageClass, RequestPayer = payer, BucketName = bucket, Key = Key(destination) }, ct);
                try
                {
                    var parts = new List<PartETag>();
                    const long chunk = 512L * 1024 * 1024;
                    for (long start = 0; start < entry.Size; start += chunk)
                    {
                        var part = await client.CopyPartAsync(new CopyPartRequest { RequestPayer = payer, SourceBucket = bucket, SourceKey = Key(source), DestinationBucket = bucket, DestinationKey = Key(destination), UploadId = upload.UploadId, FirstByte = start, LastByte = Math.Min(entry.Size - 1, start + chunk - 1), PartNumber = parts.Count + 1 }, ct);
                        parts.Add(new PartETag(part.PartNumber ?? parts.Count + 1, part.ETag));
                    }

                    await client.CompleteMultipartUploadAsync(new CompleteMultipartUploadRequest { RequestPayer = payer, BucketName = bucket, Key = Key(destination), UploadId = upload.UploadId, PartETags = parts }, ct);
                }
                catch
                {
                    await client.AbortMultipartUploadAsync(bucket, Key(destination), upload.UploadId, CancellationToken.None);
                    throw;
                }
            }
        }
    }

    public async Task Download(string remote, string local, long offset, Action<TransferProgress> progress, CancellationToken ct)
    {
        using var r = await client.GetObjectAsync(new GetObjectRequest { RequestPayer = payer, BucketName = bucket, Key = Key(remote) }, ct);
        await using var output = File.Create(local);
        await StreamCopy.Copy(r.ResponseStream, output, 0, r.ContentLength, progress, ct);
    }

    public async Task Upload(string local, string remote, long offset, Action<TransferProgress> progress, CancellationToken ct)
    {
        using var transfer = new TransferUtility(client);
        var request = new TransferUtilityUploadRequest
        {
            RequestPayer = payer,
            BucketName = bucket,
            Key = Key(remote),
            FilePath = local,
            StorageClass = storageClass
        };
        request.UploadProgressEvent += (_, e) => progress(new(e.TransferredBytes, e.TotalBytes));
        await transfer.UploadAsync(request, ct);
    }

    public ValueTask DisposeAsync()
    {
        client.Dispose();
        return ValueTask.CompletedTask;
    }
}
